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
    // Her settings, read from and written to the backend's data/config.json through /settings.
    internal sealed class SettingsWindow : Window
    {
        private readonly ComboBox brain = new ComboBox { Width = 220 };
        private readonly CheckBox speak = new CheckBox { Content = "Speak replies (ElevenLabs)", Foreground = TalkLook.Ink };
        private readonly TextBox voiceId = new TextBox { Width = 220, Watermark = "voice id", FontSize = 12 };
        private readonly TextBox modelId = new TextBox { Width = 220, Watermark = "model id", FontSize = 12 };
        private readonly Slider sounds = new Slider { Minimum = 0, Maximum = 1, Width = 220, TickFrequency = 0.1 };
        private readonly Slider wander = new Slider { Minimum = 0, Maximum = 1, Width = 220, TickFrequency = 0.05 };
        private readonly NumericUpDown sleepAfter = new NumericUpDown { Minimum = 0, Maximum = 240, Increment = 1, Width = 120, FormatString = "0" };
        private readonly NumericUpDown breakAfter = new NumericUpDown { Minimum = 0, Maximum = 600, Increment = 10, Width = 120, FormatString = "0" };
        private readonly CheckBox mini = new CheckBox { Content = "Start in mini form", Foreground = TalkLook.Ink };
        private readonly TextBlock status = new TextBlock { Foreground = TalkLook.Faint, FontSize = 11, Text = "" };
        private readonly List<string> brainNames = new List<string>();
        private Point? dragFrom;
        private PixelPoint dragStart;

        // Called with the body to POST when Apply is pressed.
        internal event Func<object, System.Threading.Tasks.Task<string>> Applied;

        internal SettingsWindow()
        {
            TalkLook.Floating(this);
            ShowActivated = true;
            StackPanel form = new StackPanel { Spacing = 8, Margin = new Thickness(16, 12, 16, 14) };
            // The base's Mainframe layout, in Solin's colours: a title bar, then sections with a red label and a rule.
            Grid title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            title.Children.Add(new TextBlock { Text = "SOLIN · CORE", FontFamily = TalkLook.Mono, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = TalkLook.Ink, LetterSpacing = 2 });
            TextBlock version = new TextBlock { Text = "v0.1", FontFamily = TalkLook.Mono, FontSize = 10, Foreground = TalkLook.Dim, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(version, 1);
            title.Children.Add(version);
            form.Children.Add(title);
            form.Children.Add(Section("BRAIN >", "Pick one. Only one brain runs at a time."));
            form.Children.Add(Row("Brain", brain));
            form.Children.Add(Section("VOICE >", "Her voice, through ElevenLabs."));
            form.Children.Add(speak);
            form.Children.Add(Row("Voice", voiceId));
            form.Children.Add(Row("Model", modelId));
            form.Children.Add(Section("SOLIN >", "How she behaves on your desk."));
            form.Children.Add(Row("Sounds", sounds));
            form.Children.Add(Row("Wandering", wander));
            form.Children.Add(Row("Sleep after (min)", sleepAfter));
            form.Children.Add(Row("Break nudge after (min, 0 off)", breakAfter));
            form.Children.Add(mini);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            Button apply = new Button { Content = "Apply" };
            Button close = new Button { Content = "Close" };
            apply.Click += async delegate
            {
                status.Text = "saving…";
                string error = Applied == null ? null : await Applied(Body());
                status.Text = error ?? "saved";
            };
            close.Click += delegate { Hide(); };
            buttons.Children.Add(status);
            buttons.Children.Add(apply);
            buttons.Children.Add(close);
            form.Children.Add(buttons);
            Grid body = new Grid();
            body.Children.Add(form);
            body.Children.Add(new Border { Width = 3, Background = TalkLook.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(14, 0, 0, 14) });
            Content = TalkLook.Card(body);
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); };
            // Drag anywhere on the card that is not a control.
            PointerPressed += delegate(object sender, PointerPressedEventArgs e) { if (e.Source is Border || e.Source is TextBlock || e.Source is StackPanel || e.Source is Grid) { dragFrom = e.GetPosition(this); dragStart = Position; } };
            PointerMoved += delegate(object sender, PointerEventArgs e) { if (dragFrom.HasValue) { Point at = e.GetPosition(this); Position = new PixelPoint(dragStart.X + (int)(at.X - dragFrom.Value.X), dragStart.Y + (int)(at.Y - dragFrom.Value.Y)); dragStart = Position; } };
            PointerReleased += delegate { dragFrom = null; };
        }

        private static Control Section(string label, string hint)
        {
            StackPanel head = new StackPanel { Spacing = 2, Margin = new Thickness(0, 10, 0, 2) };
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Border { Width = 3, Height = 14, Background = TalkLook.AccentBrush, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = label, FontFamily = TalkLook.Mono, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = TalkLook.AccentBrush });
            head.Children.Add(row);
            head.Children.Add(new TextBlock { Text = hint, FontFamily = TalkLook.Mono, FontSize = 10, Foreground = TalkLook.Dim, Margin = new Thickness(11, 0, 0, 0) });
            head.Children.Add(new Border { Height = 1, Background = TalkLook.PlateEdge, Margin = new Thickness(0, 4, 0, 0) });
            return head;
        }

        private static Control Row(string label, Control control)
        {
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            TextBlock name = new TextBlock { Text = label, Foreground = TalkLook.Faint, FontSize = 12, FontFamily = TalkLook.Mono, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(name);
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        // Fills the form from the backend's settings document.
        internal void Load(JsonElement root)
        {
            brainNames.Clear();
            List<string> items = new List<string> { "auto" };
            brainNames.Add("auto");
            string current = root.TryGetProperty("brain_setting", out JsonElement bs) ? bs.GetString() : "auto";
            if (root.TryGetProperty("brains", out JsonElement all))
                foreach (JsonProperty p in all.EnumerateObject())
                {
                    bool ok = p.Value.TryGetProperty("ok", out JsonElement okEl) && okEl.GetBoolean();
                    string why = p.Value.TryGetProperty("why", out JsonElement w) ? w.GetString() : "";
                    items.Add(p.Name + (ok ? "" : "  (" + why + ")"));
                    brainNames.Add(p.Name);
                }
            brain.ItemsSource = items;
            brain.SelectedIndex = Math.Max(0, brainNames.IndexOf(current));
            if (root.TryGetProperty("voice", out JsonElement v))
            {
                speak.IsChecked = v.TryGetProperty("speak", out JsonElement sp) && sp.ValueKind == JsonValueKind.True;
                voiceId.Text = v.TryGetProperty("voice_id", out JsonElement vi) ? vi.GetString() : "";
                modelId.Text = v.TryGetProperty("model_id", out JsonElement mi) ? mi.GetString() : "";
            }
            if (root.TryGetProperty("companion", out JsonElement c))
            {
                sounds.Value = c.TryGetProperty("sounds", out JsonElement so) ? so.GetDouble() : 0.5;
                wander.Value = c.TryGetProperty("wander", out JsonElement wa) ? wa.GetDouble() : 0.35;
                sleepAfter.Value = c.TryGetProperty("sleep_after_min", out JsonElement sl) ? (decimal)sl.GetDouble() : 8;
                breakAfter.Value = c.TryGetProperty("break_after_min", out JsonElement br) ? (decimal)br.GetDouble() : 120;
                mini.IsChecked = c.TryGetProperty("size", out JsonElement sz) && sz.GetString() == "mini";
            }
            status.Text = "";
        }

        private object Body()
        {
            string chosen = brain.SelectedIndex >= 0 && brain.SelectedIndex < brainNames.Count ? brainNames[brain.SelectedIndex] : "auto";
            return new
            {
                brain = chosen,
                voice = new { speak = speak.IsChecked == true, voice_id = voiceId.Text ?? "", model_id = modelId.Text ?? "" },
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
