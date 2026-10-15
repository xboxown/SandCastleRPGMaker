using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>System Settings > Day &amp; Night: Exult's time-of-day palettes - when dawn and dusk come, and the night's and the dusk's colours.</summary>
public partial class SystemSettingsWindow
{
    private UIElement BuildDayNightTab()
    {
        var d = _settings.DayNight;
        var panel = new StackPanel { Margin = new Thickness(10) };
        var enabled = new CheckBox { Content = "Day and night follow the game clock", IsChecked = d.Enabled, Margin = new Thickness(0, 0, 0, 8) };
        enabled.Checked += (_, _) => d.Enabled = true;
        enabled.Unchecked += (_, _) => d.Enabled = false;
        panel.Children.Add(enabled);

        FrameworkElement Hour(string label, Func<int> get, Action<int> set)
        {
            var box = new TextBox { Width = 40, Text = get().ToString() };
            box.LostFocus += (_, _) => { if (int.TryParse(box.Text.Trim(), out int h)) set(Math.Clamp(h, 0, 23)); box.Text = get().ToString(); };
            var row = new StackPanel { Margin = new Thickness(0, 0, 14, 4) };
            row.Children.Add(new TextBlock { Text = label, FontSize = 11 });
            row.Children.Add(box);
            return row;
        }
        var hours = new WrapPanel();
        hours.Children.Add(Hour("Dawn starts (hour)", () => d.DawnStartHour, v => d.DawnStartHour = v));
        hours.Children.Add(Hour("Full day from", () => d.DayStartHour, v => d.DayStartHour = v));
        hours.Children.Add(Hour("Dusk starts", () => d.DuskStartHour, v => d.DuskStartHour = v));
        hours.Children.Add(Hour("Full night from", () => d.NightStartHour, v => d.NightStartHour = v));
        panel.Children.Add(hours);

        FrameworkElement Colour(string label, Func<string> get, Action<string> set, Func<double> strength, Action<double> setStrength)
        {
            var swatch = new Rectangle { Width = 22, Height = 18, Stroke = Brushes.Gray, Margin = new Thickness(6, 0, 0, 0) };
            void Show()
            {
                try { swatch.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(get())) { Opacity = Math.Max(0.15, strength()) }; }
                catch { swatch.Fill = Brushes.Transparent; }
            }
            var box = new TextBox { Width = 80, Text = get() };
            box.LostFocus += (_, _) => { set(box.Text.Trim().StartsWith('#') ? box.Text.Trim() : "#" + box.Text.Trim()); Show(); };
            var amount = new TextBox { Width = 50, Text = strength().ToString("0.##") };
            amount.LostFocus += (_, _) => { if (double.TryParse(amount.Text.Trim(), out double v)) setStrength(Math.Clamp(v, 0, 1)); amount.Text = strength().ToString("0.##"); Show(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box);
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock { Text = "  strength (0-1):", VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(amount);
            Show();
            return row;
        }
        panel.Children.Add(Colour("Night colour", () => d.NightColor, v => d.NightColor = v, () => d.NightStrength, v => d.NightStrength = v));
        panel.Children.Add(Colour("Dawn / dusk colour", () => d.DuskColor, v => d.DuskColor = v, () => d.DuskStrength, v => d.DuskStrength = v));
        var overcast = new TextBox { Width = 50, Text = d.OvercastStrength.ToString("0.##") };
        overcast.LostFocus += (_, _) => { if (double.TryParse(overcast.Text.Trim(), out double v)) d.OvercastStrength = Math.Clamp(v, 0, 1); overcast.Text = d.OvercastStrength.ToString("0.##"); };
        var overcastRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        overcastRow.Children.Add(new TextBlock { Text = "Overcast (cloud, rain, storm)", Width = 150, VerticalAlignment = VerticalAlignment.Center });
        overcastRow.Children.Add(overcast);
        overcastRow.Children.Add(new TextBlock { Text = "  how much darker a full storm makes it", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        panel.Children.Add(overcastRow);
        var reset = new Button { Content = "Ultima VII's colours", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        reset.Click += (_, _) =>
        {
            var fresh = new DayNightSettings();
            d.NightColor = fresh.NightColor; d.NightStrength = fresh.NightStrength; d.DuskColor = fresh.DuskColor; d.DuskStrength = fresh.DuskStrength;
            MessageBox.Show(this, "Back to the night and dusk colours taken from Ultima VII's PALETTES.FLX. Close and reopen System Settings to see them here.", "Day & Night");
        };
        panel.Children.Add(reset);
        panel.Children.Add(new TextBlock
        {
            Text = "As Exult's time-of-day palettes: from Dawn the night lifts through the dawn colour to day; from Dusk the day falls through the dusk colour to night. " +
                   "In the dark, light sources lift it (Object Editor > Light Source > Light gives: Exult's light levels, the dusk look, or full daylight). " +
                   "The Change Day/Night event command sets the sky until the next hour. Each map's weather: Edit > Map Weather.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 10, 0, 0)
        });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
