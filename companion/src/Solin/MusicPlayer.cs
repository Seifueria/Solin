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
    // A small player under her feet while music plays: title, artist, time, a progress bar, previous / play-pause / next.
    // (Look described from the base's mini player; no code or art taken.)
    internal sealed class MusicPlayer : Window
    {
        private readonly TextBlock title = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 13, Foreground = TalkLook.Ink, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 };
        private readonly TextBlock artist = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(58, 214, 235)), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
        private readonly TextBlock time = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 10, Foreground = TalkLook.Dim, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly ProgressLine progress = new ProgressLine { Height = 14 };
        private readonly TextBlock playGlyph = new TextBlock { Text = "❚❚", FontSize = 13, Foreground = TalkLook.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private Func<PixelPoint> anchor;
        private Music.Track track;
        private DateTime readAt;

        internal event Action<string> Command;
        internal event Action Closed2;

        internal MusicPlayer()
        {
            TalkLook.Floating(this);
            ShowActivated = false;
            Width = 300;
            SizeToContent = SizeToContent.Height;
            Button close = Glyph("×", 14); close.Click += delegate { Hide(); Closed2?.Invoke(); };
            Grid top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            StackPanel names = new StackPanel { Spacing = 2 };
            names.Children.Add(title);
            Grid line2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            line2.Children.Add(artist);
            Grid.SetColumn(time, 1); line2.Children.Add(time);
            names.Children.Add(line2);
            top.Children.Add(names);
            Grid.SetColumn(close, 1); top.Children.Add(close);

            Button prev = Glyph("⏮", 13), next = Glyph("⏭", 13);
            Button play = new Button { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(0), Background = Brushes.Transparent, BorderBrush = TalkLook.AccentBrush, BorderThickness = new Thickness(2), Content = playGlyph, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            prev.Click += delegate { Command?.Invoke("previous track"); };
            next.Click += delegate { Command?.Invoke("next track"); };
            play.Click += delegate { Command?.Invoke("playpause"); };
            StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center };
            controls.Children.Add(prev); controls.Children.Add(play); controls.Children.Add(next);

            StackPanel stack = new StackPanel { Spacing = 6, Margin = new Thickness(16, 10, 10, 10) };
            stack.Children.Add(top);
            stack.Children.Add(progress);
            stack.Children.Add(controls);
            // A pink-to-cyan glowing edge.
            Content = new Border
            {
                Child = stack, Background = new SolidColorBrush(Color.FromArgb(245, 14, 11, 18)), CornerRadius = new CornerRadius(12), Margin = new Thickness(10),
                BorderThickness = new Thickness(2), BorderBrush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.FromRgb(232, 72, 120), 0), new GradientStop(Color.FromRgb(58, 214, 235), 1) } },
                BoxShadow = BoxShadows.Parse("0 0 18 2 #66E84878"),
            };
            follow.Tick += delegate { if (IsVisible) Place(); else follow.Stop(); };
            tick.Tick += delegate { Refresh(); };
        }

        private static Button Glyph(string text, double size)
        {
            return new Button { Content = new TextBlock { Text = text, FontSize = size, Foreground = TalkLook.Ink }, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 2) };
        }

        internal void Follow(Func<PixelPoint> anchorPoint) { anchor = anchorPoint; }

        internal void Show(Music.Track t)
        {
            track = t; readAt = DateTime.Now;
            title.Text = t.Title; artist.Text = t.Artist;
            playGlyph.Text = t.Playing ? "❚❚" : "▶";
            Refresh();
            if (!IsVisible) { Show(); Place(); }
            follow.Start(); tick.Start();
        }

        internal void Update(Music.Track t) { if (t == null) return; track = t; readAt = DateTime.Now; playGlyph.Text = t.Playing ? "❚❚" : "▶"; title.Text = t.Title; artist.Text = t.Artist; Refresh(); }

        private static string Clock(double s) { s = Math.Max(0, s); return ((int)s / 60) + ":" + ((int)s % 60).ToString("00"); }

        private void Refresh()
        {
            if (track == null) return;
            double pos = track.Position + (track.Playing ? (DateTime.Now - readAt).TotalSeconds : 0);
            pos = Math.Min(pos, track.Duration);
            time.Text = Clock(pos) + " / " + Clock(track.Duration);
            progress.Fraction = track.Duration > 0 ? pos / track.Duration : 0;
            progress.InvalidateVisual();
        }

        private void Place()
        {
            if (anchor == null) return;
            PixelPoint at = anchor();
            double scale = Screens.Primary?.Scaling ?? 1;
            int wide = (int)(Bounds.Width * scale), tall = (int)(Bounds.Height * scale);
            if (wide == 0) return;
            PixelPoint want = new PixelPoint(at.X - wide / 2, at.Y);
            Screen screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                want = new PixelPoint(Math.Clamp(want.X, area.X, Math.Max(area.X, area.Right - wide)), Math.Min(want.Y, area.Bottom - tall));
            }
            if (want != Position) Position = want;
        }

        internal void Stop() { tick.Stop(); follow.Stop(); Hide(); }
    }

    // A thin track with a pink fill and a round knob.
    internal sealed class ProgressLine : Control
    {
        internal double Fraction;
        public override void Render(DrawingContext g)
        {
            double y = Bounds.Height / 2, w = Bounds.Width, x = Math.Clamp(Fraction, 0, 1) * w;
            g.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 3, lineCap: PenLineCap.Round), new Point(0, y), new Point(w, y));
            g.DrawLine(new Pen(TalkLook.AccentBrush, 3, lineCap: PenLineCap.Round), new Point(0, y), new Point(x, y));
            g.DrawEllipse(Brushes.White, new Pen(TalkLook.AccentBrush, 2), new Point(x, y), 6, 6);
        }
    }
}
