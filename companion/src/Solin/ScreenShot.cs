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

        // One screen's picture, for her to click by: where that screen is (in the same pixels as window positions) and the
        // picture's own size, which her click coordinates are given in.
        internal sealed class Shot
        {
            internal string Path;
            internal Avalonia.PixelRect Area;
            internal int W, H;
        }

        // A picture of each screen (areas: the screens, in window-position pixels), each at most 1280 across, so it reaches
        // her brain without being shrunk again and her click lands where she meant; the screen with the cursor first.
        // Empty, with LastError set, when macOS has not given her permission. scaling maps window pixels to CoreGraphics points.
        internal static async Task<List<Shot>> CaptureEach(IReadOnlyList<Avalonia.PixelRect> areas, Avalonia.PixelPoint? cursor, double scaling)
        {
            LastError = null;
            List<Shot> shots = new List<Shot>();
            if (!OperatingSystem.IsMacOS()) { LastError = "not on a Mac"; return shots; }
            Directory.CreateDirectory(Folder);
            List<Avalonia.PixelRect> order = new List<Avalonia.PixelRect>(areas);
            int first = cursor.HasValue ? order.FindIndex(a => a.Contains(cursor.Value)) : -1;
            if (first > 0) { Avalonia.PixelRect c = order[first]; order.RemoveAt(first); order.Insert(0, c); }
            string stamp = DateTime.Now.ToString("HHmmssfff");
            for (int i = 0; i < order.Count && i < 4; i++)
            {
                Avalonia.PixelRect area = order[i];
                string file = System.IO.Path.Combine(Folder, "hand-" + stamp + "-" + i + ".jpg");
                double s = scaling <= 0 ? 1 : scaling;
                // -R takes points; written joined to the flag so a negative x or y is not read as another option.
                string rect = (int)Math.Round(area.X / s) + "," + (int)Math.Round(area.Y / s) + "," + (int)Math.Round(area.Width / s) + "," + (int)Math.Round(area.Height / s);
                (int code, string err) = await Run("/usr/sbin/screencapture", "-x -t jpg -R" + rect + " \"" + file + "\"");
                if (!File.Exists(file) || new FileInfo(file).Length == 0)
                {
                    Forget(file);
                    LastError = string.IsNullOrWhiteSpace(err) ? "screen capture refused" : err.Trim();
                    continue;
                }
                await Run("/usr/bin/sips", "-Z 1280 \"" + file + "\"");
                (int w, int h) = await Size(file);
                if (w <= 0 || h <= 0) { Forget(file); continue; }
                shots.Add(new Shot { Path = file, Area = area, W = w, H = h });
            }
            return shots;
        }

        // Where a point on one of those pictures is on screen, in window-position pixels.
        internal static Avalonia.PixelPoint OnScreen(Shot shot, double x, double y)
        {
            return new Avalonia.PixelPoint(shot.Area.X + (int)Math.Round(Math.Clamp(x, 0, shot.W - 1) * shot.Area.Width / (double)shot.W),
                                           shot.Area.Y + (int)Math.Round(Math.Clamp(y, 0, shot.H - 1) * shot.Area.Height / (double)shot.H));
        }

        private static async Task<(int, int)> Size(string file)
        {
            ProcessStartInfo info = new ProcessStartInfo("/usr/bin/sips", "-g pixelWidth -g pixelHeight \"" + file + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process p = Process.Start(info))
            {
                string text = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                System.Text.RegularExpressions.Match w = System.Text.RegularExpressions.Regex.Match(text, @"pixelWidth:\s*(\d+)"), h = System.Text.RegularExpressions.Regex.Match(text, @"pixelHeight:\s*(\d+)");
                return (w.Success ? int.Parse(w.Groups[1].Value) : 0, h.Success ? int.Parse(h.Groups[1].Value) : 0);
            }
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
