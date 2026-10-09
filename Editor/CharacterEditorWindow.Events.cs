using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Event tab: the same multi-page event editor books and containers
/// have - conditions on the left, commands on the right - but with the
/// character's own triggers: By Equip, By UnEquip, By Hit, By Ailment
/// Effect, By Death. "Self" in the commands is this character.
/// </summary>
public partial class CharacterEditorWindow
{
    private EventEditorControl? _characterEventEditor;
    private CharacterDefinition? _eventCharacter;

    private void BuildEventTab(CharacterDefinition character)
    {
        _eventCharacter = character;
        // Never headless: a character's events always have at least one page, and every page a character trigger.
        if (character.EventPages.Count == 0) character.EventPages.Add(NewCharacterEventPage());
        foreach (var page in character.EventPages)
            if (!CharacterTriggers.IsCharacterTrigger(page.Conditions.TriggerConditionBased)) page.Conditions.TriggerConditionBased = BookTriggerConditionBased.ByEquip;

        var pages = new ObservableCollection<EventCommandPage>(character.EventPages.Select((p, i) => new EventCommandPage { Label = (i + 1).ToString(), Commands = p.Commands }));
        _characterEventEditor = new EventEditorControl
        {
            ResourceDatabase = _resources,
            Variables = ProjectSession.VariablesProvider?.Invoke(),
            CurrentBookLocalVariables = new List<VariableDefinition>(),
            Books = _books,
            Objects = _objects,
            Containers = ProjectSession.ContainersProvider?.Invoke(),
            Sprites = _sprites
        };
        _characterEventEditor.PageSelected += index =>
        {
            if (_eventCharacter is null) return;
            CharacterConditionsControl.Conditions = index >= 0 && index < _eventCharacter.EventPages.Count ? _eventCharacter.EventPages[index].Conditions : null;
        };
        _characterEventEditor.PageInserted += index =>
        {
            if (_eventCharacter is not null && index >= 0 && index <= _eventCharacter.EventPages.Count) _eventCharacter.EventPages.Insert(index, NewCharacterEventPage());
        };
        _characterEventEditor.PageRemoved += index =>
        {
            if (_eventCharacter is not null && index >= 0 && index < _eventCharacter.EventPages.Count) _eventCharacter.EventPages.RemoveAt(index);
        };
        _characterEventEditor.ContentChanged += () =>
        {
            // Some edits replace a page's command list outright - keep the character's pages pointing at the live lists.
            if (_eventCharacter is null || _characterEventEditor is null) return;
            var enginePages = _characterEventEditor.Pages;
            for (int i = 0; i < enginePages.Count && i < _eventCharacter.EventPages.Count; i++) _eventCharacter.EventPages[i].Commands = enginePages[i].Commands;
        };
        _characterEventEditor.SelfCharacter = character;
        _characterEventEditor.Pages = pages;
        CharacterEventEnginePlaceholder.Content = _characterEventEditor;

        var stats = ProjectSession.StatsProvider?.Invoke() ?? _stats;
        CharacterConditionsControl.CurrentBookId = -1;
        CharacterConditionsControl.CurrentBookLocalVariables = new List<VariableDefinition>();
        CharacterConditionsControl.SetDatabases(_books, _objects, ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase(), _sprites,
            ProjectSession.VariablesProvider?.Invoke() ?? new VariableDatabase(), stats);
        CharacterConditionsControl.ApplyCharacterTriggerChoices();
        CharacterConditionsControl.Conditions = character.EventPages[0].Conditions;
    }

    private static BookEventPage NewCharacterEventPage()
    {
        var page = new BookEventPage();
        page.Conditions.TriggerConditionBased = BookTriggerConditionBased.ByEquip;
        return page;
    }

    private void CharacterEventDebugButton_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterConditionsControl.Conditions is not { } conditions)
        {
            MessageBox.Show(this, "Select an event page first.", "Debug Event", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var commands = _characterEventEditor?.CurrentEditingPage?.Commands ?? new List<EventCommand>();
        new BookEventDebugWindow(conditions, commands, _resources, _stats, -1) { Owner = this }.Show();
    }
}
