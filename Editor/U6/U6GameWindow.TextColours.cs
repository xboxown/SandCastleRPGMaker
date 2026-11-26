// Ultima 6 engine (U6GameWindow): colours in the text area - Ultima 6 projects only.
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Words in a colour of their own (the user, 2026-11-25: a book's words, any text - [COLOR=RED]...[/COLOR]): anything said in
/// the text area may hold [COLOR=name]...[/COLOR] or [COLOR=#RRGGBB]...[/COLOR]. The codes become marks (\u0003 colour \u0005 ...
/// \u0004) that take no room; a line wrapped inside a colour carries it on to the next. The Apple IIgs and the C player do
/// the same (the game palette's nearest colour on the IIgs).
/// </summary>
public partial class U6GameWindow
{
    private static readonly Regex ColourCode = new(@"\[COLOR=([^\]]+)\]|\[/COLOR\]", RegexOptions.IgnoreCase);

    /// <summary>[COLOR=x] and [/COLOR] as marks.</summary>
    private static string ColourMarks(string text) =>
        text.IndexOf("[", StringComparison.Ordinal) < 0 ? text
        : ColourCode.Replace(text, m => m.Groups[1].Success ? "\u0003" + m.Groups[1].Value.Trim() + "\u0005" : "\u0004");

    private static string StripColourMarks(string text) =>
        text.IndexOf('\u0003') < 0 && text.IndexOf('\u0004') < 0 ? text : Regex.Replace(text, "\u0003[^\u0005]*\u0005|\u0004", "");

    /// <summary>Wrapped lines: one that ends inside a colour closes it, the next opens it again.</summary>
    private static List<string> ColourLines(List<string> lines)
    {
        string? open = null;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (open is not null) line = "\u0003" + open + "\u0005" + line;
            foreach (Match m in Regex.Matches(lines[i], "\u0003([^\u0005]*)\u0005|\u0004")) open = m.Value == "\u0004" ? null : m.Groups[1].Value;
            if (open is not null) line += "\u0004";
            lines[i] = line;
        }
        return lines;
    }

    /// <summary>A line with colour marks: each piece in its colour (the piece's own colour outside them).</summary>
    private FrameworkElement ColouredWords(string text, HudPiece? piece)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        string font = piece?.FontName ?? "";
        double space = Math.Max(2, HudTextWidth("a a", font) - HudTextWidth("aa", font));
        string? colour = null;
        foreach (var part in Regex.Split(text, "(\u0003[^\u0005]*\u0005|\u0004)"))
        {
            if (part.StartsWith('\u0003')) { colour = part[1..^1]; continue; }
            if (part == "\u0004") { colour = null; continue; }
            if (part.Length == 0) continue;
            if (part.StartsWith(' ')) row.Children.Add(new Border { Width = space });
            if (part.Trim().Length > 0)
            {
                var own = colour is null ? piece : new HudPiece { FontName = font, TextColor = ColourHex(colour) };
                row.Children.Add(HudWords(part.Trim(), own));
            }
            if (part.EndsWith(' ') && part.Trim().Length > 0) row.Children.Add(new Border { Width = space });
        }
        return row;
    }

    /// <summary>A colour's name or #RRGGBB as #RRGGBB ("" if it isn't one).</summary>
    private static string ColourHex(string name)
    {
        try { return ColorConverter.ConvertFromString(name.Trim()) is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : ""; }
        catch { return ""; }
    }
}
