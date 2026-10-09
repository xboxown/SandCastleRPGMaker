using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Talking, dialog boxes, and characters taking part in the world like the
/// player does:
///  - Double-clicking a character runs its Talk commands (Exult
///    Actor::activate); a party member's inventory opens instead while a
///    gump is open (show_party_inv = showing_gumps()).
///  - Show Text is a real dialog box over the world: the speaker's face on
///    the chosen side, the text in the DialogFont, at the top, middle or
///    bottom. A character's line only shows while that character is in
///    sight (on screen). While a box is up the game is paused, as during an
///    Exult conversation; a click, Enter, Space or Escape goes on.
///  - A character stepping onto, staying on or leaving a map event sets it
///    off exactly as the player does; commands acting on "whoever set it
///    off" (Teleport, Change Behavior, Show Text by Self) then act on it.
///    Narration with no speaker is only shown when the player set it off.
/// </summary>
public partial class GameTestWindow
{
    private sealed record GameMessage(string Text, CharacterDefinition? Speaker, string Position, bool FaceRight)
    {
        public string Title { get; init; } = "";
        /// <summary>The DialogFrame sprite, or null for no box.</summary>
        public SpriteEntry? Frame { get; init; }
        public bool Dim { get; init; }
        /// <summary>The replies on offer - the box waits for one to be picked.</summary>
        public List<DialogChoice>? Replies { get; init; }
        public Action<int>? OnChosen { get; init; }
        /// <summary>Not a box: Close Show Text's "go on" - run when every box before it has been read.</summary>
        public Action? AfterRead { get; init; }
        public bool HasReplies => Replies is { Count: > 0 };
    }

    private readonly Queue<GameMessage> _messages = new();
    private GameMessage? _currentMessage;
    private DialogBoxDrawing? _messageDrawing;

    /// <summary>The character whose action is running the current commands (stepped on an event, is being talked to); null = the player.</summary>
    private MapCharacter? _activator;
    /// <summary>True while the commands running were set off by a character walking over an event, not by the player.</summary>
    private bool _activatedByCharacterStep;

    private bool IsShowingMessage => _currentMessage is not null || _messages.Count > 0;

    private void InitializeTalk()
    {
        _execContext.OnShowMessage = QueueMessage;
        _execContext.OnAskChoice = AskChoice;
        _execContext.OnWaitUntilRead = WaitUntilRead;
        _execContext.OnTeleport = TeleportCommand;
        _execContext.OnChangeBehavior = ChangeBehaviorCommand;
    }

    // ---------- Talking ----------

    /// <summary>Exult's Actor::activate for a placed character.</summary>
    private void TalkTo(PlacedCharacter placement)
    {
        var mapCharacter = _mapCharacters.FirstOrDefault(mc => mc.Placement == placement);
        if (mapCharacter is null) return;
        var character = mapCharacter.Character;
        if (IsDead(character))
        {
            // The body: its belongings can be taken (Exult's dead body container).
            OpenPaperdoll(character);
            StatusText.Text = $"The body of {character.Name}.";
            return;
        }
        if (IsIncapacitated(character))
        {
            StatusText.Text = $"{character.Name} can't answer right now ({DescribeVitals(character)}).";
            return;
        }
        if (mapCharacter.Npc.InCombat && !character.IsInParty) return; // Actor::activate: "Too busy fighting." - nothing happens
        if (character.IsInParty && IsGamePaused)
        {
            OpenPaperdoll(character); // Exult: with gumps showing, a party member's inventory opens instead
            return;
        }
        if (character.TalkCommands.Count == 0)
        {
            StatusText.Text = $"{character.Name} has nothing to say (Character Editor > Talk).";
            return;
        }
        // Stop and turn to the player for the conversation; it carries on with its schedule afterwards.
        var npc = mapCharacter.Npc;
        if (npc.Path is not null) { npc.Path = null; npc.WaitMs = StdDelay; }
        if (npc.Activity != ScheduleType.Sleep)
            SetCharacterPose(mapCharacter, npc.Activity == ScheduleType.Dance ? "Dancing" : "Idle", FacingTowards(mapCharacter.PixelX, mapCharacter.PixelY, _playerPixelX, _playerPixelY));
        RunCommandsAs(mapCharacter, character.TalkCommands, byStepping: false);
    }

    private static SpriteDirection FacingTowards(double fromX, double fromY, double toX, double toY)
    {
        double dx = toX - fromX, dy = toY - fromY;
        return Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? SpriteDirection.East : SpriteDirection.West) : (dy > 0 ? SpriteDirection.South : SpriteDirection.North);
    }

    private void RunCommandsAs(MapCharacter activator, List<EventCommand> commands, bool byStepping)
    {
        var (previous, previousStep) = (_activator, _activatedByCharacterStep);
        _activator = activator;
        _activatedByCharacterStep = byStepping;
        try
        {
            _execContext.SelfBookId = -1;
            EventCommandInterpreter.Run(commands, _execContext, new List<string>());
        }
        finally
        {
            _activator = previous;
            _activatedByCharacterStep = previousStep;
        }
    }

    // ---------- Characters setting off map events ----------

    private void FireEventAs(MapCharacter activator, GameEvent gameEvent, BookTriggerConditionBased trigger)
    {
        var (previous, previousStep) = (_activator, _activatedByCharacterStep);
        _activator = activator;
        _activatedByCharacterStep = true;
        try { TryFireEventPages(gameEvent, gameEvent.EventPages, trigger, selfBookId: -1, $"\"{gameEvent.Name}\" fired ({trigger}) by {activator.Character.Name}"); }
        finally
        {
            _activator = previous;
            _activatedByCharacterStep = previousStep;
        }
    }

    /// <summary>A character walked onto a new cell: leaving and entering events fire for it, as they do for the player.</summary>
    private void CharacterChangedCell(MapCharacter mapCharacter)
    {
        var leaving = mapCharacter.Npc.EventUnder;
        var now = EventAt(mapCharacter.Placement.X, mapCharacter.Placement.Y, mapCharacter.Lift);
        mapCharacter.Npc.EventUnder = now;
        if (leaving is not null && leaving != now) FireEventAs(mapCharacter, leaving, BookTriggerConditionBased.ByExit);
        if (now is not null && now != leaving) FireEventAs(mapCharacter, now, BookTriggerConditionBased.ByEnter);
    }

    /// <summary>Each second (the player's By Stay timer): every character standing on an event sets off its By Stay pages.</summary>
    private void FireCharacterStayEvents()
    {
        foreach (var mapCharacter in _mapCharacters.ToList())
            if (mapCharacter.Npc.EventUnder is { } gameEvent) FireEventAs(mapCharacter, gameEvent, BookTriggerConditionBased.ByStay);
    }

    /// <summary>The event on this cell of the floor someone at this lift is on.</summary>
    private GameEvent? EventAt(int x, int y, int lift) => _map.Events.FirstOrDefault(ev => ev.X == x && ev.Y == y && ev.Floor == MapFloors.FloorOfLift(lift));

    // ---------- Teleport / Change Behavior ----------

    private MapCharacter? CharacterFor(string who) => who switch
    {
        WhoChoice.Activator => _activator,
        _ => _mapCharacters.FirstOrDefault(mc => mc.Character.Id.ToString() == who)
    };

    private bool TeleportCommand(string who, int x, int y)
    {
        if (!InsideMap(x, y)) return false;
        if (who == WhoChoice.Player || (who == WhoChoice.Activator && _activator is null))
        {
            _playerX = x;
            _playerY = y;
            _playerPixelX = x * _tileWidth + _tileWidth / 2.0;
            _playerPixelY = y * _tileHeight + _tileHeight / 2.0;
            _playerZ = StandingLiftOnFloor(PlayerFootprint(), MapFloors.FloorOfLift(_playerZ)); // on the floor they were on
            _playerVisualLiftZ = _playerZ;
            _eventUnderPlayer = EventAt(x, y, _playerZ); // arriving doesn't set off the event there
            SnapPlayerPosition();
            UpdateCamera();
            return true;
        }
        var mapCharacter = CharacterFor(who);
        if (mapCharacter is null) return false;
        bool wasBusy = mapCharacter.Npc.WalkingToSchedule || mapCharacter.Npc.Activity is ScheduleType.Loiter or ScheduleType.Wander or ScheduleType.Dance or ScheduleType.HorizPace or ScheduleType.VertPace;
        Teleport(mapCharacter, (x, y));
        mapCharacter.Npc.EventUnder = EventAt(x, y, mapCharacter.Lift);
        if (wasBusy) mapCharacter.Npc.WaitMs = StdDelay; // think again from the new place
        return true;
    }

    /// <summary>Exult's set_schedule_type: start an activity right where it is (lasting until the schedule next changes), or go back to the schedule now.</summary>
    private bool ChangeBehaviorCommand(string who, string behavior)
    {
        var mapCharacter = CharacterFor(who);
        if (mapCharacter is null) return false;
        var npc = mapCharacter.Npc;
        StopWalking(mapCharacter);
        npc.WalkingToSchedule = false;
        if (behavior == ChangeBehaviorCommandWindow.Resume)
        {
            npc.Entry = null;
            npc.Activity = null;
            npc.OldScheduleLoc = null;
            UpdateSchedule(mapCharacter, _clock.Period, 0);
            return true;
        }
        if (!Enum.TryParse<ScheduleType>(behavior, out var type)) return false;
        npc.Entry = new ScheduleEntry { Period = -1, Type = type, X = mapCharacter.Placement.X, Y = mapCharacter.Placement.Y };
        npc.Dest = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
        BeginActivity(mapCharacter);
        return true;
    }

    // ---------- The dialog box ----------

    private bool QueueMessage(EventCommand command)
    {
        if (BuildMessage(command) is not { } message) return false;
        _messages.Enqueue(message);
        if (_currentMessage is null) ShowNextMessage();
        return true;
    }

    /// <summary>A Show Text's box, or null when it isn't shown (no text and no replies, narration for a character walking past, a speaker out of sight).</summary>
    private GameMessage? BuildMessage(EventCommand command, List<DialogChoice>? replies = null, Action<int>? onChosen = null)
    {
        string text = command.Get("Text");
        string title = command.Get(DialogBoxBuilder.TitleKey);
        if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(title) && (replies is null || replies.Count == 0)) return null;
        string key = command.Get("SpeakerId");
        CharacterDefinition? speaker;
        MapCharacter? speakerOnMap = null;
        switch (key)
        {
            case "":
                if (_activatedByCharacterStep) return null; // narration is for the player, not for a character walking past
                speaker = null;
                break;
            case "Self":
                speakerOnMap = _activator;
                speaker = _activator?.Character ?? _player;
                break;
            case "Player":
                speaker = _player;
                break;
            case var id when _player is not null && _player.Id.ToString() == id:
                speaker = _player; // the main player picked by name: always here, not one of the placed characters
                break;
            default:
                speakerOnMap = _mapCharacters.FirstOrDefault(mc => mc.Character.Id.ToString() == key);
                speaker = speakerOnMap?.Character;
                if (speaker is null)
                {
                    StatusText.Text = "That speaker isn't on this map - their line isn't shown.";
                    return null;
                }
                break;
        }
        if (speakerOnMap is not null && !IsInSight(speakerOnMap))
        {
            StatusText.Text = $"{speakerOnMap.Character.Name} isn't in sight - their line isn't shown.";
            return null;
        }
        var (frame, dim) = DialogBoxBuilder.LookOf(command);
        return new GameMessage(text, speaker, command.Get("Position"), command.Get("FaceSide") == "Right")
        {
            Title = title,
            Frame = _sprites.FindDialogFrame(frame),
            Dim = dim,
            Replies = replies,
            OnChosen = onChosen
        };
    }

    /// <summary>In sight: on the visible screen itself.</summary>
    private bool IsInSight(MapCharacter mapCharacter) =>
        mapCharacter.PixelX >= _cameraX && mapCharacter.PixelX <= _cameraX + ViewportWidth
        && mapCharacter.PixelY >= _cameraY && mapCharacter.PixelY <= _cameraY + ViewportHeight;

    private void AdvanceMessage()
    {
        if (_currentMessage is { HasReplies: true }) return; // a reply has to be picked
        _currentMessage = null;
        ShowNextMessage();
    }

    private void ShowNextMessage()
    {
        _messageDrawing?.Stop();
        _messageDrawing = null;
        MessageLayer.Children.Clear();
        if (!_messages.TryDequeue(out var message))
        {
            _currentMessage = null;
            MessageLayer.Visibility = Visibility.Collapsed;
            return;
        }
        if (message.AfterRead is { } goOn)
        {
            // Close Show Text: the boxes before it are read - close, and the commands after it go on.
            _currentMessage = null;
            MessageLayer.Visibility = Visibility.Collapsed;
            goOn();
            if (_currentMessage is null) ShowNextMessage();
            return;
        }
        _currentMessage = message;
        _replyCursor = -1;
        MessageLayer.Visibility = Visibility.Visible;

        BitmapSource? face = null;
        if (message.Speaker is { } speaker && FindSprite(speaker.FaceSpriteGroup, speaker.FaceSpriteName) is { } faceSprite) face = FirstFrameOf(faceSprite);
        var content = new DialogBoxContent(message.Title, message.Text, face, message.FaceRight, message.Position, message.Dim, message.Frame,
            message.Replies?.Select(r => r.Text).ToList() ?? new List<string>());
        _messageDrawing = DialogBoxBuilder.Draw(MessageLayer, content, _sprites, _resources);
    }

    private SpriteEntry? FindSprite(string group, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _sprites.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == name);
    }

    private static BitmapImage? FirstFrameOf(SpriteEntry sprite) => LoadStaticFrame(sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath);
}
