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
        // Sent with the requests whose answers this client shows itself, so the same reply coming back on the event
        // stream is not shown a second time. Replies started anywhere else still come through.
        private readonly string origin = Guid.NewGuid().ToString("N");
        private CancellationTokenSource listening;

        internal event Action<string, double> MoodChanged;
        // visible, mood, an audio path when the backend already spoke the line, and a kind: null, "greeting" or "reminder".
        internal event Action<string, string, string, string> Replied;
        internal event Action<bool> Speaking;
        internal event Action<bool> Connected;
        internal event Action<string, string> AgentChanged;
        // The whole agent event: state, detail, project, and on done a report {actions, files[], seconds}.
        internal event Action<JsonElement> AgentEvent;
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
            // What she wants to do: (kind, what) in order, her hands ("open", "click", "run") or her antics ("zap", "sit",
            // "possess", "point", "terminal", "hop", "emote"). Empty when nothing.
            internal readonly IReadOnlyList<(string kind, string what)> Actions;
            internal Reply(string visible, string mood, string audio, string internalThought, IReadOnlyList<(string, string)> actions = null)
            {
                Visible = visible; Mood = mood; Audio = audio; Internal = internalThought;
                Actions = actions ?? Array.Empty<(string, string)>();
            }
        }

        private static List<(string, string)> ActionsOf(JsonElement root)
        {
            List<(string, string)> list = new List<(string, string)>();
            if (root.TryGetProperty("actions", out JsonElement acts) && acts.ValueKind == JsonValueKind.Array)
                foreach (JsonElement a in acts.EnumerateArray())
                    if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("kind", out JsonElement k) && a.TryGetProperty("what", out JsonElement w)
                        && k.ValueKind == JsonValueKind.String && w.ValueKind == JsonValueKind.String)
                        list.Add((k.GetString(), w.GetString()));
            return list;
        }

        private static object ShotsBody(List<ScreenShot.Shot> shots)
        {
            return shots?.ConvertAll(s => (object)new { path = s.Path, w = s.W, h = s.H });
        }

        // A glance at your screens (one picture each, the one with your cursor first): maybe a short line about what you're
        // doing (Visible is "" when she keeps quiet) and now and then one thing she does about it; null when neither. hands:
        // she may also open, click or run. The pictures are the caller's to delete.
        internal async Task<Reply?> Remark(List<ScreenShot.Shot> shots, bool hands)
        {
            using (JsonDocument doc = JsonDocument.Parse(await Post("/remark", new { session_id = sessionId, shots = ShotsBody(shots), hands })))
            {
                JsonElement root = doc.RootElement;
                List<(string, string)> actions = ActionsOf(root);
                bool said = root.TryGetProperty("visible", out JsonElement v) && v.ValueKind == JsonValueKind.String;
                if (!said && actions.Count == 0) return null;
                string audio = root.TryGetProperty("audio", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                return new Reply(said ? v.GetString() : "", root.TryGetProperty("mood", out JsonElement m) && m.ValueKind == JsonValueKind.String ? m.GetString() : "neutral", audio, null, actions);
            }
        }

        // speak null leaves it to the backend's configured default.
        // screens: how many screens the image shows side by side. hands: she may ask to open, click or run things; shots:
        // one picture per screen for clicking, the screen with the mouse first (sent instead of image).
        // look: they asked her to look (otherwise any picture is just so she knows what they are doing).
        internal async Task<Reply> Chat(string text, bool? speak = null, string image = null, int screens = 1, bool hands = false, List<ScreenShot.Shot> shots = null, bool look = false)
        {
            object body = ShotsBody(shots);
            string json = speak.HasValue
                ? await Post("/chat", new { session_id = sessionId, origin, text, speak = speak.Value, image, screens, hands, shots = body, look })
                : await Post("/chat", new { session_id = sessionId, origin, text, image, screens, hands, shots = body, look });
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                string audio = root.TryGetProperty("audio", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                string inner = root.TryGetProperty("internal", out JsonElement i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
                return new Reply(root.GetProperty("visible").GetString(), root.GetProperty("mood").GetString(), audio, inner, ActionsOf(root));
            }
        }

        // A command you let her run has finished: her line on what came of it.
        internal async Task<Reply> Ran(string command, int code, string output)
        {
            using (JsonDocument doc = JsonDocument.Parse(await Post("/ran", new { session_id = sessionId, origin, command, code, output })))
            {
                JsonElement root = doc.RootElement;
                string audio = root.TryGetProperty("audio", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                return new Reply(root.GetProperty("visible").GetString(), root.GetProperty("mood").GetString(), audio, null);
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

        // A scripted line of hers through /say: shown, mood set, spoken when asked.
        internal async Task Say(string text, string mood, bool speak)
        {
            await Post("/say", new { session_id = sessionId, text, mood, speak });
        }

        internal async Task Greet()
        {
            try { await Post("/greet", new { session_id = sessionId }); } catch (Exception) { }
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
                    catch (Exception e) { if (PointerWindow.Debug) Console.WriteLine("[events] dropped: " + e.GetType().Name + " " + e.Message); }
                    Connected?.Invoke(false);
                    try { await Task.Delay(TimeSpan.FromSeconds(2), token); } catch (OperationCanceledException) { }
                }
            }, token);
        }

        private void Dispatch(string kind, string json)
        {
            if (kind == null || json.Length == 0) return;
            if (PointerWindow.Debug) Console.WriteLine("[events] " + kind);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    switch (kind)
                    {
                        case "mood": MoodChanged?.Invoke(root.GetProperty("mood").GetString(), root.GetProperty("trust").GetDouble()); break;
                        case "reply":
                            if (root.TryGetProperty("origin", out JsonElement from) && from.ValueKind == JsonValueKind.String && from.GetString() == origin) break;
                            Replied?.Invoke(root.GetProperty("visible").GetString(),
                                root.TryGetProperty("mood", out JsonElement m) ? m.GetString() : null,
                                root.TryGetProperty("audio", out JsonElement au) && au.ValueKind == JsonValueKind.String ? au.GetString() : null,
                                root.TryGetProperty("kind", out JsonElement k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null);
                            break;
                        case "speaking": Speaking?.Invoke(root.GetProperty("on").GetBoolean()); break;
                        case "settings": SettingsChanged?.Invoke(root.GetProperty("companion").Clone()); break;
                        case "agent": AgentEvent?.Invoke(root.Clone()); AgentChanged?.Invoke(root.GetProperty("state").GetString(), root.TryGetProperty("detail", out JsonElement d) ? d.GetString() : ""); break;
                    }
                }
            }
            catch (Exception e) { if (PointerWindow.Debug) Console.WriteLine("[events] " + kind + " failed: " + e.Message); }
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
