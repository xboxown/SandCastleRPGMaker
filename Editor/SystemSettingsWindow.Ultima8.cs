using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>System Settings > Ultima 8 (only in an Ultima 8 project): what its engine shows and how it moves.</summary>
public partial class SystemSettingsWindow
{
    private UIElement BuildUltima8Tab()
    {
        var u8 = _settings.Ultima8;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = "Only this Ultima 8 project's engine uses these.", Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10) });

        panel.Children.Add(new TextBlock { Text = "The paperdoll's stats (as Ultima 8 writes them beside the body)", FontWeight = FontWeights.Bold });
        var show = new CheckBox { Content = "Show the stats on the paperdoll", IsChecked = u8.ShowPaperdollStats, Margin = new Thickness(0, 4, 0, 4) };
        show.Checked += (_, _) => u8.ShowPaperdollStats = true;
        show.Unchecked += (_, _) => u8.ShowPaperdollStats = false;
        panel.Children.Add(show);
        string[] meaning = { "Strength", "Intelligence", "Dexterity", "Armour class", "Hit points", "Mana", "Weight carried" };
        while (u8.StatLabels.Count < meaning.Length) u8.StatLabels.Add(string.Empty);
        for (int i = 0; i < meaning.Length; i++)
        {
            int index = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock { Text = meaning[i], Width = 120, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { Text = u8.StatLabels[i], Width = 120 };
            box.TextChanged += (_, _) => u8.StatLabels[index] = box.Text;
            row.Children.Add(box);
            panel.Children.Add(row);
        }
        var where = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        where.Children.Add(Number("Labels at x", () => u8.StatsLabelX, v => u8.StatsLabelX = v));
        where.Children.Add(Number("Values end at x", () => u8.StatsValueX, v => u8.StatsValueX = v));
        where.Children.Add(Number("First line at y", () => u8.StatsTopY, v => u8.StatsTopY = v));
        where.Children.Add(Number("Line height", () => u8.StatsLineHeight, v => u8.StatsLineHeight = Math.Max(1, v)));
        where.Children.Add(Number("Weight shown divided by", () => u8.WeightDivisor, v => u8.WeightDivisor = Math.Max(1, v)));
        panel.Children.Add(where);
        panel.Children.Add(new TextBlock { Text = "In the paperdoll's own 320 x 400 layout (the Character Editor's paperdoll).", Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) });

        panel.Children.Add(new TextBlock { Text = "Moving with the mouse (the right button held)", FontWeight = FontWeights.Bold });
        var mouse = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        mouse.Children.Add(Number("Walk beyond (of 255)", () => u8.WalkThreshold, v => u8.WalkThreshold = Math.Clamp(v, 0, 255)));
        mouse.Children.Add(Number("Run beyond (of 255)", () => u8.RunThreshold, v => u8.RunThreshold = Math.Clamp(v, 0, 255)));
        panel.Children.Add(mouse);
        panel.Children.Add(new TextBlock { Text = "How far the pointer is from the Avatar, out of 255 of half the view: nearer than Walk is a step, beyond Run is a run (Pentagram's 50 and 160).", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });

        panel.Children.Add(new TextBlock { Text = "Conversations (words over the speaker's head, answers over the Avatar)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
        var talk = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        talk.Children.Add(Number("Words wrap at (pixels)", () => u8.BarkWidth, v => u8.BarkWidth = Math.Clamp(v, 40, 320)));
        talk.Children.Add(Number("Answer rows (pixels)", () => u8.AskWidth, v => u8.AskWidth = Math.Clamp(v, 40, 320)));
        talk.Children.Add(Number("Time per letter (ms)", () => u8.BarkMsPerChar, v => u8.BarkMsPerChar = Math.Max(0, v)));
        talk.Children.Add(Number("At least (ms)", () => u8.BarkMinMs, v => u8.BarkMinMs = Math.Max(200, v)));
        panel.Children.Add(talk);
        panel.Children.Add(new TextBlock { Text = "A page with no answers goes on by itself after its time (or a click). Pentagram's BarkGump is 194 wide, its AskGump rows 160. Narration (nobody on the map speaking) keeps its box.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        static FrameworkElement Number(string label, Func<int> get, Action<int> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2) };
            row.Children.Add(new TextBlock { Text = label + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var box = new TextBox { Text = get().ToString(), Width = 50 };
            box.LostFocus += (_, _) => { if (int.TryParse(box.Text, out int v)) set(v); box.Text = get().ToString(); };
            row.Children.Add(box);
            return row;
        }
    }
}
