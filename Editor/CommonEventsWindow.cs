using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > Common Events: command lists made once and run from anywhere with
/// Call Common Event - a shop, a riddle, a "heal the party" - each with a name
/// and its parameters (the variables Call Common Event's values go into).
/// Return ends one early, its value going into the variable Result.
/// </summary>
public class CommonEventsWindow : Window
{
    private readonly List<CommonEventDefinition> _events;
    private readonly ListBox _list = new() { DisplayMemberPath = nameof(CommonEventDefinition.Name) };
    private readonly TextBox _name = new() { Width = 260, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBox _parameters = new() { Width = 260, Margin = new Thickness(6, 0, 0, 0) };
    private readonly EventEditorControl _editor;
    private CommonEventDefinition? _current;
    private bool _loading;

    public CommonEventsWindow(List<CommonEventDefinition> events, ResourceDatabase resources, VariableDatabase variables, BookDatabase books,
        ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites)
    {
        _events = events;
        Title = "Common Events";
        Width = 1050;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _editor = new EventEditorControl { ResourceDatabase = resources, Variables = variables, Books = books, Objects = objects, Containers = containers, Sprites = sprites, CurrentBook = null };
        foreach (var button in new[] { _editor.NewPageButton, _editor.CopyPageButton, _editor.PastePageButton, _editor.DeletePageButton }) button.Visibility = Visibility.Collapsed;
        _editor.ContentChanged += () => { if (_current is not null && _editor.Pages.Count > 0) _current.Commands = _editor.Pages[0].Commands; };

        var add = new Button { Content = "New", Width = 70, Margin = new Thickness(0, 0, 4, 0) };
        add.Click += (_, _) =>
        {
            var e = new CommonEventDefinition { Id = _events.Count == 0 ? 1 : _events.Max(x => x.Id) + 1, Name = $"Common Event {_events.Count + 1}" };
            _events.Add(e);
            RefreshList(e);
        };
        var delete = new Button { Content = "Delete", Width = 70 };
        delete.Click += (_, _) =>
        {
            if (_list.SelectedItem is not CommonEventDefinition e) return;
            if (MessageBox.Show(this, $"Delete \"{e.Name}\"? Call Common Event lines that use it will do nothing.", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _events.Remove(e);
            RefreshList(_events.FirstOrDefault());
        };
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        leftButtons.Children.Add(add);
        leftButtons.Children.Add(delete);
        var left = new DockPanel { Width = 220, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        left.Children.Add(_list);
        _list.SelectionChanged += (_, _) => Show(_list.SelectedItem as CommonEventDefinition);

        _name.TextChanged += (_, _) =>
        {
            if (_loading || _current is null) return;
            _current.Name = _name.Text;
            _list.Items.Refresh();
        };
        _parameters.TextChanged += (_, _) =>
        {
            if (_loading || _current is null) return;
            _current.Parameters = _parameters.Text.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        };
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = "Name:", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        nameRow.Children.Add(_name);
        var paramRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        paramRow.Children.Add(new TextBlock { Text = "Parameters (variables, comma between):", Width = 170, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        paramRow.Children.Add(_parameters);
        head.Children.Add(nameRow);
        head.Children.Add(paramRow);
        head.Children.Add(new TextBlock
        {
            Text = "Call Common Event runs these commands, then the event that called it goes on. Its values go into the parameters' variables in order. " +
                   "Return ends it early (its value into the variable Result). Whoever set the calling event off is still the Activator here.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0)
        });

        var right = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        right.Children.Add(head);
        right.Children.Add(_editor);

        var close = new Button { Content = "Close", Width = 90, IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        close.Click += (_, _) => Close();
        var body = new DockPanel();
        body.Children.Add(left);
        body.Children.Add(right);
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(body);
        Content = root;
        RefreshList(_events.FirstOrDefault());
    }

    private void RefreshList(CommonEventDefinition? select)
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _events;
        _list.SelectedItem = select;
        Show(select);
    }

    private void Show(CommonEventDefinition? e)
    {
        _current = e;
        _loading = true;
        _name.Text = e?.Name ?? "";
        _parameters.Text = e is null ? "" : string.Join(", ", e.Parameters);
        _name.IsEnabled = _parameters.IsEnabled = _editor.IsEnabled = e is not null;
        _loading = false;
        _editor.Pages = new ObservableCollection<EventCommandPage> { new() { Label = "1", Commands = e?.Commands ?? new List<EventCommand>() } };
    }
}
