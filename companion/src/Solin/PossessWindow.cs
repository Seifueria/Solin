// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // She sinks into a window and wears it for a few seconds: its frame goes jagged, glitch bars run across it, then it grows
    // eyes that follow your cursor and a zigzag mouth. Drawn over the window, click-through; the window itself is untouched.
    // (Idea described from mochiiwuff/capri-desktop's window possession; no code or art taken; Solin's own colours.)
    internal sealed class PossessWindow : Window
    {
        private readonly PossessCanvas canvas = new PossessCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private Func<PixelRect?> target;
        internal event Action Finished;
        internal bool Running { get { return frames.IsEnabled; } }

        internal PossessWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            Content = canvas;
            frames.Tick += delegate
            {
                PixelRect? box = target?.Invoke();
                if (box == null || !canvas.Step()) { frames.Stop(); Hide(); Finished?.Invoke(); return; }
                Fit(box.Value);
            };
        }

        private const int Pad = 30;

        private void Fit(PixelRect box)
        {
            double scale = Screens.ScreenFromPoint(box.Center)?.Scaling ?? Screens.Primary?.Scaling ?? 1;
            PixelPoint want = new PixelPoint(box.X - Pad, box.Y - Pad);
            if (want != Position) Position = want;
            Width = (box.Width + Pad * 2) / scale; Height = (box.Height + Pad * 2) / scale;
            canvas.Box = new Rect(Pad / scale, Pad / scale, box.Width / scale, box.Height / scale);
            canvas.Origin = new PixelPoint(box.X - Pad, box.Y - Pad);
            canvas.Scale = scale;
        }

        // targetRect is re-read every frame, so the possession follows the window if it moves; null ends it early.
        internal void Possess(Func<PixelRect?> targetRect, double entryX, double seconds = 7)
        {
            target = targetRect;
            PixelRect? box = target();
            if (box == null) return;
            Fit(box.Value);
            canvas.Begin(entryX, seconds);
            if (!IsVisible) Show();
            Spaces.Front(this);
            frames.Start();
        }

        internal void Release() { canvas.Leave(); }
        internal bool Talking { set { canvas.Talking = value; } }
    }

    internal sealed class PossessCanvas : Control
    {
        internal Rect Box;
        internal PixelPoint Origin;
        internal double Scale = 1;
        internal bool Talking;
        private readonly Random random = new Random();
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private double length, entry, leaveAt = -1;
        private readonly List<(double x, double y, double r, double delay)> eyes = new List<(double, double, double, double)>();
        private double nextBlink, blinkAt = -10;

        private static readonly Color Pink = Color.FromRgb(232, 72, 120), Cyan = Color.FromRgb(58, 214, 235), Violet = Color.FromRgb(123, 61, 255), Paper = Color.FromRgb(255, 244, 246), Ink = Color.FromRgb(12, 9, 16), Green = Color.FromRgb(54, 255, 122);

        internal void Begin(double entryX, double seconds)
        {
            entry = entryX; length = seconds; leaveAt = -1;
            eyes.Clear();
            // Two big eyes a little above centre, then a handful of small ones scattered around them.
            eyes.Add((0.42, 0.42, 0.11, 1.15));
            eyes.Add((0.60, 0.40, 0.11, 1.2));
            for (int i = 0; i < 7; i++) eyes.Add((0.12 + random.NextDouble() * 0.76, 0.12 + random.NextDouble() * 0.7, 0.025 + random.NextDouble() * 0.03, 1.4 + i * 0.12));
            nextBlink = 2.5;
            clock.Restart();
        }

        internal void Leave() { if (leaveAt < 0) leaveAt = clock.Elapsed.TotalSeconds; }

        internal bool Step()
        {
            double t = clock.Elapsed.TotalSeconds;
            if (leaveAt < 0 && t > length) leaveAt = t;
            if (t > nextBlink) { blinkAt = t; nextBlink = t + 2 + random.NextDouble() * 3; }
            InvalidateVisual();
            return leaveAt < 0 || t - leaveAt < 0.45;
        }

        private static IBrush Tint(Color c, double a) { return new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B)); }
        private static double Ease(double x) { x = Math.Clamp(x, 0, 1); return 1 - Math.Pow(1 - x, 3); }

        public override void Render(DrawingContext g)
        {
            double t = clock.Elapsed.TotalSeconds;
            double fade = leaveAt < 0 ? 1 : Math.Max(0, 1 - (t - leaveAt) / 0.45);
            double sinkX = Box.X + Math.Clamp((entry - Origin.X) / Scale, Box.Width * 0.1, Box.Width * 0.9);
            // 0 to 0.45 s: she pours down into the title bar: vertical streaks and a few chips at the entry point.
            if (t < 0.6)
            {
                double k = t / 0.6;
                for (int i = 0; i < 4; i++)
                {
                    double x = sinkX + (i - 1.5) * 5;
                    g.DrawLine(new Pen(Tint(i % 2 == 0 ? Violet : Pink, 1 - k), 3), new Point(x, Box.Y - 60 * (1 - k)), new Point(x, Box.Y + 18 * k));
                }
                for (int i = 0; i < 8; i++)
                {
                    double a = random.NextDouble() * Math.PI * 2, r = 20 + k * 40;
                    g.FillRectangle(Tint(i % 2 == 0 ? Green : Violet, 1 - k), new Rect(sinkX + Math.Cos(a) * r, Box.Y + Math.Sin(a) * r * 0.5 - 10, 6, 3));
                }
            }
            // From 0.3 s: the frame goes jagged, in pink and violet, drawn in around the window.
            double frameIn = Ease((t - 0.3) / 0.7) * fade;
            if (frameIn > 0)
            {
                StreamGeometry loop = new StreamGeometry();
                using (StreamGeometryContext c = loop.Open())
                {
                    int n = 40;
                    c.BeginFigure(Box.TopLeft, false);
                    int k = 0;
                    foreach ((Point a, Point b) in new[] { (Box.TopLeft, Box.TopRight), (Box.TopRight, Box.BottomRight), (Box.BottomRight, Box.BottomLeft), (Box.BottomLeft, Box.TopLeft) })
                        for (int i = 1; i <= n; i++) { Point p = a + (b - a) * (i / (double)n); c.LineTo(new Point(p.X + (a.Y == b.Y ? 0 : (k % 2 == 0 ? 4.5 : -4.5)), p.Y + (a.X == b.X ? 0 : (k % 2 == 0 ? 4.5 : -4.5)))); k++; }
                    c.EndFigure(true);
                }
                g.DrawGeometry(null, new Pen(Tint(Violet, frameIn), 6, lineJoin: PenLineJoin.Round), loop);
                g.DrawGeometry(null, new Pen(Tint(Pink, frameIn), 3, lineJoin: PenLineJoin.Round), loop);
                g.DrawGeometry(null, new Pen(Tint(Paper, frameIn * 0.8), 1, lineJoin: PenLineJoin.Round), loop);
            }
            // 0.5 s to 1.6 s, and lightly after: glitch bars across the window.
            double barsOn = t < 1.6 ? Math.Min(1, (t - 0.5) / 0.3) : 0.35;
            if (t > 0.5)
            {
                int count = t < 1.6 ? 7 : 3;
                for (int i = 0; i < count; i++)
                {
                    double y = Box.Y + Box.Height * (0.08 + random.NextDouble() * 0.84), w = Box.Width * (0.15 + random.NextDouble() * 0.45);
                    double x = Box.X + random.NextDouble() * (Box.Width - w);
                    Color c = i % 3 == 0 ? Paper : i % 3 == 1 ? Green : Pink;
                    g.FillRectangle(Tint(c, barsOn * fade * 0.85), new Rect(x, y, w, 2 + random.NextDouble() * 3));
                }
            }
            // From 1.1 s: eyes pop in one by one and follow the cursor; then the zigzag mouth.
            PixelPoint? cursor = Mouse.Position(Scale);
            Point look = cursor.HasValue ? new Point((cursor.Value.X - Origin.X) / Scale, (cursor.Value.Y - Origin.Y) / Scale) : Box.Center;
            double blink = t - blinkAt < 0.14 ? 1 - Math.Abs((t - blinkAt) / 0.07 - 1) : 0;
            double unit = Math.Min(Box.Width, Box.Height * 1.6);
            foreach ((double ex, double ey, double er, double delay) in eyes)
            {
                double pop = Ease((t - delay) / 0.25) * fade;
                if (pop <= 0) continue;
                Point c = new Point(Box.X + Box.Width * ex, Box.Y + Box.Height * ey);
                double r = unit * er * (0.6 + 0.4 * pop) * (1 + 0.15 * Math.Sin(Math.Min(1, (t - delay) / 0.25) * Math.PI));
                Vector toward = look - c;
                double dist = Math.Max(1, Math.Sqrt(toward.X * toward.X + toward.Y * toward.Y));
                Vector shift = toward / dist * r * 0.28;
                g.DrawEllipse(Tint(Ink, pop), new Pen(Tint(Violet, pop), r * 0.16), new Point(c.X + r * 0.06, c.Y + r * 0.06), r, r);
                g.DrawEllipse(null, new Pen(Tint(Pink, pop), r * 0.14), c, r, r);
                double open = 1 - blink;
                g.DrawEllipse(Tint(Cyan, pop), null, c + shift, r * 0.52, r * 0.52 * Math.Max(0.08, open));
                g.DrawEllipse(Tint(Paper, pop), null, c + shift + new Vector(r * 0.1, -r * 0.12 * open), r * 0.18, r * 0.18 * Math.Max(0.08, open));
            }
            double mouthIn = Ease((t - 1.5) / 0.3) * fade;
            if (mouthIn > 0)
            {
                // A zigzag grin under the big eyes; it chatters while she talks.
                double mx = Box.X + Box.Width * 0.51, my = Box.Y + Box.Height * 0.62, half = Box.Width * 0.12 * mouthIn;
                int teeth = 4;
                double open = Talking ? 0.5 + 0.5 * Math.Abs(Math.Sin(t * 14)) : 0.35;
                StreamGeometry zig = new StreamGeometry();
                using (StreamGeometryContext c = zig.Open())
                {
                    c.BeginFigure(new Point(mx - half, my), false);
                    for (int i = 1; i <= teeth * 2; i++)
                        c.LineTo(new Point(mx - half + half * 2 * i / (teeth * 2), my + (i % 2 == 1 ? half * 0.45 * open : 0)));
                    c.EndFigure(false);
                }
                g.DrawGeometry(null, new Pen(Tint(Violet, mouthIn), 9, lineJoin: PenLineJoin.Miter), zig);
                g.DrawGeometry(null, new Pen(Tint(Paper, mouthIn), 5, lineJoin: PenLineJoin.Miter), zig);
                // A couple of red scan lines across the face, like the reference.
                g.FillRectangle(Tint(Pink, mouthIn * 0.8), new Rect(Box.X + Box.Width * 0.62, Box.Y + Box.Height * 0.18, Box.Width * 0.32, 3));
                g.FillRectangle(Tint(Pink, mouthIn * 0.8), new Rect(Box.X + Box.Width * 0.6, Box.Y + Box.Height * 0.25, Box.Width * 0.34, 3));
            }
        }
    }
}
