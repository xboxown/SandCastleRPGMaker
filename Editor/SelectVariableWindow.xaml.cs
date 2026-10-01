using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class SelectVariableWindow : Window
{
    private readonly List<VariableDefinition> _localVariables;
    private readonly VariableDatabase _globalVariables;
    private readonly VariableValueType[] _allowedTypes;

    public VariableDefinition? Result { get; private set; }
    public VariableScope ResultScope { get; private set; } = VariableScope.Local;

    public SelectVariableWindow(List<VariableDefinition> localVariables, VariableDatabase globalVariables, VariableValueType[] allowedTypes, VariableScope? currentScope, int? currentId)
    {
        InitializeComponent();
        _localVariables = localVariables;
        _globalVariables = globalVariables;
        _allowedTypes = allowedTypes;
        RefreshLocal();
        RefreshGlobal();

        if (currentId is int id)
        {
            if (currentScope == VariableScope.Global)
            {
                ScopeTabs.SelectedIndex = 1;
                var match = ((List<VariableDefinition>)GlobalListView.ItemsSource).FirstOrDefault(v => v.Id == id);
                if (match is not null) GlobalListView.SelectedItem = match;
            }
            else
            {
                var match = ((List<VariableDefinition>)LocalListView.ItemsSource).FirstOrDefault(v => v.Id == id);
                if (match is not null) LocalListView.SelectedItem = match;
            }
        }
    }

    private void RefreshLocal() => LocalListView.ItemsSource = _localVariables.Where(v => _allowedTypes.Contains(v.Type)).ToList();
    private void RefreshGlobal() => GlobalListView.ItemsSource = _globalVariables.Variables.Where(v => _allowedTypes.Contains(v.Type)).ToList();

    private void ScopeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void NewVariableButton_Click(object sender, RoutedEventArgs e)
    {
        var name = SimpleInputWindow.Show(this, "Variable name:", "New Variable", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        VariableValueType type;
        if (_allowedTypes.Length == 1)
        {
            type = _allowedTypes[0];
        }
        else
        {
            var chosen = PromptForType(_allowedTypes);
            if (chosen is null) return;
            type = chosen.Value;
        }

        // Was always adding to _localVariables regardless of which tab was
        // active - meant there was genuinely no way to create a new GLOBAL
        // variable from here at all, only local ones.
        bool onGlobalTab = ScopeTabs.SelectedIndex == 1;
        if (onGlobalTab)
        {
            int newGlobalId = _globalVariables.GetNextId();
            var newGlobalVar = new VariableDefinition { Id = newGlobalId, Name = name.Trim(), Type = type };
            _globalVariables.Variables.Add(newGlobalVar);
            RefreshGlobal();
            GlobalListView.SelectedItem = ((List<VariableDefinition>)GlobalListView.ItemsSource).FirstOrDefault(v => v.Id == newGlobalVar.Id);
        }
        else
        {
            int newId = _localVariables.Count == 0 ? 1 : _localVariables.Max(v => v.Id) + 1;
            var newVar = new VariableDefinition { Id = newId, Name = name.Trim(), Type = type };
            _localVariables.Add(newVar);
            RefreshLocal();
            LocalListView.SelectedItem = ((List<VariableDefinition>)LocalListView.ItemsSource).FirstOrDefault(v => v.Id == newVar.Id);
        }
    }

    /// <summary>Small inline picker for which numeric type a new variable should be - built in code since it's only needed here.</summary>
    private VariableValueType? PromptForType(VariableValueType[] options)
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

        var combo = new ComboBox { ItemsSource = options, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 12) };
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

    private void LocalListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LocalListView.SelectedItem is VariableDefinition) TryAccept();
    }

    private void GlobalListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GlobalListView.SelectedItem is VariableDefinition) TryAccept();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => TryAccept();

    private void TryAccept()
    {
        bool onLocalTab = ScopeTabs.SelectedIndex == 0;
        var selected = onLocalTab ? LocalListView.SelectedItem as VariableDefinition : GlobalListView.SelectedItem as VariableDefinition;

        if (selected is null)
        {
            MessageBox.Show("Select a variable first.", "Select Variable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = selected;
        ResultScope = onLocalTab ? VariableScope.Local : VariableScope.Global;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
