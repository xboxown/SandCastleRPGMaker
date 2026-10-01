using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class SetVariableCommandWindow : Window
{
    private static readonly VariableValueType[] AllowedTypes = { VariableValueType.Integer, VariableValueType.Float, VariableValueType.Decimal };

    private readonly VariableDatabase _globalVariables;
    private readonly List<VariableDefinition> _localVariables;
    private string? _pickedName;

    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.SetVariable };

    public SetVariableCommandWindow(VariableDatabase globalVariables, List<VariableDefinition> localVariables, EventCommand? existing = null)
    {
        InitializeComponent();
        _globalVariables = globalVariables;
        _localVariables = localVariables;

        if (existing is not null)
        {
            _pickedName = existing.Get("VariableName");
            NameBox.Text = _pickedName;
            ValueBox.Text = existing.Get("Value");
            SelectOperation(existing.Get("Operation"));
        }
        else
        {
            OperationCombo.SelectedIndex = 0;
            ValueBox.Text = "0";
        }
    }

    private void SelectOperation(string op) =>
        OperationCombo.SelectedIndex = op switch { "Add" => 1, "Subtract" => 2, "Multiply" => 3, "Divide" => 4, "Modulo" => 5, _ => 0 };

    private void PickVariable_Click(object sender, RoutedEventArgs e)
    {
        // Same name-only limitation as Control Switches - see that dialog's
        // own PickVariable_Click for why this is the best a Name-based
        // storage model can do for preselecting scope/id.
        var currentLocal = _localVariables.FirstOrDefault(v => v.Name == _pickedName && AllowedTypes.Contains(v.Type));
        var currentGlobal = _globalVariables.Variables.FirstOrDefault(v => v.Name == _pickedName && AllowedTypes.Contains(v.Type));
        VariableScope? scope = currentLocal is not null ? VariableScope.Local : currentGlobal is not null ? VariableScope.Global : null;
        int? id = (currentLocal ?? currentGlobal)?.Id;

        var picker = new SelectVariableWindow(_localVariables, _globalVariables, AllowedTypes, scope, id) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is null) return;

        _pickedName = picker.Result.Name;
        NameBox.Text = _pickedName;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pickedName))
        {
            MessageBox.Show("Pick a variable first.", "Control Variables", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(ValueBox.Text, out _))
        {
            MessageBox.Show("Value needs to be a number.", "Control Variables", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new EventCommand { Type = EventCommandType.SetVariable };
        command.Parameters["VariableName"] = _pickedName;
        command.Parameters["Operation"] = (OperationCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Set";
        command.Parameters["Value"] = ValueBox.Text.Trim();
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
