// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's combat (Pentagram's U8AvatarMoverProcess::handleCombatMode, Actor::calculateAttackDamage,
/// MainActor::getDamageAmount / getAttackingDex / getDefendingDex):
///  - The Avatar fights only when told: in combat mode a double left click swings toward the pointer, a double right
///    click kicks, the left button held blocks; a double right click on the Avatar (or C) goes in and out of combat.
///  - To hit: random(0..attacking Dex + 2) beats random(0..defending Dex - 1) (Dex plus the weapon's attack / defend bonus).
///  - Damage: a weapon random(0..its modifier) + its base + Strength / 5; bare hands random(0..Strength / 2) + 1; a kick
///    random(0..Strength / 2) + the leg armour's kick bonus. Blocking takes the blocker's Strength / 5 off; armour takes
///    3 x its armour class percent off (half against fire), at most all of it.
/// </summary>
public partial class U8GameWindow
{
    private bool _u8Blocking;
    private bool _u8Kicking;
    private PoseDefinition? _u8NormalIdle;

    /// <summary>Ultima 8: random(n) is 0..n.</summary>
    private static int U8Random(int n) => n <= 0 ? 0 : Rng.Next(n + 1);

    private (int Attack, int Defend) U8DexBonus(CharacterDefinition who)
    {
        var w = WieldedWeapon(who)?.Def?.Weapon;
        return w is null ? (0, 0) : (w.U8DexAttackBonus, w.U8DexDefendBonus);
    }

    /// <summary>Actor::calculateAttackDamage's to-hit.</summary>
    private bool U8Hits(CharacterDefinition attacker, CharacterDefinition defender)
    {
        if (IsMotionless(defender)) return true; // stunned (lying still): every blow lands
        int attackDex = Math.Max(0, Stat(attacker, BuiltInStatKind.Dexterity, 10) + U8DexBonus(attacker).Attack);
        int defendDex = Math.Max(1, Stat(defender, BuiltInStatKind.Dexterity, 10) + U8DexBonus(defender).Defend);
        return U8Random(attackDex + 2) > U8Random(defendDex - 1);
    }

    /// <summary>MainActor::getDamageAmount, then the block and the armour (calculateAttackDamage).</summary>
    private int U8Damage(CharacterDefinition attacker, CharacterDefinition defender, WorldObject? weapon, WorldObject? ammo, DamageType type)
    {
        int str = Stat(attacker, BuiltInStatKind.Strength, 10), damage;
        if (_u8Kicking)
        {
            int kickBonus = WornArmor(attacker).Sum(a => a.Armor.U8KickBonus);
            damage = U8Random(str / 2) + kickBonus;
        }
        else if (weapon is { Type: ObjectType.Weapon } w)
            damage = U8Random(w.Weapon.U8DamageModifier) + w.Weapon.Damage + (ammo?.Ammo.DamageBonus ?? 0) + str / 5;
        else damage = U8Random(str / 2) + 1;
        foreach (var worn in WornArmor(defender))
            if (worn.Armor.Immunities.Contains(type)) { PlaySoundFrom(worn.Armor.ImmuneSoundResourceId, defender); return 0; }
        if (defender == _player && _u8Blocking) damage -= Stat(defender, BuiltInStatKind.Strength, 10) / 5; // blocking
        int ac = WornArmor(defender).Sum(a => Math.Max(0, a.Armor.ArmorPoints)) + (WieldedWeapon(defender)?.Def?.Weapon.U8ArmourBonus ?? 0);
        int acMod = 3 * ac;
        if (type == DamageType.Fire) acMod /= 2; // armour doesn't protect from fire as well
        if (IsMotionless(defender)) acMod /= 2; // stunned
        acMod = Math.Min(100, acMod);
        damage = (100 - acMod) * damage / 100;
        return Math.Max(0, damage);
    }

    /// <summary>In or out of combat: the combat stance is the Avatar's idle while in it (Combat Stand), drawing the weapon (Ready Weapon).</summary>
    private void U8CombatModeChanged()
    {
        _u8NormalIdle ??= _idlePose;
        _idlePose = _combatMode ? PoseByName("Combat Stand") ?? _u8NormalIdle : _u8NormalIdle;
        _u8Blocking = false;
        if (!_wasMovingLastFrame) ApplyPoseSprite(PoseByName(_combatMode ? "Ready Weapon" : "Unready Weapon") ?? _idlePose);
        _playerPoseResetMs = 2 * StdDelay;
    }

    /// <summary>A mouse press in combat mode on the map: Ultima 8's swing, kick, block. False: not combat's (a window, or not in combat).</summary>
    private bool U8CombatMouseDown(MouseButtonEventArgs e)
    {
        if (!_combatMode || _player is null || IsIncapacitated(_player) || _gamePaused || U8Stunned) return false;
        var onScreen = e.GetPosition(PaperdollGumpsCanvas);
        if (HitTestGumpLayer(onScreen, out _, out _)) return false; // a window takes its own clicks
        if (e.ChangedButton == MouseButton.Right && e.ClickCount == 2)
        {
            if (FindMapTarget(onScreen) is PlayerTarget) { ToggleCombatMode(); return true; } // double right click on the Avatar
            U8Swing(kick: true);
            return true;
        }
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2) { _u8Blocking = false; U8Swing(kick: false); return true; }
            if (!_rightMouseHeld) { _u8Blocking = true; ApplyPoseSprite(PoseByName("Block") ?? _idlePose); } // held: a block
            return true;
        }
        return false;
    }

    private void U8CombatMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_u8Blocking) return;
        _u8Blocking = false;
        ApplyPoseSprite(_idlePose);
    }

    /// <summary>A swing (or a kick) toward the pointer: whoever is in reach that way is struck; nobody - the air.</summary>
    private double _u8LastSwingMs = double.NegativeInfinity;

    private void U8Swing(bool kick)
    {
        if (_player is null) return;
        // U8AvatarMoverProcess: not again till 25 - Dexterity frames (30 a second) after the last one.
        double now = AnimationClock.NowMs, wait = Math.Max(0, 25 - Stat(_player, BuiltInStatKind.Dexterity, 10)) * 1000.0 / 30;
        if (now - _u8LastSwingMs < wait) return;
        _u8LastSwingMs = now;
        BuildUpFromSwing(_player); // attacking gives Strength and Dexterity (U8GameWindow.U8Reactions.cs)
        var (screen, _) = PointerFromAvatar();
        var (dx, dy) = WorldFromScreen(screen.X, screen.Y);
        _playerFacing = FacingOf(dx, dy);
        var weapon = WieldedWeapon(_player);
        // A bow or a thrown weapon: shot at the nearest foe that way, in range.
        double facingAngle = Math.Atan2(dy, dx);
        bool InFront(CharacterDefinition c)
        {
            var (x, y) = PixelOf(c);
            double a = Math.Atan2(y - _playerPixelY, x - _playerPixelX), d = Math.Abs(Math.IEEERemainder(a - facingAngle, 2 * Math.PI));
            return d <= Math.PI * 3 / 8;
        }
        var candidates = AllLivingCharacters().Where(c => c != _player && !c.IsInParty && SameFloor(_player, c) && InFront(c)).OrderBy(c => TilesBetween(_player, c)).ToList();
        if (!kick && weapon is { } held && candidates.FirstOrDefault(c => Shoots(held.Def, InReach(_player, c), TilesBetween(_player, c))) is { } shotAt)
        {
            if (Shoot(_player, shotAt, held.Item, held.Def)) ApplyPoseSprite(PoseByName(StrikePose(_player, held.Def, true, _playerFacing)) ?? _idlePose);
            _playerPoseResetMs = StrikeMs(held.Def.Weapon.ShootSwing);
            return;
        }
        ApplyPoseSprite(kick ? PoseByName("Kick") ?? _idlePose : PoseByName(StrikePose(_player, weapon?.Def, false, _playerFacing)) ?? PoseByName("Attack") ?? _idlePose);
        _playerPoseResetMs = weapon is null || kick ? 2 * StdDelay : StrikeMs(weapon.Value.Def.Weapon.Swing);
        if (candidates.FirstOrDefault(c => InReach(_player, c)) is not { } target) return; // the air
        _u8Kicking = kick;
        try { Strike(_player, target); }
        finally { _u8Kicking = false; }
    }
}
