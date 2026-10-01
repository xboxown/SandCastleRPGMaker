using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class SetSwitchCommandWindow : Window
{
    private static readonly VariableValueType[] AllowedTypes = { VariableValueType.Boolean };

    private readonly VariableDatabase _globalVariables;
    private readonly List<VariableDefinition> _localVariables;
    private string? _pickedName;

    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.SetSwitch };

    public SetSwitchCommandWindow(VariableDatabase globalVariables, List<VariableDefinition> localVariables, EventCommand? existing = null)
    {
        InitializeComponent();
        _globalVariables = globalVariables;
        _localVariables = localVariables;

        if (existing is not null)
        {
            _pickedName = existing.Get("SwitchName");
            NameBox.Text = _pickedName;
            bool.TryParse(existing.Get("Value"), out bool value);
            TrueRadio.IsChecked = value;
            FalseRadio.IsChecked = !value;
        }
    }

    private void PickVariable_Click(object sender, RoutedEventArgs e)
    {
        // Which scope/id to preselect - a Name string alone can't tell us
        // that on its own (both a local and a global variable could share
        // a name), but this dialog only ever writes a Name, so there's
        // nothing more precise to remember between edits anyway. Matching
        // by name against whichever list it's actually found in is the
        // best a Name-only storage model can do.
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
            MessageBox.Show("Pick a switch first.", "Control Switches", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new EventCommand { Type = EventCommandType.SetSwitch };
        command.Parameters["SwitchName"] = _pickedName;
        command.Parameters["Value"] = (TrueRadio.IsChecked == true).ToString();
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
