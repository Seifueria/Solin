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
            Spaces.ClickThrough(this);
            frames.Tick += delegate { Fit(); };
        }

        // Starts pointing from the hand to the rectangle; both are re-read every tick so they can move.
        internal void Point(Func<PixelPoint> handPoint, Func<PixelRect?> targetRect, bool withBolt = true, string label = "CLAUDE")
        {
            hand = handPoint; target = targetRect;
            canvas.Label = label;
            if (Debug) Console.WriteLine("[point] start " + label + " " + DateTime.Now.ToString("HH:mm:ss.fff"));
            if (!Fit()) return;
            if (withBolt) canvas.Bolt(0.7);
            if (!IsVisible) Show();
            frames.Start();
        }

        // Told where the target is each tick, so she can turn toward it; null when pointing stops.
        internal Action<int?> Aiming;

        internal static readonly bool Debug = Environment.GetEnvironmentVariable("SOLIN_DEBUG_POINT") == "1";
        internal void Stop()
        {
            if (Debug && IsVisible) Console.WriteLine("[point] stop " + DateTime.Now.ToString("HH:mm:ss.fff") + "\n" + Environment.StackTrace);
            frames.Stop(); Hide(); Aiming?.Invoke(null);
        }

        // Sizes the window to cover hand and target with a margin, and hands the canvas their local coordinates.
        private bool Fit()
        {
            PixelRect? t = target?.Invoke();
            if (hand == null || t == null) { if (Debug) Console.WriteLine("[point] no target"); Stop(); return false; }
            PixelPoint h = hand();
            PixelRect box = t.Value;
            Aiming?.Invoke(box.Center.X);
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
        internal string Label = "CLAUDE";
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        // A bolt of lightning along the line for its first moments, then the dashed line takes over.
        internal double BoltUntil = -1;
        private readonly Random random = new Random();
        internal void Bolt(double seconds) { BoltUntil = clock.Elapsed.TotalSeconds + seconds; }
        private static readonly Color Red = Color.FromRgb(255, 33, 82), Violet = Color.FromRgb(123, 61, 255);

        public override void Render(DrawingContext context)
        {
            double t = clock.Elapsed.TotalSeconds;
            // The nearest point on the target's edge to her hand.
            Point aim = new Point(Math.Clamp(Hand.X, Target.Left, Target.Right), Math.Clamp(Hand.Y, Target.Top, Target.Bottom));
            if (Target.Contains(Hand))
            {
                // Her hand is over the window: aim at its nearest edge instead, so there is a line to draw.
                double l = Hand.X - Target.Left, r = Target.Right - Hand.X, tp = Hand.Y - Target.Top, b = Target.Bottom - Hand.Y;
                double m = Math.Min(Math.Min(l, r), Math.Min(tp, b));
                aim = m == l ? new Point(Target.Left, Hand.Y) : m == r ? new Point(Target.Right, Hand.Y) : m == tp ? new Point(Hand.X, Target.Top) : new Point(Hand.X, Target.Bottom);
            }
            if (t < BoltUntil)
            {
                // Jagged lightning from hand to window, redrawn every frame so it crackles: a red core with violet and white edges.
                Vector step = (aim - Hand) / 9;
                Vector side = new Vector(-step.Y, step.X);
                double norm = Math.Max(1, Math.Sqrt(side.X * side.X + side.Y * side.Y));
                side /= norm;
                Point[] pts = new Point[10];
                pts[0] = Hand; pts[9] = aim;
                for (int i = 1; i < 9; i++) pts[i] = Hand + step * i + side * ((random.NextDouble() - 0.5) * 34);
                foreach ((Color c, double w) in new[] { (Violet, 7.0), (Red, 4.0), (Color.FromRgb(255, 244, 246), 1.6) })
                    for (int i = 0; i < 9; i++) context.DrawLine(new Pen(new SolidColorBrush(c), w, lineCap: PenLineCap.Round), pts[i], pts[i + 1]);
                // A reticle around the hand.
                context.DrawEllipse(null, new Pen(new SolidColorBrush(Red), 2), Hand, 26, 26);
            }
            else
            {
                // Dashed line, dashes marching toward the window.
                Pen dashed = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 244, 246)), 2, new DashStyle(new double[] { 6, 5 }, -(t * 14) % 11));
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 123, 61, 255)), 5), Hand, aim);
                context.DrawLine(dashed, Hand, aim);
            }
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
            FormattedText label = new FormattedText(Label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(TalkLook.Mono), 11, Brushes.White);
            Rect chip = Target.Top - label.Height - 12 < 0
                ? new Rect(Target.Left - label.Width - 22, Target.Bottom + 8, label.Width + 14, label.Height + 6)
                : new Rect(Target.Left, Target.Top - label.Height - 12, label.Width + 14, label.Height + 6);
            context.FillRectangle(new SolidColorBrush(Red), chip, 2);
            context.DrawText(label, new Point(chip.X + 7, chip.Y + 3));
        }
    }
}
