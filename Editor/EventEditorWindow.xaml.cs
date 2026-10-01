using System.Collections.ObjectModel;
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map event editing, rebuilt to share the exact same mechanics as the
/// Book Editor's own Event tab - EventEditorControl (multi-page command
/// list, every command type it knows how to build) and
/// BookEventConditionsControl (the same condition system, duplicated here
/// deliberately per spec until map events get their own). Any command type
/// added to EventEditorControl later shows up here automatically too -
/// there is exactly one place that mechanic lives, matching the whole
/// point of building this as a shared control rather than a second,
/// parallel implementation.
///
/// GameEvent has no "self book" - Change Self Book Content will correctly
/// show its own "isn't fully wired" error if picked here, since there
/// genuinely isn't a book context for a map event to resolve "self" to.
/// Change External Book Content still works fine, since it never needs
/// "self" at all.
/// </summary>
public partial class EventEditorWindow : Window
{
    private readonly GameEvent _target;
    private EventEditorControl? _eventEditorControl;
    private readonly ResourceDatabase _resources;
    private readonly StatDatabase _stats;

    public EventEditorWindow(GameEvent target, ResourceDatabase resources, VariableDatabase variables, BookDatabase books, ObjectDatabase objects, SpriteDatabase sprites, StatDatabase stats, ContainerDatabase containers)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        _target = target;
        _resources = resources;
        _stats = stats;
        NameBox.Text = target.Name;

        ConditionsControl.SetDatabases(books, objects, containers, sprites, variables, stats);

        // Never headless - an event with zero pages/conditions is impossible
        // by design, so guarantee at least one page exists, same rule as
        // the Book Editor's own EventPages.
        if (_target.EventPages.Count == 0)
            _target.EventPages.Add(new BookEventPage());

        var enginePages = new ObservableCollection<EventCommandPage>(
            _target.EventPages.Select((p, i) => new EventCommandPage { Label = (i + 1).ToString(), Commands = p.Commands }));

        _eventEditorControl = new EventEditorControl
        {
            ResourceDatabase = resources,
            Variables = variables,
            CurrentBookLocalVariables = new List<VariableDefinition>(), // map events have no "book" to scope local variables to - global variables only, for now
            Books = books,
            Objects = objects,
            Sprites = sprites,
            CurrentBook = null // no self-book context for a map event
        };
        _eventEditorControl.PageSelected += OnEventPageSelected;
        _eventEditorControl.PageInserted += OnEventPageInserted;
        _eventEditorControl.PageRemoved += OnEventPageRemoved;
        _eventEditorControl.ContentChanged += OnEventContentChanged;
        _eventEditorControl.Pages = enginePages;
        EventEnginePlaceholder.Content = _eventEditorControl;

        ConditionsControl.Conditions = _target.EventPages[0].Conditions;
    }

    private void OnEventPageSelected(int index)
    {
        ConditionsControl.Conditions = index >= 0 && index < _target.EventPages.Count
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
        // Some engine operations (Compile, Paste Event Page) replace a
        // page's Commands list wholesale rather than mutating it in place -
        // re-sync every page's Commands after any change rather than trying
        // to keep every mutation path perfectly reference-aligned.
        if (_eventEditorControl is null) return;
        var enginePages = _eventEditorControl.Pages;
        for (int i = 0; i < enginePages.Count && i < _target.EventPages.Count; i++)
            _target.EventPages[i].Commands = enginePages[i].Commands;
    }

    /// <summary>Same real debug tool every other event editor in the project uses - per spec ("ALL EVENT EDITORS LIKE THE ONE IN THE BOOK EDITOR SHOULD HAVE THE DEBUG FEATURE... PLEASE BE CONSISTENT"), this window was missing it entirely until now.</summary>
    private void EventDebugButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConditionsControl.Conditions is not { } conditions)
        {
            MessageBox.Show("Select an event page first.", "Debug Event", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var commands = _eventEditorControl?.CurrentEditingPage?.Commands ?? new List<EventCommand>();

        var dialog = new BookEventDebugWindow(conditions, commands, _resources, _stats, _target.Id) { Owner = this };
        dialog.Show();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _target.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _target.Name : NameBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
