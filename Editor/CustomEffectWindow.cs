using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A custom effect (a "custom stat" in the Object Editor's effect lists):
/// its name - shown green in every effect list - and the event commands it
/// runs, with no conditions: when a potion, food, drink, spell or useable
/// object with this effect is used on someone, these commands run with that
/// someone as the Activator.
/// </summary>
public class CustomEffectWindow : Window
{
    private readonly CustomEffect _effect;
    private readonly List<EventCommand> _commands;
    private readonly TextBox _name;

    public CustomEffectWindow(CustomEffect effect, ResourceDatabase resources, VariableDatabase variables, BookDatabase books,
        ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites, bool isNew)
    {
        _effect = effect;
        _commands = effect.Commands.ToList(); // edited on a copy: Cancel leaves it as it was
        Title = isNew ? "Add Custom Stat" : $"Custom Stat: {effect.Name}";
        Width = 900;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _name = new TextBox { Width = 260, Text = effect.Name, Margin = new Thickness(6, 0, 0, 0) };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        nameRow.Children.Add(new TextBlock { Text = "Name (shown green in the effect lists):", VerticalAlignment = VerticalAlignment.Center });
        nameRow.Children.Add(_name);

        var editor = new EventEditorControl
        {
            ResourceDatabase = resources, Variables = variables, Books = books, Objects = objects, Containers = containers, Sprites = sprites, CurrentBook = null
        };
        var page = new EventCommandPage { Label = "1", Commands = _commands };
        editor.Pages = new ObservableCollection<EventCommandPage> { page };
        editor.ContentChanged += () => { if (editor.Pages.Count > 0) { _commands.Clear(); _commands.AddRange(editor.Pages[0].Commands); } };
        // One list of commands, no conditions and no pages.
        foreach (var button in new[] { editor.NewPageButton, editor.CopyPageButton, editor.PastePageButton, editor.DeletePageButton }) button.Visibility = Visibility.Collapsed;

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) =>
        {
            string name = _name.Text.Trim();
            if (name.Length == 0) { MessageBox.Show("Give it a name.", Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _effect.Name = name;
            _effect.Commands = editor.Pages.Count > 0 ? editor.Pages[0].Commands.ToList() : _commands.ToList();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var note = new TextBlock
        {
            Text = "These commands run, with no conditions, on whoever the potion, food, drink, spell or object is used on - they are the Activator. " +
                   "For example: Change HP of the Activator, Modify Ailments, Show Text, Play Sound, Set Variable.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 6)
        };
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(nameRow, Dock.Top);
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(nameRow);
        root.Children.Add(note);
        root.Children.Add(buttons);
        root.Children.Add(editor);
        Content = root;
    }
}
