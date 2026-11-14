// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Solin.Companion
{
    // The cut-corner frame behind the console, in the manner of the base's Mainframe (look described; no code taken).
    internal sealed class ConsoleFrame : Control
    {
        internal static readonly Color Cyan = Color.FromRgb(58, 214, 235);
        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height, cut = 14;
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(cut, 0), true); c.LineTo(new Point(w - cut, 0)); c.LineTo(new Point(w, cut)); c.LineTo(new Point(w, h - cut));
                c.LineTo(new Point(w - cut, h)); c.LineTo(new Point(cut, h)); c.LineTo(new Point(0, h - cut)); c.LineTo(new Point(0, cut)); c.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(248, 9, 11, 15)), new Pen(new SolidColorBrush(Cyan), 1.5), g);
        }
    }

    // Her console: BRAIN, VOICE and SOLIN sections with radio choices, in the layout of the base's Mainframe.
    internal sealed class SettingsWindow : Window
    {
        private static readonly IBrush Cyan = new SolidColorBrush(ConsoleFrame.Cyan);
        private static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(232, 36, 64));
        private readonly StackPanel brainRadios = new StackPanel { Spacing = 4 };
        private readonly TextBlock brainNow = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 13, Foreground = TalkLook.Ink };
        private readonly TextBlock brainWhere = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 10, Foreground = TalkLook.Dim };
        private readonly RadioButton voiceOff = Radio("OFF", "Silent · text only", "voice");
        private readonly RadioButton voiceOn = Radio("ELEVENLABS", "Her voice · needs a key", "voice");
        private readonly TextBox voiceId = Field("voice id");
        private readonly TextBox modelId = Field("model id");
        private readonly Slider sounds = new Slider { Minimum = 0, Maximum = 1, Width = 170 };
        private readonly Slider wander = new Slider { Minimum = 0, Maximum = 1, Width = 170 };
        private readonly NumericUpDown sleepAfter = new NumericUpDown { Minimum = 0, Maximum = 240, Increment = 1, Width = 110, FormatString = "0", FontFamily = TalkLook.Mono, FontSize = 12 };
        private readonly NumericUpDown breakAfter = new NumericUpDown { Minimum = 0, Maximum = 600, Increment = 10, Width = 110, FormatString = "0", FontFamily = TalkLook.Mono, FontSize = 12 };
        private readonly CheckBox mini = new CheckBox { Content = Mono("Start in mini form", 11, TalkLook.Ink) };
        private readonly TextBlock status = new TextBlock { FontFamily = TalkLook.Mono, FontSize = 10, Foreground = TalkLook.Dim, VerticalAlignment = VerticalAlignment.Center };
        private readonly List<(string name, RadioButton radio)> brains = new List<(string, RadioButton)>();
        private Point? dragFrom;
        private PixelPoint dragStart;

        internal event Func<object, System.Threading.Tasks.Task<string>> Applied;
        // Buttons in her section: "bring", "mini", "lie", "quit", "test-voice", "refresh".
        internal event Action<string> Action;

        internal SettingsWindow()
        {
            TalkLook.Floating(this);
            ShowActivated = true;
            Width = 400;
            StackPanel form = new StackPanel { Spacing = 6, Margin = new Thickness(18, 12, 18, 14) };

            Grid title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto"), Margin = new Thickness(0, 0, 0, 6) };
            TextBlock name = new TextBlock { Text = "SOLIN CORE", FontFamily = TalkLook.Mono, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = TalkLook.Ink, LetterSpacing = 3, VerticalAlignment = VerticalAlignment.Center };
            Border pillTag = Pill("Settings");
            TextBlock version = new TextBlock { Text = "v0.1", FontFamily = TalkLook.Mono, FontSize = 10, Foreground = TalkLook.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            Button hideButton = Flat("—"); Button closeButton = Flat("×");
            hideButton.Click += delegate { Hide(); }; closeButton.Click += delegate { Hide(); };
            Grid.SetColumn(pillTag, 1); Grid.SetColumn(version, 3); Grid.SetColumn(hideButton, 4); Grid.SetColumn(closeButton, 5);
            title.Children.Add(name); title.Children.Add(pillTag); title.Children.Add(version); title.Children.Add(hideButton); title.Children.Add(closeButton);
            form.Children.Add(title);

            // BRAIN
            StackPanel brainSection = Section("BRAIN >", null);
            brainSection.Children.Add(brainNow);
            brainSection.Children.Add(brainWhere);
            brainSection.Children.Add(new TextBlock { Text = "Pick one. Only one brain runs at a time.", FontFamily = TalkLook.Mono, FontStyle = FontStyle.Italic, FontSize = 10, Foreground = TalkLook.Dim, Margin = new Thickness(0, 6, 0, 2) });
            brainSection.Children.Add(brainRadios);
            Button refresh = Outline("Refresh"); refresh.HorizontalAlignment = HorizontalAlignment.Right; refresh.Margin = new Thickness(0, -26, 0, 0);
            refresh.Click += delegate { Action?.Invoke("refresh"); };
            brainSection.Children.Add(refresh);
            form.Children.Add(brainSection);

            // VOICE
            StackPanel voiceSection = Section("VOICE >", null);
            voiceSection.Children.Add(voiceOff);
            voiceSection.Children.Add(voiceOn);
            Grid voiceRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
            StackPanel ids = new StackPanel { Spacing = 4 };
            ids.Children.Add(voiceId); ids.Children.Add(modelId);
            Button test = Outline("Test"); Grid.SetColumn(test, 1); test.VerticalAlignment = VerticalAlignment.Top; test.Margin = new Thickness(8, 0, 0, 0);
            test.Click += delegate { Action?.Invoke("test-voice"); };
            voiceRow.Children.Add(ids); voiceRow.Children.Add(test);
            voiceSection.Children.Add(voiceRow);
            form.Children.Add(voiceSection);

            // SOLIN
            StackPanel solinSection = Section("SOLIN >", null);
            WrapPanel actions = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach ((string label, string act) in new[] { ("Bring here", "bring"), ("Mini / Full", "mini"), ("Lie down / Sit up", "lie") })
            {
                Button b = Outline(label); b.Margin = new Thickness(0, 0, 8, 6);
                string which = act;
                b.Click += delegate { Action?.Invoke(which); };
                actions.Children.Add(b);
            }
            solinSection.Children.Add(actions);
            solinSection.Children.Add(Row("Sounds", sounds));
            solinSection.Children.Add(Row("Wandering", wander));
            solinSection.Children.Add(Row("Sleep after (min)", sleepAfter));
            solinSection.Children.Add(Row("Break nudge (min, 0 off)", breakAfter));
            solinSection.Children.Add(mini);
            form.Children.Add(solinSection);

            Grid foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) };
            Button apply = Outline("Apply"); Button quit = Outline("Quit Solin"); quit.Foreground = Red; quit.BorderBrush = Red;
            apply.Margin = new Thickness(0, 0, 8, 0);
            apply.Click += async delegate { status.Text = "saving…"; string error = Applied == null ? null : await Applied(Body()); status.Text = error ?? "saved"; };
            quit.Click += delegate { Action?.Invoke("quit"); };
            Grid.SetColumn(apply, 1); Grid.SetColumn(quit, 2);
            foot.Children.Add(status); foot.Children.Add(apply); foot.Children.Add(quit);
            form.Children.Add(foot);

            Grid layers = new Grid { Margin = new Thickness(6) };
            layers.Children.Add(new ConsoleFrame());
            layers.Children.Add(form);
            Content = layers;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); };
            PointerPressed += delegate(object sender, PointerPressedEventArgs e) { if (e.Source is ConsoleFrame || e.Source is TextBlock || e.Source is StackPanel || e.Source is Grid) { dragFrom = e.GetPosition(this); dragStart = Position; } };
            PointerMoved += delegate(object sender, PointerEventArgs e) { if (dragFrom.HasValue) { Point at = e.GetPosition(this); Position = new PixelPoint(dragStart.X + (int)(at.X - dragFrom.Value.X), dragStart.Y + (int)(at.Y - dragFrom.Value.Y)); dragStart = Position; } };
            PointerReleased += delegate { dragFrom = null; };
        }

        private static TextBlock Mono(string text, double size, IBrush brush) { return new TextBlock { Text = text, FontFamily = TalkLook.Mono, FontSize = size, Foreground = brush, VerticalAlignment = VerticalAlignment.Center }; }

        private static Border Pill(string text)
        {
            return new Border { BorderBrush = Cyan, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 1), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Mono(text, 10, Cyan) };
        }

        private static Button Flat(string text)
        {
            return new Button { Content = Mono(text, 13, TalkLook.Dim), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 0) };
        }

        private static Button Outline(string text)
        {
            return new Button { Content = Mono(text, 11, TalkLook.Ink), Background = Brushes.Transparent, BorderBrush = TalkLook.PlateEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 4) };
        }

        private static TextBox Field(string watermark)
        {
            TextBox box = new TextBox { Watermark = watermark, FontFamily = TalkLook.Mono, FontSize = 12, Padding = new Thickness(8, 4), BorderBrush = TalkLook.PlateEdge, CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Foreground = TalkLook.Ink };
            return box;
        }

        // A radio in the Mainframe manner: a bold label, then a dim note.
        private static RadioButton Radio(string label, string note, string group)
        {
            StackPanel line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            line.Children.Add(new TextBlock { Text = label, FontFamily = TalkLook.Mono, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = TalkLook.Ink, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(note)) line.Children.Add(Mono(note, 10, TalkLook.Dim));
            return new RadioButton { Content = line, GroupName = group, MinHeight = 0, Padding = new Thickness(6, 0, 0, 0) };
        }

        private static StackPanel Section(string label, string hint)
        {
            StackPanel section = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 4) };
            StackPanel head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new TextBlock { Text = label, FontFamily = TalkLook.Mono, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Red });
            section.Children.Add(head);
            Border body = new Border { BorderBrush = Red, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2) };
            StackPanel inner = new StackPanel { Spacing = 4 };
            body.Child = inner;
            section.Children.Add(body);
            // Children added by callers go inside the red-barred body.
            section.Tag = inner;
            return new SectionPanel(section, inner);
        }

        // A StackPanel whose Children.Add goes to the inner body; keeps the call sites simple.
        private sealed class SectionPanel : StackPanel
        {
            internal SectionPanel(StackPanel outer, StackPanel inner) { base.Children.Add(outer); Inner = inner; }
            internal readonly StackPanel Inner;
            internal new Avalonia.Controls.Controls Children { get { return Inner.Children; } }
        }

        private static Control Row(string label, Control control)
        {
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(Mono(label, 11, TalkLook.Dim));
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        internal void Load(JsonElement root)
        {
            brains.Clear();
            brainRadios.Children.Clear();
            string current = root.TryGetProperty("brain_setting", out JsonElement bs) ? bs.GetString() : "auto";
            string running = root.TryGetProperty("brain", out JsonElement br) ? br.GetString() : "";
            brainNow.Text = running.Replace(":", " · ");
            brainWhere.Text = running.StartsWith("ollama") || running.StartsWith("claude-cli") ? "Running on this Mac" : running.StartsWith("offline") ? "No brain reachable" : "In the cloud";
            void Add(string key, string label, string note)
            {
                RadioButton r = Radio(label, note, "brain");
                r.IsChecked = key == current;
                brains.Add((key, r));
                brainRadios.Children.Add(r);
            }
            Add("auto", "AUTO", "First brain that can run");
            if (root.TryGetProperty("brains", out JsonElement all))
                foreach (JsonProperty p in all.EnumerateObject())
                {
                    if (p.Name == "offline") continue;
                    bool ok = p.Value.TryGetProperty("ok", out JsonElement okEl) && okEl.GetBoolean();
                    string why = p.Value.TryGetProperty("why", out JsonElement w) ? w.GetString() : "";
                    string model = p.Value.TryGetProperty("model", out JsonElement m) && m.ValueKind == JsonValueKind.String ? m.GetString() : "";
                    Add(p.Name, p.Name.ToUpperInvariant(), (ok ? model : why) ?? "");
                    if (!ok) brains[brains.Count - 1].radio.Opacity = 0.55;
                }
            if (root.TryGetProperty("voice", out JsonElement v))
            {
                bool on = v.TryGetProperty("speak", out JsonElement sp) && sp.ValueKind == JsonValueKind.True;
                voiceOn.IsChecked = on; voiceOff.IsChecked = !on;
                voiceId.Text = v.TryGetProperty("voice_id", out JsonElement vi) ? vi.GetString() : "";
                modelId.Text = v.TryGetProperty("model_id", out JsonElement mi) ? mi.GetString() : "";
            }
            if (root.TryGetProperty("companion", out JsonElement c))
            {
                sounds.Value = c.TryGetProperty("sounds", out JsonElement so) ? so.GetDouble() : 0.5;
                wander.Value = c.TryGetProperty("wander", out JsonElement wa) ? wa.GetDouble() : 0.35;
                sleepAfter.Value = c.TryGetProperty("sleep_after_min", out JsonElement sl) ? (decimal)sl.GetDouble() : 8;
                breakAfter.Value = c.TryGetProperty("break_after_min", out JsonElement bk) ? (decimal)bk.GetDouble() : 120;
                mini.IsChecked = c.TryGetProperty("size", out JsonElement sz) && sz.GetString() == "mini";
            }
            status.Text = "";
        }

        private object Body()
        {
            string chosen = "auto";
            foreach ((string key, RadioButton r) in brains) if (r.IsChecked == true) chosen = key;
            return new
            {
                brain = chosen,
                voice = new { speak = voiceOn.IsChecked == true, voice_id = voiceId.Text ?? "", model_id = modelId.Text ?? "" },
                companion = new
                {
                    sounds = Math.Round(sounds.Value, 2), wander = Math.Round(wander.Value, 2),
                    sleep_after_min = (double)(sleepAfter.Value ?? 8), break_after_min = (double)(breakAfter.Value ?? 120),
                    size = mini.IsChecked == true ? "mini" : "full",
                },
            };
        }

        internal void Open(PixelPoint at) { Position = at; Show(); Activate(); }
    }
}
