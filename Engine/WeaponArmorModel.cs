namespace SandcastleRpgMaker.Engine;

/// <summary>Exult's damage types (Weapon_data: normal, fire, magic, lightning, ethereal, sonic, freezing). Armor can make its wearer immune to any of them.</summary>
public enum DamageType { Normal, Fire, Magic, Lightning, Ethereal, Sonic, Freezing }

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

/// <summary>Armor settings (Object Editor > Armor), after Exult's Armor_info: armor points and immunities, for whatever slot it's worn in.</summary>
public class ArmorSettings
{
    public EquipmentSlot? EquipSlot { get; set; } = EquipmentSlot.Body;
    public string? CustomSlotName { get; set; }
    /// <summary>Exult's armor: every blow is reduced by 1..(all the armor worn) points (apply_damage).</summary>
    public int ArmorPoints { get; set; } = 2;
    /// <summary>Ultima 8 only: added to a kick's damage (ArmourInfo::_kickAttackBonus - boots).</summary>
    public int U8KickBonus { get; set; }
    /// <summary>Damage types that do nothing at all to whoever wears it (Exult's armor immunity: the blow just clangs).</summary>
    public List<DamageType> Immunities { get; set; } = new();
    /// <summary>The clang when a blow does nothing because of it.</summary>
    public int ImmuneSoundResourceId { get; set; } = -1;
}
