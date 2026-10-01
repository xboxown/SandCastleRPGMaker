using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class EditEventCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new();

    private readonly Dictionary<string, TextBox> _fieldBoxes = new();

    public EditEventCommandWindow(EventCommand? existing = null)
    {
        InitializeComponent();

        CommandTypeCombo.ItemsSource = Enum.GetValues<EventCommandType>();

        if (existing is not null)
        {
            CommandTypeCombo.SelectedItem = existing.Type;
            IndentBox.Text = existing.IndentLevel.ToString();
            // Fields get (re)built by the SelectionChanged handler above; populate values after.
            foreach (var kv in existing.Parameters)
            {
                if (_fieldBoxes.TryGetValue(kv.Key, out var box))
                    box.Text = kv.Value;
            }
        }
        else
        {
            CommandTypeCombo.SelectedIndex = 0;
        }
    }

    private void CommandTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DynamicFieldsPanel.Children.Clear();
        _fieldBoxes.Clear();

        if (CommandTypeCombo.SelectedItem is not EventCommandType type) return;

        var specs = EventCommandCatalog.FieldsByType.TryGetValue(type, out var f) ? f : Array.Empty<FieldSpec>();

        foreach (var spec in specs)
        {
            var label = new TextBlock { Text = spec.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
            var box = new TextBox { Margin = new Thickness(0, 0, 0, 2) };
            DynamicFieldsPanel.Children.Add(label);
            DynamicFieldsPanel.Children.Add(box);
            _fieldBoxes[spec.Key] = box;
        }

        if (specs.Length == 0)
        {
            DynamicFieldsPanel.Children.Add(new TextBlock
            {
                Text = "(No parameters for this command.)",
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 6, 0, 2)
            });
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommandTypeCombo.SelectedItem is not EventCommandType type)
        {
            MessageBox.Show("Select a command type.", "Missing type", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int.TryParse(IndentBox.Text, out int indent);

        var command = new EventCommand { Type = type, IndentLevel = Math.Max(0, indent) };
        foreach (var kv in _fieldBoxes)
            command.Parameters[kv.Key] = kv.Value.Text;

        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
