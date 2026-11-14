// Solin-owned; not derived from upstream.
using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Solin.Companion
{
    // Her entrance, all drawn: brackets lock onto her spot, a line flicks on and opens into a screen with scanlines, the
    // screen snaps to a dot, and she reassembles out of it. (An idea described from edgerunners-2/capri-ai-desktop; no code taken.)
    internal sealed class IntroFx : Control
    {
        private const double Brackets = 0.5, ScreenOn = 0.45, Collapse = 0.25;
        internal static readonly double Length = Brackets + ScreenOn + Collapse;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly Point centre;
        private readonly double size;
        private static readonly IBrush Pink = new SolidColorBrush(TalkLook.Accent);
        private static readonly IBrush White = new SolidColorBrush(Color.FromRgb(255, 246, 250));
        internal event Action ScreenLit;
        internal event Action Finished;
        private bool lit;

        internal IntroFx(Point bodyCentre, double bodySize)
        {
            centre = bodyCentre;
            size = bodySize;
            IsHitTestVisible = false;
            frames.Tick += delegate
            {
                double t = clock.Elapsed.TotalSeconds;
                if (!lit && t >= Brackets) { lit = true; ScreenLit?.Invoke(); }
                if (t >= Length) { frames.Stop(); IsVisible = false; Finished?.Invoke(); return; }
                InvalidateVisual();
            };
        }

        internal void Start() { clock.Restart(); frames.Start(); }

        private static double Ease(double x) { x = Math.Clamp(x, 0, 1); return 1 - Math.Pow(1 - x, 3); }

        public override void Render(DrawingContext context)
        {
            double t = clock.Elapsed.TotalSeconds;
            // The frame she will stand in.
            double wide = size * 0.62, tall = size * 1.11;
            Rect frame = new Rect(centre.X - wide / 2, centre.Y - tall / 2, wide, tall);
            if (t < Brackets)
            {
                // Four corner brackets slide in from the window's corners and settle on the frame.
                double p = Ease(t / Brackets);
                double arm = size * 0.11, thick = 2.5;
                Pen pen = new Pen(Pink, thick);
                Rect from = new Rect(0, 0, Bounds.Width, Bounds.Height);
                Rect now = new Rect(from.X + (frame.X - from.X) * p, from.Y + (frame.Y - from.Y) * p,
                                    from.Width + (frame.Width - from.Width) * p, from.Height + (frame.Height - from.Height) * p);
                foreach ((double cx, double cy, double dx, double dy) in new[] { (now.Left, now.Top, 1.0, 1.0), (now.Right, now.Top, -1.0, 1.0), (now.Left, now.Bottom, 1.0, -1.0), (now.Right, now.Bottom, -1.0, -1.0) })
                {
                    context.DrawLine(pen, new Point(cx, cy), new Point(cx + dx * arm, cy));
                    context.DrawLine(pen, new Point(cx, cy), new Point(cx, cy + dy * arm));
                }
                return;
            }
            if (t < Brackets + ScreenOn)
            {
                // A hairline flicks on across the middle and opens into the whole frame, scanlines rolling.
                double p = Ease((t - Brackets) / ScreenOn);
                double high = Math.Max(2, frame.Height * p);
                Rect screen = new Rect(frame.X, centre.Y - high / 2, frame.Width, high);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 232, 72, 120)), screen);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(180 * (1 - p)), 255, 246, 250)), screen);
                using (context.PushClip(screen))
                    for (double y = screen.Y + (t * 90) % 6; y < screen.Bottom; y += 6)
                        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 10, 6, 14)), 1), new Point(screen.X, y), new Point(screen.Right, y));
                context.DrawRectangle(new Pen(White, 1.5), screen);
                return;
            }
            // The screen snaps to a bright dot and winks out.
            double q = Ease((t - Brackets - ScreenOn) / Collapse);
            double w = frame.Width * (1 - q) + 6 * q, h = frame.Height * (1 - q * 0.97) + 6 * q;
            Rect dot = new Rect(centre.X - w / 2, centre.Y - h / 2, w, h);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * (1 - q * 0.5)), 255, 246, 250)), dot);
            context.DrawRectangle(new Pen(Pink, 2), dot);
        }
    }
}
