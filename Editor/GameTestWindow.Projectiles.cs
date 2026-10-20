using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Traps of three kinds, and the things shooting traps fire:
///  - Ground: whoever steps on it is hurt by it (spikes, glass, needles).
///  - Explosive: stepped on, it blows up at once - everyone and every
///    breakable thing in its blast radius is hurt.
///  - Shooting: stepped on (a pressure plate), or someone in its line of
///    fire within its range, it shoots its Projectile object the way the
///    placed copy faces (Map Editor, right-click > Facing).
/// A Projectile (a fireball, a lightning bolt, a dart) is a real object
/// flying across the map, as Ultima VII's are: it can be clicked to name it
/// (the game paused, it hangs in the air), but it's too heavy to pick up.
/// It hurts the first character it meets (and can explode), stops at
/// anything solid, and is gone at the end of its range.
/// </summary>
public partial class GameTestWindow
{
    private sealed class Flying
    {
        public required ObjectInstance Instance;
        public required WorldObject Def;
        /// <summary>Its centre, in map pixels, and where it's heading (per second).</summary>
        public double X, Y, VX, VY;
        public double Traveled;
        public int Lift;
        /// <summary>What shot it (never hit by its own shot): a trap, or a character.</summary>
        public object? Shooter;
        /// <summary>A weapon's shot: the hit is rolled on arrival and the weapon's damage done (GameTestWindow.Weapons.cs). Null: a trap's projectile, with its own damage.</summary>
        public WeaponShot? Shot;
        /// <summary>How far it flies (tiles of 8px); 0 = its own range.</summary>
        public int MaxTiles;
        /// <summary>A spell's missile: what happens where it lands (whoever it met, or the spot) - instead of its own damage.</summary>
        public Action<CharacterDefinition?, double, double>? OnArrive;
    }

    private readonly List<Flying> _flying = new();
    private double _lineOfFireCheckMs;

    // ================= Traps =================

    /// <summary>A trap's kind decides what stepping on it does.</summary>
    private void TriggerTrap(ObjectInstance trap, WorldObject obj, CharacterDefinition victim)
    {
        switch (obj.Trap.Kind)
        {
            case TrapKind.Explosive:
                if (_trapRearmMs.ContainsKey(trap) || (obj.Trap.OneShot && trap.TrapSprungRuntime)) return;
                trap.TrapSprungRuntime = true;
                if (!obj.Trap.OneShot && obj.Trap.RearmSeconds > 0) _trapRearmMs[trap] = obj.Trap.RearmSeconds * 1000;
                if (victim == _player) StatusText.Text = $"You set off \"{obj.Name}\"!";
                FireObjectEvent(trap, obj, BookTriggerConditionBased.ByEnter, MapCharacterOf(victim));
                TrapBlast(trap, obj);
                return;
            case TrapKind.Shooting:
                if (obj.Trap.TriggeredBy == TrapTrigger.StepOn) FireTrap(trap, obj, victim);
                return;
            default:
                SpringTrap(trap, obj, victim); // Ground
                return;
        }
    }

    /// <summary>An explosive trap goes off: the blast hurts and breaks what's around it; then it's gone (if set so).</summary>
    private void TrapBlast(ObjectInstance trap, WorldObject obj)
    {
        var t = obj.Trap;
        var body = BodyOf(null, trap);
        double cx = body.Left + body.Width / 2.0, cy = body.Top + body.Depth / 2.0;
        PlaySound(t.ExplosionSoundResourceId >= 0 ? t.ExplosionSoundResourceId : t.TriggerSoundResourceId);
        PlayOneShot(t.ExplosionSprite, VisualOf(trap) as FrameworkElement);
        if (VisualOf(trap) is { } visual) visual.Visibility = Visibility.Visible;
        Blast(cx, cy, t.RadiusTiles, t.DamageMin, t.DamageMax, $"a {obj.Name.ToLowerInvariant()}", trap);
        if (t.RemovedAfterExplosion) RemoveMapObject(trap);
        SettleWorld();
        RefreshAfterMove();
    }

    /// <summary>Everyone and every breakable thing within the radius (Exult tiles, 8px) of a point (in 8px units) is hurt; explosives catch.</summary>
    private void Blast(double cx, double cy, int radius, int damageMin, int damageMax, string what, ObjectInstance? source)
    {
        bool InReach(double x, double y) => Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) <= Math.Max(1, radius);
        int Roll() => Math.Min(damageMin, damageMax) + Rng.Next(Math.Abs(damageMax - damageMin) + 1);
        foreach (var character in AllLivingCharacters().ToList())
        {
            var (px, py) = PixelOf(character);
            if (InReach(px / SpriteSizing.BaseUnit, py / SpriteSizing.BaseUnit)) Damage(character, Roll(), what);
        }
        foreach (var other in _map.Objects.Where(o => o.ContainerInstanceId < 0 && !ReferenceEquals(o, source)).ToList())
        {
            if (DefinitionOf(other) is not { } def) continue;
            var b = BodyOf(null, other);
            if (!InReach(b.Left + b.Width / 2.0, b.Top + b.Depth / 2.0)) continue;
            if (def.Type == ObjectType.Explosive) { if (other.FuseRuntime < 0) { other.FuseRuntime = 0.25; other.IsLitRuntime = true; } continue; }
            if (IsBreakable(def)) StrikeObject(other, def, Roll(), byPlayer: false);
        }
    }

    /// <summary>A shooting trap shoots its projectile the way the placed copy faces.</summary>
    private void FireTrap(ObjectInstance trap, WorldObject obj, CharacterDefinition? victim)
    {
        var t = obj.Trap;
        if (_trapRearmMs.ContainsKey(trap) || (t.OneShot && trap.TrapSprungRuntime)) return;
        if (_objects.Objects.FirstOrDefault(o => o.Id == t.ProjectileObjectId && o.Type == ObjectType.Projectile) is not { } projectile)
        {
            StatusText.Text = $"\"{obj.Name}\" has no projectile to shoot (Object Editor > Trap > Shoots).";
            return;
        }
        bool wasHidden = t.Hidden && !trap.TrapSprungRuntime;
        trap.TrapSprungRuntime = true;
        if (!t.OneShot) _trapRearmMs[trap] = Math.Max(0.2, t.RearmSeconds) * 1000;
        if (wasHidden && VisualOf(trap) is { } visual) visual.Visibility = Visibility.Visible;
        PlaySound(t.TriggerSoundResourceId);
        FireObjectEvent(trap, obj, BookTriggerConditionBased.ByEnter, victim is null ? null : MapCharacterOf(victim));
        var body = BodyOf(null, trap);
        var (dx, dy) = FacingStep(trap.Facing);
        Launch(projectile, (body.Left + body.Width / 2.0) * SpriteSizing.BaseUnit, (body.Top + body.Depth / 2.0) * SpriteSizing.BaseUnit, dx, dy, trap.Z + 1, trap);
    }

    /// <summary>Shooting traps set off by someone in their line of fire (checked a few times a second).</summary>
    private void UpdateShootingTraps(double elapsedMs)
    {
        if ((_lineOfFireCheckMs -= elapsedMs) > 0) return;
        _lineOfFireCheckMs = 250;
        foreach (var trap in _map.Objects.Where(o => o.ContainerInstanceId < 0).ToList())
        {
            if (DefinitionOf(trap) is not { Type: ObjectType.Trap, Trap: { Kind: TrapKind.Shooting, TriggeredBy: TrapTrigger.LineOfFire } } obj) continue;
            if (_trapRearmMs.ContainsKey(trap) || (obj.Trap.OneShot && trap.TrapSprungRuntime)) continue;
            var body = BodyOf(null, trap);
            double tx = body.Left + body.Width / 2.0, ty = body.Top + body.Depth / 2.0;
            var (dx, dy) = FacingStep(trap.Facing);
            foreach (var who in AllLivingCharacters())
            {
                var (px, py) = PixelOf(who);
                double ux = px / SpriteSizing.BaseUnit - tx, uy = py / SpriteSizing.BaseUnit - ty;
                double along = ux * dx + uy * dy; // how far ahead of it, in units
                double across = Math.Abs(ux * dy - uy * dx) / Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                if (along <= 0 || along > Math.Max(1, obj.Trap.RangeTiles) || across > 2) continue;
                FireTrap(trap, obj, who);
                break;
            }
        }
    }

    // ================= Projectiles =================

    /// <summary>A projectile sets off from a point (map pixels) heading one way.</summary>
    private void Launch(WorldObject projectile, double x, double y, int dx, int dy, int lift, object? shooter) =>
        LaunchToward(projectile, x, y, dx, dy, lift, shooter, 0, null);

    /// <summary>A projectile (or arrow, or thrown weapon) sets off from a point (map pixels) toward a direction - a weapon's shot carries how it was fired.</summary>
    private void LaunchToward(WorldObject projectile, double x, double y, double dx, double dy, int lift, object? shooter, int maxTiles, WeaponShot? shot)
    {
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) dy = 1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double speed = Math.Max(1, projectile.Projectile.SpeedTiles) * SpriteSizing.BaseUnit;
        var instance = new ObjectInstance
        {
            ObjectId = projectile.Id, InstanceId = _map.GetNextInstanceId(), ContainerInstanceId = -1,
            X = (int)Math.Floor(x / _tileWidth), Y = (int)Math.Floor(y / _tileHeight), Z = lift,
            Facing = dx > 0 ? SpriteDirection.East : dx < 0 ? SpriteDirection.West : dy < 0 ? SpriteDirection.North : SpriteDirection.South
        };
        AddMapEntity(null, instance);
        var flying = new Flying { Instance = instance, Def = projectile, X = x, Y = y, VX = dx / length * speed, VY = dy / length * speed, Lift = lift, Shooter = shooter, Shot = shot, MaxTiles = maxTiles };
        _flying.Add(flying);
        if (projectile.Projectile.RotatesToDirection && VisualOf(instance) is FrameworkElement visual)
        {
            visual.RenderTransformOrigin = new Point(0.5, 0.5);
            visual.RenderTransform = new RotateTransform(Math.Atan2(dy, dx) * 180 / Math.PI - 90); // its art points down (south)
        }
        if (projectile.Type == ObjectType.Projectile) PlaySound(projectile.Projectile.LaunchSoundResourceId);
        PlaceFlying(flying);
    }

    private void PlaceFlying(Flying flying)
    {
        if (VisualOf(flying.Instance) is not FrameworkElement visual) return;
        Canvas.SetLeft(visual, flying.X - visual.Width / 2);
        Canvas.SetTop(visual, flying.Y - visual.Height / 2 - flying.Lift * LiftPixelShift);
        Panel.SetZIndex(visual, AboveEverythingZIndex - 100); // in the air: over what it flies past
        WorldChanged();
        flying.Instance.X = Math.Clamp((int)Math.Floor(flying.X / _tileWidth), 0, _map.Width - 1);
        flying.Instance.Y = Math.Clamp((int)Math.Floor(flying.Y / _tileHeight), 0, _map.Height - 1);
    }

    /// <summary>Each frame: every projectile flies on, and hits whoever or whatever it meets.</summary>
    private void UpdateProjectiles(double elapsedMs)
    {
        UpdateShootingTraps(elapsedMs);
        if (_flying.Count == 0) return;
        double seconds = elapsedMs / 1000.0;
        foreach (var flying in _flying.ToList())
        {
            if (!_map.Objects.Contains(flying.Instance)) { _flying.Remove(flying); continue; } // taken away by an event
            double step = Math.Sqrt(flying.VX * flying.VX + flying.VY * flying.VY) * seconds;
            // Small steps, so a fast one doesn't jump past someone.
            int parts = Math.Max(1, (int)Math.Ceiling(step / (SpriteSizing.BaseUnit / 2.0)));
            for (int i = 0; i < parts; i++)
            {
                flying.X += flying.VX * seconds / parts;
                flying.Y += flying.VY * seconds / parts;
                flying.Traveled += step / parts;
                if (CheckHit(flying)) break;
                if (flying.Traveled / SpriteSizing.BaseUnit >= Math.Max(1, flying.MaxTiles > 0 ? flying.MaxTiles : flying.Def.Projectile.RangeTiles)) { EndFlight(flying, null); break; }
                if (flying.X < 0 || flying.Y < 0 || flying.X >= _map.Width * _tileWidth || flying.Y >= _map.Height * _tileHeight) { EndFlight(flying, null); break; }
            }
            if (_flying.Contains(flying)) PlaceFlying(flying);
        }
    }

    /// <summary>Whether it has met someone (hurt) or something solid (stopped) - true when its flight is over.</summary>
    private bool CheckHit(Flying flying)
    {
        var p = flying.Def.Projectile;
        int size = Math.Max(1, flying.Def.SizeX);
        var foot = new UnitRect((int)Math.Floor(flying.X / SpriteSizing.BaseUnit) - size / 2, (int)Math.Floor(flying.Y / SpriteSizing.BaseUnit) - size / 2, size, Math.Max(1, flying.Def.SizeY));
        foreach (var who in AllLivingCharacters())
        {
            if (ReferenceEquals(who, flying.Shooter)) continue;
            int lift = LiftOf(who);
            if (flying.Lift < lift - 1 || flying.Lift > lift + 5 || !FootOf(who).Overlaps(foot)) continue;
            EndFlight(flying, who);
            return true;
        }
        foreach (var body in WorldNow().Overlapping(foot))
        {
            if (!body.Solid || ReferenceEquals(body.Entity, flying.Instance) || ReferenceEquals(body.Entity, flying.Shooter) || body.Entity is PlacedCharacter) continue;
            if (flying.Lift < body.Z || flying.Lift >= body.TopLift || !body.Overlaps(foot)) continue;
            if (body.Entity is ObjectInstance struck) FireHitTarget(flying.Instance, flying.Def, HitTargetKind.Object, null); // By Hitting Target: an object
            EndFlight(flying, null);
            return true;
        }
        return false;
    }

    /// <summary>The flight is over: whoever it met is hurt (and maybe worse), it may explode, and it's gone.</summary>
    private void EndFlight(Flying flying, CharacterDefinition? victim)
    {
        var p = flying.Def.Projectile;
        _flying.Remove(flying);
        if (flying.OnArrive is { } arrive)
        {
            if (p.Explodes) { PlayOneShot(p.ExplosionSprite, VisualOf(flying.Instance) as FrameworkElement); PlaySound(p.ExplosionSoundResourceId); }
            else PlaySound(p.HitSoundResourceId);
            RemoveMapObject(flying.Instance);
            arrive(victim, flying.X, flying.Y);
            RefreshAfterMove();
            return;
        }
        if (flying.Shot is { } shot)
        {
            // A weapon's shot: the weapon's hit and damage; a projectile it fires (a fireball) still explodes as it's set to.
            if (victim is not null) { ShotArrives(shot, victim); FireHitTarget(flying.Instance, flying.Def, HitTargetKind.Character, MapCharacterOf(victim)); }
            if (flying.Def.Type == ObjectType.Projectile && p.Explodes && (victim is not null || p.ExplodesAtEnd))
            {
                PlayOneShot(p.ExplosionSprite, VisualOf(flying.Instance) as FrameworkElement);
                PlaySound(p.ExplosionSoundResourceId);
                Blast(flying.X / SpriteSizing.BaseUnit, flying.Y / SpriteSizing.BaseUnit, p.RadiusTiles, p.DamageMin, p.DamageMax, $"a {flying.Def.Name.ToLowerInvariant()}", flying.Instance);
            }
            var landed = flying.Instance;
            RemoveMapObject(flying.Instance);
            if (shot.Thrown is { } thrownWeapon) // a thrown weapon lands where its flight ended (Exult drops it there)
                AddMapEntity(null, new ObjectInstance { ObjectId = thrownWeapon.Id, InstanceId = _map.GetNextInstanceId(), ContainerInstanceId = -1, X = landed.X, Y = landed.Y, Z = Math.Max(0, LiftOf(shot.Attacker)) });
            SettleWorld();
            RefreshAfterMove();
            return;
        }
        if (victim is not null)
        {
            int low = Math.Min(p.DamageMin, p.DamageMax), high = Math.Max(p.DamageMin, p.DamageMax);
            int damage = high <= 0 ? 0 : low + Rng.Next(high - low + 1);
            PlaySound(p.HitSoundResourceId);
            if (damage > 0) Damage(victim, damage, $"a {flying.Def.Name.ToLowerInvariant()}");
            var ailment = p.Ailment switch { TrapAilment.Poisoned => Ailment.Poisoned, TrapAilment.Asleep => Ailment.Asleep, TrapAilment.Paralyzed => Ailment.Paralyzed, _ => (Ailment?)null };
            if (ailment is { } a && !IsDead(victim)) Inflict(victim, a);
            FireObjectEvent(flying.Instance, flying.Def, BookTriggerConditionBased.ByUseItem, MapCharacterOf(victim)); // its own event: what it does to whoever it hits
            FireHitTarget(flying.Instance, flying.Def, HitTargetKind.Character, MapCharacterOf(victim));
        }
        if (p.Explodes && (victim is not null || p.ExplodesAtEnd))
        {
            PlayOneShot(p.ExplosionSprite, VisualOf(flying.Instance) as FrameworkElement);
            PlaySound(p.ExplosionSoundResourceId);
            Blast(flying.X / SpriteSizing.BaseUnit, flying.Y / SpriteSizing.BaseUnit, p.RadiusTiles, p.DamageMin, p.DamageMax, $"a {flying.Def.Name.ToLowerInvariant()}", flying.Instance);
        }
        RemoveMapObject(flying.Instance);
        RefreshAfterMove();
    }
}
