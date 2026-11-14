// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // A dashed line from her hand to a window, corner brackets on it and a CLAUDE chip: "the question is over there".
    // (Look described from mochiiwuff/capri-desktop; no code taken.)
    internal sealed class PointerWindow : Window
    {
        private readonly PointerCanvas canvas = new PointerCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private Func<PixelPoint> hand;
        private Func<PixelRect?> target;

        internal PointerWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            ShowActivated = false;
            IsHitTestVisible = false;
            Content = canvas;
            frames.Tick += delegate { Fit(); };
        }

        // Starts pointing from the hand to the rectangle; both are re-read every tick so they can move.
        internal void Point(Func<PixelPoint> handPoint, Func<PixelRect?> targetRect)
        {
            hand = handPoint; target = targetRect;
            if (!Fit()) return;
            if (!IsVisible) Show();
            frames.Start();
        }

        internal void Stop() { frames.Stop(); Hide(); }

        // Sizes the window to cover hand and target with a margin, and hands the canvas their local coordinates.
        private bool Fit()
        {
            PixelRect? t = target?.Invoke();
            if (hand == null || t == null) { Stop(); return false; }
            PixelPoint h = hand();
            PixelRect box = t.Value;
            int pad = 60;
            int left = Math.Min(h.X, box.X) - pad, top = Math.Min(h.Y, box.Y) - pad;
            int right = Math.Max(h.X, box.Right) + pad, bottom = Math.Max(h.Y, box.Bottom) + pad;
            double scale = Screens.Primary?.Scaling ?? 1;
            Position = new PixelPoint(left, top);
            Width = (right - left) / scale; Height = (bottom - top) / scale;
            canvas.Hand = new Point((h.X - left) / scale, (h.Y - top) / scale);
            canvas.Target = new Rect((box.X - left) / scale, (box.Y - top) / scale, box.Width / scale, box.Height / scale);
            canvas.InvalidateVisual();
            return true;
        }
    }

    internal sealed class PointerCanvas : Control
    {
        internal Point Hand;
        internal Rect Target;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private static readonly Color Red = Color.FromRgb(255, 33, 82), Violet = Color.FromRgb(123, 61, 255);

        public override void Render(DrawingContext context)
        {
            double t = clock.Elapsed.TotalSeconds;
            // The nearest point on the target's edge to her hand.
            Point aim = new Point(Math.Clamp(Hand.X, Target.Left, Target.Right), Math.Clamp(Hand.Y, Target.Top, Target.Bottom));
            // Dashed line, dashes marching toward the window.
            Pen dashed = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 244, 246)), 2, new DashStyle(new double[] { 6, 5 }, -(t * 14) % 11));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 123, 61, 255)), 5), Hand, aim);
            context.DrawLine(dashed, Hand, aim);
            // Arrow head at the window.
            Vector dir = aim - Hand;
            double len = Math.Max(1, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
            Vector u = dir / len, n = new Vector(-u.Y, u.X);
            StreamGeometry head = new StreamGeometry();
            using (StreamGeometryContext g = head.Open())
            {
                g.BeginFigure(aim, true); g.LineTo(aim - u * 14 + n * 7); g.LineTo(aim - u * 14 - n * 7); g.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(Red), null, head);
            // Brackets on the window's corners, breathing a little.
            double arm = 26 + Math.Sin(t * 3) * 3, thick = 3;
            Pen bracket = new Pen(new SolidColorBrush(Violet), thick);
            Pen bracketHot = new Pen(new SolidColorBrush(Red), thick);
            foreach ((Point c, double dx, double dy, Pen pen) in new[] { (Target.TopLeft, 1.0, 1.0, bracketHot), (Target.TopRight, -1.0, 1.0, bracket), (Target.BottomLeft, 1.0, -1.0, bracket), (Target.BottomRight, -1.0, -1.0, bracketHot) })
            {
                context.DrawLine(pen, c, new Point(c.X + dx * arm, c.Y));
                context.DrawLine(pen, c, new Point(c.X, c.Y + dy * arm));
            }
            // The CLAUDE chip, just above the top-left corner.
            FormattedText label = new FormattedText("CLAUDE", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(TalkLook.Mono), 11, Brushes.White);
            Rect chip = new Rect(Target.Left, Target.Top - label.Height - 12, label.Width + 14, label.Height + 6);
            context.FillRectangle(new SolidColorBrush(Red), chip, 2);
            context.DrawText(label, new Point(chip.X + 7, chip.Y + 3));
        }
    }
}
