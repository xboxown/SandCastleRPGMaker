using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A one-line formula box that colours what you type (every formula field of the command windows): a variable blue,
/// an array or grid green (Items[i], Board[x, y]), the rest black. A field that names something - Cast Spell's
/// spell - shows a text in quotes red when nothing in the project has that name, black when something does.
/// </summary>
public class FormulaBox : RichTextBox
{
    public static readonly Brush VariableBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x33, 0xCC));
    public static readonly Brush ArrayBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));
    public static readonly Brush UnknownBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0x00, 0x00));
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase) { "and", "or", "not", "true", "false" };

    private bool _busy;
    private bool _colouring = true;

    /// <summary>A text in quotes: true when the project has something by that name (Cast Spell: a spell or a Common Event); null = not checked.</summary>
    public Func<string, bool>? KnownText { get; set; }

    /// <summary>Off: plain black (a Words field set to "Words").</summary>
    public bool Colouring
    {
        get => _colouring;
        set { _colouring = value; Recolour(); }
    }

    public FormulaBox()
    {
        AcceptsReturn = false;
        AcceptsTab = false;
        IsUndoEnabled = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        MinHeight = 22;
        Document.PagePadding = new Thickness(2, 1, 2, 1);
        Document.PageWidth = 4000; // one line: never wraps
        Document.Blocks.Clear();
        Document.Blocks.Add(new Paragraph { Margin = new Thickness(0) });
        TextChanged += (_, _) => Recolour();
        DataObject.AddPastingHandler(this, OnPaste);
        ToolTip = "Blue: a variable. Green: an array or grid (Items[i]). Black: numbers, texts in quotes, functions, and / or / not.";
    }

    /// <summary>The formula as typed (one line).</summary>
    public string Text
    {
        get => new TextRange(Document.ContentStart, Document.ContentEnd).Text.Replace("\r", "").Replace("\n", "");
        set
        {
            _busy = true;
            Document.Blocks.Clear();
            Document.Blocks.Add(new Paragraph(new Run(value ?? "")) { Margin = new Thickness(0) });
            _busy = false;
            Recolour();
            CaretPosition = Document.ContentEnd;
        }
    }

    /// <summary>Pasted text comes in as plain text on one line.</summary>
    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text) { e.CancelCommand(); return; }
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text.Replace("\r", " ").Replace("\n", " "));
        e.DataObject = data;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = false; return; } // the window's OK button
        base.OnPreviewKeyDown(e);
    }

    private void Recolour()
    {
        if (_busy) return;
        _busy = true;
        BeginChange();
        try
        {
            new TextRange(Document.ContentStart, Document.ContentEnd).ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.Black);
            if (!_colouring) return;
            foreach (var (start, length, brush) in Spans(Text, KnownText))
                if (At(start) is { } from && At(start + length) is { } to)
                    new TextRange(from, to).ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        }
        finally
        {
            EndChange();
            _busy = false;
        }
    }

    /// <summary>The place in the document this many characters into the text.</summary>
    private TextPointer? At(int offset)
    {
        int count = 0;
        for (var p = Document.ContentStart; p is not null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            string run = p.GetTextInRun(LogicalDirection.Forward);
            if (count + run.Length >= offset) return p.GetPositionAtOffset(offset - count);
            count += run.Length;
        }
        return null;
    }

    /// <summary>The coloured parts of a formula: variables, arrays / grids and (when checked) texts nothing has the name of.</summary>
    public static List<(int Start, int Length, Brush Brush)> Spans(string formula, Func<string, bool>? knownText)
    {
        var spans = new List<(int, int, Brush)>();
        int i = 0;
        while (i < formula.Length)
        {
            char c = formula[i];
            if (c == '"')
            {
                int start = i++;
                var text = new System.Text.StringBuilder();
                while (i < formula.Length)
                {
                    if (formula[i] == '"')
                    {
                        if (i + 1 < formula.Length && formula[i + 1] == '"') { text.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    text.Append(formula[i++]);
                }
                if (knownText is not null && !knownText(text.ToString().Trim())) spans.Add((start, i - start, UnknownBrush));
                continue;
            }
            if (c == '{')
            {
                int close = formula.IndexOf('}', i + 1);
                int end = close < 0 ? formula.Length : close + 1;
                spans.Add((i, end - i, NextIs(formula, end, '[') ? ArrayBrush : VariableBrush));
                i = end;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_')) i++;
                string name = formula[start..i];
                if (NextIs(formula, i, '[')) spans.Add((start, i - start, ArrayBrush));
                else if (!NextIs(formula, i, '(') && !Words.Contains(name)) spans.Add((start, i - start, VariableBrush));
                continue;
            }
            if (char.IsDigit(c)) { while (i < formula.Length && (char.IsDigit(formula[i]) || formula[i] == '.')) i++; continue; }
            i++;
        }
        return spans;
    }

    private static bool NextIs(string formula, int i, char wanted)
    {
        while (i < formula.Length && char.IsWhiteSpace(formula[i])) i++;
        return i < formula.Length && formula[i] == wanted;
    }
}
