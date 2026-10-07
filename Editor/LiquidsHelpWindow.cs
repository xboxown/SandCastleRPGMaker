using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// "How liquids work", with pictures: the liquid (a Food set to Liquid) says
/// what it does; each container (a Liquid Container) says how it looks full
/// of it; wells, troughs and animals say which liquid they give. Opened from
/// the Food panel, the Liquid Container panel, the Liquid Source box and the
/// Character Editor's milking box.
/// </summary>
public class LiquidsHelpWindow : Window
{
    private static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(0xFB, 0xF7, 0xEE));
    private static readonly Brush FoodColour = new SolidColorBrush(Color.FromRgb(0xE3, 0xF2, 0xFD));
    private static readonly Brush ContainerColour = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0));
    private static readonly Brush SourceColour = new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9));
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

    public LiquidsHelpWindow()
    {
        Title = "How liquids work";
        Width = 900;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Paper;

        var page = new StackPanel { Margin = new Thickness(18) };
        page.Children.Add(Title1("How liquids work"));
        page.Children.Add(Text("Three things work together. Once you see it, it's simple:"));
        page.Children.Add(Bullets(
            "THE LIQUID says what it DOES - a Food object set to Liquid (Milk, Water, Wine, Blood).",
            "THE CONTAINER says how it LOOKS full of it - a Liquid Container object (a wooden bucket, a steel bucket, a glass bottle).",
            "THE SOURCE says which liquid it GIVES - a well or trough (an object), or an animal (a cow, in the Character Editor)."));

        // ---------- Picture 1: one liquid, many containers ----------
        page.Children.Add(Title2("Picture 1 - one liquid, many containers"));
        page.Children.Add(Text("Milk is made once. Every container that can hold milk has its OWN picture of being full of it - but drinking the milk does the same from any of them, because that's set on Milk."));
        var picture1 = new Grid { Margin = new Thickness(0, 6, 0, 10) };
        picture1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        picture1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        picture1.ColumnDefinitions.Add(new ColumnDefinition());
        var milk = Card(FoodColour, "🥛", "Milk", "Object Editor > Food\n(o) Liquid", "Nutrition when drunk: 4\nEffects: (none)\nPuts out fires: no\nUndrinkable: no", "What it DOES");
        Grid.SetColumn(milk, 0);
        picture1.Children.Add(milk);
        var arrows = new Canvas { Width = 70, Height = 230 };
        foreach (double y in new[] { 40.0, 115, 190 }) arrows.Children.Add(Arrow(4, 115, 64, y));
        Grid.SetColumn(arrows, 1);
        picture1.Children.Add(arrows);
        var containers = new StackPanel();
        containers.Children.Add(Card(ContainerColour, "🪣", "Wooden Bucket", "Object Editor > Liquid Container", "Look (empty): wooden bucket\nHolds: Water -> wooden bucket of water\nHolds: Milk  -> wooden bucket of milk", "How it LOOKS"));
        containers.Children.Add(Card(ContainerColour, "🪣", "Steel Bucket", "Object Editor > Liquid Container", "Look (empty): steel bucket\nHolds: Water -> steel bucket of water\nHolds: Milk  -> steel bucket of milk", "How it LOOKS"));
        containers.Children.Add(Card(ContainerColour, "🍾", "Glass Bottle", "Object Editor > Liquid Container", "Look (empty): empty bottle\nHolds: Milk -> bottle of milk\nHolds: Wine -> bottle of wine   (no Blood: it can't hold blood)", "How it LOOKS"));
        Grid.SetColumn(containers, 2);
        picture1.Children.Add(containers);
        page.Children.Add(picture1);

        // ---------- Picture 2: where liquids come from ----------
        page.Children.Add(Title2("Picture 2 - where liquids come from"));
        page.Children.Add(Text("Use an EMPTY container on a source and it fills - if the container can hold that liquid (it's on its list)."));
        var picture2 = new StackPanel { Margin = new Thickness(0, 6, 0, 10) };
        var wellRow = new StackPanel { Orientation = Orientation.Horizontal };
        wellRow.Children.Add(Card(SourceColour, "⛲", "Well", "Object Editor > Junk", "Liquid source: Water", "GIVES"));
        wellRow.Children.Add(FlowArrow("fills"));
        wellRow.Children.Add(Card(ContainerColour, "🪣", "Bucket of water", "", "(any bucket that holds Water)", ""));
        picture2.Children.Add(wellRow);
        var cowRow = new StackPanel { Orientation = Orientation.Horizontal };
        cowRow.Children.Add(Card(SourceColour, "🐄", "Cow", "Character Editor > Health", "Gives a liquid: Milk\nIt says: MoooOOOoooo", "GIVES"));
        cowRow.Children.Add(FlowArrow("milked"));
        cowRow.Children.Add(Card(ContainerColour, "🪣", "Bucket of milk", "", "(any bucket that holds Milk)", ""));
        picture2.Children.Add(cowRow);
        page.Children.Add(picture2);
        page.Children.Add(Text("Milking is Exult's: \"Hold still...\" (sometimes), three milking sounds while the cow stands still, then the bucket is full, the Avatar says \"That's a good girl!\" and the cow \"MoooOOOoooo\". A full bucket used on the cow: \"The bucket is full.\""));

        // ---------- Picture 3: what using a container does ----------
        page.Children.Add(Title2("Picture 3 - using a bucket in the game"));
        page.Children.Add(Text("Double-click the bucket, then click what to use it on (Exult's bucket.uc):"));
        var table = new Grid { Margin = new Thickness(0, 6, 0, 10) };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        table.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = new (string Click, string Happens)[]
        {
            ("🧍  Yourself", "You drink it: \"Ahhh, how refreshing.\" - its nutrition and effects. Empty: \"The bucket is empty.\" Undrinkable: \"No, thank thee.\""),
            ("🐄  An animal that gives a liquid", "Empty bucket: it's milked (filled). Full bucket: \"The bucket is full.\""),
            ("🧑  Anyone else", "They're doused - and woken: \"Hey, stop that!\" The bucket is emptied."),
            ("⛲  A well, trough or cask", "Empty bucket: filled with its liquid. Full: \"The bucket is full.\""),
            ("🔥  A lit torch or lamp, a lit fuse", "A liquid that Puts Out Fires (water) puts it out, and the bucket is emptied."),
            ("⬛  The floor (anything else)", "It's spilt - leaving a puddle if you set \"A spill leaves\"."),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            table.RowDefinitions.Add(new RowDefinition());
            var click = new Border { Background = i % 2 == 0 ? ContainerColour : Brushes.White, Padding = new Thickness(6, 4, 6, 4), Child = new TextBlock { Text = rows[i].Click, FontWeight = FontWeights.SemiBold, Foreground = Ink } };
            var happens = new Border { Background = i % 2 == 0 ? ContainerColour : Brushes.White, Padding = new Thickness(6, 4, 6, 4), Child = new TextBlock { Text = rows[i].Happens, TextWrapping = TextWrapping.Wrap, Foreground = Ink } };
            Grid.SetRow(click, i); Grid.SetRow(happens, i); Grid.SetColumn(happens, 1);
            table.Children.Add(click);
            table.Children.Add(happens);
        }
        page.Children.Add(table);

        // ---------- Step by step ----------
        page.Children.Add(Title2("Step by step - a well, a cow and a bucket"));
        page.Children.Add(Numbered(
            "Sprite Editor: draw the empty bucket, the bucket of water and the bucket of milk (three sprites).",
            "Object Editor > New Object > Food: name it \"Water\", choose (o) Liquid, tick \"Puts out fires\".",
            "Object Editor > New Object > Food: name it \"Milk\", choose (o) Liquid, Nutrition 4.",
            "Object Editor > New Object > Liquid Container: name it \"Bucket\"; Look = the empty bucket. Add a liquid it can hold: Water, with the bucket-of-water picture. Add another: Milk, with the bucket-of-milk picture.",
            "Object Editor > New Object > Junk: name it \"Well\"; at the bottom, Liquid source: Water.",
            "Character Editor: your cow; Health tab > Gives a liquid: Milk.",
            "Map Editor: place the bucket, the well and the cow. (Right-click the bucket > Starts full of, if you like.)",
            "Game Test: double-click the bucket, click the well - a bucket of water. Click yourself - you drink it. Click the cow - a bucket of milk."));
        page.Children.Add(Title2("Good to know"));
        page.Children.Add(Bullets(
            "A container only holds the liquids on its list - leave Blood off the glass bottle and it can't be filled with blood (\"... can't hold blood\").",
            "Identify a full bucket and it's \"Bucket of milk\".",
            "A full bucket keeps its liquid wherever it goes: in a backpack, in a chest, back on the floor.",
            "Rename Milk and every bucket, well and cow still knows it - they point at the Food object, not at its name."));

        var close = new Button { Content = "Got it", Width = 100, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        page.Children.Add(close);
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ---------- Drawing pieces ----------

    private static TextBlock Title1(string text) => new() { Text = text, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 6) };
    private static TextBlock Title2(string text) => new() { Text = text, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 14, 0, 4) };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 2, 0, 2) };

    private static StackPanel Bullets(params string[] lines)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 2, 0, 4) };
        foreach (var line in lines) panel.Children.Add(new TextBlock { Text = "•  " + line, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 1, 0, 1) });
        return panel;
    }

    private static StackPanel Numbered(params string[] lines)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 2, 0, 4) };
        for (int i = 0; i < lines.Length; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var number = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.SteelBlue, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);
            row.Children.Add(new TextBlock { Text = lines[i], TextWrapping = TextWrapping.Wrap, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }
        return panel;
    }

    /// <summary>A picture card: a big icon, a name, where it's made, its settings, and a tag (what it's for).</summary>
    private static Border Card(Brush colour, string icon, string name, string where, string settings, string tag)
    {
        var panel = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = icon, FontSize = 30, FontFamily = new FontFamily("Segoe UI Emoji"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold, FontSize = 14, Foreground = Ink });
        if (where.Length > 0) names.Children.Add(new TextBlock { Text = where, FontSize = 10, Foreground = Brushes.DimGray });
        top.Children.Add(names);
        panel.Children.Add(top);
        if (settings.Length > 0) panel.Children.Add(new TextBlock { Text = settings, FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Ink, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (tag.Length > 0) panel.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = tag, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.SteelBlue } });
        return new Border { Background = colour, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 3, 0, 3), Child = panel, MinWidth = 190, VerticalAlignment = VerticalAlignment.Center };
    }

    private static UIElement FlowArrow(string label)
    {
        var canvas = new Canvas { Width = 80, Height = 60, VerticalAlignment = VerticalAlignment.Center };
        canvas.Children.Add(Arrow(6, 34, 74, 34));
        var text = new TextBlock { Text = label, FontSize = 11, Foreground = Brushes.SteelBlue, FontWeight = FontWeights.SemiBold };
        Canvas.SetLeft(text, 18);
        Canvas.SetTop(text, 12);
        canvas.Children.Add(text);
        return canvas;
    }

    private static Shape Arrow(double x1, double y1, double x2, double y2)
    {
        double angle = Math.Atan2(y2 - y1, x2 - x1), head = 9;
        var geometry = new PathGeometry();
        var line = new PathFigure { StartPoint = new Point(x1, y1) };
        line.Segments.Add(new LineSegment(new Point(x2, y2), true));
        geometry.Figures.Add(line);
        var tip = new PathFigure { StartPoint = new Point(x2 - head * Math.Cos(angle - 0.45), y2 - head * Math.Sin(angle - 0.45)) };
        tip.Segments.Add(new LineSegment(new Point(x2, y2), true));
        tip.Segments.Add(new LineSegment(new Point(x2 - head * Math.Cos(angle + 0.45), y2 - head * Math.Sin(angle + 0.45)), true));
        geometry.Figures.Add(tip);
        return new Path { Data = geometry, Stroke = Brushes.SteelBlue, StrokeThickness = 2 };
    }
}
