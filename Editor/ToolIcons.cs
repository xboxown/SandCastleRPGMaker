using System.Windows;
using System.Windows.Media;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The toolbar's editor icons (Tools menu), drawn as vector pictures on a
/// 24 x 24 grid: bright cartoon colours, each with a dark outline, so they read
/// at a glance and stay sharp at any size.
/// </summary>
public static class ToolIcons
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Brush Fill(string top, string bottom)
    {
        var brush = new LinearGradientBrush(C(top), C(bottom), new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    private static Brush Solid(string hex)
    {
        var brush = new SolidColorBrush(C(hex));
        brush.Freeze();
        return brush;
    }

    private static Pen Outline(string hex, double width = 1)
    {
        var pen = new Pen(Solid(hex), width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private static GeometryDrawing Shape(string path, Brush? fill, Pen? pen) => new(fill, pen, Geometry.Parse(path));
    private static GeometryDrawing Line(string path, string hex, double width) => new(null, Outline(hex, width), Geometry.Parse(path));
    private static GeometryDrawing Oval(double x, double y, double rx, double ry, Brush? fill, Pen? pen) => new(fill, pen, new EllipseGeometry(new Point(x, y), rx, ry));

    /// <summary>A cog with <paramref name="teeth"/> teeth round (cx, cy).</summary>
    private static string Gear(double cx, double cy, double inner, double outer, int teeth)
    {
        var points = new List<string>();
        for (int i = 0; i < teeth * 4; i++)
        {
            double angle = Math.PI * 2 * (i + 0.5) / (teeth * 4);
            double r = i % 4 is 1 or 2 ? outer : inner;
            points.Add($"{cx + r * Math.Cos(angle):0.##},{cy + r * Math.Sin(angle):0.##}");
        }
        return "M" + string.Join(" L", points) + " Z";
    }

    /// <summary>The icon for one editor, by its name (the Tools menu's, without "...").</summary>
    public static DrawingImage For(string editor)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24)))); // the full 24 x 24, whatever is drawn
        foreach (var part in Parts(editor)) group.Children.Add(part);
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static IEnumerable<Drawing> Parts(string editor)
    {
        switch (editor)
        {
            case "Object Editor": // a crate, three faces lit
            {
                var pen = Outline("#7A3E00");
                yield return Shape("M12,3 L20.5,7 L12,11 L3.5,7 Z", Fill("#FFD08A", "#FFB347"), pen);
                yield return Shape("M3.5,7 L12,11 L12,21 L3.5,17 Z", Fill("#F59E2E", "#D97A0B"), pen);
                yield return Shape("M12,11 L20.5,7 L20.5,17 L12,21 Z", Fill("#C46A08", "#9C5005"), pen);
                yield return Line("M7.5,9 L7.5,18.5 M16.5,9 L16.5,18.5", "#7A3E00", 0.6);
                break;
            }
            case "Staircase Designer": // stone steps
                yield return Shape("M3,21 L3,17 L7,17 L7,13 L11,13 L11,9 L15,9 L15,5 L21,5 L21,21 Z", Fill("#D9DEE6", "#7D8693"), Outline("#3E4652"));
                yield return Line("M3.6,17.6 L7,17.6 M7.6,13.6 L11,13.6 M11.6,9.6 L15,9.6 M15.6,5.6 L20.4,5.6", "#FFFFFF", 0.9);
                yield return Line("M5,11.5 L10.5,6 M7.5,6 H10.5 V9", "#2F80ED", 1.5); // up the stairs
                break;
            case "Wall Designer": // bricks, with a window
                yield return Shape("M2,4.5 H22 V20.5 H2 Z", Fill("#E2654A", "#B23A22"), Outline("#5E1C0E"));
                yield return Line("M2,9.8 H22 M2,15.2 H22 M8,4.5 V9.8 M16,4.5 V9.8 M5,9.8 V15.2 M19,9.8 V15.2 M8,15.2 V20.5 M16,15.2 V20.5", "#F7DCC8", 0.9);
                yield return Shape("M9.5,11 H14.5 V15 H9.5 Z", Fill("#BFE8FF", "#5FB2E8"), Outline("#5E1C0E", 0.9));
                yield return Line("M12,11 V15 M9.5,13 H14.5", "#5E1C0E", 0.6);
                break;
            case "PaperDoll Editor": // a paper doll on its sheet
                yield return Shape("M3.5,2.5 H20.5 V21.5 H3.5 Z", Solid("#FFFDF5"), new Pen(Solid("#B0A48A"), 0.8) { DashStyle = new DashStyle(new double[] { 2, 1.5 }, 0) });
                yield return Shape("M7,10.5 Q12,8.5 17,10.5 L19,15 L16.6,15.6 L15.4,12.6 L15.4,20.5 L12.8,20.5 L12,16 L11.2,20.5 L8.6,20.5 L8.6,12.6 L7.4,15.6 L5,15 Z", Fill("#FF9CC2", "#E0508A"), Outline("#8A1F4E"));
                yield return Oval(12, 6.3, 2.8, 2.8, Fill("#FFE0C2", "#FFC08A"), Outline("#8A1F4E"));
                yield return Line("M10.2,13 H13.8", "#FFFFFF", 0.9);
                break;
            case "Common Events": // a cog with a spark: made once, run from anywhere
                yield return Shape(Gear(12, 12, 7.2, 9.6, 8), Fill("#7FA8FF", "#2F5BC2"), Outline("#1B2F6B"));
                yield return Oval(12, 12, 4.6, 4.6, Fill("#E9F0FF", "#BFD0FF"), Outline("#1B2F6B", 0.8));
                yield return Shape("M13,7 L9.4,12.8 L12,12.8 L11,17 L14.6,11.2 L12,11.2 Z", Fill("#FFE36B", "#F5B700"), Outline("#7A5200", 0.7));
                break;
            case "Title Screen": // a screen with a title and play
                yield return Shape("M2,3.5 H22 V17 H2 Z", Fill("#3B4150", "#22262F"), Outline("#121418"));
                yield return Shape("M3.8,5.3 H20.2 V15.2 H3.8 Z", Fill("#7FD3FF", "#2E7BD6"), null);
                yield return Shape("M3.8,12.5 Q8,10.5 12,12.5 T20.2,12 V15.2 H3.8 Z", Fill("#FFE9A8", "#F2C85B"), null);
                yield return Shape("M6.5,7 H17.5 V9.4 H6.5 Z", Fill("#FFE36B", "#F5B700"), Outline("#7A5200", 0.5));
                yield return Shape("M10.5,10.6 L13.9,12.4 L10.5,14.2 Z", Solid("#FFFFFF"), Outline("#22262F", 0.5));
                yield return Shape("M9,17 L15,17 L16,20.5 L8,20.5 Z", Fill("#6B7280", "#41464F"), Outline("#121418", 0.8));
                break;
            case "Currency Editor": // a stack of gold coins and one standing
            {
                var gold = Fill("#FFE88A", "#E0A100");
                var pen = Outline("#7A5000", 0.8);
                for (int k = 0; k < 3; k++)
                {
                    double y = 17.5 - k * 3;
                    yield return Shape($"M3,{y} V{y + 2} A5,2 0 0 0 13,{y + 2} V{y} Z", Fill("#E0A100", "#B07800"), pen);
                    yield return Oval(8, y, 5, 2, gold, pen);
                }
                yield return Oval(17, 13.5, 4.8, 4.8, Fill("#FFF1A8", "#E8AE10"), Outline("#7A5000", 0.9));
                yield return Oval(17, 13.5, 3.2, 3.2, null, Outline("#B07800", 0.7));
                yield return Line("M17,11.6 V15.4 M15.9,12.6 Q17,11.8 18,12.6 Q16,13.5 18,14.4 Q17,15.2 15.9,14.4", "#7A5000", 0.7);
                break;
            }
            case "Resources": // a folder full of files
                yield return Shape("M2,6 H9 L11,8 H22 V20 H2 Z", Fill("#F5BE4F", "#D9961B"), Outline("#7A5000"));
                yield return Shape("M6,3.5 H16 V12 H6 Z", Solid("#FFFFFF"), Outline("#8A8F99", 0.7));
                yield return Line("M7.5,5.5 H14.5 M7.5,7.5 H14.5 M7.5,9.5 H12", "#9AA3B5", 0.7);
                yield return Shape("M2,10.5 H22 L20.8,20 H3.2 Z", Fill("#FFD976", "#F2A91C"), Outline("#7A5000"));
                break;
            case "Faction Editor": // a shield of two colours with a star
                yield return Shape("M12,2 L20,5 V11 C20,16 16.5,19.5 12,22 C7.5,19.5 4,16 4,11 V5 Z", Fill("#5A9BFF", "#2557C9"), null);
                yield return Shape("M12,2 L4,5 V11 C4,16 7.5,19.5 12,22 Z", Fill("#FF6B6B", "#C9252D"), null);
                yield return Shape("M12,2 L20,5 V11 C20,16 16.5,19.5 12,22 C7.5,19.5 4,16 4,11 V5 Z", null, Outline("#2A1A1A", 1.1));
                yield return Shape("M12,7.5 L13.3,10.6 L16.6,10.8 L14,12.9 L14.9,16.1 L12,14.3 L9.1,16.1 L10,12.9 L7.4,10.8 L10.7,10.6 Z", Fill("#FFF2A0", "#FFC93C"), Outline("#7A5200", 0.6));
                break;
            case "Sprite Editor": // a painter's palette and brush
                yield return Shape("M12,3 C6.5,3 3,7 3,11.5 C3,16 6.5,20 11,20 C12.6,20 13.1,19 12.6,17.8 C12,16.4 13,15.4 14.6,15.4 H17 C19.6,15.4 21,13.4 21,11 C21,6.5 17,3 12,3 Z", Fill("#FBE6C0", "#E8C68A"), Outline("#7A5523"));
                yield return Oval(8, 8.2, 1.7, 1.7, Solid("#F04B4B"), null);
                yield return Oval(12, 6.4, 1.7, 1.7, Solid("#FFD23F"), null);
                yield return Oval(16.2, 8.2, 1.7, 1.7, Solid("#3BC46A"), null);
                yield return Oval(7, 12.7, 1.7, 1.7, Solid("#3E8BFF"), null);
                yield return Shape("M15.5,20.5 L21.5,13.5 L22.5,14.5 L16.5,21.3 Z", Fill("#8B5A2B", "#5E3A16"), Outline("#3B230C", 0.6));
                yield return Shape("M14.2,21.8 L15.5,20.5 L16.5,21.3 Z", Solid("#C147E9"), null);
                break;
            case "Book Editor": // an open book
                yield return Shape("M1,7 V20.5 C5,19.5 9,19.5 12,21 C15,19.5 19,19.5 23,20.5 V7", Fill("#C0662B", "#8B4513"), Outline("#5A2A08"));
                yield return Shape("M2,6 C5,5 9,5 12,7 V20 C9,18.4 5,18.4 2,19.2 Z", Fill("#FFFFFF", "#EDE6D3"), Outline("#8A7A5A", 0.7));
                yield return Shape("M22,6 C19,5 15,5 12,7 V20 C15,18.4 19,18.4 22,19.2 Z", Fill("#FFFFFF", "#E2DAC4"), Outline("#8A7A5A", 0.7));
                yield return Line("M4,9 C6,8.5 8,8.5 10,9.5 M4,12 C6,11.5 8,11.5 10,12.5 M4,15 C6,14.5 8,14.5 10,15.5 M14,9.5 C16,8.5 18,8.5 20,9 M14,12.5 C16,11.5 18,11.5 20,12", "#9AA3B5", 0.7);
                break;
            case "Container Editor": // a treasure chest
                yield return Shape("M3,10 C3,6 6,4 12,4 C18,4 21,6 21,10 Z", Fill("#D9843A", "#A4561A"), Outline("#4E2408"));
                yield return Shape("M3,10 H21 V20.5 H3 Z", Fill("#9C5222", "#6B330E"), Outline("#4E2408"));
                yield return Line("M3,10 H21 M8,4.7 V20.5 M16,4.7 V20.5", "#FFC93C", 1.4);
                yield return Shape("M10.4,10.6 H13.6 V14.8 H10.4 Z", Fill("#FFE36B", "#E0A100"), Outline("#4E2408", 0.6));
                yield return Oval(12, 12.4, 0.7, 0.7, Solid("#4E2408"), null);
                break;
            case "Sound Editor": // a speaker with its sound
                yield return Shape("M3,9 H7 L12.5,4.5 V19.5 L7,15 H3 Z", Fill("#6B7A90", "#2D3748"), Outline("#161B26"));
                yield return Line("M15.2,9 C16.8,10.6 16.8,13.4 15.2,15", "#2FB6E8", 1.8);
                yield return Line("M17.8,6.6 C21,9.8 21,14.2 17.8,17.4", "#2FB6E8", 1.8);
                break;
            case "Sound Mixer": // sliders on a desk
                yield return Shape("M2,3.5 H22 V20.5 H2 Z", Fill("#4A5568", "#1F2633"), Outline("#0E1219"));
                yield return Line("M7,6.5 V17.5 M12,6.5 V17.5 M17,6.5 V17.5", "#A0AEC0", 1.2);
                yield return Shape("M5,10.6 H9 V13.4 H5 Z", Fill("#FF8A8A", "#E53E3E"), Outline("#0E1219", 0.6));
                yield return Shape("M10,7.6 H14 V10.4 H10 Z", Fill("#7EE0A1", "#2F9E5C"), Outline("#0E1219", 0.6));
                yield return Shape("M15,13.6 H19 V16.4 H15 Z", Fill("#FFE27A", "#E5B000"), Outline("#0E1219", 0.6));
                break;
            case "Video Library": // a clapperboard
                yield return Shape("M3,10 H21 V20.5 H3 Z", Fill("#3A3A3A", "#151515"), Outline("#000000"));
                yield return Shape("M2.6,6.2 L20.2,3.2 L20.8,6.6 L3.2,9.6 Z", Solid("#FFFFFF"), Outline("#000000", 0.8));
                yield return Line("M5.6,5.7 L7.8,9.1 M9.6,5 L11.8,8.4 M13.6,4.3 L15.8,7.7 M17.6,3.6 L19.8,7", "#151515", 1.5);
                yield return Shape("M10,12.4 L15.2,15.2 L10,18 Z", Fill("#FF8A8A", "#E53E3E"), null);
                break;
            case "Variable Editor": // a tag holding a value: x =
                yield return Shape("M2.5,7 H15.5 L21.5,12 L15.5,17 H2.5 Z", Fill("#9D86FF", "#5B3CD6"), Outline("#2E1A80"));
                yield return Oval(17.4, 12, 1.1, 1.1, Solid("#FFFFFF"), null);
                yield return Line("M5,9.8 L9,14.2 M9,9.8 L5,14.2", "#FFFFFF", 1.7);
                yield return Line("M10.8,11 H13.6 M10.8,13 H13.6", "#FFFFFF", 1.2);
                break;
            case "Stat Editor": // a bar chart
                yield return Shape("M4.5,13 H8 V20.5 H4.5 Z", Fill("#7EE0A1", "#2F9E5C"), Outline("#1E4D33", 0.7));
                yield return Shape("M10,7 H13.5 V20.5 H10 Z", Fill("#8EC5FF", "#2B6CB0"), Outline("#173A66", 0.7));
                yield return Shape("M15.5,10 H19 V20.5 H15.5 Z", Fill("#FFC58A", "#DD6B20"), Outline("#7B3A0E", 0.7));
                yield return Line("M2.5,3 V20.5 H21.5", "#2D3748", 1.4);
                break;
            case "Character Editor": // a hero's bust
                yield return Shape("M4,21 C4,16 7.5,13.5 12,13.5 C16.5,13.5 20,16 20,21 Z", Fill("#5EA2FF", "#1E5BB8"), Outline("#13284A"));
                yield return Oval(12, 8.4, 4.2, 4.4, Fill("#FFE2C6", "#FFC38F"), Outline("#13284A"));
                yield return Shape("M7.7,8.2 C7.6,4.6 9.6,3 12.2,3 C15.2,3 16.7,4.8 16.4,8.4 C15.4,6.8 13.8,6.2 12,6.4 C10.2,6.6 8.8,7.2 7.7,8.2 Z", Fill("#7A4A22", "#4A2C12"), Outline("#2A1708", 0.7));
                yield return Oval(10.4, 9.4, 0.55, 0.7, Solid("#13284A"), null);
                yield return Oval(13.6, 9.4, 0.55, 0.7, Solid("#13284A"), null);
                yield return Line("M10.6,11.2 Q12,12.2 13.4,11.2", "#B5562B", 0.7);
                break;
        }
    }
}
