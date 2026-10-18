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
        [EventCommandType.PlayVideo] = new("Play Video", c => new PlayVideoCommandWindow(c), PlayVideoCommandWindow.Describe),
        [EventCommandType.OpenPaperdoll] = new("Open Paperdoll", c => new OpenPaperdollCommandWindow(c), OpenPaperdollCommandWindow.Describe),
        [EventCommandType.StopSound] = new("Stop Sound", c => new StopSoundCommandWindow(c), StopSoundCommandWindow.Describe),
        [EventCommandType.GiveItem] = new("Give Item", c => new GiveItemCommandWindow(c), GiveItemCommandWindow.Describe),
        [EventCommandType.ChangeStat] = new("Change Stat", c => new ChangeStatCommandWindow(c), ChangeStatCommandWindow.Describe),
        [EventCommandType.ResetStat] = new("Reset Stat", c => new ResetStatCommandWindow(c), ResetStatCommandWindow.Describe),
        [EventCommandType.Train] = new("Train", c => new TrainCommandWindow(c), TrainCommandWindow.Describe),
        [EventCommandType.KillCharacter] = new("Kill Character", c => new KillCharacterCommandWindow(c), KillCharacterCommandWindow.Describe),
        [EventCommandType.PickupItem] = new("Pickup Item", c => new PickupItemCommandWindow(c), PickupItemCommandWindow.Describe),
        [EventCommandType.DropItem] = new("Drop Item", c => new DropItemCommandWindow(c), DropItemCommandWindow.Describe),
        [EventCommandType.EquipItem] = new("Equip Equipment", c => new EquipItemCommandWindow(c), EquipItemCommandWindow.Describe),
        [EventCommandType.UnequipItem] = new("Unequip Equipment", c => new UnequipItemCommandWindow(c), UnequipItemCommandWindow.Describe),
        [EventCommandType.PlayPose] = new("Play Pose", c => new PlayPoseCommandWindow(c), PlayPoseCommandWindow.Describe),
        [EventCommandType.ChangePose] = new("Change Pose", c => new ChangePoseCommandWindow(c), ChangePoseCommandWindow.Describe),
        [EventCommandType.WalkToLocation] = new("Walk To Location", c => new WalkToLocationCommandWindow(c), WalkToLocationCommandWindow.Describe),
        [EventCommandType.WalkToFloorLocation] = new("Walk To Floor Location", c => new WalkToFloorLocationCommandWindow(c), WalkToFloorLocationCommandWindow.Describe),
        [EventCommandType.AttackObject] = new("Attack Object", c => new AttackObjectCommandWindow(c), AttackObjectCommandWindow.Describe),
        [EventCommandType.PlayAnimation] = new("Play Animation", c => new PlayAnimationCommandWindow(c), PlayAnimationCommandWindow.Describe),
        [EventCommandType.Sit] = new("Sit", c => new SitCommandWindow(c), SitCommandWindow.Describe),
        [EventCommandType.Stand] = new("Stand", c => new StandCommandWindow(c), StandCommandWindow.Describe),
        [EventCommandType.OpenDoor] = new("Open Door", c => new OpenDoorCommandWindow(c), OpenDoorCommandWindow.Describe),
        [EventCommandType.PicklockDoor] = new("Picklock Door", c => new PicklockDoorCommandWindow(c), PicklockDoorCommandWindow.Describe),
        [EventCommandType.Wait] = new("Wait", c => new WaitCommandWindow(c), WaitCommandWindow.Describe),
        [EventCommandType.TakeFromContainer] = new("Pick Item From Container", c => new TakeFromContainerCommandWindow(c), TakeFromContainerCommandWindow.Describe),
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

/// <summary>Play Video: a video from the Video Library over the whole game, the game paused till it ends - an intro, a cutscene, an ending.</summary>
public class OpenPaperdollCommandWindow : FactionCommandWindowBase
{
    private sealed record DesignChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly ComboBox _design;
    private readonly CheckBox _modal = new() { Content = "Modal - nothing else can be clicked or typed in till it closes", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _wait = new() { Content = "The commands after this one wait till it closes", Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _fresh = new() { Content = "Opens as designed each time (else as it was left)", Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _answer;

    public OpenPaperdollCommandWindow(EventCommand? existing = null) : base("Open Paperdoll")
    {
        AddLabel("Paperdoll (PaperDoll Editor)");
        var choices = (ProjectSession.ObjectsProvider?.Invoke()?.DollDesigns ?? new List<DollDesign>()).Select(d => new DesignChoice(d.Id, d.Name)).ToList();
        _design = new ComboBox { ItemsSource = choices };
        _design.SelectedItem = choices.FirstOrDefault(c => c.Id.ToString() == existing?.Get("DesignId")) ?? choices.FirstOrDefault();
        Rows.Children.Add(_design);
        Rows.Children.Add(_modal);
        Rows.Children.Add(_wait);
        Rows.Children.Add(_fresh);
        _modal.IsChecked = existing is null || existing.Get("Modal") == "True";
        _wait.IsChecked = existing?.Get("Wait") == "True";
        _fresh.IsChecked = existing?.Get("Fresh") == "True";
        AddLabel("Its answer into the variable (Yes / OK button = 1, No / Cancel, Escape or closed = 0)");
        var variables = ProjectSession.VariablesProvider?.Invoke()?.Variables.Select(x => x.Name).ToList() ?? new List<string>();
        _answer = new ComboBox { ItemsSource = variables, IsEditable = true, Text = existing?.Get("AnswerVariable") ?? "" };
        Rows.Children.Add(_answer);
        AddLabel("A paperdoll opened over another one: it's on top, and closing it goes back to the one behind. Its controls keep their values in their own variables; its parts' own events (a button's) run as they do on any paperdoll.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (_design.SelectedItem is not DesignChoice choice)
        {
            MessageBox.Show(this, "Pick a paperdoll - make one in the PaperDoll Editor first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = new EventCommand { Type = EventCommandType.OpenPaperdoll };
        command.Parameters["DesignId"] = choice.Id.ToString();
        command.Parameters["DesignName"] = choice.Name;
        command.Parameters["Modal"] = _modal.IsChecked == true ? "True" : "False";
        command.Parameters["Wait"] = _wait.IsChecked == true ? "True" : "False";
        command.Parameters["Fresh"] = _fresh.IsChecked == true ? "True" : "False";
        command.Parameters["AnswerVariable"] = _answer.Text.Trim();
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string name = int.TryParse(command.Get("DesignId"), out int id) && ProjectSession.ObjectsProvider?.Invoke()?.DollDesigns.FirstOrDefault(d => d.Id == id) is { } design ? design.Name : command.Get("DesignName");
        var notes = new List<string>();
        if (command.Get("Modal") == "True") notes.Add("modal");
        if (command.Get("Wait") == "True") notes.Add("waits");
        if (!string.IsNullOrEmpty(command.Get("AnswerVariable"))) notes.Add($"answer into {command.Get("AnswerVariable")}");
        return notes.Count == 0 ? name : $"{name} ({string.Join(", ", notes)})";
    }
}

public class PlayVideoCommandWindow : FactionCommandWindowBase
{
    private sealed record VideoChoice(int Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox _video;
    private readonly CheckBox _skippable = new() { Content = "The player can skip it (a click, Escape, Space or Enter)", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Width = 200, IsSnapToTickEnabled = true, TickFrequency = 5 };

    public PlayVideoCommandWindow(EventCommand? existing = null) : base("Play Video")
    {
        AddLabel("Video (Tools > Video Library)");
        var choices = ProjectSession.Videos.Videos.Select(v => new VideoChoice(v.Id, $"{v.Name}  ({v.Codec})")).ToList();
        _video = new ComboBox { ItemsSource = choices };
        _video.SelectedItem = choices.FirstOrDefault(c => c.Id.ToString() == existing?.Get("VideoId")) ?? choices.FirstOrDefault();
        Rows.Children.Add(_video);
        Rows.Children.Add(_skippable);
        if (existing?.Get("Skippable") == "False") _skippable.IsChecked = false;
        AddLabel("Volume");
        var volumeRow = CommandFields.Row(Rows);
        var volumeText = new TextBlock { Width = 40, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _volume.ValueChanged += (_, _) => volumeText.Text = $"{_volume.Value:0}%";
        _volume.Value = double.TryParse(existing?.Get("Volume"), out double volume) ? Math.Clamp(volume, 0, 100) : 100;
        volumeRow.Children.Add(_volume);
        volumeRow.Children.Add(volumeText);
        AddLabel("It fills the game's screen (black bars keep its shape) and the game is paused while it plays, as for a paperdoll. The commands after this one wait till it ends or is skipped. A video this PC can't play (H.265 without Microsoft's HEVC extension) is skipped.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (_video.SelectedItem is not VideoChoice choice)
        {
            MessageBox.Show(this, "Pick a video - add one in Tools > Video Library first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = new EventCommand { Type = EventCommandType.PlayVideo };
        command.Parameters["VideoId"] = choice.Id.ToString();
        command.Parameters["Skippable"] = _skippable.IsChecked == true ? "True" : "False";
        command.Parameters["Volume"] = _volume.Value.ToString("0");
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string name = int.TryParse(command.Get("VideoId"), out int id) && ProjectSession.Videos.Videos.FirstOrDefault(v => v.Id == id) is { } video ? video.Name : $"video #{command.Get("VideoId")}";
        return command.Get("Skippable") == "False" ? $"{name} (can't be skipped)" : name;
    }
}
