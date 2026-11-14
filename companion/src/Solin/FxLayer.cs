// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Solin.Companion.Upstream;

namespace Solin.Companion
{
    // Flourishes over and under her: afterimages when she moves fast, sparks off her hand, a red impact frame.
    // (Ideas described from mochiiwuff/capri-desktop's renderer; no code taken.)
    internal sealed class FxLayer : Control
    {
        private readonly SpriteWindow sprite;
        private readonly bool under;
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly Random random = new Random();

        private struct Echo { public Clip Clip; public int Cell; public Rect Place; public Matrix Pose; public bool Left; public double At; public Point Offset; }
        private readonly List<Echo> echoes = new List<Echo>();
        private PixelPoint lastPos;
        private double lastAt;
        private const double EchoLife = 0.32, FastEnough = 520;

        private struct Spark { public Point At, Vel; public double Born, Life; public Color Tint; }
        private readonly List<Spark> sparks = new List<Spark>();
        private static readonly Color[] SparkTints = { Color.FromRgb(255, 33, 82), Color.FromRgb(255, 33, 82), Color.FromRgb(255, 244, 246), Color.FromRgb(54, 255, 122) };

        private double impactUntil = -1;

        // under: drawn beneath her (afterimages); otherwise above (sparks, impact).
        internal FxLayer(SpriteWindow sprite, bool under)
        {
            this.sprite = sprite;
            this.under = under;
            IsHitTestVisible = false;
            lastPos = sprite.Position;
            frames.Tick += delegate { Tick(); };
            AttachedToVisualTree += delegate { frames.Start(); };
            DetachedFromVisualTree += delegate { frames.Stop(); };
        }

        private double Now { get { return clock.Elapsed.TotalSeconds; } }

        private void Tick()
        {
            double now = Now, dt = Math.Max(0.001, now - lastAt);
            lastAt = now;
            if (under)
            {
                // Moving fast across the screen leaves a trail of herself behind.
                PixelPoint pos = sprite.Position;
                double speed = Math.Sqrt(Math.Pow(pos.X - lastPos.X, 2) + Math.Pow(pos.Y - lastPos.Y, 2)) / dt;
                (Clip clip, int cell, Rect place, Matrix pose, bool left) = sprite.DrawnFrame();
                if (clip != null && speed > FastEnough)
                    echoes.Add(new Echo { Clip = clip, Cell = cell, Place = place, Pose = pose, Left = left, At = now, Offset = new Point(lastPos.X - pos.X, lastPos.Y - pos.Y) });
                lastPos = pos;
                echoes.RemoveAll(e => now - e.At > EchoLife);
                // Older echoes drift back toward where they were made as she moves on.
                for (int i = 0; i < echoes.Count; i++)
                {
                    Echo e = echoes[i];
                    e.Offset = new Point(e.Offset.X + (lastPos.X - pos.X), e.Offset.Y + (lastPos.Y - pos.Y));
                    echoes[i] = e;
                }
                if (echoes.Count > 0) InvalidateVisual();
                else if (IsVisible && echoes.Count == 0 && speed > 0) InvalidateVisual();
            }
            else
            {
                for (int i = 0; i < sparks.Count; i++)
                {
                    Spark s = sparks[i];
                    s.Vel = new Point(s.Vel.X * 0.96, s.Vel.Y + 900 * dt);
                    s.At = new Point(s.At.X + s.Vel.X * dt, s.At.Y + s.Vel.Y * dt);
                    sparks[i] = s;
                }
                sparks.RemoveAll(s => now - s.Born > s.Life);
                if (sparks.Count > 0 || impactUntil > now) InvalidateVisual();
                else if (impactUntil > 0) { impactUntil = -1; InvalidateVisual(); }
            }
        }

        // A handful of sparks off her hand, as when the base's Claude runs a command.
        internal void Sparks(int count = 14)
        {
            Point hand = sprite.HandPoint;
            double now = Now;
            for (int i = 0; i < count; i++)
            {
                double angle = -Math.PI / 2 + (random.NextDouble() - 0.5) * 1.8, speed = 120 + random.NextDouble() * 260;
                sparks.Add(new Spark { At = hand, Vel = new Point(Math.Cos(angle) * speed, Math.Sin(angle) * speed), Born = now, Life = 0.25 + random.NextDouble() * 0.35, Tint = SparkTints[random.Next(SparkTints.Length)] });
            }
            InvalidateVisual();
        }

        // A red frame around her for a few frames: something just hit.
        internal void Impact() { impactUntil = Now + 0.12; InvalidateVisual(); }

        public override void Render(DrawingContext context)
        {
            double now = Now;
            if (under)
            {
                foreach (Echo e in echoes)
                {
                    double age = (now - e.At) / EchoLife;
                    using (context.PushTransform(Matrix.CreateTranslation(e.Offset.X, e.Offset.Y) * e.Pose))
                    using (context.PushOpacity(Math.Clamp(0.45 * (1 - age), 0, 1)))
                        context.DrawImage(e.Clip.Art(e.Left), new Rect(e.Cell * e.Clip.Wide, 0, e.Clip.Wide, e.Clip.Tall), e.Place);
                }
                return;
            }
            foreach (Spark s in sparks)
            {
                double age = (now - s.Born) / s.Life;
                byte alpha = (byte)(255 * Math.Clamp(1 - age, 0, 1));
                Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, s.Tint.R, s.Tint.G, s.Tint.B)), 2);
                Point tail = new Point(s.At.X - s.Vel.X * 0.03, s.At.Y - s.Vel.Y * 0.03);
                context.DrawLine(pen, tail, s.At);
            }
            if (impactUntil > now)
            {
                Rect box = new Rect(0, 0, Bounds.Width, Bounds.Height);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(60, 255, 33, 82)), box);
                context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 33, 82)), 4), box.Deflate(2));
            }
        }
    }
}
