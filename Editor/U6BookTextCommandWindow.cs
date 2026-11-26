using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Change Self / External Book Content in an Ultima 6 project (the user, 2026-11-25: a book is nothing more than text in the
/// text area, a key to read on): the book's new words, as its Book Editor notepad - [COLOR=RED]...[/COLOR] colours any of
/// them. Stored as the content of page -1, frame -1 (U6GameWindow.U6ReadBook, the C player and the Apple IIgs read it).
/// </summary>
public class U6BookTextCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new();
    private readonly ComboBox? _book;
    private readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") };

    public U6BookTextCommandWindow(bool external, BookDatabase books, EventCommand? existing = null)
    {
        Title = external ? "Change External Book Content" : "Change Self Book Content";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var type = external ? EventCommandType.ChangeExternalBookContent : EventCommandType.ChangeSelfBookContent;
        var panel = new StackPanel { Margin = new Thickness(12) };
        if (external)
        {
            _book = new ComboBox { ItemsSource = books.Books, DisplayMemberPath = nameof(BookData.Name), Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
            _book.SelectedItem = books.Books.FirstOrDefault(b => b.Id.ToString() == existing?.Get("TargetBookId")) ?? books.Books.FirstOrDefault();
            panel.Children.Add(new TextBlock { Text = "The book:" });
            panel.Children.Add(_book);
        }
        panel.Children.Add(new TextBlock { Text = external ? "Its words from now on:" : "This book's words from now on:", Margin = new Thickness(0, 8, 0, 2) });
        _text.Text = existing?.Get("ReplacementXaml") ?? "";
        panel.Children.Add(_text);
        panel.Children.Add(new TextBlock
        {
            Text = "As Ultima 6: read in the text area, a screenful at a time. [COLOR=RED]these words[/COLOR] are red - a colour's name or #RRGGBB.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = false, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) =>
        {
            var command = new EventCommand { Type = type };
            if (external)
            {
                if (_book?.SelectedItem is not BookData b) { MessageBox.Show(this, "Pick the book.", Title); return; }
                command.Parameters["TargetBookId"] = b.Id.ToString();
            }
            command.Parameters["PageIndex"] = "-1";
            command.Parameters["FrameId"] = "-1";
            command.Parameters["ReplacementXaml"] = _text.Text;
            Result = command;
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
