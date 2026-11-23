// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Solin.Companion
{
    // One screenshot of every display, side by side in one picture (each brought to the same height), when she looks.
    // Shrunk before it goes to her brain, deleted after.
    internal static class ScreenShot
    {
        private static readonly string Folder = Path.Combine(Path.GetTempPath(), "solin-look");

        internal static string LastError;

        [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern bool CGPreflightScreenCaptureAccess();
        [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern bool CGRequestScreenCaptureAccess();

        // Whether macOS lets Solin itself capture the screen right now.
        internal static bool Allowed() { try { return OperatingSystem.IsMacOS() && CGPreflightScreenCaptureAccess(); } catch (Exception) { return false; } }

        // Asks macOS for the permission in Solin's own name, which also lists her in System Settings › Screen Recording.
        internal static bool Request() { try { return OperatingSystem.IsMacOS() && CGRequestScreenCaptureAccess(); } catch (Exception) { return false; } }

        // How many screens the last picture holds, side by side, left to right in macOS's display order.
        internal static int LastScreens = 1;

        // Path to a JPEG of every display (screens: how many are connected), or null (with LastError set) when macOS has not
        // given her permission. Must be awaited from the UI thread when there is more than one screen (the join draws).
        internal static async Task<string> Capture(int screens = 1)
        {
            LastError = null;
            LastScreens = 1;
            if (!OperatingSystem.IsMacOS()) { LastError = "not on a Mac"; return null; }
            Directory.CreateDirectory(Folder);
            string stamp = DateTime.Now.ToString("HHmmssfff");
            // screencapture writes one file per display for as many file names as it is given.
            List<string> parts = new List<string>();
            for (int i = 0; i < Math.Max(1, screens); i++) parts.Add(Path.Combine(Folder, "look-" + stamp + "-" + i + ".jpg"));
            (int code, string err) = await Run("/usr/sbin/screencapture", "-x -t jpg " + string.Join(" ", parts.ConvertAll(p => "\"" + p + "\"")));
            List<string> made = parts.FindAll(p => File.Exists(p) && new FileInfo(p).Length > 0);
            foreach (string p in parts) if (!made.Contains(p)) Forget(p);
            if (made.Count == 0)
            {
                LastError = string.IsNullOrWhiteSpace(err) ? "screen capture refused" : err.Trim();
                return null;
            }
            if (made.Count == 1)
            {
                // About 1600 pixels across is plenty to read a screen, and far cheaper to send.
                await Run("/usr/bin/sips", "-Z 1600 \"" + made[0] + "\"");
                return made[0];
            }
            // Several screens: each to the same height, then side by side, then one JPEG at most 2400 across.
            foreach (string p in made) await Run("/usr/bin/sips", "--resampleHeight 1000 \"" + p + "\"");
            string joined = Path.Combine(Folder, "look-" + stamp + ".png"), file = Path.Combine(Folder, "look-" + stamp + ".jpg");
            try { Join(made, joined); }
            catch (Exception e) { LastError = "joining screens: " + e.Message; }
            foreach (string p in made.GetRange(1, made.Count - 1)) Forget(p);
            if (!File.Exists(joined)) return made[0];
            Forget(made[0]);
            await Run("/usr/bin/sips", "-s format jpeg -Z 2400 \"" + joined + "\" --out \"" + file + "\"");
            Forget(joined);
            if (!File.Exists(file)) return null;
            LastScreens = made.Count;
            return file;
        }

        // Draws the pictures left to right with a thin dark gap between them, into one PNG.
        private static void Join(List<string> files, string output)
        {
            List<Avalonia.Media.Imaging.Bitmap> pictures = files.ConvertAll(f => new Avalonia.Media.Imaging.Bitmap(f));
            try
            {
                const int gap = 8;
                int wide = gap * (pictures.Count - 1), tall = 0;
                foreach (Avalonia.Media.Imaging.Bitmap b in pictures) { wide += b.PixelSize.Width; tall = Math.Max(tall, b.PixelSize.Height); }
                using (Avalonia.Media.Imaging.RenderTargetBitmap canvas = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(wide, tall), new Avalonia.Vector(96, 96)))
                {
                    using (Avalonia.Media.DrawingContext g = canvas.CreateDrawingContext())
                    {
                        g.FillRectangle(Avalonia.Media.Brushes.Black, new Avalonia.Rect(0, 0, wide, tall));
                        double x = 0;
                        foreach (Avalonia.Media.Imaging.Bitmap b in pictures)
                        {
                            g.DrawImage(b, new Avalonia.Rect(x, 0, b.PixelSize.Width, b.PixelSize.Height));
                            x += b.PixelSize.Width + gap;
                        }
                    }
                    canvas.Save(output);
                }
            }
            finally { foreach (Avalonia.Media.Imaging.Bitmap b in pictures) b.Dispose(); }
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
