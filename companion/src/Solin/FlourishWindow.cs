// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Solin.Companion.Upstream;

namespace Solin.Companion
{
    // Screen-sized flourishes: when she teleports, glitch bars tear across the whole screen at her height, she breaks into
    // shards that fly wide, and a beam the full height of the display marks where she reforms; when a job is done, speed lines
    // run out to the screen's edges and a cut-in of her face slides in. Click-through, so it never blocks anything.
    // (Looks described from mochiiwuff/capri-desktop; no code taken.)
    internal sealed class FlourishWindow : Window
    {
        private readonly FlourishCanvas canvas = new FlourishCanvas();
        private readonly Portrait face = new Portrait(190) { Round = false, IsVisible = false };
        private readonly Border cutIn;
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly DispatcherTimer done = new DispatcherTimer();
        private PixelRect covering;

        internal Func<(Clip clip, int cell, Clip mouth, int mouthCell)> Frame { set { face.Frame = value; } }

        internal FlourishWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            Width = 400; Height = 300;
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            cutIn = new Border
            {
                Child = face, BorderBrush = TalkLook.AccentBrush, BorderThickness = new Thickness(3), Background = new SolidColorBrush(Color.FromArgb(245, 18, 13, 24)),
                Width = 420, Height = 196, IsVisible = false, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                RenderTransform = new SkewTransform(-12, 0), ClipToBounds = true,
            };
            Grid layers = new Grid();
            layers.Children.Add(canvas);
            layers.Children.Add(cutIn);
            Content = layers;
            frames.Tick += delegate { canvas.Step(); if (canvas.Idle && !cutIn.IsVisible) { frames.Stop(); Hide(); } };
            done.Tick += delegate { done.Stop(); cutIn.IsVisible = false; face.IsVisible = false; };
        }

        // Covers the whole screen the point is on and returns the point in this window's own units.
        private Point Cover(PixelPoint at)
        {
            Screen screen = Screens.ScreenFromPoint(at) ?? Screens.Primary;
            double scale = screen?.Scaling ?? 1;
            PixelRect area = screen?.Bounds ?? new PixelRect(at.X - 600, at.Y - 400, 1200, 800);
            if (area != covering || !IsVisible)
            {
                covering = area;
                Position = area.Position;
                Width = area.Width / scale; Height = area.Height / scale;
                canvas.Area = new Size(Width, Height);
            }
            if (!IsVisible) Show();
            Spaces.Front(this);
            frames.Start();
            return new Point((at.X - area.X) / scale, (at.Y - area.Y) / scale);
        }

        internal void Shatter(PixelPoint centre, double size) { canvas.Shatter(Cover(centre), size); }
        internal void Beam(PixelPoint centre, double size) { canvas.Beam(Cover(centre), size); }

        // cutInArt: a still for the cut-in panel; null shows her live face as before. avoid: text boxes on screen (card, bubble,
        // pill) in screen pixels; the panel goes in the first spot beside her that keeps clear of them.
        internal void Burst(PixelPoint centre, double size, Avalonia.Media.Imaging.Bitmap cutInArt = null, bool rays = true, System.Collections.Generic.IEnumerable<PixelRect> avoid = null, bool panel = true)
        {
            Point at = Cover(centre);
            // No panel: just the rays (the big face panel is off for now, Kaelen's call).
            if (!panel) { if (rays) canvas.Burst(at, size); return; }
            Screen screen = Screens.ScreenFromPoint(centre) ?? Screens.Primary;
            double scale = screen?.Scaling ?? 1;
            if (cutInArt != null)
            {
                // No bigger than about 1.35 of the art's own pixels, and never cropped, so it stays sharp.
                double w = Math.Min(300, cutInArt.PixelSize.Width * 1.35 / scale);
                cutIn.Width = w;
                cutIn.Height = w * cutInArt.PixelSize.Height / cutInArt.PixelSize.Width;
                Image still = new Image { Source = cutInArt, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapInterpolationMode(still, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
                cutIn.Child = still;
            }
            else { cutIn.Width = 420; cutIn.Height = 196; cutIn.Child = face; }
            if (rays) canvas.Burst(at, size);
            System.Collections.Generic.List<Rect> keepOff = new System.Collections.Generic.List<Rect>();
            if (avoid != null)
                foreach (PixelRect r in avoid)
                    keepOff.Add(new Rect((r.X - covering.X) / scale - 12, (r.Y - covering.Y) / scale - 12, r.Width / scale + 24, r.Height / scale + 24));
            double cw = cutIn.Width, ch = cutIn.Height, gap = size * 0.35;
            Point[] tries =
            {
                new Point(at.X + gap, at.Y - size * 0.9), new Point(at.X - gap - cw, at.Y - size * 0.9),
                new Point(at.X + gap, at.Y - size * 0.25), new Point(at.X - gap - cw, at.Y - size * 0.25),
                new Point(at.X - cw / 2, at.Y + size * 0.55), new Point(at.X - cw / 2, at.Y - size * 1.2 - ch),
            };
            Rect best = default; double bestOverlap = double.MaxValue;
            foreach (Point p in tries)
            {
                Rect r = new Rect(Math.Clamp(p.X, 20, Math.Max(20, canvas.Area.Width - cw - 30)), Math.Clamp(p.Y, 20, Math.Max(20, canvas.Area.Height - ch - 20)), cw, ch);
                double overlap = 0;
                foreach (Rect k in keepOff) { Rect i = r.Intersect(k); if (i.Width > 0 && i.Height > 0) overlap += i.Width * i.Height; }
                if (overlap < bestOverlap) { best = r; bestOverlap = overlap; }
                if (overlap == 0) break;
            }
            cutIn.Margin = new Thickness(best.X, best.Y, 0, 0);
            cutIn.IsVisible = true;
            face.IsVisible = cutInArt == null;
            done.Interval = TimeSpan.FromSeconds(1.5);
            done.Start();
        }
    }

    internal sealed class FlourishCanvas : Control
    {
        internal Size Area = new Size(1200, 800);
        private readonly Random random = new Random();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private double lastAt;
        private static readonly Color Red = Color.FromRgb(255, 33, 82), Green = Color.FromRgb(54, 255, 122), Violet = Color.FromRgb(123, 61, 255), Paper = Color.FromRgb(255, 244, 246), Ink = Color.FromRgb(20, 16, 28);
        private static readonly Color[] Palette = { Red, Red, Red, Ink, Green, Green, Violet, Paper };

        private struct Shard { public Point At, Vel; public double Angle, Spin, Born, Life, Size; public Color Tint; }
        private readonly List<Shard> shards = new List<Shard>();
        private struct Bar { public double Y, X, W, H, Born, Life, Drift; public Color Tint; }
        private readonly List<Bar> bars = new List<Bar>();
        private struct Ray { public Point From; public double Angle, Inner, Outer, Width, Born, Life; public Color Tint; }
        private readonly List<Ray> rays = new List<Ray>();
        private Point beamAt; private double beamBorn = -10;
        private const double BeamLife = 0.5;

        private double Now { get { return clock.Elapsed.TotalSeconds; } }
        private double Diagonal { get { return Math.Sqrt(Area.Width * Area.Width + Area.Height * Area.Height); } }

        internal bool Idle { get { return shards.Count == 0 && rays.Count == 0 && bars.Count == 0 && Now - beamBorn > BeamLife; } }

        // Her leaving: bars tear across the screen at her height, then she flies apart in shards that cross half the screen.
        internal void Shatter(Point centre, double size)
        {
            double now = Now, reach = Math.Max(Area.Width, Area.Height);
            for (int i = 0; i < 14; i++)
            {
                double y = centre.Y + (random.NextDouble() - 0.5) * size * 1.2;
                double w = Area.Width * (0.15 + random.NextDouble() * 0.55);
                double x = Math.Clamp(centre.X - w * random.NextDouble(), -w * 0.2, Area.Width);
                bars.Add(new Bar { Y = y, X = x, W = w, H = 2 + random.NextDouble() * 7, Born = now + random.NextDouble() * 0.12, Life = 0.25 + random.NextDouble() * 0.25, Drift = (random.NextDouble() - 0.5) * 900, Tint = i % 4 == 0 ? Paper : Palette[random.Next(Palette.Length)] });
            }
            for (int i = 0; i < 120; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2, speed = reach * (0.25 + random.NextDouble() * 0.9);
                Point from = new Point(centre.X + (random.NextDouble() - 0.5) * size * 0.5, centre.Y + (random.NextDouble() - 0.5) * size * 0.9);
                shards.Add(new Shard { At = from, Vel = new Point(Math.Cos(angle) * speed, Math.Sin(angle) * speed * 0.7), Angle = random.NextDouble() * Math.PI, Spin = (random.NextDouble() - 0.5) * 16, Born = now + 0.08, Life = 0.55 + random.NextDouble() * 0.45, Size = size * (0.03 + random.NextDouble() * 0.07), Tint = Palette[random.Next(Palette.Length)] });
            }
            InvalidateVisual();
        }

        // Her arriving: a beam from the top of the screen to the bottom, white-hot and wide, thinning to a red thread.
        internal void Beam(Point centre, double size) { beamAt = centre; beamBorn = Now; InvalidateVisual(); }

        // A job done: speed lines from around her out past the screen's edges.
        internal void Burst(Point centre, double size)
        {
            double now = Now, far = Diagonal;
            for (int i = 0; i < 56; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                rays.Add(new Ray { From = centre, Angle = angle, Inner = size * (0.55 + random.NextDouble() * 0.5), Outer = far * (0.35 + random.NextDouble() * 0.7), Width = 3 + random.NextDouble() * 16, Born = now + random.NextDouble() * 0.12, Life = 0.4 + random.NextDouble() * 0.35, Tint = i % 3 == 0 ? Paper : Palette[random.Next(Palette.Length)] });
            }
            InvalidateVisual();
        }

        internal void Step()
        {
            double now = Now, dt = Math.Min(0.05, Math.Max(0.001, now - lastAt));
            lastAt = now;
            for (int i = 0; i < shards.Count; i++)
            {
                Shard s = shards[i];
                if (now < s.Born) continue;
                s.Vel = new Point(s.Vel.X * 0.96, s.Vel.Y * 0.96 + Area.Height * 0.9 * dt);
                s.At = new Point(s.At.X + s.Vel.X * dt, s.At.Y + s.Vel.Y * dt);
                s.Angle += s.Spin * dt;
                shards[i] = s;
            }
            for (int i = 0; i < bars.Count; i++) { Bar b = bars[i]; b.X += b.Drift * dt; bars[i] = b; }
            shards.RemoveAll(s => now - s.Born > s.Life);
            bars.RemoveAll(b => now - b.Born > b.Life);
            rays.RemoveAll(r => now - r.Born > r.Life);
            InvalidateVisual();
        }

        private static IBrush Tint(Color c, double alpha) { return new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B)); }

        public override void Render(DrawingContext context)
        {
            double now = Now;
            foreach (Bar b in bars)
            {
                if (now < b.Born) continue;
                double age = (now - b.Born) / b.Life;
                context.FillRectangle(Tint(b.Tint, 1 - age * age), new Rect(b.X, b.Y, b.W, b.H));
            }
            foreach (Shard s in shards)
            {
                if (now < s.Born) continue;
                double age = (now - s.Born) / s.Life;
                using (context.PushTransform(Matrix.CreateRotation(s.Angle) * Matrix.CreateTranslation(s.At.X, s.At.Y)))
                {
                    StreamGeometry g = new StreamGeometry();
                    using (StreamGeometryContext c = g.Open())
                    {
                        c.BeginFigure(new Point(-s.Size, 0), true); c.LineTo(new Point(0, -s.Size * 0.38)); c.LineTo(new Point(s.Size, 0)); c.LineTo(new Point(0, s.Size * 0.38)); c.EndFigure(true);
                    }
                    context.DrawGeometry(Tint(s.Tint, 1 - age * age), null, g);
                }
            }
            foreach (Ray r in rays)
            {
                if (now < r.Born) continue;
                double age = (now - r.Born) / r.Life;
                double inner = r.Inner + age * r.Outer * 0.35, outer = r.Outer * (0.7 + age * 0.5);
                Point a = new Point(r.From.X + Math.Cos(r.Angle) * inner, r.From.Y + Math.Sin(r.Angle) * inner);
                Point b = new Point(r.From.X + Math.Cos(r.Angle) * outer, r.From.Y + Math.Sin(r.Angle) * outer);
                // A tapered blade: wide near her, a point at the far end.
                Vector dir = b - a; double len = Math.Max(1, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
                Vector n = new Vector(-dir.Y / len, dir.X / len) * (r.Width * (1 - age * 0.6) / 2);
                StreamGeometry blade = new StreamGeometry();
                using (StreamGeometryContext c = blade.Open()) { c.BeginFigure(a + n, true); c.LineTo(b); c.LineTo(a - n); c.EndFigure(true); }
                context.DrawGeometry(Tint(r.Tint, 1 - age), null, blade);
            }
            double beamAge = (now - beamBorn) / BeamLife;
            if (beamAge >= 0 && beamAge < 1)
            {
                // Full height of the screen at her x. Wide and white first, then a thin red thread with a jagged edge, then gone.
                double ease = 1 - Math.Pow(1 - beamAge, 2);
                double core = Math.Max(2, Area.Width * 0.022 * (1 - ease));
                double edge = core + 10 + 14 * (1 - ease);
                double x = beamAt.X;
                StreamGeometry jag = new StreamGeometry();
                using (StreamGeometryContext c = jag.Open())
                {
                    int steps = 28;
                    c.BeginFigure(new Point(x - edge / 2, 0), true);
                    for (int i = 1; i <= steps; i++) c.LineTo(new Point(x - edge / 2 - random.NextDouble() * 8, Area.Height * i / steps));
                    for (int i = steps; i >= 0; i--) c.LineTo(new Point(x + edge / 2 + random.NextDouble() * 8, Area.Height * i / steps));
                    c.EndFigure(true);
                }
                context.DrawGeometry(Tint(Violet, 0.85 * (1 - beamAge)), null, jag);
                context.FillRectangle(Tint(Red, 1 - beamAge * 0.8), new Rect(x - (core + 8) / 2, 0, core + 8, Area.Height));
                context.FillRectangle(Tint(Paper, 1 - beamAge), new Rect(x - core / 2, 0, core, Area.Height));
            }
        }
    }
}
