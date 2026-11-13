// Solin companion entry point. Solin-owned; not derived from upstream.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
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
        private bool thinking;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                sprite = new SpriteWindow();
                typer = new Typer();
                bubble = new Bubble();
                desktop.MainWindow = sprite;
                Wire();
                desktop.Exit += delegate { backend.Dispose(); launcher.Dispose(); };
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
            backend.Replied += delegate(string visible) { Dispatcher.UIThread.Post(() => { if (!thinking) bubble.Say(visible, mood.Mood); }); };
            sprite.Poked += delegate { sprite.React("dip"); _ = backend.Event("poked"); };
            sprite.Dragged += delegate { _ = backend.Event("dragged"); };
            sprite.TalkAsked += delegate
            {
                // Under her feet, centred.
                typer.Open(new PixelPoint(sprite.Position.X + Px(sprite.Width / 2) - Px(160), sprite.Position.Y + Px(sprite.Height) - Px(30)));
            };
            bubble.Follow(() => new PixelPoint(sprite.Position.X + Px(sprite.Width / 2), sprite.Position.Y + Px(sprite.Height * 0.12)));
            bubble.Speaking += delegate(bool on) { sprite.Talking = on; };
            typer.Sent += delegate(string text) { _ = Ask(text); };
            sprite.Opened += async delegate
            {
                if (await launcher.EnsureRunning(backend)) backend.Listen();
            };
        }

        private async Task Ask(string text)
        {
            thinking = true;
            sprite.React("dip");
            try
            {
                (string visible, string replyMood) = await backend.Chat(text);
                thinking = false;
                mood.Set(replyMood, mood.Trust);
                bubble.Say(visible, replyMood);
            }
            catch (Exception)
            {
                thinking = false;
                bubble.Say("[offline] i can't reach my backend right now.", "neutral");
            }
        }
    }
}
