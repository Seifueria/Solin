// Solin-owned; not derived from upstream.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Solin.Companion
{
    // One screenshot of the main display, only when you ask her to look. Shrunk before it goes to her brain, deleted after.
    internal static class ScreenShot
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "solin-look");

        internal static string LastError;

        // Path to a JPEG of the main display, or null (with LastError set) when macOS has not given her permission.
        internal static async Task<string> Capture()
        {
            LastError = null;
            if (!OperatingSystem.IsMacOS()) { LastError = "not on a Mac"; return null; }
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, "look-" + DateTime.Now.ToString("HHmmss") + ".jpg");
            (int code, string err) = await Run("/usr/sbin/screencapture", "-x -m -t jpg \"" + file + "\"");
            if (code != 0 || !File.Exists(file) || new FileInfo(file).Length == 0)
            {
                LastError = string.IsNullOrWhiteSpace(err) ? "screen capture refused" : err.Trim();
                return null;
            }
            // About 1600 pixels across is plenty to read a screen, and far cheaper to send.
            await Run("/usr/bin/sips", "-Z 1600 \"" + file + "\"");
            return file;
        }

        internal static void Forget(string file)
        {
            try { if (file != null && File.Exists(file)) File.Delete(file); } catch (Exception) { }
        }

        // The Screen Recording pane, so granting her permission is one click away.
        internal static void OpenPermissionPane()
        {
            try { Process.Start(new ProcessStartInfo("/usr/bin/open", "\"x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture\"") { UseShellExecute = false })?.Dispose(); }
            catch (Exception) { }
        }

        private static async Task<(int, string)> Run(string exe, string args)
        {
            ProcessStartInfo info = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (Process p = Process.Start(info))
            {
                string err = await p.StandardError.ReadToEndAsync();
                await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return (p.ExitCode, err);
            }
        }
    }
}
