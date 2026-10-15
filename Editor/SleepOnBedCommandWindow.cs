using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Sleep On Bed: the main character walks to a bed (or a laid-down bedroll)
/// and lies down on it, exactly as double-clicking it does - walk, lie
/// down, the hours slider. For a bed's cover, whose own double-click would
/// otherwise be the cover's.
/// </summary>
public class SleepOnBedCommandWindow : FactionCommandWindowBase
{
    private static readonly (string Key, string Label)[] Choices =
    {
        ("Under", "The bed this object lies on (its cover's event), else the nearest"),
        ("Nearest", "The nearest bed or bedroll to the player")
    };

    private readonly ComboBox _which;

    public SleepOnBedCommandWindow(EventCommand? existing = null) : base("Sleep On Bed")
    {
        AddLabel("Which bed");
        _which = new ComboBox { ItemsSource = Choices.Select(c => c.Label).ToList() };
        _which.SelectedIndex = Math.Max(0, Array.FindIndex(Choices, c => c.Key == existing?.Get("Which")));
        Rows.Children.Add(_which);
        AddLabel("The main character walks there and lies down - as double-clicking the bed does (the hours slider, sleeping, waking).", bold: false);
    }

    protected override bool TryBuild()
    {
        var command = new EventCommand { Type = EventCommandType.SleepOnBed };
        command.Parameters["Which"] = Choices[Math.Max(0, _which.SelectedIndex)].Key;
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => command.Get("Which") == "Nearest" ? "the nearest bed" : "the bed under this";
}
