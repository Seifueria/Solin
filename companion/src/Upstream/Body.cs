// Derived from cross/Pet.cs (class Body) in lachydotmcg/capri-desktop, commit 26224db. PolyForm Noncommercial 1.0.0.
// Trimmed for Solin: no hologram pass, no sleeping z's.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Solin.Companion.Upstream
{
    // The control that draws the current cell of the current clip, posed with a breath bob, a tilt and a facing.
    internal sealed class Body : Control
    {
        internal Clip Shown;
        internal int Cell;
        internal Clip Mouth;
        internal int Talking = -1;
        internal double Size = 240;
        internal double Breathe;
        internal double Tilt;
        internal bool FacingLeft;
        internal Point Centre;
        // 0 is whole, 1 is gone: she fades and tears into drifting horizontal slices on the way out of a teleport, and
        // reassembles on the way in. Solin's addition to the base's body.
        internal double Dissolve;
        // Drawn this much higher than her centre, so she can sit on a ledge her window cannot reach above the screen's top.
        internal double Lift;
        // 0 none, 1 heavy: a few horizontal slices of her jump sideways this frame, without fading. Solin's addition.
        internal double Glitch;
        // Squash and stretch about her feet: 1 is her true shape. Solin's addition.
        internal double ScaleX = 1, ScaleY = 1;
        // A sideways nudge of the whole body, for a knock-back. Solin's addition.
        internal double Shove;
        // A whole picture drawn in place of the clip, in her square (the mini face plate). Solin's addition.
        internal Avalonia.Media.Imaging.Bitmap Override;
        private Rect OverrideRect { get { return new Rect(Centre.X - Size / 2, Centre.Y - Size / 2, Size, Size); } }
        private static readonly Random tear = new Random(7);

        internal Rect Place(Clip shown)
        {
            double tall = Size * shown.Height;
            double wide = tall * shown.Wide / shown.Tall;
            // Mirrored by the pose, the cell's shift mirrors with it unless the artist drew a left-facing strip.
            double shiftX = FacingLeft && shown.HasFlipped ? -shown.ShiftX : shown.ShiftX;
            return new Rect(Centre.X - wide / 2 + shiftX * Size, Centre.Y - tall / 2 + shown.ShiftY * Size, wide, tall);
        }

        internal Matrix Pose
        {
            get
            {
                // Squash and stretch happen about her feet, so a landing flattens her downward rather than through the floor.
                double feetY = Centre.Y + Size * 0.49;
                return Matrix.CreateTranslation(-Centre.X, -Centre.Y)
                    * Matrix.CreateScale(FacingLeft ? -1 : 1, 1)
                    * Matrix.CreateRotation(Tilt * Math.PI / 180)
                    * Matrix.CreateTranslation(Centre.X, Centre.Y)
                    * Matrix.CreateTranslation(-Centre.X, -feetY) * Matrix.CreateScale(ScaleX, ScaleY) * Matrix.CreateTranslation(Centre.X, feetY)
                    * Matrix.CreateTranslation(Shove, Breathe - Lift);
            }
        }

        // Whether a point in this control lands on drawn art, so clicks beside her fall through to nothing.
        internal bool Opaque(Point at, int radius = 6)
        {
            if (Override != null)
            {
                // The plate fills the middle band of its square.
                Rect r = OverrideRect;
                return at.X >= r.X + r.Width * 0.04 && at.X <= r.Right - r.Width * 0.04 && at.Y >= r.Y + r.Height * 0.24 && at.Y <= r.Y + r.Height * 0.74;
            }
            Clip shown = Shown;
            if (shown == null || !Pose.TryInvert(out Matrix inverse)) return false;
            Point local = inverse.Transform(at);
            Rect place = Place(shown);
            double x = local.X - place.X, y = local.Y - place.Y;
            if (x < 0 || y < 0 || x >= place.Width || y >= place.Height) return false;
            return shown.Solid(Cell, (int)(x / place.Width * shown.Wide), (int)(y / place.Height * shown.Tall), radius, FacingLeft);
        }

        public override void Render(DrawingContext context)
        {
            if (Override != null)
            {
                using (context.PushTransform(Pose))
                using (context.PushOpacity(Math.Clamp(1 - Dissolve * 0.95, 0, 1)))
                    context.DrawImage(Override, new Rect(0, 0, Override.PixelSize.Width, Override.PixelSize.Height), OverrideRect);
                return;
            }
            Clip shown = Shown;
            if (shown == null) return;
            using (context.PushTransform(Pose))
            using (context.PushOpacity(Math.Clamp(1 - Dissolve * 0.95, 0, 1)))
            {
                Rect place = Place(shown);
                Rect source = new Rect(Cell * shown.Wide, 0, shown.Wide, shown.Tall);
                if (Dissolve <= 0.001 && Glitch <= 0.001)
                {
                    context.DrawImage(shown.Art(FacingLeft), source, place);
                    if (Talking >= 0 && Mouth != null)
                        context.DrawImage(Mouth.Art(false), new Rect(Talking * Mouth.Wide, 0, Mouth.Wide, Mouth.Tall), place);
                    return;
                }
                if (Dissolve <= 0.001)
                {
                    // Whole, but with a few slices knocked sideways: three to five bands, the rest in place.
                    context.DrawImage(shown.Art(FacingLeft), source, place);
                    int cuts = 3 + (int)(Glitch * 2);
                    for (int i = 0; i < cuts; i++)
                    {
                        double y0 = tear.NextDouble() * 0.9, h = 0.03 + tear.NextDouble() * 0.08;
                        double slide = (tear.NextDouble() * 2 - 1) * Glitch * Size * 0.12;
                        Rect from = new Rect(source.X, source.Y + y0 * source.Height, source.Width, h * source.Height);
                        Rect to = new Rect(place.X + slide, place.Y + y0 * place.Height, place.Width, h * place.Height);
                        context.DrawImage(shown.Art(FacingLeft), from, to);
                    }
                    if (Talking >= 0 && Mouth != null)
                        context.DrawImage(Mouth.Art(false), new Rect(Talking * Mouth.Wide, 0, Mouth.Wide, Mouth.Tall), place);
                    return;
                }
                // Torn into bands that slide sideways further the more dissolved she is, each band its own way.
                const int bands = 14;
                double bandTall = shown.Tall / (double)bands, bandHigh = place.Height / bands;
                for (int i = 0; i < bands; i++)
                {
                    double slide = (tear.NextDouble() * 2 - 1) * Dissolve * Dissolve * Size * 0.6;
                    Rect from = new Rect(source.X, i * bandTall, source.Width, bandTall);
                    Rect to = new Rect(place.X + slide, place.Y + i * bandHigh, place.Width, bandHigh);
                    context.DrawImage(shown.Art(FacingLeft), from, to);
                }
            }
        }
    }
}
