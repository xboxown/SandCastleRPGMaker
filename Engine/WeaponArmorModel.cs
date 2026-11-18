namespace SandcastleRpgMaker.Engine;

/// <summary>Exult's damage types (Weapon_data: normal, fire, magic, lightning, ethereal, sonic, freezing). Armor can make its wearer immune to any of them.</summary>
public enum DamageType { Normal, Fire, Magic, Lightning, Ethereal, Sonic, Freezing, Blade, Blunt, Undead, Slayer, Pierce, Falling }

/// <summary>What a damage type does to a character (Character Editor > Health).</summary>
public enum DamageResponseKind { Normal, Immune, Half, Adjust }

/// <summary>One character's rule for one damage type; a type with no rule does its damage as usual.</summary>
public class DamageResponse
{
    public DamageType Type { get; set; }
    public DamageResponseKind Response { get; set; }
    /// <summary>Adjust: added to the damage (below 0 takes off, never under 0).</summary>
    public int Amount { get; set; }
}

/// <summary>Exult's weapon powers (Weapon_data: sleep, charm, curse, poison, paralyze, magebane, no_damage): what a blow that lands does besides hurting.</summary>
[Flags]
public enum WeaponPowers { None = 0, Sleep = 1, Charm = 2, Curse = 4, Poison = 8, Paralyze = 16, Magebane = 32, NoDamage = 128 }

/// <summary>Exult's Weapon_info uses: hand to hand, thrown (badly or well - throwing loses accuracy with distance), or a ranged weapon that always shoots.</summary>
public enum WeaponUse { Melee, PoorThrown, GoodThrown, Ranged }

/// <summary>Exult's Weapon_info actor frames: how the wielder moves to strike (actors.cc get_attack_frames) - reach (thrust), raise (overhead), fast swing, slow swing.</summary>
public enum WeaponSwing { Reach, Raise, FastSwing, SlowSwing }

/// <summary>Exult's Weapon_info ammo: none; ammo of a family (arrows for bows); charges (a wand, a magic staff - quality_ammo); or the weapon itself is thrown and used up (throwing daggers - quantity_ammo).</summary>
public enum WeaponAmmo { None, Ammo, Charges, ThrowsItself }

/// <summary>Weapon settings (Object Editor > Weapon), after Exult's Weapon_info (shapes/shapeinf/weaponinf.h) and combat.cc.</summary>
public class WeaponSettings
{
    /// <summary>The paperdoll slot it's wielded in (a built-in slot, or a custom slot by name). A weapon also fits either hand.</summary>
    public EquipmentSlot? EquipSlot { get; set; } = EquipmentSlot.RightHand;
    public string? CustomSlotName { get; set; }

    /// <summary>Exult's damage: each landed blow does 1..Damage, plus 1..Strength/3 (apply_damage). 127 or more always does 127.</summary>
    public int Damage { get; set; } = 4;
    /// <summary>Its look held in the hand on the map (a sprite in the Sprite Editor's Weapon in hand group) - unset: not drawn in the hand.</summary>
    public SpriteLink InHandSprite { get; set; } = new();
    /// <summary>Ultima 8 only: damage is random(0..this) + Damage + Strength / 5 (WeaponInfo::_damageModifier).</summary>
    public int U8DamageModifier { get; set; }
    /// <summary>Ultima 8 only: added to the wielder's Dexterity when it attacks (WeaponInfo::_dexAttackBonus).</summary>
    public int U8DexAttackBonus { get; set; }
    /// <summary>Ultima 8 only: added to the wielder's Dexterity when it is attacked (WeaponInfo::_dexDefendBonus).</summary>
    public int U8DexDefendBonus { get; set; }
    /// <summary>Ultima 8 only: added to the wielder's armour class (WeaponInfo::_armourBonus).</summary>
    public int U8ArmourBonus { get; set; }
    public DamageType DamageType { get; set; } = DamageType.Normal;
    public WeaponPowers Powers { get; set; } = WeaponPowers.None;

    public WeaponUse Use { get; set; } = WeaponUse.Melee;
    /// <summary>How far it shoots or is thrown (Exult tiles of 8px).</summary>
    public int RangeTiles { get; set; } = 16;

    /// <summary>How the wielder strikes in hand to hand, and when shooting or throwing (Exult's actor frames).</summary>
    public WeaponSwing Swing { get; set; } = WeaponSwing.FastSwing;
    public WeaponSwing ShootSwing { get; set; } = WeaponSwing.Reach;
    /// <summary>The character pose played to strike; empty = "Attacking Melee".</summary>
    public string AttackPose { get; set; } = string.Empty;
    /// <summary>The character pose played to shoot or throw; empty = "Shooting".</summary>
    public string ShootPose { get; set; } = string.Empty;

    public WeaponAmmo Ammo { get; set; } = WeaponAmmo.None;
    /// <summary>Ammo: the family it fires (an Ammo object's Family, e.g. "Arrows").</summary>
    public string AmmoFamily { get; set; } = string.Empty;
    /// <summary>Charges: how many shots a new one has.</summary>
    public int Charges { get; set; } = 10;
    /// <summary>Charges: it's gone when the last one is used (Exult's delete_depleted).</summary>
    public bool GoneWhenEmpty { get; set; }
    /// <summary>What flies when it shoots (a Projectile object: a fireball, a bolt of lightning); none = the ammo, or the weapon itself when thrown.</summary>
    public int ProjectileObjectId { get; set; } = -1;

    /// <summary>Exult's lucky: +3 to hit. Autohit: never misses. Returns: comes back after it's thrown (a boomerang, the magic axe).</summary>
    public bool Lucky { get; set; }
    public bool AutoHit { get; set; }
    public bool Returns { get; set; }

    public int AttackSoundResourceId { get; set; } = -1;
    public int HitSoundResourceId { get; set; } = -1;
}

/// <summary>What one magic bonus (on the item, or put on one copy by Enchant Item) adds while the item is worn or held.</summary>
public enum MagicBonusKind { Stat, Defense, Damage, Reach, Light }

/// <summary>One bonus: a stat (+ or -, any stat - custom ones too), defense (armor points), damage, reach (cells) or light (brightness).</summary>
public class MagicBonus
{
    public MagicBonusKind Kind { get; set; } = MagicBonusKind.Stat;
    /// <summary>Stat: the stat's id (Stat Editor).</summary>
    public int StatId { get; set; } = -1;
    public int Amount { get; set; }
}

/// <summary>
/// Object Editor > Weapon / Armor > Magic: what the item does while it's worn
/// or held - the magic sword's long reach and its bonuses, the fire sword's
/// light, immunities, a curse (it won't come off until Lift Curse), and
/// common events run as it goes on and comes off (for anything else).
/// Not Exult (Exult hardcodes these in usecode); Ultima 8 has some of it
/// (WeaponInfo armour / dex bonuses - kept in their own fields).
/// </summary>
public class MagicSettings
{
    /// <summary>Hand to hand: how many map cells away it strikes (1 = next to it, as every weapon in Exult).</summary>
    public int ReachCells { get; set; } = 1;
    /// <summary>Added to the wearer's armor (every blow reduced by 1..all armor) - a weapon that defends.</summary>
    public int DefenseBonus { get; set; }
    /// <summary>Stats raised (or lowered: a cursed item) while it's worn. Applied as it goes on, taken back exactly as it comes off.</summary>
    public List<MagicBonus> StatBonuses { get; set; } = new();
    /// <summary>Light while worn or held: a Light Source's brightness (0 = none). Never burns out - the fire sword.</summary>
    public int LightBrightness { get; set; }
    /// <summary>Damage types that do nothing to whoever wears or holds it.</summary>
    public List<DamageType> Immunities { get; set; } = new();
    /// <summary>Once on, it won't come off until a Lift Curse command (a spell, a healer's paid service, a condition met).</summary>
    public bool Cursed { get; set; }
    /// <summary>Said (status line / box) when someone tries to take it off while it's cursed.</summary>
    public string CursedText { get; set; } = "It will not come off - it is cursed!";
    /// <summary>Common events run as it goes on / comes off (the wearer is the one who set it off) - for custom stats or anything else. -1: none.</summary>
    public int OnEquipCommonEventId { get; set; } = -1;
    public int OnUnequipCommonEventId { get; set; } = -1;

    public bool IsEmpty => ReachCells <= 1 && DefenseBonus == 0 && StatBonuses.Count == 0 && LightBrightness <= 0 && Immunities.Count == 0
        && !Cursed && OnEquipCommonEventId < 0 && OnUnequipCommonEventId < 0;
}

/// <summary>A stat change a worn magic item really made on its wearer (kept on the character, saved): taken back exactly when the item comes off.</summary>
public class AppliedMagic
{
    public int InstanceId { get; set; }
    public int StatId { get; set; }
    /// <summary>What it really moved the stat by (a stat never goes below 0), and what the item asked for.</summary>
    public int Amount { get; set; }
    public int Wanted { get; set; }
}

/// <summary>A worn item whose On Equip common event has run - its On Unequip runs when it comes off.</summary>
public class WornMagicItem
{
    public int InstanceId { get; set; }
    public int ObjectId { get; set; }
}

/// <summary>Armor settings (Object Editor > Armor), after Exult's Armor_info: armor points and immunities, for whatever slot it's worn in.</summary>
public class ArmorSettings
{
    public EquipmentSlot? EquipSlot { get; set; } = EquipmentSlot.Body;
    public string? CustomSlotName { get; set; }
    /// <summary>Exult's armor: every blow is reduced by 1..(all the armor worn) points (apply_damage).</summary>
    public int ArmorPoints { get; set; } = 2;
    /// <summary>Exult's get_object_warmth, against a Cold zone: all that's worn adds up, from -75; 100 or more keeps the cold out.</summary>
    public int Warmth { get; set; }
    /// <summary>The same against a Hot zone.</summary>
    public int Cooling { get; set; }
    /// <summary>Ultima 8 only: added to a kick's damage (ArmourInfo::_kickAttackBonus - boots).</summary>
    public int U8KickBonus { get; set; }
    /// <summary>Damage types that do nothing at all to whoever wears it (Exult's armor immunity: the blow just clangs).</summary>
    public List<DamageType> Immunities { get; set; } = new();
    /// <summary>The clang when a blow does nothing because of it.</summary>
    public int ImmuneSoundResourceId { get; set; } = -1;
}
