using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>System Settings > Weapon in hand: Exult's 4 frames (the default), or smooth ones - and how many swing frames. The Sprite Editor's Weapon in hand group follows it.</summary>
public partial class SystemSettingsWindow
{
    private UIElement BuildWeaponInHandTab()
    {
        var w = _settings.WeaponInHand;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "How a weapon held in the hand is drawn on the map (Exult's Actor::paint_weapon). Each weapon picks its look in the hand - a sprite in the Sprite Editor's Weapon in hand group - and each character frame has a hand point; the weapon is drawn with its grip on the hand.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10)
        });
        var exult = new RadioButton { Content = "As Exult: 4 frames - held, and the swing pointing north, east, south (mirrored for east / west)", GroupName = "wih", IsChecked = !w.Smooth, Margin = new Thickness(0, 2, 0, 2) };
        var smooth = new RadioButton { Content = "Smooth: each of the eight directions its own held frame and swing frames", GroupName = "wih", IsChecked = w.Smooth, Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(exult);
        panel.Children.Add(smooth);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 4, 0, 8) };
        row.Children.Add(new TextBlock { Text = "Swing frames in each direction (1-8):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var count = new TextBox { Text = w.SwingFrames.ToString(), Width = 40, IsEnabled = w.Smooth };
        row.Children.Add(count);
        panel.Children.Add(row);
        var frames = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };
        panel.Children.Add(frames);
        void Show()
        {
            var names = SpriteDatabase.WeaponInHandFrameNames(w.Smooth, w.SwingFrames);
            frames.Text = $"Each weapon-in-hand sprite has {names.Length} frames:\n" + string.Join("\n", names.Select((n, i) => $"  {i + 1}. {n}")) +
                          "\n\nChanging this changes what those frames are: a sprite already drawn keeps its pictures in the same order.";
        }
        exult.Checked += (_, _) => { w.Smooth = false; count.IsEnabled = false; Show(); };
        smooth.Checked += (_, _) => { w.Smooth = true; count.IsEnabled = true; Show(); };
        count.LostFocus += (_, _) => { if (int.TryParse(count.Text, out int n)) w.SwingFrames = Math.Clamp(n, 1, 8); count.Text = w.SwingFrames.ToString(); Show(); };
        Show();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
