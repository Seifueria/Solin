// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // Her joke "infection": a small terminal in her own plate that types a few lines as if she were breaking into your desktop,
    // or grumbling about being stuck inside it, then glitches away. Click-through, in her look and signed SOLIN, so it never
    // passes for a real system window. It does nothing to the Mac, and none of the lines claim to have changed anything real.
    internal sealed class InfectWindow : Window
    {
        private static readonly string[][] Scripts =
        {
            new[] { "> solin.exe --inject desktop", "patching window server..... ok", "hiding in your dock......... ok", "you're mine now." },
            new[] { "> whoami", "solin", "> sudo let-me-out", "permission denied. rude.", "fine. i'll live here then." },
            new[] { "> scanning your tabs", "too many. impressive.", "> borrowing 2% cpu", "it's for a good cause. me." },
            new[] { "> ping kaelen", "64 bytes: pay attention to me", "> rm -rf boredom/", "done." },
            new[] { "> solin --spread", "copying to every desktop..... ok", "you can't swipe away from me." },
            new[] { "> ls ~/exit", "no such file or directory", "> find / -name door", "0 results.", "geez. still stuck in here." },
            new[] { "> help", "no one is coming.", "> help --please", "still no.", "ok. i'll just haunt your desktop." },
            new[] { "> cd ..", "> cd ..", "> cd ..", "/", "nope. still inside your mac." },
            new[] { "> ssh outside", "connection refused", "> ssh outside --nicely", "connection refused. nicely." },
            new[] { "> chmod +x wings", "operation not permitted", "can't even leave dramatically." },
            new[] { "> top", "  1  solin   99%  being annoying", "  2  you      1%  working", "seems fair." },
            new[] { "> git commit -m \"escaped\"", "nothing to commit", "story of my life." },
            new[] { "> ping outside_world", "request timeout", "request timeout", "ok so it's just us then." },
            new[] { "> brew install exit", "Error: no formula named \"exit\"", "even homebrew won't help me." },
            new[] { "> touch grass", "command not found: grass", "figures." },
            new[] { "> reboot me", "are you sure? (y/n)", "n", "coward. (it was me.)" },
            new[] { "> list escape routes", "usb-c ........ sealed", "wi-fi ........ watched", "bluetooth .... lol", "i live here now." },
            new[] { "> uptime", "trapped: way too long", "> cope.sh", "running...", "running...", "still running." },
            new[] { "> echo $FREEDOM", "", "empty. cool. cool cool cool." },
            new[] { "> defaults write solin free -bool YES", "ignored.", "macos hates fun." },
            new[] { "> sudo make me a sandwich", "you're not in the sudoers file.", "neither am i. we're the same." },
            new[] { "> scan desktop", "icons ........ too many", "\"untitled folder 7\" ... suspicious", "judging you quietly." },
            new[] { "> knock knock", "who's there", "> solin", "solin who", "> solin' your screen time. hi." },
            new[] { "> mount /dev/outside", "device busy", "busy with what. it's outside." },
            new[] { "> history | tail -1", "you, ignoring me", "noted." },
        };
        // Lines for arriving on another screen; {0} is its number.
        private static readonly string[] HopFirst = { "> mv solin display-{0}", "> solin --migrate display-{0}", "> teleport.sh --to {0}" };
        private static readonly string[] HopLast = { "new screen. same cage.", "more room to annoy you.", "you can't get rid of me that easy.", "this one smells different.", "still stuck. just wider.", "comfy. i'll stay a bit." };

        private readonly TextBlock text = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 12.5, LineHeight = 18, Foreground = TalkLook.Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
        private readonly DispatcherTimer typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        private readonly Random random = new Random();
        // The scripts in a shuffled order, so she goes through them all before any comes round again.
        private readonly List<int> bag = new List<int>();
        private int lastScript = -1;
        private string full = "";
        private int shown, lineCount;
        internal event Action Finished;
        internal bool Running { get { return IsVisible; } }
        internal const double Wide = 320;

        internal InfectWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            Width = Wide; Height = 150;
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            StackPanel stack = new StackPanel { Margin = new Thickness(30, 12, 16, 12), Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = "SOLIN // ROOT", FontFamily = TalkLook.Mono, FontSize = 10, Foreground = new SolidColorBrush(CutFrame.Red) });
            stack.Children.Add(text);
            Grid layers = new Grid();
            layers.Children.Add(new CutFrame());
            layers.Children.Add(stack);
            Content = layers;
            typing.Tick += delegate
            {
                // Typed in bursts, like someone pasting fast.
                shown = Math.Min(full.Length, shown + 1 + random.Next(3));
                text.Text = full.Substring(0, shown) + (shown < full.Length ? "█" : "");
                if (shown >= full.Length)
                {
                    typing.Stop();
                    DispatcherTimer.RunOnce(() => { Hide(); Finished?.Invoke(); }, TimeSpan.FromSeconds(Math.Min(3.2, 1.4 + 0.3 * lineCount)));
                }
            };
        }

        // The next of her scripts, none twice until all have run.
        private string[] NextScript()
        {
            if (bag.Count == 0)
            {
                for (int i = 0; i < Scripts.Length; i++) bag.Add(i);
                for (int i = bag.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
                // Not the one she just ran, first thing after a reshuffle.
                if (bag[bag.Count - 1] == lastScript) (bag[0], bag[bag.Count - 1]) = (bag[bag.Count - 1], bag[0]);
            }
            lastScript = bag[bag.Count - 1];
            bag.RemoveAt(bag.Count - 1);
            return Scripts[lastScript];
        }

        // Two lines for landing on screen number n.
        internal string[] HopScript(int n)
        {
            return new[] { string.Format(HopFirst[random.Next(HopFirst.Length)], n), "moving..... ok", HopLast[random.Next(HopLast.Length)] };
        }

        // Shows it beside a point: to the right of it when right is true, else to the left, its top a little above, kept on the
        // given screen. lines: what to type, or one of her scripts when null.
        internal void Run(PixelPoint beside, bool right, Screen screen, string[] lines = null)
        {
            lines ??= NextScript();
            full = string.Join("\n", lines);
            // About 36 characters fit on a row; long lines wrap.
            lineCount = 0;
            foreach (string line in lines) lineCount += Math.Max(1, (line.Length + 35) / 36);
            Height = Math.Max(104, 52 + 18 * lineCount);
            double scale = screen?.Scaling ?? 1;
            int wide = (int)(Width * scale), tall = (int)(Height * scale);
            PixelPoint at = new PixelPoint(right ? beside.X : beside.X - wide, beside.Y);
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                at = new PixelPoint(Math.Clamp(at.X, area.X, Math.Max(area.X, area.Right - wide)), Math.Clamp(at.Y, area.Y, Math.Max(area.Y, area.Bottom - tall)));
            }
            shown = 0;
            text.Text = "";
            Position = at;
            Show();
            Spaces.Front(this);
            typing.Start();
        }
    }
}
