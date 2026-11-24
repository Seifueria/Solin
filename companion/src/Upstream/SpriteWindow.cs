// Derived from cross/Pet.cs in lachydotmcg/capri-desktop, commit 26224db (window setup, Play, the idle and blink part of
// Advance, and the drag handlers). PolyForm Noncommercial 1.0.0.
// Trimmed for Solin: one idle loop with blinks, breathing and drag-to-move. Perching, walking, falling, menus, talk and
// the brain are not here; Solin's own state drives what gets added.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Solin.Companion.Upstream
{
    internal sealed class SpriteWindow : Window
    {
        // The idle set is 60 cells at 24 fps, drawn 1.11 squares tall, its cells cut to the outline 11 px right of centre in 384 px art.
        private const double IdleShiftX = 11.0 / 384 * 1.11;
        // The closed eye sits on cells 25 to 32 of that loop.
        private const int IdleBlinkFrom = 24;
        // Room around her square so a tilt or a bob never clips at the window's edge.
        private const double Room = 1.25;

        private readonly Clip idle = new Clip("idle", 60, 24, 1.11, IdleShiftX);
        private readonly Clip idleBlink = new Clip("idle_blink", 8, 24, 1.11, IdleShiftX);
        private readonly Clip idleBlinkGlitch = new Clip("idle_blink_glitch", 8, 24, 1.11, IdleShiftX);
        // Her mouth for the idle face, five cells cycled while she talks, drawn over the frame where it sits in the cell.
        private readonly Clip idleMouth = new Clip("idle_mouth", 5, 12, 1.11, IdleShiftX);
        // Ten cells of static, played once on the way out of a spot and once on the way in, slowed so the tear reads.
        private readonly Clip glitch = new Clip("glitch", 10, 9, 1.11);
        // Hanging from the cursor while held, and tumbling while thrown.
        private readonly Clip dangle = new Clip("dangle", 24, 10, 1.11);
        // The same arm-out art standing on the floor: its feet drawn 9 pixels higher than the idle's, so it is let down that
        // much. For pointing, stargazing and now and then as her idle. Solin's addition.
        private readonly Clip dangleStand = new Clip("dangle", 24, 10, 1.11, 0, 9.0 / 384 * 1.11);
        // Her old standing idle with her arm out (the base's idle_legacy, with its own blink and mouth), read at runtime from
        // the references folder. Its feet sit 20 pixels right of the idle's, so it is shifted back that much. Without the art,
        // the arm-out dangle stands in.
        private static string LegacyArt(string name) { return System.IO.Path.Combine(ArtLibrary.Root, "Lachy", "assets", "sprites", name + ".png"); }
        private const double LegacyShiftX = -20.0 / 384 * 1.11;
        private readonly Clip legacy = new Clip("idle_legacy", 16, 8, 1.11, LegacyShiftX, 0, LegacyArt("idle_legacy"));
        private readonly Clip legacyBlink = new Clip("blink", 4, 8, 1.11, LegacyShiftX, 0, LegacyArt("blink"));
        private readonly Clip legacyMouth = new Clip("mouth", 5, 12, 1.11, LegacyShiftX, 0, LegacyArt("mouth"));
        private Clip standClip;
        private Clip stand { get { return standClip ??= legacy.Available ? legacy : dangleStand; } }
        private bool legacyBlinking;
        private int legacyCycle = -1;
        // Asleep on her feet while you are away, and the stretch when you come back.
        private readonly Clip sleep = new Clip("sleep", 16, 6, 1.11);
        // Sitting on a window's top edge: 60 cells at 30 fps in cells cut to her outline, 26 px left of her square's middle
        // and 36 px down it in 384 px art; her blink there loops cells 29 to 36.
        private const double PerchShiftX = -26.0 / 384, PerchShiftY = 36.0 / 384;
        private const int PerchBlinkFrom = 28;
        private readonly Clip perch = new Clip("perch", 60, 30, 1, PerchShiftX, PerchShiftY);
        private readonly Clip perchBlink = new Clip("perch_blink", 8, 30, 1, PerchShiftX, PerchShiftY);
        private readonly Clip perchTalk = new Clip("perch_talk", 60, 30, 1, PerchShiftX, PerchShiftY);
        private readonly Clip perchGlitch = new Clip("perch_glitch", 10, 15, 1, PerchShiftX, PerchShiftY);
        // Lying along a window's top edge, after she has sat a while: the base's laydown set, 60 cells at 30 fps, drawn
        // smaller and shifted down so her hip stays on the ledge.
        private const double LieScale = 0.85;
        private const double LieShiftY = (SitY - 0.5) * (1 - LieScale) + 94.0 / 384 * LieScale;
        private const double LieHeight = 250.0 / 384 * LieScale;
        private readonly Clip laydown = new Clip("laydown", 60, 30, LieHeight, 0, LieShiftY);
        private readonly Clip laydownTalk = new Clip("laydown_talk", 60, 30, LieHeight, 0, LieShiftY);
        private readonly Clip laydownBlink = new Clip("laydown_blink", 8, 30, LieHeight, 0, LieShiftY);
        private const int LieBlinkFrom = 28;
        private bool lying;
        private double satAt = -1;
        internal bool Lying { get { return lying; } }
        internal void LieDown() { if (seat != null && !pressed && !flying) { lying = true; Play(laydown); } }
        // Off for now (Kaelen's call): she does not walk along the floor on her own.
        internal bool Walks = false;
        internal void SitUp() { if (lying) { lying = false; Play(seat != null ? perch : idle); } }
        private readonly Clip walk = new Clip("walk", 12, 12, 1.11);
        private readonly Clip dance = new Clip("dance", 16, 16, 1.11);
        private readonly Clip grin = new Clip("grin", 16, 8, 1.11);
        // A glitch tic now and then while she is quiet, like the base; then wandering, dancing and grinning on request.
        private double nextTic = 40;
        private bool walking;
        private int walkTo;
        private double danceUntil = -1;
        // Wander: how often she sets off on her own, 0 to 1; 0 never. Solin sets it.
        internal double Wanderlust = 0.35;
        private double nextWander = 20;
        // True while something is being said, so tics and wandering wait. Solin sets it.
        internal bool Busy;
        // True while music plays: she sways and bobs to it. Solin's addition.
        internal bool Grooving;

        internal void Dance(double seconds) { if (!Asleep && !flying && !pressed) { danceUntil = Now + seconds; Play(dance); } }

        // Little flourishes when she is quiet: a hop, a spin, looking left and right, a cocked head. Solin's additions over the
        // base's idle. (Ideas described from mochiiwuff/capri-desktop; no code taken.)
        private string flourish;
        private double flourishAt = -10, nextFlourish = 25;
        private const double FlourishLife = 1.1;
        internal void Flourish(string kind) { flourish = kind; flourishAt = Now; }
        // A landing or a hit: squash for a moment, then back. Solin's addition.
        private double squashAt = -10, squashHow;
        internal void Squash(double how = 1) { squashAt = Now; squashHow = how; }
        private double shoveAt = -10, shoveDir;
        internal void KnockBack(bool toLeft) { shoveAt = Now; shoveDir = toLeft ? -1 : 1; Squash(0.6); }

        private void ApplyFlourish(double now)
        {
            body.ScaleX = body.ScaleY = 1;
            body.Shove = 0;
            double sq = now - squashAt;
            if (sq >= 0 && sq < 0.32)
            {
                // Flat and wide on impact, a little tall on the rebound, then true.
                double k = sq / 0.32, wave = Math.Sin(k * Math.PI) * (k < 0.5 ? 1 : -0.45);
                body.ScaleY = 1 - 0.22 * squashHow * wave;
                body.ScaleX = 1 + 0.16 * squashHow * wave;
            }
            double sh = now - shoveAt;
            if (sh >= 0 && sh < 0.5) body.Shove = shoveDir * body.Size * 0.12 * Math.Sin(Math.Min(1, sh / 0.5) * Math.PI);
            double f = now - flourishAt;
            if (flourish == null || f < 0 || f > FlourishLife) { flourish = null; return; }
            double t = f / FlourishLife;
            switch (flourish)
            {
                case "hop":
                    // Up and down once, stretching on the way up, squashing on landing.
                    { double h = Math.Sin(t * Math.PI); body.Breathe -= h * body.Size * 0.14; body.ScaleY *= 1 + 0.08 * Math.Sin(t * Math.PI * 2); body.ScaleX *= 1 - 0.05 * Math.Sin(t * Math.PI * 2); if (t > 0.96 && squashAt < flourishAt) Squash(0.7); break; }
                case "spin":
                    // Facing flips four times, squashing to a sliver between faces, like a flat cut-out turning.
                    { double phase = t * 4; body.FacingLeft = ((int)phase % 2) == 1; body.ScaleX *= Math.Max(0.08, Math.Abs(Math.Cos(phase * Math.PI))); break; }
                case "look":
                    // Left, then right, then back to centre.
                    { body.FacingLeft = t < 0.4; if (t > 0.8) body.FacingLeft = false; body.Tilt += Math.Sin(t * Math.PI * 2) * 2; break; }
                case "cock":
                    // Head cocked to one side and held.
                    { body.Tilt += Math.Sin(Math.Min(1, t * 3) * Math.PI / 2) * (t < 0.85 ? 9 : 9 * (1 - (t - 0.85) / 0.15)); break; }
            }
        }
        internal void Grin() { if (!Asleep && !flying && !pressed && seat == null && clip == idle) Play(grin); }

        // Sets off along the floor to an x within her screen's work area.
        private void Wander(double now)
        {
            Screen screen = Screens.ScreenFromPoint(new PixelPoint(Position.X + Px(Width / 2), Position.Y + Px(Height))) ?? Screens.Primary;
            if (screen == null) return;
            PixelRect area = screen.WorkingArea;
            int wide = Px(Width);
            walkTo = area.X + random.Next(0, Math.Max(1, area.Width - wide));
            if (Math.Abs(walkTo - Position.X) < Px(body.Size * 0.5)) return;
            walking = true;
            body.FacingLeft = walkTo < Position.X;
            Play(walk);
        }

        private void Walk(double dt)
        {
            double speed = Px(body.Size * 0.55);
            int step = (int)Math.Max(1, speed * dt);
            if (Math.Abs(walkTo - Position.X) <= step) { Position = new PixelPoint(walkTo, Position.Y); walking = false; Play(idle); return; }
            Position = new PixelPoint(Position.X + (walkTo > Position.X ? step : -step), Position.Y);
        }
        // Where her hip sits in her square, as a share of its size, for resting on a ledge.
        private const double SitX = 0.30, SitY = 0.79;
        // The top of her head in the perch art, as a share of her square; the blank above it is slack her window can lend.
        private const double PerchTop = 0.17;
        private int Slack { get { return Math.Max(0, Px(body.Centre.Y - body.Size / 2 + PerchTop * body.Size) - 2); } }
        private readonly MacDesk desk = OperatingSystem.IsMacOS() ? new MacDesk() : null;
        private Spot seat;
        // Her hip's offset from the window's left edge while seated, so she rides it where she landed.
        private int seatOffset;
        private double seatChecked;
        internal bool Seated { get { return seat != null; } }
        // The window she is sitting on, where it is now, or null.
        internal PixelRect? SeatBounds { get { Spot s = seat; return s == null ? (PixelRect?)null : s.Bounds; } }
        internal static readonly bool DebugPerch = Environment.GetEnvironmentVariable("SOLIN_DEBUG_PERCH") == "1";

        // Sits her on the first window she can see, for checking the seat without a drag. Returns what happened.
        internal string DebugSit()
        {
            if (desk == null) return "no desk";
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            foreach (Spot spot in desk.Windows())
                foreach (double share in new[] { 0.5, 0.25, 0.75 })
                {
                    PixelPoint hip = new PixelPoint(spot.Bounds.X + (int)(spot.Bounds.Width * share), spot.Bounds.Y);
                    if (LedgeUnder(hip, 2)?.Window != spot.Window) continue;
                    // From sixty points above the edge, so the fall and the catch are what gets tested.
                    Position = new PixelPoint(hip.X - Hip.X, hip.Y - Hip.Y - 60);
                    Drop();
                    return "dropped toward #" + spot.Window + " " + spot.Owner + " " + spot.Bounds + " from " + Position;
                }
            System.Text.StringBuilder why = new System.Text.StringBuilder("nothing to sit on (slack " + Slack + ", hip " + Hip + ")");
            foreach (Screen sc in Screens.All) why.Append("\n  screen " + sc.Bounds + " work " + sc.WorkingArea + (sc.IsPrimary ? " primary" : ""));
            foreach (Spot spot in desk.Windows())
            {
                PixelPoint hip = new PixelPoint(spot.Bounds.X + (int)(spot.Bounds.Width * 0.75), spot.Bounds.Y);
                Screen sc = Screens.ScreenFromPoint(hip);
                why.Append("\n  " + spot.Owner + " " + spot.Bounds + " hip " + hip + " screen " + (sc == null ? "none" : sc.WorkingArea.ToString()) + " need " + (spot.Bounds.Y - Hip.Y + Slack) + " verdict " + (LedgeUnder(hip, 2)?.Window == spot.Window ? "seat" : "no"));
            }
            return why.ToString();
        }

        internal string DebugSeat() { return seat == null ? "unseated, clip " + clip.Title + " flying " + flying : "seated on #" + seat.Window + " at " + Position + " clip " + clip.Title; }

        // What she can see to sit on right now, for checking the listing works.
        internal string DescribeLedges()
        {
            if (desk == null) return "no desk";
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            System.Collections.Generic.List<Spot> all = desk.Windows();
            Screen primary = Screens.Primary;
            System.Text.StringBuilder sb = new System.Text.StringBuilder("screen " + (primary == null ? "?" : primary.Bounds + " scaling " + primary.Scaling + " work " + primary.WorkingArea) + "; me at " + Position + " size " + Width + "x" + Height + " hip " + Hip + "; " + all.Count + " windows");
            foreach (Spot spot in all.GetRange(0, Math.Min(5, all.Count))) sb.Append("; #" + spot.Window + " " + spot.Owner + " " + spot.Bounds);
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_WINDOWS") == "2") sb.Append(desk.Dump());
            return sb.ToString();
        }
        internal Action Perched;
        internal Action Fell;
        // Pocket size: she watches the cursor with her whole body and takes less to annoy. Solin's addition.
        internal bool Mini { get; private set; }
        internal const double FullSize = 240, MiniSize = 110;
        // Where the cursor is on screen, supplied by Solin's Mouse so this file stays platform-plain.
        internal Func<PixelPoint?> CursorAt;
        private double facedAt;

        // Changes her size in place, keeping her feet where they are; a seat is kept too.
        internal void Resize(double size)
        {
            if (Math.Abs(size - body.Size) < 0.5) return;
            PixelPoint feet = new PixelPoint(Position.X + Px(Width / 2), Position.Y + Px(Height));
            int wasHipX = Hip.X;
            body.Size = size;
            Width = Height = size * Room;
            body.Centre = new Point(Width / 2, Height / 2);
            Mini = size <= MiniSize + 0.5;
            if (seat != null) { seatOffset += Hip.X - wasHipX; Settle(seat); }
            else Position = new PixelPoint(feet.X - Px(Width / 2), feet.Y - Px(Height));
            frames.Start();
        }
        private readonly Clip wake = new Clip("wake", 13, 15, 1.11);
        internal bool Asleep { get { return clip == sleep; } }
        internal void Sleep() { if (!flying && !pressed && clip != glitch) Play(sleep); }
        internal void Wake() { if (clip == sleep) Play(wake); }
        // Flight after a throw, in screen pixels per second; Solin's addition over the base's drag.
        private bool flying;
        private double flyX, flyY, flyVx, flyVy;
        private readonly System.Collections.Generic.List<(double at, PixelPoint where)> dragTrail = new System.Collections.Generic.List<(double, PixelPoint)>();
        private const double Gravity = 2600, Bounce = 0.45, ThrowSpeed = 900;
        internal Action Thrown;
        internal Action FlightLanded;
        private PixelPoint? teleportTo;
        private bool teleportLanding;
        // Fired once she has landed after a teleport, and when one begins.
        internal Action Landed;
        internal Action TeleportStarted;
        // Roaming: now and then, left alone, she teleports onto some window's top edge and sits, sometimes lying straight down.
        // Solin's addition over the base, in the manner of the Mochii build's idle jumps.
        private Spot pendingSeat;
        private bool lieOnArrival;
        private double nextRoam = 150;

        // app: only that app's windows (its name contains this, any case); Solin's addition.
        internal bool TeleportToLedge(bool lie, string app = null)
        {
            if (desk == null) return false;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            System.Collections.Generic.List<(Spot spot, int hipX)> choices = new System.Collections.Generic.List<(Spot, int)>();
            foreach (Spot spot in desk.Windows())
            {
                if (!string.IsNullOrWhiteSpace(app) && !OwnedBy(spot, app)) continue;
                if (seat != null && spot.Window == seat.Window) continue;
                if (spot.Bounds.Width < Px(body.Size * 0.9)) continue;
                int hipX = spot.Bounds.X + (int)(spot.Bounds.Width * (0.2 + random.NextDouble() * 0.6));
                // Reachable and not hidden behind another window at that spot.
                if (LedgeUnder(new PixelPoint(hipX, spot.Bounds.Y), 2)?.Window == spot.Window) choices.Add((spot, hipX));
            }
            if (choices.Count == 0) return false;
            (Spot chosen, int x) = choices[random.Next(choices.Count)];
            pendingSeat = chosen;
            lieOnArrival = lie;
            Teleport(new PixelPoint(x - Hip.X, chosen.Bounds.Y - Hip.Y));
            pendingSeat = chosen;
            return true;
        }

        // Her body's centre in screen pixels, for effects drawn in other windows.
        internal PixelPoint ScreenCentre { get { return new PixelPoint(Position.X + Px(body.Centre.X), Position.Y + Px(body.Centre.Y - body.Lift)); } }
        // Body plus anything Solin draws over her, like the entrance.
        internal readonly Panel Layers = new Panel();
        internal Point BodyCentre { get { return body.Centre; } }
        internal double BodySize { get { return body.Size; } }
        // Hidden until Arrive: the entrance draws first, then she reassembles over this long.
        private double arriveAt = -1, arriveFor;
        internal void HideForEntrance() { arriveAt = double.MaxValue; }
        internal void Arrive(double seconds) { arriveAt = Now; arriveFor = Math.Max(0.1, seconds); }

        private readonly Body body = new Body();
        private readonly DispatcherTimer frames = new DispatcherTimer();
        private readonly Random random = new Random();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        private Clip clip;
        private double clipStart;
        private int idleCycle;
        private Clip blinkWith;

        private bool pressed, dragging;
        private PixelPoint downAt, grab;

        // How her idle moves right now. Solin's MoodState sets it from the backend's mood; the defaults are the base's numbers.
        internal double BreathRate = 1.75, BreathDepth = 2.2, TiltRate = 0.95, TiltDepth = 0.65, BlinkChance = 0.55;
        // Fired for a plain click on her art and when a drag ends, for Solin's own reactions.
        internal Action Poked;
        internal Action Dragged;
        internal Action TalkAsked;
        // Two plain clicks within this long of each other count as a double click. Timed here because the platform's own
        // click count resets when the pointer is captured and released between clicks.
        private const double DoubleClickWithin = 0.45;
        private double lastClickAt = -10;
        private PixelPoint lastClickWhere;
        // While true the mouth cycles; Solin sets it for as long as a reply is being shown or spoken.
        internal bool Talking;
        // The frame on screen right now, for a live portrait elsewhere.
        internal Clip ShownClip { get { return body.Shown; } }
        internal bool FacingLeft { get { return body.FacingLeft; } }
        internal int ShownCell { get { return body.Cell; } }
        internal int ShownMouth { get { return body.Talking; } }
        internal Clip ShownMouthClip { get { return body.Mouth; } }
        // The frame as drawn: clip, cell, source rect, destination rect and the pose, for effects that echo her.
        internal (Clip clip, int cell, Rect place, Matrix pose, bool left) DrawnFrame()
        {
            Clip shown = body.Shown;
            return shown == null ? (null, 0, default, Matrix.Identity, false) : (shown, body.Cell, body.Place(shown), body.Pose, body.FacingLeft);
        }
        // A short burst of slice glitching, like the base's tic but without the strip; decays on its own.
        private double glitchUntil = -1;
        internal void Jitter(double seconds) { glitchUntil = Now + seconds; }
        // The numbers of every other window on screen right now; a wholesale change means the Space switched under her.
        // Other apps' windows on screen, front to back, in screen pixels. For her mischief.
        internal System.Collections.Generic.List<PixelRect> OtherWindows()
        {
            System.Collections.Generic.List<PixelRect> list = new System.Collections.Generic.List<PixelRect>();
            if (desk == null) return list;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            foreach (Spot spot in desk.Windows()) list.Add(spot.Bounds);
            return list;
        }

        private static bool OwnedBy(Spot spot, string app)
        {
            return spot.Owner != null && spot.Owner.IndexOf(app.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // The windows on screen of an app whose name contains this, front to back. Solin's addition.
        internal System.Collections.Generic.List<PixelRect> WindowsOf(string app)
        {
            System.Collections.Generic.List<PixelRect> list = new System.Collections.Generic.List<PixelRect>();
            if (desk == null || string.IsNullOrWhiteSpace(app)) return list;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            foreach (Spot spot in desk.Windows()) if (OwnedBy(spot, app) && spot.Bounds.Width > 60 && spot.Bounds.Height > 40) list.Add(spot.Bounds);
            return list;
        }

        internal System.Collections.Generic.HashSet<long> WindowIds()
        {
            System.Collections.Generic.HashSet<long> ids = new System.Collections.Generic.HashSet<long>();
            if (desk == null) return ids;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            foreach (Spot spot in desk.Windows()) ids.Add(spot.Window);
            return ids;
        }

        // Whether her window is drawn on a screen now (false once a swipe has left it on a desktop out of view). Solin's addition.
        internal bool Showing()
        {
            long number = Spaces.Number(this);
            return desk == null || number == 0 || desk.Showing(number);
        }

        internal PixelRect? MenuBarItem() { if (desk == null) return null; desk.Scale = Screens.Primary?.Scaling ?? 1; return desk.OwnMenuBarItem(); }
        internal string DumpAll() { return desk == null ? "" : desk.Dump(); }

        // The frontmost window of any of these apps, in screen pixels, or null. For pointing at where Claude is.
        internal PixelRect? WindowOf(params string[] owners)
        {
            if (desk == null) return null;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            foreach (Spot spot in desk.Windows())
                foreach (string owner in owners)
                    if (string.Equals(spot.Owner, owner, StringComparison.OrdinalIgnoreCase)) return spot.Bounds;
            return null;
        }
        // Her hand in screen pixels.
        internal PixelPoint HandScreen { get { Point h = HandPoint; return new PixelPoint(Position.X + Px(h.X), Position.Y + Px(h.Y)); } }

        // Her hand, roughly, in her window's points: where sparks come from.
        internal Point HandPoint
        {
            get
            {
                // Arm out: the outstretched hand, on the left in the art and on the right mirrored.
                if (body.Shown == legacy || body.Shown == legacyBlink)
                    return new Point(body.Centre.X + (body.FacingLeft ? 1 : -1) * body.Size * 0.3093, body.Centre.Y + body.Size * 0.020 - body.Lift);
                if (body.Shown == dangleStand || body.Shown == dangle)
                    return new Point(body.Centre.X + (body.FacingLeft ? 1 : -1) * body.Size * 0.2775, body.Centre.Y - body.Size * 0.032 - body.Lift);
                return new Point(body.Centre.X + (body.FacingLeft ? -1 : 1) * body.Size * 0.22, body.Centre.Y + body.Size * 0.05 - body.Lift);
            }
        }

        // A short, visible reaction laid over the idle: a dip of acknowledgement, a jolt of annoyance, or a shake of refusal.
        private string reaction;
        private double reactionAt = -10;
        private const double ReactionLife = 1.2;
        internal void React(string kind) { reaction = kind; reactionAt = Now; }

        internal SpriteWindow(double size = 240)
        {
            Title = "Solin";
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            body.Size = size;
            Width = Height = size * Room;
            body.Centre = new Point(Width / 2, Height / 2);
            RenderOptions.SetBitmapInterpolationMode(body, BitmapInterpolationMode.HighQuality);
            Layers.Children.Add(body);
            Content = Layers;

            Play(idle);
            frames.Interval = TimeSpan.FromMilliseconds(33);
            frames.Tick += delegate { Advance(Now); };
            Opened += delegate { PlaceOnPrimary(); frames.Start(); };
            Closed += delegate { frames.Stop(); pressed = dragging = false; };
        }

        private double Now { get { return clock.Elapsed.TotalSeconds; } }

        private void PlaceOnPrimary()
        {
            Screen screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
            if (screen == null) return;
            PixelRect area = screen.WorkingArea;
            int wide = Px(Width), tall = Px(Height);
            // Bottom centre of the work area, toes at the bottom edge.
            Position = new PixelPoint(area.X + (area.Width - wide) / 2, area.Bottom - tall);
        }

        private int Px(double points)
        {
            return (int)Math.Round(points * (Screens.Primary?.Scaling ?? 1));
        }

        // Glitches out, moves to target (her window's top-left, in screen pixels) and glitches back in.
        internal void Teleport(PixelPoint target)
        {
            // Any teleport cancels a seat chosen by an earlier one; TeleportToLedge sets its own again right after.
            pendingSeat = null;
            seat = null;
            floating = false;
            body.Lift = 0;
            QuietLanding = false;
            teleportTo = target;
            teleportLanding = false;
            Play(glitch);
            TeleportStarted?.Invoke();
        }

        // Straight to a spot with no effect of her own (Solin draws one): off any seat, standing. Solin's addition.
        internal void PutAt(PixelPoint target)
        {
            pendingSeat = null; seat = null; lying = false; floating = false; flying = false; walking = false; body.Lift = 0;
            teleportTo = null;
            Position = target;
            if (clip != sleep) Play(idle);
        }

        // Arriving from nowhere (a swipe to another desktop): she is put at target already gone, then reassembles over the
        // glitch the way a teleport lands. quiet: no beam on landing (Solin skips it), just the glitch. Solin's addition.
        internal bool QuietLanding;
        // How fast a teleport's glitch plays: above 1 for quick hops in a chain. Solin sets it.
        internal double TeleportSpeed = 1;
        internal void TeleportIn(PixelPoint target, bool quiet)
        {
            pendingSeat = null;
            seat = null;
            lying = false;
            floating = false;
            flying = false;
            walking = false;
            body.Lift = 0;
            Position = target;
            teleportTo = target;
            teleportLanding = true;
            arriveAt = -1;
            QuietLanding = quiet;
            Play(glitch);
        }

        // Her hip, in pixels from her window's top-left.
        private PixelPoint Hip { get { return new PixelPoint(Px(body.Centre.X - body.Size / 2 + SitX * body.Size), Px(body.Centre.Y - body.Size / 2 + SitY * body.Size)); } }

        // A window whose top edge is under her hip, within reach, front to back; null when there is none.
        private Spot LedgeUnder(PixelPoint hip, int reach)
        {
            if (desk == null) return null;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            foreach (Spot spot in desk.Windows())
            {
                PixelRect b = spot.Bounds;
                if (hip.X < b.X + 8 || hip.X > b.Right - 8) continue;
                // Too close to the top of its screen for her window to reach, even with her lifted inside it: no seat.
                Screen screen = Screens.ScreenFromPoint(new PixelPoint(hip.X, b.Y)) ?? Screens.Primary;
                int top = screen == null ? int.MinValue : screen.WorkingArea.Y;
                if (b.Y - Hip.Y + Slack < top) { if (b.Contains(hip)) return null; continue; }
                if (Math.Abs(hip.Y - b.Y) <= reach) return spot;
                // A window in front of this spot hides any ledge behind it.
                if (b.Contains(hip)) return null;
            }
            return null;
        }

        // Puts her hip on the seat's top edge; when that is above the screen's top, her window stops there and she is lifted inside it.
        private void Settle(Spot spot)
        {
            int wantY = spot.Bounds.Y - Hip.Y;
            Screen screen = Screens.ScreenFromPoint(new PixelPoint(spot.Bounds.X + seatOffset, spot.Bounds.Y)) ?? Screens.Primary;
            int top = screen == null ? wantY : screen.WorkingArea.Y;
            int lift = Math.Clamp(top - wantY, 0, Slack);
            PixelPoint want = new PixelPoint(spot.Bounds.X + seatOffset - Hip.X, wantY + lift);
            if (want != Position) Position = want;
            body.Lift = lift / (Screens.Primary?.Scaling ?? 1);
        }

        // Within a body's height of the bottom of her screen: close enough to just land.
        private bool NearFloor()
        {
            Screen screen = Screens.ScreenFromPoint(new PixelPoint(Position.X + Px(Width / 2), Position.Y + Px(Height) - 1)) ?? Screens.Primary;
            return screen == null || Position.Y + Px(Height) > screen.WorkingArea.Bottom - Px(body.Size);
        }

        private void Sit(Spot spot)
        {
            floating = false;
            seat = spot;
            seatOffset = Position.X + Hip.X - spot.Bounds.X;
            Settle(spot);
            Play(perch);
            Perched?.Invoke();
        }

        internal void Unseat()
        {
            if (seat == null) return;
            seat = null;
            body.Lift = 0;
            if (clip == perch) Play(idle);
        }

        // Every few frames while seated: follow the window, or drop when it has gone.
        private void HoldSeat(double now)
        {
            if (seat == null || desk == null || now - seatChecked < 0.08) return;
            seatChecked = now;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            Spot found = desk.Find(seat.Window);
            if (found == null && DebugPerch) Console.WriteLine("[perch] lost window #" + seat.Window);
            if (found == null)
            {
                seat = null;
                body.Lift = 0;
                flying = true; flyX = Position.X; flyY = Position.Y; flyVx = 0; flyVy = 0;
                Fell?.Invoke();
                return;
            }
            seat = found;
            Settle(found);
        }

        // Her window's top-left that puts her feet at a screen point, kept inside that screen's work area.
        internal PixelPoint StandingAt(PixelPoint feet)
        {
            // Feet on a screen's bottom edge stand on that screen, not on one below it that starts there. Solin's fix.
            Screen screen = Screens.ScreenFromPoint(new PixelPoint(feet.X, feet.Y - 1)) ?? Screens.ScreenFromPoint(feet) ?? Screens.Primary;
            int wide = Px(Width), tall = Px(Height);
            int x = feet.X - wide / 2, y = feet.Y - tall;
            if (screen != null)
            {
                PixelRect area = screen.WorkingArea;
                x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - wide));
                y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - tall));
            }
            return new PixelPoint(x, y);
        }

        private void Play(Clip next)
        {
            clip = next;
            clipStart = Now;
            idleCycle = 0;
            blinkWith = null;
        }

        private double lastTick;

        private void Fly(double dt)
        {
            Screen screen = Screens.ScreenFromPoint(new PixelPoint((int)flyX + Px(Width / 2), (int)flyY + Px(Height / 2))) ?? Screens.Primary;
            if (screen == null) { flying = false; return; }
            PixelRect area = screen.WorkingArea;
            int wide = Px(Width), tall = Px(Height);
            int hipWas = (int)flyY + Hip.Y;
            flyVy += Gravity * dt;
            flyX += flyVx * dt;
            flyY += flyVy * dt;
            if (flyX < area.X) { flyX = area.X; flyVx = -flyVx * Bounce; }
            if (flyX > area.Right - wide) { flyX = area.Right - wide; flyVx = -flyVx * Bounce; }
            if (flyY < area.Y) { flyY = area.Y; flyVy = -flyVy * Bounce; }
            // On the way down, the first window top edge her hip crosses this frame catches her, unless she is going too fast.
            if (flyVy > 0 && flyVy < 1800)
            {
                int hipX = (int)flyX + Hip.X, hipNow = (int)flyY + Hip.Y;
                Spot ledge = LedgeCrossed(hipX, hipWas, hipNow);
                if (ledge != null)
                {
                    flying = false;
                    flyY = ledge.Bounds.Y - Hip.Y;
                    Position = new PixelPoint((int)flyX, (int)flyY);
                    Sit(ledge);
                    Squash(0.8);
                    FlightLanded?.Invoke();
                    return;
                }
            }
            double floor = area.Bottom - tall;
            if (flyY >= floor)
            {
                flyY = floor;
                flyVx *= 0.7;
                if (Math.Abs(flyVy) > 260) flyVy = -flyVy * Bounce;
                else
                {
                    flying = false;
                    Position = new PixelPoint((int)flyX, (int)flyY);
                    Play(idle);
                    Squash(Math.Min(1.4, 0.5 + Math.Abs(flyVy) / 900));
                    FlightLanded?.Invoke();
                    return;
                }
            }
            Position = new PixelPoint((int)flyX, (int)flyY);
        }

        // The frontmost window whose top edge lies between two hip heights at this x, honouring headroom and what is in front.
        private Spot LedgeCrossed(int hipX, int fromY, int toY)
        {
            if (desk == null || toY < fromY) return null;
            desk.Scale = Screens.Primary?.Scaling ?? 1;
            foreach (Spot spot in desk.Windows())
            {
                PixelRect b = spot.Bounds;
                if (hipX < b.X + 8 || hipX > b.Right - 8) continue;
                if (b.Y > fromY && b.Y <= toY)
                {
                    Screen screen = Screens.ScreenFromPoint(new PixelPoint(hipX, b.Y)) ?? Screens.Primary;
                    int top = screen == null ? int.MinValue : screen.WorkingArea.Y;
                    if (b.Y - Hip.Y + Slack >= top) return spot;
                }
                // A window in front that already holds this height hides anything behind it.
                if (b.Y <= fromY && b.Bottom >= toY) return null;
            }
            return null;
        }

        // Let go in mid-air with nothing under her, she hovers where she is instead of dropping. Solin's choice over the base.
        private bool floating;
        internal bool Floating { get { return floating; } }

        // Whether a window top edge lies somewhere below her hip on this screen.
        private bool LedgeBelow()
        {
            int hipX = Position.X + Hip.X, hipY = Position.Y + Hip.Y;
            Screen screen = Screens.ScreenFromPoint(new PixelPoint(hipX, hipY)) ?? Screens.Primary;
            int floor = screen == null ? hipY + 4000 : screen.WorkingArea.Bottom;
            return LedgeCrossed(hipX, hipY, floor) != null;
        }

        // Lets her go from wherever she is: she falls, catching the first ledge below or landing on the floor.
        private void Drop()
        {
            floating = false;
            seat = null;
            body.Lift = 0;
            flying = true;
            flyX = Position.X; flyY = Position.Y; flyVx = 0; flyVy = 0;
        }

        // Pointing: while set, she turns toward this screen point. Standing, if it is to the side or below her, she holds her
        // arm out at it (the arm-out art, mirrored to point right); if it is above her she keeps her usual pose and turns. ArmAlways: arm out whatever the angle (stargazing). Solin's addition.
        internal PixelPoint? Aim;
        internal bool ArmAlways;
        // Now and then, standing idle, she holds the arm-out pose for a while instead of the usual one.
        private double armIdleUntil = -1, nextArmIdle = 90;

        // Whether, standing, she should be in the arm-out pose now; and which way it faces.
        private bool WantArm(double now, out bool facingLeft)
        {
            facingLeft = false;
            bool standing = seat == null && !lying && !Mini && !walking && !pressed && !flying && !dragging && clip != sleep && clip != wake && clip != glitch && danceUntil < 0;
            if (!standing) return false;
            if (Aim.HasValue)
            {
                int cx = Position.X + Px(body.Centre.X), cy = Position.Y + Px(body.Centre.Y);
                double dx = Aim.Value.X - cx, dy = Aim.Value.Y - cy;
                // The art's arm points to the left; mirrored, to the right.
                facingLeft = dx > 0;
                // Below her the arm-out pose fits too; only straight above does she keep her usual pose.
                return ArmAlways || dy > 0 || Math.Abs(dx) >= Math.Abs(dy) * 0.7;
            }
            if ((Talking || Busy) && stand != legacy) return false;
            if (armIdleUntil > now) { facingLeft = armIdleLeft; return true; }
            return false;
        }
        private bool armIdleLeft;

        // Small, with a face plate to show, she is the plate: blinking, looking, talking, scowling. Solin's addition.
        internal Func<string, Avalonia.Media.Imaging.Bitmap> MiniArt;
        private double nextPlateBlink = 3, plateBlinkAt = -10;

        private string PlateState(double now)
        {
            bool annoyed = reaction == "jolt" && now - reactionAt < 2.5;
            if (body.Glitch > 0.2 || clip == glitch) return "glitch";
            if (Talking) return (annoyed ? "annoyed_talk" : ((int)(now * 6) % 2 == 0 ? "talk" : "talk2"));
            if (annoyed) return "annoyed";
            if (now > nextPlateBlink) { plateBlinkAt = now; nextPlateBlink = now + 2.5 + random.NextDouble() * 3.5; }
            if (now - plateBlinkAt < 0.14) return "blink";
            PixelPoint? at = CursorAt?.Invoke();
            if (at.HasValue)
            {
                int middle = Position.X + Px(body.Centre.X);
                if (at.Value.X < middle - Px(body.Size * 0.6)) return "look_left";
                if (at.Value.X > middle + Px(body.Size * 0.6)) return "look_right";
            }
            return "idle";
        }

        private void Advance(double now)
        {
            double dt = lastTick > 0 ? Math.Min(now - lastTick, 0.1) : 0.033;
            lastTick = now;
            if (flying) Fly(dt);
            if (seat != null && !pressed && !flying) HoldSeat(now);
            bool onFloor = seat == null && !flying && !pressed && !Asleep && !floating;
            if (walking && (pressed || flying || seat != null || Asleep)) { walking = false; }
            if (walking) Walk(dt);
            bool quiet = !Busy && !Grooving && !pressed && !flying && !dragging && !walking && danceUntil < 0 && clip != glitch && clip != perchGlitch && clip != grin && !Asleep && teleportTo == null && arriveAt < 0;
            if (quiet && !lying && now >= nextTic) { nextTic = now + 30 + random.NextDouble() * 30; Play(seat != null ? perchGlitch : glitch); }
            if (quiet && Wanderlust > 0 && now >= nextRoam && !Mini)
            {
                nextRoam = now + (180 + random.NextDouble() * 240) / Math.Max(0.1, Wanderlust);
                if (random.NextDouble() < 0.6) TeleportToLedge(lie: random.NextDouble() < 0.5);
            }
            if (quiet && !lying && flourish == null && now >= nextFlourish)
            {
                nextFlourish = now + 35 + random.NextDouble() * 55;
                string[] kinds = seat != null ? new[] { "look", "cock" } : new[] { "hop", "spin", "look", "cock" };
                Flourish(kinds[random.Next(kinds.Length)]);
            }
            if (quiet && onFloor && Walks && Wanderlust > 0 && now >= nextWander)
            {
                nextWander = now + (60 + random.NextDouble() * 120) / Math.Max(0.05, Wanderlust);
                if (random.NextDouble() < Wanderlust) Wander(now);
            }
            if (danceUntil >= 0 && (now >= danceUntil || pressed || flying)) { danceUntil = -1; if (clip == dance) Play(seat != null ? perch : idle); }
            int index = (int)((now - clipStart) * clip.Fps * (clip == glitch ? TeleportSpeed : 1));
            if (clip == perchGlitch && index >= perchGlitch.Count) { Play(perch); index = 0; }
            if (clip == grin && index >= grin.Count * 2) { Play(idle); index = 0; }
            if (clip == glitch && index >= glitch.Count && !teleportTo.HasValue) { Play(seat != null ? perch : idle); index = 0; }
            if (clip == glitch && index >= glitch.Count)
            {
                if (teleportTo.HasValue && !teleportLanding)
                {
                    Position = teleportTo.Value;
                    teleportLanding = true;
                    Play(glitch);
                }
                else
                {
                    teleportTo = null;
                    Play(idle);
                    Squash(0.6);
                    if (pendingSeat != null)
                    {
                        Spot target = desk?.Find(pendingSeat.Window);
                        pendingSeat = null;
                        if (target != null)
                        {
                            Sit(target);
                            if (lieOnArrival) LieDown();
                        }
                    }
                    Landed?.Invoke();
                }
                index = 0;
            }
            if (clip == wake && index >= wake.Count) { Play(seat != null ? perch : idle); index = 0; }
            if (seat != null && !pressed && !flying && clip == idle) { Play(perch); index = 0; }
            if (seat != null && clip == perch && Talking) { Play(perchTalk); index = 0; }
            if (clip == perchTalk && (!Talking || seat == null)) { Play(seat != null ? perch : idle); index = 0; }
            // Sat quietly for a couple of minutes, she lies down; picked up or unseated, she is up again.
            if (seat != null && !lying && clip == perch) { if (satAt < 0) satAt = now; else if (quiet && now - satAt > 120 + random.NextDouble() * 120 && random.NextDouble() < 0.02) LieDown(); }
            if (seat == null || pressed || flying) { satAt = -1; if (lying) { lying = false; if (clip == laydown || clip == laydownTalk) Play(idle); } }
            if (lying && clip == perch) { Play(laydown); index = 0; }
            if (lying && clip == laydown && Talking) { Play(laydownTalk); index = 0; }
            if (clip == laydownTalk && !Talking) { Play(lying ? laydown : perch); index = 0; }
            if (clip == idle || clip == perch || clip == laydown)
            {
                int cycle = index / clip.Count;
                if (cycle != idleCycle)
                {
                    idleCycle = cycle;
                    double roll = random.NextDouble();
                    if (clip == laydown) blinkWith = roll < BlinkChance ? laydownBlink : null;
                    else if (clip == perch) blinkWith = roll < BlinkChance ? perchBlink : null;
                    else blinkWith = roll < BlinkChance * 0.18 ? idleBlinkGlitch : roll < BlinkChance ? idleBlink : null;
                }
            }
            int cell = index % clip.Count;
            Clip shown = clip;
            int blinkFrom = clip == perch ? PerchBlinkFrom : clip == laydown ? LieBlinkFrom : IdleBlinkFrom;
            if ((clip == idle || clip == perch || clip == laydown) && blinkWith != null && cell >= blinkFrom && cell < blinkFrom + blinkWith.Count) { shown = blinkWith; cell -= blinkFrom; }
            if ((pressed || flying) && clip != dangle && clip != glitch) Play(dangle);
            if (clip == sleep && pressed) Play(wake);
            if (!pressed && !flying && clip == dangle) Play(seat != null ? perch : idle);
            // Standing: the arm-out pose while pointing or for a spell of idling, the usual one otherwise.
            if (quiet && clip == idle && !Aim.HasValue && armIdleUntil < now && now >= nextArmIdle)
            {
                nextArmIdle = now + 60 + random.NextDouble() * 120;
                if (random.NextDouble() < 0.45) { armIdleUntil = now + 15 + random.NextDouble() * 30; armIdleLeft = random.NextDouble() < 0.5; }
            }
            bool arm = WantArm(now, out bool armLeft);
            if (arm && clip == idle) { Play(stand); index = 0; cell = 0; shown = stand; }
            if (!arm && clip == stand) { Play(seat != null ? perch : idle); index = 0; cell = 0; shown = clip; }
            if (clip == stand)
            {
                cell = index % stand.Count;
                shown = stand;
                // The legacy pose blinks with its own four frames, in the middle of some loops.
                if (stand == legacy && legacyBlink.Available)
                {
                    int cycle = index / stand.Count;
                    if (cycle != legacyCycle) { legacyCycle = cycle; legacyBlinking = random.NextDouble() < BlinkChance * 0.7; }
                    if (legacyBlinking && cell >= 8 && cell < 12) { shown = legacyBlink; cell -= 8; }
                }
            }
            // Small, she turns to face the cursor, with a little hysteresis so she does not flicker when it is near.
            if (Mini && CursorAt != null && !pressed && !flying && now - facedAt > 0.25)
            {
                facedAt = now;
                PixelPoint? at = CursorAt();
                if (at.HasValue)
                {
                    int middle = Position.X + Px(body.Centre.X);
                    if (at.Value.X < middle - Px(body.Size * 0.15)) body.FacingLeft = true;
                    else if (at.Value.X > middle + Px(body.Size * 0.15)) body.FacingLeft = false;
                }
            }
            else if (!Mini && !walking && flourish == null && !Aim.HasValue && clip != stand) body.FacingLeft = false;
            if (clip == stand) body.FacingLeft = armLeft;
            else if (Aim.HasValue && !walking && !pressed && !flying) body.FacingLeft = Aim.Value.X < Position.X + Px(body.Centre.X);
            bool settled = !dragging && !flying && clip != glitch;
            // Asleep she only breathes, slow and deep.
            if (clip == sleep)
            {
                body.Breathe = Math.Sin(now * 1.1) * 3.5 * (body.Size / 240);
                body.Tilt = 0;
                body.Dissolve = 0;
                body.Shown = sleep;
                body.Cell = cell;
                body.Mouth = idleMouth;
                body.Talking = -1;
                body.InvalidateVisual();
                return;
            }
            // Out: she dissolves over the glitch. In: she reassembles over it.
            double progress = clip == glitch ? Math.Clamp((now - clipStart) * glitch.Fps * TeleportSpeed / glitch.Count, 0, 1) : 0;
            body.Dissolve = clip != glitch ? 0 : teleportLanding ? 1 - progress : progress;
            if (arriveAt == double.MaxValue) body.Dissolve = 1;
            else if (arriveAt >= 0)
            {
                double arriving = (now - arriveAt) / arriveFor;
                if (arriving >= 1) arriveAt = -1; else body.Dissolve = Math.Max(body.Dissolve, 1 - arriving);
            }
            // Breath and tilt scale with her size so they read the same at every size.
            double scale = body.Size / 240;
            body.Breathe = settled ? Math.Sin(now * BreathRate) * BreathDepth * scale : 0;
            if (Grooving && settled && clip != sleep)
            {
                // A sway on the beat-ish and a small bob on every other half-beat.
                body.Tilt += Math.Sin(now * 2.4) * 3.2;
                body.Breathe -= Math.Abs(Math.Sin(now * 4.8)) * 3 * scale;
            }
            // Hovering, she rides a slow swell on top of her breathing.
            if (floating && settled) body.Breathe += Math.Sin(now * 0.9) * 7 * scale;
            body.Tilt = walking ? Math.Sin(now * 9) * 1.2 : settled ? Math.Sin(now * TiltRate) * TiltDepth : flying ? Math.Clamp(flyVx / 60, -25, 25) : 0;
            // Leaning toward what she points at.
            if (Aim.HasValue && settled && clip != stand) body.Tilt += body.FacingLeft ? -5 : 5;
            double age = now - reactionAt;
            if (settled && age >= 0 && age < ReactionLife)
            {
                double dip = Math.Sin(age / ReactionLife * Math.PI);
                if (reaction == "dip") body.Breathe -= 9 * dip * scale;
                if (reaction == "jolt") { body.Breathe += 14 * dip * scale; body.Tilt -= 8 * dip; }
                if (reaction == "shake" && age < 0.5) body.Tilt += 8 * Math.Sin(age * 50) * (1 - age * 2);
            }
            body.Glitch = glitchUntil > now ? Math.Min(1, (glitchUntil - now) * 2) : clip == glitch || clip == perchGlitch ? 0.6 : 0;
            ApplyFlourish(now);
            body.Override = Mini && MiniArt != null ? MiniArt(PlateState(now)) : null;
            if (body.Override != null) body.FacingLeft = false;
            body.Shown = shown;
            body.Cell = cell;
            bool legacyFace = shown == legacy && legacyMouth.Available;
            body.Mouth = legacyFace ? legacyMouth : idleMouth;
            // The mouth only fits a plain face (the idle's or the legacy pose's), not a blink; a bit of jitter keeps it from
            // looking metronomic.
            bool mouthFits = shown == idle || legacyFace;
            body.Talking = Talking && mouthFits ? (int)(now * body.Mouth.Fps + Math.Sin(now * 7) * 1.5) % body.Mouth.Count : -1;
            if (body.Talking < 0 && Talking && mouthFits) body.Talking = 0;
            body.InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Point at = e.GetPosition(this);
            if (!body.Opaque(at)) return;
            e.Pointer.Capture(this);
            pressed = true;
            dragging = false;
            downAt = this.PointToScreen(at);
            grab = new PixelPoint(downAt.X - Position.X, downAt.Y - Position.Y);
            flying = false;
            floating = false;
            seat = null;
            body.Lift = 0;
            dragTrail.Clear();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!pressed) return;
            PixelPoint at = this.PointToScreen(e.GetPosition(this));
            if (!dragging && (Math.Abs(at.X - downAt.X) > 4 || Math.Abs(at.Y - downAt.Y) > 4)) dragging = true;
            if (dragging)
            {
                Position = new PixelPoint(at.X - grab.X, at.Y - grab.Y);
                dragTrail.Add((Now, Position));
                if (dragTrail.Count > 8) dragTrail.RemoveAt(0);
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!pressed || e.InitialPressMouseButton != MouseButton.Left) return;
            // Releasing the capture raises OnPointerCaptureLost at once, which would wipe the drag flag before it is read.
            bool wasDragging = dragging;
            pressed = false;
            releasing = true;
            e.Pointer.Capture(null);
            releasing = false;
            if (wasDragging)
            {
                // Speed over the last few drag samples; fast enough and she flies.
                if (dragTrail.Count >= 2)
                {
                    (double t0, PixelPoint p0) = dragTrail[0];
                    (double t1, PixelPoint p1) = dragTrail[dragTrail.Count - 1];
                    double span = Math.Max(t1 - t0, 0.016);
                    double vx = (p1.X - p0.X) / span, vy = (p1.Y - p0.Y) / span;
                    if (Math.Sqrt(vx * vx + vy * vy) > ThrowSpeed)
                    {
                        flying = true;
                        flyX = Position.X; flyY = Position.Y; flyVx = vx; flyVy = vy;
                        Thrown?.Invoke();
                        dragging = false;
                        return;
                    }
                }
                Dragged?.Invoke();
                // Right on a ledge she sits; anywhere else she drops, onto the first ledge below or the floor, like the base.
                Spot ledge = LedgeUnder(new PixelPoint(Position.X + Hip.X, Position.Y + Hip.Y), 24);
                if (ledge != null) Sit(ledge);
                else if (LedgeBelow() || NearFloor()) Drop();
                else floating = true;
            }
            else
            {
                double now = Now;
                PixelPoint where = this.PointToScreen(e.GetPosition(this));
                bool again = now - lastClickAt < DoubleClickWithin && Math.Abs(where.X - lastClickWhere.X) < 12 && Math.Abs(where.Y - lastClickWhere.Y) < 12;
                lastClickAt = again ? -10 : now;
                lastClickWhere = where;
                if (again) TalkAsked?.Invoke(); else Poked?.Invoke();
            }
            dragging = false;
        }

        private bool releasing;

        // Capture lost some other way mid-drag, like the window going away under the cursor: let her go as if released.
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            if (releasing) return;
            bool wasDragging = pressed && dragging;
            pressed = dragging = false;
            if (wasDragging) Drop();
        }
    }
}
