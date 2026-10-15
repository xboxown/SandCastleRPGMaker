using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Edit > Map Weather: this map's weather in play - rain, snow, hail or storms, clouds, the wind, and whether it comes and goes.</summary>
public partial class MainWindow
{
    private void MapWeatherMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_map is null) { MessageBox.Show(this, "Open or make a map first.", "Map Weather"); return; }
        var w = _map.Weather;
        var window = new Window
        {
            Title = $"Map Weather - {_map.MapName}", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ShowInTaskbar = false
        };
        var rows = new StackPanel { Margin = new Thickness(12) };
        TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
        var kind = new ComboBox { ItemsSource = Enum.GetValues<WeatherKind>(), SelectedItem = w.Kind };
        var intensity = new Slider { Minimum = 1, Maximum = 100, Value = w.Intensity, TickFrequency = 10, IsSnapToTickEnabled = false };
        var clouds = new CheckBox { Content = "Clouds drifting over the land (their shadows)", IsChecked = w.Clouds, Margin = new Thickness(0, 8, 0, 0) };
        var cover = new Slider { Minimum = 5, Maximum = 100, Value = w.CloudCover };
        var winds = new[] { SpriteDirection.North, SpriteDirection.NorthEast, SpriteDirection.East, SpriteDirection.SouthEast, SpriteDirection.South, SpriteDirection.SouthWest, SpriteDirection.West, SpriteDirection.NorthWest };
        var wind = new ComboBox { ItemsSource = winds, SelectedItem = w.Wind };
        var changes = new CheckBox { Content = "It comes and goes by itself (every game hour, a chance it turns)", IsChecked = w.Changes, Margin = new Thickness(0, 8, 0, 0) };
        var chance = new TextBox { Width = 50, Text = w.ChangeChancePercent.ToString(), HorizontalAlignment = HorizontalAlignment.Left };
        var none = new ResourceEntry { Id = -1, Name = "(None)" };
        List<ResourceEntry> Sounds() { var list = new List<ResourceEntry> { none }; list.AddRange(_resourceDatabase.Entries.Where(r => r.Kind == ResourceKind.Sound)); return list; }
        var rainSounds = Sounds();
        var rain = new ComboBox { ItemsSource = rainSounds, DisplayMemberPath = "Name", SelectedItem = rainSounds.FirstOrDefault(s => s.Id == w.RainSoundResourceId) ?? none };
        var thunderSounds = Sounds();
        var thunder = new ComboBox { ItemsSource = thunderSounds, DisplayMemberPath = "Name", SelectedItem = thunderSounds.FirstOrDefault(s => s.Id == w.ThunderSoundResourceId) ?? none };
        rows.Children.Add(Label("Weather"));
        rows.Children.Add(kind);
        rows.Children.Add(Label("How heavy"));
        rows.Children.Add(intensity);
        rows.Children.Add(clouds);
        rows.Children.Add(Label("How cloudy"));
        rows.Children.Add(cover);
        rows.Children.Add(Label("Wind (clouds drift and rain slants this way)"));
        rows.Children.Add(wind);
        rows.Children.Add(changes);
        rows.Children.Add(Label("Chance each hour it turns (%)"));
        rows.Children.Add(chance);
        rows.Children.Add(Label("Rain sound (loops while it rains)"));
        rows.Children.Add(rain);
        rows.Children.Add(Label("Thunder (after each lightning flash in a storm)"));
        rows.Children.Add(thunder);
        rows.Children.Add(new TextBlock
        {
            Text = "As Exult's weather: rain, snow and hail aren't seen under a roof; a cloudy or rainy day is darker (System Settings > Day & Night > Overcast). An event can change it (Change Weather).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 10, 0, 0)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            w.Kind = (WeatherKind)kind.SelectedItem;
            w.Intensity = (int)Math.Round(intensity.Value);
            w.Clouds = clouds.IsChecked == true;
            w.CloudCover = (int)Math.Round(cover.Value);
            w.Wind = (SpriteDirection)wind.SelectedItem;
            w.Changes = changes.IsChecked == true;
            if (int.TryParse(chance.Text.Trim(), out int c)) w.ChangeChancePercent = Math.Clamp(c, 0, 100);
            w.RainSoundResourceId = (rain.SelectedItem as ResourceEntry)?.Id ?? -1;
            w.ThunderSoundResourceId = (thunder.SelectedItem as ResourceEntry)?.Id ?? -1;
            window.DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        window.Content = rows;
        if (window.ShowDialog() == true) StatusText.Text = $"This map's weather: {w.Kind}{(w.Clouds ? ", cloudy" : "")}{(w.Changes ? " (it comes and goes)" : "")}.";
    }
}
