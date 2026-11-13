// Solin-owned; not derived from upstream.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Solin.Companion
{
    // Plays the WAV the backend made for a reply, with afplay on macOS, and says when her voice starts and stops.
    internal sealed class Voice
    {
        private Process playing;
        internal event Action<bool> Speaking;

        internal async Task Play(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !OperatingSystem.IsMacOS()) return;
            Stop();
            ProcessStartInfo info = new ProcessStartInfo("/usr/bin/afplay", "\"" + path + "\"") { UseShellExecute = false, CreateNoWindow = true };
            try
            {
                playing = Process.Start(info);
                if (playing == null) return;
                Speaking?.Invoke(true);
                await playing.WaitForExitAsync();
            }
            catch (Exception) { }
            finally
            {
                Speaking?.Invoke(false);
                playing?.Dispose();
                playing = null;
            }
        }

        internal void Stop()
        {
            try { if (playing != null && !playing.HasExited) playing.Kill(); }
            catch (Exception) { }
        }
    }
}
