using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Monsters that touch (Character Editor > Health > Joining; any kind of monster, not only slimes).
///  - Joined look, Exult's Slime_actor::update_frames: a frame by which sides have a neighbour of its own kind two tiles away, bit 0 a
///    random wobble, then 2 north, 4 east, 8 south, 16 west. Each stays its own character, hit and killed on its own.
///  - Rules: near a kind, it may multiply (another appears) or fuse (both go, a new monster stands there).
///  - Splitting, Exult's Actor::reduce_health and clone: a hit it survives, of a kind it isn't vulnerable to, makes another beside it, 1 chance in 2,
///    with no limit. Unlike Exult's, the new one isn't temporary: it stays, in saves too.
///  - Trail, Exult's Slime_actor::step: 1 step in 6 leaves a puddle where it stood. Puddles go as blood does; whether they hurt is the maker's choice.
/// </summary>
public partial class GameTestWindow
{
    private double _joinCheckMs;
    private readonly Dictionary<(CharacterDefinition, CharacterDefinition), double> _joinCooldownMs = new();
    private readonly Dictionary<MapCharacter, int> _joinedFrame = new(ReferenceEqualityComparer.Instance);
    private readonly List<MapCharacter> _pendingSplits = new();
    private DamageType _hitType = DamageType.Normal;
    private double _trailCheckMs;
    private readonly Dictionary<MapCharacter, (int X, int Y, int Lift)> _trailLastSpot = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CharacterDefinition, double> _trailHurtWaitMs = new(ReferenceEqualityComparer.Instance);

    /// <summary>How far apart neighbours stand: Exult's two tiles, or the next square on a map with bigger squares.</summary>
    private double JoinSpacingX => Math.Max(2.0 * SpriteSizing.BaseUnit, _tileWidth);
    private double JoinSpacingY => Math.Max(2.0 * SpriteSizing.BaseUnit, _tileHeight);

    private SpriteEntry? JoinedSprite(CharacterDefinition character) =>
        character.Joining.JoinedSprite.SpriteName.Length == 0 ? null : ObjectLooks.Find(_sprites, character.Joining.JoinedSprite.SpriteGroup, character.Joining.JoinedSprite.SpriteName);

    private void UpdateJoining(double elapsedMs)
    {
        DoPendingSplits();
        UpdateTrails(elapsedMs);
        if (_joinCooldownMs.Count > 0)
            foreach (var key in _joinCooldownMs.Keys.ToList())
                if ((_joinCooldownMs[key] -= elapsedMs) <= 0) _joinCooldownMs.Remove(key);
        if ((_joinCheckMs += elapsedMs) < 200) return;
        _joinCheckMs = 0;
        var joiners = _mapCharacters.Where(mc => mc.Character.Joining.IsSet && !IsDead(mc.Character)).ToList();
        if (joiners.Count == 0) return;
        foreach (var mapCharacter in joiners)
            if (JoinedSprite(mapCharacter.Character) is { Frames.Count: > 0 } sprite) DrawJoinedLook(mapCharacter, sprite);
        foreach (var mapCharacter in joiners)
            foreach (var rule in mapCharacter.Character.Joining.Rules)
                TryJoin(mapCharacter, rule);
    }

    /// <summary>update_frames: bit 0 wobbles as it moves on; a neighbour two tiles to the north sets 2, east 4, south 8, west 16.</summary>
    private void DrawJoinedLook(MapCharacter me, SpriteEntry sprite)
    {
        if (me.Visual is not Image image) return;
        int mask = 0;
        double reachX = JoinSpacingX, reachY = JoinSpacingY, slackX = reachX / 2, slackY = reachY / 2;
        foreach (var other in _mapCharacters)
        {
            if (ReferenceEquals(other, me) || other.Character.Id != me.Character.Id || IsDead(other.Character) || MapFloors.FloorOfLift(other.Lift) != MapFloors.FloorOfLift(me.Lift)) continue;
            var (dx, dy) = WrappedDelta(other.PixelX - me.PixelX, other.PixelY - me.PixelY);
            if (Math.Abs(dx) < slackX && Math.Abs(dy + reachY) < slackY) mask |= 2;
            else if (Math.Abs(dy) < slackY && Math.Abs(dx - reachX) < slackX) mask |= 4;
            else if (Math.Abs(dx) < slackX && Math.Abs(dy - reachY) < slackY) mask |= 8;
            else if (Math.Abs(dy) < slackY && Math.Abs(dx + reachX) < slackX) mask |= 16;
        }
        bool moving = me.Npc.Path is not null;
        int wobble = _joinedFrame.TryGetValue(me, out int last) && (last & ~1) == mask && !moving ? last & 1 : Rng.Next(2);
        int frame = mask | wobble;
        if (_joinedFrame.TryGetValue(me, out last) && last == frame) return;
        bool first = !_joinedFrame.ContainsKey(me);
        _joinedFrame[me] = frame;
        int shown = sprite.Frames.Count >= 32 ? frame : sprite.Frames.Count >= 16 ? mask >> 1 : Math.Min(sprite.Frames.Count - 1, wobble); // 32 frames as Exult; 16 without the wobble
        if (first)
        {
            UnregisterWorldAnimator(image);
            var (width, height) = QuantizedSpriteSize(sprite);
            image.Width = width;
            image.Height = height;
            PositionCharacterVisual(me);
        }
        image.Source = LoadStaticFrame(sprite.Frames[shown].FilePath);
    }

    private void TryJoin(MapCharacter me, JoinRule rule)
    {
        if (!rule.Fuses && me.Character.Joining.Growth == JoinGrowth.Ultima7) return; // Ultima 7's only grow by being hit
        if (_characters?.Characters.FirstOrDefault(c => c.Id == rule.ResultCharacterId) is not { } result || me.Character.IsInParty || !_mapCharacters.Contains(me)) return;
        int partnerId = rule.PartnerCharacterId < 0 ? me.Character.Id : rule.PartnerCharacterId;
        double reach = Math.Max(Math.Max(1, rule.ReachTiles) * SpriteSizing.BaseUnit, Math.Max(_tileWidth, _tileHeight)); // the next square always counts
        foreach (var other in _mapCharacters.ToList())
        {
            if (ReferenceEquals(other, me) || other.Character.Id != partnerId || other.Character.IsInParty || IsDead(other.Character) || MapFloors.FloorOfLift(other.Lift) != MapFloors.FloorOfLift(me.Lift)) continue;
            var (dx, dy) = WrappedDelta(other.PixelX - me.PixelX, other.PixelY - me.PixelY);
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > reach) continue;
            var pair = (me.Character, other.Character);
            if (_joinCooldownMs.ContainsKey(pair) || _joinCooldownMs.ContainsKey((other.Character, me.Character))) continue;
            _joinCooldownMs[pair] = Math.Max(1, rule.CooldownSeconds) * 1000;
            if (rule.MaxOnMap > 0 && _mapCharacters.Count(mc => mc.Character.Id == result.Id && !IsDead(mc.Character)) >= rule.MaxOnMap) continue;
            if (Rng.Next(100) >= rule.ChancePercent) continue;
            PlaySoundFrom(rule.SoundResourceId, me.Character);
            var cell = (me.Placement.X, me.Placement.Y);
            int floor = MapFloors.FloorOfLift(me.Lift);
            if (rule.Fuses)
            {
                var carried = me.Character.Inventory.Concat(other.Character.Inventory).ToList();
                GoneByJoining(me);
                GoneByJoining(other);
                var fused = AppearByJoining(result, cell, floor, me, fusing: true)!;
                foreach (var item in carried) { item.InstanceId = fused.GetNextInventoryInstanceId(); fused.Inventory.Add(item); }
                StatusText.Text = $"{me.Character.Name} and {other.Character.Name} become {result.Name}!";
                return;
            }
            AppearByJoining(result, cell, floor, me);
            return;
        }
    }

    /// <summary>Into the fused monster: no body, no death event.</summary>
    private void GoneByJoining(MapCharacter who)
    {
        who.Character.Inventory.Clear();
        who.Character.EquippedSlots.Clear();
        who.Character.CustomEquippedSlots.Clear();
        VitalsOf(who.Character).Dead = true;
        foreach (var other in _mapCharacters.Where(mc => mc.Npc.Target == who.Character)) other.Npc.Target = null;
        if (_playerTarget == who.Character) _playerTarget = null;
        if (who.Visual is { } visual) { UnregisterWorldAnimator(visual); GameCanvas.Children.Remove(visual); }
        _mapCharacters.Remove(who);
        _joinedFrame.Remove(who);
        who.Placement.Dead = true;
        who.Placement.BodyInstanceId = -1;
    }

    /// <summary>Actor::clone: a new monster of this kind on the nearest spot it can really stand (none: no new one), its own character from now on (in a save too). Fusing: the two have just gone, so it can stand where they did.</summary>
    private CharacterDefinition? AppearByJoining(CharacterDefinition kind, (int X, int Y) cell, int floor, MapCharacter beside, bool fusing = false)
    {
        var taken = new HashSet<(int, int)>(_mapCharacters.Select(mc => (mc.Placement.X, mc.Placement.Y)));
        if (_player is not null) taken.Add(CellOf(_player));
        (int, int)? found = null;
        int nearest = fusing ? 0 : Math.Max(1, (int)Math.Ceiling(2.0 * SpriteSizing.BaseUnit / Math.Max(_tileWidth, _tileHeight))); // far enough not to stand on it
        for (int ring = nearest; ring <= nearest + 2 && found is null; ring++)
        {
            var free = Enumerable.Range(-ring, 2 * ring + 1).SelectMany(dx => Enumerable.Range(-ring, 2 * ring + 1).Select(dy => (dx, dy)))
                .Where(d => Math.Max(Math.Abs(d.dx), Math.Abs(d.dy)) == ring).Select(d => (cell.X + d.dx, cell.Y + d.dy))
                .Where(c => InsideMap(c.Item1, c.Item2) && !taken.Contains(c) && CanStandAt(beside, c, MapFloors.BaseLift(floor), null, out int lift) && MapFloors.FloorOfLift(lift) == floor).ToList();
            if (free.Count > 0) found = free[Rng.Next(free.Count)];
        }
        if (found is null && !fusing) return null;
        var spot = found ?? cell;
        var copy = CopyOfCharacter(kind);
        copy.IsInParty = false;
        var placement = new PlacedCharacter
        {
            Id = _map.Characters.Count == 0 ? 1 : _map.Characters.Max(p => p.Id) + 1, CharacterId = copy.Id, X = spot.Item1, Y = spot.Item2, Floor = floor,
            SavedCharacterIndex = _characters!.Characters.IndexOf(copy)
        };
        _map.Characters.Add(placement);
        BringOntoMap(copy, placement);
        HealFully(copy);
        SoakLog($"joining: a new {kind.Name} at ({spot.Item1}, {spot.Item2}), {_mapCharacters.Count(mc => mc.Character.Id == kind.Id)} on the map");
        return copy;
    }

    // ---------- Splitting when hit (Exult's Actor::reduce_health) ----------

    /// <summary>Damage from a hit of this kind (what a slime may be vulnerable to).</summary>
    private void DamageAllowed(CharacterDefinition character, int amount, string cause, DamageType type)
    {
        _hitType = type;
        try { DamageAllowed(character, amount, cause); }
        finally { _hitType = DamageType.Normal; }
    }

    /// <summary>reduce_health: it survived a hit. The new one appears on the next frame: whoever hit it may be going through the map's characters.</summary>
    private void MaybeSplit(CharacterDefinition character)
    {
        var joining = character.Joining;
        if (joining.Growth == JoinGrowth.OurEngine || character == _player || character.IsInParty || joining.NoSplitFrom.Contains(_hitType) || Rng.Next(2) != 0) return;
        if (MapCharacterOf(character) is { } me) _pendingSplits.Add(me);
    }

    private void DoPendingSplits()
    {
        if (_pendingSplits.Count == 0) return;
        foreach (var me in _pendingSplits.ToList())
        {
            if (!_mapCharacters.Contains(me) || IsDead(me.Character)) continue;
            var kind = _characters?.Characters.FirstOrDefault(c => c.Id == me.Character.Id) ?? me.Character;
            AppearByJoining(kind, (me.Placement.X, me.Placement.Y), MapFloors.FloorOfLift(me.Lift), me);
        }
        _pendingSplits.Clear();
    }

    // ---------- The trail (Exult's Slime_actor::step) ----------

    private void UpdateTrails(double elapsedMs)
    {
        if (_trailHurtWaitMs.Count > 0)
            foreach (var key in _trailHurtWaitMs.Keys.ToList())
                if ((_trailHurtWaitMs[key] -= elapsedMs) <= 0) _trailHurtWaitMs.Remove(key);
        if ((_trailCheckMs += elapsedMs) < 100) return;
        _trailCheckMs = 0;
        foreach (var me in _mapCharacters)
        {
            var joining = me.Character.Joining;
            if (joining.TrailSpriteName.Length == 0 || IsDead(me.Character) || _sprites.FindTrail(joining.TrailSpriteName) is not { Frames.Count: > 0 } trail) continue;
            var unit = FeetUnit(me.Character);
            if (_trailLastSpot.TryGetValue(me, out var was))
            {
                int steps = Math.Min(4, Math.Max(Math.Abs(unit.X - was.X), Math.Abs(unit.Y - was.Y)));
                for (int step = 0; step < steps; step++)
                    if (Rng.Next(6) == 0)
                    {
                        DropPuddle(trail, was.X, was.Y, was.Lift, joining.TrailHurts ? Math.Max(1, joining.TrailDamage) : 0, me.Character.Id);
                        break;
                    }
            }
            _trailLastSpot[me] = (unit.X, unit.Y, me.Lift);
        }
        HurtOnTrails();
    }

    /// <summary>Whoever stands on a puddle that hurts, never its own kind: on stepping in, then each second.</summary>
    private void HurtOnTrails()
    {
        if (!_puddles.Any(p => p.Hurts > 0)) return;
        var hit = new List<(CharacterDefinition Who, int Hp)>();
        foreach (var who in AllLivingCharacters())
        {
            if (_trailHurtWaitMs.ContainsKey(who)) continue;
            var (x, y) = PixelOf(who);
            double feetY = y + _tileHeight / 2.0 - 1;
            int lift = LiftOf(who);
            foreach (var puddle in _puddles)
            {
                if (puddle.Hurts <= 0 || puddle.Lift != lift || puddle.MadeByKind == who.Id) continue;
                double centerX = puddle.UnitX * SpriteSizing.BaseUnit + SpriteSizing.BaseUnit / 2.0, centerY = puddle.UnitY * SpriteSizing.BaseUnit + SpriteSizing.BaseUnit / 2.0;
                if (Math.Abs(x - centerX) > Math.Max(SpriteSizing.BaseUnit / 2.0, puddle.Visual.Width / 2) || Math.Abs(feetY - centerY) > Math.Max(SpriteSizing.BaseUnit / 2.0, puddle.Visual.Height / 2)) continue;
                hit.Add((who, puddle.Hurts));
                break;
            }
        }
        foreach (var (who, hp) in hit)
        {
            _trailHurtWaitMs[who] = 1000;
            SoakLog($"trail: {who.Name} hurt {hp}");
            Damage(who, hp, "a slime trail");
        }
    }
}
