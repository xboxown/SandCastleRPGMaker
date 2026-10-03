using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Combat, theft and their consequences, ported from Exult (combat.cc,
/// actors.cc, gamewin.cc) with factions standing in for Exult's fixed
/// alignments:
///  - Combat_schedule::now_what: find an opponent, approach it (A*), gain
///    Dexterity points each standard delay until 30, then strike; flee when
///    below 3 HP; give up after more than 5 failures and go back to the
///    schedule.
///  - Opponents (find_opponents / is_enemy): anyone whose faction this one's
///    sees in an "Attack on sight" band; anyone attacking someone it sees as
///    "Ally" (defending them, as Exult's party defends the party). "Flee"
///    makes it run from them (run_away: 8-15 tiles off).
///  - Hits (roll_to_win / try_to_hit / apply_damage): a 30-sided roll plus
///    the attacker's Combat minus the defender's; damage 1..Strength/3, plus
///    1 for bare hands. A hit shows "-N" above the one hit (Exult's show_hits).
///  - fight_back: whoever is hit turns on the attacker (same faction = no
///    fight); if the player was the bully, up to 3 like-minded characters
///    within 20 tiles who can reach the player join in (attack_avatar).
///    The victim's faction thinks less of the attacker's (Faction Editor).
///  - Theft (Game_window::theft): moving something owned by someone else
///    (Belong To / Faction) when a sympathetic witness facing you, within 12
///    tiles, sees it: they follow you (hound) and warn you; after 2-4
///    warnings in one area they and their friends attack. Unseen: maybe
///    "Did you hear something?".
///  - Death: the body lies in its Body pose; double-clicking it opens its
///    inventory to loot (Exult's dead body container).
/// The player fights in combat mode (C, as in Exult): double-click a
/// character to make it your target; you strike it whenever it's in reach.
/// </summary>
public partial class GameTestWindow
{
    private const int DexToAttack = 30;

    private bool _combatMode;
    private CharacterDefinition? _playerTarget;
    private int _playerDexPoints;
    private double _playerStrikeMs, _playerPoseResetMs = -1;
    private double _sightScanMs;
    private int _theftWarnings;
    private (int X, int Y) _theftChunk = (-1, -1);

    // ---------- Who's who ----------

    private MapCharacter? MapCharacterOf(CharacterDefinition? character) => character is null ? null : _mapCharacters.FirstOrDefault(mc => mc.Character == character);

    private (double X, double Y) PixelOf(CharacterDefinition character) =>
        character == _player ? (_playerPixelX, _playerPixelY) : MapCharacterOf(character) is { } mc ? (mc.PixelX, mc.PixelY) : (double.NaN, double.NaN);

    private (int X, int Y) CellOf(CharacterDefinition character) =>
        character == _player ? (_playerX, _playerY) : MapCharacterOf(character) is { } mc ? (mc.Placement.X, mc.Placement.Y) : (-999, -999);

    private int LiftOf(CharacterDefinition character) => character == _player ? _playerZ : MapCharacterOf(character)?.Lift ?? 0;

    private int Stat(CharacterDefinition character, BuiltInStatKind kind, int fallback) =>
        _stats.Stats.FirstOrDefault(s => s.Kind == kind) is { } stat && character.StatValues.TryGetValue(stat.Id, out var text) && int.TryParse(text, out int value) ? value : fallback;

    /// <summary>Exult's tile distance (8px tiles, the larger axis) between two characters.</summary>
    private double TilesBetween(CharacterDefinition a, CharacterDefinition b)
    {
        var (ax, ay) = PixelOf(a);
        var (bx, by) = PixelOf(b);
        return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) / SpriteSizing.BaseUnit;
    }

    /// <summary>Melee reach: next to each other (neighbouring map cells, diagonals included) and within a few lifts - the closest two characters can stand on this map's cells.</summary>
    private bool InReach(CharacterDefinition a, CharacterDefinition b)
    {
        var (ax, ay) = CellOf(a);
        var (bx, by) = CellOf(b);
        return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) <= 1 && Math.Abs(LiftOf(a) - LiftOf(b)) <= 4;
    }

    /// <summary>What one character does about another, by their factions (FactionDatabase bands).</summary>
    private FactionBehavior Attitude(CharacterDefinition viewer, CharacterDefinition other) =>
        viewer == other ? FactionBehavior.Ally : BehaviorBetween(viewer, other);

    private bool SameSide(CharacterDefinition a, CharacterDefinition b) => a.FactionId >= 0 && a.FactionId == b.FactionId;

    /// <summary>Can see: within a screen's distance and nothing tall in between.</summary>
    private bool CanSee(CharacterDefinition viewer, CharacterDefinition other)
    {
        var (vx, vy) = PixelOf(viewer);
        var (ox, oy) = PixelOf(other);
        if (double.IsNaN(vx) || double.IsNaN(ox)) return false;
        if (Math.Abs(vx - ox) > ViewportWidth / 2 || Math.Abs(vy - oy) > ViewportHeight / 2) return false;
        return HasLineOfSight(vx, vy, LiftOf(viewer), ox, oy);
    }

    /// <summary>Nothing solid taller than eye level (3 lifts above the feet) lies on the straight line between two feet positions. Characters don't block sight.</summary>
    private bool HasLineOfSight(double fromX, double fromY, int fromLift, double toX, double toY)
    {
        var solids = WorldBodies().Where(b => b.Solid && b.Entity is not PlacedCharacter && b.TopLift > fromLift + 3 && b.Z <= fromLift + 3).ToList();
        if (solids.Count == 0) return true;
        double length = Math.Max(Math.Abs(toX - fromX), Math.Abs(toY - fromY));
        int steps = Math.Max(1, (int)(length / 4));
        for (int i = 1; i < steps; i++)
        {
            double x = fromX + (toX - fromX) * i / steps, y = fromY + (toY - fromY) * i / steps + _tileHeight / 2.0 - SpriteSizing.BaseUnit / 2.0;
            int ux = (int)Math.Floor(x / SpriteSizing.BaseUnit), uy = (int)Math.Floor(y / SpriteSizing.BaseUnit);
            if (solids.Any(b => ux >= b.Left && ux < b.Right && uy >= b.Top && uy < b.Bottom)) return false;
        }
        return true;
    }

    private IEnumerable<CharacterDefinition> LivingCombatants() => AllLivingCharacters();

    // ---------- Each frame ----------

    private void UpdateCombat(double elapsedMs)
    {
        if (_gameOver || _player is null) return;
        UpdatePlayerAttack(elapsedMs);

        _sightScanMs -= elapsedMs;
        if (_sightScanMs > 0) return;
        _sightScanMs = 1000;
        foreach (var mapCharacter in _mapCharacters.ToList()) ScanSurroundings(mapCharacter);
    }

    /// <summary>Once a second, a character not already fighting looks around: enemies on sight, someone it fears, an ally under attack.</summary>
    private void ScanSurroundings(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        var npc = mapCharacter.Npc;
        if (IsIncapacitated(me) || npc.InCombat) return;
        foreach (var other in LivingCombatants())
        {
            if (other == me || !CanSee(me, other)) continue;
            switch (Attitude(me, other))
            {
                case FactionBehavior.Attack:
                    StartCombat(mapCharacter, other);
                    return;
                case FactionBehavior.Flee when !npc.Fleeing:
                    npc.Fleeing = true;
                    npc.FleeFrom = other;
                    StopWalking(mapCharacter);
                    npc.WalkingToSchedule = false;
                    npc.InCombat = true;
                    npc.WaitMs = 0;
                    return;
            }
        }
        // Defending an ally (or the party defending the player): whoever is attacking someone we see as Ally, if we don't see them as Ally too.
        foreach (var other in LivingCombatants())
        {
            if (other == me) continue;
            var victim = TargetOf(other);
            if (victim is null || IsDead(victim) || victim == me) continue;
            bool defend = Attitude(me, victim) == FactionBehavior.Ally || (victim == _player && me.IsInParty);
            if (defend && Attitude(me, other) != FactionBehavior.Ally && !SameSide(me, other) && CanSee(me, other))
            {
                StartCombat(mapCharacter, other);
                return;
            }
        }
    }

    private CharacterDefinition? TargetOf(CharacterDefinition character) =>
        character == _player ? (_combatMode ? _playerTarget : null) : MapCharacterOf(character) is { Npc.InCombat: true } mc ? mc.Npc.Target : null;

    // ---------- NPC combat (Combat_schedule) ----------

    private void StartCombat(MapCharacter mapCharacter, CharacterDefinition target)
    {
        if (IsDead(mapCharacter.Character) || IsDead(target) || target == mapCharacter.Character) return;
        var npc = mapCharacter.Npc;
        if (!npc.InCombat)
        {
            StopWalking(mapCharacter);
            npc.WalkingToSchedule = false;
            npc.Hounding = false;
            npc.InCombat = true;
            npc.Failures = 0;
            npc.DexPoints = 0;
            npc.WaitMs = StdDelay;
        }
        npc.Fleeing = false;
        npc.Target = target;
    }

    private void EndCombat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        npc.InCombat = false;
        npc.Fleeing = false;
        npc.Target = null;
        npc.FleeFrom = null;
        npc.Entry = null;
        npc.Activity = null;
        npc.OldScheduleLoc = null;
        SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
        UpdateSchedule(mapCharacter, _clock.Period, 0); // back to its normal schedule
    }

    /// <summary>Combat_schedule::now_what for a character.</summary>
    private void CombatNowWhat(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        var npc = mapCharacter.Npc;
        if (mapCharacter.ShownSprite is not null && npc.Path is null) SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing); // back from a strike to the ready stance

        if (npc.Fleeing)
        {
            // Run while the one feared is still in sight.
            if (npc.FleeFrom is null || IsDead(npc.FleeFrom) || !CanSee(me, npc.FleeFrom) || npc.FleeCount > 6) { npc.FleeCount = 0; EndCombat(mapCharacter); return; }
            RunAway(mapCharacter);
            return;
        }

        // Need a new opponent? (Need_new_opponent: none, dead)
        if (npc.Target is null || IsDead(npc.Target)) npc.Target = FindFoe(mapCharacter);
        if (npc.Target is null)
        {
            if (++npc.Failures > 5) { EndCombat(mapCharacter); return; }
            npc.WaitMs = 400; // try again in 2/5 s
            return;
        }
        var target = npc.Target;

        // Time to run? (approach_foe: below 3 HP, unless it can't die)
        if ((GetHp(me) ?? 99) < 3)
        {
            npc.Fleeing = true;
            npc.FleeFrom = target;
            RunAway(mapCharacter);
            return;
        }

        if (!InReach(me, target))
        {
            var (tx, ty) = CellOf(target);
            var path = FindPath(mapCharacter, (tx, ty));
            if (path is { Count: > 0 })
            {
                npc.Failures = 0;
                StartPath(mapCharacter, path.Take(3).ToList()); // walk there, checking again on the way (the target moves)
            }
            else
            {
                npc.Failures++;
                if (npc.Failures > 5) { EndCombat(mapCharacter); return; }
                npc.WaitMs = 2 * StdDelay + Rng.Next(500);
            }
            return;
        }

        if (npc.DexPoints >= DexToAttack)
        {
            npc.DexPoints -= DexToAttack;
            var (mx, my) = PixelOf(me);
            var (ox, oy) = PixelOf(target);
            SetCharacterPose(mapCharacter, "Attacking Melee", FacingTowards(mx, my, ox, oy));
            Strike(me, target);
            npc.WaitMs = 2 * StdDelay; // the swing, then back to ready
        }
        else
        {
            npc.DexPoints += Math.Max(1, Stat(me, BuiltInStatKind.Dexterity, 10));
            npc.WaitMs = StdDelay;
        }
    }

    /// <summary>find_foe (nearest mode): the nearest one this character would attack, or who is attacking it or its allies.</summary>
    private CharacterDefinition? FindFoe(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        return LivingCombatants()
            .Where(other => other != me && CanSee(me, other)
                && (Attitude(me, other) == FactionBehavior.Attack
                    || (TargetOf(other) is { } victim && (victim == me || Attitude(me, victim) == FactionBehavior.Ally) && !SameSide(me, other))))
            .OrderBy(other => TilesBetween(me, other))
            .FirstOrDefault();
    }

    /// <summary>Combat_schedule::run_away: off 8-15 tiles in a random diagonal direction.</summary>
    private void RunAway(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        npc.FleeCount++;
        int dx = Rng.Next(2) * 2 - 1, dy = Rng.Next(2) * 2 - 1;
        int x = Math.Clamp(mapCharacter.Placement.X + dx * CellsForTiles(8 + Rng.Next(8)), 0, _map.Width - 1);
        int y = Math.Clamp(mapCharacter.Placement.Y + dy * CellsForTiles(8 + Rng.Next(8)), 0, _map.Height - 1);
        if (FindPath(mapCharacter, (x, y)) is { Count: > 0 } path) StartPath(mapCharacter, path);
        else npc.WaitMs = StdDelay + Rng.Next(1000);
        if (npc.FleeCount == 1 && Rng.Next(3) > 0) Say(mapCharacter, "Help!");
    }

    // ---------- The player's attacks ----------

    private void ToggleCombatMode()
    {
        _combatMode = !_combatMode;
        if (!_combatMode) { _playerTarget = null; _playerTargetThing = null; }
        ShowPointer(RestingPointer); // the attack pointer while in combat mode
        StatusText.Text = _combatMode
            ? "Combat mode: double-click someone to attack them; you strike whenever they're in reach. C to stop."
            : "Combat mode off.";
        ShowClock();
    }

    private void SetPlayerTarget(CharacterDefinition target)
    {
        if (IsDead(target)) return;
        _playerTarget = target;
        _playerTargetThing = null;
        StatusText.Text = $"Attacking {target.Name}.";
    }

    /// <summary>In combat mode, the player builds up Dexterity points every standard delay and strikes the target whenever it's in reach.</summary>
    private void UpdatePlayerAttack(double elapsedMs)
    {
        if (_playerPoseResetMs >= 0 && (_playerPoseResetMs -= elapsedMs) < 0) ApplyPoseSprite(_idlePose);
        if (!_combatMode || _player is null || (_playerTarget is null && _playerTargetThing is null)) return;
        if (_playerTarget is not null && IsDead(_playerTarget)) { _playerTarget = null; return; }
        if (_playerTargetThing is not null && !ThingStillThere(_playerTargetThing)) { _playerTargetThing = null; return; }
        if (IsIncapacitated(_player)) return;
        _playerStrikeMs -= elapsedMs;
        if (_playerStrikeMs > 0) return;
        _playerStrikeMs = StdDelay;
        if (_playerDexPoints < DexToAttack)
        {
            _playerDexPoints += Math.Max(1, Stat(_player, BuiltInStatKind.Dexterity, 10));
            return;
        }
        if (_playerTargetThing is { } thing)
        {
            // A thing: a chest, a locked door, a book... (GameTestWindow.ObjectCombat.cs)
            if (!ThingInReach(thing) || VisualOf(thing) is not FrameworkElement thingVisual) return;
            _playerDexPoints -= DexToAttack;
            _playerFacing = FacingTowards(_playerPixelX, _playerPixelY, Canvas.GetLeft(thingVisual) + thingVisual.Width / 2, Canvas.GetTop(thingVisual) + thingVisual.Height);
            ApplyPoseSprite(PoseByName("Attacking Melee") ?? _idlePose);
            _playerPoseResetMs = 2 * StdDelay;
            StrikeThing(thing);
            return;
        }
        if (_playerTarget is null || !InReach(_player, _playerTarget)) return;
        _playerDexPoints -= DexToAttack;
        var (tx, ty) = PixelOf(_playerTarget);
        _playerFacing = FacingTowards(_playerPixelX, _playerPixelY, tx, ty);
        ApplyPoseSprite(PoseByName("Attacking Melee") ?? _idlePose);
        _playerPoseResetMs = 2 * StdDelay;
        Strike(_player, _playerTarget);
    }

    // ---------- A blow (attack_target -> try_to_hit -> figure_hit_points -> apply_damage) ----------

    private void Strike(CharacterDefinition attacker, CharacterDefinition defender)
    {
        if (IsDead(defender)) return;
        int attval = Stat(attacker, BuiltInStatKind.Combat, 10);
        int defval = Stat(defender, BuiltInStatKind.Combat, 10);
        bool hit = RollToWin(attval, defval);
        FightBack(defender, attacker);
        if (!hit)
        {
            ShowFloatingText(defender, "miss");
            return;
        }
        int damage = ApplyDamage(attacker, defender);
        ShowFloatingText(defender, $"-{damage}");
        if (damage <= 0) return;
        bool willDie = (GetHp(defender) ?? 99) - damage <= 0;
        if (willDie) ReportFactionIncident(defender, attacker, FactionIncident.Kill);
        Damage(defender, damage, attacker == _player ? "slain by you" : $"slain by {attacker.Name}");
        if (willDie && attacker == _player) StatusText.Text = $"You have slain {defender.Name}.";
        if (!IsDead(defender)) FireCharacterEvent(defender, BookTriggerConditionBased.ByHit); // a blow that landed (a killing blow sets off By Death instead)
    }

    /// <summary>Actor::roll_to_win: a 30-sided die; 0 always loses, 29 always wins, otherwise roll + attacker - defender must reach 14.</summary>
    private static bool RollToWin(int attacker, int defender)
    {
        const int sides = 30;
        int roll = Rng.Next(sides);
        if (roll == 0) return false;
        if (roll == sides - 1) return true;
        return roll + attacker - defender >= sides / 2 - 1;
    }

    /// <summary>Actor::apply_damage with no weapon (1 point) and no armour: 1..Strength/3 plus 1.</summary>
    private int ApplyDamage(CharacterDefinition attacker, CharacterDefinition defender)
    {
        int str = Stat(attacker, BuiltInStatKind.Strength, 10) / 3;
        int damage = 0;
        if (str > 0) damage += 1 + Rng.Next(str);
        damage += 1; // wpoints = 1: "give at least one, but only if there's no weapon"
        return damage;
    }

    /// <summary>Actor::fight_back, with the faction's memory of it.</summary>
    private void FightBack(CharacterDefinition defender, CharacterDefinition attacker)
    {
        if (SameSide(defender, attacker) || IsDead(defender)) return; // friendly fire doesn't start a fight
        var defenderOnMap = MapCharacterOf(defender);
        bool firstBlow = defenderOnMap is not null && defenderOnMap.Npc.Oppressor != attacker;
        if (defenderOnMap is not null)
        {
            defenderOnMap.Npc.Oppressor = attacker;
            if (!defenderOnMap.Npc.InCombat || defenderOnMap.Npc.Target is null || defenderOnMap.Npc.Fleeing)
                StartCombat(defenderOnMap, attacker);
        }
        if (firstBlow) ReportFactionIncident(defender, attacker, FactionIncident.Attack);
        // Being a bully? The player attacking someone not in the party brings their friends.
        if (attacker == _player && !defender.IsInParty && firstBlow) AttackPlayer(defender, includeSelf: false, "Help! Help!");
    }

    /// <summary>Game_window::attack_avatar: up to 3 characters within 20 tiles, on the victim's side and able to reach the player, attack.</summary>
    private void AttackPlayer(CharacterDefinition victim, bool includeSelf, string? cry)
    {
        if (_player is null) return;
        int helpers = 0;
        foreach (var mapCharacter in _mapCharacters.OrderBy(mc => TilesBetween(mc.Character, _player)).ToList())
        {
            var who = mapCharacter.Character;
            if ((!includeSelf && who == victim) || who.IsInParty || IsIncapacitated(who) || TilesBetween(who, _player) > 20) continue;
            bool sympathetic = who == victim || SameSide(who, victim) || Attitude(who, victim) == FactionBehavior.Ally;
            if (!sympathetic || !CanSee(who, _player)) continue;
            StartCombat(mapCharacter, _player);
            if (cry is not null && helpers == 0) Say(mapCharacter, cry);
            if (++helpers >= 3) break;
        }
    }

    // ---------- Theft ----------

    /// <summary>Who an item belongs to, from its definition: a character (Belong To) and/or a faction. Null = free to take.</summary>
    private (CharacterDefinition? Owner, int FactionId)? OwnerOf(ReferencedItemCategory category, int itemId)
    {
        PlaceableItemBase? definition = category switch
        {
            ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == itemId),
            ReferencedItemCategory.Container => _containers.Containers.FirstOrDefault(c => c.Id == itemId),
            _ => null
        };
        if (definition is null || _player is null) return null;
        var owner = definition.BelongToId >= 0 ? _characters?.Characters.FirstOrDefault(c => c.Id == definition.BelongToId) : null;
        if (owner == _player || owner?.IsInParty == true) return null; // ours
        if (owner is null && (definition.FactionId < 0 || definition.FactionId == _player.FactionId)) return null;
        return (owner, owner?.FactionId >= 0 ? owner.FactionId : definition.FactionId);
    }

    /// <summary>After any successful drop (drag.cc drop: "Check for theft"): moving something that isn't yours, from the world.</summary>
    private void CheckTheft(DragOrigin origin)
    {
        (ReferencedItemCategory Category, int ItemId)? item = origin switch
        {
            MapItemOrigin m => (m.Item.Category, m.Item.ItemId),
            ContainerOrigin c when c.Container is PlacedItemInstance => (c.Item.Category, c.Item.ItemId),
            _ => null
        };
        if (item is not { } it) return;
        var ownership = OwnerOf(it.Category, it.ItemId);
        // An item inside an owned container (still in the world) is the container owner's.
        if (ownership is null && origin is ContainerOrigin { Container: PlacedItemInstance holder }) ownership = OwnerOf(holder.Category, holder.ItemId);
        if (ownership is { } owned) Theft(owned.Owner, owned.FactionId);
    }

    /// <summary>Game_window::theft.</summary>
    private void Theft(CharacterDefinition? owner, int factionId)
    {
        if (_player is null) return;
        var chunk = ((int)(_playerPixelX / 128), (int)(_playerPixelY / 128)); // Exult chunks: 16 tiles
        if (chunk != _theftChunk) { _theftChunk = chunk; _theftWarnings = 0; }

        bool Sympathetic(CharacterDefinition who) =>
            who == owner || (factionId >= 0 && (who.FactionId == factionId || _factions.BehaviorToward(who.FactionId, factionId) == FactionBehavior.Ally));

        MapCharacter? witness = null, closest = null;
        foreach (var mapCharacter in _mapCharacters.OrderBy(mc => TilesBetween(mc.Character, _player)))
        {
            var who = mapCharacter.Character;
            if (who.IsInParty || IsIncapacitated(who) || !Sympathetic(who) || TilesBetween(who, _player) > 12 || !CanSee(who, _player)) continue;
            var (wx, wy) = PixelOf(who);
            if (FacingTowards(wx, wy, _playerPixelX, _playerPixelY) == mapCharacter.Facing || mapCharacter.Npc.Hounding) { witness = mapCharacter; break; }
            closest ??= mapCharacter;
        }
        if (witness is null)
        {
            if (closest is not null && Rng.Next(2) == 0) Say(closest, "Did you hear something?");
            return; // didn't get caught
        }

        _factions.ApplyIncident(factionId, _player.FactionId, FactionIncident.Theft);
        var (x, y) = PixelOf(witness.Character);
        SetCharacterPose(witness, "Idle", FacingTowards(x, y, _playerPixelX, _playerPixelY));
        if (!witness.Npc.InCombat) StartHounding(witness);
        _theftWarnings++;
        if (_theftWarnings < 2 + Rng.Next(3))
        {
            Say(witness, _theftWarnings == 1 ? "Hey! That's not thine!" : "Put that back, thief!");
            return;
        }
        Say(witness, "Thief! Thief!");
        AttackPlayer(witness.Character, includeSelf: true, null);
    }

    // ---------- Hound (follow the player, watching) ----------

    private void StartHounding(MapCharacter mapCharacter)
    {
        StopWalking(mapCharacter);
        mapCharacter.Npc.WalkingToSchedule = false;
        mapCharacter.Npc.Hounding = true;
        mapCharacter.Npc.WaitMs = StdDelay;
    }

    /// <summary>Hound_schedule::now_what: keep about 3 tiles from the player, facing them; give up past 20.</summary>
    private void HoundNowWhat(MapCharacter mapCharacter)
    {
        if (_player is null) return;
        var me = mapCharacter.Character;
        double distance = TilesBetween(me, _player);
        var (x, y) = PixelOf(me);
        if (distance < 3 || InReach(me, _player))
        {
            SetCharacterPose(mapCharacter, "Idle", FacingTowards(x, y, _playerPixelX, _playerPixelY));
            mapCharacter.Npc.WaitMs = StdDelay + 500 + Rng.Next(1000);
            return;
        }
        if (distance > 20) { mapCharacter.Npc.WaitMs = StdDelay + 500 + Rng.Next(1000); return; }
        if (FindPath(mapCharacter, (_playerX, _playerY)) is { Count: > 0 } path) StartPath(mapCharacter, path.Take(3).ToList());
        else mapCharacter.Npc.WaitMs = 2000 + Rng.Next(3000);
    }

    // ---------- Speech and hit numbers (Exult's text effects) ----------

    private void Say(MapCharacter mapCharacter, string text) => ShowFloatingText(mapCharacter.Character, text, 3000);

    private void ShowFloatingText(CharacterDefinition over, string text, int milliseconds = 1000)
    {
        var (x, y) = PixelOf(over);
        if (double.IsNaN(x)) return;
        var label = GameTextLabel(text);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double height = MapCharacterOf(over)?.Visual?.Height ?? _playerImage.Height;
        Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, y + _tileHeight / 2.0 - height - label.DesiredSize.Height - 2 - LiftOf(over) * LiftPixelShift);
        Panel.SetZIndex(label, 1_000_000);
        GameCanvas.Children.Add(label);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); GameCanvas.Children.Remove(label); };
        timer.Start();
    }

    // ---------- Attack command ----------

    private bool AttackCommand(string attackerKey, string targetKey)
    {
        CharacterDefinition? attacker = CharacterDefinitionFor(attackerKey);
        CharacterDefinition? target = CharacterDefinitionFor(targetKey);
        if (attacker is null || target is null || attacker == target || IsDead(attacker) || IsDead(target)) return false;
        if (attacker == _player)
        {
            if (!_combatMode) ToggleCombatMode();
            SetPlayerTarget(target);
            ShowClock();
            return true;
        }
        if (MapCharacterOf(attacker) is not { } mapCharacter) return false;
        StartCombat(mapCharacter, target);
        return true;
    }
}
