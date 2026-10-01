using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class VariableEditorWindow : Window
{
    private readonly VariableDatabase _variables;

    public VariableEditorWindow(VariableDatabase variables)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        _variables = variables;
        Refresh();
    }

    private void Refresh()
    {
        var selected = VariablesListView.SelectedItem as VariableDefinition;
        VariablesListView.ItemsSource = _variables.Variables.ToList();
        if (selected is not null)
            VariablesListView.SelectedItem = ((List<VariableDefinition>)VariablesListView.ItemsSource).FirstOrDefault(v => v.Id == selected.Id);
    }

    private void NewVariable_Click(object sender, RoutedEventArgs e)
    {
        var name = SimpleInputWindow.Show(this, "Variable name:", "New Variable", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        var type = PromptForType(VariableValueType.Integer);
        if (type is null) return;

        var newVar = new VariableDefinition { Id = _variables.GetNextId(), Name = name.Trim(), Type = type.Value };
        _variables.Variables.Add(newVar);
        Refresh();
        VariablesListView.SelectedItem = newVar;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesListView.SelectedItem is not VariableDefinition selected)
        {
            MessageBox.Show("Select a variable first.", "Variable Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = SimpleInputWindow.Show(this, "Variable name:", "Rename Variable", selected.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        selected.Name = name.Trim();
        Refresh();
    }

    private void ChangeType_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesListView.SelectedItem is not VariableDefinition selected)
        {
            MessageBox.Show("Select a variable first.", "Variable Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var type = PromptForType(selected.Type);
        if (type is null) return;
        selected.Type = type.Value;
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (VariablesListView.SelectedItem is not VariableDefinition selected)
        {
            MessageBox.Show("Select a variable first.", "Variable Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Delete \"{selected.Name}\"? Any command or condition still referencing it by this Id will simply find nothing there anymore.",
            "Delete Variable", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _variables.Variables.Remove(selected);
        Refresh();
    }

    /// <summary>Small inline type picker - same shape as the one SelectVariableWindow already uses for a NEW variable's type, but with every type always offered (this editor isn't constrained to a Boolean-only or String-only picker the way a specific command's variable field is).</summary>
    private VariableValueType? PromptForType(VariableValueType current)
    {
        var window = new Window
        {
            Title = "Variable Type",
            Width = 260,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var combo = new ComboBox { ItemsSource = Enum.GetValues<VariableValueType>(), SelectedItem = current, Margin = new Thickness(0, 0, 0, 12) };
        var okButton = new Button { Content = "OK", Width = 70, IsDefault = true, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "Cancel", Width = 70, IsCancel = true, Margin = new Thickness(4) };
        VariableValueType? result = null;
        okButton.Click += (_, _) => { result = (VariableValueType)combo.SelectedItem; window.DialogResult = true; };
        cancelButton.Click += (_, _) => window.DialogResult = false;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var stack = new StackPanel { Margin = new Thickness(12) };
        stack.Children.Add(new TextBlock { Text = "Type:", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(combo);
        stack.Children.Add(buttonRow);
        window.Content = stack;

        return window.ShowDialog() == true ? result : null;
    }
}
