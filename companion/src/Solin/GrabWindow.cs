// Solin-owned; not derived from upstream. Plays the Mochii build's full-screen grab frames, read at runtime from the
// references folder through ArtLibrary (never copied into this repo). Timing and staging written fresh.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    // The "grab" entrance: her hands come in from the edges of the screen, she grabs it and leans in, says hi, waves, smiles,
    // smears away and shrinks down to where she lands. Click-through.
    internal sealed class GrabWindow : Window
    {
        private readonly GrabCanvas canvas = new GrabCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        internal event Action Finished;

        internal static bool Available { get { return ArtLibrary.Intro("grab") != null; } }

        internal GrabWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            Content = canvas;
            canvas.Beat += delegate(string what) { Beat?.Invoke(what); };
            frames.Tick += delegate { if (!canvas.Step()) { frames.Stop(); Hide(); Finished?.Invoke(); } };
        }

        // "hands", "grab", "hi", "wave", "smear": moments the companion can put a sound on.
        internal event Action<string> Beat;

        internal void Play(PixelPoint landing)
        {
            Screen screen = Screens.ScreenFromPoint(landing) ?? Screens.Primary;
            if (screen == null || !Available) { Finished?.Invoke(); return; }
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

    internal sealed class GrabCanvas : Control
    {
        private Size area;
        private Point landing;
        private readonly Random random = new Random();
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private string lastBeat;
        internal event Action<string> Beat;
        private static readonly Color Ink = Color.FromRgb(8, 6, 12), Pink = Color.FromRgb(255, 33, 82), Violet = Color.FromRgb(123, 61, 255), Paper = Color.FromRgb(255, 244, 246);

        // Picture size and where the "hi" tag sits in it.
        private const double PicW = 1672, PicH = 941, TagX = 1110, TagY = 175;
        private const double Tint = 0.3, Hands = 0.6, Grab = 0.8, Hi = 1.35, Wave = 2.05, Happy = 3.0, Smear = 3.45, Shrink = 3.7, End = 4.15;

        internal void Begin(Size size, Point land) { area = size; landing = land; lastBeat = null; clock.Restart(); InvalidateVisual(); }

        internal bool Step()
        {
            double t = clock.Elapsed.TotalSeconds;
            string beat = t < Hands ? null : t < Grab ? "hands" : t < Hi ? "grab" : t < Wave ? "hi" : t < Smear ? "wave" : t < End ? "smear" : null;
            if (beat != null && beat != lastBeat) { lastBeat = beat; Beat?.Invoke(beat); }
            InvalidateVisual();
            return t < End;
        }

        private static IBrush Brush(Color c, double a) { return new SolidColorBrush(Color.FromArgb((byte)(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B)); }
        private static double Ease(double x) { x = Math.Clamp(x, 0, 1); return 1 - Math.Pow(1 - x, 3); }

        private string FrameAt(double t)
        {
            if (t < Hi) return "grab";
            if (t < Wave) { string[] say = { "mouth1", "mouth2", "mouth3", "mouth2", "mouth1", "mouth3" }; return say[(int)((t - Hi) * 10) % say.Length]; }
            if (t < Happy) { string[] wave = { "wave1", "wave3", "wave2", "wave3", "wave1", "wave2" }; return wave[(int)((t - Wave) * 7) % wave.Length]; }
            if (t < Smear) return "happy";
            return "smear";
        }

        public override void Render(DrawingContext g)
        {
            double t = clock.Elapsed.TotalSeconds;
            double dim = t < Tint ? t / Tint : t < Shrink ? 1 : 1 - (t - Shrink) / (End - Shrink);
            g.FillRectangle(Brush(Ink, 0.55 * dim), new Rect(0, 0, area.Width, area.Height));
            Bitmap pic = ArtLibrary.Intro(FrameAt(t));
            if (pic == null) return;

            // Cover the screen with the picture, centred.
            double s = Math.Max(area.Width / PicW, area.Height / PicH);
            Rect full = new Rect((area.Width - PicW * s) / 2, (area.Height - PicH * s) / 2, PicW * s, PicH * s);
            Rect src = new Rect(0, 0, pic.PixelSize.Width, pic.PixelSize.Height);
            double ps = pic.PixelSize.Width / PicW;

            if (t < Hands) return;
            if (t < Grab)
            {
                // Only her hands, sliding in from the screen's edges.
                double k = Ease((t - Hands) / (Grab - Hands));
                double cut = 0.24;
                foreach (int side in new[] { 0, 1 })
                {
                    double slide = (1 - k) * full.Width * cut * (side == 0 ? -1 : 1);
                    Rect from = side == 0 ? new Rect(0, 0, PicW * cut * ps, PicH * ps) : new Rect((PicW * (1 - cut)) * ps, 0, PicW * cut * ps, PicH * ps);
                    Rect to = side == 0 ? new Rect(full.X + slide, full.Y, full.Width * cut, full.Height) : new Rect(full.Right - full.Width * cut + slide, full.Y, full.Width * cut, full.Height);
                    g.DrawImage(pic, from, to);
                }
                return;
            }
            Rect dest = full;
            if (t >= Shrink)
            {
                // Down to where she lands, shrinking.
                double k = Ease((t - Shrink) / (End - Shrink));
                double w = full.Width * (1 - k * 0.95), h = full.Height * (1 - k * 0.95);
                Point c = new Point(full.Center.X + (landing.X - full.Center.X) * k, full.Center.Y + (landing.Y - full.Center.Y) * k);
                dest = new Rect(c.X - w / 2, c.Y - h / 2, w, h);
            }
            double fade = t < Shrink ? 1 : 1 - (t - Shrink) / (End - Shrink);
            // The grab lands with a jolt: the picture shakes and tears for a moment.
            double jolt = t < Grab + 0.25 ? (1 - (t - Grab) / 0.25) : 0;
            Rect shaken = new Rect(dest.X + (random.NextDouble() - 0.5) * 24 * jolt, dest.Y + (random.NextDouble() - 0.5) * 16 * jolt, dest.Width, dest.Height);
            using (g.PushOpacity(fade))
            {
                g.DrawImage(pic, src, shaken);
                if (jolt > 0)
                    for (int i = 0; i < 6; i++)
                    {
                        double y0 = random.NextDouble(), hh = 0.02 + random.NextDouble() * 0.05, dx = (random.NextDouble() - 0.5) * 80 * jolt;
                        g.DrawImage(pic, new Rect(0, y0 * src.Height, src.Width, hh * src.Height), new Rect(shaken.X + dx, shaken.Y + y0 * shaken.Height, shaken.Width, hh * shaken.Height));
                    }
            }
            // The "hi" tag by her face while she says it and waves.
            if (t >= Hi && t < Happy)
            {
                double pop = Ease((t - Hi) / 0.15);
                double x = full.X + TagX * s, y = full.Y + TagY * s;
                Rect tag = new Rect(x, y, 64 * s * 2.2 * pop, 48 * s * 2.2 * pop);
                g.FillRectangle(Brush(Pink, 1), tag, 4f);
                FormattedText hi = new FormattedText("hi", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(TalkLook.Mono, FontStyle.Normal, FontWeight.Bold), 34 * s * 2 * pop, Brushes.White);
                g.DrawText(hi, new Point(tag.X + 10 * s, tag.Y + 2 * s));
                FormattedText sub = new FormattedText("PROJECT SOLIN", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(TalkLook.Mono), 9 * s * 2, Brush(Paper, pop));
                g.DrawText(sub, new Point(tag.X, tag.Bottom + 4));
            }
            // Glitch bars across the screen as she arrives and leaves.
            if (t < Grab + 0.4 || t > Smear)
                for (int i = 0; i < 5; i++)
                {
                    double y = random.NextDouble() * area.Height, w = area.Width * (0.1 + random.NextDouble() * 0.4);
                    g.FillRectangle(Brush(i % 2 == 0 ? Violet : Pink, 0.7 * dim), new Rect(random.NextDouble() * (area.Width - w), y, w, 2 + random.NextDouble() * 5));
                }
        }
    }
}
