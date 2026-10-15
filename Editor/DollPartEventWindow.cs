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
        editor.PageSelected += index => conditions.Conditions = index >= 0 && index < part.EventPages.Count ? part.EventPages[index].Conditions : null;
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
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 1, 0), Child = conditions });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(help, Dock.Top);
        dock.Children.Add(help);
        dock.Children.Add(grid);
        Content = dock;
    }

    private static string Help(DollPartKind kind) => kind switch
    {
        DollPartKind.Button => "Runs when the button is clicked in play (it shows its Pressed look first). A Play Sound started here keeps playing after the paperdoll is closed.",
        DollPartKind.Switch => "Runs each time it's clicked to its next look.",
        DollPartKind.Lid => "Runs when it's double-clicked open or shut (it has already opened or closed).",
        DollPartKind.Slot or DollPartKind.ItemArea => "By Item Put In: something was just put in. By Item Taken Out: something was just taken out. By Use: double-clicked.",
        DollPartKind.Slider => "Runs when its value is changed by a click on its track.",
        DollPartKind.CloseButton => "Runs as it closes the paperdoll.",
        _ => "Runs when the part is double-clicked in play."
    };
}
