// Book Editor, Ultima 6 projects only: no paper doll - no frames, no pages to draw, no paper doll sprite. The book's words are typed
// in a plain notepad, and in play they're read in the text area, as much as fits, then a key for the rest (U6GameWindow.U6Things.cs).
// Ultima 7 and Ultima 8 projects never run any of this.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class BookEditorWindow
{
    private readonly TextBox _u6TextBox = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 14,
        Padding = new Thickness(4)
    };

    /// <summary>Everything under the sprite's preview (pages, the drawing tools, the paper doll) goes; the notepad takes its place.</summary>
    private void SetUpU6BookLayout()
    {
        if (PaperDollNameCombo.Parent is FrameworkElement { Parent: FrameworkElement paperDollField }) paperDollField.Visibility = Visibility.Collapsed; // no paper doll sprite
        if (PreviewCanvas.Parent is not FrameworkElement start) return;
        // Up from the drawing surface to the Properties tab's own DockPanel: the scroll area with the fields stays, the rest goes.
        FrameworkElement at = start;
        while (at.Parent is FrameworkElement up && up is not DockPanel { Parent: TabItem }) at = up;
        if (at.Parent is not DockPanel tab) return;
        foreach (UIElement child in tab.Children)
            if (child is not ScrollViewer) child.Visibility = Visibility.Collapsed;

        var notepad = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new TextBlock { Text = "The book's words", FontWeight = FontWeights.Bold });
        head.Children.Add(new TextBlock
        {
            Text = "Type what the book says. In play it's read in the text area, as Ultima 6 does: as much as fits, then it waits for a key and goes on with the rest, until the end. An empty line starts a new paragraph.",
            Opacity = 0.6, FontSize = 11, TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(head, Dock.Top);
        notepad.Children.Add(head);
        notepad.Children.Add(_u6TextBox);
        tab.Children.Add(notepad); // the DockPanel's last child: fills what's left
        _u6TextBox.TextChanged += (_, _) => { if (!_suppress && _current is not null) _current.U6Text = _u6TextBox.Text; };
    }

    /// <summary>This book's words in the notepad. A book made before (words in frames): its words, to start from.</summary>
    private void ShowU6Text(BookData book)
    {
        if (book.U6Text.Length == 0 && book.Pages.Count > 0) book.U6Text = FramesAsPlainText(book);
        _u6TextBox.Text = book.U6Text;
    }

    private static string FramesAsPlainText(BookData book)
    {
        var paragraphs = new List<string>();
        foreach (var page in book.Pages)
            foreach (var frame in book.Frames)
                if (page.FrameTexts.TryGetValue(frame.Id, out var xaml) && xaml.Length > 0)
                {
                    try
                    {
                        var box = new RichTextBox();
                        RichTextXamlHelper.Load(box, xaml);
                        string text = new System.Windows.Documents.TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Replace("\r\n", "\n").Trim();
                        if (text.Length > 0) paragraphs.Add(text);
                    }
                    catch { /* a frame that can't be read is left out */ }
                }
        return string.Join("\n", paragraphs).Replace("\n", Environment.NewLine);
    }
}
