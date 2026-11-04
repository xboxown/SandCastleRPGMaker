using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Sprite Editor's Joined Monsters group: how to draw a monster that joins up with its own kind (Ultima 7's slimes, a zombie mass,
/// an alien growth), with a moving picture of what the game does and a table of the 32 frames.
/// </summary>
public sealed class JoiningGuideWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(60) };

    public JoiningGuideWindow()
    {
        Title = "Joined Monsters Guide";
        Width = 860;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(Words("Monsters that join up", bold: true, size: 16));
        panel.Children.Add(Words("A sprite here is one monster as it looks joined to its own kind: not only slimes, any monster. Ultima 7's slimes work this way (Exult's Slime_actor). Each monster stays its own character: you still hit and kill them one by one, however much they look like one big thing."));

        panel.Children.Add(Words("What the game does", bold: true));
        var demo = new Canvas { Width = 780, Height = 170, Background = new SolidColorBrush(Color.FromRgb(38, 52, 34)), ClipToBounds = true, Margin = new Thickness(0, 4, 0, 8) };
        panel.Children.Add(demo);
        BuildDemo(demo);

        panel.Children.Add(Words("Step by step", bold: true));
        foreach (string step in new[]
        {
            "1. Make the sprite here: New Sprite in this group. Give it 32 frames, all the same size: as wide and tall as one square of your map, so neighbours meet (16 frames also works, without the wobble).",
            "2. Draw each frame with bulges reaching out on the sides the table below says. Two monsters side by side must meet in the middle of their shared edge, so the seam can't be seen.",
            "3. Each pair of frames is the same join drawn two ways (frame 0 and 1, frame 2 and 3...). The game flips between them as it moves, so it wobbles.",
            "4. Character Editor > the monster > Health > Joining: pick this sprite as its Joined look.",
            "5. In play, each monster shows the frame for which sides have another of its own kind right next to it: one map square away, or 16 pixels if your squares are smaller. Diagonals don't count. The frames change by themselves as they move.",
            "6. To make them grow: in the same place choose What makes a new one. Ultima 7 / Exult: a hit it survives may split it, with no limit. Our engine: being near its own kind, by a multiplies rule. Or Both. To fuse: add a rule, and both go and a different monster stands there (body parts forming a horror).",
            "7. For a trail: the Slime Trail group holds the puddles. Pick one as the monster's Trail, and tick whether it hurts to step on.",
        }) panel.Children.Add(Words(step));

        panel.Children.Add(Words("Which frame shows what (a neighbour on that side)", bold: true));
        var table = new WrapPanel { MaxWidth = 800 };
        for (int mask = 0; mask < 16; mask++) table.Children.Add(Cell(mask));
        panel.Children.Add(table);
        panel.Children.Add(Words("Frame number = 2 if north + 4 if east + 8 if south + 16 if west, plus 1 for its second drawing."));
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Two slimes creep together: their sides bulge to meet as they come within two tiles; a third joins them.</summary>
    private void BuildDemo(Canvas demo)
    {
        const double size = 44;
        var blobs = new[] { new Blob(demo, 90, 60, size), new Blob(demo, 380, 60, size), new Blob(demo, 690, 60, size) };
        var label = new TextBlock { Foreground = Brushes.White, FontSize = 13 };
        Canvas.SetLeft(label, 10);
        Canvas.SetTop(label, 142);
        demo.Children.Add(label);
        double t = 0;
        _timer.Tick += (_, _) =>
        {
            t = (t + 0.006) % 1;
            double near = Math.Min(1, t * 2.2), third = Math.Max(0, Math.Min(1, (t - 0.45) * 2.6));
            blobs[0].X = 90 + (250 - 90) * near;
            blobs[1].X = 380 - (380 - (250 + size)) * near;
            blobs[2].X = 690 - (690 - (250 + 2 * size)) * third;
            bool firstTwo = near >= 1, allThree = third >= 1;
            blobs[0].Draw(east: firstTwo, west: false, t);
            blobs[1].Draw(east: allThree, west: firstTwo, t + 0.3);
            blobs[2].Draw(east: false, west: allThree, t + 0.6);
            label.Text = allThree ? "All touching: the left shows frame 4 (east), the middle frame 20 (east + west), the right frame 16 (west). Three characters, one mass."
                : firstTwo ? "Two touching: the left shows frame 4 (east), the right frame 16 (west). Each still has its own health."
                : "Apart: each shows frame 0 or 1, no bulges.";
        };
        _timer.Start();
    }

    private sealed class Blob
    {
        private readonly Ellipse _body, _east, _west;
        private readonly double _y, _size;
        public double X;

        public Blob(Canvas host, double x, double y, double size)
        {
            X = x; _y = y; _size = size;
            var fill = new SolidColorBrush(Color.FromRgb(120, 190, 70));
            _body = new Ellipse { Width = size, Height = size, Fill = fill };
            _east = new Ellipse { Width = size * 0.7, Height = size * 0.6, Fill = fill };
            _west = new Ellipse { Width = size * 0.7, Height = size * 0.6, Fill = fill };
            host.Children.Add(_east);
            host.Children.Add(_west);
            host.Children.Add(_body);
        }

        public void Draw(bool east, bool west, double t)
        {
            double wobble = Math.Sin(t * 60) * 2;
            Canvas.SetLeft(_body, X);
            Canvas.SetTop(_body, _y + wobble);
            _east.Visibility = east ? Visibility.Visible : Visibility.Collapsed;
            _west.Visibility = west ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(_east, X + _size * 0.65);
            Canvas.SetTop(_east, _y + _size * 0.2 + wobble);
            Canvas.SetLeft(_west, X - _size * 0.35);
            Canvas.SetTop(_west, _y + _size * 0.2 + wobble);
        }
    }

    private static FrameworkElement Cell(int mask)
    {
        int frame = mask * 2;
        var picture = new Canvas { Width = 54, Height = 54, Background = new SolidColorBrush(Color.FromRgb(38, 52, 34)) };
        var fill = new SolidColorBrush(Color.FromRgb(120, 190, 70));
        void Part(double x, double y, double w, double h)
        {
            var part = new Ellipse { Width = w, Height = h, Fill = fill };
            Canvas.SetLeft(part, x);
            Canvas.SetTop(part, y);
            picture.Children.Add(part);
        }
        Part(15, 15, 24, 24);
        if ((mask & 1) != 0) Part(19, 0, 16, 26);
        if ((mask & 2) != 0) Part(28, 19, 26, 16);
        if ((mask & 4) != 0) Part(19, 28, 16, 26);
        if ((mask & 8) != 0) Part(0, 19, 26, 16);
        string sides = string.Join(" ", new[] { (1, "N"), (2, "E"), (4, "S"), (8, "W") }.Where(s => (mask & s.Item1) != 0).Select(s => s.Item2));
        var cell = new StackPanel { Margin = new Thickness(4), Width = 86 };
        cell.Children.Add(picture);
        cell.Children.Add(new TextBlock { Text = $"Frames {frame}, {frame + 1}", FontSize = 11 });
        cell.Children.Add(new TextBlock { Text = sides.Length == 0 ? "alone" : sides, FontSize = 11, Opacity = 0.7 });
        return cell;
    }

    private static TextBlock Words(string text, bool bold = false, double size = 12) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, bold ? 10 : 2, 0, 3)
    };
}
