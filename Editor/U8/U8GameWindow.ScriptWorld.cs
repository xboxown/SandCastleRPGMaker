// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The world script commands (CommandList groups 7-8): the party, schedules,
/// placing and removing characters, facing, following, names and looks,
/// combat modes, healing and bringing back the dead, barks; creating,
/// deleting, moving and re-skinning things, doors, counting items, filling
/// containers.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>Follow: who walks after whom (re-pathing each 2/5 s while more than 2 cells away).</summary>
    private readonly Dictionary<MapCharacter, CharacterDefinition> _followers = new(ReferenceEqualityComparer.Instance);
    private double _followThinkMs;
    /// <summary>Party members who went to sit because the main character sat down (they get up with them); when each may look for a free seat again.</summary>
    private readonly HashSet<MapCharacter> _sittingWithPlayer = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MapCharacter, double> _seatLookAgainAt = new(ReferenceEqualityComparer.Instance);
    private double _followClockMs;
    /// <summary>Change Schedule for some hours: the game minute its own schedule comes back.</summary>
    private readonly Dictionary<MapCharacter, long> _scheduleUntil = new(ReferenceEqualityComparer.Instance);
    /// <summary>Characters taken off the map (Place / Remove Character), to be placed again.</summary>
    private readonly Dictionary<CharacterDefinition, PlacedCharacter> _offMap = new(ReferenceEqualityComparer.Instance);
    /// <summary>The bodies the dead left (Actor::die), for Resurrect.</summary>
    private readonly Dictionary<CharacterDefinition, (PlacedItemInstance Body, PlacedCharacter Placement)> _bodies = new(ReferenceEqualityComparer.Instance);
    /// <summary>Change Look: each changed character's own sprites, to change back.</summary>
    private readonly Dictionary<CharacterDefinition, Dictionary<int, PoseAssignment>> _ownLooks = new(ReferenceEqualityComparer.Instance);

    private void InitializeScriptWorld()
    {
        var h = _execContext.GameHandlers;
        h[EventCommandType.JoinParty] = JoinPartyCommand;
        h[EventCommandType.ChangeSchedule] = ChangeScheduleCommand;
        h[EventCommandType.PlaceCharacter] = PlaceCharacterCommand;
        h[EventCommandType.TurnToFace] = TurnToFaceCommand;
        h[EventCommandType.FollowCharacter] = FollowCommand;
        h[EventCommandType.ChangeName] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
            who.Name = ScriptValues.Text(Formula(c, "Value"));
            return true;
        };
        h[EventCommandType.ChangeLook] = ChangeLookCommand;
        h[EventCommandType.FeignDeath] = FeignDeathCommand; // their .Toughness.cs
        h[EventCommandType.WithstandDeath] = WithstandDeathCommand;
        h[EventCommandType.Changeling] = ChangelingCommand;
        h[EventCommandType.FindObjectsInArea] = FindObjectsCommand;
        h[EventCommandType.DeleteObjectsInArea] = DeleteObjectsInAreaCommand;
        h[EventCommandType.ItemsInside] = ItemsInsideCommand;
        h[EventCommandType.ItemCharges] = ItemChargesCommand;
        h[EventCommandType.HurtInArea] = HurtInAreaCommand;
        h[EventCommandType.SetAttackMode] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who || !Enum.TryParse<AttackMode>(c.Get("Mode"), out var mode)) return false;
            who.AttackMode = mode;
            return true;
        };
        h[EventCommandType.Resurrect] = ResurrectCommand;
        h[EventCommandType.LearnLanguage] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who || !int.TryParse(c.Get("Language"), out int language)) return false;
            if (c.Get("Value") == "Forget") who.KnownLanguages.RemoveAll(l => l == language);
            else if (!who.KnownLanguages.Contains(language)) who.KnownLanguages.Add(language);
            return true;
        };
        h[EventCommandType.ShowBark] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
            ShowFloatingText(who, ScriptValues.Text(Formula(c, "Value")), (int)(Math.Max(0.5, FormulaNumber(c, "Seconds")) * 1000));
            return true;
        };

        h[EventCommandType.CreateObject] = CreateObjectCommand;
        h[EventCommandType.DeleteObject] = DeleteObjectCommand;
        h[EventCommandType.DeleteObjectSelf] = DeleteObjectSelfCommand;
        h[EventCommandType.MoveObject] = MoveObjectCommand;
        h[EventCommandType.ChangeObjectLook] = ChangeObjectLookCommand;
        h[EventCommandType.SetDoorState] = SetDoorStateCommand;
        h[EventCommandType.CountItems] = CountItemsCommand;
        h[EventCommandType.SetContainerContents] = SetContainerContentsCommand;

        // Exult: the party walks after the Avatar.
        if (_player is not null)
            foreach (var member in _mapCharacters.Where(mc => mc.Character.IsInParty)) _followers[member] = _player;
    }

    // ---------- Characters ----------

    private bool JoinPartyCommand(EventCommand command)
    {
        if (CharacterFor(command.Get("Who")) is not { } mapCharacter || IsDead(mapCharacter.Character)) return false;
        var character = mapCharacter.Character;
        bool join = command.Get("Value") != "Leave the party";
        if (character.IsInParty == join) return true;
        character.IsInParty = join;
        _followers.Remove(mapCharacter);
        StopWalking(mapCharacter);
        if (join)
        {
            mapCharacter.Npc.Entry = null;
            mapCharacter.Npc.Activity = null; // a party member has no schedule (Exult)
            mapCharacter.Npc.WalkingToSchedule = false;
            _followers[mapCharacter] = _player!; // it walks with the main character
            StatusText.Text = $"{character.Name} joins the party.";
        }
        else
        {
            UpdateSchedule(mapCharacter, _clock.Period, 0); // back to its own day
            StatusText.Text = $"{character.Name} leaves the party.";
        }
        RefreshAfterMove();
        return true;
    }

    private bool ChangeScheduleCommand(EventCommand command)
    {
        if (CharacterFor(command.Get("Who")) is not { } mapCharacter) return false;
        string activity = command.Get("Activity") switch
        {
            "Pace east-west" => nameof(ScheduleType.HorizPace),
            "Pace north-south" => nameof(ScheduleType.VertPace),
            "Eat at inn" => nameof(ScheduleType.EatAtInn),
            "Back to its schedule" => ChangeBehaviorCommandWindow.Resume,
            var a => a
        };
        string key = mapCharacter.Character.Id.ToString();
        if (!ChangeBehaviorCommand(key, activity)) return false;
        double hours = FormulaNumber(command, "Hours");
        if (hours > 0 && activity != ChangeBehaviorCommandWindow.Resume) _scheduleUntil[mapCharacter] = TotalMinutes() + (long)Math.Round(hours * 60);
        else _scheduleUntil.Remove(mapCharacter);
        return true;
    }

    private bool PlaceCharacterCommand(EventCommand command)
    {
        string whoKey = command.Get("Who");
        if (command.Get("Value") == "Remove it from the map")
        {
            if (CharacterFor(whoKey) is not { } gone) return false;
            StopWalking(gone);
            if (gone.Visual is { } visual) { UnregisterWorldAnimator(visual); GameCanvas.Children.Remove(visual); }
            _mapCharacters.Remove(gone);
            _followers.Remove(gone);
            _offMap[gone.Character] = gone.Placement;
            return true;
        }
        if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y) || !InsideMap(x, y)) return false;
        int floor = int.TryParse(command.Get("Floor"), out int f) ? f : 1;
        if (CharacterFor(whoKey) is { } there)
        {
            Teleport(there, (x, y));
            there.Lift = StandingLiftOnFloor(CharacterFootprint(there), floor, there.Placement);
            PositionCharacterVisual(there);
            return true;
        }
        // Not on the map: off it (removed earlier), or never placed - it comes in at the spot.
        var character = _offMap.Keys.FirstOrDefault(c => c.Id.ToString() == whoKey) ?? _characters?.Characters.FirstOrDefault(c => c.Id.ToString() == whoKey && !c.IsTestPlayer);
        if (character is null || IsDead(character)) return false;
        var placement = _offMap.TryGetValue(character, out var old) ? old : new PlacedCharacter { Id = _map.Characters.Count == 0 ? 1 : _map.Characters.Max(p => p.Id) + 1, CharacterId = character.Id };
        _offMap.Remove(character);
        placement.X = x;
        placement.Y = y;
        placement.Floor = floor;
        BringOntoMap(character, placement);
        return true;
    }

    /// <summary>A character (back) on the map at its placement: figure, floor, schedule.</summary>
    private MapCharacter BringOntoMap(CharacterDefinition character, PlacedCharacter placement)
    {
        var mapCharacter = new MapCharacter
        {
            Placement = placement, Character = character, Size = ActorSizing.Resolve(character, _poseDatabase, _sprites),
            PixelX = placement.X * _tileWidth + _tileWidth / 2.0, PixelY = placement.Y * _tileHeight + _tileHeight / 2.0, Facing = WorldOfSlot(placement.Facing) // picked as seen on the screen
        };
        _mapCharacters.Add(mapCharacter);
        mapCharacter.Lift = StandingLiftOnFloor(CharacterFootprint(mapCharacter), placement.Floor, placement);
        if (character.Movement == MovementKind.Air) mapCharacter.Lift = CruiseLift(mapCharacter); // a flier is up in the air
        mapCharacter.Visual = BuildCharacterVisual(mapCharacter);
        GameCanvas.Children.Add(mapCharacter.Visual);
        PositionCharacterVisual(mapCharacter);
        if (!character.IsInParty) UpdateSchedule(mapCharacter, _clock.Period, 0);
        return mapCharacter;
    }

    private bool TurnToFaceCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who) return false;
        var (fromX, fromY) = PixelOf(who);
        SpriteDirection? facing = command.Get("Towards") switch
        {
            "North" => WorldOfSlot(SpriteDirection.North), "South" => WorldOfSlot(SpriteDirection.South), "East" => WorldOfSlot(SpriteDirection.East), "West" => WorldOfSlot(SpriteDirection.West), // as seen on the screen
            "The main player" => FacingTowards(fromX, fromY, _playerPixelX, _playerPixelY),
            "Whoever set it off" when (_activator?.Character ?? _player) is { } a => PixelOf(a) is var p ? FacingTowards(fromX, fromY, p.X, p.Y) : null,
            "Another character" when CharacterDefinitionFor(command.Get("Other")) is { } other => PixelOf(other) is var q ? FacingTowards(fromX, fromY, q.X, q.Y) : null,
            _ => null
        };
        if (facing is not { } dir) return false;
        if (who == _player) { _playerFacing = dir; ApplyPoseSprite(_idlePose); return true; }
        if (MapCharacterOf(who) is not { } mc) return false;
        SetCharacterPose(mc, mc.PoseName, dir);
        return true;
    }

    private bool FollowCommand(EventCommand command)
    {
        if (CharacterFor(command.Get("Who")) is not { } follower) return false;
        if (command.Get("Value") == "Stop following") { _followers.Remove(follower); StopWalking(follower); return true; }
        if (CharacterDefinitionFor(command.Get("Other")) is not { } leader || leader == follower.Character) return false;
        follower.Npc.Entry = null;
        follower.Npc.Activity = null;
        follower.Npc.WalkingToSchedule = false;
        _followers[follower] = leader;
        return true;
    }

    /// <summary>Followers walk after their leader, keeping within 2 cells (thinking again every 2/5 s).</summary>
    private void UpdateFollowers(double elapsedMs)
    {
        if (_followers.Count == 0 && _scheduleUntil.Count == 0) return;
        _followClockMs += elapsedMs;
        _followThinkMs -= elapsedMs;
        if (_followThinkMs > 0) return;
        _followThinkMs = 400;
        long minute = TotalMinutes();
        foreach (var (mc, until) in _scheduleUntil.ToList())
            if (minute >= until) { _scheduleUntil.Remove(mc); if (_mapCharacters.Contains(mc)) ChangeBehaviorCommand(mc.Character.Id.ToString(), ChangeBehaviorCommandWindow.Resume); }
        foreach (var (follower, leader) in _followers.ToList())
        {
            if (!_mapCharacters.Contains(follower) || IsDead(follower.Character) || IsDead(leader)) { _followers.Remove(follower); continue; }
            if (IsIncapacitated(follower.Character) || follower.Npc.InCombat || IsScripted(follower.Character)) continue; // carrying out an event's command (walking to a pumpkin...): it isn't pulled back
            if (leader == _player && SitsWithPlayer(follower)) continue; // sitting with them, or on the way to a chair
            var target = CellOf(leader);
            var at = follower.Placement;
            if (CellsApart(target, (at.X, at.Y)) <= 2) continue; // the short way round a map that wraps around: they follow over the seam
            if (FindPath(follower, target, allowPartial: true, besideOnly: true, goalFloor: MapFloors.FloorOfLift(LiftOf(leader))) is { Count: > 0 } path) StartPath(follower, path);
        }
    }

    /// <summary>
    /// The main character sat down on a chair: a party member following them looks for a seat too, as Exult's
    /// Sit_schedule::set_action does - the closest chair within 24 tiles with a free seat it can walk to, walked to and
    /// sat on. None free: it stays standing near them (following as before) and looks again every 1-2 seconds
    /// (Sit_schedule::now_what: try again later). The main character getting up: those who sat with them get up and follow.
    /// True while it is sitting or on its way to a seat.
    /// </summary>
    private bool SitsWithPlayer(MapCharacter follower)
    {
        var me = follower.Character;
        bool playerSits = _player is not null && _seats.TryGetValue(_player, out var seat) && !seat.Bed;
        if (!playerSits)
        {
            _seatLookAgainAt.Remove(follower);
            if (_sittingWithPlayer.Remove(follower) && (_seats.ContainsKey(me) || _headingToSeat.ContainsKey(me)))
            {
                if (_headingToSeat.ContainsKey(me)) StopWalking(follower);
                StandUp(me);
            }
            return false;
        }
        if (!me.IsInParty) return false;
        if (_seats.ContainsKey(me) || _headingToSeat.ContainsKey(me)) { _sittingWithPlayer.Add(follower); return true; } // gets up with them, however it came to sit
        if (_seatLookAgainAt.TryGetValue(follower, out var at) && _followClockMs < at) return false; // none free last time: standing near, as before
        if (GoToFurniture(follower, bed: false, maxTiles: 24)) { _sittingWithPlayer.Add(follower); _seatLookAgainAt.Remove(follower); return true; }
        _seatLookAgainAt[follower] = _followClockMs + 1000 + Rng.Next(1000);
        return false;
    }

    private bool ChangeLookCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who) return false;
        if (command.Get("Own") == "True")
        {
            if (!_ownLooks.Remove(who, out var own)) return true;
            who.PoseSprites = own;
        }
        else
        {
            if (CharacterDefinitionFor(command.Get("Other")) is not { } other || other == who) return false;
            _ownLooks.TryAdd(who, who.PoseSprites);
            who.PoseSprites = new Dictionary<int, PoseAssignment>(other.PoseSprites);
        }
        if (who == _player) ApplyPoseSprite(_idlePose);
        else if (MapCharacterOf(who) is { } mc) { mc.ShownSprite = null; SetCharacterPose(mc, mc.PoseName, mc.Facing); }
        return true;
    }

    private bool ResurrectCommand(EventCommand command)
    {
        string whoKey = command.Get("Who");
        if (command.Get("Value") != "Resurrect")
        {
            if (CharacterDefinitionFor(whoKey) is not { } who || IsDead(who)) return false;
            HealFully(who);
            return true;
        }
        // Any character - the main character too (its By Death event raising it, as Lord British the Avatar).
        bool player = whoKey == WhoChoice.Player || _player is not null && _player.Id.ToString() == whoKey || CharacterDefinitionFor(whoKey) == _player;
        CharacterDefinition? raised;
        if (player)
        {
            if (_player is null || !IsDead(_player)) return false;
            ResurrectPlayer();
            raised = _player;
        }
        else
        {
            var dead = _bodies.Keys.FirstOrDefault(c => c.Id.ToString() == whoKey || CharacterDefinitionFor(whoKey) == c);
            if (dead is null || !ResurrectDead(dead)) return false;
            raised = dead;
        }
        if (command.Get("Where") == "At a spot" && int.TryParse(command.Get("X"), out int x) && int.TryParse(command.Get("Y"), out int y) && InsideMap(x, y))
        {
            int floor = int.TryParse(command.Get("Floor"), out int f) ? f : 1;
            if (player)
            {
                _playerX = x; _playerY = y;
                _playerPixelX = x * _tileWidth + _tileWidth / 2.0; _playerPixelY = y * _tileHeight + _tileHeight / 2.0;
                _playerZ = StandingLiftOnFloor(PlayerFootprint(), floor);
                _playerVisualLiftZ = _playerZ;
                SnapPlayerPosition();
                UpdateCamera();
            }
            else if (MapCharacterOf(raised) is { } mc)
            {
                Teleport(mc, (x, y));
                mc.Lift = StandingLiftOnFloor(CharacterFootprint(mc), floor, mc.Placement);
                PositionCharacterVisual(mc);
            }
        }
        if (command.Get("Health") == "This many hit points")
            SetStat(raised, BuiltInStatKind.Hits, Math.Clamp((int)Math.Round(FormulaNumber(command, "HP")), 1, Math.Max(1, Stat(raised, BuiltInStatKind.Strength, 1))));
        return true;
    }

    /// <summary>The main character lives again (Resurrect in its By Death event): the game is no longer over - standing, healed, the map's music again.</summary>
    private void ResurrectPlayer()
    {
        if (_player is null) return;
        var vitals = VitalsOf(_player);
        vitals.Dead = false;
        vitals.Unconscious = false;
        _gameOver = false;
        HealFully(_player);
        ApplyPoseSprite(_idlePose);
        SoundPlayback.StopFor(BookEventPreviewExecutionContext.MusicOwner);
        StopCombatMusic(); // the death music ends
        _ambientPlaying = -2; // the map's own sound starts again
        SnapPlayerPosition();
    }

    /// <summary>A dead character lives again where its body lies, with what's still in it (Resurrect, the spell or the command).</summary>
    private bool ResurrectDead(CharacterDefinition dead)
    {
        if (!_bodies.TryGetValue(dead, out var lying)) return false;
        var (body, placement) = lying;
        _bodies.Remove(dead);
        var vitals = VitalsOf(dead);
        vitals.Dead = false;
        vitals.Unconscious = false;
        // Out of its body: what's still in it goes back to it; the body is gone.
        int x = body.X, y = body.Y;
        if (_map.Items.Contains(body))
        {
            dead.Inventory.AddRange(body.Contents);
            body.Contents.Clear();
            _map.Items.Remove(body);
            if (_itemVisuals.Remove(body, out var bodyVisual)) { GameCanvas.Children.Remove(bodyVisual); UnregisterWorldAnimator(bodyVisual); }
        }
        placement.X = x;
        placement.Y = y;
        placement.Dead = false;
        placement.BodyInstanceId = -1;
        HealFully(dead);
        BringOntoMap(dead, placement);
        SettleWorld();
        RefreshAfterMove();
        StatusText.Text = $"{dead.Name} lives again!";
        return true;
    }

    private void HealFully(CharacterDefinition who)
    {
        SetHp(who, MaxHp(who));
        var vitals = VitalsOf(who);
        foreach (var ailment in Enum.GetValues<Ailment>().Where(a => a != Ailment.None)) Cure(who, ailment);
        if (vitals.Unconscious) { vitals.Unconscious = false; ResumeActing(who); }
    }

    // ---------- Objects and the map ----------

    private bool CreateObjectCommand(EventCommand command)
    {
        if (!Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) || !int.TryParse(command.Get("ItemId"), out int itemId) || itemId < 0) return false;
        if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y) || !InsideMap(x, y)) return false;
        int floor = int.TryParse(command.Get("Floor"), out int f) ? f : 1;
        int quantity = Math.Max(1, (int)FormulaNumber(command, "Quantity"));
        foreach (var gift in MakeGifts(category, itemId, quantity)) PlaceNewThing(gift, x, y, floor);
        RefreshAfterMove();
        return true;
    }

    /// <summary>A new thing on the map at a cell of a floor, resting on whatever is there.</summary>
    private void PlaceNewThing(InventoryItemInstance gift, int x, int y, int floor)
    {
        var entity = CreateMapEntityFor(gift);
        var placedItem = entity as PlacedItemInstance;
        var placedObject = entity as ObjectInstance;
        if (placedObject is not null) { placedObject.ContainerInstanceId = -1; placedObject.InstanceId = _map.GetNextInstanceId(); }
        AddMapEntity(placedItem, placedObject);
        SetMapPosition(placedItem, placedObject, x, y, 0, 0);
        var body = BodyOf(placedItem, placedObject);
        SetZ(entity, StandingLiftOnFloor(new UnitRect(body.Left, body.Top, body.Width, body.Depth), floor, entity));
        ForgetHomeFloor(entity);
        SettleWorld();
    }

    private bool DeleteObjectCommand(EventCommand command)
    {
        switch (ThingOf(command))
        {
            case ObjectInstance o:
                RemoveMapObject(o);
                break;
            case PlacedItemInstance i:
                _map.Items.Remove(i);
                if (_itemVisuals.Remove(i, out var visual)) { GameCanvas.Children.Remove(visual); UnregisterWorldAnimator(visual); }
                break;
            default:
                return false;
        }
        SettleWorld();
        RefreshAfterMove();
        return true;
    }

    /// <summary>Delete Object Self: the placed copy whose event this is goes, wherever it is. False when the event isn't a placed object's.</summary>
    private bool DeleteObjectSelfCommand(EventCommand command)
    {
        if (_firingOwner is not ObjectInstance self || !_map.Objects.Contains(self)) return false;
        RemoveMapObject(self);
        SettleWorld();
        RefreshAfterMove();
        return true;
    }

    private bool MoveObjectCommand(EventCommand command)
    {
        var thing = ThingOf(command);
        if (thing is null || !int.TryParse(command.Get("ToX"), out int x) || !int.TryParse(command.Get("ToY"), out int y) || !InsideMap(x, y)) return false;
        int floor = int.TryParse(command.Get("ToFloor"), out int f) ? f : 1;
        var placedItem = thing as PlacedItemInstance;
        var placedObject = thing as ObjectInstance;
        SetMapPosition(placedItem, placedObject, x, y, 0, 0);
        var body = BodyOf(placedItem, placedObject);
        SetZ(thing, StandingLiftOnFloor(new UnitRect(body.Left, body.Top, body.Width, body.Depth), floor, thing));
        ForgetHomeFloor(thing);
        SettleWorld();
        RefreshAfterMove();
        return true;
    }

    private bool ChangeObjectLookCommand(EventCommand command)
    {
        if (ThingOf(command) is not ObjectInstance o) { StatusText.Text = "Change Object Look: only objects (Object Editor) can change their look."; return false; }
        o.LookOverrideRuntime = command.Get("Own") == "True" ? null : $"{command.Get("SpriteGroup")}/{command.Get("SpriteName")}";
        RefreshObjectLook(o);
        return true;
    }

    private bool SetDoorStateCommand(EventCommand command)
    {
        string value = command.Get("Value");
        switch (ThingOf(command))
        {
            case ObjectInstance door when DefinitionOf(door) is { } obj:
                switch (value)
                {
                    case "Locked":
                        if (IsDoorOpen(door)) door.IsOpenRuntime = false; // locked shut
                        door.IsLockedRuntime = true;
                        break;
                    case "Unlocked": door.IsLockedRuntime = false; break;
                    case "Closed": door.IsOpenRuntime = false; break;
                    case "Open": door.IsLockedRuntime = false; door.IsOpenRuntime = true; break;
                }
                RefreshObjectLook(door);
                return true;
            case PlacedItemInstance chest when chest.Category == ReferencedItemCategory.Container:
                if (value is "Locked" or "Unlocked") chest.IsLockedRuntime = value == "Locked";
                return true;
        }
        return false;
    }

    private bool CountItemsCommand(EventCommand command)
    {
        if (!Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) || !int.TryParse(command.Get("ItemId"), out int itemId)) return false;
        int count = 0;
        switch (command.Get("Where"))
        {
            case "The whole party":
                count = PartyAndPlayer().Sum(c => CountCarried(c, category, itemId));
                break;
            case "A container on the map":
                if (ThingOf(command) is not PlacedItemInstance container) return false;
                void Walk(List<InventoryItemInstance> list) { foreach (var i in list) { if (i.Category == category && i.ItemId == itemId) count += Math.Max(1, i.Quantity); Walk(i.Contents); } }
                Walk(ContentsOf(container));
                break;
            default:
                if (CharacterDefinitionFor(command.Get("Who")) is not { } who) return false;
                count = CountCarried(who, category, itemId);
                break;
        }
        SetScriptVariable(command.Get("Variable"), (double)count);
        return true;
    }

    /// <summary>A placed container's live contents (its starting contents put in the first time).</summary>
    private List<InventoryItemInstance> ContentsOf(PlacedItemInstance container)
    {
        EnsureContainerSeeded(container, ContainerDef(container.ItemId) ?? new ContainerData());
        return container.Contents;
    }

    private bool SetContainerContentsCommand(EventCommand command)
    {
        if (ThingOf(command) is not PlacedItemInstance { Category: ReferencedItemCategory.Container } container) return false;
        var contents = ContentsOf(container);
        if (command.Get("Empty") == "True") contents.Clear();
        if (Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) && int.TryParse(command.Get("ItemId"), out int itemId) && itemId >= 0)
        {
            var data = ContainerDef(container.ItemId) ?? new ContainerData();
            foreach (var gift in MakeGifts(category, itemId, Math.Max(1, (int)FormulaNumber(command, "Quantity")))) { PlaceWithinFrame(gift, data, null); contents.Add(gift); }
        }
        RefreshAfterMove();
        return true;
    }
}
