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
        // Fired once she has landed after a teleport.
        internal Action Landed;

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
            Content = body;

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
            teleportTo = target;
            teleportLanding = false;
            Play(glitch);
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
            flyVy += Gravity * dt;
            flyX += flyVx * dt;
            flyY += flyVy * dt;
            if (flyX < area.X) { flyX = area.X; flyVx = -flyVx * Bounce; }
            if (flyX > area.Right - wide) { flyX = area.Right - wide; flyVx = -flyVx * Bounce; }
            if (flyY < area.Y) { flyY = area.Y; flyVy = -flyVy * Bounce; }
            double floor = area.Bottom - tall;
            if (flyY >= floor)
            {
                flyY = floor;
                if (Math.Abs(flyVy) > 260) flyVy = -flyVy * Bounce;
                else
                {
                    flying = false;
                    Position = new PixelPoint((int)flyX, (int)flyY);
                    Play(idle);
                    FlightLanded?.Invoke();
                    return;
                }
                flyVx *= 0.7;
            }
            Position = new PixelPoint((int)flyX, (int)flyY);
        }

        private void Advance(double now)
        {
            double dt = lastTick > 0 ? Math.Min(now - lastTick, 0.1) : 0.033;
            lastTick = now;
            if (flying) Fly(dt);
            int index = (int)((now - clipStart) * clip.Fps);
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
            if (clip == wake && index >= wake.Count) { Play(idle); index = 0; }
            if (clip == idle)
            {
                int cycle = index / clip.Count;
                if (cycle != idleCycle)
                {
                    idleCycle = cycle;
                    double roll = random.NextDouble();
                    blinkWith = roll < BlinkChance * 0.18 ? idleBlinkGlitch : roll < BlinkChance ? idleBlink : null;
                }
            }
            int cell = index % clip.Count;
            Clip shown = clip;
            if (clip == idle && blinkWith != null && cell >= IdleBlinkFrom && cell < IdleBlinkFrom + blinkWith.Count) { shown = blinkWith; cell -= IdleBlinkFrom; }
            if ((pressed || flying) && clip != dangle && clip != glitch) Play(dangle);
            if (clip == sleep && pressed) Play(wake);
            if (!pressed && !flying && clip == dangle) Play(idle);
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
            // Breath and tilt scale with her size so they read the same at every size.
            double scale = body.Size / 240;
            body.Breathe = settled ? Math.Sin(now * BreathRate) * BreathDepth * scale : 0;
            body.Tilt = settled ? Math.Sin(now * TiltRate) * TiltDepth : flying ? Math.Clamp(flyVx / 60, -25, 25) : 0;
            double age = now - reactionAt;
            if (settled && age >= 0 && age < ReactionLife)
            {
                double dip = Math.Sin(age / ReactionLife * Math.PI);
                if (reaction == "dip") body.Breathe -= 9 * dip * scale;
                if (reaction == "jolt") { body.Breathe += 14 * dip * scale; body.Tilt -= 8 * dip; }
                if (reaction == "shake" && age < 0.5) body.Tilt += 8 * Math.Sin(age * 50) * (1 - age * 2);
            }
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
            pressed = false;
            e.Pointer.Capture(null);
            if (dragging)
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

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            pressed = dragging = false;
        }
    }
}
