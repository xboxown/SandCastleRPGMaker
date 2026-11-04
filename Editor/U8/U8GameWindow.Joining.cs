// Ultima 8 engine (U8GameWindow) - the same as the Ultima 7 engine's GameTestWindow.Joining.cs.
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Monsters that touch (Character Editor > Health > Joining; any kind of monster, not only slimes).
///  - Joined look, Exult's Slime_actor::update_frames: a frame by which sides have a neighbour of its own kind two tiles away, bit 0 a
///    random wobble, then 2 north, 4 east, 8 south, 16 west. Each stays its own character, hit and killed on its own.
///  - Rules: touching a kind, it may multiply (another appears, as slimes) or fuse (both go, a new monster stands there).
/// </summary>
public partial class U8GameWindow
{
    private double _joinCheckMs;
    private readonly Dictionary<(CharacterDefinition, CharacterDefinition), double> _joinCooldownMs = new();
    private readonly Dictionary<MapCharacter, int> _joinedFrame = new(ReferenceEqualityComparer.Instance);

    private SpriteEntry? JoinedSprite(CharacterDefinition character) =>
        character.Joining.JoinedSprite.SpriteName.Length == 0 ? null : ObjectLooks.Find(_sprites, character.Joining.JoinedSprite.SpriteGroup, character.Joining.JoinedSprite.SpriteName);

    private void UpdateJoining(double elapsedMs)
    {
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
        double reach = 2 * SpriteSizing.BaseUnit, slack = SpriteSizing.BaseUnit;
        foreach (var other in _mapCharacters)
        {
            if (ReferenceEquals(other, me) || other.Character.Id != me.Character.Id || IsDead(other.Character) || MapFloors.FloorOfLift(other.Lift) != MapFloors.FloorOfLift(me.Lift)) continue;
            var (dx, dy) = WrappedDelta(other.PixelX - me.PixelX, other.PixelY - me.PixelY);
            if (Math.Abs(dx) < slack && Math.Abs(dy + reach) < slack) mask |= 2;
            else if (Math.Abs(dy) < slack && Math.Abs(dx - reach) < slack) mask |= 4;
            else if (Math.Abs(dx) < slack && Math.Abs(dy - reach) < slack) mask |= 8;
            else if (Math.Abs(dy) < slack && Math.Abs(dx + reach) < slack) mask |= 16;
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
        if (_characters?.Characters.FirstOrDefault(c => c.Id == rule.ResultCharacterId) is not { } result || me.Character.IsInParty || !_mapCharacters.Contains(me)) return;
        int partnerId = rule.PartnerCharacterId < 0 ? me.Character.Id : rule.PartnerCharacterId;
        double reach = Math.Max(1, rule.ReachTiles) * SpriteSizing.BaseUnit;
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
                var fused = AppearByJoining(result, cell, floor);
                foreach (var item in carried) { item.InstanceId = fused.GetNextInventoryInstanceId(); fused.Inventory.Add(item); }
                StatusText.Text = $"{me.Character.Name} and {other.Character.Name} become {result.Name}!";
                return;
            }
            AppearByJoining(result, cell, floor);
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

    /// <summary>A new monster of this kind on the nearest free cell, its own character from now on (in a save too).</summary>
    private CharacterDefinition AppearByJoining(CharacterDefinition kind, (int X, int Y) cell, int floor)
    {
        var taken = new HashSet<(int, int)>(_mapCharacters.Select(mc => (mc.Placement.X, mc.Placement.Y)));
        if (_player is not null) taken.Add(CellOf(_player));
        var spot = cell;
        for (int ring = 0; ring <= 3; ring++)
        {
            var free = Enumerable.Range(-ring, 2 * ring + 1).SelectMany(dx => Enumerable.Range(-ring, 2 * ring + 1).Select(dy => (cell.X + dx, cell.Y + dy))).Where(c => InsideMap(c.Item1, c.Item2) && !taken.Contains(c)).ToList();
            if (free.Count > 0) { spot = free[Rng.Next(free.Count)]; break; }
        }
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
        return copy;
    }
}
