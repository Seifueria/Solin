// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Solin.Companion
{
    // Shared look for the two small windows that float beside her.
    internal static class TalkLook
    {
        internal static readonly IBrush Panel = new SolidColorBrush(Color.FromArgb(230, 20, 16, 24));
        internal static readonly IBrush Edge = new SolidColorBrush(Color.FromArgb(255, 120, 90, 160));
        internal static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(240, 236, 244));
        internal static readonly IBrush Faint = new SolidColorBrush(Color.FromArgb(150, 240, 236, 244));

        internal static Window Floating(Window window)
        {
            window.SystemDecorations = SystemDecorations.None;
            window.Background = Brushes.Transparent;
            window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            window.Topmost = true;
            window.ShowInTaskbar = false;
            window.CanResize = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            return window;
        }
    }

    // A one-line box under her. Enter sends, Escape closes.
    internal sealed class Typer : Window
    {
        private readonly TextBox box = new TextBox { Width = 300, Watermark = "say something to solin", Foreground = TalkLook.Ink, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 14 };
        internal event Action<string> Sent;

        internal Typer()
        {
            TalkLook.Floating(this);
            ShowActivated = true;
            Content = new Border { Background = TalkLook.Panel, BorderBrush = TalkLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6), Child = box };
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
                if (e.Key == Key.Enter)
                {
                    string text = (box.Text ?? "").Trim();
                    box.Text = "";
                    Hide();
                    if (text.Length > 0) Sent?.Invoke(text);
                    e.Handled = true;
                }
            };
            Deactivated += delegate { Hide(); };
        }

        internal void Open(PixelPoint at)
        {
            Position = at;
            Show();
            Activate();
            box.Focus();
        }
    }

    // Her reply above her head, typed in a few letters at a time, then left up long enough to read.
    internal sealed class Bubble : Window
    {
        private readonly TextBlock text = new TextBlock { MaxWidth = 320, TextWrapping = TextWrapping.Wrap, Foreground = TalkLook.Ink, FontSize = 14, LineHeight = 20 };
        private readonly TextBlock tag = new TextBlock { Foreground = TalkLook.Faint, FontSize = 11, Margin = new Thickness(0, 0, 0, 4), Text = "solin" };
        private readonly DispatcherTimer typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(28) };
        private readonly DispatcherTimer linger = new DispatcherTimer();
        private string full = "";
        private int shown;
        private Func<PixelPoint> anchor;

        // True from the first letter until the bubble goes; the sprite moves her mouth while it is.
        internal event Action<bool> Speaking;

        internal Bubble()
        {
            TalkLook.Floating(this);
            ShowActivated = false;
            IsHitTestVisible = false;
            StackPanel stack = new StackPanel();
            stack.Children.Add(tag);
            stack.Children.Add(text);
            Content = new Border { Background = TalkLook.Panel, BorderBrush = TalkLook.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8), Child = stack };
            typing.Tick += delegate
            {
                shown = Math.Min(full.Length, shown + 2);
                text.Text = full.Substring(0, shown);
                Place();
                if (shown >= full.Length)
                {
                    typing.Stop();
                    Speaking?.Invoke(false);
                    linger.Interval = TimeSpan.FromSeconds(Math.Clamp(2 + full.Length / 18.0, 3, 14));
                    linger.Start();
                }
            };
            linger.Tick += delegate { linger.Stop(); Hide(); };
        }

        // Where the bubble's bottom-centre should sit, in screen pixels; re-read every frame so it follows her when dragged.
        internal void Follow(Func<PixelPoint> anchorPoint) { anchor = anchorPoint; }

        internal void Say(string reply, string mood)
        {
            full = reply ?? "";
            shown = 0;
            text.Text = "";
            tag.Text = string.IsNullOrEmpty(mood) || mood == "neutral" ? "solin" : "solin · " + mood;
            linger.Stop();
            Place();
            Show();
            Speaking?.Invoke(true);
            typing.Start();
        }

        private void Place()
        {
            if (anchor == null) return;
            PixelPoint at = anchor();
            double scale = Screens.Primary?.Scaling ?? 1;
            int wide = (int)(Bounds.Width * scale), tall = (int)(Bounds.Height * scale);
            Position = new PixelPoint(at.X - wide / 2, at.Y - tall);
        }
    }
}
