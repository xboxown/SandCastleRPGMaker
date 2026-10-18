using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Edit > Map Ambient Sounds: the sound looping at dawn, by day, at dusk and at night on this map (the game's whole world).</summary>
public partial class MainWindow
{
    private void MapAmbientMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_map is null) { MessageBox.Show(this, "Open or make a map first.", "Map Ambient Sounds"); return; }
        var a = _map.Ambient;
        var d = _systemSettings.DayNight;
        var window = new Window
        {
            Title = $"Map Ambient Sounds - {_map.MapName}", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ShowInTaskbar = false
        };
        var rows = new StackPanel { Margin = new Thickness(12) };
        var none = new ResourceEntry { Id = -1, Name = "(None - quiet)" };
        var sounds = new List<ResourceEntry> { none };
        sounds.AddRange(_resourceDatabase.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var combos = new List<(ComboBox Combo, Action<int> Set)>();
        void Row(string label, int current, Action<int> set)
        {
            rows.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var combo = new ComboBox { Width = 300, ItemsSource = sounds, DisplayMemberPath = "Name", SelectedItem = sounds.FirstOrDefault(s => s.Id == current) ?? none };
            var play = new Button { Content = "Play", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
            play.Click += (_, _) => { if (combo.SelectedItem is ResourceEntry { Id: >= 0 } s) SoundPlayback.TryPlay(_resourceDatabase, s.Id); };
            var stop = new Button { Content = "Stop", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
            stop.Click += (_, _) => SoundPlayback.StopAll();
            line.Children.Add(combo);
            line.Children.Add(play);
            line.Children.Add(stop);
            rows.Children.Add(line);
            combos.Add((combo, set));
        }
        Row($"Dawn ({d.DawnStartHour}:00 - {d.DayStartHour}:00): birds waking", a.DawnSoundId, v => a.DawnSoundId = v);
        Row($"Day ({d.DayStartHour}:00 - {d.DuskStartHour}:00)", a.DaySoundId, v => a.DaySoundId = v);
        Row($"Dusk ({d.DuskStartHour}:00 - {d.NightStartHour}:00)", a.DuskSoundId, v => a.DuskSoundId = v);
        Row($"Night ({d.NightStartHour}:00 - {d.DawnStartHour}:00): crickets", a.NightSoundId, v => a.NightSoundId = v);
        rows.Children.Add(new TextBlock
        {
            Text = "Each one loops for as long as that part of the day lasts, then gives way to the next. The hours are System Settings > Day & Night's. Rain and storms play over it. A sound made to loop smoothly (its end running into its start) works best.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 10, 0, 0)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true, Margin = new Thickness(6, 0, 0, 0) };
        ok.Click += (_, _) =>
        {
            foreach (var (combo, set) in combos) set((combo.SelectedItem as ResourceEntry)?.Id ?? -1);
            window.DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        window.Content = rows;
        window.Closed += (_, _) => SoundPlayback.StopAll();
        window.ShowDialog();
    }
}
