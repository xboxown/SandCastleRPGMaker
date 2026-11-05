using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Sprite Editor's Water Splash and Lava Flare groups (Ultima 8 projects): what a sprite there is, how the game plays it, with a moving
/// picture of a small and a big splash, and the steps from tile to game.
/// </summary>
public sealed class SplashGuideWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly bool _lava;

    public SplashGuideWindow(bool lava)
    {
        _lava = lava;
        Title = lava ? "Lava Flare Guide" : "Water Splash Guide";
        Width = 820;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(Words(lava ? "Lava's flare" : "Water's splash", bold: true, size: 16));
        panel.Children.Add(Words(lava
            ? "A sprite here is what shows when something falls into lava, or someone walks into it: a burst of flame. Ultima 8 projects only."
            : "A sprite here is what shows when something comes down in real water: a thing thrown in, a piece of a burst monster, someone who drowns. Ultima 8 projects only."));
        panel.Children.Add(Words("What the game does", bold: true));
        var demo = new Canvas { Width = 760, Height = 170, Background = new SolidColorBrush(lava ? Color.FromRgb(60, 20, 10) : Color.FromRgb(20, 50, 90)), ClipToBounds = true, Margin = new Thickness(0, 4, 0, 8) };
        panel.Children.Add(demo);
        BuildDemo(demo);
        panel.Children.Add(Words("Step by step", bold: true));
        foreach (string step in lava ? LavaSteps : WaterSteps) panel.Children.Add(Words(step));
        panel.Children.Add(Words("Drawing it", bold: true));
        foreach (string tip in new[]
        {
            "- Its frames are an animation, played once from the first frame to the last, then it's gone. 6 to 10 frames at 10 to 15 a second look right.",
            "- Draw it for something small: about one map square wide. The game scales it up for bigger things - a chest, a character - up to 4 times, so keep its edges clean.",
            "- Its feet are the bottom middle of the picture: draw the splash rising from there.",
            "- Frame sounds play as usual; or leave them silent and set the sounds in System Settings > Ultima 8 (a small and a big splash).",
            "- No sprite at all: the game throws up drops of " + (lava ? "fire" : "water") + " and a ring, as in the picture above, so the game works before you draw one."
        }) panel.Children.Add(Words(tip));
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        Closed += (_, _) => _timer.Stop();
    }

    private static readonly string[] WaterSteps =
    {
        "1. Tileset > Tile Properties of your water tile: under Ultima 8, choose Real water. Untick Walkable so nobody walks in by accident.",
        "2. Here: New Sprite, draw the splash (the tips below). The first sprite in this group is the one used, unless System Settings > Ultima 8 > Water and lava names another.",
        "3. System Settings > Ultima 8 > Water and lava: the splash sounds, small and big.",
        "4. In play: anyone at the ground over real water drowns and is gone for good - no body, nothing they carried. Things thrown or falling in splash and are lost. Up on a platform, a bridge or a boat, everyone is safe.",
        "5. What you placed in the water in the Map Editor (a bridge's posts, a moored boat) stays: only what comes down in it in play is lost."
    };

    private static readonly string[] LavaSteps =
    {
        "1. Tileset > Tile Properties of your lava tile: under Ultima 8, choose Lava. Untick Walkable so nobody walks in by accident.",
        "2. Here: New Sprite, draw the flare (the tips below). The first sprite in this group is the one used, unless System Settings > Ultima 8 > Water and lava names another.",
        "3. System Settings > Ultima 8 > Water and lava: how much lava burns, how often, and its sound.",
        "4. In play: anyone in lava burns (Fire damage - a character set Immune to Fire in Character Editor > Health walks through it). Things that fall in burn away; a body left in it burns up.",
    };

    /// <summary>A small thing and then a big one go in: the small splash and the big one, over and over.</summary>
    private void BuildDemo(Canvas demo)
    {
        var color = _lava ? Color.FromRgb(255, 140, 30) : Color.FromRgb(180, 215, 255);
        var label = new TextBlock { Foreground = Brushes.White, FontSize = 13 };
        Canvas.SetLeft(label, 10); Canvas.SetTop(label, 142);
        demo.Children.Add(label);
        double t = 0;
        var parts = new List<(Shape Shape, double X, double VX, double VY, bool Ring)>();
        var rng = new Random(5);
        void Burst(double x, double size)
        {
            foreach (var p in parts) demo.Children.Remove(p.Shape);
            parts.Clear();
            var ring = new Ellipse { Stroke = new SolidColorBrush(color), StrokeThickness = 2 };
            demo.Children.Add(ring);
            parts.Add((ring, x, 0, 0, true));
            for (int i = 0; i < 6 + 6 * size; i++)
            {
                var drop = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(color) };
                demo.Children.Add(drop);
                double angle = rng.NextDouble() * Math.PI;
                parts.Add((drop, x, Math.Cos(angle) * 60 * size, 90 + rng.Next(80) * size, false));
            }
        }
        double size = 1, x = 200;
        Burst(x, size);
        _timer.Tick += (_, _) =>
        {
            t += 0.04;
            if (t > 1.4) { t = 0; size = size == 1 ? 2.5 : 1; x = size == 1 ? 200 : 520; Burst(x, size); }
            label.Text = size == 1 ? "Something small goes in: a small splash" : "Something big (a chest, a character): a big splash";
            foreach (var (shape, px, vx, vy, isRing) in parts)
            {
                if (isRing)
                {
                    double grow = 1 + 2 * t;
                    shape.Width = 40 * size * grow; shape.Height = 14 * size * grow;
                    Canvas.SetLeft(shape, px - shape.Width / 2); Canvas.SetTop(shape, 110 - shape.Height / 2);
                }
                else
                {
                    double up = Math.Max(0, vy * t - 160 * t * t);
                    Canvas.SetLeft(shape, px + vx * t); Canvas.SetTop(shape, 110 - up);
                }
                shape.Opacity = Math.Max(0, 1 - t / 1.4);
            }
        };
        _timer.Start();
    }

    private static TextBlock Words(string text, bool bold = false, double size = 13) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FontSize = size, Margin = new Thickness(0, bold ? 8 : 2, 0, 2)
    };
}
