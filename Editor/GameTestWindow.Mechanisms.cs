using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The parts of the world that work - ported from Exult where it has them:
///  - Windows: solid and drawn by the wall rule (Game_object::compare), but
///    seen through; shutters open and close (closed, they can block sight);
///    glass that breaks leaves the Broken sprite, still solid.
///  - Levers, switches, buttons: flip (a button springs back), working what
///    each placed one is linked to: doors open/close (a locked one is
///    unlocked, as a portcullis winch), lights go on/off, explosives are
///    lit, windows' shutters open/close, levers flip, anything else gets
///    its By Use event.
///  - Stairs: their footprint cut into steps a lift apart, so walkers climb
///    them by Exult's walking rule (a lift at a time, chunks.cc is_blocked).
///    Ladders: double-click to climb to their destination.
///  - Inside (gamewin.cc / gamerend.cc skip_above, chunks.cc is_roof): when
///    anything solid is over the player's head (4 lifts up or more - an
///    upper floor's tiles, a roof), nothing at that lift or above is drawn,
///    so the inside shows.
///  - Explosives (Ultima VII's powder keg): lit, the fuse burns down; struck,
///    at once. The blast hurts everyone in reach, strikes breakable things,
///    and sets off other explosives.
///  - Glass: shatters when broken, when it falls, or when dropped from a
///    height; can leave shards.
/// </summary>
public partial class GameTestWindow
{
    // ================= Windows =================

    private void UseWindow(ObjectInstance window, WorldObject obj)
    {
        if (FireObjectEvent(window, obj, BookTriggerConditionBased.ByUseItem)) return;
        if (!obj.Window.HasShutters || window.IsSpentRuntime) { StatusText.Text = $"\"{obj.DisplayName(window.IsSpentRuntime)}\" - nothing happens."; return; }
        bool open = window.IsOpenRuntime != true;
        window.IsOpenRuntime = open;
        PlaySound(open ? obj.Window.OpenSoundResourceId : obj.Window.CloseSoundResourceId);
        RefreshObjectLook(window);
        StatusText.Text = open ? $"You open the shutters of \"{obj.Name}\"." : $"You close the shutters of \"{obj.Name}\".";
    }

    /// <summary>Whether a solid body still lets sight through: an open (or unshuttered, or broken) window does.</summary>
    private bool LetsSightThrough(object entity) =>
        entity is ObjectInstance instance && DefinitionOf(instance) is { Type: ObjectType.Window } window
        && (!window.Window.HasShutters || !window.Window.ClosedBlocksSight || instance.IsOpenRuntime == true || instance.IsSpentRuntime);

    // ================= Levers, switches, buttons =================

    private readonly Dictionary<ObjectInstance, double> _buttonResetMs = new(ReferenceEqualityComparer.Instance);

    private void PullLever(ObjectInstance lever, WorldObject obj)
    {
        if (FireObjectEvent(lever, obj, BookTriggerConditionBased.ByUseItem)) return;
        var l = obj.Lever;
        if (l.Kind == LeverKind.Button)
        {
            if (lever.IsOpenRuntime == true) return; // still pressed
            lever.IsOpenRuntime = true;
            _buttonResetMs[lever] = Math.Max(100, l.ButtonResetSeconds * 1000);
            PlaySound(l.OnSoundResourceId);
            RefreshObjectLook(lever);
            StatusText.Text = $"You press \"{obj.Name}\".";
            FireObjectEvent(lever, obj, BookTriggerConditionBased.ByAfterReadItem);
            WorkLinks(lever, depth: 0);
            return;
        }
        FlipLever(lever, obj, depth: 0);
        StatusText.Text = lever.IsOpenRuntime == true ? $"You pull \"{obj.Name}\"." : $"You push \"{obj.Name}\" back.";
    }

    private void FlipLever(ObjectInstance lever, WorldObject obj, int depth)
    {
        bool on = lever.IsOpenRuntime != true;
        lever.IsOpenRuntime = on;
        PlaySound(on ? obj.Lever.OnSoundResourceId : obj.Lever.OffSoundResourceId);
        RefreshObjectLook(lever);
        FireObjectEvent(lever, obj, on ? BookTriggerConditionBased.ByAfterReadItem : BookTriggerConditionBased.ByEnter);
        WorkLinks(lever, depth);
    }

    /// <summary>Everything the lever is linked to (Map Editor) changes over. Linked levers flip too - but a loop of levers stops after a few.</summary>
    private void WorkLinks(ObjectInstance lever, int depth)
    {
        if (depth > 4) return;
        foreach (int id in lever.LinkedInstanceIds.ToList())
        {
            if (_map.Objects.FirstOrDefault(o => o.InstanceId == id && o.ContainerInstanceId < 0) is not { } target || DefinitionOf(target) is not { } obj) continue;
            switch (obj.Type)
            {
                case ObjectType.Door:
                    if (IsDoorOpen(target)) TryCloseDoor(target, obj, null);
                    else { target.IsLockedRuntime = false; OpenDoor(target, obj, null); }
                    break;
                case ObjectType.LightSource:
                    if (target.IsSpentRuntime) break;
                    bool lit = target.IsLitRuntime != true;
                    target.IsLitRuntime = lit;
                    if (lit && obj.Light.BurnMinutes > 0 && target.BurnLeftRuntime < 0) target.BurnLeftRuntime = obj.Light.BurnMinutes;
                    PlaySound(lit ? obj.Light.LitSoundResourceId : obj.Light.OffSoundResourceId);
                    RefreshObjectLook(target);
                    break;
                case ObjectType.Window when obj.Window.HasShutters && !target.IsSpentRuntime:
                    target.IsOpenRuntime = target.IsOpenRuntime != true;
                    PlaySound(target.IsOpenRuntime == true ? obj.Window.OpenSoundResourceId : obj.Window.CloseSoundResourceId);
                    RefreshObjectLook(target);
                    break;
                case ObjectType.Explosive:
                    LightFuse(target, obj, byPlayer: true, forced: true);
                    break;
                case ObjectType.Lever when obj.Lever.Kind != LeverKind.Button:
                    FlipLever(target, obj, depth + 1);
                    break;
                default:
                    FireObjectEvent(target, obj, BookTriggerConditionBased.ByUseItem);
                    break;
            }
        }
    }

    private void UpdateButtons(double elapsedMs)
    {
        foreach (var button in _buttonResetMs.Keys.ToList())
        {
            if ((_buttonResetMs[button] -= elapsedMs) > 0) continue;
            _buttonResetMs.Remove(button);
            button.IsOpenRuntime = false;
            if (DefinitionOf(button) is { } obj) { PlaySound(obj.Lever.OffSoundResourceId); FireObjectEvent(button, obj, BookTriggerConditionBased.ByEnter); }
            RefreshObjectLook(button);
        }
    }

    // ================= Stairs and ladders =================

    private void UseStairs(ObjectInstance stairs, WorldObject obj)
    {
        if (FireObjectEvent(stairs, obj, BookTriggerConditionBased.ByUseItem)) return;
        if (obj.Stairs.Kind != StairsKind.Ladder) { StatusText.Text = $"Walk up \"{obj.Name}\" a step at a time."; return; }
        if (stairs.DestinationX < 0 || !InsideMap(stairs.DestinationX, stairs.DestinationY)) { StatusText.Text = $"\"{obj.Name}\" leads nowhere (Map Editor: right-click it > Ladder destination)."; return; }
        if (_player is null || IsIncapacitated(_player)) return;
        PlaySound(obj.Stairs.ClimbSoundResourceId);
        TeleportCommand(WhoChoice.Player, stairs.DestinationX, stairs.DestinationY);
        // Arrive at the floor at (or just under) the destination lift - so a ladder can lead down into a room under another floor.
        var foot = PlayerFootprint();
        _playerZ = FloorBelow(WorldBodies().Where(b => b.Solid && b.Overlaps(foot) && b.Entity is not PlacedCharacter), stairs.DestinationLift + 1);
        _playerVisualLiftZ = _playerZ;
        SnapPlayerPosition();
        UpdateCamera();
        StatusText.Text = $"You climb \"{obj.Name}\".";
    }

    /// <summary>A placed object's bodies for the physics: stairs are cut into steps, each a lift higher toward the way they rise; everything else is its one body.</summary>
    private IEnumerable<Body> ObjectBodies(ObjectInstance instance)
    {
        var body = BodyOf(null, instance);
        if (DefinitionOf(instance) is { } designed && StairLook.PoseOf(designed, _objects, instance.Facing) is { } stairPose)
        {
            // A designed staircase (Staircase Designer): every step its own piece, from the staircase's foot up to its top lift - as Exult's stairs are separate step shapes.
            int anchorX = instance.X * UnitsPerTileX + instance.OffsetX, anchorY = instance.Y * UnitsPerTileY + instance.OffsetY;
            foreach (var step in stairPose.Steps)
                yield return step.IsLanding
                    // A landing tile: a floor slab like the floor above's tiles - solid only in its top lift.
                    ? body with { Left = anchorX + step.FootX, Top = anchorY + step.FootY, Width = Math.Max(1, step.SizeX), Depth = Math.Max(1, step.SizeY), Z = body.Z + Math.Max(0, step.TopLift - 1), Height = 1 }
                    : body with { Left = anchorX + step.FootX, Top = anchorY + step.FootY, Width = Math.Max(1, step.SizeX), Depth = Math.Max(1, step.SizeY), Height = Math.Max(1, step.TopLift) };
            yield break;
        }
        if (DefinitionOf(instance) is not { Type: ObjectType.Stairs } obj || obj.Stairs.Kind != StairsKind.Stairs || body.Height <= 1)
        {
            yield return body;
            yield break;
        }
        int steps = body.Height;
        bool alongY = obj.Stairs.RisesToward is SpriteDirection.North or SpriteDirection.South;
        int length = alongY ? body.Depth : body.Width;
        for (int i = 0; i < steps; i++)
        {
            int from = length * i / steps, to = length * (i + 1) / steps;
            if (to <= from) continue; // too short for its height: a step is missing - it can't be climbed (make the footprint longer)
            // Step i (from the bottom) is i + 1 lifts high; the bottom step is at the far side from where it rises toward.
            int start = obj.Stairs.RisesToward switch
            {
                SpriteDirection.North => body.Top + body.Depth - to,
                SpriteDirection.South => body.Top + from,
                SpriteDirection.West => body.Left + body.Width - to,
                _ => body.Left + from
            };
            yield return alongY
                ? body with { Top = start, Depth = to - from, Height = i + 1 }
                : body with { Left = start, Width = to - from, Height = i + 1 };
        }
    }

    // ================= Roofs =================

    private readonly HashSet<UIElement> _hiddenByRoof = new(ReferenceEqualityComparer.Instance);

    /// <summary>The lift of the lowest ceiling over the player, or null if they're out in the open (Exult: is_main_actor_inside).</summary>
    private int? RoofLiftOverPlayer() => _player is null ? null : CeilingOver(PlayerFootprint(), _playerZ);

    /// <summary>
    /// Exult's Map_chunk::is_roof: the lowest solid thing over this footprint,
    /// starting 4 lifts above its feet (get_lowest_blocked(lift + 4)) - an upper
    /// floor, a roof, anything solid. Characters don't count. Null: open sky.
    /// </summary>
    private int? CeilingOver(UnitRect foot, int lift)
    {
        int? lowest = null;
        foreach (var body in WorldBodies())
        {
            if (!body.Solid || body.Entity is PlacedCharacter || body.Z < lift + 4 || !body.Overlaps(foot)) continue;
            lowest = lowest is null ? body.Z : Math.Min(lowest.Value, body.Z);
        }
        return lowest;
    }

    private bool PlayerUnderRoof => RoofLiftOverPlayer() is not null;

    /// <summary>Whether a floor above (its painted tiles or a Floor + Solid tile up there) is what's at this lift over the footprint.</summary>
    private bool IsFloorSlabAt(UnitRect foot, int lift) =>
        WorldBodies().Any(b => b.Z == lift && b.Overlaps(foot) && (b.Entity is FloorSlab || b.Entity is ObjectInstance o && IsUpperFloorSlabTile(o)));

    /// <summary>skip_above: under a roof, everything at its lift or above goes; out in the open, it all comes back.</summary>
    private void UpdateRoofs()
    {
        int? skipAbove = RoofLiftOverPlayer();
        // The ceiling is the floor above (its slab, one lift under that floor's height): what lies at the slab's own lift - a book on a
        // tall table - is still on this floor, under it. Only that floor and up go (everything from its height).
        bool underFloor = skipAbove is int ceilingLift && _player is not null && IsFloorSlabAt(PlayerFootprint(), ceilingLift);
        if (underFloor) skipAbove++;
        var hide = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        if (skipAbove is int lift)
        {
            // At the ceiling's own lift only the solid things go (the ceiling itself, a roof) - something small lying on a table top as high as that stays.
            // A Floor tile is the floor itself: it goes with its floor (gamerend.cc: nothing at skip_lift or above is painted).
            foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0 && (o.Z > lift || (o.Z == lift && (underFloor || IsSolidAt(null, o) || DefinitionOf(o) is { Type: ObjectType.Tile, IsFloorTile: true })))))
                if (_objectVisuals.GetValueOrDefault(instance) is UIElement v) hide.Add(v);
            foreach (var item in _map.Items.Where(i => i.Z > lift || (i.Z == lift && (underFloor || IsSolidAt(i, null)))))
                if (_itemVisuals.GetValueOrDefault(item) is UIElement v) hide.Add(v);
            foreach (var (slabLift, v) in _floorTileVisuals.Where(t => t.SlabLift + 1 >= lift)) hide.Add(v); // the floors overhead: a floor whose height (its slab's top) is at the ceiling's lift or above - under a wall of floor 2, floor 2 goes too
            foreach (var mapCharacter in _mapCharacters.Where(mc => mc.Lift >= lift))
                if (mapCharacter.Visual is { } v) hide.Add(v);
        }
        foreach (var shown in _hiddenByRoof.Where(v => !hide.Contains(v)).ToList()) { shown.Visibility = Visibility.Visible; _hiddenByRoof.Remove(shown); }
        foreach (var hidden in hide.Where(v => !_hiddenByRoof.Contains(v))) { hidden.Visibility = Visibility.Hidden; _hiddenByRoof.Add(hidden); }
    }

    // ================= Explosives =================

    private readonly HashSet<ObjectInstance> _fusesLitByPlayer = new(ReferenceEqualityComparer.Instance);

    private void LightFuse(ObjectInstance keg, WorldObject obj, bool byPlayer, bool forced)
    {
        if (!forced && !obj.Explosive.LitByDoubleClick) { StatusText.Text = $"\"{obj.Name}\" - nothing happens."; return; }
        if (keg.FuseRuntime >= 0) return; // already burning
        keg.FuseRuntime = Math.Max(0.05, obj.Explosive.FuseSeconds);
        keg.IsLitRuntime = true;
        RecordItem("ignited", ReferencedItemCategory.Object, obj.Id); // Ignited explosive
        if (byPlayer) _fusesLitByPlayer.Add(keg);
        PlaySound(obj.Explosive.FuseSoundResourceId);
        RefreshObjectLook(keg);
        if (byPlayer && !forced) StatusText.Text = $"You light the fuse of \"{obj.Name}\"!";
    }

    private void UpdateFuses(double elapsedMs)
    {
        foreach (var keg in _map.Objects.Where(o => o.FuseRuntime >= 0 && o.ContainerInstanceId < 0).ToList())
        {
            keg.FuseRuntime -= elapsedMs / 1000;
            if (keg.FuseRuntime > 0) continue;
            if (DefinitionOf(keg) is { Type: ObjectType.Explosive } obj) Explode(keg, obj, _fusesLitByPlayer.Contains(keg));
            else keg.FuseRuntime = -1;
        }
    }

    /// <summary>The blast: everyone in reach hurt, breakable things struck, other explosives set off.</summary>
    private void Explode(ObjectInstance keg, WorldObject obj, bool byPlayer)
    {
        if (!_map.Objects.Contains(keg)) return;
        var e = obj.Explosive;
        var centre = BodyOf(null, keg);
        double cx = centre.Left + centre.Width / 2.0, cy = centre.Top + centre.Depth / 2.0;
        keg.FuseRuntime = -1;
        _fusesLitByPlayer.Remove(keg);
        FireObjectEvent(keg, obj, BookTriggerConditionBased.ByDestroyItem);
        var owner = byPlayer ? ObjectOwner(obj) : null;
        PlayOneShot(e.ExplosionSprite, VisualOf(keg) as FrameworkElement);
        PlaySound(e.ExplosionSoundResourceId);
        RemoveMapObject(keg);

        bool InReach(double x, double y) => Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) <= e.RadiusTiles;
        int Roll() => e.DamageMin + Rng.Next(Math.Max(0, e.DamageMax - e.DamageMin) + 1);

        foreach (var character in AllLivingCharacters().ToList())
        {
            var (px, py) = PixelOf(character);
            if (!InReach(px / SpriteSizing.BaseUnit, py / SpriteSizing.BaseUnit)) continue;
            Damage(character, Roll(), "an explosion");
            if (byPlayer && character != _player && !IsDead(character) && _player is not null) FightBack(character, _player);
        }
        foreach (var other in _map.Objects.Where(o => o.ContainerInstanceId < 0).ToList())
        {
            if (DefinitionOf(other) is not { } def) continue;
            var b = BodyOf(null, other);
            if (!InReach(b.Left + b.Width / 2.0, b.Top + b.Depth / 2.0)) continue;
            if (def.Type == ObjectType.Explosive) { if (other.FuseRuntime < 0) { other.FuseRuntime = 0.25; other.IsLitRuntime = true; if (byPlayer) _fusesLitByPlayer.Add(other); } continue; }
            if (IsBreakable(def)) StrikeObject(other, def, Roll(), byPlayer: byPlayer);
        }
        SettleWorld();
        RefreshAfterMove();
        StatusText.Text = $"\"{obj.Name}\" explodes!";
        if (owner is { } owned) Theft(owned.Owner, owned.FactionId);
    }

    /// <summary>A sprite played once where something was (a blast), then gone.</summary>
    private void PlayOneShot(SpriteLink link, FrameworkElement? at)
    {
        if (!link.IsSet || ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName) is not { Frames.Count: > 0 } sprite) return;
        var (width, height) = QuantizedSpriteSize(sprite);
        var image = new Image { Width = width, Height = height, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        double left = at is null ? _playerPixelX : Canvas.GetLeft(at) + at.Width / 2;
        double bottom = at is null ? _playerPixelY : Canvas.GetTop(at) + at.Height;
        Canvas.SetLeft(image, left - width / 2.0);
        Canvas.SetTop(image, bottom - height);
        Panel.SetZIndex(image, Math.Max(FloorBand(_playerZ) + (_map.Width + _map.Height) * 100 + 400, Panel.GetZIndex(_playerImage) + 1)); // over everything on the player's floor
        GameCanvas.Children.Add(image);
        var animator = new SpriteAnimator(sprite, image, _resources);
        RegisterWorldAnimator(image, animator);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(sprite.Frames.Count * 1000.0 / Math.Max(1, sprite.Fps)) };
        timer.Tick += (_, _) => { timer.Stop(); UnregisterWorldAnimator(image); GameCanvas.Children.Remove(image); };
        timer.Start();
    }

    // ================= Glass =================

    /// <summary>Glass that fell (its support gone) or was dropped from high enough shatters - noted during gravity, broken after it.</summary>
    private readonly List<ObjectInstance> _glassToShatter = new();

    private void NoteGlassFall(ObjectInstance instance, int fromZ, int toZ)
    {
        if (DefinitionOf(instance) is { Type: ObjectType.BreakableGlass } glass && glass.Glass.BreaksFromLifts > 0 && fromZ - toZ >= glass.Glass.BreaksFromLifts)
            _glassToShatter.Add(instance);
    }

    private void ShatterFallenGlass()
    {
        if (_glassToShatter.Count == 0) return;
        var falling = _glassToShatter.ToList();
        _glassToShatter.Clear();
        foreach (var glass in falling)
            if (_map.Objects.Contains(glass) && DefinitionOf(glass) is { } obj) BreakObject(glass, obj, crimeOwner: null, attacker: null);
    }

    /// <summary>
    /// Destroyed (Exult's DestroyObjectsUsecode): its sound and By Destroyed
    /// event, then - a window with a Broken sprite - it stays, broken;
    /// otherwise it's gone, leaving its shards if it has any.
    /// </summary>
    private void BreakObject(ObjectInstance instance, WorldObject obj, (CharacterDefinition? Owner, int FactionId)? crimeOwner, MapCharacter? attacker)
    {
        PlaySound(obj.DestroyedSoundResourceId);
        FireObjectEvent(instance, obj, BookTriggerConditionBased.ByDestroyItem);
        if (obj.Type == ObjectType.Window && obj.Window.BrokenSprite.IsSet)
        {
            instance.IsSpentRuntime = true;
            RefreshObjectLook(instance);
            StatusText.Text = $"The glass of \"{obj.Name}\" breaks!";
        }
        else
        {
            var (x, y) = (instance.X, instance.Y);
            RemoveMapObject(instance);
            if (obj.Type == ObjectType.BreakableGlass && obj.Glass.ShardsObjectId >= 0) SpawnObjectAt(obj.Glass.ShardsObjectId, 1, x, y);
            SettleWorld();
            RefreshAfterMove();
            StatusText.Text = obj.Type == ObjectType.BreakableGlass ? $"\"{obj.Name}\" shatters!"
                : attacker is null ? $"\"{obj.Name}\" breaks apart." : $"{attacker.Character.Name} breaks \"{obj.Name}\" down!";
        }
        if (crimeOwner is { } owned) Theft(owned.Owner, owned.FactionId);
    }
}
