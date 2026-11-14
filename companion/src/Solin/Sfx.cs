// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Solin.Companion
{
    // Little sounds made from maths, not files: each cue is rendered once to a WAV in the temp folder and played with afplay.
    // SOLIN_SFX sets the level, 0 to 1; 0 or "off" silences her. (An idea described from mochiiwuff/capri-desktop; no code taken.)
    internal sealed class Sfx
    {
        private const int Rate = 22050;
        private double level;
        // 0 to 1; the cues are re-rendered at the new level.
        internal double Level { get { return level; } set { double next = Math.Clamp(value, 0, 1); if (Math.Abs(next - level) < 0.001) return; level = next; RenderAll(); } }
        private readonly string folder = Path.Combine(Path.GetTempPath(), "solin-sfx");
        private readonly Dictionary<string, string> files = new Dictionary<string, string>();

        internal Sfx()
        {
            string set = Environment.GetEnvironmentVariable("SOLIN_SFX");
            level = string.IsNullOrEmpty(set) ? 0.5 : set == "off" ? 0 : double.TryParse(set, out double v) ? Math.Clamp(v, 0, 1) : 0.5;
            RenderAll();
        }

        private void RenderAll()
        {
            files.Clear();
            if (level <= 0) return;
            try
            {
                Directory.CreateDirectory(folder);
                Render("blip", 0.07, t => Tone(t, 880 + 400 * t / 0.07) * Env(t, 0.07, 0.004));
                Render("zap", 0.32, t => (Tone(t, 1400 - 1100 * t / 0.32) * 0.7 + Noise(t) * 0.3) * Env(t, 0.32, 0.01));
                Render("chime", 0.5, t => (Tone(t, 659) * (t < 0.25 ? 1 : 0.35) + (t >= 0.2 ? Tone(t, 988) : 0)) * Env(t, 0.5, 0.01) * 0.6);
                Render("buzz", 0.28, t => Square(t, 110) * Env(t, 0.28, 0.005) * 0.5);
                Render("tick", 0.03, t => Tone(t, 1900) * Env(t, 0.03, 0.002) * 0.6);
                Render("soft", 0.45, t => Tone(t, 523) * Env(t, 0.45, 0.06) * 0.5);
            }
            catch (Exception) { files.Clear(); }
        }

        private static double Tone(double t, double hz) { return Math.Sin(2 * Math.PI * hz * t); }
        private static double Square(double t, double hz) { return Math.Sin(2 * Math.PI * hz * t) >= 0 ? 1 : -1; }
        private static double Noise(double t) { return Math.Sin(t * 12345.678) * Math.Sin(t * 987.65); }
        // Quick attack, then a fade to nothing by the end.
        private static double Env(double t, double length, double attack) { return Math.Min(1, t / attack) * Math.Max(0, 1 - t / length); }

        private void Render(string name, double seconds, Func<double, double> wave)
        {
            int count = (int)(Rate * seconds);
            byte[] pcm = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                short sample = (short)(Math.Clamp(wave(i / (double)Rate) * level, -1, 1) * short.MaxValue * 0.8);
                pcm[i * 2] = (byte)sample;
                pcm[i * 2 + 1] = (byte)(sample >> 8);
            }
            string path = Path.Combine(folder, name + ".wav");
            using (FileStream f = File.Create(path))
            using (BinaryWriter w = new BinaryWriter(f))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + pcm.Length); w.Write("WAVE".ToCharArray());
                w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(pcm.Length); w.Write(pcm);
            }
            files[name] = path;
        }

        internal void Play(string name)
        {
            if (!files.TryGetValue(name, out string path) || !OperatingSystem.IsMacOS()) return;
            try { Process.Start(new ProcessStartInfo("/usr/bin/afplay", "\"" + path + "\"") { UseShellExecute = false, CreateNoWindow = true })?.Dispose(); }
            catch (Exception) { }
        }
    }
}
