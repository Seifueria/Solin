// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Solin.Companion.Upstream;

namespace Solin.Companion
{
    // Shared look for the small windows that float beside her.
    internal static class TalkLook
    {
        internal static readonly Color Accent = Color.FromRgb(232, 72, 120);
        internal static readonly IBrush Panel = new SolidColorBrush(Color.FromArgb(236, 16, 13, 20));
        internal static readonly IBrush Edge = new SolidColorBrush(Color.FromArgb(255, 70, 56, 84));
        internal static readonly IBrush AccentBrush = new SolidColorBrush(Accent);
        internal static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(243, 238, 246));
        internal static readonly IBrush Faint = new SolidColorBrush(Color.FromArgb(140, 243, 238, 246));
        internal static readonly BoxShadows Shadow = BoxShadows.Parse("0 6 18 0 #80000000");
        // The terminal face of the base's composer: a mono typeface, a dim prompt, a bright caret.
        internal static readonly FontFamily Mono = new FontFamily("avares://Solin.Companion/Assets#Share Tech Mono");
        internal static readonly IBrush Dim = new SolidColorBrush(Color.FromRgb(122, 128, 138));
        internal static readonly IBrush Plate = new SolidColorBrush(Color.FromArgb(245, 9, 11, 15));
        internal static readonly IBrush PlateEdge = new SolidColorBrush(Color.FromRgb(70, 73, 82));

        // The "> you:" prompt, dim and italic, for the left of a text box.
        internal static TextBlock Prompt(double size)
        {
            return new TextBlock { Text = "> you:", FontFamily = Mono, FontStyle = FontStyle.Italic, FontSize = size, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        }

        // Strips a TextBox down to bare text on a transparent plate, with the accent as caret and selection.
        internal static void Bare(TextBox box)
        {
            box.FontFamily = Mono;
            box.BorderThickness = new Thickness(0);
            box.Padding = new Thickness(0);
            box.MinHeight = 0;
            box.VerticalContentAlignment = VerticalAlignment.Center;
            foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
                box.Resources[key] = Brushes.Transparent;
            foreach (string key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
                box.Resources[key] = Ink;
            box.Resources["TextControlSelectionHighlightColor"] = new SolidColorBrush(Color.FromArgb(120, 232, 72, 120));
            box.CaretBrush = AccentBrush;
        }

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

        internal static Border Card(Control child, double radius = 14)
        {
            return new Border
            {
                Background = Panel, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius),
                BoxShadow = Shadow, Padding = new Thickness(0), Margin = new Thickness(10, 6, 10, 18), Child = child, ClipToBounds = false,
            };
        }
    }

    // Her face right now, cropped from whatever frame her body is showing, so the bubble feels like a call with her.
    internal sealed class Portrait : Control
    {
        internal Func<(Clip clip, int cell, Clip mouth, int mouthCell)> Frame;
        // A circle by default; square for the mission cards.
        internal bool Round = true;
        private readonly DispatcherTimer refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };

        internal Portrait(double size)
        {
            Width = Height = size;
            refresh.Tick += delegate { if (IsVisible) InvalidateVisual(); };
            AttachedToVisualTree += delegate { refresh.Start(); };
            DetachedFromVisualTree += delegate { refresh.Stop(); };
        }

        public override void Render(DrawingContext context)
        {
            Rect box = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(255, 30, 24, 36)), box, Round ? (float)(Bounds.Width / 2) : 0f);
            if (Frame == null) return;
            (Clip clip, int cell, Clip mouth, int mouthCell) = Frame();
            if (clip == null) return;
            // The top of the cell, where her head is: a square as wide as the cell, scaled to fill the circle.
            double cropTall = clip.Wide * 0.95;
            Rect source = new Rect(cell * clip.Wide, clip.Tall * 0.02, clip.Wide, cropTall);
            Rect dest = new Rect(box.X - box.Width * 0.08, box.Y - box.Height * 0.02, box.Width * 1.16, box.Height * 1.16 * cropTall / clip.Wide);
            using (context.PushGeometryClip(Round ? (Geometry)new EllipseGeometry(box) : new RectangleGeometry(box)))
            {
                context.DrawImage(clip.Art(false), source, dest);
                if (mouth != null && mouthCell >= 0)
                    context.DrawImage(mouth.Art(false), new Rect(mouthCell * mouth.Wide, mouth.Tall * 0.02, mouth.Wide, cropTall), dest);
            }
            if (Round) context.DrawEllipse(null, new Pen(TalkLook.AccentBrush, 2), box.Center, box.Width / 2 - 1, box.Height / 2 - 1);
        }
    }

    // A one-line box under her, in the terminal manner of the base's composer: "> you:" then what you type. Enter sends, Escape closes.
    internal sealed class Typer : Window
    {
        internal const double Tall = 44;
        private readonly TextBox box = new TextBox { Width = 320, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        internal event Action<string> Sent;

        internal Typer()
        {
            TalkLook.Floating(this);
            ShowActivated = true;
            SizeToContent = SizeToContent.Width;
            Height = Tall;
            TalkLook.Bare(box);
            // The prompt lives inside the box, so the placeholder starts after it and nothing overlaps.
            box.InnerLeftContent = TalkLook.Prompt(15);
            box.Watermark = "say something";
            Border row = new Border { Margin = new Thickness(14, 0, 14, 0), Child = box, VerticalAlignment = VerticalAlignment.Center };
            Content = new Border { Background = TalkLook.Plate, BorderBrush = TalkLook.PlateEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Height = Tall, Child = row, BoxShadow = TalkLook.Shadow, Margin = new Thickness(0, 0, 0, 0) };
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
            // Losing focus closes it, except in the moment right after opening, when a closing menu can steal focus.
            Deactivated += delegate { if ((DateTime.Now - openedAt).TotalMilliseconds > 400) Hide(); else Activate(); };
        }

        private DateTime openedAt = DateTime.MinValue;

        internal void Open(PixelPoint at)
        {
            Position = at;
            openedAt = DateTime.Now;
            Show();
            Activate();
            box.Focus();
        }
    }

    // Her reply above her head, typed in a few letters at a time, then left up long enough to read. Laid out like a call:
    // her live face, her name, how she feels, and the line.
    internal sealed class Bubble : Window
    {
        private readonly TextBlock text = new TextBlock { MaxWidth = 300, TextWrapping = TextWrapping.Wrap, Foreground = TalkLook.Ink, FontSize = 14, LineHeight = 21 };
        private readonly TextBlock name = new TextBlock { Foreground = TalkLook.Ink, FontSize = 12, FontFamily = TalkLook.Mono, Text = "SOLIN D'ARC" };
        private readonly TextBlock tag = new TextBlock { Foreground = TalkLook.Faint, FontSize = 11, FontFamily = TalkLook.Mono, Text = "live" };
        private readonly Border tagPill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1), Background = new SolidColorBrush(Color.FromArgb(60, 232, 72, 120)), VerticalAlignment = VerticalAlignment.Center };
        private readonly Portrait face = new Portrait(44);
        private readonly DispatcherTimer typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(28) };
        private readonly DispatcherTimer linger = new DispatcherTimer();
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private string full = "";
        private int shown;
        private Func<PixelPoint> anchor;

        // True from the first letter until the bubble goes; the sprite moves her mouth while it is.
        internal event Action<bool> Speaking;

        internal Func<(Clip clip, int cell, Clip mouth, int mouthCell)> Frame { set { face.Frame = value; } }

        internal Bubble()
        {
            TalkLook.Floating(this);
            ShowActivated = false;
            IsHitTestVisible = false;
            tagPill.Child = tag;
            StackPanel header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            StackPanel names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(name);
            names.Children.Add(tagPill);
            header.Children.Add(face);
            header.Children.Add(names);
            StackPanel stack = new StackPanel { Spacing = 8, Margin = new Thickness(14, 10, 16, 12) };
            stack.Children.Add(header);
            stack.Children.Add(new Border { Height = 1, Background = TalkLook.Edge, Margin = new Thickness(0, 0, 0, 2) });
            stack.Children.Add(text);
            Grid body = new Grid();
            body.Children.Add(stack);
            // A thin accent down the left edge, like a call that is connected.
            body.Children.Add(new Border { Width = 3, Background = TalkLook.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(14, 0, 0, 14) });
            Content = TalkLook.Card(body);
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
            follow.Tick += delegate { if (IsVisible) Place(); else follow.Stop(); };
        }

        // Called when she moves, so the bubble stays over her head.
        internal void Reposition() { if (IsVisible) Place(); }

        // Where the bubble's bottom-centre should sit, in screen pixels; re-read every frame so it follows her when dragged.
        internal void Follow(Func<PixelPoint> anchorPoint) { anchor = anchorPoint; }

        internal void Say(string reply, string mood)
        {
            full = reply ?? "";
            shown = 0;
            text.Text = "";
            tag.Text = string.IsNullOrEmpty(mood) || mood == "neutral" ? "live" : "feeling " + mood;
            linger.Stop();
            Place();
            Show();
            follow.Start();
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

    // One short line over her head: what the coding agent is doing right now, as a tag: a red "CLAUDE" chip and the action.
    internal sealed class Pill : Window
    {
        private readonly TextBlock text = new TextBlock { Foreground = TalkLook.Ink, FontSize = 12, FontFamily = TalkLook.Mono, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock chip = new TextBlock { Foreground = Brushes.White, FontSize = 9, FontFamily = TalkLook.Mono, Text = "CLAUDE", VerticalAlignment = VerticalAlignment.Center };
        private readonly DispatcherTimer linger = new DispatcherTimer();
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private Func<PixelPoint> anchor;

        internal Pill()
        {
            TalkLook.Floating(this);
            ShowActivated = false;
            IsHitTestVisible = false;
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(255, 33, 82)), CornerRadius = new CornerRadius(2), Padding = new Thickness(5, 1), Child = chip, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(text);
            Content = new Border { Background = TalkLook.Plate, BorderBrush = TalkLook.PlateEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 4, 10, 4), Child = row, BoxShadow = TalkLook.Shadow, Margin = new Thickness(8, 4, 8, 14) };
            linger.Tick += delegate { linger.Stop(); Hide(); };
            // Follows her every few frames while shown, so teleports, walks and drags never leave it behind.
            follow.Tick += delegate { if (IsVisible) Reposition(); else follow.Stop(); };
        }

        internal void Follow(Func<PixelPoint> anchorPoint) { anchor = anchorPoint; }

        // Shows the line; with a lifetime it fades by itself, otherwise it stays until the next line or Clear().
        internal void Say(string line, double seconds = 0, string who = "CLAUDE")
        {
            text.Text = line;
            chip.Text = who;
            linger.Stop();
            Show();
            Reposition();
            follow.Start();
            if (seconds > 0) { linger.Interval = TimeSpan.FromSeconds(seconds); linger.Start(); }
        }

        internal void Clear() { linger.Stop(); follow.Stop(); Hide(); }

        internal void Reposition()
        {
            if (anchor == null || !IsVisible) return;
            PixelPoint at = anchor();
            double scale = Screens.Primary?.Scaling ?? 1;
            int wide = (int)(Bounds.Width * scale), tall = (int)(Bounds.Height * scale);
            if (wide == 0) return;
            PixelPoint want = new PixelPoint(at.X - wide / 2, at.Y - tall);
            // Kept inside the screen she is on.
            Screen screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                want = new PixelPoint(Math.Clamp(want.X, area.X, Math.Max(area.X, area.Right - wide)), Math.Max(area.Y, want.Y));
            }
            if (want != Position) Position = want;
        }
    }
}
