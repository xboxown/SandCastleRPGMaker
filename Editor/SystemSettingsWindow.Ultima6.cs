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
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
