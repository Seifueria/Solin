// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;

namespace Solin.Companion
{
    // How a mood shows in her idle: breath and tilt pace, how big the breath is, how often she blinks.
    internal readonly struct MoodLook
    {
        internal readonly double BreathRate, BreathDepth, TiltRate, TiltDepth, BlinkChance;
        internal MoodLook(double breathRate, double breathDepth, double tiltRate, double tiltDepth, double blinkChance)
        {
            BreathRate = breathRate; BreathDepth = breathDepth; TiltRate = tiltRate; TiltDepth = tiltDepth; BlinkChance = blinkChance;
        }
    }

    // The backend's mood and trust, and the table from mood to look. The backend's vocabulary is the one in core_ai/ai_logic.py.
    internal sealed class MoodState
    {
        private static readonly Dictionary<string, MoodLook> Looks = new Dictionary<string, MoodLook>(StringComparer.OrdinalIgnoreCase)
        {
            ["neutral"] = new MoodLook(1.75, 4.5, 0.95, 0.65, 0.55),
            ["warm"]    = new MoodLook(1.5, 5.5, 0.8, 1.0, 0.6),
            ["curious"] = new MoodLook(2.3, 4.0, 1.6, 1.6, 0.7),
            ["annoyed"] = new MoodLook(3.0, 3.0, 2.4, 0.4, 0.3),
            ["calm"]    = new MoodLook(1.1, 6.0, 0.6, 0.5, 0.5),
            ["tired"]   = new MoodLook(0.8, 7.0, 0.5, 1.4, 0.85),
            ["teasing"] = new MoodLook(2.0, 4.5, 1.3, 2.0, 0.65),
        };

        internal string Mood { get; private set; } = "neutral";
        internal double Trust { get; private set; } = 0.5;
        internal event Action<MoodState> Changed;

        internal MoodLook Look { get { return Looks.TryGetValue(Mood, out MoodLook look) ? look : Looks["neutral"]; } }

        internal void Set(string mood, double trust)
        {
            string next = string.IsNullOrEmpty(mood) ? "neutral" : mood;
            bool moodChanged = next != Mood;
            if (!moodChanged && Math.Abs(trust - Trust) < 1e-9) return;
            Mood = next;
            Trust = trust;
            Changed?.Invoke(this);
            if (moodChanged) MoodChanged?.Invoke(next);
        }

        // Fires only when the mood itself moves, not on a trust nudge alone.
        internal event Action<string> MoodChanged;

        // The body reaction a fresh mood earns: annoyed jolts, tired shakes it off, the rest dip in acknowledgement.
        internal static string ReactionFor(string mood)
        {
            if (mood == "annoyed") return "jolt";
            if (mood == "tired") return "shake";
            return "dip";
        }
    }
}
