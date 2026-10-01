using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class StatEditorWindow : Window
{
    private readonly StatDatabase _stats;

    public StatEditorWindow(StatDatabase stats)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        _stats = stats;
        Refresh();
    }

    private void Refresh()
    {
        var selected = StatsListView.SelectedItem as StatDefinition;
        StatsListView.ItemsSource = _stats.Stats.ToList();
        if (selected is not null)
            StatsListView.SelectedItem = ((List<StatDefinition>)StatsListView.ItemsSource).FirstOrDefault(s => s.Id == selected.Id);
    }

    private void NewStat_Click(object sender, RoutedEventArgs e)
    {
        var name = SimpleInputWindow.Show(this, "Stat name:", "New Stat", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        var type = PromptForType(VariableValueType.Integer);
        if (type is null) return;

        var newStat = new StatDefinition { Id = _stats.GetNextId(), Name = name.Trim(), Type = type.Value };
        _stats.Stats.Add(newStat);
        Refresh();
        StatsListView.SelectedItem = newStat;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (StatsListView.SelectedItem is not StatDefinition selected)
        {
            MessageBox.Show("Select a stat first.", "Stat Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = SimpleInputWindow.Show(this, "Stat name:", "Rename Stat", selected.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        selected.Name = name.Trim();
        Refresh();
    }

    private void ChangeType_Click(object sender, RoutedEventArgs e)
    {
        if (StatsListView.SelectedItem is not StatDefinition selected)
        {
            MessageBox.Show("Select a stat first.", "Stat Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.IsBuiltIn)
        {
            MessageBox.Show($"\"{selected.Name}\" is a built-in stat and its type can't be changed - it can still be renamed.",
                "Stat Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var type = PromptForType(selected.Type);
        if (type is null) return;
        selected.Type = type.Value;
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (StatsListView.SelectedItem is not StatDefinition selected)
        {
            MessageBox.Show("Select a stat first.", "Stat Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.IsBuiltIn)
        {
            MessageBox.Show($"\"{selected.Name}\" is a built-in stat and can't be deleted - it can still be renamed.",
                "Stat Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Delete \"{selected.Name}\"? Any condition or character sheet still referencing it by this Id will simply find nothing there anymore.",
            "Delete Stat", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _stats.Stats.Remove(selected);
        Refresh();
    }

    /// <summary>Same small inline type picker VariableEditorWindow already uses - only Integer/String are meaningful for a stat per spec, but every VariableValueType option is still offered rather than silently filtering the enum down, matching how VariableEditorWindow itself does this.</summary>
    private VariableValueType? PromptForType(VariableValueType current)
    {
        var window = new Window
        {
            Title = "Stat Type",
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
