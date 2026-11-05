// Ultima 8 engine (U8GameWindow) only - Crusader's features in an Ultima 8 project (the user asked for them in U8 only).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Crusader in an Ultima 8 project:
///  - Controlling another character. The keys switch between the party's members (Control the next party member, Control
///    the main character again); the Control Character command takes any character (a robot, a guard taken over), Return
///    Control gives the main character back. The one controlled is "the player" for everything: walking, fighting, its
///    inventory, the events it steps on. The one left stands where it was: a party member follows, anyone else goes on
///    with its own day. Saves are written as if the main character were controlled, and say who was.
///  - Rolling (left, right: no blow lands while rolling, no shot either), side-stepping (keeping the way it faces), kneeling
///    (a shot flies over 1 time in 2).
///  - The crosshair (System Settings > Ultima 8): in combat mode it sits on the nearest foe (one in front first); the Next
///    Target key moves it on; swings and shots go to it. The Attack key swings or shoots without the mouse.
/// </summary>
public partial class U8GameWindow
{
    // ================= Who the player controls =================

    private CharacterDefinition? _mainCharacter;   // set on the first switch: the main character
    private bool _mainWasInParty;                  // its own IsInParty, while someone else is controlled (it counts as in the party then)
    private bool _controlLocked;                   // Control Character with Lock: only Return Control switches back
    private readonly Dictionary<CharacterDefinition, PlacedCharacter> _controlPlacements = new(ReferenceEqualityComparer.Instance);

    private CharacterDefinition? MainCharacter => _mainCharacter ?? _player;
    private bool ControllingOther => _mainCharacter is not null && _player != _mainCharacter;

    private void InitU8Crusader()
    {
        var h = _execContext.GameHandlers;
        h[EventCommandType.U8ControlCharacter] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
            if (who != _player && !SwapControlTo(who)) return false;
            _controlLocked = c.Get("Lock") != "False" && who != MainCharacter;
            return true;
        };
        h[EventCommandType.U8ReturnControl] = _ =>
        {
            _controlLocked = false;
            return !ControllingOther || SwapControlTo(_mainCharacter!, force: true);
        };
    }

    private string? WhyCannotControl(CharacterDefinition target, bool force)
    {
        if (MapCharacterOf(target) is null) return $"{target.Name} isn't here.";
        if (force) return null;
        if (_gameOver) return "The game is over.";
        if (IsDead(target)) return $"{target.Name} is dead.";
        if (IsUnconscious(target)) return $"{target.Name} is knocked out.";
        if (IsIncapacitated(target)) return $"{target.Name} can't move now (asleep or paralyzed).";
        if (IsScripted(target)) return $"{target.Name} is busy.";
        if (_driving is not null) return "Not while driving.";
        if (_jump is not null || _u8Hanging || _u8FallFrom is not null) return "Not in mid-air.";
        return null;
    }

    /// <summary>The player controls this character from now on; the one controlled so far stays where it stood, as a character on the map.</summary>
    private bool SwapControlTo(CharacterDefinition target, bool quiet = false, bool force = false)
    {
        if (_player is null) return false;
        if (target == _player) return true;
        if (WhyCannotControl(target, force) is { } why) { if (!quiet) StatusText.Text = why; return false; }
        var mc = MapCharacterOf(target)!;
        _mainCharacter ??= _player;
        var main = _mainCharacter;
        var old = _player;

        // What the player was doing stops.
        if (PlayerSeated) StandUp(old);
        if (_seats.ContainsKey(target)) StandUp(target);
        CancelPlayerWalk();
        CancelPendingSit();
        _u8Step = null;
        _jump = null;
        _u8FallFrom = null;
        _u8Hanging = false;
        _heldDirectionKeys.Clear();
        _playerTarget = null;
        _playerTargetThing = null;
        _u8Blocking = false;
        _u8Kneeling = false;
        _crosshairTarget = null;
        StopWalking(mc);
        _followers.Remove(mc);
        _scheduleUntil.Remove(mc);
        _leaping.Remove(mc);

        // The one left: onto the map where it stood.
        double px = _playerPixelX, py = _playerPixelY;
        int pz = _playerZ;
        var facing = _playerFacing;
        if (old == main) { _mainWasInParty = main.IsInParty; main.IsInParty = true; }
        var place = _controlPlacements.Remove(old, out var kept) ? kept : new PlacedCharacter { Id = NextPlacementId(), CharacterId = old.Id };
        place.X = _playerX;
        place.Y = _playerY;
        place.Floor = MapFloors.FloorOfLift(Math.Max(0, pz));
        place.Dead = false;
        place.SavedCharacterIndex = SavedIndexOf(old);
        _map.Characters.Add(place);
        var left = BringOntoMap(old, place);
        left.PixelX = px;
        left.PixelY = py;
        left.Lift = pz;
        left.ShownSprite = null;
        SetCharacterPose(left, _combatMode ? "Combat Stand" : "Idle", facing);
        PositionCharacterVisual(left);

        // The one taken: off the map, into the player's place.
        if (mc.Visual is { } visual) { UnregisterWorldAnimator(visual); GameCanvas.Children.Remove(visual); }
        _mapCharacters.Remove(mc);
        _map.Characters.Remove(mc.Placement);
        _controlPlacements[target] = mc.Placement;
        if (target == main) main.IsInParty = _mainWasInParty;
        _player = target;
        _playerSize = mc.Size;
        PlacePlayer((mc.Placement.X, mc.Placement.Y), mc.Lift);
        _playerPixelX = mc.PixelX;
        _playerPixelY = mc.PixelY;
        _playerFacing = mc.Facing;
        _playerImage.Visibility = Visibility.Visible;
        ApplyPoseSprite(U8RestPose());
        DrawPlayerU8();

        // Who follows whom: the party the one controlled, if it is of the party; else the main character.
        bool ofParty = target == main || target.IsInParty;
        foreach (var member in _mapCharacters.Where(m => m.Character.IsInParty && !IsDead(m.Character)))
        {
            if (!ofParty && member.Character == main) { _followers.Remove(member); continue; } // waits where it stood
            _followers[member] = ofParty ? target : main;
        }
        if (!quiet) StatusText.Text = target == main ? $"You control {target.Name} again." : $"You control {target.Name}.";
        SoakLog($"control: {target.Name} ({(target == main ? "the main character" : ofParty ? "party" : "not of the party")})");
        return true;
    }

    private int NextPlacementId()
    {
        int most = _map.Characters.Count == 0 ? 0 : _map.Characters.Max(p => p.Id);
        if (_controlPlacements.Count > 0) most = Math.Max(most, _controlPlacements.Values.Max(p => p.Id));
        return most + 1;
    }

    /// <summary>The Control the next party member key: the main character, then each member here, round again.</summary>
    private void ControlNextMember()
    {
        if (_player is null || MainCharacter is not { } main) return;
        if (_controlLocked) { StatusText.Text = "You can't switch now."; return; }
        var order = new List<CharacterDefinition> { main };
        order.AddRange(_mapCharacters.Select(m => m.Character).Where(c => c.IsInParty && c != main && !IsDead(c) && !IsIncapacitated(c)));
        if (_player != main && !order.Contains(_player)) order.Add(_player); // controlled by an event, not of the party
        if (order.Count < 2) { StatusText.Text = "Nobody else in the party to control."; return; }
        int at = order.IndexOf(_player);
        for (int step = 1; step < order.Count; step++)
            if (SwapControlTo(order[(at + step) % order.Count])) return;
    }

    /// <summary>The character controlled or the main character died: control goes back to the main character first (its death ends the game as always).</summary>
    private void ControlBackOnDeath(CharacterDefinition dying)
    {
        if (!ControllingOther || (dying != _player && dying != _mainCharacter)) return;
        _controlLocked = false;
        SwapControlTo(_mainCharacter!, quiet: true, force: true);
        if (dying != _mainCharacter) StatusText.Text = $"{dying.Name} is lost - you control {_mainCharacter!.Name} again.";
    }

    // ---------- Saves: written as if the main character were controlled ----------

    private GameSaveData CaptureSave()
    {
        if (!ControllingOther) return CaptureSaveAsIs();
        var controlled = _player!;
        bool locked = _controlLocked, kneeling = _u8Kneeling;
        SwapControlTo(_mainCharacter!, quiet: true, force: true);
        try
        {
            var data = CaptureSaveAsIs();
            data.ControlledCharacterIndex = SavedIndexOf(controlled);
            data.ControlLocked = locked;
            return data;
        }
        finally
        {
            SwapControlTo(controlled, quiet: true, force: true);
            _controlLocked = locked;
            if (kneeling) Kneel(true);
        }
    }

    private void RestoreControl(GameSaveData data)
    {
        if (data.ControlledCharacterIndex < 0 || _characters is not { } list || data.ControlledCharacterIndex >= list.Characters.Count) return;
        if (SwapControlTo(list.Characters[data.ControlledCharacterIndex], quiet: true, force: true)) _controlLocked = data.ControlLocked;
    }

    /// <summary>Change Map to another world: the main character goes, controlled again.</summary>
    private void ControlBackForTravel()
    {
        _controlLocked = false;
        if (ControllingOther) SwapControlTo(_mainCharacter!, quiet: true, force: true);
    }

    // ================= Rolling, side-stepping, kneeling =================

    private bool _u8Kneeling;
    private readonly Dictionary<object, bool> _shotOverKneeling = new(ReferenceEqualityComparer.Instance);

    private bool PlayerDodging => _jump is { Dodge: true };

    /// <summary>The pose the player goes back to after a blow, a shot: kneeling stays kneeling.</summary>
    private PoseDefinition? PlayerBackToPose() => _u8Kneeling ? PoseByName("Kneel") ?? _idlePose : _idlePose;

    private void Kneel(bool down)
    {
        if (_player is null) return;
        _u8Kneeling = down;
        if (down) ApplyPoseSprite(PoseByName("Kneel") ?? U8RestPose());
        else ApplyPoseSprite(HasArtFor(_player, "Stand Up") ? PoseByName("Stand Up") : U8RestPose());
        if (!down && HasArtFor(_player, "Stand Up")) _playerPoseResetMs = PoseLengthMs(_player, "Stand Up");
        SoakLog($"kneel: {(down ? "down" : "up")}");
    }

    /// <summary>A roll (2 cells) or a side-step (1 cell) to the left or right of the way the player faces; it keeps facing that way.</summary>
    private void StartDodge(bool roll, bool left)
    {
        if (_player is null || _jump is not null || _gamePaused || PlayerSeated || IsIncapacitated(_player) || U8Stunned || _u8Hanging || _driving is not null) return;
        _u8Kneeling = false;
        CancelPlayerWalk();
        _u8Step = null;
        var side = (SpriteDirection)(((int)_playerFacing + (left ? 6 : 2)) % 8);
        var (dx, dy) = VectorOf(side);
        double len = Math.Sqrt(dx * dx + dy * dy), cells = roll ? 2 : 1;
        double toX = _playerPixelX + dx / len * cells * _tileWidth, toY = _playerPixelY + dy / len * cells * _tileHeight;
        double lastX = _playerPixelX, lastY = _playerPixelY;
        const int samples = 8;
        for (int i = 1; i <= samples; i++)
        {
            double s = i / (double)samples;
            double x = _playerPixelX + (toX - _playerPixelX) * s, y = _playerPixelY + (toY - _playerPixelY) * s;
            if (x < 0 || y < 0 || x >= _map.Width * _tileWidth || y >= _map.Height * _tileHeight) break;
            var foot = PlayerFootprintAt(x, y);
            if (!TryFlyStep(foot, _playerZ, _playerSize.Z, null, null, out _, out _) && !TryFlyStep(foot, _playerZ + 1, _playerSize.Z, null, null, out _, out _)) break;
            (lastX, lastY) = (x, y);
        }
        int landLift = StandingLiftAt(PlayerFootprintAt(lastX, lastY), _player);
        if (landLift > _playerZ + 1) landLift = _playerZ;
        string pose = roll ? (left ? "Roll Left" : "Roll Right") : (left ? "Side Step Left" : "Side Step Right");
        double seconds = HasArtFor(_player, pose) ? Math.Max(0.25, PoseLengthMs(_player, pose) / 1000.0) : roll ? 0.55 : 0.3;
        _jump = new U8Jump { FromX = _playerPixelX, FromY = _playerPixelY, ToX = lastX, ToY = lastY, Duration = seconds, FromLift = _playerZ, ToLift = landLift, Arc = 0, Dodge = roll, Quiet = true };
        SoakLog($"dodge: {pose}, facing {_playerFacing}, {Math.Round((lastX - _playerPixelX) / _tileWidth, 2)},{Math.Round((lastY - _playerPixelY) / _tileHeight, 2)} cells");
        ApplyPoseSprite(PoseByName(pose) is { } p && HasArtFor(_player, pose) ? p : roll ? PoseByName("Jump") ?? U8RestPose() : U8RestPose());
    }

    /// <summary>A shot that would hit the player: rolling, it misses; kneeling, it flies over 1 time in 2 (decided once a shot).</summary>
    private bool PlayerDodgesShot(object flying)
    {
        if (PlayerDodging) return true;
        if (!_u8Kneeling) return false;
        if (_shotOverKneeling.Count > 200) _shotOverKneeling.Clear();
        if (!_shotOverKneeling.TryGetValue(flying, out bool over)) _shotOverKneeling[flying] = over = Rng.Next(2) == 0;
        return over;
    }

    // ================= The keys =================

    /// <summary>The Crusader keys on the map. True: it was one of them.</summary>
    private bool U8CrusaderKey(Key pressed, bool repeat)
    {
        if (KeyIs(pressed, "ControlNext")) { if (!repeat) ControlNextMember(); return true; }
        if (KeyIs(pressed, "ControlMain"))
        {
            if (repeat) return true;
            if (_controlLocked) StatusText.Text = "You can't switch now.";
            else if (ControllingOther) SwapControlTo(_mainCharacter!);
            return true;
        }
        if (KeyIs(pressed, "Attack"))
        {
            if (repeat || _player is null || IsIncapacitated(_player)) return true;
            if (!_combatMode) { if (_combatForbidden) return true; ToggleCombatMode(); }
            U8Swing(kick: false, fromKeys: true);
            return true;
        }
        if (KeyIs(pressed, "NextTarget")) { if (!repeat) NextCrosshairTarget(); return true; }
        if (KeyIs(pressed, "RollLeft")) { if (!repeat) StartDodge(roll: true, left: true); return true; }
        if (KeyIs(pressed, "RollRight")) { if (!repeat) StartDodge(roll: true, left: false); return true; }
        if (KeyIs(pressed, "SideStepLeft")) { if (!repeat) StartDodge(roll: false, left: true); return true; }
        if (KeyIs(pressed, "SideStepRight")) { if (!repeat) StartDodge(roll: false, left: false); return true; }
        if (KeyIs(pressed, "Kneel"))
        {
            if (!repeat && _player is not null && _jump is null && !PlayerSeated && !IsIncapacitated(_player) && _driving is null) Kneel(!_u8Kneeling);
            return true;
        }
        return false;
    }

    // ================= The crosshair =================

    private CharacterDefinition? _crosshairTarget;
    private FrameworkElement? _crosshair;

    private bool CrosshairWanted => U8Rules.CrosshairOn && _combatMode && _player is not null && !_gameOver;

    private bool CrosshairCanTake(CharacterDefinition c)
    {
        if (_player is null || c == _player || c == _mainCharacter || c.IsInParty || IsDead(c) || !SameFloor(_player, c)) return false;
        if (TilesBetween(_player, c) > Math.Max(1, U8Rules.CrosshairCells)) return false;
        return MapCharacterOf(c) is { } m && IsOnScreen(m.PixelX, m.PixelY);
    }

    private bool IsFoeOfPlayer(CharacterDefinition c) => _player is not null && (Attitude(c, _player) == FactionBehavior.Attack || Attitude(_player, c) == FactionBehavior.Attack || MapCharacterOf(c)?.Npc.Target == _player);

    private bool InFrontOfPlayer(CharacterDefinition c)
    {
        var (fx, fy) = VectorOf(_playerFacing);
        var (x, y) = PixelOf(c);
        double a = Math.Atan2(y - _playerPixelY, x - _playerPixelX), d = Math.Abs(Math.IEEERemainder(a - Math.Atan2(fy, fx), 2 * Math.PI));
        return d <= Math.PI * 3 / 8;
    }

    /// <summary>Who it settles on by itself: a foe in front, a foe, anyone in front - the nearest of those.</summary>
    private CharacterDefinition? AutoCrosshairTarget()
    {
        var all = _mapCharacters.Select(m => m.Character).Where(CrosshairCanTake).ToList();
        return all.Where(c => IsFoeOfPlayer(c) && InFrontOfPlayer(c)).OrderBy(c => TilesBetween(_player!, c)).FirstOrDefault()
               ?? all.Where(IsFoeOfPlayer).OrderBy(c => TilesBetween(_player!, c)).FirstOrDefault()
               ?? all.Where(InFrontOfPlayer).OrderBy(c => TilesBetween(_player!, c)).FirstOrDefault();
    }

    private void NextCrosshairTarget()
    {
        if (!CrosshairWanted) return;
        var all = _mapCharacters.Select(m => m.Character).Where(CrosshairCanTake).OrderBy(c => TilesBetween(_player!, c)).ToList();
        if (all.Count == 0) { _crosshairTarget = null; return; }
        int at = _crosshairTarget is null ? -1 : all.IndexOf(_crosshairTarget);
        _crosshairTarget = all[(at + 1) % all.Count];
        SoakLog($"crosshair: next, on {_crosshairTarget.Name}");
    }

    /// <summary>Each frame: the crosshair on its target, or hidden.</summary>
    private void UpdateCrosshair()
    {
        if (!CrosshairWanted) { _crosshairTarget = null; if (_crosshair is not null) _crosshair.Visibility = Visibility.Hidden; return; }
        if (_crosshairTarget is null || !CrosshairCanTake(_crosshairTarget)) { _crosshairTarget = AutoCrosshairTarget(); if (_crosshairTarget is not null) SoakLog($"crosshair: on {_crosshairTarget.Name}"); }
        if (_crosshairTarget is null || MapCharacterOf(_crosshairTarget) is not { } m) { if (_crosshair is not null) _crosshair.Visibility = Visibility.Hidden; return; }
        _crosshair ??= BuildCrosshair();
        _crosshair.Visibility = Visibility.Visible;
        double up = (m.Visual?.Height ?? _tileHeight) / 2, shift = m.Lift * LiftPixelShift;
        double cx = m.PixelX - shift - up, cy = m.PixelY - shift - up; // its middle: a straight rise on the screen is up-left on the map
        Canvas.SetLeft(_crosshair, cx - _crosshair.Width / 2);
        Canvas.SetTop(_crosshair, cy - _crosshair.Height / 2);
    }

    private FrameworkElement BuildCrosshair()
    {
        var link = U8Rules.CrosshairSprite;
        var sprite = link.IsSet ? ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName)
            : _sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.CrosshairGroupName)?.Sprites.FirstOrDefault(s => s.Frames.Count > 0); // the locked group's first
        FrameworkElement picture;
        if (sprite is { Frames.Count: > 0 })
        {
            var (width, height) = QuantizedSpriteSize(sprite);
            var image = new Image { Width = width, Height = height, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources, 0));
            picture = image;
        }
        else
        {
            // No sprite: a red ring with four ticks.
            var red = new SolidColorBrush(Color.FromRgb(255, 40, 40));
            var drawn = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
            drawn.Children.Add(new Ellipse { Width = 16, Height = 16, Stroke = red, StrokeThickness = 1.5, Margin = new Thickness(4, 4, 0, 0) });
            foreach (var (x1, y1, x2, y2) in new[] { (12.0, 0.0, 12.0, 7.0), (12.0, 17.0, 12.0, 24.0), (0.0, 12.0, 7.0, 12.0), (17.0, 12.0, 24.0, 12.0) })
                drawn.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = red, StrokeThickness = 1.5 });
            picture = drawn;
        }
        U8ProjectionCanvas.SetPlacement(picture, U8Placement.Center);
        Panel.SetZIndex(picture, AboveEverythingZIndex - 2);
        GameCanvas.Children.Add(picture);
        return picture;
    }

    /// <summary>Each frame (after the player has moved).</summary>
    private void UpdateU8Crusader(double deltaSeconds)
    {
        UpdateCrosshair();
        TrackLastGround();
        UpdateLeaps(deltaSeconds);
    }
}
