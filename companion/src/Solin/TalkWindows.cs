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

        // What she says, as shown: her voice tags like [soft sigh] and any emoji taken out, spaces tidied.
        internal static string Spoken(string text)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\[[^\]]*\]", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\p{Cs}|[\u2600-\u27BF\uFE0F\u200D\u2B50\u2764]", "");
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ").Trim();
        }

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
            Spaces.Everywhere(window);
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
        // A still face to show instead of the live crop, when there is one (reference art for her mood).
        internal Func<Avalonia.Media.Imaging.Bitmap> Still;
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
            Avalonia.Media.Imaging.Bitmap still = Still?.Invoke();
            if (still != null)
            {
                // Cover the box from the middle of the picture.
                double side = Math.Min(still.PixelSize.Width, still.PixelSize.Height);
                Rect from = new Rect((still.PixelSize.Width - side) / 2, (still.PixelSize.Height - side) / 2, side, side);
                using (context.PushGeometryClip(Round ? (Geometry)new EllipseGeometry(box) : new RectangleGeometry(box)))
                    context.DrawImage(still, from, box);
                if (Round) context.DrawEllipse(null, new Pen(TalkLook.AccentBrush, 2), box.Center, box.Width / 2 - 1, box.Height / 2 - 1);
                return;
            }
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

    // The typing box over her head, as in the base's composer: a black cut-corner plate with a cyan edge, a red bar, and
    // "> you:" then what you type. Enter sends and keeps it open for the next line; Escape or clicking away closes it.
    internal sealed class Typer : Window
    {
        internal const double Tall = 52, Wide = 340;
        private readonly TextBox box = new TextBox { FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private Func<PixelPoint> anchor;
        internal event Action<string> Sent;

        internal Typer()
        {
            TalkLook.Floating(this);
            ShowActivated = true;
            SizeToContent = SizeToContent.Manual;
            Width = Wide; Height = Tall;
            TalkLook.Bare(box);
            box.CaretBrush = new SolidColorBrush(CutFrame.Cyan);
            box.InnerLeftContent = TalkLook.Prompt(15);
            Grid layers = new Grid();
            layers.Children.Add(new CutFrame());
            layers.Children.Add(new Border { Margin = new Thickness(30, 0, 16, 0), Child = box, VerticalAlignment = VerticalAlignment.Center });
            Content = layers;
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
                if (e.Key == Key.Enter)
                {
                    string text = (box.Text ?? "").Trim();
                    box.Text = "";
                    if (text.Length > 0) Sent?.Invoke(text);
                    e.Handled = true;
                }
            };
            // Losing focus closes it, except in the moment right after opening, when a closing menu can steal focus.
            Deactivated += delegate { if ((DateTime.Now - openedAt).TotalMilliseconds > 400) Hide(); else Activate(); };
            follow.Tick += delegate { if (IsVisible) Place(); else follow.Stop(); };
        }

        private DateTime openedAt = DateTime.MinValue;

        // Where its bottom-centre goes, in screen pixels: over her head, or over her reply when one is up.
        internal void Follow(Func<PixelPoint> bottomCentre) { anchor = bottomCentre; }

        private void Place()
        {
            if (anchor == null) return;
            PixelPoint at = anchor();
            Screen screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
            double scale = screen?.Scaling ?? 1;
            int wide = (int)(Wide * scale), tall = (int)(Tall * scale);
            PixelPoint want = new PixelPoint(at.X - wide / 2, at.Y - tall);
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                want = new PixelPoint(Math.Clamp(want.X, area.X, Math.Max(area.X, area.Right - wide)), Math.Clamp(want.Y, area.Y, Math.Max(area.Y, area.Bottom - tall)));
            }
            if (want != Position) Position = want;
        }

        internal void Open()
        {
            openedAt = DateTime.Now;
            Place();
            Show();
            follow.Start();
            // She runs in the background, so the app has to be brought forward for the box to take the keyboard.
            Spaces.TakeKeyboard(this);
            Activate();
            box.Focus();
            // Activation can land a moment late; once more, if it has not.
            DispatcherTimer.RunOnce(() => { if (IsVisible) { Spaces.TakeKeyboard(this); box.Focus(); } }, TimeSpan.FromMilliseconds(150));
        }
    }

    // Her reply over her head, in the same plate as the typing box with a tail down to her: your line ("> you: ...") when it
    // answers one, then SOLIN: and her words typed out a few letters at a time behind a cyan block, then left up to read.
    internal sealed class Bubble : Window
    {
        private const double Wide = 340;
        private readonly TextBlock you = new TextBlock { MaxWidth = Wide - 50, TextWrapping = TextWrapping.Wrap, FontFamily = TalkLook.Mono, FontStyle = FontStyle.Italic, FontSize = 13, Foreground = TalkLook.Dim, IsVisible = false };
        private readonly TextBlock line = new TextBlock { MaxWidth = Wide - 50, TextWrapping = TextWrapping.Wrap, FontFamily = TalkLook.Mono, FontSize = 14, LineHeight = 20 };
        private readonly Avalonia.Controls.Documents.Run said = new Avalonia.Controls.Documents.Run { Foreground = TalkLook.Ink };
        private readonly Avalonia.Controls.Documents.Run caret = new Avalonia.Controls.Documents.Run { Text = " \u2588", Foreground = new SolidColorBrush(CutFrame.Cyan) };
        private readonly Portrait face = new Portrait(44);
        private readonly DispatcherTimer typing = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(28) };
        private readonly DispatcherTimer linger = new DispatcherTimer();
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private string full = "";
        private int shown;
        private Func<PixelPoint> anchor;
        private const double TailHigh = 14;

        // True from the first letter until the line is all out; the sprite moves her mouth while it is.
        internal event Action<bool> Speaking;

        // Kept for callers; the plate no longer shows her portrait.
        internal Func<(Clip clip, int cell, Clip mouth, int mouthCell)> Frame { set { face.Frame = value; } }
        internal Func<Avalonia.Media.Imaging.Bitmap> Still { set { face.Still = value; } }

        internal Bubble()
        {
            TalkLook.Floating(this);
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            line.Inlines = new Avalonia.Controls.Documents.InlineCollection
            {
                new Avalonia.Controls.Documents.Run { Text = "SOLIN: ", Foreground = new SolidColorBrush(CutFrame.Red) }, said, caret,
            };
            StackPanel stack = new StackPanel { Spacing = 4, Margin = new Thickness(30, 11, 18, 11 + TailHigh), Width = Wide - 48 };
            stack.Children.Add(you);
            stack.Children.Add(line);
            Grid layers = new Grid { Width = Wide };
            layers.Children.Add(new CutFrame { Tail = TailHigh });
            layers.Children.Add(stack);
            Content = layers;
            typing.Tick += delegate
            {
                shown = Math.Min(full.Length, shown + 2);
                said.Text = full.Substring(0, shown);
                caret.Text = " \u2588";
                Place();
                if (shown >= full.Length)
                {
                    typing.Stop();
                    Quiet();
                    linger.Interval = TimeSpan.FromSeconds(Math.Clamp(2 + full.Length / 18.0, 3, 14));
                    linger.Start();
                }
            };
            linger.Tick += delegate { linger.Stop(); Hide(); };
            // The block blinks while it waits.
            follow.Tick += delegate
            {
                if (!IsVisible) { follow.Stop(); return; }
                Place();
                if (!typing.IsEnabled) caret.Text = DateTime.Now.Millisecond < 500 ? " \u2588" : "  ";
            };
            // Hidden in the middle of a line (put away, a teleport, a new card): the mouth stops with it.
            PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == IsVisibleProperty && !IsVisible) { typing.Stop(); Quiet(); } };
        }

        private bool speaking;
        private void Quiet() { if (!speaking) return; speaking = false; Speaking?.Invoke(false); }

        // Called when she moves, so the bubble stays over her head.
        internal void Reposition() { if (IsVisible) Place(); }

        // Where the bubble's bottom-centre should sit, in screen pixels; re-read every frame so it follows her when dragged.
        internal void Follow(Func<PixelPoint> anchorPoint) { anchor = anchorPoint; }

        // The top-centre of the plate in screen pixels, for the typing box to sit on.
        internal PixelPoint Top { get { double scale = Screens.Primary?.Scaling ?? 1; return new PixelPoint(Position.X + (int)(Bounds.Width * scale / 2), Position.Y); } }

        // youSaid: your line this answers, shown above it; null for things she says on her own.
        // The last line she said here, for checks.
        internal string LastText { get; private set; }

        internal void Say(string reply, string mood, string youSaid = null)
        {
            LastText = reply;
            full = TalkLook.Spoken(reply);
            shown = 0;
            said.Text = "";
            you.Text = string.IsNullOrWhiteSpace(youSaid) ? "" : "> you: " + youSaid.Trim();
            you.IsVisible = !string.IsNullOrWhiteSpace(youSaid);
            linger.Stop();
            Place();
            Show();
            follow.Start();
            if (!speaking) { speaking = true; Speaking?.Invoke(true); }
            typing.Start();
        }

        private void Place()
        {
            if (anchor == null) return;
            PixelPoint at = anchor();
            Screen screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
            double scale = screen?.Scaling ?? 1;
            int wide = (int)(Bounds.Width * scale), tall = (int)(Bounds.Height * scale);
            // The tail hangs at TailAt of the width; that point goes over her head.
            PixelPoint want = new PixelPoint(at.X - (int)(wide * 0.62) + (int)(6 * scale), at.Y - tall);
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                want = new PixelPoint(Math.Clamp(want.X, area.X, Math.Max(area.X, area.Right - wide)), Math.Max(area.Y, want.Y));
            }
            if (want != Position) Position = want;
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
            Spaces.ClickThrough(this);
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(255, 33, 82)), CornerRadius = new CornerRadius(2), Padding = new Thickness(5, 1), Child = chip, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(text);
            Content = new Border { Background = TalkLook.Plate, BorderBrush = TalkLook.PlateEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 4, 10, 4), Child = row, BoxShadow = TalkLook.Shadow, Margin = new Thickness(8, 4, 8, 14) };
            linger.Tick += delegate { linger.Stop(); wanted = false; Hide(); };
            // Follows her every few frames while wanted, so teleports, walks and drags never leave it behind; while
            // something bigger is talking (a card, her bubble) it steps aside and comes back after if it is still current.
            follow.Tick += delegate
            {
                if (!wanted) { Hide(); follow.Stop(); return; }
                bool held = Hold?.Invoke() ?? false;
                if (held && IsVisible) Hide();
                else if (!held && !IsVisible) Show();
                if (IsVisible) Reposition();
            };
        }

        // True while a bigger text box is showing, so only one thing talks at a time.
        internal Func<bool> Hold;
        private bool wanted;

        internal void Follow(Func<PixelPoint> anchorPoint) { anchor = anchorPoint; }

        // Shows the line; with a lifetime it fades by itself, otherwise it stays until the next line or Clear().
        internal void Say(string line, double seconds = 0, string who = "CLAUDE")
        {
            text.Text = line;
            chip.Text = who;
            linger.Stop();
            wanted = true;
            if (!(Hold?.Invoke() ?? false)) { Show(); Reposition(); } else Hide();
            follow.Start();
            if (seconds > 0) { linger.Interval = TimeSpan.FromSeconds(seconds); linger.Start(); }
        }

        internal void Clear() { linger.Stop(); follow.Stop(); wanted = false; Hide(); }

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
