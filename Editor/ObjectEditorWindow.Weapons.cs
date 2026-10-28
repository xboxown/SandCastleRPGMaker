using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Object Editor's Weapon and Armor panels (build 308), after Exult's
/// Weapon_info and Armor_info: the slot it goes in (built-in or a custom
/// slot), damage, damage type and powers, how it's used (hand to hand,
/// thrown, ranged), ammo / charges / thrown itself, what flies, strike
/// style and poses; armor points and immunities. What they do in play is in
/// GameTestWindow.Weapons.cs.
/// </summary>
public partial class ObjectEditorWindow
{
    /// <summary>A paperdoll slot drop-down: the built-in slots, then every custom slot any character has (by name).</summary>
    private FrameworkElement GearSlotPicker(string label, Func<(EquipmentSlot? Slot, string? Custom)> get, Action<EquipmentSlot?, string?> set)
    {
        var slots = Enum.GetValues<EquipmentSlot>().Select(s => (Label: s.ToString(), Slot: (EquipmentSlot?)s, Custom: (string?)null)).ToList();
        slots.AddRange(ProjectSession.Characters.Characters.SelectMany(c => c.Paperdoll.CustomSlots).Select(s => s.Name).Distinct()
            .Select(n => ($"{n} (custom)", (EquipmentSlot?)null, (string?)n)));
        var current = get();
        var combo = new ComboBox { Width = 200, ItemsSource = slots.Select(s => s.Label).ToList() };
        combo.SelectedIndex = Math.Max(0, slots.FindIndex(s => s.Slot == current.Slot && s.Custom == current.Custom));
        combo.SelectionChanged += (_, _) => { var s = slots[Math.Max(0, combo.SelectedIndex)]; set(s.Slot, s.Custom); };
        return Labeled(label, combo);
    }

    /// <summary>A pose drop-down (the project's poses), first entry = the default.</summary>
    private static FrameworkElement PosePicker(string label, string defaultName, Func<string> get, Action<string> set)
    {
        var names = new List<string> { $"(default: {defaultName})" };
        names.AddRange(ProjectSession.Poses.Poses.Select(p => p.Name).Where(n => n != defaultName));
        var combo = new ComboBox { Width = 180, ItemsSource = names };
        combo.SelectedIndex = Math.Max(0, names.IndexOf(get()));
        combo.SelectionChanged += (_, _) => set(combo.SelectedIndex <= 0 ? string.Empty : names[combo.SelectedIndex]);
        return Labeled(label, combo);
    }

    private static FrameworkElement EnumPicker<T>(string label, Func<T> get, Action<T> set, Func<T, string> name, double width = 150) where T : struct, Enum
    {
        var values = Enum.GetValues<T>().ToList();
        var combo = new ComboBox { Width = width, ItemsSource = values.Select(name).ToList(), SelectedIndex = Math.Max(0, values.IndexOf(get())) };
        combo.SelectionChanged += (_, _) => set(values[Math.Max(0, combo.SelectedIndex)]);
        return Labeled(label, combo);
    }

    private static string SwingName(WeaponSwing swing) => swing switch
    {
        WeaponSwing.Reach => "Reach (thrust)",
        WeaponSwing.Raise => "Raise (overhead)",
        WeaponSwing.FastSwing => "Fast swing",
        _ => "Slow swing"
    };

    private void BuildWeapon(WorldObject obj)
    {
        var w = obj.Weapon;
        var body = Group("Weapon");
        var row = new WrapPanel();
        row.Children.Add(GearSlotPicker("Wielded in (also fits either hand)", () => (w.EquipSlot, w.CustomSlotName), (s, c) => { w.EquipSlot = s; w.CustomSlotName = c; }));
        row.Children.Add(NumberField("Damage", () => w.Damage, v => w.Damage = (int)Math.Clamp(v, 0, 127), "Each blow that lands does 1 to this, plus 1 to Strength/3 (Exult's apply_damage). 127 always does 127."));
        body.Children.Add(SpritePicker("In the hand (Sprite Editor > Weapon in hand; empty: not drawn in the hand)", () => (w.InHandSprite.SpriteGroup, w.InHandSprite.SpriteName),
            (g, n) => { w.InHandSprite.SpriteGroup = g; w.InHandSprite.SpriteName = n; }, onlyGroup: SpriteDatabase.WeaponInHandGroupName));
        row.Children.Add(EnumPicker("Damage type", () => w.DamageType, v => w.DamageType = v, v => v.ToString(), 110));
        body.Children.Add(row);

        var powers = new WrapPanel();
        powers.Children.Add(new TextBlock { Text = "A blow that lands also:", Margin = new Thickness(0, 4, 8, 0) });
        foreach (var (power, label) in new[] { (WeaponPowers.Sleep, "puts to sleep"), (WeaponPowers.Poison, "poisons"), (WeaponPowers.Paralyze, "paralyzes"),
                     (WeaponPowers.Charm, "charms"), (WeaponPowers.Curse, "curses"), (WeaponPowers.Magebane, "drains all mana (magebane)"), (WeaponPowers.NoDamage, "does no damage (powers only)") })
        {
            var p = power;
            powers.Children.Add(Check(label, () => w.Powers.HasFlag(p), v => w.Powers = v ? w.Powers | p : w.Powers & ~p));
        }
        body.Children.Add(powers);

        var use = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        use.Children.Add(EnumPicker("Used", () => w.Use, v => { w.Use = v; ShowCurrent(); }, v => v switch
        {
            WeaponUse.Melee => "Hand to hand",
            WeaponUse.PoorThrown => "Thrown (badly)",
            WeaponUse.GoodThrown => "Thrown (well)",
            _ => "Ranged (always shoots)"
        }, 160));
        if (w.Use != WeaponUse.Melee)
            use.Children.Add(NumberField("Range (tiles of 8px)", () => w.RangeTiles, v => w.RangeTiles = (int)Math.Clamp(v, 1, 200)));
        body.Children.Add(use);

        if (w.Use != WeaponUse.Melee)
        {
            var ammo = new WrapPanel();
            ammo.Children.Add(EnumPicker("Each shot uses", () => w.Ammo, v => { w.Ammo = v; ShowCurrent(); }, v => v switch
            {
                WeaponAmmo.None => "nothing",
                WeaponAmmo.Ammo => "ammo of a family",
                WeaponAmmo.Charges => "a charge (wand, magic staff)",
                _ => "the weapon itself (thrown)"
            }, 200));
            if (w.Ammo == WeaponAmmo.Ammo)
                ammo.Children.Add(TextField("Ammo family (an Ammo object's Family)", 160, () => w.AmmoFamily, v => w.AmmoFamily = v));
            if (w.Ammo == WeaponAmmo.Charges)
            {
                ammo.Children.Add(NumberField("Charges when new", () => w.Charges, v => w.Charges = (int)Math.Clamp(v, 0, 9999)));
                ammo.Children.Add(Check("Gone when the last charge is used", () => w.GoneWhenEmpty, v => w.GoneWhenEmpty = v));
            }
            if (w.Ammo == WeaponAmmo.ThrowsItself)
                ammo.Children.Add(Check("Comes back (a boomerang)", () => w.Returns, v => w.Returns = v));
            body.Children.Add(ammo);
            body.Children.Add(ObjectPicker("What flies (a Projectile object: a fireball, a lightning bolt)", o => o.Type == ObjectType.Projectile, () => w.ProjectileObjectId, v => w.ProjectileObjectId = v,
                "(the ammo, or the weapon itself)"));
        }

        var strike = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        strike.Children.Add(EnumPicker("Strikes with a", () => w.Swing, v => w.Swing = v, SwingName));
        strike.Children.Add(PosePicker("Strike pose", "Attacking Melee", () => w.AttackPose, v => w.AttackPose = v));
        if (w.Use != WeaponUse.Melee)
        {
            strike.Children.Add(EnumPicker("Shoots / throws with a", () => w.ShootSwing, v => w.ShootSwing = v, SwingName));
            strike.Children.Add(PosePicker("Shoot pose", "Shooting", () => w.ShootPose, v => w.ShootPose = v));
        }
        body.Children.Add(strike);

        var flags = new WrapPanel();
        flags.Children.Add(Check("Two-handed (the left hand stays empty)", () => obj.IsTwoHanded, v => obj.IsTwoHanded = v));
        flags.Children.Add(Check("Lucky (+3 to hit)", () => w.Lucky, v => w.Lucky = v));
        flags.Children.Add(Check("Never misses", () => w.AutoHit, v => w.AutoHit = v));
        body.Children.Add(flags);
        var sounds = new WrapPanel();
        sounds.Children.Add(SoundPicker("Attack sound", () => w.AttackSoundResourceId, v => w.AttackSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Hit sound", () => w.HitSoundResourceId, v => w.HitSoundResourceId = v));
        body.Children.Add(sounds);
        if (ProjectSession.IsUltima8)
        {
            // An Ultima 8 project: its engine's own numbers (Pentagram's WeaponInfo).
            var u8 = new WrapPanel();
            u8.Children.Add(NumberField("Ultima 8: damage modifier", () => w.U8DamageModifier, v => w.U8DamageModifier = (int)Math.Clamp(v, 0, 127), "Ultima 8: a blow does random 0..this, plus Damage, plus Strength/5."));
            u8.Children.Add(NumberField("Attack Dex bonus", () => w.U8DexAttackBonus, v => w.U8DexAttackBonus = (int)Math.Clamp(v, -50, 50), "Ultima 8: added to the wielder's Dexterity when it attacks (to hit)."));
            u8.Children.Add(NumberField("Defend Dex bonus", () => w.U8DexDefendBonus, v => w.U8DexDefendBonus = (int)Math.Clamp(v, -50, 50), "Ultima 8: added to the wielder's Dexterity when it is attacked."));
            u8.Children.Add(NumberField("Armour bonus", () => w.U8ArmourBonus, v => w.U8ArmourBonus = (int)Math.Clamp(v, 0, 50), "Ultima 8: added to the wielder's armour class (a shield-like weapon)."));
            body.Children.Add(u8);
            body.Children.Add(Note("Ultima 8 (Pentagram): a blow lands when random 0..(attacking Dex + 2) beats random 0..(defending Dex - 1); it does random 0..modifier + Damage + Strength/5; " +
                                   "bare hands random 0..Strength/2 + 1; armour takes 3 x its armour class percent off (half against fire), blocking the blocker's Strength/5."));
        }
        body.Children.Add(Note("As Exult (combat.cc, actors.cc): a 30-sided roll plus the wielder's Combat (+3 lucky) against the foe's Combat decides a hit; damage is 1..Damage plus 1..Strength/3, " +
                               "less 1..(all the armor the foe wears) - lightning, ethereal and sonic damage ignore armor. A ranged weapon always shoots; a thrown one is thrown at a foe out of reach, " +
                               "losing aim with distance (badly: the whole distance, well: half). The strike style sets how long a blow takes (reach is quickest, slow swing slowest); " +
                               "the pose is what the wielder shows meanwhile - any pose with art for that character, else Attacking Melee."));
    }

    private void BuildArmor(WorldObject obj)
    {
        var a = obj.Armor;
        var body = Group("Armor");
        var row = new WrapPanel();
        row.Children.Add(GearSlotPicker("Worn in (also fits either hand: a shield)", () => (a.EquipSlot, a.CustomSlotName), (s, c) => { a.EquipSlot = s; a.CustomSlotName = c; }));
        row.Children.Add(NumberField("Armor points", () => a.ArmorPoints, v => a.ArmorPoints = (int)Math.Clamp(v, 0, 127), "All the armor a character wears adds up; every blow is reduced by 1 to that total (Exult's apply_damage). Ultima 8: its armour class - every blow 3 x that percent less."));
        if (ProjectSession.IsUltima8) row.Children.Add(NumberField("Ultima 8: kick bonus", () => a.U8KickBonus, v => a.U8KickBonus = (int)Math.Clamp(v, 0, 50), "Ultima 8: added to a kick's damage (boots)."));
        body.Children.Add(row);
        var immune = new WrapPanel();
        immune.Children.Add(new TextBlock { Text = "Makes its wearer immune to:", Margin = new Thickness(0, 4, 8, 0) });
        foreach (var type in Enum.GetValues<DamageType>())
        {
            var t = type;
            immune.Children.Add(Check(t.ToString(), () => a.Immunities.Contains(t), v => { a.Immunities.Remove(t); if (v) a.Immunities.Add(t); }));
        }
        body.Children.Add(immune);
        body.Children.Add(SoundPicker("Sound when a blow does nothing (a clang)", () => a.ImmuneSoundResourceId, v => a.ImmuneSoundResourceId = v));
        body.Children.Add(Note("As Exult: armor worn anywhere counts, a shield held in a hand too. A blow of a damage type it's immune to does nothing at all - no damage, no poison or sleep."));
    }
}