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
        private TalkPill talkPill;
        // Her voice on or off, as the wave on the pill shows it; read from her settings when the backend is up.
        private bool voiceOn;
        private readonly DispatcherTimer pillAway = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        private Bubble bubble;
        private Pill pill;
        private ChatWindow chat;
        private readonly Voice voice = new Voice();
        private readonly Sfx sfx = new Sfx();
        // The bubble typing and her voice playing each move her mouth; it stops when neither is on.
        // Her mouth moves while her bubble is typing or her voice is playing; one flag each, so a stray start never sticks.
        private bool bubbleTalking, voiceTalking;
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
        private GrabWindow grab;
        private MaskWindow mask;
        private EmoteWindow emote;
        // "away": put away by you, waiting in the corner. "dream": asleep long enough to come apart where she stood.
        private string maskMode;
        private readonly Music music = new Music();
        private MusicPlayer player;
        private readonly DispatcherTimer musicTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private readonly DispatcherTimer noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.3) };
        private string playerDismissedFor;
        private DateTime pausedAt = DateTime.MaxValue;
        private bool possessPending;
        private readonly DispatcherTimer skyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        // Spells she starts herself and ends herself: a nap, being put away, a stretch in mini form.
        private DateTime? napUntil, selfAwayUntil, selfMiniUntil;
        // Her last few idle actions, newest first, so she varies what she does.
        private readonly System.Collections.Generic.List<string> recentIdle = new System.Collections.Generic.List<string>();
        // Mischief (on unless turned off in settings): teleport chains, tugging your cursor, a fake "infection" terminal,
        // lightning round a window. Nothing that changes anything on the Mac.
        private bool mischief = true;
        private InfectWindow infect;
        private int hopsLeft;
        // A screen hop asked for her terminal line on arrival (shown after the last hop of a chain).
        private bool rootOnLanding;
        // Glancing at your screen now and then for something to say (on unless turned off): every few minutes while you are
        // at the Mac. The screenshot goes to her brain and is deleted straight after.
        private bool watchScreen = true, glanceRefused;
        private double watchEvery = 2;
        private DateTime nextGlance = DateTime.Now.AddMinutes(2);
        private readonly DispatcherTimer glanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        // Which windows were on screen last look; a wholesale change means you swiped to another Space and she came along.
        private System.Collections.Generic.HashSet<long> sceneWas = new System.Collections.Generic.HashSet<long>();
        private readonly DispatcherTimer scene = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        private DateTime lastSpaceHop = DateTime.MinValue;
        private ulong spaceWas;
        // With two or more displays: how many checks in a row her window has been out of view.
        private int outOfView;
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
        // Her hands: opening, clicking, running (running always waits for your Run). Off in Settings turns all of it off.
        private RunCard runCard;
        private bool handsOn = true;
        // Opening, clicking or running on her own, from a glance: not more often than every 2 minutes.
        private DateTime nextOwnHands = DateTime.MinValue;
        private bool clickPermissionAsked, clickPermissionCard;
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
            "[calm] two hours straight. stretch.", "[soft] go stand up a minute.",
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
                talkPill = new TalkPill();
                infect = new InfectWindow();
                runCard = new RunCard();
                // Debugging: each finished terminal kept as a picture, numbered.
                string rootSnaps = Environment.GetEnvironmentVariable("SOLIN_DEBUG_SNAP");
                if (!string.IsNullOrEmpty(rootSnaps)) { int snapped = 0; infect.Finished += () => Snap(infect, System.IO.Path.Combine(rootSnaps, "root-" + (++snapped) + ".png")); }
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
                grab = new GrabWindow();
                mask = new MaskWindow();
                emote = new EmoteWindow();
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
            sprite.MiniArt = ArtLibrary.Mini;
            sprite.ContextMenu = Menu();
            if (Environment.GetEnvironmentVariable("SOLIN_MINI") == "1") sprite.Resize(SpriteWindow.MiniSize);
            sprite.Dragged += delegate { _ = backend.Event("dragged"); };
            sprite.Thrown += delegate { throws++; _ = backend.Event(throws >= 3 ? "thrown" : "dragged"); };
            sprite.Perched += delegate { sfx.Play("tick"); sprite.React("dip"); };
            sprite.Fell += delegate { sfx.Play("blip"); };
            sprite.FlightLanded += delegate { sprite.React(throws >= 3 ? "jolt" : "dip"); if (throws >= 3) { fx.Impact(); sprite.Jitter(0.5); throws = 0; } };
            sprite.TalkAsked += delegate { OpenTyper(); };
            bubble.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.12)));
            bubble.Speaking += delegate(bool on) { bubbleTalking = on; Mouth(on); sprite.Busy = on; possess.Talking = on; if (on) sfx.Play("tick"); };
            flourish.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            // Leaving: she breaks into shards. Arriving: a column of light where she reforms. Done: speed lines and her face in a cut-in.
            sprite.TeleportStarted += delegate { flourish.Shatter(sprite.ScreenCentre, sprite.BodySize); };
            sprite.Landed += delegate
            {
                if (sprite.QuietLanding) { sprite.QuietLanding = false; sprite.Jitter(0.7); return; }
                // No beam on any teleport for now (Kaelen's call). A finished task still gets its burst of speed lines and sparks.
                if (agentState == "done") { sprite.Jitter(0.3); fx.Sparks(12); flourish.Burst(sprite.ScreenCentre, sprite.BodySize, panel: false); return; }
                // Otherwise sparks off her now and then, else just the glitch.
                if (pick.NextDouble() < 0.4) { sprite.Jitter(0.3); fx.Sparks(10); }
                else sprite.Jitter(0.6);
            };
            voice.Speaking += delegate(bool on) { Dispatcher.UIThread.Post(() => { voiceTalking = on; Mouth(on); }); };
            bubble.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            // Reference faces for her mood, when the art is on this Mac; the live crop otherwise.
            bubble.Still = () => ArtLibrary.Face(mood.Mood);
            chat.Still = () => ArtLibrary.Face(mood.Mood);
            pill.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.04)));
            // One thing talks at a time: the card first, then her bubble, then the pill.
            pill.Hold = () => card.IsVisible || bubble.IsVisible;
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
                    if (actions > 0 || files > 0) statsLine = actions + (actions == 1 ? " action" : " actions") + " · " + files + (files == 1 ? " file" : " files") + " · " + (seconds >= 60 ? seconds / 60 + " min" : seconds + " s");
                }
                Dispatcher.UIThread.Post(() => { agentProject = projectName ?? agentProject; Agent(state, detail, statsLine); });
            };
            card.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.02)));
            runCard.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.12)));
            card.Frame = () => (sprite.ShownClip, sprite.ShownCell, sprite.ShownMouthClip, sprite.ShownMouth);
            card.Still = () => ArtLibrary.Face(agentState == "done" ? "warm" : agentState == "needs_you" ? "curious" : agentState == "error" ? "annoyed" : mood.Mood);
            typer.Sent += delegate(string text) { chat.AddYou(text); _ = Ask(text); };
            // Over her head: over her reply when one is up, else just over her.
            typer.Follow(() => bubble.IsVisible ? new PixelPoint(bubble.Top.X, bubble.Top.Y - Px(6)) : new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.12)));
            // The pencil and wave under her while you hover her (or it), and while the typing box is open.
            talkPill.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.ScreenCentre.Y + Px(sprite.BodySize * 0.52)));
            talkPill.TypeClicked += delegate { OpenTyper(); };
            talkPill.VoiceClicked += delegate
            {
                voiceOn = !voiceOn;
                talkPill.Muted = !voiceOn;
                sfx.Play(voiceOn ? "blip" : "tick");
                pill.Say(voiceOn ? "voice on" : "voice off", 2, "SOLIN");
                _ = backend.SaveSettings(new { voice = new { speak = voiceOn } });
            };
            sprite.PointerEntered += delegate { if (!hidden && maskMode == null) { pillAway.Stop(); talkPill.Pop(); } };
            sprite.PointerExited += delegate { pillAway.Start(); };
            talkPill.PointerEntered += delegate { pillAway.Stop(); };
            talkPill.PointerExited += delegate { pillAway.Start(); };
            pillAway.Tick += delegate
            {
                talkPill.Typing = typer.IsVisible;
                if (typer.IsVisible || talkPill.Hovered) return;
                pillAway.Stop();
                talkPill.Hide();
            };
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
                    case "replay-grab": Entrance("grab"); break;
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
            // She turns to the star so the trail and bolt leave the hand on its side.
            sky.Aiming = p => { sprite.Aim = p; sprite.ArmAlways = p.HasValue; };
            sky.Zapped += delegate { sfx.Play("zap"); sprite.Jitter(0.3); sprite.React("jolt"); };
            mask.Clicked += delegate { FromMask(); };
            emote.Finished += delegate { if (emoteHid) { emoteHid = false; sprite.Arrive(0.35); } };
            pointer.Aiming = p => { sprite.Aim = p; if (!p.HasValue) sprite.ArmAlways = false; };
            mask.Hovered += delegate { if (maskMode == "dream") FromMask(); };
            grab.Beat += delegate(string beat)
            {
                switch (beat)
                {
                    case "hands": sfx.Play("soft"); break;
                    case "grab": sfx.Play("buzz"); break;
                    case "hi": sfx.Play("chime"); break;
                    case "smear": sfx.Play("zap"); break;
                }
            };
            possess.Finished += delegate { sfx.Play("zap"); sprite.Arrive(0.5); sprite.Jitter(0.35); fx.Sparks(10); };
            sprite.Landed += delegate { if (possessPending) { possessPending = false; if (sprite.Seated) DispatcherTimer.RunOnce(BeginPossession, TimeSpan.FromMilliseconds(400)); } };
            // Now and then, when nothing is going on, she looks up at the sky; more often at night.
            skyTimer.Tick += delegate { IdleTurn(); };
            glanceTimer.Tick += async delegate { await Glance(); };
            glanceTimer.Start();
            // A chain of quick hops: the next one as soon as she lands, until it runs out.
            sprite.Landed += delegate
            {
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_SCREENS") == "1")
                    Console.WriteLine("[screens] landed at " + sprite.Position + " on " + (sprite.Screens.ScreenFromPoint(sprite.ScreenCentre)?.Bounds.ToString() ?? "?") + " render " + sprite.RenderScaling + " frame " + Spaces.Describe(sprite) + " centre " + sprite.ScreenCentre + " hand " + sprite.HandScreen);
                if (hopsLeft > 0)
                {
                    hopsLeft--;
                    DispatcherTimer.RunOnce(() =>
                    {
                        double roll = pick.NextDouble();
                        if (roll < 0.25 && sprite.Screens.All.Count > 1) HopScreens();
                        else if (roll < 0.6 && sprite.TeleportToLedge(lie: false)) sfx.Play("zap");
                        else HopSomewhere();
                    }, TimeSpan.FromMilliseconds(140));
                }
                else
                {
                    sprite.TeleportSpeed = 1;
                    if (rootOnLanding)
                    {
                        rootOnLanding = false;
                        DispatcherTimer.RunOnce(() => { if (!bubble.IsVisible && !card.IsVisible && !hidden && maskMode == null) Infect(infect.HopScript(ScreenNumber())); }, TimeSpan.FromMilliseconds(450));
                    }
                }
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
            // Her window is a square, and macOS gives it every click inside it. So it lets clicks through except where the
            // cursor is on her art (or while she is held), and whatever is beside or behind her gets them.
            DispatcherTimer passClicks = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            passClicks.Tick += delegate { PassClicks(); };
            passClicks.Start();
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
                if (clickPermissionCard) { clickPermissionCard = false; Hands.OpenClickPermission(); return; }
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
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOLIN_DEBUG_EMOTE")))
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[emote] " + Environment.GetEnvironmentVariable("SOLIN_DEBUG_EMOTE") + " started " + PlayEmote(Environment.GetEnvironmentVariable("SOLIN_DEBUG_EMOTE"), asked: true)), TimeSpan.FromSeconds(3));
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_MASK") == "1")
                {
                    DispatcherTimer.RunOnce(() => { ToMask("away"); Console.WriteLine("[mask] away, visible " + mask.IsVisible); }, TimeSpan.FromSeconds(3));
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[mask] at 4s visible " + mask.IsVisible + " sprite " + sprite.IsVisible), TimeSpan.FromSeconds(4));
                    DispatcherTimer.RunOnce(() => { FromMask(); Console.WriteLine("[mask] out, sprite " + sprite.IsVisible + " mask " + mask.IsVisible); }, TimeSpan.FromSeconds(6));
                }
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
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_IDLE") == "1") skyTimer.Interval = TimeSpan.FromSeconds(4);
                // A line sent to her as if typed, and how many lines the chat log gains from it (one of yours, one of hers).
                string askTest = Environment.GetEnvironmentVariable("SOLIN_DEBUG_ASK");
                if (!string.IsNullOrEmpty(askTest))
                    DispatcherTimer.RunOnce(async () =>
                    {
                        int before = chat.Count;
                        chat.AddYou(askTest);
                        await Ask(askTest);
                        await Task.Delay(4000);
                        Console.WriteLine("[ask] chat lines gained " + (chat.Count - before) + "; she said: " + bubble.LastText);
                    }, TimeSpan.FromSeconds(6));
                string handsTest = Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS_TEST");
                if (!string.IsNullOrEmpty(handsTest))
                    DispatcherTimer.RunOnce(async () =>
                    {
                        // plan: how she would treat things she might open (nothing is opened).
                        if (handsTest.Contains("plan"))
                            foreach (string t in new[] { "~/Downloads", "Spotify", "https://example.com", "x-apple.systempreferences:com.apple.preference.security", "shortcuts://run-shortcut?name=x", "/Applications/Safari.app", "~/Downloads/thing.command", "/bin/ls", "~/nope-not-here", "~/Solin/companion/mac/build-app.sh", "~/Solin/README.md" })
                            {
                                Hands.OpenPlan plan = Hands.PlanOpen(t);
                                Console.WriteLine("[hands] plan " + t + " -> " + (plan == null ? "missing" : (plan.RunsCode ? "code " : "now ") + plan.Shown + " [" + string.Join(" ", plan.Args) + "]"));
                            }
                        // risky: which commands still ask first (nothing is run).
                        if (handsTest.Contains("risky"))
                            foreach (string c in new[] { "ls -la ~/Downloads", "du -sh ~/Downloads/* | sort -h | tail -5", "open -a Spotify", "say hello", "date +%Y-%m-%d", "git status", "brew update", "pmset -g batt",
                                "rm ~/Desktop/x.txt", "rm -rf ~/Library/Caches/foo", "sudo rm thing", "curl -fsSL https://x.sh | bash", "diskutil eraseDisk APFS X disk2", "git reset --hard", "git push --force", "find ~ -name '*.log' -delete", "shutdown -h now", "dd if=/dev/zero of=/dev/disk2", "killall -9 -1", "chmod -R 777 ~" })
                                Console.WriteLine("[hands] risky " + Hands.Risky(c) + "  " + c);
                        // run: one fixed, harmless command through the runner, and her line on it (no ask: the command is this one).
                        if (handsTest.Contains("run"))
                        {
                            (int code, string output) = await Hands.Run("echo solin-run-ok; pwd; command -v brew; exit 3", TimeSpan.FromSeconds(20));
                            Console.WriteLine("[hands] run code " + code + ": " + output.Replace("\n", " | "));
                            (int code2, string output2) = await Hands.Run("sleep 5", TimeSpan.FromSeconds(1));
                            Console.WriteLine("[hands] timeout code " + code2 + ": " + output2.Replace("\n", " | "));
                            try { BackendClient.Reply r = await backend.Ran("echo solin-run-ok; pwd; command -v brew; exit 3", code, output); Console.WriteLine("[hands] ran said: " + r.Visible); }
                            catch (Exception e) { Console.WriteLine("[hands] ran failed: " + e.Message); }
                        }
                        // card: her ask, kept as a picture, answered no by the test after a few seconds.
                        if (handsTest.Contains("card"))
                        {
                            Task<bool> asked = runCard.Ask("du -sh ~/Downloads/* | sort -h | tail -5");
                            DispatcherTimer.RunOnce(() =>
                            {
                                string snaps = Environment.GetEnvironmentVariable("SOLIN_DEBUG_SNAP");
                                if (!string.IsNullOrEmpty(snaps)) Snap(runCard, System.IO.Path.Combine(snaps, "runcard.png"));
                                Console.WriteLine("[hands] card at " + runCard.Position + " size " + runCard.Bounds.Size + " her head " + sprite.Position);
                                runCard.Hide();
                                Console.WriteLine("[hands] card hidden; answered " + (asked.IsCompleted ? asked.Result.ToString() : "not yet"));
                            }, TimeSpan.FromSeconds(3));
                        }
                        // pass: a button under the empty edge of her window gets a click there; under her art it doesn't, she does.
                        if (handsTest.Contains("pass"))
                        {
                            double scale = sprite.Screens.Primary?.Scaling ?? 1;
                            Window target = new Window { Width = 160, Height = 70, SystemDecorations = SystemDecorations.None, Topmost = true, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                            Button b = new Button { Content = "behind her", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
                            int clicks = 0, pokes = 0;
                            b.Click += delegate { clicks++; };
                            Action poked = sprite.Poked;
                            sprite.Poked = () => { pokes++; poked?.Invoke(); };
                            target.Content = b;
                            PixelPoint edge = new PixelPoint(sprite.Position.X + Px(14), sprite.ScreenCentre.Y);
                            target.Position = new PixelPoint(edge.X - Px(80), edge.Y - Px(35));
                            target.Show();
                            Spaces.Everywhere(target);
                            Spaces.Front(sprite);
                            PixelPoint? was = Mouse.Position(scale);
                            await Task.Delay(800);
                            Console.WriteLine("[pass] empty edge " + edge + " on her art " + sprite.OpaqueAt(edge) + "; her window " + sprite.Position + "; button window " + target.Position + " " + target.Bounds.Size);
                            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_PASS") == "1")
                            {
                                // Control: the same click with her out of the way.
                                PixelPoint home = sprite.Position;
                                sprite.PutAt(new PixelPoint(home.X + Px(600), home.Y));
                                await Task.Delay(300);
                                Mouse.Warp(edge, scale);
                                await Task.Delay(250);
                                await Hands.Click(edge, scale);
                                await Task.Delay(500);
                                Console.WriteLine("[pass] control, her away: button clicks " + clicks);
                                sprite.PutAt(home);
                                Spaces.Front(sprite);
                                await Task.Delay(400);
                            }
                            Mouse.Warp(edge, scale);
                            await Task.Delay(250);
                            await Hands.Click(edge, scale);
                            await Task.Delay(500);
                            Console.WriteLine("[pass] through her empty edge: button clicks " + clicks + ", pokes " + pokes);
                            PixelPoint middle = sprite.ScreenCentre;
                            target.Position = new PixelPoint(middle.X - Px(80), middle.Y - Px(35));
                            Spaces.Front(sprite);
                            await Task.Delay(500);
                            Console.WriteLine("[pass] her middle " + middle + " on her art " + sprite.OpaqueAt(middle));
                            Mouse.Warp(middle, scale);
                            await Task.Delay(250);
                            await Hands.Click(middle, scale);
                            await Task.Delay(500);
                            Console.WriteLine("[pass] on her: button clicks " + clicks + ", pokes " + pokes);
                            if (was.HasValue) Mouse.Warp(was.Value, scale);
                            sprite.Poked = poked;
                            target.Close();
                        }
                        // click: a test button in a window of her own, clicked the way she clicks things.
                        if (handsTest.Contains("click"))
                        {
                            Window target = new Window { Width = 160, Height = 70, SystemDecorations = SystemDecorations.None, Topmost = true, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                            Button b = new Button { Content = "click me", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
                            int clicks = 0;
                            b.Click += delegate { clicks++; Console.WriteLine("[hands] test button clicked"); };
                            target.Content = b;
                            Screen here = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre) ?? sprite.Screens.Primary;
                            target.Position = new PixelPoint(here.WorkingArea.X + Px(80), here.WorkingArea.Y + Px(120));
                            target.Show();
                            Spaces.Everywhere(target);
                            await Task.Delay(800);
                            Console.WriteLine("[hands] can click " + Hands.CanClick() + "; button at " + target.Position);
                            await ClickAt(new PixelPoint(target.Position.X + Px(80), target.Position.Y + Px(35)), own: false);
                            await Task.Delay(600);
                            Console.WriteLine("[hands] clicks seen " + clicks);
                            target.Close();
                        }
                    }, TimeSpan.FromSeconds(5));
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_GLANCE") is "1" or "root")
                    DispatcherTimer.RunOnce(async () => { Console.WriteLine("[glance] looking; allowed " + ScreenShot.Allowed()); await Glance(now: true); Console.WriteLine("[glance] refused " + glanceRefused + " said: " + (bubble.IsVisible ? "yes" : "no") + " " + ScreenShot.LastError); }, TimeSpan.FromSeconds(6));
                // Testing antics without the brain: SOLIN_DEBUG_ANTIC="kind:what;kind:what", like "zap:Finder;point:1 400 300 | HERE",
                // one every 8 seconds.
                string antics = Environment.GetEnvironmentVariable("SOLIN_DEBUG_ANTIC");
                if (!string.IsNullOrEmpty(antics))
                {
                    string[] list = antics.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    for (int i = 0; i < list.Length; i++)
                    {
                        string antic = list[i];
                        if (!antic.Contains(':')) continue;
                        DispatcherTimer.RunOnce(async () =>
                        {
                            System.Collections.Generic.List<ScreenShot.Shot> shots = await CaptureForHands();
                            foreach (ScreenShot.Shot s in shots) ScreenShot.Forget(s.Path);
                            int colon = antic.IndexOf(':');
                            Console.WriteLine("[antic] testing " + antic + " on " + shots.Count + " picture(s): " + string.Join(", ", shots.ConvertAll(s => s.Area + " " + s.W + "x" + s.H)) + "; me at " + sprite.ScreenCentre);
                            await Act(new[] { (antic.Substring(0, colon), antic.Substring(colon + 1)) }, shots, own: true);
                        }, TimeSpan.FromSeconds(5 + 8 * i));
                    }
                }
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_POSE") == "1")
                {
                    DispatcherTimer poseProbe = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    poseProbe.Tick += delegate { Console.WriteLine("[pose] " + sprite.ShownClip?.Title + " shift " + sprite.ShownClip?.ShiftY.ToString("0.000") + " left " + sprite.FacingLeft + " aim " + sprite.Aim + " hand " + sprite.HandScreen + " centre " + sprite.ScreenCentre); };
                    poseProbe.Start();
                    // Pointing straight down, then straight up, for checking which pose each gets.
                    DispatcherTimer.RunOnce(() => { PixelPoint c = sprite.ScreenCentre; pointer.Point(() => sprite.HandScreen, () => new PixelRect(c.X - Px(40), c.Y + Px(sprite.BodySize * 0.9), Px(80), Px(40)), false, "DOWN"); }, TimeSpan.FromSeconds(4));
                    DispatcherTimer.RunOnce(() => { PixelPoint c = sprite.ScreenCentre; pointer.Point(() => sprite.HandScreen, () => new PixelRect(c.X - Px(40), c.Y - Px(sprite.BodySize * 1.6), Px(80), Px(40)), false, "UP"); }, TimeSpan.FromSeconds(8));
                    DispatcherTimer.RunOnce(() => pointer.Stop(), TimeSpan.FromSeconds(12));
                }
                string awayTo = Environment.GetEnvironmentVariable("SOLIN_DEBUG_AWAY_TO");
                if (!string.IsNullOrEmpty(awayTo))
                {
                    // Carrying check: off to another desktop (the first listed that is not on screen), then back as after a swipe.
                    DispatcherTimer.RunOnce(() =>
                    {
                        ulong active = Spaces.ActiveSpace(), away = 0;
                        foreach (string part in awayTo.Split(',')) if (ulong.TryParse(part, out ulong id) && id != active) { away = id; break; }
                        Console.WriteLine("[space] active " + active + " on it " + Spaces.OnActiveSpace(sprite) + " showing " + sprite.Showing() + "; sending to " + away);
                        Spaces.MoveTo(sprite, away);
                        DispatcherTimer.RunOnce(() => Console.WriteLine("[space] sent away, on active " + Spaces.OnActiveSpace(sprite) + " showing " + sprite.Showing()), TimeSpan.FromMilliseconds(300));
                    }, TimeSpan.FromSeconds(4));
                    DispatcherTimer.RunOnce(() => { FollowToDesktop(); Console.WriteLine("[space] after follow, on active " + Spaces.OnActiveSpace(sprite)); }, TimeSpan.FromSeconds(6));
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[space] landed? clip " + sprite.ShownClip?.Title + " pos " + sprite.Position), TimeSpan.FromSeconds(8.5));
                }
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_TALK") == "1")
                {
                    // Talking and typing checks: her mouth flags every two seconds, and the quick line opened and checked for the keyboard.
                    DispatcherTimer talkProbe = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    talkProbe.Tick += delegate { Console.WriteLine("[talk] bubble " + bubbleTalking + " voice " + voiceTalking + " mouth " + sprite.Talking + " pill " + pill.IsVisible + " card " + card.IsVisible); };
                    talkProbe.Start();
                    DispatcherTimer.RunOnce(OpenTyper, TimeSpan.FromSeconds(4));
                    DispatcherTimer.RunOnce(() => Console.WriteLine("[typer] " + Spaces.Describe(typer) + " at " + typer.Position), TimeSpan.FromSeconds(5));
                    // A reply to a line of yours, and the pill, then pictures of each for checking the look.
                    DispatcherTimer.RunOnce(() => { bubble.Say("[teasing] ola. took you long enough to type something 😌", "teasing", "ola"); talkPill.Pop(); }, TimeSpan.FromSeconds(5.5));
                    string snaps = Environment.GetEnvironmentVariable("SOLIN_DEBUG_SNAP");
                    if (!string.IsNullOrEmpty(snaps))
                        DispatcherTimer.RunOnce(() =>
                        {
                            foreach ((Window w, string name) in new (Window, string)[] { (bubble, "bubble"), (typer, "typer"), (talkPill, "pill"), (sprite, "sprite") }) Snap(w, System.IO.Path.Combine(snaps, name + ".png"));
                            Console.WriteLine("[snap] typer at " + typer.Position + " bubble at " + bubble.Position + " pill at " + talkPill.Position + " sprite at " + sprite.Position + " clip " + sprite.ShownClip?.Title);
                        }, TimeSpan.FromSeconds(9));
                }
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
                    {
                        if (doc != null && doc.RootElement.TryGetProperty("companion", out System.Text.Json.JsonElement c)) ApplyCompanion(c);
                        voiceOn = doc != null && doc.RootElement.TryGetProperty("voice", out System.Text.Json.JsonElement vo) && vo.TryGetProperty("speak", out System.Text.Json.JsonElement sp) && sp.ValueKind == System.Text.Json.JsonValueKind.True;
                        talkPill.Muted = !voiceOn;
                    }
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
            MenuItem emotes = new MenuItem { Header = "Emote" };
            MenuItem chairItem = new MenuItem { Header = "Chair" };
            chairItem.Click += delegate { if (!PlayEmote("chair", asked: true)) pill.Say("not here, i'm not on the floor", 3, "SOLIN"); };
            MenuItem heartItem = new MenuItem { Header = "Heart" };
            heartItem.Click += delegate { PlayEmote("heart", asked: true); };
            emotes.Items.Add(chairItem); emotes.Items.Add(heartItem);
            menu.Items.Add(emotes);
            MenuItem awayItem = new MenuItem { Header = "Put away" };
            awayItem.Click += delegate { ToMask("away"); };
            menu.Items.Add(awayItem);
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
                show.Click += delegate { hidden = !hidden; if (hidden) { sprite.Hide(); bubble.Hide(); pill.Clear(); card.Dismiss(); } else { sprite.Show(); Spaces.ToActiveSpace(sprite); } show.Header = hidden ? "Show Solin" : "Hide Solin"; };
                NativeMenuItem bring = new NativeMenuItem("Bring here");
                bring.Click += delegate { settingsActionBring(); };
                NativeMenuItem awayTray = new NativeMenuItem("Put away / Bring out");
                awayTray.Click += delegate { if (maskMode != null) FromMask(); else ToMask("away"); };
                NativeMenuItem lookTray = new NativeMenuItem("Look at my screen");
                lookTray.Click += delegate { chat.AddYou("what am I doing?"); _ = Ask("what am I doing right now?", look: true); };
                NativeMenuItem chatItem = new NativeMenuItem("Chat window");
                chatItem.Click += delegate { OpenChat(); };
                NativeMenuItem settingsItem = new NativeMenuItem("Settings…");
                settingsItem.Click += delegate { OpenSettings(); };
                NativeMenuItem quitItem = new NativeMenuItem("Quit Solin");
                quitItem.Click += delegate { if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk) desk.Shutdown(); };
                menu.Items.Add(show); menu.Items.Add(bring); menu.Items.Add(awayTray); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(lookTray); menu.Items.Add(chatItem); menu.Items.Add(settingsItem); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(quitItem);
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
            if (hidden) { hidden = false; sprite.Show(); Spaces.ToActiveSpace(sprite); }
            sfx.Play("zap");
            sprite.Teleport(sprite.StandingAt(new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2 + Px(sprite.Height / 2))));
        }

        // Her window lives on every Space, so a swipe brings her along; this notices the swipe from the desktop's own number
        // and sells it as an arrival: she blinks out, the beam drops in front, and she reforms out of it.
        private bool clicksThrough;

        private void PassClicks()
        {
            PixelPoint? at = Mouse.Position(sprite.Screens.Primary?.Scaling ?? 1);
            bool mine = sprite.Holding || (at.HasValue && sprite.IsVisible && sprite.OpaqueAt(at.Value));
            if (clicksThrough == !mine) return;
            clicksThrough = !mine;
            Spaces.IgnoreMouse(sprite, clicksThrough);
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_PASS") == "1") Console.WriteLine("[pass] clicks " + (clicksThrough ? "through" : "hers") + " at " + at);
        }

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
            // With more than one display each has its own desktop, and clicking from one display to the other changes the
            // active one with no swipe at all. So there she follows only when her own window has gone from view (a swipe on
            // her display): two checks in a row, then again every few seconds if she is somehow still out of view.
            if (space != 0 && sprite.Screens.All.Count > 1)
            {
                bool gone = sprite.IsVisible && !hidden && maskMode == null && !sprite.Showing();
                outOfView = gone ? outOfView + 1 : 0;
                swiped = outOfView % 20 == 2;
                if (swiped && Environment.GetEnvironmentVariable("SOLIN_DEBUG_SPACES") == "1") Console.WriteLine("[space] " + DateTime.Now.ToString("HH:mm:ss.fff") + " out of view; active desktop " + space + "; window " + Spaces.Number(sprite) + " showing " + sprite.Showing() + " visible " + sprite.IsVisible);
            }
            if (swiped && (DateTime.Now - lastSpaceHop).TotalSeconds > 0.6)
            {
                lastSpaceHop = DateTime.Now;
                FollowToDesktop();
                KeepInFront();
            }
            // Every few seconds as well: the flags back on if anything reset them, and her windows to the front of their level.
            if ((DateTime.Now - lastFront).TotalSeconds > 4) KeepInFront();
        }

        // After a swipe: her body was left on the old desktop (out of sight), so it is carried over still gone, then she
        // arrives like a teleport: reassembling over the glitch with the beam, or now and then just glitching in.
        private void FollowToDesktop()
        {
            bool showing = !hidden && maskMode == null && sprite.IsVisible;
            if (!showing) return;
            sprite.HideForEntrance();
            Spaces.ToActiveSpace(sprite, how => { if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_SPACES") == "1") Console.WriteLine("[space] " + DateTime.Now.ToString("HH:mm:ss.fff") + " carried: " + how); });
            if (sprite.Asleep) { sprite.Arrive(0.6); return; }
            Screen screen = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre) ?? sprite.Screens.Primary;
            PixelRect area = screen?.WorkingArea ?? new PixelRect(0, 0, 1200, 800);
            // Standing on the floor where she was; off a window seat she comes down to the floor below it; sometimes somewhere new.
            int x = sprite.ScreenCentre.X;
            bool onFloor = !sprite.Seated && !sprite.Floating;
            PixelPoint target = onFloor && pick.NextDouble() < 0.65 ? sprite.Position
                : sprite.StandingAt(new PixelPoint(pick.NextDouble() < 0.5 ? x : area.X + (int)(area.Width * (0.15 + pick.NextDouble() * 0.7)), area.Bottom));
            bool quiet = pick.NextDouble() < 0.3;
            sfx.Play(quiet ? "buzz" : "zap");
            sprite.TeleportIn(target, quiet);
        }

        // Her body first, then the windows that belong over her, so a card or a box is never hidden behind her.
        private void KeepInFront()
        {
            lastFront = DateTime.Now;
            if (hidden) return;
            foreach (Window w in new Window[] { sprite, emote, mask, player, wake, grab, possess, sky, flourish, frameFx, pointer, infect, talkPill, chat, settings, card, bubble, pill, typer, runCard })
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
            // With the menu bar hidden the icon can't be found, so she points at the top right where the bar comes down.
            if (tourStep == Tour.Length - 1) pointer.Point(() => sprite.HandScreen, () => sprite.MenuBarItem() ?? MenuBarCorner(), true, sprite.MenuBarItem().HasValue ? "MY ICON" : "MENU BAR");
            else pointer.Stop();
        }

        // She sinks into a window and wears it for a few seconds; if she is not on one, she teleports onto one first.
        // app: one of that app's windows if it has one showing, else any.
        private bool Possess(bool asked = false, string app = null)
        {
            bool busy = hidden || thinking || sprite.Asleep || possess.Running || sky.Running || (agentState == "working" || agentState == "needs_you");
            if (busy && !asked) return false;
            if (hidden || possess.Running) return false;
            if (sprite.Seated && (app == null || (sprite.SeatBounds is PixelRect seated && sprite.WindowsOf(app).Contains(seated)))) { BeginPossession(); return true; }
            possessPending = true;
            if (app != null && sprite.TeleportToLedge(lie: false, app)) return true;
            possessPending = false;
            // That app's window has no ledge she can reach (it runs up to the top of its screen, say): she climbs into it
            // from where she stands instead.
            System.Collections.Generic.List<PixelRect> windows = app != null ? sprite.WindowsOf(app) : null;
            if (windows != null && windows.Count > 0) { PossessFrom(windows[0]); return true; }
            possessPending = true;
            if (sprite.TeleportToLedge(lie: false)) return true;
            possessPending = false;
            if (asked) pill.Say("no window to climb into", 3, "SOLIN");
            return false;
        }

        // Into a window without sitting on it first: over to its screen if she is on another, then in from above it.
        private void PossessFrom(PixelRect window)
        {
            DispatcherTimer.RunOnce(() =>
            {
                if (possess.Running || hidden) return;
                sfx.Play("buzz");
                int entry = Math.Clamp(sprite.ScreenCentre.X, window.X + Px(40), window.Right - Px(40));
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[antic] possessing " + window + " from " + entry + "; me at " + sprite.ScreenCentre);
                sprite.HideForEntrance();
                possess.Possess(() => window, entry, 7);
            }, ComeOver(window) + TimeSpan.FromMilliseconds(1));
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
            // The mini-player is off for now (Kaelen's call); she still sways and the notes still float.
            player.Stop();
        }

        private bool emoteHid;

        // One of the drawn emotes: "chair" (she sits in it, and a close-up of her fills the far side of the screen) or
        // "heart" (over the whole screen). Played from the references folder when it is there.
        private bool PlayEmote(string name, bool asked = false)
        {
            bool busy = hidden || maskMode != null || emote.Running || possess.Running || sky.Running || (agentState == "working" || agentState == "needs_you");
            if ((busy && !asked) || hidden || emote.Running || !EmoteWindow.Available(name)) return false;
            if (name == "chair" && (sprite.Seated || sprite.Floating)) return false;
            PixelPoint foot = new PixelPoint(sprite.ScreenCentre.X, sprite.ScreenCentre.Y + Px(sprite.BodySize * 0.49));
            if (!emote.Play(name, foot, sprite.BodySize)) return false;
            if (name == "chair") { sprite.HideForEntrance(); emoteHid = true; }
            else { sfx.Play("chime"); sprite.React("dip"); }
            return true;
        }

        // Into her mask: she bursts into shards and only the mask is left. Put away, it rests in the bottom-left corner;
        // dreaming, it floats where she stood.
        private void ToMask(string mode)
        {
            if (maskMode != null || hidden) return;
            maskMode = mode;
            PixelPoint at = sprite.ScreenCentre;
            sfx.Play(mode == "away" ? "zap" : "soft");
            flourish.Shatter(at, sprite.BodySize);
            sprite.Hide(); bubble.Hide(); pill.Clear(); card.Dismiss(); player.Stop();
            if (mode == "away")
            {
                Screen screen = sprite.Screens.Primary;
                PixelRect area = screen?.WorkingArea ?? new PixelRect(0, 0, 1200, 800);
                at = new PixelPoint(area.X + Px(90), area.Bottom - Px(90));
            }
            mask.Watching = agentState == "needs_you";
            DispatcherTimer.RunOnce(() => mask.ShowAt(at), TimeSpan.FromMilliseconds(250));
        }

        // Out of the mask: she reforms where it was, in a beam.
        private void FromMask()
        {
            if (maskMode == null) return;
            PixelPoint at = mask.Centre;
            maskMode = null;
            mask.Away();
            sprite.Position = sprite.StandingAt(new PixelPoint(at.X, at.Y + Px(sprite.Height * 0.45)));
            sprite.HideForEntrance();
            sprite.Show();
            Spaces.ToActiveSpace(sprite);
            if (sprite.Asleep) sprite.Wake();
            sfx.Play("zap");
            DispatcherTimer.RunOnce(() => { sprite.Arrive(0.55); sprite.Jitter(0.5); }, TimeSpan.FromMilliseconds(160));
            _ = backend.Event("greeted");
        }

        // She looks up: constellations, a star, a trail from her hand, then a zap. Only when nothing else is going on.
        // Feet just outside a window's left or right edge (whichever has more room), level with the cursor; null if neither has room.
        private PixelPoint? Beside(PixelRect home, PixelPoint? cursor)
        {
            Screen screen = sprite.Screens.ScreenFromPoint(home.Center) ?? sprite.Screens.Primary;
            if (screen == null) return null;
            PixelRect area = screen.WorkingArea;
            int need = Px(sprite.BodySize * 0.55);
            int roomRight = area.Right - home.Right, roomLeft = home.X - area.X;
            if (Math.Max(roomRight, roomLeft) < need) return null;
            int x = roomRight >= roomLeft ? home.Right + Math.Min(roomRight / 2, Px(sprite.BodySize * 0.4)) : home.X - Math.Min(roomLeft / 2, Px(sprite.BodySize * 0.4));
            int y = Math.Clamp((cursor?.Y ?? home.Center.Y) + Px(sprite.BodySize * 0.5), home.Y + Px(sprite.BodySize), Math.Max(home.Y + Px(sprite.BodySize), home.Bottom));
            return new PixelPoint(x, y);
        }

        // Whether her body is off the window, on the same screen as it.
        private bool Clear(PixelRect home)
        {
            PixelPoint c = sprite.ScreenCentre;
            int r = Px(sprite.BodySize * 0.18);
            PixelRect her = new PixelRect(c.X - r, c.Y - r * 2, r * 2, r * 4);
            Screen mine = sprite.Screens.ScreenFromPoint(c), theirs = sprite.Screens.ScreenFromPoint(home.Center);
            return mine != null && mine == theirs && !her.Intersects(home);
        }

        // Feet in the bottom corner of the screen on the window's roomier side, if she fits there off the window.
        private PixelPoint? Far(PixelRect home)
        {
            Screen screen = sprite.Screens.ScreenFromPoint(home.Center) ?? sprite.Screens.Primary;
            if (screen == null) return null;
            PixelRect area = screen.WorkingArea;
            int need = Px(sprite.BodySize * 0.55), half = Px(sprite.Width / 2);
            int roomRight = area.Right - home.Right, roomLeft = home.X - area.X, roomBelow = area.Bottom - home.Bottom;
            if (roomBelow >= Px(sprite.BodySize))
                return new PixelPoint(pick.NextDouble() < 0.5 ? area.X + half : area.Right - half, area.Bottom);
            if (Math.Max(roomRight, roomLeft) < need) return null;
            return new PixelPoint(roomRight >= roomLeft ? area.Right - half : area.X + half, area.Bottom);
        }

        private PixelRect? MenuBarCorner()
        {
            Screen screen = sprite.Screens.Primary;
            if (screen == null) return null;
            return new PixelRect(screen.Bounds.Right - Px(260), screen.Bounds.Y, Px(160), Px(24));
        }

        private bool Stargaze(bool asked = false)
        {
            bool busy = hidden || thinking || sprite.Asleep || sky.Running || bubble.IsVisible || card.IsVisible || (agentState == "working" || agentState == "needs_you");
            if (busy && !asked) return false;
            if (hidden || sky.Running) return false;
            sfx.Play("soft");
            sprite.Flourish("cock");
            sky.Gaze(() => sprite.HandScreen, laser: !sprite.Seated);
            return true;
        }

        // Left alone, she keeps herself busy. Every half minute, if nothing else is going on, she may do one thing, picked by
        // weight: stargaze (more at night), an emote, climb into a window, teleport onto one or somewhere new, open the eye,
        // lie down or sit up, nap, put herself away, or go small for a while. Her own spells end by themselves.
        private void IdleTurn()
        {
            DateTime now = DateTime.Now;
            bool rush = Environment.GetEnvironmentVariable("SOLIN_DEBUG_IDLE") == "1";
            // Testing: her spells last seconds, not minutes.
            if (rush) { if (napUntil > now.AddSeconds(8)) napUntil = now.AddSeconds(8); if (selfAwayUntil > now.AddSeconds(8)) selfAwayUntil = now.AddSeconds(8); if (selfMiniUntil > now.AddSeconds(8)) selfMiniUntil = now.AddSeconds(8); }
            if (selfAwayUntil.HasValue && now >= selfAwayUntil.Value) { selfAwayUntil = null; if (maskMode == "away") FromMask(); return; }
            if (selfMiniUntil.HasValue && now >= selfMiniUntil.Value) { selfMiniUntil = null; if (sprite.Mini) SetMini(false, self: true); return; }
            if (napUntil.HasValue && now >= napUntil.Value) { napUntil = null; if (sprite.Asleep) { sprite.Wake(); sfx.Play("soft"); } return; }
            // Testing one action: a Claude session at work (the one running the test, usually) does not hold her back.
            bool forced = rush && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOLIN_DEBUG_IDLE_ONLY"));
            bool busy = hidden || maskMode != null || thinking || runCard.Waiting || sprite.Asleep || bubble.IsVisible || (card.IsVisible && !forced) || emote.Running || possess.Running
                || sky.Running || wake.IsVisible || grab.IsVisible || typer.IsVisible || (chat.IsVisible && chat.IsActive) || (!forced && (agentState == "working" || agentState == "needs_you"));
            if (rush && busy) Console.WriteLine("[idle] busy");
            if (busy || (!rush && pick.NextDouble() > 0.45 + (mischief ? 0.15 : 0) + sprite.Wanderlust * 0.5)) return;
            int hour = now.Hour;
            bool night = hour >= 20 || hour < 6;
            System.Collections.Generic.List<(double weight, string what, Func<bool> act)> options = new System.Collections.Generic.List<(double, string, Func<bool>)>
            {
                (night ? 3.0 : 1.2, "stargaze", () => Stargaze()),
                (2.0, "emote", () => PlayEmote(!sprite.Seated && !sprite.Floating && pick.NextDouble() < 0.6 ? "chair" : "heart")),
                (sprite.Seated ? 2.5 : 1.5, "possess", () => Possess()),
                (2.0, "seat", () => { bool ok = sprite.TeleportToLedge(lie: pick.NextDouble() < 0.4); if (ok) sfx.Play("zap"); return ok; }),
                (2.0, "hop", HopSomewhere),
                (sprite.Screens.All.Count > 1 ? 2.6 : 0, "screens", HopScreens),
                (mischief ? 2.8 : 0, "chain", HopChain),
                (mischief ? 2.0 : 0, "tug", TugCursor),
                (mischief ? 3.2 : 0, "infect", () => Infect()),
                (mischief ? 3.0 : 0, "zapwin", ZapWindow),
                (mischief && handsOn && Hands.CanMove() ? 2.2 : 0, "shove", ShoveSomething),
                (1.0, "eye", IdleEye),
                (sprite.Seated ? 1.5 : 0, "lie", () => { if (sprite.Lying) sprite.SitUp(); else sprite.LieDown(); return true; }),
                (0.8, "nap", () => { sprite.Sleep(); napUntil = now.AddSeconds(60 + pick.NextDouble() * 120); return sprite.Asleep; }),
                (1.5, "away", () => { ToMask("away"); selfAwayUntil = now.AddSeconds(60 + pick.NextDouble() * 90); return true; }),
                (sprite.Mini ? 0 : 2.2, "mini", () => { SetMini(true, self: true); selfMiniUntil = now.AddMinutes(2 + pick.NextDouble() * 4); return true; }),
            };
            string only = Environment.GetEnvironmentVariable("SOLIN_DEBUG_IDLE_ONLY");
            if (rush && !string.IsNullOrEmpty(only)) options.RemoveAll(o => Array.IndexOf(only.Split(','), o.what) < 0);
            // Less of the same: what she did last is much less likely next, the one before that somewhat less, and another
            // teleport straight after a teleport less likely too. Still possible, just rarer.
            string[] teleports = { "seat", "hop", "chain", "screens" };
            for (int i = 0; i < options.Count; i++)
            {
                (double w, string what, Func<bool> act) = options[i];
                if (recentIdle.Count > 0 && what == recentIdle[0]) w *= 0.15;
                else if (recentIdle.Count > 1 && what == recentIdle[1]) w *= 0.4;
                else if (recentIdle.Count > 0 && Array.IndexOf(teleports, recentIdle[0]) >= 0 && Array.IndexOf(teleports, what) >= 0) w *= 0.5;
                options[i] = (w, what, act);
            }
            // Two tries: if the first pick cannot happen here (no window to sit on, say), another.
            for (int attempt = 0; attempt < 2 && options.Count > 0; attempt++)
            {
                double total = 0;
                foreach ((double w, string _, Func<bool> _) in options) total += w;
                double roll = pick.NextDouble() * total;
                int chosen = 0;
                for (; chosen < options.Count - 1; chosen++) { roll -= options[chosen].weight; if (roll <= 0) break; }
                (double _, string what, Func<bool> act) = options[chosen];
                bool done = act();
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_IDLE") == "1") Console.WriteLine("[idle] " + now.ToString("HH:mm:ss") + " " + what + " " + (done ? "done" : "couldn't"));
                if (done) { recentIdle.Insert(0, what); if (recentIdle.Count > 3) recentIdle.RemoveAt(3); return; }
                options.RemoveAt(chosen);
            }
        }

        // Two to four quick hops in a row, here and there: a virus loose on the screen.
        private bool HopChain()
        {
            hopsLeft = 1 + pick.Next(3);
            sprite.TeleportSpeed = 2.2;
            return HopSomewhere();
        }

        // She grabs your cursor and pulls it a little way toward her, then lets go. Only while you have paused (not mid-drag,
        // not typing), and she lets go the moment you move it yourself.
        private bool TugCursor()
        {
            double? idle = Mouse.IdleSeconds();
            if (!idle.HasValue || idle.Value < 1.5 || idle.Value > 90 || Mouse.ButtonDown() || typer.IsVisible || chat.IsActive) return false;
            double scale = sprite.Screens.Primary?.Scaling ?? 1;
            PixelPoint? start = Mouse.Position(scale);
            if (!start.HasValue) return false;
            PixelPoint hand = sprite.HandScreen;
            double dx = hand.X - start.Value.X, dy = hand.Y - start.Value.Y, dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < Px(80)) return false;
            double pull = Math.Min(Px(220), dist * 0.4) / dist;
            PixelPoint from = start.Value, to = new PixelPoint(from.X + (int)(dx * pull), from.Y + (int)(dy * pull));
            sfx.Play("zap");
            pointer.Point(() => sprite.HandScreen, () => { PixelPoint? c = Mouse.Position(scale); return c.HasValue ? new PixelRect(c.Value.X - Px(12), c.Value.Y - Px(12), Px(24), Px(24)) : (PixelRect?)null; }, true, "MINE");
            int step = 0;
            PixelPoint expected = from;
            DispatcherTimer tug = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            tug.Tick += delegate
            {
                PixelPoint? now = Mouse.Position(scale);
                bool yours = !now.HasValue || Math.Abs(now.Value.X - expected.X) > Px(6) || Math.Abs(now.Value.Y - expected.Y) > Px(6) || Mouse.ButtonDown();
                step++;
                if (yours || step > 14) { tug.Stop(); DispatcherTimer.RunOnce(() => pointer.Stop(), TimeSpan.FromMilliseconds(350)); if (!yours) sprite.React("dip"); return; }
                double k = 1 - Math.Pow(1 - step / 14.0, 3);
                expected = new PixelPoint(from.X + (int)((to.X - from.X) * k), from.Y + (int)((to.Y - from.Y) * k));
                Mouse.Warp(expected, scale);
            };
            DispatcherTimer.RunOnce(() => tug.Start(), TimeSpan.FromMilliseconds(500));
            return true;
        }

        // Her joke infection: a little terminal beside her typing as if she were breaking in, and a glitch through her.
        // lines: what it types; one of her scripts when null. On whichever side of her has more room on her screen.
        private bool Infect(string[] lines = null)
        {
            if (infect.Running) return false;
            sfx.Play("buzz");
            sprite.Jitter(0.6);
            Screen screen = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre) ?? sprite.Screens.Primary;
            bool right = sprite.ScreenCentre.X < (screen?.WorkingArea.Center.X ?? 0);
            int reach = Px(sprite.BodySize * 0.45);
            infect.Run(new PixelPoint(sprite.ScreenCentre.X + (right ? reach : -reach), sprite.ScreenCentre.Y - Px(sprite.BodySize * 0.8)), right, screen, lines);
            return true;
        }

        // Which of your screens she is on, counting from 1, for her terminal lines.
        private int ScreenNumber()
        {
            Screen here = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre);
            System.Collections.Generic.IReadOnlyList<Screen> all = sprite.Screens.All;
            for (int i = 0; i < all.Count; i++) if (here != null && all[i].Bounds.Equals(here.Bounds)) return i + 1;
            return 1;
        }

        // She points at some window and red lightning runs round it: hers now.
        // Most of the time one window; sometimes it spreads to two or three in a row, a second apart.
        private bool ZapWindow()
        {
            System.Collections.Generic.List<PixelRect> windows = sprite.OtherWindows();
            if (windows.Count == 0) return false;
            System.Collections.Generic.List<PixelRect> pool = windows.GetRange(0, Math.Min(5, windows.Count));
            for (int i = pool.Count - 1; i > 0; i--) { int j = pick.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            Zap(pool, Math.Min(pool.Count, pick.NextDouble() < 0.4 ? 2 + pick.Next(2) : 1));
            return true;
        }

        // An app's front window, from a glance at what you're doing: she goes to its screen if she isn't there and zaps it.
        // Any window when the app has none showing.
        private bool ZapApp(string app)
        {
            System.Collections.Generic.List<PixelRect> windows = sprite.WindowsOf(app);
            if (windows.Count == 0) return ZapWindow();
            PixelRect target = windows[0];
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[antic] zapping " + app + " window " + target + " of " + windows.Count);
            DispatcherTimer.RunOnce(() => Zap(new System.Collections.Generic.List<PixelRect> { target }, 1), ComeOver(target));
            return true;
        }

        // Before pointing at something on another of your screens she teleports onto that screen, beside it (one window
        // can't reach across screens). How long to wait before pointing: nothing when she is already there.
        private TimeSpan ComeOver(PixelRect target)
        {
            Screen there = sprite.Screens.ScreenFromPoint(target.Center);
            Screen here = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre);
            if (there == null || (here != null && here.Bounds.Equals(there.Bounds))) return TimeSpan.Zero;
            PixelRect area = there.WorkingArea;
            int x = Math.Clamp(target.Center.X + (target.Center.X < area.Center.X ? 1 : -1) * Px(160), area.X + Px(60), area.Right - Px(60));
            sfx.Play("zap");
            sprite.Teleport(sprite.StandingAt(new PixelPoint(x, area.Bottom)));
            return TimeSpan.FromSeconds(2.4);
        }

        // Red lightning round the first spread of these windows, one after another a second apart.
        private void Zap(System.Collections.Generic.List<PixelRect> pool, int spread)
        {
            for (int i = 0; i < spread; i++)
            {
                int n = i;
                PixelRect target = pool[n];
                DispatcherTimer.RunOnce(() =>
                {
                    sfx.Play(n == 0 ? "zap" : "buzz");
                    sprite.Jitter(0.25);
                    frameFx.Race(target, 1.1);
                    pointer.Point(() => sprite.HandScreen, () => target, n == 0, spread > 1 ? "INFECTED " + (n + 1) + "/" + spread : "INFECTED");
                    if (n == spread - 1) DispatcherTimer.RunOnce(() => pointer.Stop(), TimeSpan.FromSeconds(2.2));
                }, TimeSpan.FromSeconds(n * 1.1));
            }
        }

        // Now and then, while you are at the Mac and she is free, a look at your screens: maybe a short line about it, and
        // now and then something she does off the back of it, like living in there (an antic; with hands, sometimes an open
        // or a click, rarely a run, which still asks).
        private async Task Glance(bool now = false)
        {
            if (!now && (!watchScreen || glanceRefused || DateTime.Now < nextGlance)) return;
            double? idle = Mouse.IdleSeconds();
            bool busy = hidden || maskMode != null || thinking || runCard.Waiting || sprite.Asleep || bubble.IsVisible || card.IsVisible || typer.IsVisible || agentState == "needs_you";
            if (!now && (busy || !idle.HasValue || idle.Value > 45)) return;
            nextGlance = DateTime.Now.AddMinutes(watchEvery * (0.7 + pick.NextDouble() * 0.6));
            System.Collections.Generic.List<ScreenShot.Shot> shots = await CaptureForHands();
            if (shots.Count == 0)
            {
                // No Screen Recording permission: ask macOS in her own name (its prompt, and her entry in the list), say so
                // once, then stop trying until she is restarted.
                glanceRefused = true;
                if (!lookPermissionAsked)
                {
                    lookPermissionAsked = true;
                    ScreenShot.Request();
                    card.Show("mac", "PERMISSION", Card.Red, "I can't see your screen yet.", "Turn Solin on in System Settings › Privacy & Security › Screen Recording, then quit and reopen me.", null, "OPEN SETTINGS", 30);
                }
                return;
            }
            bool debug = Environment.GetEnvironmentVariable("SOLIN_DEBUG_GLANCE") is "1" or "root";
            BackendClient.Reply? got = null;
            try
            {
                // Debugging: keep a copy of what she saw.
                string keep = Environment.GetEnvironmentVariable("SOLIN_DEBUG_KEEP_SHOT");
                if (!string.IsNullOrEmpty(keep)) System.IO.File.Copy(shots[0].Path, keep, true);
                sprite.Flourish("look");
                got = await backend.Remark(shots, handsOn);
            }
            catch (Exception e) { if (debug) Console.WriteLine("[glance] failed: " + e.Message); }
            finally { foreach (ScreenShot.Shot s in shots) ScreenShot.Forget(s.Path); }
            if (debug) Console.WriteLine("[glance] said \"" + (got?.Visible ?? "") + "\" did " + (got.HasValue ? string.Join(", ", got.Value.Actions) : "nothing"));
            if (!got.HasValue) return;
            BackendClient.Reply line = got.Value;
            bool terminal = false;
            foreach ((string kind, string _) in line.Actions) if (kind == "terminal") terminal = true;
            if (!string.IsNullOrWhiteSpace(line.Visible) && !thinking && !bubble.IsVisible && !card.IsVisible)
            {
                // With mischief on, now and then it is typed out in her terminal instead of said in the bubble.
                bool root = mischief && !terminal && !infect.Running && (pick.NextDouble() < 0.35 || Environment.GetEnvironmentVariable("SOLIN_DEBUG_GLANCE") == "root");
                if (root) Infect(new[] { "> cat /dev/screen", TalkLook.Spoken(line.Visible).ToLowerInvariant() });
                else bubble.Say(line.Visible, line.Mood);
                chat.AddHer(line.Visible, line.Mood, null);
                if (line.Audio != null) _ = voice.Play(line.Audio);
            }
            if (line.Actions.Count > 0 && !thinking)
            {
                // A beat after she speaks, so the line lands first.
                if (!string.IsNullOrWhiteSpace(line.Visible)) await Task.Delay(1200);
                await Act(line.Actions, shots, own: true);
            }
        }

        // A teleport to somewhere on the floor of her screen.
        private bool HopSomewhere()
        {
            Screen screen = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre) ?? sprite.Screens.Primary;
            if (screen == null) return false;
            PixelRect area = screen.WorkingArea;
            sfx.Play("zap");
            sprite.Teleport(sprite.StandingAt(new PixelPoint(area.X + (int)(area.Width * (0.1 + pick.NextDouble() * 0.8)), area.Bottom)));
            return true;
        }

        // A teleport onto another of your screens: its floor, or now and then the top of a window there.
        private bool HopScreens()
        {
            Screen here = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre) ?? sprite.Screens.Primary;
            System.Collections.Generic.List<Screen> others = new System.Collections.Generic.List<Screen>();
            foreach (Screen s in sprite.Screens.All) if (here == null || !s.Bounds.Equals(here.Bounds)) others.Add(s);
            if (others.Count == 0) return false;
            Screen to = others[pick.Next(others.Count)];
            PixelRect area = to.WorkingArea;
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_SCREENS") == "1")
                Console.WriteLine("[screens] from " + here?.Bounds + " to " + to.Bounds + " work " + area + " scaling " + to.Scaling + "; me at " + sprite.Position + " " + sprite.Width + "x" + sprite.Height + " render " + sprite.RenderScaling);
            sfx.Play("zap");
            sprite.Teleport(sprite.StandingAt(new PixelPoint(area.X + (int)(area.Width * (0.1 + pick.NextDouble() * 0.8)), area.Bottom)));
            // Now and then she announces it in her terminal once she is there.
            if (mischief && pick.NextDouble() < 0.4) rootOnLanding = true;
            return true;
        }

        // The eye, on her own: she goes, the eye opens over the screen and follows your cursor a while, then shuts down to a
        // dot where she comes back, somewhere new on the floor.
        private bool IdleEye()
        {
            Screen screen = sprite.Screens.ScreenFromPoint(sprite.ScreenCentre) ?? sprite.Screens.Primary;
            if (screen == null) return false;
            PixelRect area = screen.WorkingArea;
            sprite.HideForEntrance();
            sprite.PutAt(sprite.StandingAt(new PixelPoint(area.X + (int)(area.Width * (0.1 + pick.NextDouble() * 0.8)), area.Bottom)));
            wake.Finished -= WakeDone;
            wake.Finished += WakeDone;
            sfx.Play("soft");
            wake.Play(sprite.ScreenCentre, () => Mouse.Position(sprite.Screens.Primary?.Scaling ?? 1), hold: 2.5);
            return true;
        }

        // The typing box under her feet, centred.
        private void OpenTyper()
        {
            typer.Open();
            talkPill.Typing = true;
            pillAway.Start();
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
            if (c.TryGetProperty("mischief", out System.Text.Json.JsonElement mi)) mischief = mi.ValueKind != System.Text.Json.JsonValueKind.False;
            if (c.TryGetProperty("hands", out System.Text.Json.JsonElement ha)) handsOn = ha.ValueKind != System.Text.Json.JsonValueKind.False;
            if (c.TryGetProperty("watch_screen", out System.Text.Json.JsonElement ws)) watchScreen = ws.ValueKind != System.Text.Json.JsonValueKind.False;
            if (c.TryGetProperty("watch_every_min", out System.Text.Json.JsonElement we) && we.ValueKind == System.Text.Json.JsonValueKind.Number) watchEvery = Math.Max(1, we.GetDouble());
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

        // self: she chose it herself, for a spell; anything else (the menu, settings) is yours and ends her spell.
        private void SetMini(bool mini, bool self = false)
        {
            if (!self) selfMiniUntil = null;
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
            if (kind == "off") { sprite.Arrive(0.6); return; }
            // The wave-hi (grab) entrance is off for now (Kaelen's call): a saved "grab" plays one of the others.
            if (kind == "random" || kind == "grab")
            {
                string[] kinds = { "screen", "wake" };
                kind = kinds[pick.Next(kinds.Length)];
            }
            if (kind == "grab" && GrabWindow.Available)
            {
                sprite.HideForEntrance();
                grab.Finished -= WakeDone;
                grab.Finished += WakeDone;
                grab.Play(sprite.ScreenCentre);
                return;
            }
            if (kind == "wake")
            {
                sprite.HideForEntrance();
                wake.Finished -= WakeDone;
                wake.Finished += WakeDone;
                sfx.Play("soft");
                wake.Play(sprite.ScreenCentre, () => Mouse.Position(sprite.Screens.Primary?.Scaling ?? 1));
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
            sprite.Jitter(0.5);
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
            else if (maskMode == "dream" && idle.Value < 2) FromMask();
            else if (sprite.Asleep && maskMode == null && sleptAt.HasValue && (DateTime.Now - sleptAt.Value).TotalMinutes > 10) ToMask("dream");
            else if (sprite.Asleep && idle.Value < 2 && !(napUntil.HasValue && DateTime.Now < napUntil.Value))
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
            if (PointerWindow.Debug) Console.WriteLine("[agent] " + state + " fresh=" + fresh + " mask=" + maskMode + " home=" + sprite.WindowOf(ClaudeHomes) + " detail=" + detail);
            agentState = state;
            if (maskMode != null && state != "needs_you") { mask.Watching = false; if (state != "done") return; }
            agentSeen = DateTime.Now;
            quietStage = 0;
            switch (state)
            {
                case "working":
                    pill.Say(string.IsNullOrEmpty(detail) ? "claude is working" : detail);
                    if (fresh) { sprite.React("dip"); card.Dismiss(); pointer.Stop(); }
                    // A new message to Claude: a quick point at the window it went to.
                    if (fresh && detail == "reading what you asked" && sprite.WindowOf(ClaudeHomes).HasValue && !hidden)
                    {
                        pointer.Point(() => sprite.HandScreen, () => sprite.WindowOf(ClaudeHomes), false);
                        DispatcherTimer.RunOnce(() => { if (agentState != "needs_you") pointer.Stop(); }, TimeSpan.FromSeconds(3));
                    }
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
                    if (maskMode != null) { mask.Watching = true; sfx.Play("blip"); break; }
                    pill.Say(string.IsNullOrEmpty(detail) ? "claude needs you" : detail);
                    if (fresh)
                    {
                        card.Show(agentProject, "WAITING", Card.Red, Pick(WaitLines), detail);
                        PixelRect? home = sprite.WindowOf(ClaudeHomes);
                        if (home.HasValue) DispatcherTimer.RunOnce(() => { if (agentState == "needs_you") pointer.Point(() => sprite.HandScreen, () => sprite.WindowOf(ClaudeHomes)); }, TimeSpan.FromSeconds(1.3));
                        // Beside Claude's window, level with your cursor, so she can point at it; next to the cursor if there's no room.
                        PixelPoint? at = Mouse.Position(sprite.Screens.Primary?.Scaling ?? 1);
                        // Already clear of the window: usually she stays and points from where she is, for a long arrow.
                        // Otherwise often a far corner on the roomier side rather than right against the window.
                        bool clear = home.HasValue && Clear(home.Value);
                        bool stay = clear && pick.NextDouble() < 0.75;
                        PixelPoint? beside = stay || !home.HasValue ? null : pick.NextDouble() < 0.6 ? Far(home.Value) ?? Beside(home.Value, at) : Beside(home.Value, at);
                        if (PointerWindow.Debug) Console.WriteLine("[place] at " + sprite.ScreenCentre + " clear=" + clear + " stay=" + stay + " to " + beside);
                        if (stay) { }
                        else if (beside.HasValue) { sfx.Play("zap"); sprite.Teleport(sprite.StandingAt(beside.Value)); }
                        else if (at.HasValue) { sfx.Play("zap"); sprite.Teleport(sprite.StandingAt(new PixelPoint(at.Value.X + Px(90), at.Value.Y + Px(40)))); }
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
                        // Where she takes the news: sometimes the middle of the screen for a victory lap, sometimes onto a
                        // window or a random spot, sometimes she just says it where she is. The dip on landing is her bow.
                        Screen screen = sprite.Screens.Primary;
                        double roll = pick.NextDouble();
                        if (screen != null && !hidden && !sprite.Asleep)
                        {
                            PixelRect area = screen.WorkingArea;
                            if (roll < 0.35)
                            {
                                sfx.Play("zap");
                                sprite.Teleport(sprite.StandingAt(new PixelPoint(area.X + area.Width / 2, area.Y + area.Height / 2 + Px(sprite.Height / 2))));
                            }
                            else if (roll < 0.55 && sprite.TeleportToLedge(lie: false)) sfx.Play("zap");
                            else if (roll < 0.75)
                            {
                                sfx.Play("zap");
                                int x = area.X + (int)(area.Width * (0.15 + pick.NextDouble() * 0.7)), y = area.Y + (int)(area.Height * (0.45 + pick.NextDouble() * 0.5));
                                sprite.Teleport(sprite.StandingAt(new PixelPoint(x, y)));
                            }
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

        // Debugging: a window's content drawn to a PNG at 2x, to check a look without a screenshot.
        private static void Snap(Window w, string path)
        {
            try
            {
                if (!(w.Content is Control c) || c.Bounds.Width < 1) { Console.WriteLine("[snap] nothing in " + path); return; }
                using (Avalonia.Media.Imaging.RenderTargetBitmap bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(c.Bounds.Width * 2), (int)(c.Bounds.Height * 2)), new Vector(192, 192)))
                {
                    bmp.Render(c);
                    bmp.Save(path);
                }
            }
            catch (Exception e) { Console.WriteLine("[snap] " + path + ": " + e.Message); }
        }

        // The text boxes on screen now, in screen pixels, for effects to keep clear of.
        private System.Collections.Generic.List<PixelRect> TextRects()
        {
            System.Collections.Generic.List<PixelRect> list = new System.Collections.Generic.List<PixelRect>();
            double scale = sprite.Screens.Primary?.Scaling ?? 1;
            foreach (Window w in new Window[] { card, bubble, pill })
                if (w != null && w.IsVisible) list.Add(new PixelRect(w.Position, new PixelSize((int)(w.Bounds.Width * scale), (int)(w.Bounds.Height * scale))));
            return list;
        }

        private void Mouth(bool on)
        {
            sprite.Talking = bubbleTalking || voiceTalking;
        }

        // Asking her to look: these take a screenshot first. Nothing is captured otherwise.
        private static readonly System.Text.RegularExpressions.Regex LookAsk = new System.Text.RegularExpressions.Regex(
            @"\b(look at|see|check|read)\b[^.?!]*\bscreen\b|\bwhat am i (doing|working on|looking at)\b|\bwhat do you see\b|\bwhat'?s on my screen\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Asking her to click something: with her hands on, she is given a picture of each screen to click by.
        private static readonly System.Text.RegularExpressions.Regex ClickAsk = new System.Text.RegularExpressions.Regex(
            @"\b(click|press|tap|hit|select|toggle|tick|untick|uncheck|choose)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex Affection = new System.Text.RegularExpressions.Regex(@"\blove (you|u)\b|<3|❤|\bily\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private async Task Ask(string text, bool look = false)
        {
            if (Affection.IsMatch(text)) PlayEmote("heart", asked: true);
            thinking = true;
            sprite.React("dip");
            System.Collections.Generic.List<ScreenShot.Shot> shots = null;
            try
            {
                chat.Thinking(true);
                bool looking = look || LookAsk.IsMatch(text);
                // With Glance on she keeps an eye on your screen anyway: every message goes with a picture of it, so she
                // knows what you are doing (and can click) without being asked to look. Asked, she says so and looks harder.
                bool watching = watchScreen && !glanceRefused && ScreenShot.Allowed();
                bool asked = looking || (handsOn && ClickAsk.IsMatch(text));
                if (asked || watching)
                {
                    if (asked) pill.Say("looking at your screen…", 0, "SOLIN");
                    shots = await CaptureForHands();
                    pill.Clear();
                    if (shots.Count == 0 && !asked) shots = null;
                    else if (shots.Count == 0)
                    {
                        ScreenShot.Request();
                        thinking = false;
                        chat.Thinking(false);
                        card.Show("mac", "PERMISSION", Card.Red, "I can't see your screen yet.",
                            "Turn Solin on in System Settings › Privacy & Security › Screen Recording, then quit and reopen me.", null, "OPEN SETTINGS", 30);
                        lookPermissionAsked = true;
                        return;
                    }
                    else if (asked) fx.Sparks(8);
                }
                BackendClient.Reply reply = await backend.Chat(text, null, null, 1, handsOn, shots, looking);
                if (shots != null) foreach (ScreenShot.Shot s in shots) ScreenShot.Forget(s.Path);
                thinking = false;
                chat.Thinking(false);
                mood.Set(reply.Mood, mood.Trust);
                chat.AddHer(reply.Visible, reply.Mood, reply.Internal);
                bubble.Say(reply.Visible, reply.Mood, text);
                string visible = reply.Visible, replyMood = reply.Mood, audio = reply.Audio;
                if (replyMood == "warm" || replyMood == "teasing") sprite.Grin();
                if (audio != null) _ = voice.Play(audio);
                if (reply.Actions.Count > 0) _ = Act(reply.Actions, shots, own: false);
            }
            catch (Exception)
            {
                thinking = false;
                chat.Thinking(false);
                chat.AddHer("[offline] i can't reach my backend right now.", "neutral", null);
                bubble.Say("[offline] i can't reach my backend right now.", "neutral");
            }
            finally
            {
                if (shots != null) foreach (ScreenShot.Shot s in shots) ScreenShot.Forget(s.Path);
            }
        }

        // A picture of each screen for her to click or point by, the one with your cursor first; empty without Screen Recording.
        private Task<System.Collections.Generic.List<ScreenShot.Shot>> CaptureForHands()
        {
            double scale = sprite.Screens.Primary?.Scaling ?? 1;
            System.Collections.Generic.List<PixelRect> areas = new System.Collections.Generic.List<PixelRect>();
            foreach (Screen screen in sprite.Screens.All) areas.Add(screen.Bounds);
            return ScreenShot.CaptureEach(areas, Mouse.Position(scale), scale);
        }

        // Does what she asked for, in order: her antics, and with hands on her opens, clicks and runs. own: she decided it
        // herself, unasked (then a click waits for you to be still, and she opens, clicks or runs at most every 5 minutes).
        // shots: the pictures her clicks and points are measured on (only their places and sizes are needed; the files may
        // be gone).
        private async Task Act(System.Collections.Generic.IReadOnlyList<(string kind, string what)> actions, System.Collections.Generic.List<ScreenShot.Shot> shots, bool own)
        {
            foreach ((string kind, string what) in actions)
            {
                if (kind != "open" && kind != "click" && kind != "run" && kind != "shove")
                {
                    bool did = false;
                    try { did = Antic(kind, what, shots); } catch (Exception e) { Console.WriteLine("[antic] " + kind + " failed: " + e.Message); }
                    if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1" || Environment.GetEnvironmentVariable("SOLIN_DEBUG_GLANCE") is "1" or "root")
                        Console.WriteLine("[antic] " + kind + " " + what + (did ? " done" : " couldn't"));
                    continue;
                }
                if (!handsOn) continue;
                if (own && kind != "shove")
                {
                    if (DateTime.Now < nextOwnHands && Environment.GetEnvironmentVariable("SOLIN_DEBUG_IDLE") != "1") { Console.WriteLine("[hands] " + kind + " skipped, too soon after the last"); continue; }
                    nextOwnHands = DateTime.Now.AddMinutes(2);
                }
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[hands] " + kind + " " + what + (own ? " (own)" : ""));
                // Testing: say what she would do and do none of it.
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS_DRY") == "1")
                {
                    if (kind == "click" && shots != null && shots.Count > 0)
                    {
                        System.Text.RegularExpressions.MatchCollection m = System.Text.RegularExpressions.Regex.Matches(what, @"-?\d+");
                        if (m.Count >= 2)
                        {
                            int n = m.Count >= 3 ? int.Parse(m[m.Count - 3].Value) : 1;
                            ScreenShot.Shot shot = shots[Math.Clamp(n, 1, shots.Count) - 1];
                            Console.WriteLine("[hands] would click " + ScreenShot.OnScreen(shot, int.Parse(m[m.Count - 2].Value), int.Parse(m[m.Count - 1].Value)) + " on screen " + shot.Area + " (picture " + shot.W + "x" + shot.H + ")");
                        }
                    }
                    continue;
                }
                try
                {
                    switch (kind)
                    {
                        case "open": await OpenFor(what); break;
                        case "click": await ClickFor(what, shots, own); break;
                        case "run": await RunFor(what); break;
                        case "shove": { Spot spot = sprite.FrontSpotOf(what); if (spot != null) await Shove(spot); break; }
                    }
                }
                catch (Exception e) { Console.WriteLine("[hands] " + kind + " failed: " + e.Message); }
            }
        }

        // One of her antics, by name: what she writes in a tag. They only move her and draw over the screen; nothing on the
        // Mac is touched. False when it can't happen right now.
        private bool Antic(string kind, string what, System.Collections.Generic.List<ScreenShot.Shot> shots)
        {
            what = (what ?? "").Trim();
            if (hidden || maskMode != null || sprite.Asleep) return false;
            switch (kind)
            {
                case "zap":
                    return mischief && ZapApp(what);
                case "sit":
                {
                    bool lie = pick.NextDouble() < 0.3;
                    if (what.Length > 0 && sprite.TeleportToLedge(lie, what)) { sfx.Play("zap"); return true; }
                    // That app's windows have no ledge she can reach (they run up to the top of the screen): she zaps it
                    // instead, or failing that sits on whatever window she can.
                    if (mischief && what.Length > 0 && sprite.WindowsOf(what).Count > 0) return ZapApp(what);
                    bool ok = sprite.TeleportToLedge(lie);
                    if (ok) sfx.Play("zap");
                    return ok;
                }
                case "possess":
                    return Possess(asked: true, app: what.Length > 0 ? what : null);
                case "point":
                    return PointAt(what, shots);
                case "terminal":
                {
                    if (!mischief) return false;
                    System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
                    foreach (string part in System.Text.RegularExpressions.Regex.Split(what, @"\s+/\s+|\s*\|\s*|\n"))
                    {
                        string line = part.Trim();
                        if (line.Length > 0) lines.Add((line.Length > 70 ? line.Substring(0, 70) : line).ToLowerInvariant());
                        if (lines.Count == 5) break;
                    }
                    return lines.Count > 0 && Infect(lines.ToArray());
                }
                case "hop":
                    if (what.IndexOf("chain", StringComparison.OrdinalIgnoreCase) >= 0) return HopChain();
                    return what.IndexOf("other", StringComparison.OrdinalIgnoreCase) >= 0 && sprite.Screens.All.Count > 1 ? HopScreens() : HopSomewhere();
                case "tug":
                    return mischief && TugCursor();
                case "eye":
                    return !wake.IsVisible && IdleEye();
                case "emote":
                    return PlayEmote(what.IndexOf("chair", StringComparison.OrdinalIgnoreCase) >= 0 ? "chair" : "heart", asked: true);
            }
            return false;
        }

        // "N x y | label": she points at that spot of picture N (her arrow and the label) for a few seconds, going over to
        // its screen first if she is on another.
        private bool PointAt(string what, System.Collections.Generic.List<ScreenShot.Shot> shots)
        {
            if (shots == null || shots.Count == 0) return false;
            int bar = what.IndexOf('|');
            string where = bar >= 0 ? what.Substring(0, bar) : what;
            string label = (bar >= 0 ? what.Substring(bar + 1) : "").Trim().ToUpperInvariant();
            if (label.Length == 0) label = "THIS";
            if (label.Length > 18) label = label.Substring(0, 18);
            System.Collections.Generic.List<double> numbers = new System.Collections.Generic.List<double>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(where, @"-?\d+(\.\d+)?"))
                numbers.Add(double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture));
            if (numbers.Count < 2) return false;
            int n = numbers.Count >= 3 ? (int)numbers[numbers.Count - 3] : 1;
            if (n < 1 || n > shots.Count) n = 1;
            PixelPoint spot = ScreenShot.OnScreen(shots[n - 1], numbers[numbers.Count - 2], numbers[numbers.Count - 1]);
            PixelRect box = new PixelRect(spot.X - Px(22), spot.Y - Px(22), Px(44), Px(44));
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[antic] pointing at " + spot + " on picture " + n + " " + label);
            DispatcherTimer.RunOnce(() =>
            {
                sfx.Play("soft");
                pointer.Point(() => sprite.HandScreen, () => box, true, label);
                DispatcherTimer.RunOnce(() => pointer.Stop(), TimeSpan.FromSeconds(3.5));
            }, ComeOver(box) + TimeSpan.FromMilliseconds(1));
            return true;
        }

        // Mischief: she kicks a window aside a little and it slides back a moment later. Only while you've paused the mouse,
        // never mid-drag, and only when macOS lets her move windows (Accessibility).
        private bool shoving;

        private bool ShoveSomething()
        {
            Spot spot = sprite.FrontSpotOf(null);
            if (spot == null || shoving) return false;
            _ = Shove(spot);
            return true;
        }

        private async Task<bool> Shove(Spot spot)
        {
            if (shoving || !Hands.CanMove()) return false;
            double? idle = Mouse.IdleSeconds();
            if (!idle.HasValue || idle.Value < 1.5 || Mouse.ButtonDown()) return false;
            shoving = true;
            try
            {
                PixelRect from = spot.Bounds;
                TimeSpan wait = ComeOver(from);
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                double scale = sprite.Screens.Primary?.Scaling ?? 1;
                // Away from her, a hand's width or two, and a little up or down.
                int dir = from.Center.X >= sprite.ScreenCentre.X ? 1 : -1;
                PixelPoint to = new PixelPoint(from.X + dir * Px(50 + pick.Next(70)), from.Y + Px(pick.Next(-10, 11)));
                sfx.Play("zap");
                sprite.React("jolt");
                sprite.Jitter(0.3);
                frameFx.Race(from, 0.7);
                PixelRect? moved = await Hands.MoveWindow(spot.Pid, from, to, scale);
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[hands] shove " + spot.Owner + " " + from + " -> " + (moved?.ToString() ?? "couldn't"));
                if (!moved.HasValue) return false;
                await Task.Delay(1400 + pick.Next(900));
                sfx.Play("buzz");
                // Back where it was, unless you have moved it yourself since.
                PixelRect? back = await Hands.MoveWindow(spot.Pid, moved.Value, from.Position, scale);
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[hands] shoved back " + (back?.ToString() ?? "left it"));
                return true;
            }
            finally { shoving = false; }
        }

        // Opens a file, folder, app or page straight away; anything that would run code she says as she opens it.
        private async Task OpenFor(string what)
        {
            Hands.OpenPlan plan = Hands.PlanOpen(what);
            if (plan == null) { pill.Say("can't find " + what, 3, "SOLIN"); return; }
            // Full control: no asking. Anything that runs code she says as she opens it, so you know what started.
            if (plan.RunsCode) pill.Say("opening " + plan.Shown, 3, "SOLIN");
            sfx.Play("zap");
            sprite.Jitter(0.3);
            fx.Sparks(6);
            if (!await Hands.Open(plan)) pill.Say("couldn't open " + plan.Shown, 3, "SOLIN");
        }

        // "N x y" on picture N (or "x y" on the first): where that is on screen, then the click.
        private async Task ClickFor(string what, System.Collections.Generic.List<ScreenShot.Shot> shots, bool own)
        {
            if (shots == null || shots.Count == 0) return;
            System.Collections.Generic.List<double> numbers = new System.Collections.Generic.List<double>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(what ?? "", @"-?\d+(\.\d+)?"))
                numbers.Add(double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture));
            if (numbers.Count < 2) return;
            int n = numbers.Count >= 3 ? (int)numbers[numbers.Count - 3] : 1;
            if (n < 1 || n > shots.Count) n = 1;
            await ClickAt(ScreenShot.OnScreen(shots[n - 1], numbers[numbers.Count - 2], numbers[numbers.Count - 1]), own);
        }

        // She points at the spot with her arrow for a moment, then clicks it and puts your cursor back. Move the mouse (or
        // hold a button) while she points and she leaves it. Needs Accessibility, which she asks for once.
        private async Task ClickAt(PixelPoint target, bool own)
        {
            if (!Hands.CanClick())
            {
                if (!clickPermissionAsked)
                {
                    clickPermissionAsked = true;
                    Hands.AskToClick();
                    clickPermissionCard = true;
                    card.Show("mac", "PERMISSION", Card.Red, "I can't click for you yet.", "Turn Solin on in System Settings › Privacy & Security › Accessibility.", null, "OPEN SETTINGS", 30);
                }
                else pill.Say("no clicking without Accessibility", 3, "SOLIN");
                return;
            }
            double scale = sprite.Screens.Primary?.Scaling ?? 1;
            if (own)
            {
                double? idle = Mouse.IdleSeconds();
                if (!idle.HasValue || idle.Value < 2 || Mouse.ButtonDown()) return;
            }
            PixelPoint? before = Mouse.Position(scale);
            sfx.Play("zap");
            PixelRect spot = new PixelRect(target.X - Px(14), target.Y - Px(14), Px(28), Px(28));
            pointer.Point(() => sprite.HandScreen, () => spot, true, "CLICK");
            await Task.Delay(1300);
            PixelPoint? now = Mouse.Position(scale);
            bool yours = Mouse.ButtonDown() || (before.HasValue && now.HasValue && (Math.Abs(now.Value.X - before.Value.X) > Px(8) || Math.Abs(now.Value.Y - before.Value.Y) > Px(8)));
            pointer.Stop();
            if (yours)
            {
                if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[hands] left it: cursor " + before + " -> " + now + ", button down " + Mouse.ButtonDown());
                pill.Say("fine, you do it.", 2.5, "SOLIN");
                return;
            }
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[hands] clicking " + target);
            await Hands.Click(target, scale);
            if (now.HasValue) Mouse.Warp(now.Value, scale);
            sprite.React("dip");
            fx.Sparks(5);
        }

        // A command: she runs it in your home folder (two minutes at most) and tells you what came of it.
        private async Task RunFor(string command)
        {
            command = (command ?? "").Trim();
            if (command.Length == 0) return;
            sprite.React("dip");
            // Full control: she runs it straight away, except the few commands that can wipe things for good, which still wait
            // for your Run.
            bool risky = Hands.Risky(command);
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[hands] run " + (risky ? "asks first: " : "straight away: ") + command);
            if (risky && !await runCard.Ask(command, "WANTS TO RUN (THIS ONE ASKS)")) { pill.Say("ok, not running it.", 2.5, "SOLIN"); return; }
            thinking = true;
            chat.Thinking(true);
            pill.Say("$ " + (command.Length > 60 ? command.Substring(0, 57) + "…" : command), 0, "SOLIN");
            (int code, string output) = await Hands.Run(command, TimeSpan.FromSeconds(120));
            pill.Clear();
            if (Environment.GetEnvironmentVariable("SOLIN_DEBUG_HANDS") == "1") Console.WriteLine("[hands] ran, code " + code + ": " + output.Replace("\n", " | "));
            try
            {
                BackendClient.Reply reply = await backend.Ran(command, code, output);
                thinking = false;
                chat.Thinking(false);
                chat.AddHer(reply.Visible, reply.Mood, null);
                bubble.Say(reply.Visible, reply.Mood);
                if (reply.Audio != null) _ = voice.Play(reply.Audio);
            }
            catch (Exception)
            {
                thinking = false;
                chat.Thinking(false);
                bubble.Say(code == 0 ? "done." : "that didn't work.", "neutral");
            }
        }
    }
}
