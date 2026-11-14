// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Solin.Companion
{
    // Talks to server/companion_api.py on the loopback. Events arrive on a background task; callers marshal to the UI thread.
    internal sealed class BackendClient : IDisposable
    {
        internal const int DefaultPort = 47831;
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        private readonly string baseUrl;
        private readonly string sessionId;
        private CancellationTokenSource listening;

        internal event Action<string, double> MoodChanged;
        // visible, mood, and an audio path when the backend already spoke the line.
        internal event Action<string, string, string> Replied;
        internal event Action<bool> Speaking;
        internal event Action<bool> Connected;
        internal event Action<string, string> AgentChanged;
        internal event Action<JsonElement> SettingsChanged;

        internal BackendClient(int port = DefaultPort, string sessionId = "solin")
        {
            baseUrl = "http://127.0.0.1:" + port;
            this.sessionId = sessionId;
        }

        internal async Task<bool> Healthy()
        {
            try
            {
                using (CancellationTokenSource quick = new CancellationTokenSource(TimeSpan.FromSeconds(1.5)))
                using (HttpResponseMessage response = await http.GetAsync(baseUrl + "/health", quick.Token))
                    return response.IsSuccessStatusCode;
            }
            catch (Exception) { return false; }
        }

        internal async Task<(string mood, double trust)> State()
        {
            using (JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "/state?session_id=" + Uri.EscapeDataString(sessionId))))
                return (doc.RootElement.GetProperty("mood").GetString(), doc.RootElement.GetProperty("trust").GetDouble());
        }

        internal readonly struct Reply
        {
            internal readonly string Visible, Mood, Audio, Internal;
            internal Reply(string visible, string mood, string audio, string internalThought) { Visible = visible; Mood = mood; Audio = audio; Internal = internalThought; }
        }

        // speak null leaves it to the backend's configured default.
        internal async Task<Reply> Chat(string text, bool? speak = null)
        {
            string json = speak.HasValue
                ? await Post("/chat", new { session_id = sessionId, text, speak = speak.Value })
                : await Post("/chat", new { session_id = sessionId, text });
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                string audio = root.TryGetProperty("audio", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                string inner = root.TryGetProperty("internal", out JsonElement i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                return new Reply(root.GetProperty("visible").GetString(), root.GetProperty("mood").GetString(), audio, inner);
            }
        }

        internal async Task<List<(string user, string visible, string internalThought)>> History(int limit = 30)
        {
            List<(string, string, string)> out_ = new List<(string, string, string)>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "/history?limit=" + limit)))
                    foreach (JsonElement e in doc.RootElement.GetProperty("history").EnumerateArray())
                        out_.Add((e.GetProperty("user").GetString(), e.GetProperty("visible").GetString(), e.TryGetProperty("internal", out JsonElement i) ? i.GetString() : null));
            }
            catch (Exception) { }
            return out_;
        }

        // The settings document as the backend holds it; callers read what they need from the JSON.
        internal async Task<JsonDocument> Settings()
        {
            try { return JsonDocument.Parse(await http.GetStringAsync(baseUrl + "/settings")); }
            catch (Exception) { return null; }
        }

        internal async Task<(JsonDocument doc, string error)> SaveSettings(object body)
        {
            using (StringContent content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await http.PostAsync(baseUrl + "/settings", content))
            {
                string json = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(json);
                string error = doc.RootElement.TryGetProperty("error", out JsonElement e) ? e.GetString() : null;
                return (doc, error);
            }
        }

        internal async Task<string> BrainName()
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "/health")))
                    return doc.RootElement.GetProperty("brain").GetString();
            }
            catch (Exception) { return null; }
        }

        internal async Task Event(string kind)
        {
            try { await Post("/event", new { session_id = sessionId, kind }); }
            catch (Exception) { }
        }

        private async Task<string> Post(string path, object body)
        {
            using (StringContent content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await http.PostAsync(baseUrl + path, content))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        // Keeps a server-sent events stream open, reconnecting after a short wait when it drops.
        internal void Listen()
        {
            if (listening != null) return;
            listening = new CancellationTokenSource();
            CancellationToken token = listening.Token;
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/events?session_id=" + Uri.EscapeDataString(sessionId)))
                        using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                        using (Stream stream = await response.Content.ReadAsStreamAsync(token))
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            Connected?.Invoke(true);
                            string kind = null;
                            StringBuilder data = new StringBuilder();
                            string line;
                            while ((line = await reader.ReadLineAsync(token)) != null)
                            {
                                if (line.StartsWith("event:")) kind = line.Substring(6).Trim();
                                else if (line.StartsWith("data:")) data.Append(line.Substring(5).Trim());
                                else if (line.Length == 0) { Dispatch(kind, data.ToString()); kind = null; data.Clear(); }
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                    Connected?.Invoke(false);
                    try { await Task.Delay(TimeSpan.FromSeconds(2), token); } catch (OperationCanceledException) { }
                }
            }, token);
        }

        private void Dispatch(string kind, string json)
        {
            if (kind == null || json.Length == 0) return;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    switch (kind)
                    {
                        case "mood": MoodChanged?.Invoke(root.GetProperty("mood").GetString(), root.GetProperty("trust").GetDouble()); break;
                        case "reply":
                            Replied?.Invoke(root.GetProperty("visible").GetString(),
                                root.TryGetProperty("mood", out JsonElement m) ? m.GetString() : null,
                                root.TryGetProperty("audio", out JsonElement au) && au.ValueKind == JsonValueKind.String ? au.GetString() : null);
                            break;
                        case "speaking": Speaking?.Invoke(root.GetProperty("on").GetBoolean()); break;
                        case "settings": SettingsChanged?.Invoke(root.GetProperty("companion").Clone()); break;
                        case "agent": AgentChanged?.Invoke(root.GetProperty("state").GetString(), root.TryGetProperty("detail", out JsonElement d) ? d.GetString() : ""); break;
                    }
                }
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            listening?.Cancel();
            listening?.Dispose();
            listening = null;
            http.Dispose();
        }
    }
}
