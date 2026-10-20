using System.Collections.ObjectModel;
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Properties (Page 1) plus a real Event tab (Page 2), sharing the exact
/// same EventEditorControl/BookEventConditionsControl every other event
/// editor in the project uses - not a copy. "By After Read Item" is
/// removed from this tab's own trigger dropdown specifically (a tile
/// isn't a readable book), via BookEventConditionsControl.
/// ExcludeTriggerTypes - Book/Map events never call that, so their own
/// dropdowns are unaffected.
/// </summary>
public partial class TilePropertiesWindow : Window
{
    private readonly TileTypeProperties _target;
    private readonly List<WorldObject> _availableObjects;
    private readonly List<CustomTileEffect> _workingEffects;
    private EventEditorControl? _eventEditorControl;
    private readonly ResourceDatabase _resources;
    private readonly StatDatabase _stats;

    public TilePropertiesWindow(TileTypeProperties target, List<WorldObject> availableObjects,
        ResourceDatabase resources, VariableDatabase variables, BookDatabase books, ObjectDatabase objects, SpriteDatabase sprites, StatDatabase stats, ContainerDatabase containers)
    {
        _resources = resources;
        _stats = stats;
        InitializeComponent();
        _target = target;
        _availableObjects = availableObjects;

        TileIndexLabel.Text = $"Tile #{target.TileIndex}";
        TerrainLabelBox.Text = target.TerrainLabel;
        WalkableCheck.IsChecked = target.Walkable;
        WaterCheck.IsChecked = target.IsWater;

        RequiredObjectsListBox.ItemsSource = _availableObjects;
        foreach (var obj in _availableObjects.Where(o => target.RequiredObjectIds.Contains(o.Id)))
            RequiredObjectsListBox.SelectedItems.Add(obj);

        _workingEffects = target.CustomEffects.Select(CloneEffect).ToList();
        RefreshEffectsList();

        TileConditionsControl.SetDatabases(books, objects, containers, sprites, variables, stats);
        TileConditionsControl.ExcludeTriggerTypes(BookTriggerConditionBased.ByAfterReadItem);

        if (_target.EventPages.Count == 0)
            _target.EventPages.Add(new BookEventPage());

        var enginePages = new ObservableCollection<EventCommandPage>(
            _target.EventPages.Select((p, i) => new EventCommandPage { Label = (i + 1).ToString(), Commands = p.Commands }));

        _eventEditorControl = new EventEditorControl
        {
            ResourceDatabase = resources,
            Variables = variables,
            CurrentBookLocalVariables = new List<VariableDefinition>(), // a tile has no "book" to scope local variables to - global variables only, for now
            Books = books,
            Objects = objects,
            Sprites = sprites,
            CurrentBook = null // no self-book context for a tile either
        };
        _eventEditorControl.PageSelected += OnEventPageSelected;
        _eventEditorControl.PageInserted += OnEventPageInserted;
        _eventEditorControl.PageRemoved += OnEventPageRemoved;
        _eventEditorControl.ContentChanged += OnEventContentChanged;
        _eventEditorControl.Pages = enginePages;
        TileEventEnginePlaceholder.Content = _eventEditorControl;

        TileConditionsControl.Conditions = _target.EventPages[0].Conditions;
    }

    private void OnEventPageSelected(int index)
    {
        TileConditionsControl.Conditions = index >= 0 && index < _target.EventPages.Count
            ? _target.EventPages[index].Conditions
            : null;
    }

    private void OnEventPageInserted(int index)
    {
        if (index >= 0 && index <= _target.EventPages.Count)
            _target.EventPages.Insert(index, new BookEventPage());
    }

    private void OnEventPageRemoved(int index)
    {
        if (index >= 0 && index < _target.EventPages.Count)
            _target.EventPages.RemoveAt(index);
    }

    private void OnEventContentChanged()
    {
        if (_eventEditorControl is null) return;
        var enginePages = _eventEditorControl.Pages;
        for (int i = 0; i < enginePages.Count && i < _target.EventPages.Count; i++)
            _target.EventPages[i].Commands = enginePages[i].Commands;
    }

    /// <summary>Same real debug tool every other event editor in the project uses - per spec ("ALL EVENT EDITORS LIKE THE ONE IN THE BOOK EDITOR SHOULD HAVE THE DEBUG FEATURE... PLEASE BE CONSISTENT"), this tab was missing it entirely until now.</summary>
    private void TileEventDebugButton_Click(object sender, RoutedEventArgs e)
    {
        if (TileConditionsControl.Conditions is not { } conditions)
        {
            MessageBox.Show("Select an event page first.", "Debug Event", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var commands = _eventEditorControl?.CurrentEditingPage?.Commands ?? new List<EventCommand>();

        var dialog = new BookEventDebugWindow(conditions, commands, _resources, _stats, _target.TileIndex) { Owner = this };
        dialog.Show();
    }

    private static CustomTileEffect CloneEffect(CustomTileEffect e) => new()
    {
        Name = e.Name,
        Trigger = e.Trigger,
        TickIntervalSeconds = e.TickIntervalSeconds,
        DamagePerTick = e.DamagePerTick,
        RampingDamage = e.RampingDamage,
        RampIncreasePerTick = e.RampIncreasePerTick,
        MitigatingObjectIds = new List<int>(e.MitigatingObjectIds),
        RecoverySeconds = e.RecoverySeconds,
        Notes = e.Notes
    };

    private void RefreshEffectsList()
    {
        EffectsListBox.ItemsSource = null;
        EffectsListBox.ItemsSource = _workingEffects
            .Select(e => $"{e.Name} [{e.Trigger}, {e.DamagePerTick}/tick{(e.RampingDamage ? " ramping" : "")}]")
            .ToList();
    }

    private void AddEffectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditCustomTileEffectWindow(_availableObjects) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _workingEffects.Add(dialog.Result);
            RefreshEffectsList();
        }
    }

    private void EditEffectButton_Click(object sender, RoutedEventArgs e)
    {
        int index = EffectsListBox.SelectedIndex;
        if (index < 0) return;

        var dialog = new EditCustomTileEffectWindow(_availableObjects, _workingEffects[index]) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _workingEffects[index] = dialog.Result;
            RefreshEffectsList();
        }
    }

    private void RemoveEffectButton_Click(object sender, RoutedEventArgs e)
    {
        int index = EffectsListBox.SelectedIndex;
        if (index < 0) return;

        _workingEffects.RemoveAt(index);
        RefreshEffectsList();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _target.TerrainLabel = TerrainLabelBox.Text;
        _target.Walkable = WalkableCheck.IsChecked == true;
        _target.IsWater = WaterCheck.IsChecked == true;
        _target.RequiredObjectIds = RequiredObjectsListBox.SelectedItems.Cast<WorldObject>().Select(o => o.Id).ToList();
        _target.CustomEffects = _workingEffects;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
