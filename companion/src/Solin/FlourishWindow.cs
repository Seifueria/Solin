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
    // Big flourishes that need more room than her own window: shards and a beam when she teleports, radial speed lines
    // and a slanted cut-in of her face when a job is done. (Looks described from mochiiwuff/capri-desktop; no code taken.)
    internal sealed class FlourishWindow : Window
    {
        private const double Wide = 900, Tall = 700;
        private readonly FlourishCanvas canvas = new FlourishCanvas();
        private readonly Portrait face = new Portrait(120) { Round = false, IsVisible = false };
        private readonly Border cutIn;
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly DispatcherTimer done = new DispatcherTimer();

        internal Func<(Clip clip, int cell, Clip mouth, int mouthCell)> Frame { set { face.Frame = value; } }

        internal FlourishWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            Width = Wide; Height = Tall;
            ShowActivated = false;
            IsHitTestVisible = false;
            cutIn = new Border
            {
                Child = face, BorderBrush = TalkLook.AccentBrush, BorderThickness = new Thickness(2), Background = new SolidColorBrush(Color.FromArgb(240, 18, 13, 24)),
                Width = 260, Height = 124, IsVisible = false, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                RenderTransform = new SkewTransform(-12, 0), ClipToBounds = true,
            };
            Grid layers = new Grid();
            layers.Children.Add(canvas);
            layers.Children.Add(cutIn);
            Content = layers;
            frames.Tick += delegate { canvas.Step(); if (canvas.Idle && !cutIn.IsVisible) { frames.Stop(); Hide(); } };
            done.Tick += delegate { done.Stop(); cutIn.IsVisible = false; face.IsVisible = false; };
        }

        // Centres the window on a screen point and shows it.
        private void Around(PixelPoint centre)
        {
            double scale = Screens.Primary?.Scaling ?? 1;
            Position = new PixelPoint(centre.X - (int)(Wide * scale / 2), centre.Y - (int)(Tall * scale / 2));
            if (!IsVisible) Show();
            frames.Start();
        }

        private static readonly Point Middle = new Point(Wide / 2, Tall / 2);

        internal void Shatter(PixelPoint centre, double size) { Around(centre); canvas.Shatter(Middle, size); }
        internal void Beam(PixelPoint centre, double size) { Around(centre); canvas.Beam(Middle, size); }

        internal void Burst(PixelPoint centre, double size)
        {
            Around(centre);
            canvas.Burst(Middle, size);
            // The cut-in slides in from the right of her head and holds for a beat.
            cutIn.Margin = new Thickness(Middle.X + size * 0.3, Middle.Y - size * 0.75, 0, 0);
            cutIn.IsVisible = true;
            face.IsVisible = true;
            done.Interval = TimeSpan.FromSeconds(1.4);
            done.Start();
        }
    }

    internal sealed class FlourishCanvas : Control
    {
        private readonly Random random = new Random();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private double lastAt;
        private static readonly Color[] Palette = { Color.FromRgb(255, 33, 82), Color.FromRgb(255, 33, 82), Color.FromRgb(20, 16, 28), Color.FromRgb(54, 255, 122), Color.FromRgb(123, 61, 255), Color.FromRgb(255, 244, 246) };

        private struct Shard { public Point At, Vel; public double Angle, Spin, Born, Life, Size; public Color Tint; }
        private readonly List<Shard> shards = new List<Shard>();
        private struct Ray { public double Angle, Inner, Outer, Width, Born, Life; public Color Tint; }
        private readonly List<Ray> rays = new List<Ray>();
        private Point beamAt; private double beamBorn = -10, beamTall;
        private const double BeamLife = 0.35;

        internal bool Idle { get { double now = clock.Elapsed.TotalSeconds; return shards.Count == 0 && rays.Count == 0 && now - beamBorn > BeamLife; } }

        internal void Shatter(Point centre, double size)
        {
            double now = clock.Elapsed.TotalSeconds;
            for (int i = 0; i < 46; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2, speed = 150 + random.NextDouble() * 520;
                Point from = new Point(centre.X + (random.NextDouble() - 0.5) * size * 0.5, centre.Y + (random.NextDouble() - 0.5) * size * 0.9);
                shards.Add(new Shard { At = from, Vel = new Point(Math.Cos(angle) * speed, Math.Sin(angle) * speed - 80), Angle = random.NextDouble() * Math.PI, Spin = (random.NextDouble() - 0.5) * 14, Born = now, Life = 0.45 + random.NextDouble() * 0.4, Size = 4 + random.NextDouble() * 10, Tint = Palette[random.Next(Palette.Length)] });
            }
            InvalidateVisual();
        }

        internal void Beam(Point centre, double size) { beamAt = centre; beamTall = size * 1.6; beamBorn = clock.Elapsed.TotalSeconds; InvalidateVisual(); }

        internal void Burst(Point centre, double size)
        {
            double now = clock.Elapsed.TotalSeconds;
            for (int i = 0; i < 34; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                rays.Add(new Ray { Angle = angle, Inner = size * (0.5 + random.NextDouble() * 0.4), Outer = size * (1.4 + random.NextDouble() * 1.6), Width = 2 + random.NextDouble() * 9, Born = now + random.NextDouble() * 0.12, Life = 0.35 + random.NextDouble() * 0.3, Tint = Palette[random.Next(Palette.Length)] });
            }
            InvalidateVisual();
        }

        internal void Step()
        {
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.05, Math.Max(0.001, now - lastAt));
            lastAt = now;
            for (int i = 0; i < shards.Count; i++)
            {
                Shard s = shards[i];
                s.Vel = new Point(s.Vel.X * 0.97, s.Vel.Y + 700 * dt);
                s.At = new Point(s.At.X + s.Vel.X * dt, s.At.Y + s.Vel.Y * dt);
                s.Angle += s.Spin * dt;
                shards[i] = s;
            }
            shards.RemoveAll(s => now - s.Born > s.Life);
            rays.RemoveAll(r => now - r.Born > r.Life);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            double now = clock.Elapsed.TotalSeconds;
            foreach (Shard s in shards)
            {
                double age = (now - s.Born) / s.Life;
                byte alpha = (byte)(255 * Math.Clamp(1 - age * age, 0, 1));
                using (context.PushTransform(Matrix.CreateRotation(s.Angle) * Matrix.CreateTranslation(s.At.X, s.At.Y)))
                {
                    // A slim diamond, like a chip of her.
                    StreamGeometry g = new StreamGeometry();
                    using (StreamGeometryContext c = g.Open())
                    {
                        c.BeginFigure(new Point(-s.Size, 0), true); c.LineTo(new Point(0, -s.Size * 0.4)); c.LineTo(new Point(s.Size, 0)); c.LineTo(new Point(0, s.Size * 0.4)); c.EndFigure(true);
                    }
                    context.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, s.Tint.R, s.Tint.G, s.Tint.B)), null, g);
                }
            }
            foreach (Ray r in rays)
            {
                if (now < r.Born) continue;
                double age = (now - r.Born) / r.Life;
                byte alpha = (byte)(255 * Math.Clamp(1 - age, 0, 1));
                double inner = r.Inner + age * r.Outer * 0.5, outer = r.Outer + age * r.Outer * 0.3;
                Point a = new Point(Bounds.Width / 2 + Math.Cos(r.Angle) * inner, Bounds.Height / 2 + Math.Sin(r.Angle) * inner);
                Point b = new Point(Bounds.Width / 2 + Math.Cos(r.Angle) * outer, Bounds.Height / 2 + Math.Sin(r.Angle) * outer);
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(alpha, r.Tint.R, r.Tint.G, r.Tint.B)), r.Width * (1 - age * 0.6), lineCap: PenLineCap.Round), a, b);
            }
            double beamAge = (now - beamBorn) / BeamLife;
            if (beamAge >= 0 && beamAge < 1)
            {
                // A vertical column that is wide and white at first, then thins to a red thread and goes.
                double width = Math.Max(1, 28 * (1 - beamAge)), tall = beamTall * (1 - beamAge * 0.3);
                Rect column = new Rect(beamAt.X - width / 2, beamAt.Y - tall / 2, width, tall);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(200 * (1 - beamAge)), 255, 33, 82)), column.Inflate(new Thickness(6, 0)));
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(160 * (1 - beamAge)), 123, 61, 255)), column.Inflate(new Thickness(3, 0)));
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * (1 - beamAge)), 255, 244, 246)), column);
            }
        }
    }
}
