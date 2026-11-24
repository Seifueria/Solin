// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // Her hands on the Mac: opening things, clicking, and running commands. Nothing here decides to act; Program does. She has
    // full control (Kaelen's call): commands run straight away, except the few that Risky catches, which wait for Run on RunCard.
    internal static class Hands
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint { public double X, Y; }

        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        [DllImport(CoreGraphics)] private static extern bool CGPreflightPostEventAccess();
        [DllImport(CoreGraphics)] private static extern bool CGRequestPostEventAccess();
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint type, CGPoint at, uint button);
        [DllImport(CoreGraphics)] private static extern void CGEventPost(uint tap, IntPtr evt);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
        private const uint LeftDown = 1, LeftUp = 2, Moved = 5, HidTap = 0;

        // Whether macOS lets her click for you (System Settings › Privacy & Security › Accessibility).
        internal static bool CanClick() { try { return OperatingSystem.IsMacOS() && CGPreflightPostEventAccess(); } catch (Exception) { return false; } }

        // Asks macOS in her own name, which also lists her under Accessibility; you turn her on there.
        internal static void AskToClick() { try { if (OperatingSystem.IsMacOS()) CGRequestPostEventAccess(); } catch (Exception) { } }

        // The Accessibility pane, so allowing her is one click away.
        internal static void OpenClickPermission() { Launch("/usr/bin/open", "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"); }

        // One left click at a point in screen pixels (the space of window positions); scaling maps them to CoreGraphics points.
        internal static async Task Click(PixelPoint at, double scaling)
        {
            if (!OperatingSystem.IsMacOS() || scaling <= 0) return;
            CGPoint p = new CGPoint { X = at.X / scaling, Y = at.Y / scaling };
            foreach (uint type in new[] { Moved, LeftDown, LeftUp })
            {
                IntPtr e = CGEventCreateMouseEvent(IntPtr.Zero, type, p, 0);
                if (e == IntPtr.Zero) return;
                CGEventPost(HidTap, e);
                CFRelease(e);
                await Task.Delay(type == Moved ? 60 : 40);
            }
        }

        // How she would open something: what to show you, the arguments for /usr/bin/open, and whether it would run code
        // (a script, an installer, an app from outside your Applications folders, an unusual link), which she says as she
        // opens it.
        internal sealed class OpenPlan
        {
            internal string Shown;
            internal string[] Args;
            internal bool RunsCode;
        }

        // Commands that delete things for good (rm skips the Trash) or can take the Mac down: the only ones that still wait for
        // your Run. Everything else she runs straight away.
        private static readonly System.Text.RegularExpressions.Regex RiskyCommand = new System.Text.RegularExpressions.Regex(
            @"\bsudo\b|\b(rm|rmdir|unlink)\s|\b(srm|shred|mkfs\w*|diskutil|dd|fdisk|gpt|csrutil|nvram|shutdown|reboot|halt)\b"
            + @"|>\s*/dev/r?disk|\b(curl|wget)\b[^|;&]*\|\s*(sudo\s+)?(ba|z)?sh\b|\bgit\s+(reset\s+--hard|clean\s+-\w*f|push\s+(-f\b|--force))"
            + @"|:\(\)\s*\{|\bkill(all)?\s+-9\s+-1|\blaunchctl\s+(unload|remove|bootout)|\btmutil\s+delete|\bch(mod|own)\s+-R\b|\bfind\b.*\s-delete\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        internal static bool Risky(string command) { return RiskyCommand.IsMatch(command ?? ""); }

        private static readonly string[] Runnable = { ".command", ".sh", ".zsh", ".bash", ".tool", ".pkg", ".mpkg", ".terminal", ".workflow", ".scpt", ".applescript", ".jar", ".py", ".rb", ".pl", ".js", ".shortcut" };

        // null when there is nothing to open: a path that does not exist.
        internal static OpenPlan PlanOpen(string what)
        {
            what = (what ?? "").Trim().Trim('"', '\'', '`');
            if (what.Length == 0) return null;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // A web page, or a pane of System Settings: straight away.
            if (what.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || what.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || what.StartsWith("x-apple.systempreferences:", StringComparison.OrdinalIgnoreCase))
                return new OpenPlan { Shown = what, Args = new[] { what } };
            // Any other kind of link can start anything at all.
            if (System.Text.RegularExpressions.Regex.IsMatch(what, @"^[A-Za-z][A-Za-z0-9+.\-]*:") && !what.StartsWith("/") && !what.StartsWith("~"))
                return new OpenPlan { Shown = "'" + what + "'", Args = new[] { what }, RunsCode = true };
            if (what.StartsWith("~")) what = home + what.Substring(1);
            if (what.StartsWith("/"))
            {
                string path = what.TrimEnd('/');
                if (path.Length == 0) path = "/";
                bool isDir = Directory.Exists(path), isFile = File.Exists(path);
                if (!isDir && !isFile) return null;
                bool ask;
                if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && isDir)
                {
                    string[] trusted = { "/Applications/", "/System/Applications/", home + "/Applications/", "/System/Library/CoreServices/" };
                    ask = Array.Find(trusted, t => path.StartsWith(t, StringComparison.Ordinal)) == null;
                }
                else if (isDir) ask = false;
                else
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    ask = Array.IndexOf(Runnable, ext) >= 0 || IsExecutable(path);
                }
                return new OpenPlan { Shown = path.Replace(home, "~"), Args = new[] { path }, RunsCode = ask };
            }
            // Otherwise an app by name: straight away (open says so when there is no such app).
            return new OpenPlan { Shown = what, Args = new[] { "-a", what } };
        }

        private static bool IsExecutable(string path)
        {
            try { return OperatingSystem.IsMacOS() && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0; }
            catch (Exception) { return false; }
        }

        // Opens it; false when open failed (no such app, say).
        internal static async Task<bool> Open(OpenPlan plan)
        {
            ProcessStartInfo info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string a in plan.Args) info.ArgumentList.Add(a);
            try
            {
                using (Process p = Process.Start(info))
                {
                    await p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception) { return false; }
        }

        // Runs a command in zsh (a login shell, so your PATH is there) in your home folder, with no input, for at most the
        // limit. Returns its exit code (-1 when stopped or it could not start) and the end of what it printed.
        internal static async Task<(int code, string output)> Run(string command, TimeSpan limit)
        {
            ProcessStartInfo info = new ProcessStartInfo("/bin/zsh")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            info.ArgumentList.Add("-lc");
            info.ArgumentList.Add(command);
            StringBuilder printed = new StringBuilder();
            object gate = new object();
            DataReceivedEventHandler keep = (sender, e) => { if (e.Data != null) lock (gate) if (printed.Length < 200_000) printed.AppendLine(e.Data); };
            try
            {
                using (Process p = Process.Start(info))
                {
                    p.StandardInput.Close();
                    p.OutputDataReceived += keep;
                    p.ErrorDataReceived += keep;
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    using (CancellationTokenSource timer = new CancellationTokenSource(limit))
                    {
                        try { await p.WaitForExitAsync(timer.Token); }
                        catch (OperationCanceledException)
                        {
                            try { p.Kill(true); } catch (Exception) { }
                            lock (gate) printed.AppendLine("(stopped: still running after " + (int)limit.TotalSeconds + " seconds)");
                            return (-1, Tail(printed, gate));
                        }
                    }
                    p.WaitForExit();
                    return (p.ExitCode, Tail(printed, gate));
                }
            }
            catch (Exception e) { return (-1, "could not start zsh: " + e.Message); }
        }

        private static string Tail(StringBuilder text, object gate)
        {
            lock (gate)
            {
                string all = text.ToString().TrimEnd();
                return all.Length <= 4000 ? all : "…" + all.Substring(all.Length - 4000);
            }
        }

        private static void Launch(string exe, string arg)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(exe) { UseShellExecute = false };
                info.ArgumentList.Add(arg);
                Process.Start(info)?.Dispose();
            }
            catch (Exception) { }
        }
    }

    // Her ask before running a risky command: the exact command in her terminal plate, and RUN / NO. Says no by itself after a minute.
    // Only one at a time; a new ask answers the old one no.
    internal sealed class RunCard : Window
    {
        private readonly TextBlock heading = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 10, Foreground = new SolidColorBrush(CutFrame.Red) };
        private readonly TextBlock command = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 12.5, LineHeight = 18, Foreground = TalkLook.Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 330, MaxLines = 8, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Border yes, no;
        private readonly DispatcherTimer expire = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private TaskCompletionSource<bool> answer;
        private Func<PixelPoint> anchor;

        internal RunCard()
        {
            TalkLook.Floating(this);
            ShowActivated = false;
            yes = Choice("RUN", CutFrame.Cyan, () => Answer(true));
            no = Choice("NO", Color.FromRgb(150, 154, 166), () => Answer(false));
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            StackPanel stack = new StackPanel { Margin = new Thickness(30, 12, 18, 14), Spacing = 6 };
            stack.Children.Add(heading);
            stack.Children.Add(command);
            stack.Children.Add(buttons);
            Grid layers = new Grid { MinWidth = 260 };
            layers.Children.Add(new CutFrame());
            layers.Children.Add(stack);
            Content = layers;
            expire.Tick += delegate { Answer(false); };
            // Hidden any other way (she hides, say): that is a no, so nothing is left waiting on it.
            PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == IsVisibleProperty && !IsVisible && answer != null && !answer.Task.IsCompleted) { expire.Stop(); answer.TrySetResult(false); }
            };
            follow.Tick += delegate { if (IsVisible) Place(); else follow.Stop(); };
        }

        private static Border Choice(string label, Color colour, Action picked)
        {
            Border b = new Border
            {
                BorderBrush = new SolidColorBrush(colour), BorderThickness = new Thickness(1.5), Padding = new Thickness(14, 4), Background = Brushes.Transparent,
                Child = new TextBlock { Text = label, FontFamily = TalkLook.Mono, FontSize = 12, Foreground = new SolidColorBrush(colour) },
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            b.PointerEntered += delegate { b.Background = new SolidColorBrush(Color.FromArgb(60, colour.R, colour.G, colour.B)); };
            b.PointerExited += delegate { b.Background = Brushes.Transparent; };
            b.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Handled = true; picked(); };
            return b;
        }

        // Where its bottom-centre goes (over her head), re-read while it shows.
        internal void Follow(Func<PixelPoint> bottomCentre) { anchor = bottomCentre; }

        // Shows the command and waits for RUN (true) or NO, or a minute of nothing (false).
        internal Task<bool> Ask(string shown, string what = "WANTS TO RUN")
        {
            answer?.TrySetResult(false);
            answer = new TaskCompletionSource<bool>();
            heading.Text = "SOLIN // " + what;
            command.Text = shown;
            Show();
            Spaces.Front(this);
            Place();
            follow.Start();
            expire.Stop();
            expire.Start();
            return answer.Task;
        }

        internal bool Waiting { get { return answer != null && !answer.Task.IsCompleted; } }

        private void Answer(bool run)
        {
            expire.Stop();
            Hide();
            answer?.TrySetResult(run);
        }

        private void Place()
        {
            if (anchor == null) return;
            PixelPoint at = anchor();
            double scale = Screens.ScreenFromPoint(at)?.Scaling ?? Screens.Primary?.Scaling ?? 1;
            int wide = (int)(Bounds.Width * scale), tall = (int)(Bounds.Height * scale);
            if (wide == 0) return;
            PixelPoint want = new PixelPoint(at.X - wide / 2, at.Y - tall);
            Screen screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                want = new PixelPoint(Math.Clamp(want.X, area.X, Math.Max(area.X, area.Right - wide)), Math.Clamp(want.Y, area.Y, Math.Max(area.Y, area.Bottom - tall)));
            }
            if (want != Position) Position = want;
        }
    }
}
