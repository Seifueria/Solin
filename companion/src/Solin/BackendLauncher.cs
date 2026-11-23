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

        // Written by mac/build-app.sh into Solin.app: where the repo is, which python, and a PATH with Homebrew in it.
        private static (string repo, string python, string path) Home()
        {
            try
            {
                string file = Path.Combine(AppContext.BaseDirectory, "..", "Resources", "solin-home.json");
                if (!File.Exists(file)) return (null, null, null);
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)))
                {
                    System.Text.Json.JsonElement r = doc.RootElement;
                    string Get(string k) { return r.TryGetProperty(k, out System.Text.Json.JsonElement v) ? v.GetString() : null; }
                    return (Get("repo"), Get("python"), Get("path"));
                }
            }
            catch (Exception) { return (null, null, null); }
        }

        // The Solin checkout: the app's recorded repo, else the first folder above the executable (or the current directory)
        // holding core_ai/ and server/.
        internal static string FindRepo()
        {
            string recorded = Home().repo;
            if (recorded != null && File.Exists(Path.Combine(recorded, "server", "companion_api.py"))) return recorded;
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
            (string _, string homePython, string homePath) = Home();
            string python = Environment.GetEnvironmentVariable("SOLIN_PYTHON");
            if (string.IsNullOrEmpty(python))
            {
                string venv = Path.Combine(repo, "venv", "bin", "python");
                python = File.Exists(venv) ? venv : !string.IsNullOrEmpty(homePython) && File.Exists(homePython) ? homePython : "python3";
            }
            ProcessStartInfo info = new ProcessStartInfo(python, "-m server.companion_api")
            {
                WorkingDirectory = repo,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // Started from the app, PATH is bare; give the backend the one recorded at build time so claude and ollama are found.
            if (!string.IsNullOrEmpty(homePath)) info.Environment["PATH"] = homePath;
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
