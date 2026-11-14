// Derived from cross/Pet.cs (class Clip) in lachydotmcg/capri-desktop, commit 26224db. PolyForm Noncommercial 1.0.0.
// Trimmed for Solin: no legacy cell sizes, no hologram, no background loading of the mirrored strip.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Solin.Companion.Upstream
{
    // One animation: a horizontal PNG strip of equal cells, played at a fixed rate.
    internal sealed class Clip
    {
        private const string Prefix = "Solin.Sprite.";
        private Bitmap strip;
        private Bitmap flipped;
        private bool triedFlipped;

        internal readonly string Title;
        internal readonly int Count;
        internal readonly double Fps;
        // Drawn height as a share of the body's square.
        internal readonly double Height;
        // How far a cell's middle sits from the middle of the body's square, as a share of its size.
        internal readonly double ShiftX, ShiftY;

        internal Clip(string title, int count, double fps, double height, double shiftX = 0, double shiftY = 0)
        {
            Title = title;
            Count = count;
            Fps = fps;
            Height = height;
            ShiftX = shiftX;
            ShiftY = shiftY;
        }

        internal Bitmap Strip { get { if (strip == null) strip = Read(Prefix + Title + ".png"); return strip; } }

        internal bool HasFlipped { get { return Has(Prefix + Title + "_flipped.png"); } }

        // The strip facing left, when the artist drew one; otherwise the normal strip, which the body mirrors itself.
        internal Bitmap Art(bool left)
        {
            if (!left || !HasFlipped) return Strip;
            if (!triedFlipped) { triedFlipped = true; flipped = Read(Prefix + Title + "_flipped.png"); }
            return flipped ?? Strip;
        }

        internal int Wide { get { return Strip.PixelSize.Width / Count; } }
        internal int Tall { get { return Strip.PixelSize.Height; } }

        // Whether any pixel within radius of (x, y) in the given cell is drawn, for hit testing the art rather than its box.
        internal bool Solid(int cell, int x, int y, int radius, bool facingLeft = false)
        {
            Bitmap source = Art(facingLeft);
            int left = cell * Wide + Math.Max(0, x - radius);
            int right = cell * Wide + Math.Min(Wide, x + radius + 1);
            int top = Math.Max(0, y - radius);
            int bottom = Math.Min(source.PixelSize.Height, y + radius + 1);
            if (right <= left || bottom <= top) return false;
            int wide = right - left;
            byte[] pixels = new byte[wide * (bottom - top) * 4];
            GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { source.CopyPixels(new PixelRect(left, top, wide, bottom - top), pin.AddrOfPinnedObject(), pixels.Length, wide * 4); }
            finally { pin.Free(); }
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 24) return true;
            return false;
        }

        private static bool Has(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceInfo(name) != null;
        }

        private static Bitmap Read(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new FileNotFoundException("Missing sprite resource " + name);
                return new Bitmap(stream);
            }
        }
    }
}
