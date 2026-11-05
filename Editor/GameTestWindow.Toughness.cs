// Ultima 7 engine (GameTestWindow): the same as the Ultima 8 engine's U8GameWindow.Toughness.cs.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Rules that began as Ultima 8's (Pentagram) and are open to every game:
///  - Damage types (Character Editor > Health): each type can do nothing to a character, half, or be adjusted by a number.
///  - Getting back up (Actor::dieU8, ResurrectionProcess): rises some seconds after dying, unless killed by certain types.
///  - Feign Death (Actor::I_setFeignDeath, ClearFeignDeathProcess): falls, gets up, and those fooled by it leave them be for a time.
///  - Withstand Death (ACT_WITHSTANDDEATH, Actor::receiveHitU8): the next blow that would kill restores full health instead, once.
///  - Changeling (CombatProcess's shifters, AmbushProcess): looks like something else and waits, until an enemy comes near or it is hurt.
///  - Area commands and conditions: what a ritual needs, and open to anything else.
/// </summary>
public partial class GameTestWindow
{
    // ---------- Damage types ----------

    /// <summary>What this character's own rule for a damage type makes of the damage.</summary>
    private static int AfterDamageResponse(CharacterDefinition defender, int damage, DamageType type)
    {
        if (damage <= 0 || defender.DamageResponses.FirstOrDefault(r => r.Type == type) is not { } rule) return damage;
        return rule.Response switch
        {
            DamageResponseKind.Immune => 0,
            DamageResponseKind.Half => (damage + 1) / 2,
            DamageResponseKind.Adjust => Math.Max(0, damage + rule.Amount),
            _ => damage
        };
    }

    // ---------- Getting back up ----------

    private readonly List<CharacterDefinition> _rising = new();

    /// <summary>Just died: its time till it rises, unless what killed it stops it (Pentagram: fire). It needs its body to rise from.</summary>
    private void MaybeGetBackUp(CharacterDefinition character)
    {
        if (!character.GetsBackUp || character == _player || character.Body.NoBody || character.BackUpNotFrom.Contains(_hitType) || !_bodies.ContainsKey(character)) return;
        int min = Math.Max(0, character.BackUpMinSeconds), max = Math.Max(min, character.BackUpMaxSeconds);
        VitalsOf(character).RiseRemainingMs = (min + Rng.Next(max - min + 1)) * 1000.0;
        _rising.Add(character);
    }

    private void UpdateRising(double elapsedMs)
    {
        for (int i = _rising.Count - 1; i >= 0; i--)
        {
            var character = _rising[i];
            var vitals = VitalsOf(character);
            if (!vitals.Dead) { vitals.RiseRemainingMs = 0; _rising.RemoveAt(i); continue; } // raised some other way
            if ((vitals.RiseRemainingMs -= elapsedMs) > 0) continue;
            vitals.RiseRemainingMs = 0;
            _rising.RemoveAt(i);
            if (!_bodies.TryGetValue(character, out var lying)) continue;
            foreach (var gump in _openContainers.Where(g => ReferenceEquals(g.PlacedItem, lying.Item1)).ToList()) CloseContainerGump(gump); // ResurrectionProcess: the player's rummaging ends
            if (!ResurrectDead(character)) continue;
            if (HasArtFor(character, "Stand Up")) BeginScriptAction(character, Chain(PoseOnce(character, "Stand Up")), null, afterEvent: true);
        }
    }

    private bool HasArtFor(CharacterDefinition who, string poseName)
    {
        if (PoseByName(poseName) is not { } pose) return false;
        var sprite = who == _player ? ResolveFacingSprite(pose, out _) : ResolveCharacterSprite(who, pose, MapCharacterOf(who)?.Facing ?? SpriteDirection.South, out _, out _);
        return sprite is { Frames.Count: > 0 };
    }

    // ---------- Feign Death, Withstand Death ----------

    /// <summary>Falls and gets up (the Die, or Body, pose, then Stand Up, if it has them); for the seconds after that those fooled by it leave it be.</summary>
    private bool StartFeignDeath(CharacterDefinition who, double seconds, int endSoundId)
    {
        if (IsDead(who) || Has(who, Ailment.FeignDeath)) return false;
        Inflict(who, Ailment.FeignDeath);
        var vitals = VitalsOf(who);
        vitals.FeignRemainingMs = Math.Max(0.1, seconds) * 1000;
        vitals.FeignSoundId = endSoundId;
        StopAttackingInvisible(who); // those who no longer see it stop fighting it
        var steps = new List<Func<Func<double, bool?>?>>();
        string? fall = new[] { "Die", "Body" }.FirstOrDefault(p => HasArtFor(who, p));
        if (fall is not null) steps.Add(PoseOnce(who, fall));
        if (HasArtFor(who, "Stand Up")) steps.Add(PoseOnce(who, "Stand Up"));
        if (steps.Count > 0) BeginScriptAction(who, Chain(steps.ToArray()), null, afterEvent: true);
        return true;
    }

    private void UpdateFeignDeath(CharacterDefinition character, Vitals vitals, double elapsedMs)
    {
        if (!vitals.Ailments.HasFlag(Ailment.FeignDeath) || (vitals.FeignRemainingMs -= elapsedMs) > 0) return;
        Cure(character, Ailment.FeignDeath);
        PlaySoundFrom(vitals.FeignSoundId, character);
    }

    /// <summary>The blow that would kill it: its health is back instead, once. True: it was saved.</summary>
    private bool WithstoodDeath(CharacterDefinition character, int hpAfter)
    {
        if (!Has(character, Ailment.WithstandDeath) || character.Immortal || !IsDying(character, hpAfter)) return false;
        Cure(character, Ailment.WithstandDeath);
        int max = MaxHp(character);
        if (max > 0) SetHp(character, max);
        if (character == _player) StatusText.Text = "You withstand death!";
        return true;
    }

    private bool FeignDeathCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        return StartFeignDeath(who, FormulaNumber(c, "Seconds"), int.TryParse(c.Get("SoundId"), out int sound) ? sound : -1);
    }

    private bool WithstandDeathCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        if (c.Get("Value") == "Take it away") { Cure(who, Ailment.WithstandDeath); return true; }
        return Inflict(who, Ailment.WithstandDeath);
    }

    // ---------- Changeling ----------

    private bool ChangelingCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who || who == _player || IsDead(who) || MapCharacterOf(who) is not { } mapCharacter) return false;
        if (CharacterDefinitionFor(c.Get("Other")) is not { } other || other == who) return false;
        double cells = Math.Max(1, FormulaNumber(c, "Cells"));
        bool attacks = c.Get("Attacks") != "False";
        int hpAtStart = GetHp(who) ?? 0;
        _ownLooks.TryAdd(who, who.PoseSprites);
        who.PoseSprites = new Dictionary<int, PoseAssignment>(other.PoseSprites);
        mapCharacter.ShownSprite = null;
        SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
        BeginScriptAction(who, _ =>
        {
            var foe = EnemiesWithin(who, cells).FirstOrDefault();
            bool hurt = (GetHp(who) ?? 0) < hpAtStart;
            if (foe is null && !hurt) return null; // still waiting
            SoakLog($"changeling: {who.Name} is itself again (hurt {hurt}, enemy {foe?.Name})");
            if (_ownLooks.Remove(who, out var own)) who.PoseSprites = own;
            mapCharacter.ShownSprite = null;
            SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
            if (attacks && (foe ?? _player) is { } target) StartCombat(mapCharacter, target);
            return true;
        }, null, afterEvent: true);
        return true;
    }

    /// <summary>Those who would attack it, or it them, on its floor within so many map cells: the nearest first.</summary>
    private IEnumerable<CharacterDefinition> EnemiesWithin(CharacterDefinition who, double cells)
    {
        var (x, y) = PixelOf(who);
        if (double.IsNaN(x)) return Array.Empty<CharacterDefinition>();
        double Apart(CharacterDefinition o)
        {
            var (ox, oy) = PixelOf(o);
            return double.IsNaN(ox) ? double.MaxValue : Math.Max(Math.Abs(ox - x) / _tileWidth, Math.Abs(oy - y) / _tileHeight);
        }
        return AllLivingCharacters().Where(o => o != who && SameFloor(who, o) && (Attitude(o, who) == FactionBehavior.Attack || Attitude(who, o) == FactionBehavior.Attack) && Apart(o) <= cells).OrderBy(Apart).ToList();
    }

    // ---------- Things in an area ----------

    private readonly record struct AreaCentre(int X, int Y, int Floor);

    /// <summary>The middle of an area command: Who, a spot, or this object.</summary>
    private AreaCentre? AreaCentreOf(EventCommand c)
    {
        switch (c.Get("Around"))
        {
            case "A spot (below)":
                if (!int.TryParse(c.Get("X"), out int x) || !int.TryParse(c.Get("Y"), out int y)) return null;
                return new AreaCentre(x, y, int.TryParse(c.Get("Floor"), out int f) ? f : 1);
            case "This object":
                return _firingOwner is ObjectInstance self ? new AreaCentre(self.X, self.Y, MapFloors.FloorOfLift(self.Z)) : null;
            default:
                return CharacterDefinitionFor(c.Get("Who")) is { } who ? CentreOf(who) : null;
        }
    }

    private AreaCentre CentreOf(CharacterDefinition who)
    {
        var (x, y) = CellOf(who);
        return new AreaCentre(x, y, MapFloors.FloorOfLift(LiftOf(who)));
    }

    /// <summary>Placed things within so many cells on the floor: of one kind (itemId below 0: any), lit ones only if asked. Each with its count.</summary>
    private List<(object Thing, int Count)> ThingsInArea(AreaCentre centre, int cells, ReferencedItemCategory? category, int itemId, bool onlyLit)
    {
        var found = new List<(object, int)>();
        bool Near(int x, int y) => Math.Abs(x - centre.X) <= cells && Math.Abs(y - centre.Y) <= cells;
        foreach (var o in _map.Objects)
        {
            if (o.ContainerInstanceId >= 0 || !Near(o.X, o.Y) || MapFloors.FloorOfLift(o.Z) != centre.Floor) continue;
            if (itemId >= 0 && (category != ReferencedItemCategory.Object || o.ObjectId != itemId)) continue;
            if (onlyLit && o.IsLitRuntime != true) continue;
            found.Add((o, Math.Max(1, o.Quantity)));
        }
        if (onlyLit) return found; // only objects burn
        foreach (var i in _map.Items)
        {
            if (!Near(i.X, i.Y) || MapFloors.FloorOfLift(i.Z) != centre.Floor) continue;
            if (itemId >= 0 && (i.Category != category || i.ItemId != itemId)) continue;
            found.Add((i, 1));
        }
        return found;
    }

    private static (ReferencedItemCategory? Category, int ItemId) ItemFieldOf(EventCommand c) =>
        Enum.TryParse<ReferencedItemCategory>(c.Get("Category"), out var category) && int.TryParse(c.Get("ItemId"), out int id) ? (category, id) : (null, -1);

    private bool FindObjectsCommand(EventCommand c)
    {
        if (AreaCentreOf(c) is not { } centre) return false;
        var (category, itemId) = ItemFieldOf(c);
        int count = ThingsInArea(centre, Math.Max(0, (int)FormulaNumber(c, "Cells")), category, itemId, c.Get("OnlyLit") == "True").Sum(t => t.Count);
        SetScriptVariable(c.Get("Variable"), (double)count);
        return true;
    }

    private bool DeleteObjectsInAreaCommand(EventCommand c)
    {
        if (AreaCentreOf(c) is not { } centre) return false;
        var (category, itemId) = ItemFieldOf(c);
        int most = (int)FormulaNumber(c, "Most");
        var things = ThingsInArea(centre, Math.Max(0, (int)FormulaNumber(c, "Cells")), category, itemId, c.Get("OnlyLit") == "True");
        if (most > 0) things = things.Take(most).ToList();
        foreach (var (thing, _) in things)
        {
            if (thing is ObjectInstance o) RemoveMapObject(o);
            else if (thing is PlacedItemInstance i)
            {
                _map.Items.Remove(i);
                if (_itemVisuals.Remove(i, out var visual)) { GameCanvas.Children.Remove(visual); UnregisterWorldAnimator(visual); }
            }
        }
        if (things.Count > 0) { SettleWorld(); RefreshAfterMove(); }
        return things.Count > 0;
    }

    private string CarriedName(InventoryItemInstance item) =>
        (item.Category == ReferencedItemCategory.Container ? ContainerDef(item.ItemId)?.Name : ObjectDef(item)?.Name) ?? string.Empty;

    /// <summary>What is inside a bag someone carries (named as in its editor) - counted, and taken away if asked.</summary>
    private bool ItemsInsideCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        string name = c.Get("BagName").Trim();
        var bag = AllWithin(who.Inventory).FirstOrDefault(i => string.Equals(CarriedName(i), name, StringComparison.OrdinalIgnoreCase));
        if (bag is null) { SetScriptVariable(c.Get("Variable"), 0.0); return false; }
        var (category, itemId) = ItemFieldOf(c);
        bool Wanted(InventoryItemInstance i) => itemId < 0 || (i.Category == category && i.ItemId == itemId);
        SetScriptVariable(c.Get("Variable"), (double)AllWithin(bag.Contents).Where(Wanted).Sum(i => Math.Max(1, i.Quantity)));
        if (c.Get("Remove") == "True")
        {
            void TakeFrom(List<InventoryItemInstance> list) { list.RemoveAll(Wanted); foreach (var i in list) TakeFrom(i.Contents); }
            TakeFrom(bag.Contents);
            RefreshAfterMove();
        }
        return true;
    }

    /// <summary>A carried item's charges as they stand: not used yet, what it starts with (a focus, a charged weapon).</summary>
    private int ChargesOf(InventoryItemInstance item)
    {
        if (item.ChargesRuntime >= 0) return item.ChargesRuntime;
        if (ObjectDef(item) is not { } def) return 0;
        if (def.Type == ObjectType.Weapon && def.Weapon.Ammo == WeaponAmmo.Charges) return Math.Max(0, def.Weapon.Charges);
        return Math.Max(0, def.Spellbook.U8FocusCharges);
    }

    private bool ItemChargesCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        var (category, itemId) = ItemFieldOf(c);
        var item = AllWithin(who.Inventory).FirstOrDefault(i => i.Category == category && i.ItemId == itemId);
        if (item is null) return false;
        int amount = (int)FormulaNumber(c, "Amount");
        switch (c.Get("Operation"))
        {
            case "Set": item.ChargesRuntime = Math.Max(0, amount); break;
            case "Add": item.ChargesRuntime = Math.Max(0, ChargesOf(item) + amount); break;
        }
        if (c.Get("Variable").Length > 0) SetScriptVariable(c.Get("Variable"), (double)ChargesOf(item));
        return true;
    }

    /// <summary>Everyone chosen within reach takes this damage, of this type, by their own rules for it - Grant Peace, a blast, a holy word.</summary>
    private bool HurtInAreaCommand(EventCommand c)
    {
        if (AreaCentreOf(c) is not { } centre) return false;
        int cells = Math.Max(0, (int)FormulaNumber(c, "Cells")), amount = (int)FormulaNumber(c, "Amount");
        var type = Enum.TryParse<DamageType>(c.Get("DamageType"), out var parsed) ? parsed : DamageType.Normal;
        var by = CharacterDefinitionFor(c.Get("Who"));
        string affects = c.Get("Affects");
        var struck = AllLivingCharacters().ToList().Where(t =>
        {
            if (t == by) return false;
            var at = CentreOf(t);
            if (at.Floor != centre.Floor || Math.Abs(at.X - centre.X) > cells || Math.Abs(at.Y - centre.Y) > cells) return false;
            return affects switch
            {
                "Enemies of Who" => by is not null && Attitude(t, by) == FactionBehavior.Attack,
                "Only those with a rule for this type" => t.DamageResponses.Any(r => r.Type == type && r.Response != DamageResponseKind.Normal),
                _ => true
            };
        }).ToList();
        foreach (var target in struck)
        {
            DamageAllowed(target, AfterDamageResponse(target, amount, type), by is null ? "an event" : $"struck by {by.Name}", type);
            if (by is not null && IsDead(target)) AwardKill(by, target);
        }
        return struck.Count > 0;
    }

    // ---------- Conditions ----------

    private bool TestWorldCondition(GameCondition c, CharacterDefinition? who)
    {
        switch (c.Kind)
        {
            case GameConditionKind.InParty:
                if (c.CharacterId == GameCondition.Anyone) return AllLivingCharacters().Count(m => m != _player && m.IsInParty) >= Math.Max(1, c.Number);
                return who is not null && (who == _player || who.IsInParty);
            case GameConditionKind.CharacterState:
                if (who is null) return false;
                return c.Value switch
                {
                    "Unconscious" => IsUnconscious(who),
                    "In combat" => who == _player ? _combatMode : MapCharacterOf(who)?.Npc.InCombat == true,
                    "Dead" => IsDead(who),
                    "Controlled by the player" => who == _player,
                    _ => Enum.TryParse<Ailment>(c.Value, out var ailment) && Has(who, ailment)
                };
            case GameConditionKind.EnemyNear:
                return who is not null && !IsDead(who) && EnemiesWithin(who, Math.Max(1, c.Number)).Any();
            case GameConditionKind.ObjectsInArea:
            {
                if (who is null) return false;
                bool any = c.Item is not { ItemId: >= 0 };
                int count = ThingsInArea(CentreOf(who), Math.Max(0, c.Number2), any ? null : c.Item!.Category, any ? -1 : c.Item!.ItemId, c.On == false).Sum(t => t.Count);
                return GameConditions.Compare(count, c.Comparison, c.Number);
            }
            case GameConditionKind.ItemCharges:
            {
                if (who is null || c.Item is not { ItemId: >= 0 } wanted) return false;
                var item = AllWithin(who.Inventory).FirstOrDefault(i => IsItem(i, wanted));
                return item is not null && GameConditions.Compare(ChargesOf(item), c.Comparison, c.Number);
            }
        }
        return false;
    }
}
