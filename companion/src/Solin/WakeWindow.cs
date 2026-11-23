// Solin-owned; not derived from upstream.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // The "wake" entrance across the whole screen: it dims, a giant jagged eye opens, its iris spins and the pupil
    // tightens, it blinks, snaps shut to a line, and the line collapses to a dot where she stands. Click-through.
    // (Idea described from edgerunners-2/capri-ai-desktop's Wake intro; drawn fresh in Solin's colours, no art taken.)
    internal sealed class WakeWindow : Window
    {
        private readonly WakeCanvas canvas = new WakeCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        internal event Action Finished;

        internal WakeWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            Content = canvas;
            frames.Tick += delegate { if (!canvas.Step()) { frames.Stop(); Hide(); Finished?.Invoke(); } };
        }

        // landing: where she will appear, in screen pixels; the eye fills the screen that point is on.
        internal void Play(PixelPoint landing)
        {
            Screen screen = Screens.ScreenFromPoint(landing) ?? Screens.Primary;
            if (screen == null) { Finished?.Invoke(); return; }
            double scale = screen.Scaling;
            PixelRect area = screen.Bounds;
            Position = area.Position;
            Width = area.Width / scale; Height = area.Height / scale;
            canvas.Begin(new Size(Width, Height), new Point((landing.X - area.X) / scale, (landing.Y - area.Y) / scale));
            if (!IsVisible) Show();
            Spaces.Front(this);
            frames.Start();
        }
    }

    internal sealed class WakeCanvas : Control
    {
        private Size area;
        private Point landing;
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private static readonly Color Pink = Color.FromRgb(232, 72, 120), PinkLight = Color.FromRgb(246, 128, 168), Cyan = Color.FromRgb(58, 214, 235),
            CyanDeep = Color.FromRgb(20, 130, 170), Violet = Color.FromRgb(123, 61, 255), Paper = Color.FromRgb(245, 242, 248), Ink = Color.FromRgb(6, 5, 9);
        // Dim in, open, look, blink, shut, collapse.
        private const double Dim = 0.25, Open = 0.75, Blink = 1.55, Shut = 2.05, Collapse = 2.35, End = 2.6;

        internal void Begin(Size size, Point land) { area = size; landing = land; clock.Restart(); InvalidateVisual(); }

        internal bool Step() { InvalidateVisual(); return clock.Elapsed.TotalSeconds < End; }

        private static IBrush Tint(Color c, double a) { return new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B)); }
        private static double Ease(double x) { x = Math.Clamp(x, 0, 1); return 1 - Math.Pow(1 - x, 3); }

        // The jagged almond: points along a lens shape, alternately pushed out to make spikes.
        private static StreamGeometry Almond(Point c, double halfW, double halfH, double spike)
        {
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                int n = 26;
                for (int side = 0; side < 2; side++)
                    for (int i = 0; i <= n; i++)
                    {
                        double k = i / (double)n;
                        double x = c.X - halfW + 2 * halfW * (side == 0 ? k : 1 - k);
                        double lens = Math.Sin(Math.PI * (side == 0 ? k : 1 - k));
                        double push = (i % 2 == 0 ? 1 : 0.55) * spike * lens;
                        double y = c.Y + (side == 0 ? -1 : 1) * (halfH * lens + push);
                        if (side == 0 && i == 0) ctx.BeginFigure(new Point(x, y), true); else ctx.LineTo(new Point(x, y));
                    }
                ctx.EndFigure(true);
            }
            return g;
        }

        public override void Render(DrawingContext g)
        {
            double t = clock.Elapsed.TotalSeconds;
            Point centre = new Point(area.Width / 2, area.Height / 2);
            double dim = t < Dim ? t / Dim : t < Collapse ? 1 : 1 - (t - Collapse) / (End - Collapse);
            g.FillRectangle(Tint(Ink, 0.86 * dim), new Rect(0, 0, area.Width, area.Height));

            // How open the lids are: open, a blink in the middle, then shut.
            double open = t < Dim ? 0 : t < Open ? Ease((t - Dim) / (Open - Dim)) : t < Blink ? 1
                : t < Blink + 0.16 ? 1 - Math.Sin((t - Blink) / 0.16 * Math.PI) * 0.92 : t < Shut ? 1 : Math.Max(0, 1 - (t - Shut) / (Collapse - Shut));
            double halfW = area.Width * 0.44, halfH = area.Height * 0.38;
            if (t < Collapse && open > 0.01)
            {
                double h = halfH * open, spike = area.Height * 0.06 * open;
                // Shadow edges, then the plate, then a highlight facet.
                g.DrawGeometry(Tint(Violet, 1), null, Almond(new Point(centre.X + 6, centre.Y + 6), halfW, h, spike));
                g.DrawGeometry(Tint(Cyan, 1), null, Almond(new Point(centre.X - 4, centre.Y - 4), halfW, h, spike));
                g.DrawGeometry(Tint(Pink, 1), null, Almond(centre, halfW * 0.985, h * 0.97, spike * 0.95));
                StreamGeometry facet = new StreamGeometry();
                using (StreamGeometryContext c = facet.Open())
                {
                    c.BeginFigure(new Point(centre.X - halfW * 0.62, centre.Y - h * 0.62), true);
                    c.LineTo(new Point(centre.X - halfW * 0.28, centre.Y - h * 0.92)); c.LineTo(new Point(centre.X - halfW * 0.48, centre.Y - h * 0.2)); c.EndFigure(true);
                }
                g.DrawGeometry(Tint(PinkLight, 0.9), null, facet);

                // The iris, clipped to the open lids: black ring, cyan ring, spinning spokes, a pupil that tightens.
                double r = Math.Min(halfH, area.Width * 0.22);
                using (g.PushGeometryClip(Almond(centre, halfW * 0.985, h * 0.97, 0)))
                {
                    g.DrawEllipse(Tint(Ink, 1), null, centre, r, r);
                    g.DrawEllipse(null, new Pen(Tint(Cyan, 1), r * 0.06), centre, r * 0.88, r * 0.88);
                    g.DrawEllipse(Tint(CyanDeep, 1), null, centre, r * 0.72, r * 0.72);
                    double spin = t * 1.6;
                    for (int i = 0; i < 48; i++)
                    {
                        double a = spin + i * Math.PI * 2 / 48;
                        double inner = r * 0.38, outer = r * (i % 2 == 0 ? 0.7 : 0.62);
                        g.DrawLine(new Pen(Tint(Ink, 0.9), r * 0.03), new Point(centre.X + Math.Cos(a) * inner, centre.Y + Math.Sin(a) * inner), new Point(centre.X + Math.Cos(a) * outer, centre.Y + Math.Sin(a) * outer));
                    }
                    g.DrawEllipse(Tint(Cyan, 1), null, centre, r * 0.42, r * 0.42);
                    double pupil = r * (0.3 - 0.1 * Ease((t - Open) / 0.5));
                    g.DrawEllipse(Tint(Paper, 1), null, centre, pupil, pupil);
                }
            }
            else if (t >= Shut && t < End)
            {
                // Shut: a bright line that shrinks to a dot over where she will stand, then winks out.
                double k = Ease((t - Collapse) / (End - Collapse));
                Point from = centre, to = landing;
                Point at = new Point(from.X + (to.X - from.X) * k, from.Y + (to.Y - from.Y) * k);
                double w = Math.Max(6, halfW * 2 * (1 - k)), h = Math.Max(6, 6 * (1 - k) + 2);
                g.FillRectangle(Tint(Violet, 1 - k * 0.5), new Rect(at.X - w / 2 - 3, at.Y - h / 2 - 3, w + 6, h + 6), 3f);
                g.FillRectangle(Tint(Paper, 1 - k * 0.3), new Rect(at.X - w / 2, at.Y - h / 2, w, h), 3f);
            }
        }
    }
}
