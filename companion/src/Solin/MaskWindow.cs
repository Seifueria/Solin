// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // Her mask on its own: a green block face with a curled horn, shut eyes as two chevrons, and a fang. What is left of
    // her when she is put away or has been asleep a long while. It bobs, winks now and then, opens its eyes when asked to
    // watch, and is clicked to bring her back. Drawn in code. (Described from mochiiwuff/capri-desktop's chip; no code taken.)
    internal sealed class MaskWindow : Window
    {
        private readonly MaskCanvas canvas = new MaskCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        internal event Action Clicked;
        internal event Action Hovered;
        internal const double Side = 120;

        internal MaskWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            Width = Side; Height = Side * 1.1;
            ShowActivated = false;
            Content = canvas;
            frames.Tick += delegate { canvas.InvalidateVisual(); };
            PointerPressed += delegate { Clicked?.Invoke(); };
            PointerEntered += delegate { Hovered?.Invoke(); };
        }

        // Eyes: false shut (asleep or put away), true open and blinking (watching for you).
        internal bool Watching { set { canvas.Watching = value; } }

        // Shows it with its middle at a screen point.
        internal void ShowAt(PixelPoint centre)
        {
            double scale = Screens.ScreenFromPoint(centre)?.Scaling ?? Screens.Primary?.Scaling ?? 1;
            Position = new PixelPoint(centre.X - (int)(Width * scale / 2), centre.Y - (int)(Height * scale / 2));
            canvas.Born = DateTime.Now;
            if (!IsVisible) Show();
            Spaces.Front(this);
            frames.Start();
        }

        internal PixelPoint Centre
        {
            get { double scale = Screens.Primary?.Scaling ?? 1; return new PixelPoint(Position.X + (int)(Width * scale / 2), Position.Y + (int)(Height * scale / 2)); }
        }

        internal void Away() { frames.Stop(); Hide(); }
    }

    internal sealed class MaskCanvas : Control
    {
        internal bool Watching;
        internal DateTime Born = DateTime.Now;
        private readonly Random random = new Random();
        private double nextWink = 4, winkAt = -10, nextBlink = 2, blinkAt = -10;
        private static readonly Color Green = Color.FromRgb(54, 255, 122), GreenDark = Color.FromRgb(30, 190, 92), Violet = Color.FromRgb(123, 61, 255),
            Ink = Color.FromRgb(10, 8, 14), Paper = Color.FromRgb(250, 246, 250), Blue = Color.FromRgb(2, 111, 254);

        public override void Render(DrawingContext g)
        {
            double t = (DateTime.Now - Born).TotalSeconds;
            double w = Bounds.Width, h = Bounds.Height;
            double s = w / 120.0;
            // A slow bob, a little more when watching.
            double bob = Math.Sin(t * (Watching ? 3.0 : 1.4)) * (Watching ? 4 : 2.5) * s;
            if (t > nextWink) { winkAt = t; nextWink = t + 5 + random.NextDouble() * 6; }
            if (t > nextBlink) { blinkAt = t; nextBlink = t + 1.8 + random.NextDouble() * 2.5; }
            using (g.PushTransform(Matrix.CreateTranslation(0, bob)))
            {
                // The horn: a hooked block rising from the top right and curling over.
                StreamGeometry horn = new StreamGeometry();
                using (StreamGeometryContext c = horn.Open())
                {
                    c.BeginFigure(new Point(52 * s, 52 * s), true);
                    c.LineTo(new Point(52 * s, 34 * s)); c.LineTo(new Point(78 * s, 24 * s)); c.LineTo(new Point(80 * s, 8 * s)); c.LineTo(new Point(96 * s, 2 * s));
                    c.LineTo(new Point(98 * s, 24 * s)); c.LineTo(new Point(72 * s, 36 * s)); c.LineTo(new Point(72 * s, 52 * s));
                    c.EndFigure(true);
                }
                // The face: a slightly tapered block.
                StreamGeometry face = new StreamGeometry();
                using (StreamGeometryContext c = face.Open())
                {
                    c.BeginFigure(new Point(18 * s, 50 * s), true);
                    c.LineTo(new Point(102 * s, 46 * s)); c.LineTo(new Point(100 * s, 96 * s)); c.LineTo(new Point(22 * s, 100 * s));
                    c.EndFigure(true);
                }
                // A violet offset shadow, like a print slightly out of register.
                using (g.PushTransform(Matrix.CreateTranslation(3 * s, 3 * s)))
                {
                    g.DrawGeometry(new SolidColorBrush(Violet), null, horn);
                    g.DrawGeometry(new SolidColorBrush(Violet), null, face);
                }
                g.DrawGeometry(new SolidColorBrush(Green), null, horn);
                g.DrawGeometry(new SolidColorBrush(Green), null, face);
                g.DrawLine(new Pen(new SolidColorBrush(GreenDark), 3 * s), new Point(22 * s, 92 * s), new Point(98 * s, 89 * s));

                // The fang under the face.
                StreamGeometry fang = new StreamGeometry();
                using (StreamGeometryContext c = fang.Open())
                {
                    c.BeginFigure(new Point(50 * s, 99 * s), true); c.LineTo(new Point(68 * s, 98 * s)); c.LineTo(new Point(58 * s, 120 * s)); c.EndFigure(true);
                }
                g.DrawGeometry(new SolidColorBrush(Paper), new Pen(new SolidColorBrush(Violet), 1.5 * s), fang);

                // Eyes.
                bool wink = !Watching && t - winkAt < 0.5;
                bool blink = Watching && t - blinkAt < 0.12;
                foreach ((double ex, bool right) in new[] { (42.0, false), (78.0, true) })
                {
                    Point c = new Point(ex * s, 72 * s);
                    bool open = (Watching && !blink) || (wink && right);
                    if (!open)
                    {
                        // Shut: a chevron.
                        Pen pen = new Pen(new SolidColorBrush(Ink), 4 * s, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                        g.DrawLine(pen, new Point(c.X - 10 * s, c.Y - 3 * s), new Point(c.X, c.Y + 4 * s));
                        g.DrawLine(pen, new Point(c.X, c.Y + 4 * s), new Point(c.X + 10 * s, c.Y - 3 * s));
                        continue;
                    }
                    g.DrawEllipse(new SolidColorBrush(Ink), null, c, 11 * s, 9 * s);
                    g.DrawEllipse(new SolidColorBrush(Paper), null, c, 7.5 * s, 6 * s);
                    g.DrawEllipse(new SolidColorBrush(Blue), null, c, 5.5 * s, 5 * s);
                    g.DrawEllipse(new SolidColorBrush(Paper), null, new Point(c.X + 1.5 * s, c.Y - 1.5 * s), 1.8 * s, 1.8 * s);
                }
            }
        }
    }
}
