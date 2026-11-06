using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>System Settings > Ultima 6: only an Ultima 6 project's engine uses these (Engine/Ultima6Model.cs).</summary>
public partial class SystemSettingsWindow
{
    private UIElement BuildUltima6Tab()
    {
        var u6 = _settings.Ultima6;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = "Only this Ultima 6 project's engine uses these (Ultima 6, Savage Empire, Martian Dreams). Its screen is made in the HUD Editor (Tools).", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10) });

        panel.Children.Add(new TextBlock { Text = "The party", FontWeight = FontWeights.Bold });
        var party = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
        party.Children.Add(new TextBlock { Text = "Most who can join (with the main character):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        party.Children.Add(new TextBox { Width = 40, Text = Ultima6Settings.MaxPartyMembers.ToString(), IsEnabled = false, ToolTip = "Locked: Ultima 6's party is at most 9." });
        party.Children.Add(new TextBlock { Text = "  locked, as Ultima 6", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        panel.Children.Add(party);
        panel.Children.Add(new TextBlock { Text = "One member shows in the HUD's panel at a time - the player picks them with 1 to 9. A character asked to join a full party says its Party is full words (Character Editor > Verbose).", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 10) });

        panel.Children.Add(new TextBlock { Text = "Conversations", FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "As Ultima 6: the character's portrait and words in the text area; the player types a word to ask about (name, job, bye...). No clicking.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        keyRow.Children.Add(new TextBlock { Text = "Keywords in what they say, in this colour:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var swatch = new Button { Width = 44, Height = 20 };
        void ShowSwatch() => swatch.Background = ColorConverter.ConvertFromString(u6.KeywordColor) is Color c ? new SolidColorBrush(c) : Brushes.Gold;
        swatch.Click += (_, _) =>
        {
            var start = ColorConverter.ConvertFromString(u6.KeywordColor) is Color c ? c : Colors.Gold;
            var picker = new ColorPickerWindow(start) { Owner = this };
            if (picker.ShowDialog() != true) return;
            u6.KeywordColor = $"#{picker.Result.R:X2}{picker.Result.G:X2}{picker.Result.B:X2}";
            ShowSwatch();
        };
        ShowSwatch();
        keyRow.Children.Add(swatch);
        panel.Children.Add(keyRow);

        var none = new ResourceEntry { Id = -1, Name = "(none - the map's music goes on)" };
        var tunes = new List<ResourceEntry> { none };
        tunes.AddRange(_resources.Entries.Where(r => r.Kind is ResourceKind.Music or ResourceKind.Sound));
        var music = new ComboBox { Width = 260, ItemsSource = tunes, DisplayMemberPath = "Name", SelectedItem = tunes.FirstOrDefault(t => t.Id == u6.ConversationMusicId) ?? none };
        var after = new ComboBox { Width = 260, ItemsSource = new[] { Ultima6Settings.AfterTalkRestart, Ultima6Settings.AfterTalkRandom }, SelectedItem = u6.AfterTalkMusic == Ultima6Settings.AfterTalkRandom ? Ultima6Settings.AfterTalkRandom : Ultima6Settings.AfterTalkRestart };
        music.SelectionChanged += (_, _) => { u6.ConversationMusicId = (music.SelectedItem as ResourceEntry)?.Id ?? -1; after.IsEnabled = u6.ConversationMusicId >= 0; };
        after.SelectionChanged += (_, _) => { if (after.SelectedItem is string s) u6.AfterTalkMusic = s; };
        after.IsEnabled = u6.ConversationMusicId >= 0;
        var musicRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        musicRow.Children.Add(new TextBlock { Text = "Music while talking:", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        musicRow.Children.Add(music);
        panel.Children.Add(musicRow);
        var afterRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        afterRow.Children.Add(new TextBlock { Text = "When the talk ends:", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        afterRow.Children.Add(after);
        panel.Children.Add(afterRow);
        panel.Children.Add(new TextBlock { Text = "As Nuvie: talking stops the map's music; when it ends the map's music starts again from the beginning, or a random song of the map's plays.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
        panel.Children.Add(BuildU6WordsSection(u6));
        panel.Children.Add(BuildU6KeysSection(u6));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>The words the text area says (Engine/U6Words): each verb as it shows and each message, Ultima 6's until changed.</summary>
    private UIElement BuildU6WordsSection(Ultima6Settings u6)
    {
        var section = new StackPanel();
        section.Children.Add(new TextBlock { Text = "Words (the verbs and what the text area says)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
        section.Children.Add(new TextBlock { Text = "Everything the game tells is written in the HUD's text area, as Ultima 6: \"Look-wall\", \"You see a wall.\", \"Chicken attacks rat.\". Change any of the words here. {0} is the thing, {1} the one doing it. Empty: Ultima 6's words.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var (id, def, what) in U6Words.All)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock { Text = what + ":", Width = 270, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = what });
            var box = new TextBox { Width = 260, Text = u6.Words.TryGetValue(id, out var w) ? w : def };
            string wordId = id, standard = def;
            box.TextChanged += (_, _) =>
            {
                if (box.Text.Length == 0 || box.Text == standard) u6.Words.Remove(wordId);
                else u6.Words[wordId] = box.Text;
            };
            row.Children.Add(box);
            section.Children.Add(row);
        }
        return section;
    }

    /// <summary>Ultima 6's keys, changeable (Engine/U6Keys.cs) - as the Ultima 8 tab's keyboard.</summary>
    private UIElement BuildU6KeysSection(Ultima6Settings u6)
    {
        var section = new StackPanel();
        section.Children.Add(new TextBlock { Text = "The keyboard", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
        section.Children.Add(new TextBlock { Text = "Ultima 6's keys to start with. Click a key, then press the new one, alone or held with Ctrl, Shift or Alt (click elsewhere: no change). A key that clashes: they swap. In the game, the Ultima 6 tab's commands change them too: Ask For Key, Set Key, Get Key Name, Reset Keys.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        var keys = U6Keys.MakerKeys(u6);
        var buttons = new Dictionary<string, Button>();
        var refused = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
        void ShowKeys()
        {
            foreach (var action in U6Keys.Actions) buttons[action.Id].Content = U8Keys.Display(keys[action.Id]);
        }
        void Store() => u6.Keys = keys.Where(kv => U6Keys.Actions.First(a => a.Id == kv.Key).DefaultKey != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (group, heading) in new[] { (U8KeyGroup.World, "On the map"), (U8KeyGroup.Everywhere, "Everywhere (no other action can share these keys)"), (U8KeyGroup.Screens, "While typing") })
        {
            section.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            foreach (var action in U6Keys.Actions.Where(a => a.Group == group))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new TextBlock { Text = action.Name + ":", Width = 270, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Width = 130 };
                buttons[action.Id] = button;
                var capture = new U8KeyCapture();
                bool waiting = false;
                var a = action;
                void Take(string key)
                {
                    waiting = false;
                    bool ok = U6Keys.Assign(keys, a.Id, key, swap: true);
                    refused.Text = ok ? "" : $"{U8Keys.Display(key)} can't go to \"{a.Name}\": the action that has it can't take {U8Keys.Display(keys[a.Id])} in return. Move that one first.";
                    Store();
                    ShowKeys();
                }
                button.Click += (_, _) => { waiting = true; capture.Reset(); refused.Text = ""; button.Content = U6Words.Of(u6, "PressAKey"); };
                button.LostFocus += (_, _) => { waiting = false; ShowKeys(); };
                button.PreviewKeyDown += (_, e) =>
                {
                    if (!waiting) return;
                    var pressed = U8KeyCapture.RealKey(e);
                    if (pressed == System.Windows.Input.Key.F4 && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) return;
                    e.Handled = true;
                    if (!e.IsRepeat && capture.Down(pressed, System.Windows.Input.Keyboard.Modifiers) is { } key) Take(key);
                };
                button.PreviewKeyUp += (_, e) =>
                {
                    if (!waiting) return;
                    e.Handled = true;
                    if (capture.Up(U8KeyCapture.RealKey(e)) is { } key) Take(key);
                };
                row.Children.Add(button);
                section.Children.Add(row);
            }
        }
        ShowKeys();
        section.Children.Add(refused);
        var back = new Button { Content = "Back to Ultima 6's keys", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 0, 10) };
        back.Click += (_, _) =>
        {
            foreach (var action in U6Keys.Actions) keys[action.Id] = action.DefaultKey;
            Store();
            ShowKeys();
        };
        section.Children.Add(back);
        if (ProjectSession.ProjectIdProvider?.Invoke() is { } projectId)
        {
            var forget = new Button { Content = "Forget the keys changed while playing", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), IsEnabled = U6Keys.HasPlayerKeys(projectId) };
            forget.Click += (_, _) => { U6Keys.ForgetPlayerKeys(projectId); forget.IsEnabled = false; };
            section.Children.Add(forget);
        }
        return section;
    }
}
