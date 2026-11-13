// Solin-owned; not derived from upstream.
using System;
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
        internal event Action<string> Replied;
        internal event Action<bool> Speaking;
        internal event Action<bool> Connected;

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

        internal async Task<(string visible, string mood)> Chat(string text, bool speak = false)
        {
            string json = await Post("/chat", new { session_id = sessionId, text, speak });
            using (JsonDocument doc = JsonDocument.Parse(json))
                return (doc.RootElement.GetProperty("visible").GetString(), doc.RootElement.GetProperty("mood").GetString());
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
                        case "reply": Replied?.Invoke(root.GetProperty("visible").GetString()); break;
                        case "speaking": Speaking?.Invoke(root.GetProperty("on").GetBoolean()); break;
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
