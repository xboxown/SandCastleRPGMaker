using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// What one paperdoll part does (PaperDoll Editor: double-click the part) -
/// the same event pages, conditions and commands as every object's Event
/// tab, with the triggers named for what happens to a part in play.
/// </summary>
public class DollPartEventWindow : Window
{
    /// <summary>The triggers a part fires, named for what happens to it.</summary>
    public static IReadOnlyDictionary<BookTriggerConditionBased, string> TriggerLabels(DollPartKind kind)
    {
        var labels = new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = kind switch
            {
                DollPartKind.Button => "By Press (click)",
                DollPartKind.Switch => "By Switched (click)",
                DollPartKind.Lid => "By Opened or Closed (double-click)",
                DollPartKind.Slider => "By Value Changed (click)",
                DollPartKind.CloseButton => "By Close (click)",
                _ => "By Use (double-click)"
            }
        };
        labels[BookTriggerConditionBased.ByCombineItem] = "By Combined With An Item (dropped on it)";
        labels[BookTriggerConditionBased.ByCombinePart] = "By Combined With A Part (dragged onto it)";
        if (kind is DollPartKind.Slot or DollPartKind.ItemArea)
        {
            labels[BookTriggerConditionBased.ByDropItem] = "By Item Put In";
            labels[BookTriggerConditionBased.ByPickupItem] = "By Item Taken Out";
        }
        return labels;
    }

    public DollPartEventWindow(DollDesign design, DollPart part, ObjectDatabase objects, SpriteDatabase sprites, ResourceDatabase resources,
        BookDatabase books, ContainerDatabase containers, VariableDatabase variables, StatDatabase stats)
    {
        Title = $"Events - {part.Name} ({DollPart.KindName(part.Kind)}) on \"{design.Name}\" [{BuildInfo.CurrentBuild}]";
        Width = 1180;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (part.EventPages.Count == 0) part.EventPages.Add(new BookEventPage()); // never headless, as books, containers and objects
        var pages = new ObservableCollection<EventCommandPage>(part.EventPages.Select((p, i) => new EventCommandPage { Label = (i + 1).ToString(), Commands = p.Commands }));
        var conditions = new BookEventConditionsControl();
        var editor = new EventEditorControl
        {
            ResourceDatabase = resources,
            Variables = variables,
            CurrentBookLocalVariables = new List<VariableDefinition>(),
            Books = books,
            Objects = objects,
            Containers = containers,
            Sprites = sprites,
            CurrentBook = null
        };
        // By Combined With A Part: which part (this page's own choice).
        var others = new List<DollPart> { new() { Id = -1, Name = "(any part)" } };
        others.AddRange(design.Parts.Where(p => p.Id != part.Id));
        var withPart = new ComboBox { Width = 220, ItemsSource = others, DisplayMemberPath = nameof(DollPart.Name), Margin = new Thickness(6, 0, 0, 0) };
        BookEventConditions? shown = part.EventPages[0].Conditions;
        void ShowWithPart() => withPart.SelectedItem = others.FirstOrDefault(p => p.Id == (shown?.CombinedPartId ?? -1)) ?? others[0];
        withPart.SelectionChanged += (_, _) => { if (shown is not null && withPart.SelectedItem is DollPart chosen) shown.CombinedPartId = chosen.Id; };
        editor.PageSelected += index =>
        {
            shown = index >= 0 && index < part.EventPages.Count ? part.EventPages[index].Conditions : null;
            conditions.Conditions = shown;
            ShowWithPart();
        };
        ShowWithPart();
        editor.PageInserted += index => { if (index >= 0 && index <= part.EventPages.Count) part.EventPages.Insert(index, new BookEventPage()); };
        editor.PageRemoved += index => { if (index >= 0 && index < part.EventPages.Count) part.EventPages.RemoveAt(index); };
        editor.ContentChanged += () =>
        {
            for (int i = 0; i < editor.Pages.Count && i < part.EventPages.Count; i++) part.EventPages[i].Commands = editor.Pages[i].Commands;
        };
        editor.Pages = pages;

        conditions.CurrentBookId = -1;
        conditions.CurrentBookLocalVariables = new List<VariableDefinition>();
        conditions.SetDatabases(books, objects, containers, sprites, variables, stats);
        conditions.ApplyTriggerLabels(TriggerLabels(part.Kind));
        conditions.Conditions = part.EventPages[0].Conditions;

        var help = new TextBlock
        {
            Text = Help(part.Kind), TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 6)
        };
        var partRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        partRow.Children.Add(new TextBlock { Text = "By Combined With A Part - only the part:", VerticalAlignment = VerticalAlignment.Center });
        partRow.Children.Add(withPart);
        partRow.Children.Add(new TextBlock { Text = "  (a part ticked \"Can be dragged onto other parts\")", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 11 });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 1, 0), Child = conditions });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(help, Dock.Top);
        dock.Children.Add(help);
        DockPanel.SetDock(partRow, Dock.Top);
        dock.Children.Add(partRow);
        dock.Children.Add(grid);
        Content = dock;
        // The Paperdoll command lists this paperdoll's parts while this window is open.
        DollCommandWindow.CurrentDesign = design;
        Closed += (_, _) => DollCommandWindow.CurrentDesign = null;
    }

    private static string Help(DollPartKind kind) => kind switch
    {
        DollPartKind.Button => "Runs when the button is clicked in play (it shows its Pressed look first). A Play Sound started here keeps playing after the paperdoll is closed.",
        DollPartKind.Switch => "Runs each time it's clicked to its next look.",
        DollPartKind.Lid => "Runs when it's double-clicked open or shut (it has already opened or closed).",
        DollPartKind.Slot or DollPartKind.ItemArea => "By Item Put In: something was just put in. By Item Taken Out: something was just taken out. By Use: double-clicked. Pick an item under the trigger to react to that item only.",
        DollPartKind.Slider => "Runs when its value is changed by a click on its track.",
        DollPartKind.CloseButton => "Runs as it closes the paperdoll.",
        _ => "Runs when the part is double-clicked in play."
    } + " By Combined With An Item: an item (from the world, a pack, or this paperdoll) dropped on it - pick one to react to it only; it stays where it was unless the Paperdoll command uses it up. Commands that change the paperdoll: Paperdoll... (Movement / World).";
}
