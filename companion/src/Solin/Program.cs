// Solin companion entry point. Solin-owned; not derived from upstream.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Platform;
using Avalonia.Threading;
using Solin.Companion.Upstream;

namespace Solin.Companion
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            return AppBuilder.Configure<SolinApp>()
                .UsePlatformDetect()
                .With(new MacOSPlatformOptions { ShowInDock = false })
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
        }
    }

    internal sealed class SolinApp : Application
    {
        private readonly MoodState mood = new MoodState();
        private readonly BackendClient backend = new BackendClient();
        private readonly BackendLauncher launcher = new BackendLauncher();
        private SpriteWindow sprite;
        private Typer typer;
        private Bubble bubble;
        private Pill pill;
        private readonly Voice voice = new Voice();
        private readonly Sfx sfx = new Sfx();
        // The bubble typing and her voice playing each move her mouth; it stops when neither is on.
        private int mouthHolds;
        private bool thinking;
        // Throws in a row; the third one she takes personally.
        private int throws;
        // Presence: how long you have been away, how long you have worked without a real pause.
        private readonly DispatcherTimer presence = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private double activeSeconds;
        private DateTime? sleptAt;
        private const double SleepAfter = 8 * 60, PauseIs = 5 * 60, BreakAfter = 2 * 3600, LongAway = 20 * 60;
        private static readonly string[] WakeLines =
        {
            "[soft yawn] oh. you're back.", "[blink] mm. was i out long?", "[stretch] hey. missed you a bit. don't make it weird.",
        };
        private static readonly string[] BreakLines =
        {
            "[calm] you've been at it two hours straight. stretch, drink some water. i'll be here.",
            "[soft] two hours without a break. go stand up for a minute, yeah?",
        };
        private readonly Random pick = new Random();
        private string agentState = "idle";
        // While Claude is waiting on you she keeps bristling until the state moves on.
        private readonly DispatcherTimer bristle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                sprite = new SpriteWindow();
                sprite.HideForEntrance();
                typer = new Typer();
                bubble = new Bubble();
                pill = new Pill();
                desktop.MainWindow = sprite;
                Wire();
                desktop.Exit += delegate { voice.Stop(); backend.Dispose(); launcher.Dispose(); };
            }
            base.OnFrameworkInitializationCompleted();
        }

        private int Px(double points) { return (int)Math.Round(points * (sprite.Screens.Primary?.Scaling ?? 1)); }

        // Mood from the backend shapes her idle; her clicks and drags go back as events; a double-click opens the typing box.
        private void Wire()
        {
            mood.Changed += delegate(MoodState state)
            {
                MoodLook look = state.Look;
                sprite.BreathRate = look.BreathRate;
                sprite.BreathDepth = look.BreathDepth;
                sprite.TiltRate = look.TiltRate;
                sprite.TiltDepth = look.TiltDepth;
                sprite.BlinkChance = look.BlinkChance;
            };
            mood.MoodChanged += delegate(string name) { sprite.React(MoodState.ReactionFor(name)); };
            backend.MoodChanged += delegate(string name, double trust) { Dispatcher.UIThread.Post(() => mood.Set(name, trust)); };
            // Replies from anywhere (the typing box, curl, another client) show in her bubble.
            backend.Replied += delegate(string visible, string replyMood, string audio)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (thinking) return;
                    bubble.Say(visible, replyMood ?? mood.Mood);
                    if (audio != null) _ = voice.Play(audio);
                });
            };
            sprite.Poked += delegate { sprite.React("dip"); sfx.Play("blip"); _ = backend.Event("poked"); };
            sprite.Dragged += delegate { _ = backend.Event("dragged"); };
            sprite.Thrown += delegate { throws++; _ = backend.Event(throws >= 3 ? "thrown" : "dragged"); };
            sprite.Perched += delegate { sfx.Play("tick"); sprite.React("dip"); };
            sprite.Fell += delegate { sfx.Play("blip"); };
            sprite.FlightLanded += delegate { sprite.React(throws >= 3 ? "jolt" : "dip"); if (throws >= 3) throws = 0; };
            sprite.TalkAsked += delegate
            {
                // Under her feet, centred.
                typer.Open(new PixelPoint(sprite.Position.X + Px(sprite.Width / 2) - Px(160), sprite.Position.Y + Px(sprite.Height) - Px(30)));
            };
            bubble.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.12)));
            bubble.Speaking += delegate(bool on) { Mouth(on); if (on) sfx.Play("tick"); };
            voice.Speaking += delegate(bool on) { Dispatcher.UIThread.Post(() => Mouth(on)); };
            bubble.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            pill.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.04)));
            sprite.PositionChanged += delegate { bubble.Reposition(); pill.Reposition(); };
            bristle.Tick += delegate { if (agentState == "needs_you") sprite.React("jolt"); else bristle.Stop(); };
            backend.AgentChanged += delegate(string state, string detail) { Dispatcher.UIThread.Post(() => Agent(state, detail)); };
            typer.Sent += delegate(string text) { _ = Ask(text); };
            presence.Tick += delegate { Presence(); };
            sprite.Opened += async delegate
            {
                presence.Start();
                Entrance();
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_WINDOWS") == "1") Console.WriteLine("[windows] " + sprite.DescribeLedges());
                if (await launcher.EnsureRunning(backend)) backend.Listen();
            };
        }

        // The drawn entrance, then she reassembles out of the dot.
        private void Entrance()
        {
            IntroFx intro = new IntroFx(sprite.BodyCentre, sprite.BodySize);
            intro.ScreenLit += delegate { sfx.Play("zap"); };
            intro.Finished += delegate { sprite.Layers.Children.Remove(intro); sprite.Arrive(0.8); sfx.Play("soft"); };
            sprite.Layers.Children.Add(intro);
            intro.Start();
        }

        // Once a second: sleep when you have been away a while, wake when you return, nudge a break after two hours of no pauses.
        private void Presence()
        {
            double? idle = Mouse.IdleSeconds();
            if (!idle.HasValue) return;
            if (idle.Value >= PauseIs) activeSeconds = 0; else activeSeconds += 1;
            if (!sprite.Asleep && idle.Value >= SleepAfter && agentState != "working" && agentState != "needs_you")
            {
                sprite.Sleep();
                sleptAt = DateTime.Now;
                _ = backend.Event("ignored");
            }
            else if (sprite.Asleep && idle.Value < 2)
            {
                sprite.Wake();
                sfx.Play("soft");
                bool longAway = sleptAt.HasValue && (DateTime.Now - sleptAt.Value).TotalSeconds > LongAway;
                sleptAt = null;
                _ = backend.Event("greeted");
                if (longAway) bubble.Say(WakeLines[pick.Next(WakeLines.Length)], "warm");
            }
            if (activeSeconds >= BreakAfter && !bubble.IsVisible && agentState != "working" && !sprite.Asleep)
            {
                activeSeconds = 0;
                bubble.Say(BreakLines[pick.Next(BreakLines.Length)], "calm");
            }
        }

        // What the coding agent is doing, from Claude Code hooks through the backend.
        private void Agent(string state, string detail)
        {
            bool fresh = state != agentState;
            agentState = state;
            switch (state)
            {
                case "working":
                    pill.Say(string.IsNullOrEmpty(detail) ? "claude is working" : detail);
                    if (fresh) sprite.React("dip");
                    break;
                case "needs_you":
                    pill.Say(string.IsNullOrEmpty(detail) ? "claude needs you" : detail);
                    if (fresh)
                    {
                        // Next to the cursor, so the question is where you are looking.
                        PixelPoint? at = Mouse.Position(sprite.Screens.Primary?.Scaling ?? 1);
                        if (at.HasValue) { sfx.Play("zap"); sprite.Teleport(sprite.StandingAt(new PixelPoint(at.Value.X + Px(90), at.Value.Y + Px(40)))); }
                        sprite.React("jolt");
                        bristle.Start();
                    }
                    break;
                case "done":
                    bristle.Stop();
                    pill.Say("done", 8);
                    if (fresh)
                    {
                        sfx.Play("chime");
                        // Centre of the screen for the victory lap; the dip on landing is her bow.
                        Screen screen = sprite.Screens.Primary;
                        if (screen != null)
                        {
                            PixelRect area = screen.WorkingArea;
                            sfx.Play("zap");
                            sprite.Teleport(sprite.StandingAt(new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2 + Px(sprite.Height / 2))));
                        }
                        if (!string.IsNullOrEmpty(detail) && detail != "done") bubble.Say(detail, mood.Mood);
                    }
                    break;
                case "error":
                    sfx.Play("buzz");
                    pill.Say(string.IsNullOrEmpty(detail) ? "something broke" : detail, 8);
                    sprite.React("shake");
                    break;
                default:
                    bristle.Stop();
                    pill.Clear();
                    break;
            }
        }

        private void Mouth(bool on)
        {
            mouthHolds = Math.Max(0, mouthHolds + (on ? 1 : -1));
            sprite.Talking = mouthHolds > 0;
        }

        private async Task Ask(string text)
        {
            thinking = true;
            sprite.React("dip");
            try
            {
                (string visible, string replyMood, string audio) = await backend.Chat(text);
                thinking = false;
                mood.Set(replyMood, mood.Trust);
                bubble.Say(visible, replyMood);
                if (audio != null) _ = voice.Play(audio);
            }
            catch (Exception)
            {
                thinking = false;
                bubble.Say("[offline] i can't reach my backend right now.", "neutral");
            }
        }
    }
}
