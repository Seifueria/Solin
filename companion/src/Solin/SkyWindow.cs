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
    // Stargazing, across the whole screen she is on: faint constellations fade in above her, a bright star appears and
    // drifts, a dotted trail grows from her hand toward it, then she zaps it with lightning, a burst and two reticles,
    // and the crosshaired star fades. Click-through. (Look described from mochiiwuff/capri-desktop; no code taken.)
    internal sealed class SkyWindow : Window
    {
        private readonly SkyCanvas canvas = new SkyCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        internal event Action Zapped;
        internal bool Running { get { return frames.IsEnabled; } }
        // The star's screen point while the show runs, null after: so she can face it and hold her arm out at it.
        internal Action<PixelPoint?> Aiming;

        internal SkyWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            Content = canvas;
            canvas.Zapped += delegate { Zapped?.Invoke(); };
            frames.Tick += delegate { if (!canvas.Step()) { frames.Stop(); Hide(); Aiming?.Invoke(null); } };
        }

        // hand: where her hand is, in screen pixels; the sky is the screen that point is on.
        // laser: the dotted trail and the zap from her hand; off when she is sitting or lying, where it does not read.
        internal void Gaze(Func<PixelPoint> hand, bool laser = true)
        {
            canvas.Laser = laser;
            PixelPoint h = hand();
            Screen screen = Screens.ScreenFromPoint(h) ?? Screens.Primary;
            if (screen == null) return;
            double scale = screen.Scaling;
            PixelRect area = screen.Bounds;
            Position = area.Position;
            Width = area.Width / scale; Height = area.Height / scale;
            canvas.Begin(new Size(Width, Height), () => { PixelPoint p = hand(); return new Point((p.X - area.X) / scale, (p.Y - area.Y) / scale); });
            Aiming?.Invoke(new PixelPoint(area.X + (int)(canvas.StarX * scale), area.Y + (int)(canvas.StarY * scale)));
            if (!IsVisible) Show();
            Spaces.Front(this);
            frames.Start();
        }
    }

    internal sealed class SkyCanvas : Control
    {
        private readonly Random random = new Random();
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private Size area;
        private Func<Point> hand;
        private readonly List<Point> stars = new List<Point>();
        private readonly List<(int a, int b)> lines = new List<(int, int)>();
        private Point starFrom, starTo;
        internal double StarX { get { return starTo.X; } }
        internal double StarY { get { return starTo.Y; } }
        internal bool Laser = true;
        private bool zapped;
        internal event Action Zapped;

        private const double Fade = 1.6, Trail = 4.4, Zap = 4.6, Hold = 5.6, End = 7.2;
        private static readonly Color Red = Color.FromRgb(255, 33, 82), Violet = Color.FromRgb(123, 61, 255), Paper = Color.FromRgb(255, 244, 246);

        internal void Begin(Size size, Func<Point> handPoint)
        {
            area = size; hand = handPoint; zapped = false;
            stars.Clear(); lines.Clear();
            Point h = hand();
            // A loose constellation above her: a dozen dots, a few joined.
            // The bright star goes on the far side of the screen from her; the constellation hangs over her, leaning away from it.
            bool right = h.X < area.Width / 2;
            Point centre = new Point(Math.Clamp(h.X + (right ? -1 : 1) * (0.02 + random.NextDouble() * 0.1) * area.Width, 80, area.Width - 80), Math.Max(60, h.Y - area.Height * 0.32));
            for (int i = 0; i < 16; i++) stars.Add(new Point(centre.X + (random.NextDouble() - 0.5) * area.Width * 0.22, centre.Y + (random.NextDouble() - 0.5) * area.Height * 0.16));
            for (int i = 0; i < 6; i++) { int a = random.Next(stars.Count), b = random.Next(stars.Count); if (a != b) lines.Add((a, b)); }
            // The bright one: far off on the other side of the screen, drifting a little.
            starFrom = new Point(right ? area.Width * (0.68 + random.NextDouble() * 0.2) : area.Width * (0.12 + random.NextDouble() * 0.2), area.Height * (0.12 + random.NextDouble() * 0.2));
            starTo = new Point(starFrom.X + (right ? -1 : 1) * area.Width * 0.04, starFrom.Y + area.Height * 0.03);
            clock.Restart();
            InvalidateVisual();
        }

        // False once the show is over.
        internal bool Step()
        {
            double t = clock.Elapsed.TotalSeconds;
            if (!zapped && t >= Zap && Laser) { zapped = true; Zapped?.Invoke(); }
            InvalidateVisual();
            return t < End;
        }

        private static IBrush Tint(Color c, double a) { return new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B)); }

        private Point Star(double t) { double k = Math.Clamp(t / Zap, 0, 1); return new Point(starFrom.X + (starTo.X - starFrom.X) * k, starFrom.Y + (starTo.Y - starFrom.Y) * k); }

        private static void Sparkle(DrawingContext g, Point at, double r, IBrush fill)
        {
            // A four-pointed star.
            StreamGeometry s = new StreamGeometry();
            using (StreamGeometryContext c = s.Open())
            {
                c.BeginFigure(new Point(at.X, at.Y - r), true);
                c.LineTo(new Point(at.X + r * 0.22, at.Y - r * 0.22)); c.LineTo(new Point(at.X + r, at.Y)); c.LineTo(new Point(at.X + r * 0.22, at.Y + r * 0.22));
                c.LineTo(new Point(at.X, at.Y + r)); c.LineTo(new Point(at.X - r * 0.22, at.Y + r * 0.22)); c.LineTo(new Point(at.X - r, at.Y)); c.LineTo(new Point(at.X - r * 0.22, at.Y - r * 0.22));
                c.EndFigure(true);
            }
            g.DrawGeometry(fill, null, s);
        }

        public override void Render(DrawingContext g)
        {
            if (hand == null) return;
            double t = clock.Elapsed.TotalSeconds;
            double out_ = t > Hold ? 1 - (t - Hold) / (End - Hold) : 1;
            Point h = hand();
            Point star = Star(t);
            // Constellation: fades in, gently twinkles, fades out before the zap.
            double sky = Math.Min(1, t / Fade) * (t < Zap ? 1 : Math.Max(0, 1 - (t - Zap) / 0.6));
            foreach ((int a, int b) in lines) g.DrawLine(new Pen(Tint(Violet, 0.55 * sky), 1.2), stars[a], stars[b]);
            for (int i = 0; i < stars.Count; i++)
            {
                double tw = 0.6 + 0.4 * Math.Sin(t * 3 + i);
                g.FillRectangle(Tint(Paper, 0.75 * sky * tw), new Rect(stars[i].X - 1.2, stars[i].Y - 1.2, 2.4, 2.4), 1.2f);
            }
            // The bright star.
            double glow = Math.Min(1, t / 0.8) * out_;
            Sparkle(g, star, 9 + 2 * Math.Sin(t * 5), Tint(Paper, glow));
            // Dotted trail from her hand, growing toward the star.
            double trail = Math.Clamp((t - Fade) / (Trail - Fade), 0, 1);
            if (Laser && (trail > 0 && t < Zap + 0.1 || t >= Hold))
            {
                int dots = 34;
                double reach = t >= Hold ? 1 : trail;
                for (int i = 1; i <= dots * reach; i++)
                {
                    double k = i / (double)dots;
                    Point p = new Point(h.X + (star.X - h.X) * k, h.Y + (star.Y - h.Y) * k);
                    double size = 1.2 + 1.6 * (1 - k);
                    g.FillRectangle(Tint(Paper, 0.85 * (t >= Hold ? out_ : 1)), new Rect(p.X - size / 2, p.Y - size / 2, size, size));
                }
            }
            if (Laser && t >= Zap && t < Hold)
            {
                // The zap: a jagged bolt with violet and white edges, a reticle at her hand, a burst and reticle at the star.
                double z = (t - Zap) / (Hold - Zap);
                Vector step = (star - h) / 11;
                Vector side = new Vector(-step.Y, step.X);
                double n = Math.Max(1, Math.Sqrt(side.X * side.X + side.Y * side.Y)); side /= n;
                Point[] pts = new Point[12]; pts[0] = h; pts[11] = star;
                for (int i = 1; i < 11; i++) pts[i] = h + step * i + side * ((i % 2 == 0 ? 1 : -1) * 22 * (0.6 + random.NextDouble() * 0.6));
                foreach ((Color c, double w) in new[] { (Violet, 9.0), (Red, 5.0), (Paper, 2.0) })
                    for (int i = 0; i < 11; i++) g.DrawLine(new Pen(Tint(c, 1 - z * 0.5), w, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), pts[i], pts[i + 1]);
                g.DrawEllipse(null, new Pen(Tint(Violet, 1 - z), 2), h, 70, 70);
                for (int i = 0; i < 26; i++)
                {
                    double a = random.NextDouble() * Math.PI * 2, inner = 36 + z * 30, outer = inner + 40 + random.NextDouble() * 110;
                    Color c = i % 3 == 0 ? Paper : i % 3 == 1 ? Violet : Red;
                    g.DrawLine(new Pen(Tint(c, 1 - z), 2 + random.NextDouble() * 5), new Point(star.X + Math.Cos(a) * inner, star.Y + Math.Sin(a) * inner), new Point(star.X + Math.Cos(a) * outer, star.Y + Math.Sin(a) * outer));
                }
                Sparkle(g, star, 26 * (1 - z * 0.4), Tint(Paper, 1));
            }
            if (Laser && t >= Zap)
            {
                // The reticle that stays on the star: a circle and a long crosshair.
                double r = 30, arm = 80;
                double a = t < Hold ? 1 : out_;
                g.DrawEllipse(null, new Pen(Tint(Red, a), 2), star, r, r);
                g.DrawEllipse(null, new Pen(Tint(Violet, a * 0.8), 1.5), star, r + 3, r + 3);
                g.DrawLine(new Pen(Tint(Violet, a), 2), new Point(star.X - arm, star.Y), new Point(star.X + arm, star.Y));
                g.DrawLine(new Pen(Tint(Violet, a), 2), new Point(star.X, star.Y - arm), new Point(star.X, star.Y + arm));
                Sparkle(g, star, 14, Tint(Paper, a));
                Sparkle(g, star, 5, Tint(Red, a));
            }
        }
    }
}
