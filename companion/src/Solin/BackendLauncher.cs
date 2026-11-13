// Solin-owned; not derived from upstream.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Solin.Companion
{
    // Starts server/companion_api.py when nothing answers on the loopback, and stops it with her.
    internal sealed class BackendLauncher : IDisposable
    {
        private Process child;

        // The Solin checkout: the first folder above the executable (or the current directory) holding core_ai/ and server/.
        internal static string FindRepo()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                DirectoryInfo dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "core_ai")) && File.Exists(Path.Combine(dir.FullName, "server", "companion_api.py"))) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        internal async Task<bool> EnsureRunning(BackendClient client)
        {
            if (await client.Healthy()) return true;
            string repo = FindRepo();
            if (repo == null) return false;
            string python = Environment.GetEnvironmentVariable("SOLIN_PYTHON");
            if (string.IsNullOrEmpty(python))
            {
                string venv = Path.Combine(repo, "venv", "bin", "python");
                python = File.Exists(venv) ? venv : "python3";
            }
            ProcessStartInfo info = new ProcessStartInfo(python, "-m server.companion_api")
            {
                WorkingDirectory = repo,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try { child = Process.Start(info); }
            catch (Exception) { return false; }
            child.OutputDataReceived += delegate { };
            child.ErrorDataReceived += delegate { };
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(250);
                if (await client.Healthy()) return true;
                if (child.HasExited) return false;
            }
            return false;
        }

        public void Dispose()
        {
            try { if (child != null && !child.HasExited) child.Kill(true); }
            catch (Exception) { }
            child?.Dispose();
            child = null;
        }
    }
}
