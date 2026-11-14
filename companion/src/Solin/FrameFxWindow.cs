// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // Red lightning racing round a window's frame with a few glitch bars across it, while Claude works in it.
    // (Look described from mochiiwuff/capri-desktop's window possession; no code taken; the window itself is left alone.)
    internal sealed class FrameFxWindow : Window
    {
        private readonly FrameCanvas canvas = new FrameCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private double until;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        internal FrameFxWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            ShowActivated = false;
            IsHitTestVisible = false;
            Content = canvas;
            frames.Tick += delegate { if (clock.Elapsed.TotalSeconds > until) { frames.Stop(); Hide(); } else canvas.InvalidateVisual(); };
        }

        internal void Race(PixelRect around, double seconds = 0.9)
        {
            double scale = Screens.Primary?.Scaling ?? 1;
            int pad = 24;
            Position = new PixelPoint(around.X - pad, around.Y - pad);
            Width = (around.Width + pad * 2) / scale; Height = (around.Height + pad * 2) / scale;
            canvas.Frame = new Rect(pad / scale, pad / scale, around.Width / scale, around.Height / scale);
            canvas.Start = clock.Elapsed.TotalSeconds;
            until = canvas.Start + seconds;
            if (!IsVisible) Show();
            frames.Start();
        }
    }

    internal sealed class FrameCanvas : Control
    {
        internal Rect Frame;
        internal double Start;
        private readonly Random random = new Random();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private static readonly Color Red = Color.FromRgb(255, 33, 82), Violet = Color.FromRgb(123, 61, 255), Paper = Color.FromRgb(255, 244, 246);

        public override void Render(DrawingContext context)
        {
            double t = clock.Elapsed.TotalSeconds - Start;
            double fade = Math.Clamp(1 - Math.Max(0, t - 0.6) / 0.3, 0, 1);
            // The frame as a jagged loop: each edge wobbles by a few pixels, redrawn every frame.
            StreamGeometry loop = new StreamGeometry();
            using (StreamGeometryContext g = loop.Open())
            {
                int n = 14;
                Point first = Jag(Frame.TopLeft);
                g.BeginFigure(first, false);
                foreach ((Point a, Point b) in new[] { (Frame.TopLeft, Frame.TopRight), (Frame.TopRight, Frame.BottomRight), (Frame.BottomRight, Frame.BottomLeft), (Frame.BottomLeft, Frame.TopLeft) })
                    for (int i = 1; i <= n; i++) g.LineTo(Jag(a + (b - a) * (i / (double)n)));
                g.EndFigure(false);
            }
            foreach ((Color c, double w) in new[] { (Violet, 6.0), (Red, 3.0), (Paper, 1.2) })
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * fade), c.R, c.G, c.B)), w, lineJoin: PenLineJoin.Round), loop);
            // A few glitch bars sliding across the window.
            for (int i = 0; i < 5; i++)
            {
                double y = Frame.Y + random.NextDouble() * Frame.Height, h = 2 + random.NextDouble() * 6, x = Frame.X + random.NextDouble() * Frame.Width * 0.6, w = Frame.Width * (0.2 + random.NextDouble() * 0.4);
                Color c = i % 3 == 0 ? Red : i % 3 == 1 ? Violet : Color.FromRgb(54, 255, 122);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(170 * fade), c.R, c.G, c.B)), new Rect(x, y, w, h));
            }
        }

        private Point Jag(Point p) { return new Point(p.X + (random.NextDouble() - 0.5) * 9, p.Y + (random.NextDouble() - 0.5) * 9); }
    }
}
