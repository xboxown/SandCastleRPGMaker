using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Sprite Editor's Crosshair group (Ultima 8 projects): Crusader's crosshair - what a sprite there is, a moving picture of it
/// settling on a foe and moving on with the Next Target key, and the steps from sprite to game.
/// </summary>
public sealed class CrosshairGuideWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };

    public CrosshairGuideWindow()
    {
        Title = "Crosshair Guide";
        Width = 820;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(Words("The crosshair", bold: true, size: 16));
        panel.Children.Add(Words("A sprite here is Crusader's crosshair: in combat mode it sits on the one the player will hit or shoot. Ultima 8 projects only."));
        panel.Children.Add(Words("What the game does", bold: true));
        var demo = new Canvas { Width = 760, Height = 190, Background = new SolidColorBrush(Color.FromRgb(40, 40, 48)), ClipToBounds = true, Margin = new Thickness(0, 4, 0, 8) };
        panel.Children.Add(demo);
        BuildDemo(demo);
        panel.Children.Add(Words("Step by step", bold: true));
        foreach (string step in new[]
        {
            "1. Here: New Sprite, draw the crosshair (the tips below). The first sprite in this group is the one used, unless System Settings > Ultima 8 > Crusader's crosshair names another.",
            "2. System Settings > Ultima 8 > Crusader's crosshair: tick \"In combat mode, a crosshair on the target\", and how far it looks for one (12 map cells to start with).",
            "3. System Settings > Ultima 8 > The keyboard: Attack (A), Crosshair: next target (T) - change them if you like.",
            "4. In play: combat mode on (C). The crosshair settles on the nearest foe in front of the player, else the nearest foe, else whoever is in front. T moves it to the next one, nearest first. A swing, a kick or a shot - the mouse or the Attack key - goes to it: a bow shoots it from afar, a sword turns to it and strikes when it's in reach.",
            "5. Party members, the main character and the dead are never under it; nor anyone off the screen or on another floor."
        }) panel.Children.Add(Words(step));
        panel.Children.Add(Words("Drawing it", bold: true));
        foreach (string tip in new[]
        {
            "- Its middle goes on the middle of the target: draw it centred in its picture. 16 to 32 pixels across suits a character.",
            "- Its frames loop while it shows: 2 to 4 frames turning or pulsing look right; one frame is fine too.",
            "- Leave the rest of the picture see-through: only the ring, the ticks or the corners show.",
            "- No sprite at all: the game draws a red ring with four ticks, as in the picture above, so the game works before you draw one."
        }) panel.Children.Add(Words(tip));
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>The player and three foes; the crosshair settles on the nearest and steps on to the next, as T does.</summary>
    private void BuildDemo(Canvas demo)
    {
        var player = new Ellipse { Width = 22, Height = 34, Fill = Brushes.SteelBlue };
        Canvas.SetLeft(player, 90); Canvas.SetTop(player, 90);
        demo.Children.Add(player);
        var foes = new List<(double X, double Y)> { (330, 70), (480, 120), (640, 60) };
        foreach (var (x, y) in foes)
        {
            var foe = new Ellipse { Width = 22, Height = 34, Fill = Brushes.IndianRed };
            Canvas.SetLeft(foe, x); Canvas.SetTop(foe, y);
            demo.Children.Add(foe);
        }
        var red = new SolidColorBrush(Color.FromRgb(255, 40, 40));
        var cross = new Canvas { Width = 32, Height = 32 };
        cross.Children.Add(new Ellipse { Width = 22, Height = 22, Stroke = red, StrokeThickness = 2, Margin = new Thickness(5, 5, 0, 0) });
        foreach (var (x1, y1, x2, y2) in new[] { (16.0, 0.0, 16.0, 9.0), (16.0, 23.0, 16.0, 32.0), (0.0, 16.0, 9.0, 16.0), (23.0, 16.0, 32.0, 16.0) })
            cross.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = red, StrokeThickness = 2 });
        demo.Children.Add(cross);
        var label = new TextBlock { Foreground = Brushes.White, FontSize = 13 };
        Canvas.SetLeft(label, 10); Canvas.SetTop(label, 162);
        demo.Children.Add(label);
        int at = 0;
        double t = 0, cx = foes[0].X - 5, cy = foes[0].Y + 1;
        _timer.Tick += (_, _) =>
        {
            t += 0.04;
            if (t > 1.6) { t = 0; at = (at + 1) % foes.Count; }
            var (fx, fy) = foes[at];
            cx += (fx - 5 - cx) * 0.25; cy += (fy + 1 - cy) * 0.25;
            Canvas.SetLeft(cross, cx); Canvas.SetTop(cross, cy);
            cross.Opacity = 0.75 + 0.25 * Math.Sin(t * 10);
            label.Text = at == 0 ? "Combat mode: it settles on the nearest foe" : $"T (Next target): on to the next one ({at + 1} of {foes.Count})";
        };
        _timer.Start();
    }

    private static TextBlock Words(string text, bool bold = false, double size = 13) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FontSize = size, Margin = new Thickness(0, bold ? 8 : 2, 0, 2)
    };
}
