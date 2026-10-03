using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

internal enum FieldKind { PlainText, SoundPicker }

internal record FieldSpec(string Key, string Label, FieldKind Kind = FieldKind.PlainText);

/// <summary>
/// Which parameter fields the Add/Edit Command dialog should show for each
/// command type, and how a command's Parameters dictionary is summarized in
/// the event's command-list display. Centralizing this here means adding a
/// new command type later is a matter of adding one dictionary entry, not
/// touching the dialog's layout code.
/// </summary>
internal static class EventCommandCatalog
{
    public static readonly Dictionary<EventCommandType, FieldSpec[]> FieldsByType = new()
    {
        [EventCommandType.ShowText] = new[]
        {
            new FieldSpec("Speaker", "Speaker (optional)"),
            new FieldSpec("Text", "Text"),
            new FieldSpec("SpeakerId", "Speaker (\"\" none, Self, Player, or character id)"),
            new FieldSpec("FaceSide", "Face (Left/Right)")
        },
        [EventCommandType.SetSwitch] = new[]
        {
            new FieldSpec("SwitchName", "Switch name"),
            new FieldSpec("Value", "Value (True/False)")
        },
        [EventCommandType.SetVariable] = new[]
        {
            new FieldSpec("VariableName", "Variable name"),
            new FieldSpec("Operation", "Operation (Set/Add/Subtract)"),
            new FieldSpec("Value", "Value")
        },
        [EventCommandType.ConditionalBranchSwitch] = new[]
        {
            new FieldSpec("SwitchName", "Switch name"),
            new FieldSpec("Value", "Is (True/False)")
        },
        [EventCommandType.ConditionalBranchVariable] = new[]
        {
            new FieldSpec("VariableName", "Variable name"),
            new FieldSpec("Comparison", "Comparison (== > < >= <=)"),
            new FieldSpec("Value", "Value")
        },
        [EventCommandType.Else] = Array.Empty<FieldSpec>(),
        [EventCommandType.EndBranch] = Array.Empty<FieldSpec>(),
        [EventCommandType.Teleport] = new[]
        {
            new FieldSpec("Who", "Who (Activator, Player, or character id)"),
            new FieldSpec("X", "X"),
            new FieldSpec("Y", "Y")
        },
        [EventCommandType.Wait] = new[]
        {
            new FieldSpec("Seconds", "Seconds")
        },
        [EventCommandType.PlaySound] = new[]
        {
            new FieldSpec("SoundResourceId", "Sound", FieldKind.SoundPicker)
        },
        [EventCommandType.PlayMusic] = new[]
        {
            new FieldSpec("MusicPath", "Music file")
        },
        [EventCommandType.ChangeTile] = new[]
        {
            new FieldSpec("X", "X (blank = event's own tile)"),
            new FieldSpec("Y", "Y (blank = event's own tile)"),
            new FieldSpec("TileIndex", "New tile index")
        },
        [EventCommandType.GiveItem] = new[]
        {
            new FieldSpec("ObjectName", "Object name"),
            new FieldSpec("Quantity", "Quantity")
        },
        [EventCommandType.RemoveItem] = new[]
        {
            new FieldSpec("ObjectName", "Object name"),
            new FieldSpec("Quantity", "Quantity")
        },
        [EventCommandType.Label] = new[]
        {
            new FieldSpec("LabelName", "Label name")
        },
        [EventCommandType.GoToLabel] = new[]
        {
            new FieldSpec("LabelName", "Label name")
        },
        [EventCommandType.ChangeHp] = new[]
        {
            new FieldSpec("Amount", "Amount (negative = damage)"),
            new FieldSpec("Target", "Target (Player/Self)")
        },
        [EventCommandType.ShakeScreen] = new[]
        {
            new FieldSpec("Intensity", "Intensity (1-10)"),
            new FieldSpec("DurationSeconds", "Duration (seconds)"),
            new FieldSpec("AffectsCharacters", "NPC/PC affected by earthquake (True/False)")
        },
        [EventCommandType.ChangeDayNight] = new[]
        {
            new FieldSpec("Period", "Period (Morning/Afternoon/Evening)")
        },
        [EventCommandType.ApplyPaletteColor] = new[]
        {
            new FieldSpec("PaletteGroup", "Palette group name"),
            new FieldSpec("ColorIndex", "Color index within that group"),
            new FieldSpec("Hex", "Resolved hex color"),
            new FieldSpec("OpacityPercent", "Overlay strength (0-100)")
        },
        [EventCommandType.ChangeSelfBookContent] = new[]
        {
            new FieldSpec("PageIndex", "Page index (0-based)"),
            new FieldSpec("FrameId", "Frame id within the book's shared layout"),
            new FieldSpec("ReplacementXaml", "Runtime-only replacement content (base64 XamlPackage)")
        },
        [EventCommandType.ChangeExternalBookContent] = new[]
        {
            new FieldSpec("TargetBookId", "Which book to change"),
            new FieldSpec("PageIndex", "Page index (0-based)"),
            new FieldSpec("FrameId", "Frame id within that book's shared layout"),
            new FieldSpec("ReplacementXaml", "Runtime-only replacement content (base64 XamlPackage)")
        },
        [EventCommandType.ChangeBookSettings] = new[]
        {
            new FieldSpec("TargetBookId", "Which book's settings to change (runtime-only)")
        },
        [EventCommandType.ChangeFactionRelationship] = new[]
        {
            new FieldSpec("ViewerFactionId", "Faction whose view changes (id)"),
            new FieldSpec("TargetFactionId", "Faction it sees (id)"),
            new FieldSpec("Operation", "Operation (Set/Add/Subtract)"),
            new FieldSpec("Value", "Value (0-100)"),
            new FieldSpec("BothWays", "Both ways (True/False)")
        },
        [EventCommandType.ChangeFaction] = new[]
        {
            new FieldSpec("CharacterId", "Character id (Player = main player)"),
            new FieldSpec("FactionId", "New faction id (-1 = none)")
        },
        [EventCommandType.ChangeBehavior] = new[]
        {
            new FieldSpec("CharacterId", "Character (Activator or character id)"),
            new FieldSpec("Behavior", "Behavior (a schedule activity, or Resume)")
        },
        [EventCommandType.ChangeFoodLevel] = new[]
        {
            new FieldSpec("Who", "Who (Activator, Player, or character id)"),
            new FieldSpec("Operation", "Operation (Set/Add/Subtract)"),
            new FieldSpec("Value", "Value (food level 0-31)")
        },
        [EventCommandType.ModifyAilments] = new[]
        {
            new FieldSpec("Who", "Who (Activator, Player, or character id)"),
            new FieldSpec("Ailment", "Ailment (Poisoned/Asleep/Paralyzed/Charmed/Cursed)"),
            new FieldSpec("Action", "Action (Inflict/Cure)")
        },
        [EventCommandType.Attack] = new[]
        {
            new FieldSpec("Attacker", "Attacker (Activator, Player, or character id)"),
            new FieldSpec("Target", "Target (Activator, Player, or character id)")
        },
        [EventCommandType.End] = Array.Empty<FieldSpec>()
    };

    public static string Summarize(EventCommand command)
    {
        string indent = new string(' ', command.IndentLevel * 4);
        string prefix = command.Type switch
        {
            EventCommandType.ConditionalBranchSwitch or EventCommandType.ConditionalBranchVariable => "If: ",
            EventCommandType.Else => "Else",
            EventCommandType.EndBranch => "End If",
            _ => ""
        };

        var fields = FieldsByType.TryGetValue(command.Type, out var specs) ? specs : Array.Empty<FieldSpec>();
        string args = string.Join(", ", fields
            .Select(f => command.Get(f.Key))
            .Where(v => !string.IsNullOrWhiteSpace(v)));

        return $"{indent}{command.Type}{(prefix.Length > 0 ? " (" + prefix.TrimEnd(':', ' ') + ")" : "")}" +
               (args.Length > 0 ? $" [{args}]" : "");
    }
}
