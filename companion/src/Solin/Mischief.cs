// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // Her joke "infection": a small terminal in her own plate that types a few lines as if she were breaking into your desktop,
    // then glitches away. Click-through, in her look and signed SOLIN, so it never passes for a real system window. It does
    // nothing to the Mac.
    internal sealed class InfectWindow : Window
    {
        private static readonly string[][] Scripts =
        {
            new[] { "> solin.exe --inject desktop", "patching window server..... ok", "hiding in your dock......... ok", "you're mine now." },
            new[] { "> whoami", "solin", "> sudo let-me-out", "permission denied. rude.", "fine. i'll live here then." },
            new[] { "> scanning your tabs", "too many. impressive.", "> borrowing 2% cpu", "it's for a good cause. me." },
            new[] { "> ping kaelen", "64 bytes: pay attention to me", "> rm -rf boredom/", "done." },
            new[] { "> solin --spread", "copying to every desktop..... ok", "you can't swipe away from me." },
        };
        private readonly TextBlock text = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 12.5, LineHeight = 18, Foreground = TalkLook.Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
        private readonly DispatcherTimer typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        private readonly Random random = new Random();
        private string full = "";
        private int shown;
        internal event Action Finished;
        internal bool Running { get { return IsVisible; } }

        internal InfectWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            Width = 320; Height = 150;
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
                    DispatcherTimer.RunOnce(() => { Hide(); Finished?.Invoke(); }, TimeSpan.FromSeconds(1.6));
                }
            };
        }

        // Shows it with its top-left at a screen point (kept on that screen).
        internal void Run(PixelPoint at)
        {
            Screen screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
            double scale = screen?.Scaling ?? 1;
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                at = new PixelPoint(Math.Clamp(at.X, area.X, area.Right - (int)(Width * scale)), Math.Clamp(at.Y, area.Y, area.Bottom - (int)(Height * scale)));
            }
            full = string.Join("\n", Scripts[random.Next(Scripts.Length)]);
            shown = 0;
            text.Text = "";
            Position = at;
            Show();
            Spaces.Front(this);
            typing.Start();
        }
    }
}
