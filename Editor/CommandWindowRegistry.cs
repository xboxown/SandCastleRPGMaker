using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The commands added from the CommandList document on: each one's name in
/// the event's list, its window (new or editing) and its one-line summary.
/// The Event Editor looks here first, so a new command is one entry here
/// plus its window - no more else-if branches in EventEditorControl.
/// </summary>
internal static class CommandWindowRegistry
{
    internal sealed record Entry(string Name, Func<EventCommand?, FactionCommandWindowBase> Open, Func<EventCommand, string> Describe);

    public static readonly Dictionary<EventCommandType, Entry> All = new()
    {
        [EventCommandType.StopSound] = new("Stop Sound", c => new StopSoundCommandWindow(c), StopSoundCommandWindow.Describe),
    };
}

/// <summary>Stop Sound: the reverse of Play Sound - stops one sound (every copy of it, looping ones too) or every sound playing.</summary>
public class StopSoundCommandWindow : FactionCommandWindowBase
{
    private sealed record SoundChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox _sound;

    public StopSoundCommandWindow(EventCommand? existing = null) : base("Stop Sound")
    {
        AddLabel("Sound to stop");
        var choices = new List<SoundChoice> { new("All", "(Every sound playing)") };
        choices.AddRange((ProjectSession.ResourcesProvider?.Invoke()?.Entries ?? new List<ResourceEntry>())
            .Where(r => r.Kind == ResourceKind.Sound).Select(r => new SoundChoice(r.Id.ToString(), r.Name)));
        _sound = new ComboBox { ItemsSource = choices };
        _sound.SelectedItem = choices.FirstOrDefault(c => c.Key == existing?.Get("SoundResourceId")) ?? (choices.Count > 1 ? choices[1] : choices[0]);
        Rows.Children.Add(_sound);
        AddLabel("Stops it wherever it is playing - a looping Play Sound keeps going until this stops it.", bold: false);
    }

    protected override bool TryBuild()
    {
        var command = new EventCommand { Type = EventCommandType.StopSound };
        command.Parameters["SoundResourceId"] = (_sound.SelectedItem as SoundChoice)?.Key ?? "All";
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string key = command.Get("SoundResourceId");
        if (key == "All") return "every sound";
        return int.TryParse(key, out int id) && ProjectSession.ResourcesProvider?.Invoke()?.Entries.FirstOrDefault(r => r.Id == id) is { } entry ? entry.Name : $"sound #{key}";
    }
}
