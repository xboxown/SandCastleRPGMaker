using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A Show Text with replies owns the lines under it, RPG Maker style:
///   ShowText "Hello." [name, job, bye]
///     When "name"      &lt;- that reply's commands, one level deeper
///     When "job"
///     When "bye"
///   End Replies
/// These keep those lines in step with the Show Text's replies: adding,
/// renaming, reordering and removing replies (a removed reply's branch goes
/// with it), deleting the whole block with its Show Text, and opening the
/// Show Text from any of its lines.
/// </summary>
public partial class EventEditorControl
{
    private static bool IsChoiceLine(EventCommand command) => command.Type is EventCommandType.WhenChoice or EventCommandType.EndChoices;

    /// <summary>The Show Text a When / End Replies line belongs to, or -1.</summary>
    private int OwningShowTextOf(int index)
    {
        if (CurrentPage is null) return -1;
        var commands = CurrentPage.Commands;
        int indent = commands[index].IndentLevel;
        for (int j = index - 1; j >= 0; j--)
        {
            if (commands[j].IndentLevel < indent) return -1;
            if (commands[j].IndentLevel == indent && DialogChoices.HasChoices(commands[j]) && DialogChoices.FindEndChoices(commands, j) >= index) return j;
        }
        return -1;
    }

    /// <summary>Each reply's branch (the commands under its When line), by reply Id.</summary>
    private static Dictionary<int, List<EventCommand>> BranchesOf(List<EventCommand> commands, int showTextIndex, int end)
    {
        var branches = new Dictionary<int, List<EventCommand>>();
        int indent = commands[showTextIndex].IndentLevel;
        List<EventCommand>? current = null;
        for (int j = showTextIndex + 1; j < end; j++)
        {
            var line = commands[j];
            if (line.IndentLevel == indent && line.Type == EventCommandType.WhenChoice)
            {
                current = new List<EventCommand>();
                if (int.TryParse(line.Get(DialogChoices.ChoiceIdKey), out int id)) branches[id] = current;
                continue;
            }
            current?.Add(line);
        }
        return branches;
    }

    /// <summary>Puts a Show Text at <paramref name="index"/> (replacing the one there when <paramref name="replace"/>) with a When line for each of its replies, keeping the commands already in the branches of replies it still has. False if the person backed out.</summary>
    private bool PlaceShowText(int index, EventCommand showText, bool replace)
    {
        if (CurrentPage is null) return false;
        var commands = CurrentPage.Commands;
        int indent = showText.IndentLevel;
        var branches = new Dictionary<int, List<EventCommand>>();
        int oldEnd = -1;
        if (replace)
        {
            oldEnd = DialogChoices.FindEndChoices(commands, index);
            if (oldEnd >= 0) branches = BranchesOf(commands, index, oldEnd);
        }
        var replies = DialogChoices.Read(showText);
        var keptIds = replies.Select(r => r.Id).ToHashSet();
        int lostCommands = branches.Where(b => !keptIds.Contains(b.Key)).Sum(b => b.Value.Count);
        if (lostCommands > 0 && MessageBox.Show(Window.GetWindow(this),
                $"The replies you removed have {lostCommands} command{(lostCommands == 1 ? "" : "s")} in their branches. They'll be deleted with them. Go on?",
                "Show Text", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;

        if (replace)
        {
            if (oldEnd >= 0) commands.RemoveRange(index + 1, oldEnd - index);
            commands[index] = showText;
        }
        else commands.Insert(index, showText);

        if (replies.Count > 0)
        {
            var lines = new List<EventCommand>();
            foreach (var reply in replies)
            {
                lines.Add(new EventCommand
                {
                    Type = EventCommandType.WhenChoice, IndentLevel = indent,
                    Parameters = { [DialogChoices.ChoiceIdKey] = reply.Id.ToString(), ["Text"] = reply.Text }
                });
                if (branches.TryGetValue(reply.Id, out var body)) lines.AddRange(body);
            }
            lines.Add(new EventCommand { Type = EventCommandType.EndChoices, IndentLevel = indent });
            commands.InsertRange(index + 1, lines);
        }
        return true;
    }

    /// <summary>Delete for a Show Text with replies (the whole block goes) or one of its lines (refused - its replies are edited in the Show Text). True when handled here.</summary>
    private bool TryDeleteChoiceAware(int index)
    {
        if (CurrentPage is null) return false;
        var commands = CurrentPage.Commands;
        var command = commands[index];
        if (IsChoiceLine(command))
        {
            MessageBox.Show(Window.GetWindow(this),
                "This line belongs to the Show Text above it. Double-click it (or the Show Text) to add, rename, reorder or remove replies - or delete the Show Text to delete the whole conversation box.",
                "Delete Command", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        if (command.Type != EventCommandType.ShowText) return false;
        int end = DialogChoices.FindEndChoices(commands, index);
        if (end < 0) return false;
        int inside = BranchesOf(commands, index, end).Sum(b => b.Value.Count);
        if (inside > 0 && MessageBox.Show(Window.GetWindow(this),
                $"Delete this Show Text with its replies and the {inside} command{(inside == 1 ? "" : "s")} in their branches?",
                "Delete Command", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return true;
        commands.RemoveRange(index, end - index + 1);
        RenderContentsList();
        ContentChanged?.Invoke();
        return true;
    }

    /// <summary>Label / Jump to Label: just a name.</summary>
    private EventCommand? AskLabel(EventCommandType type, EventCommand? existing)
    {
        var labels = CurrentPage?.Commands.Where(c => c.Type == EventCommandType.Label).Select(c => c.Get("LabelName")).Where(n => n.Length > 0).Distinct().ToList() ?? new List<string>();
        string prompt = type == EventCommandType.Label
            ? "Label name - a place in this command list that Jump to Label can go back to (e.g. \"Start\", put just before a Show Text to come back to that box):"
            : "Jump to which label?" + (labels.Count > 0 ? $"\n\nLabels on this page: {string.Join(", ", labels)}" : "\n\n(No labels on this page yet - add a Label command first.)");
        string? name = SimpleInputWindow.Show(Window.GetWindow(this)!, prompt, type == EventCommandType.Label ? "Label" : "Jump to Label",
            existing?.Get("LabelName") ?? (type == EventCommandType.GoToLabel && labels.Count > 0 ? labels[0] : "Start"));
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new EventCommand { Type = type, Parameters = { ["LabelName"] = name.Trim() } };
    }
}
