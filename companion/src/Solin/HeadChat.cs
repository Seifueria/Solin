// Solin-owned; not derived from upstream. The look follows lachydotmcg/capri-desktop's composer and talk pill as Kaelen
// showed them in screenshots (cut-corner black plates with a cyan edge, "> you:" and her name in red, the pencil and wave
// under her on hover); drawn fresh here, no code taken.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // A black plate with cut corners, faint scan lines and a cyan edge with a soft glow; a red bar down the inside left; and
    // optionally a tail at the bottom pointing down at her. Content sits on top of it in a Grid.
    internal sealed class CutFrame : Control
    {
        internal static readonly Color Cyan = Color.FromRgb(58, 214, 235), Red = Color.FromRgb(232, 32, 72), Fill = Color.FromArgb(244, 9, 10, 14);
        internal double Cut = 11;
        // Height of the tail below the plate; 0 for none. TailAt: where along the bottom it hangs, as a share of the width.
        internal double Tail;
        internal double TailAt = 0.62;
        internal bool Lines = true;

        private StreamGeometry Shape(double w, double h)
        {
            double c = Cut, t = Tail, tx = w * TailAt;
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext s = g.Open())
            {
                s.BeginFigure(new Point(c, 0), true);
                s.LineTo(new Point(w - c, 0)); s.LineTo(new Point(w, c)); s.LineTo(new Point(w, h - c)); s.LineTo(new Point(w - c, h));
                if (t > 0) { s.LineTo(new Point(tx + 6, h)); s.LineTo(new Point(tx + 6, h + t)); s.LineTo(new Point(tx - 16, h)); }
                s.LineTo(new Point(c, h)); s.LineTo(new Point(0, h - c)); s.LineTo(new Point(0, c));
                s.EndFigure(true);
            }
            return g;
        }

        public override void Render(DrawingContext g)
        {
            // Inset a little so the edge and its glow are not cut off by the window's border.
            const double inset = 3;
            double w = Bounds.Width - inset * 2, h = Bounds.Height - Tail - inset * 2;
            if (w < 2 * Cut || h < 2 * Cut) return;
            using IDisposable shift = g.PushTransform(Matrix.CreateTranslation(inset, inset));
            StreamGeometry shape = Shape(w, h);
            g.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(55, Cyan.R, Cyan.G, Cyan.B)), 6, lineJoin: PenLineJoin.Miter), shape);
            g.DrawGeometry(new SolidColorBrush(Fill), null, shape);
            if (Lines)
                using (g.PushGeometryClip(shape))
                    for (double y = 2; y < h; y += 3) g.FillRectangle(new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), new Rect(0, y, w, 1));
            g.DrawGeometry(null, new Pen(new SolidColorBrush(Cyan), 2, lineJoin: PenLineJoin.Miter), shape);
            g.FillRectangle(new SolidColorBrush(Red), new Rect(14, 12, 4, Math.Max(8, h - 24)));
        }
    }

    // The pencil and wave under her when you hover her: the pencil opens the typing box over her head, the wave turns her
    // voice on and off. A capsule, each half lighting up cyan under the pointer.
    internal sealed class TalkPill : Window
    {
        private readonly PillCanvas canvas = new PillCanvas();
        private readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private Func<PixelPoint> anchor;
        internal event Action TypeClicked;
        internal event Action VoiceClicked;
        internal const double Wide = 108, Tall = 40;

        internal TalkPill()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            Width = Wide; Height = Tall;
            ShowActivated = false;
            Content = canvas;
            canvas.PointerMoved += delegate(object sender, PointerEventArgs e) { int part = e.GetPosition(canvas).X < Wide / 2 ? 1 : 2; if (part != canvas.Hover) { canvas.Hover = part; canvas.InvalidateVisual(); } };
            canvas.PointerExited += delegate { canvas.Hover = 0; canvas.InvalidateVisual(); };
            canvas.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (e.GetPosition(canvas).X < Wide / 2) TypeClicked?.Invoke(); else VoiceClicked?.Invoke();
                e.Handled = true;
            };
            follow.Tick += delegate { if (IsVisible) Place(); else follow.Stop(); };
        }

        // Where its top-centre goes, in screen pixels; re-read every few frames.
        internal void Follow(Func<PixelPoint> topCentre) { anchor = topCentre; }

        internal bool Muted { set { canvas.Muted = value; canvas.InvalidateVisual(); } }
        internal bool Typing { set { canvas.Typing = value; canvas.InvalidateVisual(); } }
        internal bool Hovered { get { return canvas.Hover != 0; } }

        internal void Pop()
        {
            if (!IsVisible) { canvas.Born = DateTime.Now; Show(); Spaces.Front(this); }
            Place();
            follow.Start();
        }

        private void Place()
        {
            if (anchor == null) return;
            PixelPoint at = anchor();
            double scale = Screens.ScreenFromPoint(at)?.Scaling ?? Screens.Primary?.Scaling ?? 1;
            Position = new PixelPoint(at.X - (int)(Wide * scale / 2), at.Y);
        }
    }

    internal sealed class PillCanvas : Control
    {
        internal int Hover;
        internal bool Muted, Typing;
        internal DateTime Born = DateTime.Now;
        private static readonly Color Body = Color.FromArgb(240, 18, 19, 23), Rim = Color.FromRgb(74, 77, 88), Lit = Color.FromRgb(40, 42, 48), Grey = Color.FromRgb(225, 226, 232), Dim = Color.FromRgb(110, 112, 122);

        public override void Render(DrawingContext g)
        {
            double w = Bounds.Width, h = Bounds.Height;
            Rect plate = new Rect(2, 2, w - 4, h - 4);
            // It glitches in for a few frames when it shows: a couple of slivers slipped sideways.
            double age = (DateTime.Now - Born).TotalSeconds;
            if (age < 0.2) Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
            g.DrawRectangle(new SolidColorBrush(Body), new Pen(new SolidColorBrush(Rim), 1.5), plate, h / 2 - 2, h / 2 - 2);
            if (age < 0.2)
            {
                Random r = new Random((int)(age * 1000));
                for (int i = 0; i < 3; i++) g.FillRectangle(new SolidColorBrush(i % 2 == 0 ? CutFrame.Cyan : CutFrame.Red), new Rect(r.NextDouble() * w * 0.7, r.NextDouble() * h, w * 0.3, 1.5));
            }
            double mid = w / 2;
            g.DrawLine(new Pen(new SolidColorBrush(Rim), 1.2), new Point(mid, h * 0.28), new Point(mid, h * 0.72));
            Point left = new Point(w * 0.27, h / 2), right = new Point(w * 0.73, h / 2);
            if (Hover == 1 || Typing) g.DrawEllipse(new SolidColorBrush(Lit), null, left, h * 0.32, h * 0.32);
            if (Hover == 2) g.DrawEllipse(new SolidColorBrush(Lit), null, right, h * 0.32, h * 0.32);
            Pencil(g, left, h * 0.19, Hover == 1 || Typing ? CutFrame.Cyan : Grey);
            Wave(g, right, h * 0.19, Muted ? Dim : Hover == 2 ? CutFrame.Cyan : Grey, Muted);
        }

        // A square-cut pencil at 45 degrees.
        private static void Pencil(DrawingContext g, Point c, double r, Color colour)
        {
            using (g.PushTransform(Matrix.CreateRotation(Math.PI / 4) * Matrix.CreateTranslation(c.X, c.Y)))
            {
                double len = r * 2, wide = r * 0.62;
                StreamGeometry body = new StreamGeometry();
                using (StreamGeometryContext s = body.Open())
                {
                    s.BeginFigure(new Point(-wide / 2, -len / 2), true);
                    s.LineTo(new Point(wide / 2, -len / 2)); s.LineTo(new Point(wide / 2, len / 2 - wide * 0.8)); s.LineTo(new Point(0, len / 2)); s.LineTo(new Point(-wide / 2, len / 2 - wide * 0.8));
                    s.EndFigure(true);
                }
                g.DrawGeometry(new SolidColorBrush(colour), null, body);
                g.FillRectangle(new SolidColorBrush(Body), new Rect(-wide / 2, -len / 2 + wide * 0.55, wide, wide * 0.18));
            }
        }

        // Five rounded bars, tallest in the middle; struck through when her voice is off.
        private static void Wave(DrawingContext g, Point c, double r, Color colour, bool muted)
        {
            double[] tall = { 0.45, 0.8, 1.15, 0.8, 0.45 };
            double gap = r * 0.42;
            for (int i = 0; i < 5; i++)
            {
                double x = c.X + (i - 2) * gap, half = r * tall[i];
                g.DrawLine(new Pen(new SolidColorBrush(colour), r * 0.3, lineCap: PenLineCap.Round), new Point(x, c.Y - half), new Point(x, c.Y + half));
            }
            if (muted) g.DrawLine(new Pen(new SolidColorBrush(CutFrame.Red), r * 0.18, lineCap: PenLineCap.Round), new Point(c.X - r * 1.1, c.Y + r * 1.1), new Point(c.X + r * 1.1, c.Y - r * 1.1));
        }
    }
}
