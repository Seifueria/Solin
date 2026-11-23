// Solin-owned; not derived from upstream. Plays the two drawn emotes that ship with lachydotmcg/capri-desktop (the chair
// and the mini heart, by Qewiw), read at runtime from the references folder and never copied into this repo. The frame
// list format and the placement rules (chair at her size with her feet where she stands, its close-up the height of the
// screen against the far edge, the heart covering the screen) are as the base describes them; the player is written fresh.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion
{
    internal sealed class EmoteClip
    {
        internal const double Fps = 30;
        internal int Width, Height, CloseWidth, CloseHeight, Count;
        private readonly Dictionary<string, byte[]> pictures = new Dictionary<string, byte[]>();
        internal readonly List<(string pic, int x, int y)> Her = new List<(string, int, int)>();
        internal readonly List<(string pic, int x, int y)> Close = new List<(string, int, int)>();
        private readonly Dictionary<string, Bitmap> decoded = new Dictionary<string, Bitmap>();
        private readonly Queue<string> order = new Queue<string>();

        internal static EmoteClip Load(string name)
        {
            string path = Path.Combine(ArtLibrary.Root, "Lachy", "assets", "emotes", name + ".zip");
            if (!File.Exists(path)) return null;
            try
            {
                EmoteClip clip = new EmoteClip();
                string[] lines;
                using (ZipArchive zip = ZipFile.OpenRead(path))
                {
                    foreach (ZipArchiveEntry e in zip.Entries)
                        if (e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                            using (Stream s = e.Open()) using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); clip.pictures[e.Name] = m.ToArray(); }
                    using (StreamReader r = new StreamReader(zip.GetEntry("frames.txt").Open())) lines = r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }
                foreach (string raw in lines)
                {
                    string[] p = raw.Trim().Split(' ');
                    if (p[0] == "size") { clip.Width = int.Parse(p[1]); clip.Height = int.Parse(p[2]); continue; }
                    if (p[0] == "closeup") { clip.CloseWidth = int.Parse(p[1]); clip.CloseHeight = int.Parse(p[2]); continue; }
                    if (!int.TryParse(p[0], out _)) continue;
                    // "frame her x y [close x y]", "-" for an empty layer.
                    int next = 1;
                    if (p[next] == "-") { clip.Her.Add((null, 0, 0)); next++; }
                    else { clip.Her.Add((p[next], int.Parse(p[next + 1]), int.Parse(p[next + 2]))); next += 3; }
                    if (p.Length >= next + 3) clip.Close.Add((p[next], int.Parse(p[next + 1]), int.Parse(p[next + 2])));
                    else clip.Close.Add((null, 0, 0));
                }
                clip.Count = clip.Her.Count;
                return clip.Count > 0 && clip.Width > 0 ? clip : null;
            }
            catch (Exception) { return null; }
        }

        // Decodes a picture, keeping the last few.
        internal Bitmap Picture(string name)
        {
            if (name == null) return null;
            if (decoded.TryGetValue(name, out Bitmap hit)) return hit;
            if (!pictures.TryGetValue(name, out byte[] data)) return null;
            Bitmap bmp;
            using (MemoryStream m = new MemoryStream(data)) bmp = new Bitmap(m);
            decoded[name] = bmp; order.Enqueue(name);
            while (order.Count > 24) { string old = order.Dequeue(); if (decoded.TryGetValue(old, out Bitmap b)) { decoded.Remove(old); b.Dispose(); } }
            return bmp;
        }
    }

    // A layer's placement in the window: scale, horizontal scale (negative is mirrored), and where the clip's top left lands.
    internal struct EmotePlace { public double Scale, Across, Left, Top; }

    internal sealed class EmoteWindow : Window
    {
        private const double ChairFootX = 609, ChairFootY = 412, ChairSize = 240;
        private readonly EmoteCanvas canvas = new EmoteCanvas();
        private readonly DispatcherTimer frames = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / EmoteClip.Fps) };
        private readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private static readonly Dictionary<string, EmoteClip> clips = new Dictionary<string, EmoteClip>();
        internal event Action Finished;
        internal bool Running { get { return frames.IsEnabled; } }

        internal EmoteWindow()
        {
            TalkLook.Floating(this);
            SizeToContent = SizeToContent.Manual;
            ShowActivated = false;
            IsHitTestVisible = false;
            Spaces.ClickThrough(this);
            Content = canvas;
            frames.Tick += delegate
            {
                int frame = (int)(clock.Elapsed.TotalSeconds * EmoteClip.Fps);
                if (canvas.Playing == null || frame >= canvas.Playing.Count) { frames.Stop(); canvas.Playing = null; Hide(); Finished?.Invoke(); return; }
                canvas.Frame = frame;
                canvas.InvalidateVisual();
            };
        }

        internal static EmoteClip Load(string name)
        {
            lock (clips) { if (!clips.TryGetValue(name, out EmoteClip c)) clips[name] = c = EmoteClip.Load(name); return c; }
        }

        internal static bool Available(string name) { return File.Exists(Path.Combine(ArtLibrary.Root, "Lachy", "assets", "emotes", name + ".zip")); }

        // foot: her feet in screen pixels; size: her size in points. The whole screen she is on is the stage.
        internal bool Play(string name, PixelPoint foot, double size)
        {
            EmoteClip clip = Load(name);
            Screen screen = Screens.ScreenFromPoint(foot) ?? Screens.Primary;
            if (clip == null || screen == null) return false;
            double scale = screen.Scaling;
            PixelRect area = screen.Bounds;
            Position = area.Position;
            Width = area.Width / scale; Height = area.Height / scale;
            Point f = new Point((foot.X - area.X) / scale, (foot.Y - area.Y) / scale);
            bool mirrored = f.X < Width / 2;
            canvas.Playing = clip;
            canvas.Frame = 0;
            if (name == "chair")
            {
                double s = size / ChairSize;
                canvas.HerPlace = new EmotePlace { Scale = s, Across = mirrored ? -s : s, Left = f.X + (mirrored ? ChairFootX : -ChairFootX) * s, Top = f.Y - ChairFootY * s };
                // The big close-up across the far side of the screen is left out (Kaelen's call); only her in the chair.
                canvas.ClosePlace = new EmotePlace();
            }
            else
            {
                double s = Math.Max(Width / clip.Width, Height / clip.Height);
                canvas.HerPlace = new EmotePlace { Scale = s, Across = s, Left = (Width - clip.Width * s) / 2, Top = (Height - clip.Height * s) / 2 };
                canvas.ClosePlace = new EmotePlace();
            }
            clock.Restart();
            if (!IsVisible) Show();
            Spaces.Front(this);
            frames.Start();
            return true;
        }
    }

    internal sealed class EmoteCanvas : Control
    {
        internal EmoteClip Playing;
        internal int Frame;
        internal EmotePlace HerPlace, ClosePlace;

        private void Layer(DrawingContext g, (string pic, int x, int y) cell, EmotePlace place)
        {
            if (cell.pic == null || place.Scale <= 0) return;
            Bitmap bmp = Playing.Picture(cell.pic);
            if (bmp == null) return;
            using (g.PushTransform(Matrix.CreateScale(place.Across, place.Scale) * Matrix.CreateTranslation(place.Left, place.Top)))
                g.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), new Rect(cell.x, cell.y, bmp.PixelSize.Width, bmp.PixelSize.Height));
        }

        public override void Render(DrawingContext g)
        {
            if (Playing == null || Frame >= Playing.Count) return;
            Layer(g, Playing.Close[Frame], ClosePlace);
            Layer(g, Playing.Her[Frame], HerPlace);
        }
    }
}
