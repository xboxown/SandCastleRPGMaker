using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// What a spell does once cast, after Exult's spells (usecode spells/*.uc):
///  - Delivery: straight onto its target, or as a missile (a Projectile
///    object) flying from the caster - Fire Blast, Lightning, Death Bolt -
///    that does its work where it lands.
///  - Reach: the one target, or everyone within so many tiles of it (or of
///    the caster, cast on no one) - Mass Cure, Explosion, Mass Death -
///    friends only, foes only, or everyone.
///  - Its animation over each one it touches.
///  - Its effects: on characters (heal, harm, ailments, charm, fear, dance,
///    clone), on the thing clicked (magic unlock and lock, telekinesis,
///    damage) and on the spot (teleport, Mark and Recall, summon, create
///    food or gold, fields of fire or poison, douse and ignite, reveal and
///    destroy traps, tremor, time stop, locate).
/// A harmful spell is an attack (the victim fights back); a kill by a
/// spell gives the caster the victim's experience, as a blow does.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Mark: where each caster marked (Recall goes back there).</summary>
    private readonly Dictionary<CharacterDefinition, (int X, int Y, int Lift)> _marks = new(ReferenceEqualityComparer.Instance);
    /// <summary>Summoned and cloned creatures, and how long they stay (ms).</summary>
    private readonly Dictionary<CharacterDefinition, double> _summonedMs = new(ReferenceEqualityComparer.Instance);
    /// <summary>Magic fields (fire, poison, sleep, energy) and how long they last (ms).</summary>
    private readonly Dictionary<ObjectInstance, double> _fieldMs = new(ReferenceEqualityComparer.Instance);
    /// <summary>Frightened characters, and how long their fear lasts (ms).</summary>
    private readonly Dictionary<CharacterDefinition, double> _fearMs = new(ReferenceEqualityComparer.Instance);
    /// <summary>Time Stop: everyone but the caster and the party stands frozen this long (ms).</summary>
    private double _timeStopMs;

    private static bool IsSpotEffect(MagicEffectKind kind) => kind is MagicEffectKind.Teleport or MagicEffectKind.Mark or MagicEffectKind.Recall
        or MagicEffectKind.Summon or MagicEffectKind.CreateItem or MagicEffectKind.CreateField or MagicEffectKind.DouseLights or MagicEffectKind.IgniteLights
        or MagicEffectKind.RevealTraps or MagicEffectKind.DestroyTraps or MagicEffectKind.TimeStop or MagicEffectKind.Tremor or MagicEffectKind.Locate;

    private static bool IsThingEffect(MagicEffectKind kind) => kind is MagicEffectKind.MagicUnlock or MagicEffectKind.MagicLock or MagicEffectKind.Telekinesis;

    private static bool IsHarmful(MagicEffectKind kind) => kind is MagicEffectKind.Damage or MagicEffectKind.Poison or MagicEffectKind.Sleep
        or MagicEffectKind.Paralyze or MagicEffectKind.Curse or MagicEffectKind.Charm or MagicEffectKind.Fear or MagicEffectKind.Dance;

    /// <summary>On the same side: the party together, the same faction, or allied factions.</summary>
    private bool IsFriendOf(CharacterDefinition a, CharacterDefinition b) =>
        a == b || (IsPartyOrPlayer(a) && IsPartyOrPlayer(b))
        || (a.FactionId >= 0 && (a.FactionId == b.FactionId || _factions.BehaviorToward(a.FactionId, b.FactionId) == FactionBehavior.Ally));

    private bool SpellTouches(SpellDefinition spell, CharacterDefinition caster, CharacterDefinition who) => spell.Affects switch
    {
        SpellAffects.Foes => !IsFriendOf(who, caster),
        SpellAffects.Friends => IsFriendOf(who, caster),
        _ => true
    };

    private (int X, int Y) CellAtPixel(double x, double y) =>
        (Math.Clamp((int)Math.Floor(x / _tileWidth), 0, _map.Width - 1), Math.Clamp((int)Math.Floor(y / _tileHeight), 0, _map.Height - 1));

    // ================= On its way =================

    /// <summary>The spell goes to its target: as a missile flying from the caster, or straight there.</summary>
    private void DeliverSpell(CharacterDefinition caster, SpellDefinition spell, CharacterDefinition? who, object? thing, (int X, int Y) cell)
    {
        bool atSomeoneElse = who != caster || thing is not null || cell != CellOf(caster);
        if (spell.NeedsTarget && atSomeoneElse && ObjectDef(spell.MissileObjectId) is { Type: ObjectType.Projectile } missile && !double.IsNaN(PixelOf(caster).X))
        {
            var (sx, sy) = PixelOf(caster);
            var (tx, ty) = who is not null && !double.IsNaN(PixelOf(who).X) ? PixelOf(who) : (CellCenterX(cell.X), CellCenterY(cell.Y));
            double distance = Math.Sqrt((tx - sx) * (tx - sx) + (ty - sy) * (ty - sy));
            int tiles = (int)Math.Ceiling(distance / SpriteSizing.BaseUnit) + 1;
            var aimedAt = cell;
            LaunchToward(missile, sx, sy, tx - sx, ty - sy, LiftOf(caster) + 2, caster, tiles, null);
            _flying[^1].OnArrive = (victim, x, y) =>
            {
                var landed = CellAtPixel(x, y);
                bool reached = Math.Max(Math.Abs(landed.X - aimedAt.X), Math.Abs(landed.Y - aimedAt.Y)) <= 1;
                SpellArrives(caster, spell, victim, victim is null && reached ? thing : null, victim is not null ? CellOf(victim) : landed);
            };
            return;
        }
        SpellArrives(caster, spell, who, thing, cell);
    }

    // ================= Where it lands =================

    private void SpellArrives(CharacterDefinition caster, SpellDefinition spell, CharacterDefinition? who, object? thing, (int X, int Y) cell)
    {
        // Who it touches: the one, or everyone within its reach.
        var touched = new List<CharacterDefinition>();
        if (spell.RadiusTiles > 0)
        {
            var (cx, cy) = who is not null && !double.IsNaN(PixelOf(who).X) ? PixelOf(who) : (CellCenterX(cell.X), CellCenterY(cell.Y));
            int floor = MapFloors.FloorOfLift(who is not null ? LiftOf(who) : LiftOf(caster));
            foreach (var other in AllLivingCharacters().ToList())
            {
                var (px, py) = PixelOf(other);
                if (double.IsNaN(px) || MapFloors.FloorOfLift(LiftOf(other)) != floor) continue;
                if (Math.Max(Math.Abs(px - cx), Math.Abs(py - cy)) / SpriteSizing.BaseUnit > spell.RadiusTiles) continue;
                if (SpellTouches(spell, caster, other)) touched.Add(other);
            }
        }
        else if (who is not null && !IsDead(who)) touched.Add(who);

        var onCharacters = spell.Effects.Where(e => !IsSpotEffect(e.Kind) && !IsThingEffect(e.Kind)).ToList();
        foreach (var other in touched)
        {
            PlaySpellAnimation(spell.Animation, PixelOf(other), LiftOf(other));
            ApplyEffects(onCharacters, other, spell.Name, caster);
            if (other != caster && !IsFriendOf(other, caster) && onCharacters.Any(e => IsHarmful(e.Kind)) && !IsDead(other)) FightBack(other, caster); // a harmful spell is an attack
        }
        if (touched.Count == 0) PlaySpellAnimation(spell.Animation, (CellCenterX(cell.X), CellCenterY(cell.Y)), LiftOf(caster));

        // Resurrect: the dead lie as bodies - the body clicked, or every body within its reach.
        if (spell.Effects.Any(e => e.Kind == MagicEffectKind.Resurrect))
            foreach (var dead in _bodies.Where(b => ReferenceEquals(b.Value.Body, thing) || (spell.RadiusTiles > 0 && Math.Max(Math.Abs(b.Value.Body.X - cell.X), Math.Abs(b.Value.Body.Y - cell.Y)) <= CellsForTiles(spell.RadiusTiles)))
                         .Select(b => b.Key).ToList())
                if (dead != _player) ResurrectDead(dead);

        // The thing clicked: magic unlock and lock, telekinesis, damage.
        if (thing is not null)
            foreach (var effect in spell.Effects.Where(e => IsThingEffect(e.Kind))) ApplyThingSpell(effect, caster, thing);
        if (thing is ObjectInstance struck && DefinitionOf(struck) is { } struckObj) ApplyEffectsToObject(spell.Effects, struck, struckObj);

        // The spot: once for the spell.
        foreach (var effect in spell.Effects.Where(e => IsSpotEffect(e.Kind))) ApplySpotEffect(effect, caster, cell, spell.RadiusTiles, who);

        if (string.IsNullOrEmpty(StatusText.Text) || touched.Count > 0)
            StatusText.Text = touched.Count switch
            {
                0 => $"{(caster == _player ? "You cast" : $"{caster.Name} casts")} {spell.Name}.",
                1 => $"{spell.Name} on {touched[0].Name}.",
                _ => $"{spell.Name} touches {touched.Count}."
            };
    }

    /// <summary>The spell's animation played once over someone (or at a spot).</summary>
    private void PlaySpellAnimation(SpriteLink link, (double X, double Y) feet, int lift)
    {
        if (!link.IsSet || double.IsNaN(feet.X) || ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName) is not { Frames.Count: > 0 } sprite) return;
        var (width, height) = QuantizedSpriteSize(sprite);
        var image = new System.Windows.Controls.Image { Width = width, Height = height, IsHitTestVisible = false };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
        System.Windows.Controls.Panel.SetZIndex(image, AboveEverythingZIndex - 1);
        GameCanvas.Children.Add(image);
        PlaceEffect(image, feet, lift);
        RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources));
        double left = sprite.Frames.Sum(f => Math.Max(1, f.DurationMs));
        _scriptEffects.Add(new ScriptEffect { Image = image, Over = ms => (left -= ms) <= 0 });
    }

    // ================= On characters =================

    /// <summary>The spell effects that only a character can have (ApplyEffects calls it).</summary>
    private void ApplyCharacterSpell(MagicEffect effect, CharacterDefinition target, CharacterDefinition? by)
    {
        var mapCharacter = MapCharacterOf(target);
        double seconds = Math.Max(1, effect.Seconds);
        switch (effect.Kind)
        {
            case MagicEffectKind.Charm:
                Inflict(target, Ailment.Charmed); // Exult's charm: fights for the other side a while
                break;
            case MagicEffectKind.Fear:
                if (mapCharacter is null || by is null) break;
                mapCharacter.Npc.InCombat = true;
                mapCharacter.Npc.Fleeing = true;
                mapCharacter.Npc.FleeFrom = by;
                _fearMs[target] = seconds * 1000;
                RunAway(mapCharacter);
                if (target.IsHuman) ShowFloatingText(target, "Aaagh!", 2000);
                break;
            case MagicEffectKind.Dance:
                if (mapCharacter is null) break;
                ChangeBehaviorCommand(target.Id.ToString(), nameof(ScheduleType.Dance));
                _scheduleUntil[mapCharacter] = TotalMinutes() + Math.Max(1, (long)Math.Round(seconds / 5)); // a game minute is 5 seconds
                break;
            case MagicEffectKind.RevealInvisible:
                Cure(target, Ailment.Invisible);
                break;
            case MagicEffectKind.Clone:
                Summon(target, by ?? target, CellOf(target), 1, seconds);
                break;
        }
    }

    // ================= On the thing clicked =================

    private void ApplyThingSpell(MagicEffect effect, CharacterDefinition caster, object thing)
    {
        switch (effect.Kind)
        {
            case MagicEffectKind.MagicUnlock:
                if (thing is ObjectInstance door && DefinitionOf(door) is { Type: ObjectType.Door } doorObj && door.IsLockedRuntime == true) UnlockDoor(door, doorObj, "unlockmagic");
                else if (thing is PlacedItemInstance { Category: ReferencedItemCategory.Container } chest && ContainerDef(chest.ItemId) is { } data && IsLockedNow(chest, data))
                {
                    chest.IsLockedRuntime = false;
                    PlaySound(data.UnlockSoundResourceId);
                    RecordItem("unlockmagic", ReferencedItemCategory.Container, chest.ItemId);
                    StatusText.Text = $"\"{data.Name}\" is unlocked.";
                }
                break;
            case MagicEffectKind.MagicLock:
                if (thing is ObjectInstance toLock && DefinitionOf(toLock) is { Type: ObjectType.Door } lockObj && toLock.IsOpenRuntime != true)
                {
                    toLock.IsLockedRuntime = true;
                    PlaySound(lockObj.Door.LockSoundResourceId);
                    RefreshObjectLook(toLock);
                    StatusText.Text = $"\"{lockObj.Name}\" is locked.";
                }
                else if (thing is PlacedItemInstance { Category: ReferencedItemCategory.Container } box && ContainerDef(box.ItemId) is { } boxData)
                {
                    box.IsLockedRuntime = true;
                    PlaySound(boxData.LockSoundResourceId);
                    StatusText.Text = $"\"{boxData.Name}\" is locked.";
                }
                break;
            case MagicEffectKind.Telekinesis:
                if (thing is ObjectInstance far && _map.Objects.Contains(far)) HandleObjectClick(far, isUse: true); // used from where the caster stands
                break;
        }
    }

    // ================= On the spot =================

    private void ApplySpotEffect(MagicEffect effect, CharacterDefinition caster, (int X, int Y) cell, int radiusTiles, CharacterDefinition? who)
    {
        int reach = CellsForTiles(Math.Max(1, radiusTiles));
        bool Near(int x, int y) => Math.Max(Math.Abs(x - cell.X), Math.Abs(y - cell.Y)) <= (radiusTiles > 0 ? reach : 0);
        double seconds = Math.Max(1, effect.Seconds);
        int amount = RollEffect(effect);
        switch (effect.Kind)
        {
            case MagicEffectKind.Teleport:
                MoveCasterTo(caster, cell, null);
                break;
            case MagicEffectKind.Mark:
                _marks[caster] = (CellOf(caster).X, CellOf(caster).Y, LiftOf(caster));
                StatusText.Text = "This place is marked.";
                break;
            case MagicEffectKind.Recall:
                if (_marks.TryGetValue(caster, out var mark)) MoveCasterTo(caster, (mark.X, mark.Y), mark.Lift);
                else StatusText.Text = "Nothing has been marked.";
                break;
            case MagicEffectKind.Summon:
                if (_characters?.Characters.FirstOrDefault(c => c.Id == effect.ThingId) is { } kind) Summon(kind, caster, cell, Math.Max(1, amount), seconds);
                break;
            case MagicEffectKind.CreateItem:
                if (ObjectDef(effect.ThingId) is not null)
                {
                    foreach (var gift in MakeGifts(ReferencedItemCategory.Object, effect.ThingId, Math.Max(1, amount)))
                        if (!HandOver(gift, caster)) PutDownAt(gift, caster);
                    RefreshAfterMove();
                }
                break;
            case MagicEffectKind.CreateField:
                if (ObjectDef(effect.ThingId) is null) break;
                for (int y = cell.Y - (radiusTiles > 0 ? reach : 0); y <= cell.Y + (radiusTiles > 0 ? reach : 0); y++)
                    for (int x = cell.X - (radiusTiles > 0 ? reach : 0); x <= cell.X + (radiusTiles > 0 ? reach : 0); x++)
                        if (InsideMap(x, y) && SpawnObjectAt(effect.ThingId, 1, x, y) is { } field) _fieldMs[field] = seconds * 1000;
                break;
            case MagicEffectKind.DouseLights:
            case MagicEffectKind.IgniteLights:
            {
                bool lit = effect.Kind == MagicEffectKind.IgniteLights;
                foreach (var light in _map.Objects.Where(o => o.ContainerInstanceId < 0 && Math.Max(Math.Abs(o.X - cell.X), Math.Abs(o.Y - cell.Y)) <= reach).ToList())
                {
                    if (DefinitionOf(light) is not { Type: ObjectType.LightSource } lightObj || light.IsSpentRuntime || (light.IsLitRuntime == true) == lit) continue;
                    light.IsLitRuntime = lit;
                    if (lit && lightObj.Light.BurnMinutes > 0 && light.BurnLeftRuntime < 0) light.BurnLeftRuntime = lightObj.Light.BurnMinutes;
                    RefreshObjectLook(light);
                }
                break;
            }
            case MagicEffectKind.RevealTraps:
            case MagicEffectKind.DestroyTraps:
                foreach (var trap in _map.Objects.Where(o => o.ContainerInstanceId < 0 && Math.Max(Math.Abs(o.X - cell.X), Math.Abs(o.Y - cell.Y)) <= Math.Max(reach, CellsForTiles(8))).ToList())
                {
                    if (DefinitionOf(trap) is not { Type: ObjectType.Trap }) continue;
                    if (effect.Kind == MagicEffectKind.DestroyTraps) RemoveMapObject(trap);
                    else if (VisualOf(trap) is { } visual) visual.Visibility = System.Windows.Visibility.Visible;
                }
                break;
            case MagicEffectKind.TimeStop:
                _timeStopMs = Math.Max(_timeStopMs, seconds * 1000);
                StatusText.Text = "Time stands still.";
                break;
            case MagicEffectKind.Tremor:
                PlayRealShakeEffect(Math.Clamp(amount, 2, 20), 2);
                break;
            case MagicEffectKind.Locate:
                StatusText.Text = $"{caster.Name} stands at {CellOf(caster).X}, {CellOf(caster).Y}.";
                break;
        }
    }

    /// <summary>Teleport and Recall: the caster (the party, if it's the main character) is there at once.</summary>
    private void MoveCasterTo(CharacterDefinition caster, (int X, int Y) cell, int? lift)
    {
        if (!InsideMap(cell.X, cell.Y)) return;
        if (caster == _player)
        {
            TeleportCommand(WhoChoice.Player, cell.X, cell.Y);
            if (lift is { } l) { _playerZ = StandingLiftOnFloor(PlayerFootprint(), MapFloors.FloorOfLift(l)); _playerVisualLiftZ = _playerZ; SnapPlayerPosition(); }
            foreach (var follower in AllLivingCharacters().Where(c => c.IsInParty && c != _player).ToList())
                if (MapCharacterOf(follower) is { } mc) Teleport(mc, cell); // the party comes along
            return;
        }
        if (MapCharacterOf(caster) is { } mapCharacter) Teleport(mapCharacter, cell);
    }

    /// <summary>Summon and Clone: copies of a character appear round a spot, on the caster's side, for a while.</summary>
    private void Summon(CharacterDefinition kind, CharacterDefinition caster, (int X, int Y) cell, int count, double seconds)
    {
        int floor = MapFloors.FloorOfLift(LiftOf(caster));
        var taken = new HashSet<(int, int)>(_mapCharacters.Select(mc => (mc.Placement.X, mc.Placement.Y))) { CellOf(_player ?? caster) };
        for (int i = 0; i < count; i++)
        {
            (int X, int Y)? spot = null;
            for (int ring = 1; ring <= 3 && spot is null; ring++)
                foreach (var (dx, dy) in Enumerable.Range(-ring, 2 * ring + 1).SelectMany(dx => Enumerable.Range(-ring, 2 * ring + 1).Select(dy => (dx, dy))))
                {
                    var c = (cell.X + dx, cell.Y + dy);
                    if (InsideMap(c.Item1, c.Item2) && !taken.Contains(c)) { spot = c; break; }
                }
            if (spot is not { } at) break;
            taken.Add(at);
            var copy = CopyOfCharacter(kind);
            copy.FactionId = caster.FactionId;
            copy.IsInParty = false;
            copy.Schedules.Clear();
            copy.Name = kind.Name;
            var placement = new PlacedCharacter { Id = _map.Characters.Count == 0 ? 1 : _map.Characters.Max(p => p.Id) + 1 + i, CharacterId = copy.Id, X = at.X, Y = at.Y, Floor = floor };
            BringOntoMap(copy, placement);
            HealFully(copy);
            _summonedMs[copy] = seconds * 1000;
        }
    }

    // ================= As time goes by =================

    /// <summary>Each frame: summoned creatures leave, fields fade, fear wears off, time starts again.</summary>
    private void UpdateSpellTimers(double elapsedMs)
    {
        if (_timeStopMs > 0) _timeStopMs = Math.Max(0, _timeStopMs - elapsedMs);
        foreach (var summoned in _summonedMs.Keys.ToList())
        {
            if ((_summonedMs[summoned] -= elapsedMs) > 0 && !IsDead(summoned)) continue;
            _summonedMs.Remove(summoned);
            if (MapCharacterOf(summoned) is not { } gone) continue;
            StopWalking(gone);
            if (gone.Visual is { } visual) { UnregisterWorldAnimator(visual); GameCanvas.Children.Remove(visual); }
            _mapCharacters.Remove(gone);
            _followers.Remove(gone);
        }
        foreach (var field in _fieldMs.Keys.ToList())
        {
            if ((_fieldMs[field] -= elapsedMs) > 0) continue;
            _fieldMs.Remove(field);
            if (_map.Objects.Contains(field)) RemoveMapObject(field);
        }
        foreach (var afraid in _fearMs.Keys.ToList())
        {
            if ((_fearMs[afraid] -= elapsedMs) > 0) continue;
            _fearMs.Remove(afraid);
            if (MapCharacterOf(afraid) is { } calm) { calm.Npc.Fleeing = false; calm.Npc.FleeFrom = null; }
        }
    }

    /// <summary>Time Stop: this character stands frozen (not the party, not a summoned friend of it).</summary>
    private bool FrozenInTime(MapCharacter mapCharacter) => _timeStopMs > 0 && !IsPartyOrPlayer(mapCharacter.Character);
}
