using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Weapons and armor in play, ported from Exult (combat.cc
/// Game_object::attack_object, actors.cc get_weapon / figure_hit_points /
/// apply_damage / get_attack_frames):
///  - The weapon used is the Weapon object in the right hand, else the left
///    hand, else one wielded in its own slot (a custom "Tail" slot, say).
///  - A blow: 30-sided roll + Combat (+3 if lucky) against the defender's
///    Combat (+3 protected); autohit never misses. Damage: 1..Strength/3
///    (not for lightning) + 1..weapon damage (127 = always 127), minus
///    1..(all armor worn) - lightning, ethereal and sonic ignore armor; a
///    damage type the wearer's armor is immune to does nothing (a clang).
///    A blow that hurts also brings its powers: sleep, charm, curse, poison,
///    paralyze, magebane (no-damage weapons bring only their powers).
///  - Shooting and throwing (not_in_melee_range): a ranged weapon always
///    shoots; a thrown one is thrown when the foe is out of reach and within
///    its range. Attack +6, minus the distance (poorly thrown) or half of it
///    (well thrown). What flies is its Projectile object, else the ammo,
///    else the weapon itself. Ammo of its family is used up; charges run
///    down (and it may be gone when empty); a thrown weapon lands where its
///    flight ends unless it returns.
///  - Strike style (Weapon_info actor frames): reach, raise, fast or slow
///    swing - how long the strike takes; the pose shown is the weapon's own
///    (default "Attacking Melee", "Shooting").
/// </summary>
public partial class GameTestWindow
{
    /// <summary>A shot on its way: who fired it, with what, how well aimed - the hit is rolled when it arrives (Projectile_effect keeps the attack value).</summary>
    private sealed record WeaponShot(CharacterDefinition Attacker, WorldObject Weapon, WorldObject? Ammo, int AttackValue, WorldObject? Thrown);

    private WorldObject? ObjectDef(InventoryItemInstance item) =>
        item.Category == ReferencedItemCategory.Object ? ObjectDef(item.ItemId) : null;

    /// <summary>A weapon's or armor's own slot for this character: a built-in slot, or their custom slot of that name.</summary>
    private static object? OwnSlotFor(EquipmentSlot? slot, string? customName, CharacterDefinition character) =>
        slot is { } builtin ? builtin : customName is { } name ? character.Paperdoll.CustomSlots.FirstOrDefault(s => s.Name == name)?.Id : null;

    private static object? OwnSlotFor(WorldObject def, CharacterDefinition character) => def.Type switch
    {
        ObjectType.Weapon => OwnSlotFor(def.Weapon.EquipSlot, def.Weapon.CustomSlotName, character),
        ObjectType.Armor => OwnSlotFor(def.Armor.EquipSlot, def.Armor.CustomSlotName, character),
        _ => null
    };

    /// <summary>Everything worn or held: each slot with the item in it.</summary>
    private static IEnumerable<(object Slot, InventoryItemInstance Item)> ItemsInSlots(CharacterDefinition character)
    {
        foreach (var (slot, id) in character.EquippedSlots)
            if (character.Inventory.FirstOrDefault(i => i.InstanceId == id) is { } item) yield return (slot, item);
        foreach (var (slot, id) in character.CustomEquippedSlots)
            if (character.Inventory.FirstOrDefault(i => i.InstanceId == id) is { } item) yield return (slot, item);
    }

    /// <summary>Actor::get_weapon: the weapon in hand (right, then left), else one wielded in its own slot. Null: bare hands.</summary>
    private (InventoryItemInstance Item, WorldObject Def)? WieldedWeapon(CharacterDefinition character)
    {
        var equipped = ItemsInSlots(character).Select(e => (e.Slot, e.Item, Def: ObjectDef(e.Item))).Where(e => e.Def is { Type: ObjectType.Weapon }).ToList();
        foreach (var hand in new object[] { EquipmentSlot.RightHand, EquipmentSlot.LeftHand })
            foreach (var e in equipped)
                if (Equals(e.Slot, hand)) return (e.Item, e.Def!);
        foreach (var e in equipped)
            if (Equals(OwnSlotFor(e.Def!, character), e.Slot)) return (e.Item, e.Def!);
        return null;
    }

    /// <summary>Armor worn anywhere (a shield in a hand counts, as in Exult).</summary>
    private List<WorldObject> WornArmor(CharacterDefinition character) =>
        ItemsInSlots(character).Select(e => ObjectDef(e.Item)).Where(d => d is { Type: ObjectType.Armor }).Select(d => d!).ToList();

    /// <summary>Actor::apply_damage (no difficulty bias): the damage a landed blow does, after armor - 0 and a clang if the armor is immune.</summary>
    private int WeaponDamage(CharacterDefinition attacker, CharacterDefinition defender, int weaponPoints, DamageType type)
    {
        int str = Stat(attacker, BuiltInStatKind.Strength, 10) / 3, damage = 0;
        if (weaponPoints >= 127) damage = 127;
        else
        {
            if (type != DamageType.Lightning && str > 0) damage += 1 + Rng.Next(str); // lightning ignores strength
            if (weaponPoints > 0) damage += 1 + Rng.Next(weaponPoints);
        }
        int armor = 0;
        foreach (var worn in WornArmor(defender))
        {
            armor += Math.Max(0, worn.Armor.ArmorPoints);
            if (worn.Armor.Immunities.Contains(type))
            {
                PlaySoundFrom(worn.Armor.ImmuneSoundResourceId, defender); // Exult: metal clang
                return 0;
            }
        }
        if (weaponPoints >= 127 || type is DamageType.Lightning or DamageType.Ethereal or DamageType.Sonic) armor = 0; // these ignore armor
        if (armor > 0) damage -= 1 + Rng.Next(armor);
        if (damage <= 0 && IsIncapacitated(defender)) damage = str > 0 ? 1 + Rng.Next(str) : 0; // the defenceless get hurt anyway
        return Math.Max(0, damage);
    }

    /// <summary>A blow (or a shot) that has landed: damage, then its powers (none if it did no damage, unless it's a no-damage weapon).</summary>
    private void LandBlow(CharacterDefinition attacker, CharacterDefinition defender, WorldObject? weapon, WorldObject? ammo)
    {
        PartyHitMusic(defender); // from the danger music to the battle music (GameTestWindow.CombatMusic.cs)
        int points = weapon?.Weapon.Damage ?? 1; // bare hands: 1
        var type = weapon?.Weapon.DamageType ?? DamageType.Normal;
        var powers = weapon?.Weapon.Powers ?? WeaponPowers.None;
        if (ammo is not null)
        {
            points += ammo.Ammo.DamageBonus;
            if (ammo.Ammo.Poisons) powers |= WeaponPowers.Poison;
        }
        PlaySoundFrom(weapon?.Weapon.HitSoundResourceId ?? -1, defender);
        bool noDamage = powers.HasFlag(WeaponPowers.NoDamage);
        int damage = noDamage || points == 0 ? 0 : WeaponDamage(attacker, defender, points, type);
        damage = AfterDamageResponse(defender, damage, type); // its own rule for this type (Character Editor > Health)
        if (!WoundsIt(defender, weapon, null)) damage = 0; // it takes another weapon or a spell to hurt this one (Character Editor > Health)
        if (ShowHitNumbers) ShowFloatingText(defender, $"-{damage}"); // Exult's show_hits
        if (damage <= 0 && !noDamage) return; // no damage, no powers
        if (damage > 0)
        {
            bool willDie = !defender.Immortal && !Has(defender, Ailment.WithstandDeath) && (GetHp(defender) ?? 99) - damage <= 0;
            if (willDie) ReportFactionIncident(defender, attacker, FactionIncident.Kill);
            MaybeStartBleeding(defender, damage);
            DamageAllowed(defender, damage, attacker == _player ? "slain by you" : $"slain by {attacker.Name}", type);
            if (willDie && attacker == _player) StatusText.Text = $"You have slain {defender.Name}.";
            if (willDie && IsDead(defender)) AwardKill(attacker, defender); // Exult: the killing blow's experience (GameTestWindow.Levels.cs)
        }
        if (IsDead(defender)) return;
        if (powers.HasFlag(WeaponPowers.Sleep)) Inflict(defender, Ailment.Asleep);
        if (powers.HasFlag(WeaponPowers.Charm)) Inflict(defender, Ailment.Charmed);
        if (powers.HasFlag(WeaponPowers.Curse)) Inflict(defender, Ailment.Cursed);
        if (powers.HasFlag(WeaponPowers.Poison)) Inflict(defender, Ailment.Poisoned);
        if (powers.HasFlag(WeaponPowers.Paralyze)) Inflict(defender, Ailment.Paralyzed);
        if (powers.HasFlag(WeaponPowers.Magebane)) SetStat(defender, BuiltInStatKind.Mana, 0);
        FireCharacterEvent(defender, BookTriggerConditionBased.ByHit); // a blow that landed
    }

    /// <summary>How long a strike takes, by the weapon's strike style (Exult's attack frames after the ready frame: reach 1, raise and fast swing 2, slow swing 3).</summary>
    private static int StrikeMs(WeaponSwing swing) => (swing switch
    {
        WeaponSwing.Reach => 1,
        WeaponSwing.SlowSwing => 3,
        _ => 2
    }) * StdDelay;

    /// <summary>The pose to strike or shoot with: the weapon's own, else the default - if the character has art for it, else "Attacking Melee".</summary>
    private string StrikePose(CharacterDefinition character, WorldObject? weapon, bool shooting, SpriteDirection facing)
    {
        string name = shooting
            ? (string.IsNullOrWhiteSpace(weapon?.Weapon.ShootPose) ? "Shooting" : weapon!.Weapon.ShootPose)
            : (string.IsNullOrWhiteSpace(weapon?.Weapon.AttackPose) ? "Attacking Melee" : weapon!.Weapon.AttackPose);
        return PoseByName(name) is { } pose && ResolveCharacterSprite(character, pose, facing, out _, out _) is not null ? name : "Attacking Melee";
    }

    // ================= Shooting and throwing =================

    /// <summary>combat.cc not_in_melee_range + range: whether this weapon shoots (or is thrown) at a foe this far, rather than striking.</summary>
    private static bool Shoots(WorldObject? weapon, bool inReach, double tiles) =>
        weapon is { Type: ObjectType.Weapon } w
        && (w.Weapon.Use == WeaponUse.Ranged || (w.Weapon.Use is WeaponUse.PoorThrown or WeaponUse.GoodThrown && !inReach))
        && tiles <= Math.Max(1, w.Weapon.RangeTiles);

    /// <summary>Ammo of a family: in a slot first (a quiver), then the pack, then inside what's carried.</summary>
    private (InventoryItemInstance Item, List<InventoryItemInstance> Holder, WorldObject Def)? FindAmmo(CharacterDefinition character, string family)
    {
        bool Fits(InventoryItemInstance i) => ObjectDef(i) is { Type: ObjectType.Ammo } d && string.Equals(d.Ammo.Family.Trim(), family.Trim(), StringComparison.OrdinalIgnoreCase);
        foreach (var (_, item) in ItemsInSlots(character)) if (Fits(item)) return (item, character.Inventory, ObjectDef(item)!);
        foreach (var item in character.Inventory) if (Fits(item)) return (item, character.Inventory, ObjectDef(item)!);
        (InventoryItemInstance, List<InventoryItemInstance>, WorldObject)? Inside(List<InventoryItemInstance> contents)
        {
            foreach (var item in contents)
            {
                if (Fits(item)) return (item, contents, ObjectDef(item)!);
                if (item.Contents.Count > 0 && Inside(item.Contents) is { } deeper) return deeper;
            }
            return null;
        }
        foreach (var item in character.Inventory) if (item.Contents.Count > 0 && Inside(item.Contents) is { } found) return found;
        return null;
    }

    /// <summary>One of a stack used up (gone at 0).</summary>
    private void UseOne(CharacterDefinition character, InventoryItemInstance item, List<InventoryItemInstance> holder)
    {
        if (item.Quantity > 1) { item.Quantity--; return; }
        if (ReferenceEquals(holder, character.Inventory)) RemoveFromCharacter(character, item);
        else holder.Remove(item);
    }

    /// <summary>
    /// Game_object::attack_object at range: ammo, charges or the weapon itself
    /// used up, and the shot sent flying at the foe. False (with why, for the
    /// player) if it can't be fired.
    /// </summary>
    private bool Shoot(CharacterDefinition attacker, CharacterDefinition target, InventoryItemInstance weaponItem, WorldObject weapon)
    {
        var w = weapon.Weapon;
        WorldObject? ammoDef = null;
        WorldObject? thrown = null;
        switch (w.Ammo)
        {
            case WeaponAmmo.Ammo:
                if (FindAmmo(attacker, w.AmmoFamily) is not { } ammo)
                {
                    if (attacker == _player) StatusText.Text = $"Out of ammunition for \"{weapon.Name}\".";
                    return false;
                }
                ammoDef = ammo.Def;
                UseOne(attacker, ammo.Item, ammo.Holder);
                break;
            case WeaponAmmo.Charges:
                if (weaponItem.ChargesRuntime < 0) weaponItem.ChargesRuntime = Math.Max(0, w.Charges);
                if (weaponItem.ChargesRuntime <= 0)
                {
                    if (attacker == _player) StatusText.Text = $"\"{weapon.Name}\" has no charges left.";
                    return false;
                }
                weaponItem.ChargesRuntime--;
                if (weaponItem.ChargesRuntime <= 0 && w.GoneWhenEmpty) RemoveFromCharacter(attacker, weaponItem);
                break;
            case WeaponAmmo.ThrowsItself:
                if (!w.Returns) { thrown = weapon; UseOne(attacker, weaponItem, attacker.Inventory); }
                break;
        }
        var flies = _objects.Objects.FirstOrDefault(o => o.Id == w.ProjectileObjectId && o.Type == ObjectType.Projectile) ?? ammoDef ?? weapon;
        double tiles = TilesBetween(attacker, target);
        int attval = Stat(attacker, BuiltInStatKind.Combat, 10) + (w.Lucky ? 3 : 0) + 6;
        if (w.Use == WeaponUse.PoorThrown) attval -= (int)tiles;
        else if (w.Use == WeaponUse.GoodThrown) attval -= (int)tiles / 2;
        var (ax, ay) = PixelOf(attacker);
        var (tx, ty) = PixelOf(target);
        (tx, ty) = NearestImage(ax, ay, tx, ty); // shot over the seam of a map that wraps around, if that's the short way
        PlaySoundFrom(w.AttackSoundResourceId, attacker);
        LaunchToward(flies, ax, ay, tx - ax, ty - ay, LiftOf(attacker) + 2, attacker, Math.Max(1, w.RangeTiles) + 2,
            new WeaponShot(attacker, weapon, ammoDef, attval, thrown), LiftOf(target) + 2, Math.Sqrt((tx - ax) * (tx - ax) + (ty - ay) * (ty - ay))); // up at a bird, down from a wall
        RefreshAfterMove();
        return true;
    }

    /// <summary>A weapon's shot reaches someone: the hit is rolled now, with the attack value it was fired with.</summary>
    private void ShotArrives(WeaponShot shot, CharacterDefinition victim)
    {
        if (IsDead(victim)) return;
        int defval = Stat(victim, BuiltInStatKind.Combat, 10) + (Has(victim, Ailment.Protected) ? 3 : 0);
        FightBack(victim, shot.Attacker);
        if (!shot.Weapon.Weapon.AutoHit && !RollToWin(shot.AttackValue, defval))
        {
            if (ShowHitNumbers) ShowFloatingText(victim, "miss");
            return;
        }
        LandBlow(shot.Attacker, victim, shot.Weapon, shot.Ammo);
    }
}