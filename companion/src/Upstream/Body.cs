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
        private static readonly Random tear = new Random(7);

        internal Rect Place(Clip shown)
        {
            double tall = Size * shown.Height;
            double wide = tall * shown.Wide / shown.Tall;
            return new Rect(Centre.X - wide / 2 + shown.ShiftX * Size, Centre.Y - tall / 2 + shown.ShiftY * Size, wide, tall);
        }

        internal Matrix Pose
        {
            get
            {
                return Matrix.CreateTranslation(-Centre.X, -Centre.Y)
                    * Matrix.CreateScale(FacingLeft ? -1 : 1, 1)
                    * Matrix.CreateRotation(Tilt * Math.PI / 180)
                    * Matrix.CreateTranslation(Centre.X, Centre.Y + Breathe);
            }
        }

        // Whether a point in this control lands on drawn art, so clicks beside her fall through to nothing.
        internal bool Opaque(Point at, int radius = 6)
        {
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
            Clip shown = Shown;
            if (shown == null) return;
            using (context.PushTransform(Pose))
            using (context.PushOpacity(Math.Clamp(1 - Dissolve * 0.95, 0, 1)))
            {
                Rect place = Place(shown);
                Rect source = new Rect(Cell * shown.Wide, 0, shown.Wide, shown.Tall);
                if (Dissolve <= 0.001)
                {
                    context.DrawImage(shown.Art(FacingLeft), source, place);
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
