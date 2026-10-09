using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// One reply the player can pick under a Show Text (Ultima VII's answers:
/// "name", "job", "bye" - or any words at all). Its branch in the command
/// list is the WhenChoice line with the same Id, down to the next WhenChoice
/// or the EndChoices: picking it runs those commands - another Show Text
/// (the next dialog box), a Jump to Label back to an earlier one, or
/// anything else (attack, die, give an item...). When the branch runs out
/// with no new Show Text, the conversation is over.
/// </summary>
public class DialogChoice
{
    /// <summary>Stable within its Show Text: links the reply to its WhenChoice branch even when replies are reordered.</summary>
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>null = always offered. Otherwise it's only offered while these conditions pass (Trigger = the party having / not having an item; Cycle counts how many times it was picked: Once = gone after it's picked).</summary>
    public BookEventConditions? Condition { get; set; }
    /// <summary>Pinned: when this reply's branch runs out, the conversation comes back to the same box and its replies instead of ending (Ultima VII's answers that stay).</summary>
    public bool Pinned { get; set; }
}

/// <summary>Reads and writes a Show Text command's replies (kept as JSON in its "Choices" parameter, so the command keeps its generic shape).</summary>
public static class DialogChoices
{
    public const string ChoicesKey = "Choices";
    public const string ChoiceIdKey = "ChoiceId";

    public static List<DialogChoice> Read(EventCommand command)
    {
        string json = command.Get(ChoicesKey);
        if (string.IsNullOrWhiteSpace(json)) return new List<DialogChoice>();
        try { return JsonSerializer.Deserialize<List<DialogChoice>>(json) ?? new List<DialogChoice>(); }
        catch (JsonException) { return new List<DialogChoice>(); }
    }

    public static void Write(EventCommand command, List<DialogChoice> choices)
    {
        if (choices.Count == 0) command.Parameters.Remove(ChoicesKey);
        else command.Parameters[ChoicesKey] = JsonSerializer.Serialize(choices);
    }

    public static bool HasChoices(EventCommand command) =>
        command.Type == EventCommandType.ShowText && !string.IsNullOrWhiteSpace(command.Get(ChoicesKey));

    /// <summary>The EndChoices closing the reply branches of the Show Text at <paramref name="showTextIndex"/>, or -1 when it has none.</summary>
    public static int FindEndChoices(List<EventCommand> commands, int showTextIndex)
    {
        int indent = commands[showTextIndex].IndentLevel;
        if (showTextIndex + 1 >= commands.Count || commands[showTextIndex + 1] is not { Type: EventCommandType.WhenChoice } first || first.IndentLevel != indent) return -1;
        for (int j = showTextIndex + 1; j < commands.Count; j++)
        {
            if (commands[j].IndentLevel < indent) return -1;
            if (commands[j].IndentLevel == indent && commands[j].Type == EventCommandType.EndChoices) return j;
        }
        return -1;
    }

    /// <summary>The WhenChoice line for this reply under the Show Text at <paramref name="showTextIndex"/>, or -1.</summary>
    public static int FindWhen(List<EventCommand> commands, int showTextIndex, int choiceId)
    {
        int end = FindEndChoices(commands, showTextIndex);
        if (end < 0) return -1;
        int indent = commands[showTextIndex].IndentLevel;
        for (int j = showTextIndex + 1; j < end; j++)
            if (commands[j].IndentLevel == indent && commands[j].Type == EventCommandType.WhenChoice && commands[j].Get(ChoiceIdKey) == choiceId.ToString()) return j;
        return -1;
    }

    /// <summary>Where the branch starting at the WhenChoice <paramref name="whenIndex"/> ends: the next WhenChoice or the EndChoices.</summary>
    public static int FindBranchEnd(List<EventCommand> commands, int whenIndex)
    {
        int indent = commands[whenIndex].IndentLevel;
        for (int j = whenIndex + 1; j < commands.Count; j++)
            if (commands[j].IndentLevel == indent && commands[j].Type is EventCommandType.WhenChoice or EventCommandType.EndChoices) return j;
        return commands.Count;
    }

    /// <summary>From a WhenChoice (or anywhere inside the branches), the matching EndChoices at this indent.</summary>
    public static int FindEndChoicesFrom(List<EventCommand> commands, int fromIndex)
    {
        int indent = commands[fromIndex].IndentLevel;
        for (int j = fromIndex + 1; j < commands.Count; j++)
            if (commands[j].IndentLevel == indent && commands[j].Type == EventCommandType.EndChoices) return j;
        return commands.Count - 1;
    }
}

/// <summary>
/// A host that can wait for the player to pick a reply (the game). The
/// interpreter hands it the Show Text and its replies and stops; when one is
/// picked the host calls <c>chosen</c> with its Id and the commands go on from
/// that reply's branch. A host that isn't one of these (the Debug preview)
/// just takes the first reply.
/// </summary>
public interface IDialogChoiceHost
{
    /// <summary>False when nobody can pick right now (a preview): the first reply is taken.</summary>
    bool CanAskChoice { get; }

    /// <summary>Shows the box with its replies. Returns false when no reply is on offer (the box shows as plain text, and the commands go on after the branches).</summary>
    bool AskChoice(EventCommand showText, List<DialogChoice> choices, Action<int> chosen);

    /// <summary>Stop Conversation: false when no box is up (the commands just go on). Otherwise <paramref name="goOn"/> is called once the boxes up now have been read and closed.</summary>
    bool WaitUntilRead(Action goOn);
}
