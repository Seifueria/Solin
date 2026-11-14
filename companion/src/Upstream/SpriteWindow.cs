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

        internal void Dance(double seconds) { if (!Asleep && !flying && !pressed) { danceUntil = Now + seconds; Play(dance); } }
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
        // Her hand, roughly, in her window's points: where sparks come from.
        internal Point HandPoint { get { return new Point(body.Centre.X + (body.FacingLeft ? -1 : 1) * body.Size * 0.22, body.Centre.Y + body.Size * 0.05 - body.Lift); } }

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
            seat = null;
            floating = false;
            body.Lift = 0;
            teleportTo = target;
            teleportLanding = false;
            Play(glitch);
            TeleportStarted?.Invoke();
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
            Screen screen = Screens.ScreenFromPoint(new PixelPoint(Position.X + Px(Width / 2), Position.Y + Px(Height))) ?? Screens.Primary;
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
            Screen screen = Screens.ScreenFromPoint(feet) ?? Screens.Primary;
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

        private void Advance(double now)
        {
            double dt = lastTick > 0 ? Math.Min(now - lastTick, 0.1) : 0.033;
            lastTick = now;
            if (flying) Fly(dt);
            if (seat != null && !pressed && !flying) HoldSeat(now);
            bool onFloor = seat == null && !flying && !pressed && !Asleep && !floating;
            if (walking && (pressed || flying || seat != null || Asleep)) { walking = false; }
            if (walking) Walk(dt);
            bool quiet = !Busy && !pressed && !flying && !dragging && !walking && danceUntil < 0 && clip != glitch && clip != perchGlitch && clip != grin && !Asleep && teleportTo == null && arriveAt < 0;
            if (quiet && now >= nextTic) { nextTic = now + 30 + random.NextDouble() * 30; Play(seat != null ? perchGlitch : glitch); }
            if (quiet && onFloor && Wanderlust > 0 && now >= nextWander)
            {
                nextWander = now + (60 + random.NextDouble() * 120) / Math.Max(0.05, Wanderlust);
                if (random.NextDouble() < Wanderlust) Wander(now);
            }
            if (danceUntil >= 0 && (now >= danceUntil || pressed || flying)) { danceUntil = -1; if (clip == dance) Play(seat != null ? perch : idle); }
            int index = (int)((now - clipStart) * clip.Fps);
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
                    Landed?.Invoke();
                }
                index = 0;
            }
            if (clip == wake && index >= wake.Count) { Play(seat != null ? perch : idle); index = 0; }
            if (seat != null && !pressed && !flying && clip == idle) { Play(perch); index = 0; }
            if (seat != null && clip == perch && Talking) { Play(perchTalk); index = 0; }
            if (clip == perchTalk && (!Talking || seat == null)) { Play(seat != null ? perch : idle); index = 0; }
            if (clip == idle || clip == perch)
            {
                int cycle = index / clip.Count;
                if (cycle != idleCycle)
                {
                    idleCycle = cycle;
                    double roll = random.NextDouble();
                    if (clip == perch) blinkWith = roll < BlinkChance ? perchBlink : null;
                    else blinkWith = roll < BlinkChance * 0.18 ? idleBlinkGlitch : roll < BlinkChance ? idleBlink : null;
                }
            }
            int cell = index % clip.Count;
            Clip shown = clip;
            int blinkFrom = clip == perch ? PerchBlinkFrom : IdleBlinkFrom;
            if ((clip == idle || clip == perch) && blinkWith != null && cell >= blinkFrom && cell < blinkFrom + blinkWith.Count) { shown = blinkWith; cell -= blinkFrom; }
            if ((pressed || flying) && clip != dangle && clip != glitch) Play(dangle);
            if (clip == sleep && pressed) Play(wake);
            if (!pressed && !flying && clip == dangle) Play(seat != null ? perch : idle);
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
            else if (!Mini && !walking) body.FacingLeft = false;
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
            double progress = clip == glitch ? Math.Clamp((now - clipStart) * glitch.Fps / glitch.Count, 0, 1) : 0;
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
            // Hovering, she rides a slow swell on top of her breathing.
            if (floating && settled) body.Breathe += Math.Sin(now * 0.9) * 7 * scale;
            body.Tilt = walking ? Math.Sin(now * 9) * 1.2 : settled ? Math.Sin(now * TiltRate) * TiltDepth : flying ? Math.Clamp(flyVx / 60, -25, 25) : 0;
            double age = now - reactionAt;
            if (settled && age >= 0 && age < ReactionLife)
            {
                double dip = Math.Sin(age / ReactionLife * Math.PI);
                if (reaction == "dip") body.Breathe -= 9 * dip * scale;
                if (reaction == "jolt") { body.Breathe += 14 * dip * scale; body.Tilt -= 8 * dip; }
                if (reaction == "shake" && age < 0.5) body.Tilt += 8 * Math.Sin(age * 50) * (1 - age * 2);
            }
            body.Glitch = glitchUntil > now ? Math.Min(1, (glitchUntil - now) * 2) : clip == glitch || clip == perchGlitch ? 0.6 : 0;
            body.Shown = shown;
            body.Cell = cell;
            body.Mouth = idleMouth;
            // The mouth only fits the plain idle face, not a blink; a bit of jitter keeps it from looking metronomic.
            body.Talking = Talking && shown == idle ? (int)(now * idleMouth.Fps + Math.Sin(now * 7) * 1.5) % idleMouth.Count : -1;
            if (body.Talking < 0 && Talking && shown == idle) body.Talking = 0;
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
