// Solin-owned; not derived from upstream.
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace Solin.Companion
{
    // What Spotify or Apple Music is playing, read with AppleScript every couple of seconds, but only while one of them is
    // running (checked with pgrep, which needs no permission), so neither app is ever launched by her.
    // (Idea described from the base's and the Edgerunners build's music player; no code taken.)
    internal sealed class Music
    {
        internal sealed class Track
        {
            internal string App, Title, Artist;
            internal double Position, Duration;
            internal bool Playing;
            internal string Key { get { return App + "|" + Title + "|" + Artist; } }
        }

        internal Track Now;
        internal event Action<Track> Changed;

        private static async Task<(int code, string output)> Run(string exe, string args, string stdin = null)
        {
            ProcessStartInfo info = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null };
            using (Process p = Process.Start(info))
            {
                if (stdin != null) { await p.StandardInput.WriteAsync(stdin); p.StandardInput.Close(); }
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                return (p.ExitCode, output.Trim());
            }
        }

        private static async Task<bool> Running(string process) { return (await Run("/usr/bin/pgrep", "-x " + process)).code == 0; }

        // Spotify gives duration in milliseconds, Music in seconds; both give position in seconds.
        private const string SpotifyScript = "tell application \"Spotify\"\nif player state is stopped then return \"\"\nset t to current track\nreturn (name of t) & tab & (artist of t) & tab & (player position as text) & tab & ((duration of t) / 1000 as text) & tab & (player state as text)\nend tell";
        private const string MusicScript = "tell application \"Music\"\nif player state is stopped then return \"\"\nset t to current track\nreturn (name of t) & tab & (artist of t) & tab & (player position as text) & tab & (duration of t as text) & tab & (player state as text)\nend tell";

        internal async Task Poll()
        {
            Track found = null;
            try
            {
                foreach ((string app, string script) in new[] { ("Spotify", SpotifyScript), ("Music", MusicScript) })
                {
                    if (!OperatingSystem.IsMacOS() || !await Running(app)) continue;
                    (int code, string output) = await Run("/usr/bin/osascript", "-", script);
                    if (code != 0 || string.IsNullOrEmpty(output)) continue;
                    string[] parts = output.Split('\t');
                    if (parts.Length < 5) continue;
                    Track t = new Track { App = app, Title = parts[0], Artist = parts[1], Playing = parts[4].Trim() == "playing" };
                    double.TryParse(parts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out t.Position);
                    double.TryParse(parts[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out t.Duration);
                    // Prefer whichever app is actually playing.
                    if (found == null || (t.Playing && !found.Playing)) found = t;
                }
            }
            catch (Exception) { found = null; }
            Track was = Now;
            Now = found;
            if ((was == null) != (found == null) || (was != null && found != null && (was.Key != found.Key || was.Playing != found.Playing))) Changed?.Invoke(found);
        }

        // "playpause", "next track" or "previous track", sent to the app that is playing.
        internal async Task Control(string command)
        {
            if (Now == null) return;
            try { await Run("/usr/bin/osascript", "-", "tell application \"" + Now.App + "\" to " + command); } catch (Exception) { }
            await Poll();
        }
    }
}
