// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;

namespace Solin.Companion
{
    // Stand-in art read at runtime from the reference checkouts on this Mac, never copied into the repo: the Mochii build's
    // mood faces, cut-ins and full-screen grab frames, and the Edgerunners build's mini face plates. Their art is not covered
    // by those projects' licences, so it stays where it is and is only shown locally, until Solin's own art replaces it.
    // Every lookup returns null when the file is not there, and every caller falls back to what it drew before.
    internal static class ArtLibrary
    {
        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();
        private static string root;

        // SOLIN_ART, else ~/Capri References.
        internal static string Root
        {
            get
            {
                if (root != null) return root;
                string env = Environment.GetEnvironmentVariable("SOLIN_ART");
                root = !string.IsNullOrEmpty(env) ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Capri References");
                return root;
            }
        }

        internal static bool Present { get { return Directory.Exists(Path.Combine(Root, "Mochii", "assets", "capri")); } }

        private static Bitmap Load(params string[] parts)
        {
            string path = Path.Combine(Root, Path.Combine(parts));
            lock (cache)
            {
                if (cache.TryGetValue(path, out Bitmap hit)) return hit;
                Bitmap bmp = null;
                try { if (File.Exists(path)) using (FileStream f = File.OpenRead(path)) bmp = new Bitmap(f); }
                catch (Exception) { bmp = null; }
                cache[path] = bmp;
                return bmp;
            }
        }

        // Solin's moods onto the faces there are.
        private static readonly Dictionary<string, string> FaceFor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["neutral"] = "calm", ["calm"] = "calm", ["warm"] = "laugh", ["curious"] = "look", ["teasing"] = "smug",
            ["annoyed"] = "wide", ["tired"] = "aside", ["excited"] = "many",
        };

        internal static Bitmap Face(string mood)
        {
            string name = mood != null && FaceFor.TryGetValue(mood, out string f) ? f : "calm";
            return Load("Mochii", "assets", "capri", "face-" + name + ".png");
        }

        // eyes, grin, laugh, look, many.
        internal static Bitmap CutIn(string kind) { return Load("Mochii", "assets", "capri", "cutin-" + kind + ".png"); }

        // grab, happy, mouth1..3, smear, wave1..3.
        internal static Bitmap Intro(string frame) { return Load("Mochii", "assets", "capri", "intro-" + frame + ".webp"); }

        // idle, blink, look_left, look_right, talk, talk2, annoyed, annoyed_talk, glitch.
        internal static Bitmap Mini(string state) { return Load("Edgerunners", "assets", "desk", "mini", state + ".png"); }
    }
}
