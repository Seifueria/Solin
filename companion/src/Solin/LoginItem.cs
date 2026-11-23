// Solin-owned; not derived from upstream.
using System;
using System.Diagnostics;
using System.IO;

namespace Solin.Companion
{
    // Opens her at login with a per-user LaunchAgent. Only offered when she is running from Solin.app.
    internal static class LoginItem
    {
        private const string Label = "com.seifueria.solin";
        private static string PlistPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist"); } }

        // The .app she is running from, or null when run from the terminal.
        internal static string AppBundle
        {
            get
            {
                string dir = AppContext.BaseDirectory.TrimEnd('/');
                int at = dir.IndexOf(".app/Contents/MacOS", StringComparison.Ordinal);
                return at < 0 ? null : dir.Substring(0, at + 4);
            }
        }

        internal static bool Enabled { get { return File.Exists(PlistPath); } }

        internal static void Set(bool on)
        {
            string app = AppBundle;
            if (!OperatingSystem.IsMacOS() || app == null) return;
            if (on == Enabled && (!on || File.ReadAllText(PlistPath).Contains(app))) return;
            if (!on) { try { File.Delete(PlistPath); } catch (Exception) { } return; }
            Directory.CreateDirectory(Path.GetDirectoryName(PlistPath));
            // "open -g" starts the app in the background at login, through LaunchServices, so it runs as Solin.app.
            File.WriteAllText(PlistPath, $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
  <key>Label</key><string>{Label}</string>
  <key>ProgramArguments</key>
  <array><string>/usr/bin/open</string><string>-g</string><string>{System.Security.SecurityElement.Escape(app)}</string></array>
  <key>RunAtLoad</key><true/>
  <key>AssociatedBundleIdentifiers</key><string>{Label}</string>
</dict>
</plist>
");
        }
    }
}
