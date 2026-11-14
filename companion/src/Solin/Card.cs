// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Solin.Companion.Upstream;

namespace Solin.Companion
{
    // The dark angled panel behind a card, with a tail toward her and a bright edge. (Look described from mochiiwuff/capri-desktop's cards; no code taken.)
    internal sealed class CardPlate : Control
    {
        internal IBrush Edge = TalkLook.AccentBrush;
        private const double Notch = 16, Tail = 14;

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height - Tail;
            StreamGeometry shape = new StreamGeometry();
            using (StreamGeometryContext g = shape.Open())
            {
                g.BeginFigure(new Point(Notch, 0), true);
                g.LineTo(new Point(w, 0));
                g.LineTo(new Point(w, h - Notch));
                g.LineTo(new Point(w - Notch, h));
                // The tail, near the right end of the bottom edge, pointing down at her.
                g.LineTo(new Point(w * 0.72 + Tail, h));
                g.LineTo(new Point(w * 0.72, h + Tail));
                g.LineTo(new Point(w * 0.72 - 4, h));
                g.LineTo(new Point(0, h));
                g.LineTo(new Point(0, Notch));
                g.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(242, 18, 13, 24)), new Pen(Edge, 2), shape);
            // A brighter bottom edge, like a lit rim.
            context.DrawLine(new Pen(Edge, 4), new Point(0, h), new Point(w * 0.72 - 4, h));
            context.DrawLine(new Pen(Edge, 4), new Point(w * 0.72 + Tail, h), new Point(w - Notch, h));
        }
    }

    // A mission card: her portrait, "SOLIN / project  TAG", a headline, the line, a stats row, and sometimes a button.
    internal sealed class Card : Window
    {
        private readonly CardPlate plate = new CardPlate();
        private readonly Portrait face = new Portrait(52) { Round = false };
        private readonly TextBlock who = new TextBlock { Text = "SOLIN", FontFamily = TalkLook.Mono, FontSize = 12, Foreground = TalkLook.AccentBrush, FontWeight = FontWeight.Bold };
        private readonly TextBlock project = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 11, Foreground = TalkLook.Dim, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock tag = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(10, 8, 14)), FontWeight = FontWeight.Bold };
        private readonly Border tagChip = new Border { CornerRadius = new CornerRadius(2), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock headline = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TalkLook.Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 330 };
        private readonly TextBlock body = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 12, Foreground = TalkLook.Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 310, LineHeight = 18 };
        private readonly Border bodyBar = new Border { Width = 2, Background = new SolidColorBrush(Color.FromRgb(123, 61, 255)), Margin = new Thickness(0, 2, 10, 2) };
        private readonly TextBlock stats = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(54, 255, 122)), IsVisible = false };
        private readonly Button button = new Button { IsVisible = false, FontFamily = TalkLook.Mono, FontSize = 11, Padding = new Thickness(12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        private readonly DispatcherTimer linger = new DispatcherTimer();
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private Func<PixelPoint> anchor;

        internal static readonly Color Green = Color.FromRgb(54, 255, 122), Red = Color.FromRgb(255, 33, 82), Violet = Color.FromRgb(123, 61, 255);
        internal Func<(Clip clip, int cell, Clip mouth, int mouthCell)> Frame { set { face.Frame = value; } }
        internal event Action Acknowledged;

        internal Card()
        {
            TalkLook.Floating(this);
            ShowActivated = false;
            tagChip.Child = tag;
            StackPanel header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(who);
            header.Children.Add(new TextBlock { Text = "/", FontFamily = TalkLook.Mono, FontSize = 12, Foreground = TalkLook.Dim });
            header.Children.Add(project);
            header.Children.Add(tagChip);
            StackPanel text = new StackPanel { Spacing = 6 };
            text.Children.Add(header);
            text.Children.Add(headline);
            StackPanel bodyRow = new StackPanel { Orientation = Orientation.Horizontal };
            bodyRow.Children.Add(bodyBar);
            bodyRow.Children.Add(body);
            text.Children.Add(bodyRow);
            text.Children.Add(stats);
            text.Children.Add(button);
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(18, 14, 20, 26) };
            row.Children.Add(new Border { Child = face, BorderBrush = TalkLook.AccentBrush, BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Top });
            row.Children.Add(text);
            Grid layers = new Grid { Margin = new Thickness(8, 6, 8, 8) };
            layers.Children.Add(plate);
            layers.Children.Add(row);
            Content = layers;
            IsHitTestVisible = false;
            button.Click += delegate { Hide(); Acknowledged?.Invoke(); };
            linger.Tick += delegate { linger.Stop(); Hide(); };
            follow.Tick += delegate { if (IsVisible) Place(); else follow.Stop(); };
        }

        internal void Follow(Func<PixelPoint> anchorPoint) { anchor = anchorPoint; }

        // tagColour picks the mood of the card: green done, red waiting or error, violet sync.
        internal void Show(string projectName, string tagText, Color tagColour, string title, string line, string statsLine = null, string buttonText = null, double seconds = 0)
        {
            project.Text = string.IsNullOrEmpty(projectName) ? "desk" : projectName.ToUpperInvariant();
            tag.Text = tagText;
            tagChip.Background = new SolidColorBrush(tagColour);
            plate.Edge = new SolidColorBrush(tagColour == Green ? Violet : tagColour);
            headline.Text = title;
            body.Text = line ?? "";
            bodyRow_visible(!string.IsNullOrEmpty(line));
            stats.Text = statsLine == null ? "" : "◆ " + statsLine;
            stats.IsVisible = statsLine != null;
            button.Content = buttonText;
            button.IsVisible = buttonText != null;
            IsHitTestVisible = buttonText != null;
            linger.Stop();
            Show();
            Place();
            follow.Start();
            if (seconds > 0) { linger.Interval = TimeSpan.FromSeconds(seconds); linger.Start(); }
        }

        private void bodyRow_visible(bool on) { body.IsVisible = on; bodyBar.IsVisible = on; }

        internal void Dismiss() { linger.Stop(); Hide(); }

        private void Place()
        {
            if (anchor == null) return;
            PixelPoint at = anchor();
            double scale = Screens.Primary?.Scaling ?? 1;
            int wide = (int)(Bounds.Width * scale), tall = (int)(Bounds.Height * scale);
            if (wide == 0) return;
            // The tail sits at 72% of the width; put that over her head.
            PixelPoint want = new PixelPoint(at.X - (int)(wide * 0.72), at.Y - tall + (int)(10 * scale));
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
