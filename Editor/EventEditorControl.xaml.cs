using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The shared, reusable Event Editor engine - the "right side" of every
/// event-editing UI in this app. Deliberately knows nothing about which
/// editor it's attached to or what conditions govern a page; it only owns
/// page management (New/Copy/Paste/Delete/Clear) and the command list
/// (click-and-play via a categorized picker, or manual typing validated by
/// Compile). Any future editor (NPC, Item, etc.) attaches this exact same
/// control the same way Book Editor does.
///
/// v1 scope, stated plainly: only "Show Text" and "Play Sound" are
/// implemented. The Add Command picker shows the full category structure
/// from the design doc so the shape is visible, but everything else is
/// disabled - proving the foundation without pretending more commands
/// exist yet.
/// </summary>
public partial class EventEditorControl : UserControl
{
    private ObservableCollection<EventCommandPage> _pages = new();
    private EventCommandPage? _clipboardPage;
    private bool _suppressTabSelection;

    /// <summary>Raised whenever a page is inserted, so the consumer can keep its own per-page data (e.g. Book's conditions) in sync at the same index.</summary>
    public event Action<int>? PageInserted;
    /// <summary>Raised whenever a page is removed, so the consumer can keep its own per-page data in sync.</summary>
    public event Action<int>? PageRemoved;
    /// <summary>Raised after any command-list edit is committed (click-and-play or successful Compile), so the consumer knows to persist.</summary>
    public event Action? ContentChanged;
    /// <summary>Raised when the selected page tab changes, so the consumer can show that page's own conditions on the left side.</summary>
    public event Action<int>? PageSelected;

    public EventEditorControl()
    {
        InitializeComponent();
    }

    /// <summary>The pages this control is editing. Setting this attaches a fresh set (e.g. when the consumer lazily loads on first tab click).</summary>
    public ObservableCollection<EventCommandPage> Pages
    {
        get => _pages;
        set
        {
            _pages = value;
            if (_pages.Count == 0) _pages.Add(new EventCommandPage { Label = "1" });
            RebuildPageTabs();
        }
    }

    /// <summary>Needed so Play Sound's picker can list real sounds - null until the host editor sets it, matching the pattern BookConditionsControl.SetDatabases already uses elsewhere.</summary>
    public ResourceDatabase? ResourceDatabase { get; set; }
    public VariableDatabase? Variables { get; set; }
    public List<VariableDefinition>? CurrentBookLocalVariables { get; set; }
    public BookDatabase? Books { get; set; }
    public ObjectDatabase? Objects { get; set; }
    public ContainerDatabase? Containers { get; set; }
    public SpriteDatabase? Sprites { get; set; }
    /// <summary>The book whose Event tab this control is currently editing - "self" for Change Self Book Content.</summary>
    public BookData? CurrentBook { get; set; }

    private void RebuildPageTabs()
    {
        _suppressTabSelection = true;
        PageTabs.Items.Clear();
        foreach (var page in _pages)
            PageTabs.Items.Add(new TabItem { Header = page.Label });
        _suppressTabSelection = false;
        PageTabs.SelectedIndex = _pages.Count > 0 ? 0 : -1;
        RenderContentsList();
    }

    private EventCommandPage? CurrentPage => PageTabs.SelectedIndex >= 0 && PageTabs.SelectedIndex < _pages.Count
        ? _pages[PageTabs.SelectedIndex]
        : null;

    /// <summary>Public read access to the currently-selected page, so a host editor (Book's Preview button) can grab its live Commands list without this control needing to know anything about Preview itself.</summary>
    public EventCommandPage? CurrentEditingPage => CurrentPage;

    private void PageTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelection) return;
        SetTextMode(false);
        RenderContentsList();
        PageSelected?.Invoke(PageTabs.SelectedIndex);
    }

    // ---------- Page management ----------

    private void NewPageButton_Click(object sender, RoutedEventArgs e)
    {
        int insertAt = PageTabs.SelectedIndex + 1;
        if (insertAt <= 0) insertAt = _pages.Count;
        var newPage = new EventCommandPage { Label = (_pages.Count + 1).ToString() };
        _pages.Insert(insertAt, newPage);
        RelabelPagesSequentially();
        PageInserted?.Invoke(insertAt);
        RebuildPageTabs();
        PageTabs.SelectedIndex = insertAt;
    }

    private void CopyPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPage is null) return;
        _clipboardPage = new EventCommandPage
        {
            Label = CurrentPage.Label,
            Commands = CurrentPage.Commands.Select(c => new EventCommand { Type = c.Type, IndentLevel = c.IndentLevel, Parameters = new Dictionary<string, string>(c.Parameters) }).ToList()
        };
    }

    private void PastePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_clipboardPage is null)
        {
            MessageBox.Show("Copy an event page first.", "Paste Event Page", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int insertAt = PageTabs.SelectedIndex + 1;
        if (insertAt <= 0) insertAt = _pages.Count;
        var pasted = new EventCommandPage
        {
            Label = (_pages.Count + 1).ToString(),
            Commands = _clipboardPage.Commands.Select(c => new EventCommand { Type = c.Type, IndentLevel = c.IndentLevel, Parameters = new Dictionary<string, string>(c.Parameters) }).ToList()
        };
        _pages.Insert(insertAt, pasted);
        RelabelPagesSequentially();
        PageInserted?.Invoke(insertAt);
        RebuildPageTabs();
        PageTabs.SelectedIndex = insertAt;
        ContentChanged?.Invoke();
    }

    private void DeletePageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pages.Count <= 1)
        {
            MessageBox.Show("An event must have at least one page.", "Delete Event Page", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int index = PageTabs.SelectedIndex;
        if (index < 0) return;
        _pages.RemoveAt(index);
        RelabelPagesSequentially();
        PageRemoved?.Invoke(index);
        RebuildPageTabs();
        PageTabs.SelectedIndex = Math.Min(index, _pages.Count - 1);
        ContentChanged?.Invoke();
    }

    private void ClearPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPage is null) return;
        var result = MessageBox.Show("Clear all commands on this page?", "Clear Event Page", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        CurrentPage.Commands.Clear();
        RenderContentsList();
        ContentChanged?.Invoke();
    }

    private void RelabelPagesSequentially()
    {
        for (int i = 0; i < _pages.Count; i++)
            _pages[i].Label = (i + 1).ToString();
    }

    // ---------- Contents list (click-and-play view) ----------

    private void RenderContentsList()
    {
        ContentsListBox.Items.Clear();
        if (CurrentPage is null) return;

        foreach (var command in CurrentPage.Commands)
            ContentsListBox.Items.Add(BuildCommandLine(command));

        // Trailing empty marker - RPG-Maker style "click/double-click here to add the next command".
        var emptyLine = new TextBlock();
        emptyLine.Inlines.Add(new Run("\u25C6") { Foreground = Brushes.Black }); // solid diamond
        ContentsListBox.Items.Add(emptyLine);
    }

    private TextBlock BuildCommandLine(EventCommand command)
    {
        var block = new TextBlock { Margin = new Thickness(command.IndentLevel * 20, 0, 0, 0) };
        block.Inlines.Add(new Run("\u25C6") { Foreground = Brushes.Red, FontWeight = FontWeights.Bold }); // hollow-style marker, start of command
        block.Inlines.Add(new Run(command.Type.ToString()) { Foreground = Brushes.Red, FontWeight = FontWeights.Bold });

        if (command.Type == EventCommandType.PlaySound)
        {
            string soundName = "(no sound selected)";
            if (int.TryParse(command.Get("SoundResourceId"), out int id))
            {
                var entry = ResourceDatabase?.Entries.FirstOrDefault(e => e.Id == id);
                if (entry is not null) soundName = entry.Name;
            }
            block.Inlines.Add(new Run($" : {soundName} (Vol {command.Get("Volume")})"));
        }
        else if (command.Type == EventCommandType.PlayMusic)
        {
            string musicName = "(no music selected)";
            if (int.TryParse(command.Get("MusicResourceId"), out int id))
            {
                var entry = ResourceDatabase?.Entries.FirstOrDefault(e => e.Id == id);
                if (entry is not null) musicName = entry.Name;
            }
            block.Inlines.Add(new Run($" : {musicName} (Vol {command.Get("Volume")})"));
        }
        else if (command.Type == EventCommandType.SetSwitch)
        {
            block.Inlines.Add(new Run($" : {command.Get("SwitchName")} = {command.Get("Value")}"));
        }
        else if (command.Type == EventCommandType.SetVariable)
        {
            block.Inlines.Add(new Run($" : {command.Get("VariableName")} {command.Get("Operation")} {command.Get("Value")}"));
        }
        else if (command.Type == EventCommandType.ConditionalBranchSwitch)
        {
            block.Inlines.Add(new Run($" : If {command.Get("SwitchName")} is {command.Get("Value")}"));
        }
        else if (command.Type == EventCommandType.ConditionalBranchVariable)
        {
            block.Inlines.Add(new Run($" : If {command.Get("VariableName")} {command.Get("Comparison")} {command.Get("Value")}"));
        }
        else if (command.Type == EventCommandType.ShakeScreen)
        {
            string affected = command.Get("AffectsCharacters") == "True" ? ", characters fall" : "";
            block.Inlines.Add(new Run($" : Intensity {command.Get("Intensity")}, {command.Get("DurationSeconds")}s{affected}"));
        }
        else if (command.Type == EventCommandType.ChangeDayNight)
        {
            block.Inlines.Add(new Run($" : {command.Get("Period")}"));
        }
        else if (command.Type == EventCommandType.ApplyPaletteColor)
        {
            block.Inlines.Add(new Run($" : {command.Get("PaletteGroup")} #{command.Get("ColorIndex")} (#{command.Get("Hex")}), {command.Get("OpacityPercent")}%"));
        }
        else if (command.Type == EventCommandType.ChangeSelfBookContent)
        {
            int.TryParse(command.Get("PageIndex"), out int pageIdx);
            block.Inlines.Add(new Run($" : Page {pageIdx + 1}, frame id {command.Get("FrameId")}"));
        }
        else if (command.Type == EventCommandType.ChangeExternalBookContent)
        {
            int.TryParse(command.Get("PageIndex"), out int pageIdx);
            var targetBook = Books?.Books.FirstOrDefault(b => b.Id.ToString() == command.Get("TargetBookId"));
            string bookLabel = targetBook?.Name ?? $"book #{command.Get("TargetBookId")}";
            block.Inlines.Add(new Run($" : {bookLabel}, page {pageIdx + 1}, frame id {command.Get("FrameId")}"));
        }
        else if (command.Type == EventCommandType.ChangeFactionRelationship)
        {
            block.Inlines.Add(new Run($" : {ChangeFactionRelationshipCommandWindow.Describe(command)}"));
        }
        else if (command.Type == EventCommandType.ChangeFaction)
        {
            block.Inlines.Add(new Run($" : {ChangeFactionCommandWindow.Describe(command)}"));
        }
        else if (command.Type == EventCommandType.Teleport)
        {
            block.Inlines.Add(new Run($" : {TeleportCommandWindow.Describe(command)}"));
        }
        else if (command.Type == EventCommandType.ChangeBehavior)
        {
            block.Inlines.Add(new Run($" : {ChangeBehaviorCommandWindow.Describe(command)}"));
        }
        else if (command.Type == EventCommandType.ChangeFoodLevel)
        {
            block.Inlines.Add(new Run($" : {FoodLevelCommandWindow.Describe(command)}"));
        }
        else if (command.Type == EventCommandType.ModifyAilments)
        {
            block.Inlines.Add(new Run($" : {ModifyAilmentsCommandWindow.Describe(command)}"));
        }
        else if (command.Type == EventCommandType.Attack)
        {
            block.Inlines.Add(new Run($" : {AttackCommandWindow.Describe(command)}"));
        }
        else if (command.Type == EventCommandType.ShowText)
        {
            string speaker = ShowTextSpeakerLabel(command.Get("SpeakerId"));
            block.Inlines.Add(new Run($" : {(speaker.Length > 0 ? speaker + ": " : "")}{Truncate(command.Get("Text"), 50)}"));
        }
        else if (command.Parameters.Count > 0)
        {
            string paramSummary = string.Join(", ", command.Parameters.Select(kv => Truncate(kv.Value, 30)));
            block.Inlines.Add(new Run($" : {paramSummary}"));
        }
        return block;
    }

    private static string ShowTextSpeakerLabel(string key) => key switch
    {
        "" => "",
        "Self" => "Self",
        "Player" => "Main Player",
        _ => ProjectSession.Characters.Characters.FirstOrDefault(c => c.Id.ToString() == key)?.Name ?? $"character #{key}"
    };

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "..." : s;

    private void ContentsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CurrentPage is null) return;
        int index = ContentsListBox.SelectedIndex;

        if (index < 0 || index >= CurrentPage.Commands.Count)
        {
            // Double-clicked the trailing empty marker - same as Add Command.
            OpenAddCommandPicker();
            return;
        }

        EditCommandAt(index);
    }

    private void AddCommandButton_Click(object sender, RoutedEventArgs e) => OpenAddCommandPicker();

    private void OpenAddCommandPicker()
    {
        if (CurrentPage is null) return;
        var picker = new AddCommandPickerWindow { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true || picker.ChosenType is not EventCommandType type) return;

        // Insert right AFTER whatever's currently selected - not always at
        // the end of the list. This was a real bug: there was no way to
        // add a command before an Else (select the line right before it,
        // Add always landed past EndBranch instead), and a command added
        // after Else the same way. Nothing selected (or the trailing empty
        // marker) still means "add at the end", same as before.
        int selected = ContentsListBox.SelectedIndex;
        int insertAt = (selected >= 0 && selected < CurrentPage.Commands.Count) ? selected + 1 : CurrentPage.Commands.Count;
        int indentLevel = ComputeInsertIndentLevel(insertAt);

        if (type == EventCommandType.ShowText)
        {
            var dialog = new ShowTextCommandWindow { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.PlaySound)
        {
            var dialog = new PlaySoundCommandWindow(ResourceDatabase ?? new ResourceDatabase()) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.PlayMusic)
        {
            var dialog = new PlayMusicCommandWindow(ResourceDatabase ?? new ResourceDatabase()) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.SetSwitch)
        {
            var dialog = new SetSwitchCommandWindow(Variables ?? new VariableDatabase(), CurrentBookLocalVariables ?? new List<VariableDefinition>()) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.SetVariable)
        {
            var dialog = new SetVariableCommandWindow(Variables ?? new VariableDatabase(), CurrentBookLocalVariables ?? new List<VariableDefinition>()) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.ConditionalBranchSwitch)
        {
            var dialog = new ConditionalBranchCommandWindow(Variables ?? new VariableDatabase(), CurrentBookLocalVariables ?? new List<VariableDefinition>()) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                foreach (var cmd in dialog.Results)
                {
                    cmd.IndentLevel = indentLevel; // branch, its Else, and its EndBranch are siblings - all at the same level, only what's between them goes one deeper
                    CurrentPage.Commands.Insert(insertAt, cmd);
                    insertAt++;
                }
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.ShakeScreen)
        {
            var dialog = new ShakeScreenCommandWindow { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.ChangeDayNight)
        {
            var dialog = new ChangeDayNightCommandWindow { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.ApplyPaletteColor)
        {
            var dialog = new ApplyPaletteColorCommandWindow { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type is EventCommandType.ChangeSelfBookContent or EventCommandType.ChangeExternalBookContent)
        {
            if (CurrentBook is null || Books is null || Objects is null || Sprites is null || ResourceDatabase is null)
            {
                MessageBox.Show("This event editor isn't fully wired to its databases yet.", "Change Book Content", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            bool isExternal = type == EventCommandType.ChangeExternalBookContent;
            var dialog = new ChangeBookContentCommandWindow(isExternal, CurrentBook, Books, Objects, Containers, Sprites, ResourceDatabase) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type is EventCommandType.ChangeFactionRelationship or EventCommandType.ChangeFaction or EventCommandType.Teleport or EventCommandType.ChangeBehavior
                 or EventCommandType.ChangeFoodLevel or EventCommandType.ModifyAilments or EventCommandType.Attack)
        {
            FactionCommandWindowBase dialog = type switch
            {
                EventCommandType.Attack => new AttackCommandWindow(),
                EventCommandType.ChangeFoodLevel => new FoodLevelCommandWindow(),
                EventCommandType.ModifyAilments => new ModifyAilmentsCommandWindow(),
                EventCommandType.ChangeFaction => new ChangeFactionCommandWindow(),
                EventCommandType.Teleport => new TeleportCommandWindow(),
                EventCommandType.ChangeBehavior => new ChangeBehaviorCommandWindow(),
                _ => new ChangeFactionRelationshipCommandWindow()
            };
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (type == EventCommandType.ChangeBookSettings)
        {
            if (Books is null || Objects is null || Sprites is null)
            {
                MessageBox.Show("This event editor isn't fully wired to its databases yet.", "Change Book Settings", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var dialog = new ChangeBookSettingsCommandWindow(Books, Objects, Containers, Sprites) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = indentLevel;
                CurrentPage.Commands.Insert(insertAt, dialog.Result);
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// A command inherits the indent of whatever's immediately before its
    /// insertion point - UNLESS that command opens a new block (a
    /// Conditional Branch or an Else), in which case the new command goes
    /// one level deeper, since it's now the first thing inside that block.
    /// EndBranch does NOT open a block - a command added right after an
    /// EndBranch sits back at that EndBranch's own level, not one deeper.
    /// This is a purely local rule (only looks at the one preceding
    /// command), which is sufficient because IndentLevel is already
    /// correctly maintained transitively: whatever came before it was
    /// itself computed the same way.
    /// </summary>
    private int ComputeInsertIndentLevel(int insertAt)
    {
        if (CurrentPage is null || insertAt <= 0 || insertAt > CurrentPage.Commands.Count) return 0;
        var previous = CurrentPage.Commands[insertAt - 1];
        bool opensBlock = previous.Type is EventCommandType.ConditionalBranchSwitch or EventCommandType.ConditionalBranchVariable or EventCommandType.Else;
        return opensBlock ? previous.IndentLevel + 1 : previous.IndentLevel;
    }

    private void EditCommandAt(int index)
    {
        if (CurrentPage is null) return;
        var command = CurrentPage.Commands[index];

        if (command.Type == EventCommandType.ShowText)
        {
            var dialog = new ShowTextCommandWindow(command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel; // editing must preserve the existing command's position in the nesting - a fresh object here defaults to 0
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.PlaySound)
        {
            var dialog = new PlaySoundCommandWindow(ResourceDatabase ?? new ResourceDatabase(), command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel; // editing must preserve the existing command's position in the nesting - a fresh object here defaults to 0
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.PlayMusic)
        {
            var dialog = new PlayMusicCommandWindow(ResourceDatabase ?? new ResourceDatabase(), command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel; // editing must preserve the existing command's position in the nesting - a fresh object here defaults to 0
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.SetSwitch)
        {
            var dialog = new SetSwitchCommandWindow(Variables ?? new VariableDatabase(), CurrentBookLocalVariables ?? new List<VariableDefinition>(), command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel; // editing must preserve the existing command's position in the nesting - a fresh object here defaults to 0
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.SetVariable)
        {
            var dialog = new SetVariableCommandWindow(Variables ?? new VariableDatabase(), CurrentBookLocalVariables ?? new List<VariableDefinition>(), command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel; // editing must preserve the existing command's position in the nesting - a fresh object here defaults to 0
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type is EventCommandType.ConditionalBranchSwitch or EventCommandType.ConditionalBranchVariable)
        {
            var dialog = new ConditionalBranchCommandWindow(Variables ?? new VariableDatabase(), CurrentBookLocalVariables ?? new List<VariableDefinition>(), command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.Results.Count > 0)
            {
                dialog.Results[0].IndentLevel = command.IndentLevel; // preserve nesting position, same as every other edit path
                CurrentPage.Commands[index] = dialog.Results[0]; // editing an existing branch never touches its Else/EndBranch - see the window's own doc comment
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.ShakeScreen)
        {
            var dialog = new ShakeScreenCommandWindow(command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel;
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.ChangeDayNight)
        {
            var dialog = new ChangeDayNightCommandWindow(command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel;
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.ApplyPaletteColor)
        {
            var dialog = new ApplyPaletteColorCommandWindow(command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel;
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type is EventCommandType.ChangeSelfBookContent or EventCommandType.ChangeExternalBookContent)
        {
            if (CurrentBook is null || Books is null || Objects is null || Sprites is null || ResourceDatabase is null)
            {
                MessageBox.Show("This event editor isn't fully wired to its databases yet.", "Change Book Content", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            bool isExternal = command.Type == EventCommandType.ChangeExternalBookContent;
            var dialog = new ChangeBookContentCommandWindow(isExternal, CurrentBook, Books, Objects, Containers, Sprites, ResourceDatabase, command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel;
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type is EventCommandType.ChangeFactionRelationship or EventCommandType.ChangeFaction or EventCommandType.Teleport or EventCommandType.ChangeBehavior
                 or EventCommandType.ChangeFoodLevel or EventCommandType.ModifyAilments or EventCommandType.Attack)
        {
            FactionCommandWindowBase dialog = command.Type switch
            {
                EventCommandType.Attack => new AttackCommandWindow(command),
                EventCommandType.ChangeFoodLevel => new FoodLevelCommandWindow(command),
                EventCommandType.ModifyAilments => new ModifyAilmentsCommandWindow(command),
                EventCommandType.ChangeFaction => new ChangeFactionCommandWindow(command),
                EventCommandType.Teleport => new TeleportCommandWindow(command),
                EventCommandType.ChangeBehavior => new ChangeBehaviorCommandWindow(command),
                _ => new ChangeFactionRelationshipCommandWindow(command)
            };
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel;
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
        else if (command.Type == EventCommandType.ChangeBookSettings)
        {
            if (Books is null || Objects is null || Sprites is null)
            {
                MessageBox.Show("This event editor isn't fully wired to its databases yet.", "Change Book Settings", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var dialog = new ChangeBookSettingsCommandWindow(Books, Objects, Containers, Sprites, command) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                dialog.Result.IndentLevel = command.IndentLevel;
                CurrentPage.Commands[index] = dialog.Result;
                RenderContentsList();
                ContentChanged?.Invoke();
            }
        }
    }

    private void DeleteCommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPage is null) return;
        int index = ContentsListBox.SelectedIndex;
        if (index < 0 || index >= CurrentPage.Commands.Count) return;
        CurrentPage.Commands.RemoveAt(index);
        RenderContentsList();
        ContentChanged?.Invoke();
    }

    // ---------- Manual typing (text view) + Compile/Validate ----------

    private void EditAsTextToggle_Click(object sender, RoutedEventArgs e)
    {
        SetTextMode(EditAsTextToggle.IsChecked == true);
    }

    private bool _suppressHighlight;
    private static readonly HashSet<string> KnownCommandNames = Enum.GetNames<EventCommandType>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void SetTextMode(bool enabled)
    {
        EditAsTextToggle.IsChecked = enabled;
        CompileButton.IsEnabled = enabled;
        ContentsListBox.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ScriptTextBox.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && CurrentPage is not null)
        {
            _suppressHighlight = true;
            new TextRange(ScriptTextBox.Document.ContentStart, ScriptTextBox.Document.ContentEnd).Text = EventScriptParser.ToScriptText(CurrentPage.Commands);
            _suppressHighlight = false;
            HighlightKeywords(preserveCaret: false);
        }
    }

    /// <summary>
    /// Colors the command keyword at the start of each line (matching the
    /// Contents list's red/bold style) - rebuilds the whole document on
    /// every keystroke to do it, which is a real, accepted trade-off for
    /// simplicity over typing speed on longer scripts.
    /// </summary>
    private void HighlightKeywords(bool preserveCaret = true)
    {
        if (_suppressHighlight) return;
        _suppressHighlight = true;
        try
        {
            var doc = ScriptTextBox.Document;
            int caretOffset = preserveCaret ? doc.ContentStart.GetOffsetToPosition(ScriptTextBox.CaretPosition) : 0;
            string fullText = new TextRange(doc.ContentStart, doc.ContentEnd).Text;

            doc.Blocks.Clear();
            foreach (var line in fullText.Replace("\r\n", "\n").Split('\r', '\n'))
            {
                var para = new Paragraph { Margin = new Thickness(0) };
                int parenIndex = line.IndexOf('(');
                string keyword = parenIndex > 0 ? line[..parenIndex].Trim() : string.Empty;

                if (parenIndex > 0 && KnownCommandNames.Contains(keyword))
                {
                    int keywordStart = line.IndexOf(keyword, StringComparison.Ordinal);
                    if (keywordStart > 0) para.Inlines.Add(new Run(line[..keywordStart]));
                    para.Inlines.Add(new Run(keyword) { Foreground = Brushes.Red, FontWeight = FontWeights.Bold });
                    para.Inlines.Add(new Run(line[(keywordStart + keyword.Length)..]));
                }
                else
                {
                    para.Inlines.Add(new Run(line));
                }
                doc.Blocks.Add(para);
            }

            if (preserveCaret)
            {
                int total = doc.ContentStart.GetOffsetToPosition(doc.ContentEnd);
                var restored = doc.ContentStart.GetPositionAtOffset(Math.Min(caretOffset, total));
                if (restored is not null) ScriptTextBox.CaretPosition = restored;
            }
        }
        finally
        {
            _suppressHighlight = false;
        }
    }

    private void ScriptTextBox_TextChanged(object sender, TextChangedEventArgs e) => HighlightKeywords();

    private void CompileButton_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPage is null) return;
        string scriptText = new TextRange(ScriptTextBox.Document.ContentStart, ScriptTextBox.Document.ContentEnd).Text;
        var (commands, error) = EventScriptParser.Parse(scriptText);
        if (error is not null)
        {
            MessageBox.Show(error, "Compile Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        CurrentPage.Commands = commands!;
        SetTextMode(false);
        RenderContentsList();
        ContentChanged?.Invoke();
    }
}
