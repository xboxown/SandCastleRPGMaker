using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Returns a LIST of commands, not one - a new branch scaffolds its own
/// matching EndBranch (and, if asked, an Else) at the same time, since a
/// bare ConditionalBranchSwitch/Variable with nothing closing it would be
/// malformed. Editing an existing branch only ever returns that one
/// command back - its Else/EndBranch, if present, are separate commands
/// in the list the author manages independently, not something this
/// dialog restructures.
///
/// Nested branches (one Conditional Branch fully inside another) aren't
/// supported yet - everything here sits at IndentLevel 0, since there's no
/// indent-management UI in the event list yet. Sequential, non-nested
/// branches work correctly regardless, since the interpreter just needs
/// the next Else/EndBranch at the same indent, and "the same indent" is
/// every command right now.
///
/// A String mode (per the task doc) isn't here yet - it needs Control
/// Strings' own string-variable concept to exist first (there's no
/// GetStringVariable/SetStringVariable anywhere yet), so there's nothing
/// for a String radio option to actually check against. Switch and
/// Variable modes now use the real picker, same as Control
/// Switches/Variables.
/// </summary>
public partial class ConditionalBranchCommandWindow : Window
{
    private static readonly VariableValueType[] SwitchTypes = { VariableValueType.Boolean };
    private static readonly VariableValueType[] VariableTypes = { VariableValueType.Integer, VariableValueType.Float, VariableValueType.Decimal };

    private readonly VariableDatabase _globalVariables;
    private readonly List<VariableDefinition> _localVariables;
    private readonly bool _isEditingExisting;
    private string? _pickedSwitchName;
    private string? _pickedVariableName;

    public List<EventCommand> Results { get; private set; } = new();

    public ConditionalBranchCommandWindow(VariableDatabase globalVariables, List<VariableDefinition> localVariables, EventCommand? existing = null)
    {
        InitializeComponent();
        _globalVariables = globalVariables;
        _localVariables = localVariables;
        _isEditingExisting = existing is not null;
        IncludeElseCheck.Visibility = _isEditingExisting ? Visibility.Collapsed : Visibility.Visible;
        IncludeElseNote.Visibility = _isEditingExisting ? Visibility.Visible : Visibility.Collapsed;

        if (existing is not null && existing.Type == EventCommandType.ConditionalBranchVariable)
        {
            VariableRadio.IsChecked = true;
            _pickedVariableName = existing.Get("VariableName");
            VariableNameBox.Text = _pickedVariableName;
            SelectComparison(existing.Get("Comparison"));
            VariableValueBox.Text = existing.Get("Value");
            SwitchTrueRadio.IsChecked = true;
        }
        else if (existing is not null)
        {
            SwitchRadio.IsChecked = true;
            _pickedSwitchName = existing.Get("SwitchName");
            SwitchNameBox.Text = _pickedSwitchName;
            bool.TryParse(existing.Get("Value"), out bool v);
            SwitchTrueRadio.IsChecked = v;
            SwitchFalseRadio.IsChecked = !v;
            ComparisonCombo.SelectedIndex = 0;
            VariableValueBox.Text = "0";
        }
        else
        {
            SwitchRadio.IsChecked = true;
            SwitchTrueRadio.IsChecked = true;
            ComparisonCombo.SelectedIndex = 0;
            VariableValueBox.Text = "0";
        }

        UpdateModeVisibility();
    }

    private void SelectComparison(string op) =>
        ComparisonCombo.SelectedIndex = op switch { "!=" => 1, ">" => 2, ">=" => 3, "<" => 4, "<=" => 5, _ => 0 };

    private void ModeRadio_Checked(object sender, RoutedEventArgs e) => UpdateModeVisibility();

    private void UpdateModeVisibility()
    {
        if (SwitchPanel is null || VariablePanel is null) return; // guard against firing while InitializeComponent sets defaults
        bool isSwitch = SwitchRadio.IsChecked == true;
        SwitchPanel.Visibility = isSwitch ? Visibility.Visible : Visibility.Collapsed;
        VariablePanel.Visibility = isSwitch ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PickSwitch_Click(object sender, RoutedEventArgs e)
    {
        var currentLocal = _localVariables.FirstOrDefault(v => v.Name == _pickedSwitchName && SwitchTypes.Contains(v.Type));
        var currentGlobal = _globalVariables.Variables.FirstOrDefault(v => v.Name == _pickedSwitchName && SwitchTypes.Contains(v.Type));
        VariableScope? scope = currentLocal is not null ? VariableScope.Local : currentGlobal is not null ? VariableScope.Global : null;
        int? id = (currentLocal ?? currentGlobal)?.Id;

        var picker = new SelectVariableWindow(_localVariables, _globalVariables, SwitchTypes, scope, id) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is null) return;

        _pickedSwitchName = picker.Result.Name;
        SwitchNameBox.Text = _pickedSwitchName;
    }

    private void PickVariable_Click(object sender, RoutedEventArgs e)
    {
        var currentLocal = _localVariables.FirstOrDefault(v => v.Name == _pickedVariableName && VariableTypes.Contains(v.Type));
        var currentGlobal = _globalVariables.Variables.FirstOrDefault(v => v.Name == _pickedVariableName && VariableTypes.Contains(v.Type));
        VariableScope? scope = currentLocal is not null ? VariableScope.Local : currentGlobal is not null ? VariableScope.Global : null;
        int? id = (currentLocal ?? currentGlobal)?.Id;

        var picker = new SelectVariableWindow(_localVariables, _globalVariables, VariableTypes, scope, id) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is null) return;

        _pickedVariableName = picker.Result.Name;
        VariableNameBox.Text = _pickedVariableName;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        EventCommand branch;
        if (SwitchRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(_pickedSwitchName))
            {
                MessageBox.Show("Pick a switch first.", "Conditional Branch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            branch = new EventCommand { Type = EventCommandType.ConditionalBranchSwitch };
            branch.Parameters["SwitchName"] = _pickedSwitchName;
            branch.Parameters["Value"] = (SwitchTrueRadio.IsChecked == true).ToString();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_pickedVariableName) || !double.TryParse(VariableValueBox.Text, out _))
            {
                MessageBox.Show("Pick a variable and give a numeric value.", "Conditional Branch", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            branch = new EventCommand { Type = EventCommandType.ConditionalBranchVariable };
            branch.Parameters["VariableName"] = _pickedVariableName;
            branch.Parameters["Comparison"] = (ComparisonCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "==";
            branch.Parameters["Value"] = VariableValueBox.Text.Trim();
        }

        Results = new List<EventCommand> { branch };
        if (!_isEditingExisting)
        {
            if (IncludeElseCheck.IsChecked == true) Results.Add(new EventCommand { Type = EventCommandType.Else });
            Results.Add(new EventCommand { Type = EventCommandType.EndBranch });
        }
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
