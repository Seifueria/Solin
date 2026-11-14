// Solin-owned; not derived from upstream.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Solin.Companion.Upstream;

namespace Solin.Companion
{
    // A floating conversation with her: past exchanges from her memory, new ones as they happen, her face and mood up top.
    internal sealed class ChatWindow : Window
    {
        private readonly StackPanel messages = new StackPanel { Spacing = 4, Margin = new Thickness(14, 10) };
        private readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 360 };
        private readonly TextBox box = new TextBox { Watermark = "say something", AcceptsReturn = false, Foreground = TalkLook.Ink, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 14, Width = 300 };
        private readonly TextBlock tag = new TextBlock { Foreground = TalkLook.Faint, FontSize = 11, Text = "live" };
        private readonly TextBlock brainTag = new TextBlock { Foreground = TalkLook.Faint, FontSize = 10, Text = "" };
        private readonly Portrait face = new Portrait(40);
        private readonly TextBlock thinking = new TextBlock { Foreground = TalkLook.Faint, FontSize = 11, Text = "thinking…", IsVisible = false, Margin = new Thickness(14, 0, 0, 4) };
        private Point? dragFrom;
        private PixelPoint dragStart;

        internal event Action<string> Sent;
        internal Func<(Clip clip, int cell, Clip mouth, int mouthCell)> Frame { set { face.Frame = value; } }

        internal ChatWindow()
        {
            TalkLook.Floating(this);
            ShowActivated = true;
            Width = 380;
            TalkLook.Bare(box);
            SizeToContent = SizeToContent.Height;
            StackPanel names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Foreground = TalkLook.Ink, FontSize = 12, FontFamily = TalkLook.Mono, Text = "SOLIN D'ARC" });
            StackPanel tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            tags.Children.Add(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1), Background = new SolidColorBrush(Color.FromArgb(60, 232, 72, 120)), Child = tag });
            tags.Children.Add(brainTag);
            names.Children.Add(tags);
            Button close = new Button { Content = "×", Foreground = TalkLook.Faint, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 0), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += delegate { Hide(); };
            Grid header = new Grid { Margin = new Thickness(14, 10, 8, 8), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            left.Children.Add(face);
            left.Children.Add(names);
            header.Children.Add(left);
            Grid.SetColumn(close, 2);
            header.Children.Add(close);
            // Drag the card by its header.
            header.Background = Brushes.Transparent;
            header.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { dragFrom = e.GetPosition(this); dragStart = Position; e.Pointer.Capture(header); };
            header.PointerMoved += delegate(object sender, PointerEventArgs e) { if (dragFrom.HasValue) { Point at = e.GetPosition(this); Position = new PixelPoint(dragStart.X + (int)(at.X - dragFrom.Value.X), dragStart.Y + (int)(at.Y - dragFrom.Value.Y)); dragStart = Position; } };
            header.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { dragFrom = null; e.Pointer.Capture(null); };

            scroll.Content = messages;
            DockPanel inputRow = new DockPanel { LastChildFill = true };
            TextBlock prompt = new TextBlock { Text = "> you:", FontFamily = TalkLook.Mono, FontStyle = FontStyle.Italic, FontSize = 14, Foreground = TalkLook.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(prompt, Dock.Left);
            inputRow.Children.Add(prompt);
            inputRow.Children.Add(box);
            Border inputCard = new Border { Background = TalkLook.Plate, BorderBrush = TalkLook.PlateEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Margin = new Thickness(14, 4, 14, 12), Child = inputRow };
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
                if (e.Key == Key.Enter)
                {
                    string text = (box.Text ?? "").Trim();
                    box.Text = "";
                    if (text.Length > 0) { AddYou(text); Sent?.Invoke(text); }
                    e.Handled = true;
                }
            };
            StackPanel stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(new Border { Height = 1, Background = TalkLook.Edge });
            stack.Children.Add(scroll);
            stack.Children.Add(thinking);
            stack.Children.Add(inputCard);
            Grid body = new Grid();
            body.Children.Add(stack);
            body.Children.Add(new Border { Width = 3, Background = TalkLook.AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(14, 0, 0, 14) });
            Content = TalkLook.Card(body);
        }

        internal void SetMood(string mood) { tag.Text = string.IsNullOrEmpty(mood) || mood == "neutral" ? "live" : "feeling " + mood; }
        internal void SetBrain(string brain) { brainTag.Text = brain ?? ""; }
        internal void Thinking(bool on) { thinking.IsVisible = on; }

        internal void Open(PixelPoint near)
        {
            Position = near;
            Show();
            Activate();
            box.Focus();
            scroll.ScrollToEnd();
        }

        internal void Load(IEnumerable<(string user, string visible, string internalThought)> history)
        {
            messages.Children.Clear();
            foreach ((string user, string visible, string internalThought) in history)
            {
                if (!string.IsNullOrEmpty(user)) AddYou(user);
                if (!string.IsNullOrEmpty(visible)) AddHer(visible, null, internalThought);
            }
            scroll.ScrollToEnd();
        }

        internal void AddYou(string text)
        {
            messages.Children.Add(Line(text, true, null));
            scroll.ScrollToEnd();
        }

        // Her line; the private thought behind it shows on hover.
        internal void AddHer(string text, string mood, string internalThought)
        {
            Control line = Line(text, false, mood);
            if (!string.IsNullOrEmpty(internalThought)) ToolTip.SetTip(line, new TextBlock { Text = internalThought, MaxWidth = 280, TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic });
            messages.Children.Add(line);
            scroll.ScrollToEnd();
        }

        // A transcript line in the terminal manner: "> you: ..." dim and italic, "SOLIN: ..." with her name in the accent.
        private static Control Line(string text, bool you, string mood)
        {
            TextBlock block = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320, FontSize = 13, LineHeight = 19, FontFamily = TalkLook.Mono };
            if (you)
            {
                block.Foreground = TalkLook.Dim;
                block.FontStyle = FontStyle.Italic;
                block.Text = "> you: " + text;
            }
            else
            {
                block.Inlines = new InlineCollection();
                block.Inlines.Add(new Run("SOLIN: ") { Foreground = TalkLook.AccentBrush, FontWeight = FontWeight.Bold });
                block.Inlines.Add(new Run(text) { Foreground = TalkLook.Ink });
                if (!string.IsNullOrEmpty(mood) && mood != "neutral") block.Inlines.Add(new Run("  [" + mood + "]") { Foreground = TalkLook.Faint, FontSize = 11 });
            }
            return new Border { Child = block, Padding = new Thickness(2, 2), HorizontalAlignment = HorizontalAlignment.Left };
        }
    }
}
