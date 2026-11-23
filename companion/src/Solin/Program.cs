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
        private FrameFxWindow frameFx;
        private SkyWindow sky;
        private PossessWindow possess;
        private WakeWindow wake;
        private readonly Music music = new Music();
        private MusicPlayer player;
        private readonly DispatcherTimer musicTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private readonly DispatcherTimer noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.3) };
        private string playerDismissedFor;
        private DateTime pausedAt = DateTime.MaxValue;
        private bool possessPending;
        private readonly DispatcherTimer skyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        // Which windows were on screen last look; a wholesale change means you swiped to another Space and she came along.
        private System.Collections.Generic.HashSet<long> sceneWas = new System.Collections.Generic.HashSet<long>();
        private readonly DispatcherTimer scene = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        private DateTime lastSpaceHop = DateTime.MinValue;
        private ulong spaceWas;
        private DateTime lastFront = DateTime.MinValue;
        private readonly Vitals vitals = new Vitals();
        private TrayIcon tray;
        private bool hidden;
        // Claude's last sign of life, for the "thinking it over" ticker when a task goes quiet.
        private DateTime agentSeen = DateTime.MinValue;
        private int quietStage;
        private readonly DispatcherTimer quiet = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private int tourStep = -1;
        private bool lookPermissionAsked;
        private static readonly (string title, string line)[] Tour =
        {
            ("Hey. I'm Solin.", "This is me on your desk. Drag me anywhere. Drop me on a window and I'll sit on it; let go over nothing and I'll float."),
            ("Talk to me.", "Double-click me for a quick line, or right-click for my menu: chat window, settings, mini form, sleep, quit."),
            ("I watch Claude Code too.", "While it works I'll show what it's doing. When it needs you I'll come find your cursor and point at its window."),
            ("My icon's up there.", "By the clock. Click it for my menu, or to hide me and bring me back."),
        };
        private readonly DispatcherTimer vitalsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
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
            Spaces.BeAgent();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                sprite = new SpriteWindow();
                Spaces.Everywhere(sprite);
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
                frameFx = new FrameFxWindow();
                sky = new SkyWindow();
                possess = new PossessWindow();
                wake = new WakeWindow();
                player = new MusicPlayer();
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
            backend.Replied += delegate(string visible, string replyMood, string audio, string kind)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (kind == "reminder")
                    {
                        // Something you asked her to remind you of: a card, a chime, and her voice if it is on.
                        sfx.Play("chime");
                        sprite.React("jolt");
                        card.Show("you", "REMINDER", Card.Violet, "You asked me to remind you.", visible, null, null, 30);
                        chat.AddHer("reminder: " + visible, replyMood, null);
                        if (audio != null) _ = voice.Play(audio);
                        return;
                    }
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
            bubble.Speaking += delegate(bool on) { Mouth(on); sprite.Busy = on; possess.Talking = on; if (on) sfx.Play("tick"); };
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
                    case "bring": settingsActionBring(); break;
                    case "mini": SetMini(!sprite.Mini); break;
                    case "replay-screen": Entrance("screen"); break;
                    case "replay-wake": Entrance("wake"); break;
                    case "tour": StartTour(); break;
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
            // The Mac's own state: she remarks on chargers, low battery, lost network and heavy load.
            vitals.Changed += delegate(string kind, string detail)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _ = backend.Event(kind);
                    switch (kind)
                    {
                        case "battery_critical": sfx.Play("buzz"); card.Show("mac", "BATTERY", Card.Red, "Plug me in.", detail, null, null, 20); sprite.React("shake"); break;
                        case "battery_low": card.Show("mac", "BATTERY", Card.Red, "Running low.", detail, null, null, 12); sprite.React("jolt"); break;
                        case "unplugged": pill.Say(detail, 6, "MAC"); sprite.React("jolt"); break;
                        case "plugged": pill.Say(detail, 5, "MAC"); sprite.React("dip"); break;
                        case "offline": sfx.Play("buzz"); card.Show("mac", "OFFLINE", Card.Violet, "Network's gone.", "i can still hear you, but my brain may not answer.", null, null, 12); sprite.Jitter(0.6); break;
                        case "online": pill.Say(detail, 5, "MAC"); sprite.React("dip"); break;
                        case "hot": pill.Say(detail, 8, "MAC"); sprite.React("shake"); break;
                        case "cooled": pill.Say(detail, 4, "MAC"); break;
                    }
                });
            };
            vitalsTimer.Tick += async delegate { await vitals.Poll(); };
            Tray();
            sky.Zapped += delegate { sfx.Play("zap"); sprite.Jitter(0.3); sprite.React("jolt"); };
            possess.Finished += delegate { sfx.Play("zap"); sprite.Arrive(0.5); sprite.Jitter(0.35); flourish.Beam(sprite.ScreenCentre, sprite.BodySize); };
            sprite.Landed += delegate { if (possessPending) { possessPending = false; if (sprite.Seated) DispatcherTimer.RunOnce(BeginPossession, TimeSpan.FromMilliseconds(400)); } };
            // Now and then, when nothing is going on, she looks up at the sky; more often at night.
            skyTimer.Tick += delegate
            {
                int hour = DateTime.Now.Hour;
                double chance = hour >= 20 || hour < 6 ? 0.12 : 0.03;
                if (pick.NextDouble() < chance) Stargaze();
                // Seated and left alone, once in a while she climbs into the window she is sitting on.
                else if (sprite.Seated && pick.NextDouble() < 0.04) Possess();
            };
            skyTimer.Start();
            // Music: the player shows under her while something plays, and she sways along.
            player.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height) - Px(16)));
            player.Command += delegate(string cmd) { _ = music.Control(cmd); };
            player.Closed2 += delegate { playerDismissedFor = music.Now?.Key; };
            music.Changed += delegate(Music.Track t) { Dispatcher.UIThread.Post(() => OnMusic(t)); };
            musicTimer.Tick += async delegate
            {
                await music.Poll();
                if (player.IsVisible) player.Update(music.Now);
                if (music.Now != null && !music.Now.Playing && (DateTime.Now - pausedAt).TotalSeconds > 25) player.Stop();
            };
            noteTimer.Tick += delegate { if (sprite.Grooving && !hidden) fx.Notes(); };
            musicTimer.Start();
            scene.Tick += delegate { SceneCheck(); };
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_LEVEL") == "1")
            {
                DispatcherTimer probe = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                probe.Tick += delegate { Console.WriteLine("[level] " + DateTime.Now.ToString("HH:mm:ss") + " " + Spaces.Describe(sprite)); };
                probe.Start();
            }
            scene.Start();
            quiet.Tick += delegate { QuietTicker(); };
            quiet.Start();
            card.Acknowledged += delegate
            {
                if (lookPermissionAsked) { lookPermissionAsked = false; ScreenShot.OpenPermissionPane(); return; }
                NextTourCard();
            };
            sprite.Opened += async delegate
            {
                presence.Start();
                _ = vitals.Poll();
                vitalsTimer.Start();
                Entrance();
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOLIN_DEBUG_WINDOWS"))) Console.WriteLine("[windows] " + sprite.DescribeLedges());
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_ICON") == "1")
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[icon] " + (sprite.MenuBarItem()?.ToString() ?? "not found") + sprite.DumpAll()), TimeSpan.FromSeconds(3));
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_POSSESS") == "2")
                    DispatcherTimer.RunOnce(() => { possess.Possess(() => new PixelRect(300, 300, 700, 420), 650, 5); Console.WriteLine("[possess] drawing on a test rectangle"); }, TimeSpan.FromSeconds(3));
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_POSSESS") == "1")
                {
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[possess] started " + Possess(asked: true) + " seated " + sprite.Seated), TimeSpan.FromSeconds(3));
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[possess] at 5.5s running " + possess.Running + " " + sprite.DebugSeat()), TimeSpan.FromSeconds(5.5));
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[possess] at 13s running " + possess.Running + " " + sprite.DebugSeat()), TimeSpan.FromSeconds(13));
                }
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_SKY") == "1")
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[sky] started " + Stargaze(asked: true)), TimeSpan.FromSeconds(3));
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_ROAM") == "1")
                {
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[roam] started " + sprite.TeleportToLedge(lie: true)), TimeSpan.FromSeconds(3));
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[roam] " + sprite.DebugSeat() + " lying " + sprite.Lying), TimeSpan.FromSeconds(6));
                }
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
                    using (System.Text.Json.JsonDocument doc2 = await backend.Settings())
                        if (doc2 != null && doc2.RootElement.TryGetProperty("companion", out System.Text.Json.JsonElement c2) && !(c2.TryGetProperty("toured", out System.Text.Json.JsonElement t) && t.ValueKind == System.Text.Json.JsonValueKind.True))
                            DispatcherTimer.RunOnce(StartTour, TimeSpan.FromSeconds(4.5));
                    // A hello for the time of day, once she has arrived.
                    if (Environment.GetEnvironmentVariable("SOLIN_QUIET_START") != "1") DispatcherTimer.RunOnce(() => _ = backend.Greet(), TimeSpan.FromSeconds(3.5));
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
            MenuItem talk = new MenuItem { Header = "Quick line" };
            // A beat after the menu has closed, or its closing steals the focus straight back.
            talk.Click += delegate { DispatcherTimer.RunOnce(OpenTyper, TimeSpan.FromMilliseconds(180)); };
            MenuItem lookItem = new MenuItem { Header = "Look at my screen" };
            lookItem.Click += delegate { chat.AddYou("what am I doing?"); _ = Ask("what am I doing right now?", look: true); };
            menu.Items.Add(lookItem);
            MenuItem possessItem = new MenuItem { Header = "Possess a window" };
            possessItem.Click += delegate { Possess(asked: true); };
            menu.Items.Add(possessItem);
            MenuItem starItem = new MenuItem { Header = "Stargaze" };
            starItem.Click += delegate { Stargaze(asked: true); };
            menu.Items.Add(starItem);
            MenuItem seatItem = new MenuItem { Header = "Find a seat" };
            seatItem.Click += delegate { if (!sprite.TeleportToLedge(lie: true)) pill.Say("nowhere to sit", 3, "SOLIN"); };
            menu.Items.Add(seatItem);
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

        // Her glyph in the menu bar: show or hide her, bring her here, chat, settings, quit.
        private void Tray()
        {
            try
            {
                tray = new TrayIcon { ToolTipText = "Solin" };
                using (System.IO.Stream icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://Solin.Companion/Assets/tray.png")))
                    tray.Icon = new WindowIcon(icon);
                NativeMenu menu = new NativeMenu();
                NativeMenuItem show = new NativeMenuItem("Hide Solin");
                show.Click += delegate { hidden = !hidden; if (hidden) { sprite.Hide(); bubble.Hide(); pill.Clear(); card.Dismiss(); } else sprite.Show(); show.Header = hidden ? "Show Solin" : "Hide Solin"; };
                NativeMenuItem bring = new NativeMenuItem("Bring here");
                bring.Click += delegate { settingsActionBring(); };
                NativeMenuItem lookTray = new NativeMenuItem("Look at my screen");
                lookTray.Click += delegate { chat.AddYou("what am I doing?"); _ = Ask("what am I doing right now?", look: true); };
                NativeMenuItem chatItem = new NativeMenuItem("Chat window");
                chatItem.Click += delegate { OpenChat(); };
                NativeMenuItem settingsItem = new NativeMenuItem("Settings…");
                settingsItem.Click += delegate { OpenSettings(); };
                NativeMenuItem quitItem = new NativeMenuItem("Quit Solin");
                quitItem.Click += delegate { if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk) desk.Shutdown(); };
                menu.Items.Add(show); menu.Items.Add(bring); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(lookTray); menu.Items.Add(chatItem); menu.Items.Add(settingsItem); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(quitItem);
                tray.Menu = menu;
                tray.IsVisible = true;
            }
            catch (Exception) { tray = null; }
        }

        private void settingsActionBring()
        {
            Screen screen = sprite.Screens.Primary;
            if (screen == null) return;
            PixelRect area = screen.WorkingArea;
            if (hidden) { hidden = false; sprite.Show(); }
            sfx.Play("zap");
            sprite.Teleport(sprite.StandingAt(new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2 + Px(sprite.Height / 2))));
        }

        // Her window lives on every Space, so a swipe brings her along; this notices the swipe from the desktop's own number
        // and sells it as an arrival: she blinks out, the beam drops in front, and she reforms out of it.
        private void SceneCheck()
        {
            ulong space = Spaces.ActiveSpace();
            bool swiped;
            if (space != 0)
            {
                swiped = spaceWas != 0 && space != spaceWas;
                spaceWas = space;
            }
            else
            {
                // Fallback when the desktop number cannot be read: most of the other windows changed at once.
                System.Collections.Generic.HashSet<long> now = sprite.WindowIds();
                int shared = 0;
                foreach (long id in now) if (sceneWas.Contains(id)) shared++;
                swiped = sceneWas.Count >= 2 && now.Count >= 1 && shared / (double)Math.Max(sceneWas.Count, now.Count) < 0.34;
                sceneWas = now;
            }
            if (swiped && (DateTime.Now - lastSpaceHop).TotalSeconds > 0.6)
            {
                lastSpaceHop = DateTime.Now;
                KeepInFront();
                if (!hidden && !sprite.Asleep && !sprite.Seated)
                {
                    sprite.HideForEntrance();
                    sfx.Play("zap");
                    flourish.Beam(sprite.ScreenCentre, sprite.BodySize);
                    DispatcherTimer.RunOnce(() => { sprite.Arrive(0.55); sprite.Jitter(0.3); }, TimeSpan.FromMilliseconds(160));
                }
            }
            // Every few seconds as well: the flags back on if anything reset them, and her windows to the front of their level.
            if ((DateTime.Now - lastFront).TotalSeconds > 4) KeepInFront();
        }

        // Her body first, then the windows that belong over her, so a card or a box is never hidden behind her.
        private void KeepInFront()
        {
            lastFront = DateTime.Now;
            if (hidden) return;
            foreach (Window w in new Window[] { sprite, player, wake, possess, sky, flourish, frameFx, pointer, chat, settings, card, bubble, pill, typer })
            {
                if (w == null) continue;
                Spaces.Reassert(w);
                Spaces.Front(w);
            }
        }

        // A task that has gone quiet: after fifteen seconds the pill says she's waiting on Claude's thinking, after a minute that it's slow.
        private void QuietTicker()
        {
            if (agentState != "working") return;
            double silent = (DateTime.Now - agentSeen).TotalSeconds;
            if (silent > 60 && quietStage < 2) { quietStage = 2; pill.Say("still thinking… slow one", 0, "CLAUDE"); sprite.React("dip"); }
            else if (silent > 15 && quietStage < 1) { quietStage = 1; pill.Say("thinking it over…", 0, "CLAUDE"); }
        }

        // First run: three cards she walks you through, each dismissed with a button; remembered in the backend's config.
        private void StartTour() { tourStep = -1; NextTourCard(); }

        private void NextTourCard()
        {
            tourStep++;
            if (tourStep >= Tour.Length)
            {
                tourStep = -1;
                pointer.Stop();
                _ = backend.SaveSettings(new { companion = new { toured = true } });
                return;
            }
            (string title, string line) = Tour[tourStep];
            card.Show("hello", "SYNC", Card.Violet, title, line, null, tourStep == Tour.Length - 1 ? "GOT IT" : "NEXT");
            // The last card points at her menu-bar icon, if it can be found.
            if (tourStep == Tour.Length - 1 && sprite.MenuBarItem().HasValue) pointer.Point(() => sprite.HandScreen, () => sprite.MenuBarItem(), true, "MY ICON");
            else pointer.Stop();
        }

        // She sinks into a window and wears it for a few seconds; if she is not on one, she teleports onto one first.
        private bool Possess(bool asked = false)
        {
            bool busy = hidden || thinking || sprite.Asleep || possess.Running || sky.Running || (agentState == "working" || agentState == "needs_you");
            if (busy && !asked) return false;
            if (hidden || possess.Running) return false;
            if (sprite.Seated) { BeginPossession(); return true; }
            possessPending = true;
            if (!sprite.TeleportToLedge(lie: false)) { possessPending = false; pill.Say("no window to climb into", 3, "SOLIN"); return false; }
            return true;
        }

        private void BeginPossession()
        {
            if (!sprite.Seated || possess.Running) return;
            sfx.Play("buzz");
            int entry = sprite.ScreenCentre.X;
            sprite.HideForEntrance();
            possess.Possess(() => sprite.SeatBounds, entry, 7);
        }

        private void OnMusic(Music.Track t)
        {
            bool playing = t != null && t.Playing;
            sprite.Grooving = playing;
            if (playing) { noteTimer.Start(); pausedAt = DateTime.MaxValue; } else { noteTimer.Stop(); pausedAt = DateTime.Now; }
            if (t == null) { player.Stop(); return; }
            if (playing && t.Key != playerDismissedFor && !hidden) player.Show(t);
            else if (player.IsVisible) player.Update(t);
        }

        // She looks up: constellations, a star, a trail from her hand, then a zap. Only when nothing else is going on.
        private bool Stargaze(bool asked = false)
        {
            bool busy = hidden || thinking || sprite.Asleep || sky.Running || bubble.IsVisible || card.IsVisible || (agentState == "working" || agentState == "needs_you");
            if (busy && !asked) return false;
            if (hidden || sky.Running) return false;
            sfx.Play("soft");
            sprite.Flourish("cock");
            sky.Gaze(() => sprite.HandScreen);
            return true;
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
            // Open at login: on by default from the app, since that is the point of the app; the setting can turn it off.
            bool wantLogin = !c.TryGetProperty("login", out System.Text.Json.JsonElement lg) || lg.ValueKind != System.Text.Json.JsonValueKind.False;
            LoginItem.Set(wantLogin);
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
        // Which entrance to play: "screen", "wake" or "random", from companion.entrance in data/config.json, read directly
        // because her entrance plays before the backend is up.
        private static string EntranceSetting()
        {
            string env = Environment.GetEnvironmentVariable("SOLIN_ENTRANCE");
            if (!string.IsNullOrEmpty(env)) return env;
            try
            {
                string repo = BackendLauncher.FindRepo();
                string file = repo == null ? null : System.IO.Path.Combine(repo, "data", "config.json");
                if (file == null || !System.IO.File.Exists(file)) return "random";
                string text = System.IO.File.ReadAllText(file).Trim();
                if (text.Length == 0) return "random";
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(text))
                    if (doc.RootElement.TryGetProperty("companion", out System.Text.Json.JsonElement c) && c.TryGetProperty("entrance", out System.Text.Json.JsonElement e))
                        return e.GetString() ?? "random";
            }
            catch (Exception) { }
            return "random";
        }

        private void Entrance(string kind = null)
        {
            kind = kind ?? EntranceSetting();
            if (kind == "random") kind = pick.NextDouble() < 0.5 ? "wake" : "screen";
            if (kind == "wake")
            {
                sprite.HideForEntrance();
                wake.Finished -= WakeDone;
                wake.Finished += WakeDone;
                sfx.Play("soft");
                wake.Play(sprite.ScreenCentre);
                return;
            }
            IntroFx intro = new IntroFx(sprite.BodyCentre, sprite.BodySize);
            intro.ScreenLit += delegate { sfx.Play("zap"); };
            intro.Finished += delegate { sprite.Layers.Children.Remove(intro); sprite.Arrive(0.8); sfx.Play("soft"); };
            sprite.Layers.Children.Add(intro);
            intro.Start();
        }

        private void WakeDone()
        {
            sfx.Play("zap");
            sprite.Arrive(0.7);
            sprite.Jitter(0.3);
            flourish.Beam(sprite.ScreenCentre, sprite.BodySize);
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
            agentSeen = DateTime.Now;
            quietStage = 0;
            switch (state)
            {
                case "working":
                    pill.Say(string.IsNullOrEmpty(detail) ? "claude is working" : detail);
                    if (fresh) { sprite.React("dip"); card.Dismiss(); pointer.Stop(); }
                    // Running a command or going on the web: sparks off her fingers.
                    if (detail != null && (detail.StartsWith("bash") || detail.StartsWith("webfetch") || detail.StartsWith("websearch") || detail.StartsWith("edit") || detail.StartsWith("write")))
                    {
                        fx.Sparks();
                        // Red lightning races round the window Claude is working in.
                        PixelRect? home = sprite.WindowOf(ClaudeHomes);
                        if (home.HasValue) frameFx.Race(home.Value);
                    }
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
                    sprite.KnockBack(toLeft: true);
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

        // Asking her to look: these take a screenshot first. Nothing is captured otherwise.
        private static readonly System.Text.RegularExpressions.Regex LookAsk = new System.Text.RegularExpressions.Regex(
            @"\b(look at|see|check|read)\b[^.?!]*\bscreen\b|\bwhat am i (doing|working on|looking at)\b|\bwhat do you see\b|\bwhat'?s on my screen\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private async Task Ask(string text, bool look = false)
        {
            thinking = true;
            sprite.React("dip");
            string shot = null;
            try
            {
                chat.Thinking(true);
                if (look || LookAsk.IsMatch(text))
                {
                    pill.Say("looking at your screen…", 0, "SOLIN");
                    shot = await ScreenShot.Capture();
                    pill.Clear();
                    if (shot == null)
                    {
                        thinking = false;
                        chat.Thinking(false);
                        card.Show("mac", "PERMISSION", Card.Red, "I can't see your screen yet.",
                            "Turn Solin on in System Settings › Privacy & Security › Screen Recording, then quit and reopen me.", null, "OPEN SETTINGS", 30);
                        lookPermissionAsked = true;
                        return;
                    }
                    fx.Sparks(8);
                }
                BackendClient.Reply reply = await backend.Chat(text, null, shot);
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
            finally { ScreenShot.Forget(shot); }
        }
    }
}
