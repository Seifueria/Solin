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
        private ChatWindow chat;
        private readonly Voice voice = new Voice();
        private readonly Sfx sfx = new Sfx();
        // The bubble typing and her voice playing each move her mouth; it stops when neither is on.
        private int mouthHolds;
        private bool thinking;
        // Throws in a row; the third one she takes personally.
        private int throws;
        // Recent pokes; a flurry of them is pestering, and small she has less patience.
        private readonly System.Collections.Generic.List<double> pokes = new System.Collections.Generic.List<double>();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private MenuItem miniItem;
        // Presence: how long you have been away, how long you have worked without a real pause.
        private readonly DispatcherTimer presence = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private double activeSeconds;
        private DateTime? sleptAt;
        private const double PauseIs = 5 * 60, LongAway = 20 * 60;
        // From settings: seconds; a break threshold of 0 turns the nudge off.
        private double SleepAfter = 8 * 60, BreakAfter = 2 * 3600;
        private SettingsWindow settings;
        private FxLayer trail, fx;
        private Card card;
        private FlourishWindow flourish;
        private PointerWindow pointer;
        // Apps Claude Code tends to live in, frontmost first.
        private static readonly string[] ClaudeHomes = { "Claude", "Terminal", "iTerm2", "Warp", "Ghostty", "kitty", "Alacritty", "Code", "Cursor", "Windsurf" };
        private string agentProject;
        private static readonly string[] DoneLines = { "Mission accomplished.", "Done. Told you.", "Wrapped. Go look.", "That's handled." };
        private static readonly string[] WaitLines = { "Waiting on your go-ahead.", "Claude needs you.", "It's asking. Answer it." };
        private static readonly string[] ErrorLines = { "That one broke.", "Ow. Error.", "Something snapped." };
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
                trail = new FxLayer(sprite, under: true);
                fx = new FxLayer(sprite, under: false);
                sprite.Layers.Children.Insert(0, trail);
                sprite.Layers.Children.Add(fx);
                typer = new Typer();
                bubble = new Bubble();
                pill = new Pill();
                chat = new ChatWindow();
                settings = new SettingsWindow();
                card = new Card();
                flourish = new FlourishWindow();
                pointer = new PointerWindow();
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
                    chat.AddHer(visible, replyMood, null);
                    bubble.Say(visible, replyMood ?? mood.Mood);
                    if (audio != null) _ = voice.Play(audio);
                });
            };
            sprite.Poked += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                pokes.RemoveAll(t => now - t > 6);
                pokes.Add(now);
                if (pokes.Count >= (sprite.Mini ? 3 : 5))
                {
                    pokes.Clear();
                    sprite.React("jolt");
                    sprite.Jitter(0.4);
                    sfx.Play("buzz");
                    _ = backend.Event("pestered");
                    return;
                }
                sprite.React("dip");
                sfx.Play("blip");
                _ = backend.Event("poked");
            };
            sprite.CursorAt = () => Mouse.Position(sprite.Screens.Primary?.Scaling ?? 1);
            sprite.ContextMenu = Menu();
            if (Environment.GetEnvironmentVariable("SOLIN_MINI") == "1") sprite.Resize(SpriteWindow.MiniSize);
            sprite.Dragged += delegate { _ = backend.Event("dragged"); };
            sprite.Thrown += delegate { throws++; _ = backend.Event(throws >= 3 ? "thrown" : "dragged"); };
            sprite.Perched += delegate { sfx.Play("tick"); sprite.React("dip"); };
            sprite.Fell += delegate { sfx.Play("blip"); };
            sprite.FlightLanded += delegate { sprite.React(throws >= 3 ? "jolt" : "dip"); if (throws >= 3) { fx.Impact(); sprite.Jitter(0.5); throws = 0; } };
            sprite.TalkAsked += delegate { OpenTyper(); };
            bubble.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.12)));
            bubble.Speaking += delegate(bool on) { Mouth(on); sprite.Busy = on; if (on) sfx.Play("tick"); };
            flourish.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            // Leaving: she breaks into shards. Arriving: a column of light where she reforms. Done: speed lines and her face in a cut-in.
            sprite.TeleportStarted += delegate { flourish.Shatter(sprite.ScreenCentre, sprite.BodySize); };
            sprite.Landed += delegate
            {
                sprite.Jitter(0.25);
                flourish.Beam(sprite.ScreenCentre, sprite.BodySize);
                if (agentState == "done") flourish.Burst(sprite.ScreenCentre, sprite.BodySize);
            };
            voice.Speaking += delegate(bool on) { Dispatcher.UIThread.Post(() => Mouth(on)); };
            bubble.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            pill.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.04)));
            sprite.PositionChanged += delegate { bubble.Reposition(); pill.Reposition(); };
            bristle.Tick += delegate { if (agentState == "needs_you") sprite.React("jolt"); else bristle.Stop(); };
            backend.AgentEvent += delegate(System.Text.Json.JsonElement root)
            {
                string state = root.GetProperty("state").GetString();
                string detail = root.TryGetProperty("detail", out System.Text.Json.JsonElement d) ? d.GetString() : "";
                string projectName = root.TryGetProperty("project", out System.Text.Json.JsonElement p) && p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() : null;
                string statsLine = null;
                if (root.TryGetProperty("report", out System.Text.Json.JsonElement r) && r.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    int actions = r.TryGetProperty("actions", out System.Text.Json.JsonElement a) ? a.GetInt32() : 0;
                    int files = r.TryGetProperty("files", out System.Text.Json.JsonElement f) ? f.GetArrayLength() : 0;
                    int seconds = r.TryGetProperty("seconds", out System.Text.Json.JsonElement sec) ? sec.GetInt32() : 0;
                    statsLine = actions + (actions == 1 ? " action" : " actions") + " · " + files + (files == 1 ? " file" : " files") + " · " + (seconds >= 60 ? seconds / 60 + " min" : seconds + " s");
                }
                Dispatcher.UIThread.Post(() => { agentProject = projectName ?? agentProject; Agent(state, detail, statsLine); });
            };
            card.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.02)));
            card.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            typer.Sent += delegate(string text) { chat.AddYou(text); _ = Ask(text); };
            chat.Sent += delegate(string text) { _ = Ask(text); };
            settings.Applied += async delegate(object body)
            {
                (System.Text.Json.JsonDocument doc, string error) = await backend.SaveSettings(body);
                using (doc)
                {
                    if (doc.RootElement.TryGetProperty("companion", out System.Text.Json.JsonElement c)) ApplyCompanion(c);
                    else if (doc.RootElement.TryGetProperty("settings", out System.Text.Json.JsonElement st) && st.TryGetProperty("companion", out System.Text.Json.JsonElement c2)) ApplyCompanion(c2);
                    chat.SetBrain(await backend.BrainName());
                }
                return error;
            };
            backend.SettingsChanged += delegate(System.Text.Json.JsonElement c) { Dispatcher.UIThread.Post(() => ApplyCompanion(c)); };
            settings.Action += async delegate(string act)
            {
                switch (act)
                {
                    case "bring":
                        {
                            Screen screen = sprite.Screens.Primary;
                            if (screen == null) break;
                            PixelRect area = screen.WorkingArea;
                            sfx.Play("zap");
                            sprite.Teleport(sprite.StandingAt(new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2 + Px(sprite.Height / 2))));
                            break;
                        }
                    case "mini": SetMini(!sprite.Mini); break;
                    case "lie": if (sprite.Lying) sprite.SitUp(); else sprite.LieDown(); break;
                    case "quit": if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk) desk.Shutdown(); break;
                    case "test-voice":
                        try { await backend.Say("[soft] testing, testing. can you hear me?", mood.Mood, true); } catch (Exception) { }
                        break;
                    case "refresh":
                        using (System.Text.Json.JsonDocument doc = await backend.Settings())
                            if (doc != null) settings.Load(doc.RootElement);
                        break;
                }
            };
            chat.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            mood.Changed += delegate(MoodState state) { chat.SetMood(state.Mood); };
            presence.Tick += delegate { Presence(); };
            sprite.Opened += async delegate
            {
                presence.Start();
                Entrance();
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOLIN_DEBUG_WINDOWS"))) Console.WriteLine("[windows] " + sprite.DescribeLedges());
                if (SpriteWindow.DebugPerch)
                {
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[perch] " + sprite.DebugSit()), TimeSpan.FromSeconds(3));
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[perch] after 2s: " + sprite.DebugSeat()), TimeSpan.FromSeconds(5));
                }
                if (await launcher.EnsureRunning(backend))
                {
                    backend.Listen();
                    chat.Load(await backend.History());
                    chat.SetBrain(await backend.BrainName());
                    using (System.Text.Json.JsonDocument doc = await backend.Settings())
                        if (doc != null && doc.RootElement.TryGetProperty("companion", out System.Text.Json.JsonElement c)) ApplyCompanion(c);
                }
            };
        }

        // Right-click on her.
        private ContextMenu Menu()
        {
            ContextMenu menu = new ContextMenu();
            miniItem = new MenuItem { Header = "Mini form" };
            miniItem.Click += delegate { SetMini(!sprite.Mini); };
            MenuItem chatItem = new MenuItem { Header = "Chat window" };
            chatItem.Click += delegate { OpenChat(); };
            menu.Items.Add(chatItem);
            MenuItem talk = new MenuItem { Header = "Talk to her" };
            // A beat after the menu has closed, or its closing steals the focus straight back.
            talk.Click += delegate { DispatcherTimer.RunOnce(OpenTyper, TimeSpan.FromMilliseconds(180)); };
            MenuItem lie = new MenuItem { Header = "Lie down" };
            lie.Click += delegate { if (sprite.Lying) sprite.SitUp(); else sprite.LieDown(); };
            menu.Items.Add(lie);
            MenuItem sleep = new MenuItem { Header = "Sleep" };
            sleep.Click += delegate { if (sprite.Asleep) sprite.Wake(); else sprite.Sleep(); };
            MenuItem settingsItem = new MenuItem { Header = "Settings…" };
            settingsItem.Click += delegate { OpenSettings(); };
            menu.Items.Add(settingsItem);
            MenuItem quit = new MenuItem { Header = "Quit" };
            quit.Click += delegate { if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown(); };
            menu.Items.Add(talk);
            menu.Items.Add(miniItem);
            menu.Items.Add(sleep);
            menu.Items.Add(new Separator());
            menu.Items.Add(quit);
            menu.Opening += delegate { miniItem.Header = sprite.Mini ? "Full size" : "Mini form"; sleep.Header = sprite.Asleep ? "Wake up" : "Sleep"; lie.Header = sprite.Lying ? "Sit up" : "Lie down"; lie.IsEnabled = sprite.Seated; };
            return menu;
        }

        // The typing box under her feet, centred.
        private void OpenTyper()
        {
            typer.Open(new PixelPoint(sprite.Position.X + Px(sprite.Width / 2) - Px(175), sprite.Position.Y + Px(sprite.Height) - Px(30)));
        }

        private async void OpenSettings()
        {
            using (System.Text.Json.JsonDocument doc = await backend.Settings())
                if (doc != null) settings.Load(doc.RootElement);
            settings.Open(new PixelPoint(sprite.Position.X - Px(420), Math.Max(sprite.Screens.Primary?.WorkingArea.Y ?? 0, sprite.Position.Y - Px(260))));
        }

        // Companion-side settings from the backend's document: size, sounds, wandering, sleep and break timings.
        private void ApplyCompanion(System.Text.Json.JsonElement c)
        {
            if (c.TryGetProperty("sounds", out System.Text.Json.JsonElement so)) sfx.Level = so.GetDouble();
            if (c.TryGetProperty("wander", out System.Text.Json.JsonElement wa)) sprite.Wanderlust = wa.GetDouble();
            if (c.TryGetProperty("sleep_after_min", out System.Text.Json.JsonElement sl)) SleepAfter = sl.GetDouble() * 60;
            if (c.TryGetProperty("break_after_min", out System.Text.Json.JsonElement br)) BreakAfter = br.GetDouble() * 60;
            if (c.TryGetProperty("size", out System.Text.Json.JsonElement sz)) sprite.Resize(sz.GetString() == "mini" ? SpriteWindow.MiniSize : SpriteWindow.FullSize);
        }

        private async void OpenChat()
        {
            chat.SetBrain(await backend.BrainName());
            if (chat.IsVisible) { chat.Activate(); return; }
            chat.Open(new PixelPoint(sprite.Position.X - Px(400), Math.Max(sprite.Screens.Primary?.WorkingArea.Y ?? 0, sprite.Position.Y - Px(200))));
        }

        private void SetMini(bool mini)
        {
            sfx.Play("zap");
            sprite.Resize(mini ? SpriteWindow.MiniSize : SpriteWindow.FullSize);
            sprite.React("dip");
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
            if (BreakAfter > 0 && activeSeconds >= BreakAfter && !bubble.IsVisible && agentState != "working" && !sprite.Asleep)
            {
                activeSeconds = 0;
                bubble.Say(BreakLines[pick.Next(BreakLines.Length)], "calm");
            }
        }

        // What the coding agent is doing, from Claude Code hooks through the backend.
        private string Pick(string[] lines) { return lines[pick.Next(lines.Length)]; }

        private void Agent(string state, string detail, string statsLine = null)
        {
            bool fresh = state != agentState;
            agentState = state;
            switch (state)
            {
                case "working":
                    pill.Say(string.IsNullOrEmpty(detail) ? "claude is working" : detail);
                    if (fresh) { sprite.React("dip"); card.Dismiss(); pointer.Stop(); }
                    // Running a command or going on the web: sparks off her fingers.
                    if (detail != null && (detail.StartsWith("bash") || detail.StartsWith("webfetch") || detail.StartsWith("websearch") || detail.StartsWith("edit") || detail.StartsWith("write"))) fx.Sparks();
                    break;
                case "needs_you":
                    pill.Say(string.IsNullOrEmpty(detail) ? "claude needs you" : detail);
                    if (fresh)
                    {
                        card.Show(agentProject, "WAITING", Card.Red, Pick(WaitLines), detail);
                        PixelRect? home = sprite.WindowOf(ClaudeHomes);
                        if (home.HasValue) DispatcherTimer.RunOnce(() => { if (agentState == "needs_you") pointer.Point(() => sprite.HandScreen, () => sprite.WindowOf(ClaudeHomes)); }, TimeSpan.FromSeconds(1.3));
                        // Next to the cursor, so the question is where you are looking.
                        PixelPoint? at = Mouse.Position(sprite.Screens.Primary?.Scaling ?? 1);
                        if (at.HasValue) { sfx.Play("zap"); sprite.Teleport(sprite.StandingAt(new PixelPoint(at.Value.X + Px(90), at.Value.Y + Px(40)))); }
                        sprite.React("jolt");
                        bristle.Start();
                    }
                    break;
                case "done":
                    bristle.Stop();
                    pointer.Stop();
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
                        card.Show(agentProject, "DONE", Card.Green, Pick(DoneLines), detail == "done" ? null : detail, statsLine, null, 14);
                    }
                    break;
                case "error":
                    sfx.Play("buzz");
                    fx.Impact();
                    sprite.Jitter(0.5);
                    pill.Say(string.IsNullOrEmpty(detail) ? "something broke" : detail, 8);
                    card.Show(agentProject, "ERROR", Card.Red, Pick(ErrorLines), detail, null, null, 10);
                    sprite.React("shake");
                    break;
                default:
                    bristle.Stop();
                    pointer.Stop();
                    pill.Clear();
                    card.Dismiss();
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
                chat.Thinking(true);
                BackendClient.Reply reply = await backend.Chat(text);
                thinking = false;
                chat.Thinking(false);
                mood.Set(reply.Mood, mood.Trust);
                chat.AddHer(reply.Visible, reply.Mood, reply.Internal);
                bubble.Say(reply.Visible, reply.Mood);
                string visible = reply.Visible, replyMood = reply.Mood, audio = reply.Audio;
                if (replyMood == "warm" || replyMood == "teasing") sprite.Grin();
                if (audio != null) _ = voice.Play(audio);
            }
            catch (Exception)
            {
                thinking = false;
                chat.Thinking(false);
                chat.AddHer("[offline] i can't reach my backend right now.", "neutral", null);
                bubble.Say("[offline] i can't reach my backend right now.", "neutral");
            }
        }
    }
}
