using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Object Editor objects in play: their live state, their look for it, and
/// what using them does - ported from Exult where Exult has the mechanic:
///  - Doors (actions.cc / paths.cc / shape_info.txt): double-click opens and
///    closes; locked: "Locked" (keys and lockpicks work on them as on
///    chests). Solid only while closed. Breakable only with no key and not
///    magically locked (Exult's effective_hps: doors 6 HP at quality 0).
///    NPCs plan their paths through closed unlocked doors, open them when
///    they reach them, and close them behind them; locked doors stop them.
///  - Light sources: double-click lights and puts out; they burn for their
///    minutes, then are "Spent ..." and can't be lit again. Their light is
///    GameTestWindow.Lighting.cs.
///  - Ammo and money: stacks of up to 100 drawn by their bundle picture for
///    how many there are (Exult's quantity frames), combining when dropped
///    on the same kind (Game_object::drop).
///  - Junk, tiles, traps: their Event tab's By Use fires on a double-click.
///  - Anything with hit points (and not "Cannot be destroyed") can be broken
///    in combat mode; owned things broken in sight of their owner's side are
///    a crime.
/// </summary>
public partial class GameTestWindow
{
    private WorldObject? DefinitionOf(ObjectInstance instance) => ObjectDef(instance.ObjectId);
    private WorldObject? DefinitionOf(InventoryItemInstance item) => item.Category == ReferencedItemCategory.Object ? ObjectDef(item.ItemId) : null;

    // ================= State =================

    /// <summary>At the start of play: every placed object takes its starting state (Map Editor overrides, else the Object Editor's).</summary>
    private void InitializeObjectStates()
    {
        foreach (var instance in _map.Objects.ToList())
        {
            if (DefinitionOf(instance) is not { } obj) continue;
            var start = ObjectLooks.StartState(obj, instance);
            instance.IsOpenRuntime ??= start.Open;
            instance.IsLockedRuntime ??= start.Locked;
            instance.IsLitRuntime ??= start.Lit;
            if (obj.Type == ObjectType.LightSource && obj.Light.BurnMinutes > 0 && instance.BurnLeftRuntime < 0) instance.BurnLeftRuntime = obj.Light.BurnMinutes;
            if (obj.HitPoints > 0 && instance.HitPointsRuntime < 0) instance.HitPointsRuntime = obj.HitPoints;
        }
        InitializeNewTypeStates();
        SpawnBedCovers();
    }

    private static ObjectState StateOf(ObjectInstance instance) =>
        new(instance.IsOpenRuntime ?? false, instance.IsLockedRuntime ?? false, instance.IsLitRuntime ?? false, instance.IsSpentRuntime, Math.Max(1, instance.Quantity), instance.Facing,
            Stage: instance.FrameRuntime, LiquidId: instance.LiquidIdRuntime ?? -1);

    private static ObjectState StateOf(InventoryItemInstance item) =>
        new(false, false, item.IsLitRuntime ?? false, item.IsSpentRuntime, Math.Max(1, item.Quantity), item.FacingRuntime,
            Stage: item.FrameRuntime, LiquidId: item.LiquidIdRuntime ?? -1);

    /// <summary>The name it goes by now: "Spent ..." once burnt out, "12 Arrows" for a stack.</summary>
    private static string ObjectDisplayName(WorldObject obj, bool spent, int quantity) =>
        quantity > 1 ? $"{quantity} {obj.DisplayName(spent)}" : obj.DisplayName(spent);

    // ================= Pictures =================

    /// <summary>A placed object's picture for its current state (animated unless its frames are states).</summary>
    private FrameworkElement BuildObjectVisual(ObjectInstance instance, List<SpriteAnimator>? animators)
    {
        var obj = DefinitionOf(instance);
        if (obj is not null && StairLook.PoseOf(obj, _objects, instance.Facing) is { } stairPose) // a designed staircase: every step its own (animated) picture
            return StairLook.Visual(stairPose, _sprites, _resources, _tileWidth, _tileHeight, animators, landings: false); // landing tiles: drawn with their floor (BuildLandingVisuals)
        var overridden = instance.LookOverrideRuntime?.Split('/', 2) is [var lookGroup, var lookName] ? _sprites.Groups.FirstOrDefault(g => g.Name == lookGroup)?.Sprites.FirstOrDefault(sp => sp.Name == lookName) : null; // Change Object Look
        if (obj is not null && (overridden ?? ObjectLooks.SpriteFor(obj, StateOf(instance), _sprites)) is { } sprite && (sprite.Frames.Count > 0 || File.Exists(sprite.FilePath)))
        {
            var (width, height) = QuantizedSpriteSize(sprite);
            var image = new Image { Width = width, Height = height, IsHitTestVisible = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            if (obj.FramesAreStates && sprite.Frames.Count > 0)
                image.Source = LoadStaticFrame(sprite.Frames[Math.Clamp(instance.FrameRuntime, 0, sprite.Frames.Count - 1)].FilePath);
            else
                animators?.Add(new SpriteAnimator(sprite, image, _resources,
                    obj.Type is ObjectType.Chair or ObjectType.Bed ? (obj.Furniture.Muted ? 0 : obj.Furniture.Volume / 100.0) : null)); // a chair's or bed's own mute / volume
            return image;
        }
        if (obj is not null && !string.IsNullOrEmpty(obj.IconPath) && File.Exists(obj.IconPath) && LoadStaticFrame(obj.IconPath) is { } bitmap)
        {
            var (width, height) = SpriteSizing.Quantize(bitmap.PixelWidth, bitmap.PixelHeight);
            return new Image { Source = bitmap, Width = width, Height = height, IsHitTestVisible = true };
        }
        return new Border
        {
            Width = _tileWidth, Height = _tileHeight, Background = Brushes.DarkSlateGray, IsHitTestVisible = true,
            Child = new TextBlock { Text = "O", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    /// <summary>A carried object's picture in a paperdoll slot or container window, scaled to match the world.</summary>
    private FrameworkElement? BuildCarriedObjectPicture(InventoryItemInstance item, double counterScale, List<SpriteAnimator> animators)
    {
        if (DefinitionOf(item) is not { } obj) return null;
        if (ObjectLooks.SpriteFor(obj, StateOf(item), _sprites) is { Frames.Count: > 0 } sprite)
        {
            var first = LoadStaticFrame(sprite.Frames[0].FilePath);
            var (width, height) = first is not null ? SpriteSizing.Quantize(first.PixelWidth, first.PixelHeight) : (32, 32);
            var image = new AlphaHitImage { Width = width * counterScale, Height = height * counterScale, Stretch = Stretch.Uniform };
            if (obj.FramesAreStates) image.Source = LoadStaticFrame(sprite.Frames[Math.Clamp(item.FrameRuntime, 0, sprite.Frames.Count - 1)].FilePath);
            else animators.Add(new SpriteAnimator(sprite, image, _resources));
            return image;
        }
        if (!string.IsNullOrEmpty(obj.IconPath) && LoadStaticFrame(obj.IconPath) is { } bitmap)
        {
            var (width, height) = SpriteSizing.Quantize(bitmap.PixelWidth, bitmap.PixelHeight);
            return new AlphaHitImage { Source = bitmap, Width = width * counterScale, Height = height * counterScale, Stretch = Stretch.Uniform };
        }
        return null;
    }

    /// <summary>Draws every placed object at the start of play.</summary>
    private void BuildObjectVisuals()
    {
        foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            var animators = new List<SpriteAnimator>();
            var visual = BuildObjectVisual(instance, animators);
            foreach (var animator in animators) RegisterWorldAnimator(visual, animator);
            ApplyZToVisual(visual, instance.X, instance.Y, instance.Z, instance.OffsetX, instance.OffsetY);
            ShiftOpenDoor(instance, visual);
            GameCanvas.Children.Add(visual);
            _objectVisuals[instance] = visual;
        }
    }

    /// <summary>An open door is drawn at its open position (Object Editor), hanging from its hinge.</summary>
    private void ShiftOpenDoor(ObjectInstance instance, FrameworkElement visual)
    {
        if (!IsDoorOpen(instance) || DefinitionOf(instance) is not { Type: ObjectType.Door } door) return;
        Canvas.SetLeft(visual, Canvas.GetLeft(visual) + door.Door.OpenOffsetX);
        Canvas.SetTop(visual, Canvas.GetTop(visual) + door.Door.OpenOffsetY);
    }

    /// <summary>After an object's state changes (a door opens, a light goes out, a stack grows): its picture is rebuilt for the new state.</summary>
    private void RefreshObjectLook(ObjectInstance instance)
    {
        WorldChanged(); // kept bodies are made again (GameTestWindow.Perf.cs)
        if (!_objectVisuals.TryGetValue(instance, out var old)) return;
        UnregisterWorldAnimator(old);
        GameCanvas.Children.Remove(old);
        var animators = new List<SpriteAnimator>();
        var visual = BuildObjectVisual(instance, animators);
        foreach (var animator in animators) RegisterWorldAnimator(visual, animator);
        GameCanvas.Children.Add(visual);
        _objectVisuals[instance] = visual;
        RestackAround(instance, old, visual);
    }

    /// <summary>
    /// A thing's new look in its place: drawn where it was and in the same
    /// order (over what it rests on, as before) - and for a door, wall or
    /// window whose picture moved or changed size, the things drawn near it
    /// ordered again among the walls. Not the whole map's order again (that
    /// cost nearly a second on a big map, every time a door opened).
    /// </summary>
    private void RestackAround(ObjectInstance instance, UIElement oldVisual, UIElement newVisual)
    {
        WorldChanged();
        if (newVisual is not FrameworkElement fe) { RefreshStackingVisuals(); return; }
        var before = ScreenRectOf(oldVisual);
        int z = Panel.GetZIndex(oldVisual);
        ApplyZToVisual(fe, instance.X, instance.Y, instance.Z, instance.OffsetX, instance.OffsetY);
        ShiftOpenDoor(instance, fe); // hangs from its hinge
        Panel.SetZIndex(fe, z); // the order it had: over what it rests on
        if (DefinitionOf(instance)?.Type is not (ObjectType.Wall or ObjectType.Door or ObjectType.Window)) return;
        var area = before;
        area.Union(ScreenRectOf(fe));
        area.Inflate(8, 8);
        foreach (var (item, itemVisual) in _itemVisuals)
            if (ScreenRectOf(itemVisual).IntersectsWith(area))
            {
                var body = BodyOf(item, null);
                Panel.SetZIndex(itemVisual, OrderAmongWalls(new UnitRect(body.Left, body.Top, body.Width, body.Depth), body.Z, body.Height, ScreenRectOf(itemVisual), Panel.GetZIndex(itemVisual)));
            }
        foreach (var (obj, objVisual) in _objectVisuals)
            if (!ReferenceEquals(obj, instance) && ScreenRectOf(objVisual).IntersectsWith(area) && DefinitionOf(obj)?.Type is not (ObjectType.Wall or ObjectType.Door or ObjectType.Window or ObjectType.Roof))
            {
                var body = BodyOf(null, obj);
                Panel.SetZIndex(objVisual, OrderAmongWalls(new UnitRect(body.Left, body.Top, body.Width, body.Depth), body.Z, body.Height, ScreenRectOf(objVisual), Panel.GetZIndex(objVisual)));
            }
        foreach (var mapCharacter in _mapCharacters)
            if (mapCharacter.Visual is { } cv && ScreenRectOf(cv).IntersectsWith(area)) PositionCharacterVisual(mapCharacter);
    }

    // ================= Identify and use =================

    /// <summary>The step of a designed staircase last clicked: identified, a step goes by its own name (the bottom one by the staircase's).</summary>
    private (ObjectInstance Stairs, int Step)? _clickedStairStep;

    private void RememberStairStep(ObjectInstance instance, DependencyObject? hit, UIElement visual) =>
        _clickedStairStep = hit is FrameworkElement { Tag: int step } picture && ReferenceEquals(VisualTreeHelper.GetParent(picture), visual) ? (instance, step) : null;

    /// <summary>Single click (identify) and double click (use) on a placed object.</summary>
    private void HandleObjectClick(ObjectInstance instance, bool isUse)
    {
        var obj = DefinitionOf(instance);
        string name = obj is null ? $"Object #{instance.ObjectId}" : ObjectDisplayName(obj, instance.IsSpentRuntime, instance.Quantity);
        if (obj is { Type: ObjectType.LiquidContainer } && LiquidFood(instance.LiquidIdRuntime ?? -1) is { } liquid) name = $"{name} of {liquid.Name.ToLowerInvariant()}"; // a bucket of water
        if (obj is not null && _clickedStairStep is { } clicked && ReferenceEquals(clicked.Stairs, instance)
            && StairLook.PoseOf(obj, _objects, instance.Facing) is { } stairPose && clicked.Step < stairPose.Steps.Count)
            name = StairDesign.StepName(stairPose.Steps[clicked.Step], clicked.Step, name); // a designed staircase: each step goes by its own name
        if (!isUse)
        {
            ShowIdentifyLabel(name, instance.X, instance.Y, _objectVisuals.GetValueOrDefault(instance) as FrameworkElement);
            return;
        }
        if (obj is null) return;
        if (TryUseLockTool(obj, new MapObjectOrigin(instance))) return;
        if (TryUseVehiclePart(instance, obj)) return; // a vehicle's controls, its driver's seat (GameTestWindow.Vehicles.cs)
        if (obj.Type == ObjectType.Spellbook && _player is not null && RefusedSpellbook(obj, _player)) return; // not everyone can read it (GameTestWindow.Magic.cs)
        if (obj.Type == ObjectType.Spellbook && _player is not null) _spellbookReader = _player;
        if (TryUseDoll(instance, obj, () => FireObjectEvent(instance, obj, BookTriggerConditionBased.ByUseItem))) return; // it opens a paperdoll
        // A bed's cover lies over the bed: double-clicking it means the bed (unless the cover has events of its own).
        if (!obj.EventPages.Any(p => p.Commands.Count > 0) && BedUnderCover(instance) is { } coveredBed) { LieDownOn(coveredBed); return; }
        switch (obj.Type)
        {
            case ObjectType.Door: UseDoor(instance, obj); return;
            case ObjectType.LightSource: UseLight(instance, obj); return;
            case ObjectType.Chair: if (!FireObjectEvent(instance, obj, BookTriggerConditionBased.ByUseItem)) SitOn(instance, obj); return;
            case ObjectType.Bed: if (!FireObjectEvent(instance, obj, BookTriggerConditionBased.ByUseItem)) LieOn(instance, obj); return;
        }
        if (UseNewTypeOnMap(instance, obj)) return; // windows, plants, levers, food, potions... (GameTestWindow.Types.cs)
        if (!FireObjectEvent(instance, obj, BookTriggerConditionBased.ByUseItem))
            StatusText.Text = $"\"{name}\" - nothing happens."; // Exult: no usecode, nothing
    }

    /// <summary>Double-clicking a carried object (paperdoll, backpack list, container window): keys and lockpicks, lights (a carried torch), and By Use events. True = handled.</summary>
    private bool UseCarriedObject(InventoryItemInstance item, DragOrigin source)
    {
        if (DefinitionOf(item) is not { } obj) return false;
        RecordItem("used", ReferencedItemCategory.Object, obj.Id); // Used object (GameTestWindow.Conditions.cs)
        if (TryUseLockTool(obj, source)) return true;
        if (obj.Type == ObjectType.Spellbook)
        {
            var reader = source switch { SlotOrigin s => s.Character, BackpackOrigin b => b.Character, _ => _player };
            if (reader is not null && RefusedSpellbook(obj, reader)) return true;
            _spellbookReader = reader;
        }
        if (TryUseDoll(item, obj, () => TryFireEventPages(item, obj.EventPages, BookTriggerConditionBased.ByUseItem, -1, $"You use \"{obj.Name}\" - event fired."))) return true; // it opens a paperdoll
        if (obj.Type == ObjectType.LightSource)
        {
            // Lit in a hand or set down - not inside a container, the pack or a paperdoll.
            if (source is ContainerOrigin or BackpackOrigin or DollItemOrigin && item.IsLitRuntime != true)
            {
                StatusText.Text = $"Take \"{obj.Name}\" out first - hold it in a hand, or set it down, to light it.";
                return true;
            }
            UseCarriedLight(item, obj);
            return true;
        }
        if (UseNewTypeCarried(item, obj, source)) return true; // food, potions, keychains, maps, spellbooks... (GameTestWindow.Types.cs)
        if (TryFireEventPages(item, obj.EventPages, BookTriggerConditionBased.ByUseItem, -1, $"You use \"{obj.Name}\" - event fired.")) return true;
        StatusText.Text = $"\"{ObjectDisplayName(obj, item.IsSpentRuntime, item.Quantity)}\" - nothing happens.";
        return true;
    }

    /// <summary>Runs an object's own event pages for a trigger (per placement, as every prefab copy is its own). True if a page with commands ran.</summary>
    private bool FireObjectEvent(ObjectInstance instance, WorldObject obj, BookTriggerConditionBased trigger, MapCharacter? activator = null)
    {
        RecordObjectHappening(obj, trigger); // destroyed, used, lit, put out, burnt out (GameTestWindow.Conditions.cs)
        if (obj.EventPages.Count == 0) return false;
        var (previous, previousStep) = (_activator, _activatedByCharacterStep);
        _activator = activator;
        _activatedByCharacterStep = activator is not null;
        try { return TryFireEventPages(instance, obj.EventPages, trigger, selfBookId: -1, $"\"{obj.Name}\" fired ({trigger})"); }
        finally { (_activator, _activatedByCharacterStep) = (previous, previousStep); }
    }

    // ================= Doors =================

    private bool IsDoorOpen(ObjectInstance door) => door.IsOpenRuntime == true;
    private bool IsDoorLocked(ObjectInstance door) => door.IsLockedRuntime == true;

    /// <summary>Double-clicking a door: locked says "Locked"; otherwise it opens or closes (its By Open / Close event first).</summary>
    private void UseDoor(ObjectInstance door, WorldObject obj)
    {
        if (FireObjectEvent(door, obj, BookTriggerConditionBased.ByUseItem)) return;
        if (!IsDoorOpen(door) && IsDoorLocked(door))
        {
            PlaySoundAt(obj.Door.LockedRattleSoundResourceId, door);
            ShowFloatingTextOver(VisualOf(door), "Locked");
            StatusText.Text = obj.Door.MagicallyLocked ? $"\"{obj.Name}\" is magically locked." : $"\"{obj.Name}\" is locked.";
            return;
        }
        if (IsDoorOpen(door)) TryCloseDoor(door, obj, null);
        else OpenDoor(door, obj, null);
    }

    private void OpenDoor(ObjectInstance door, WorldObject obj, MapCharacter? by)
    {
        door.IsOpenRuntime = true;
        PlaySoundAt(obj.Door.OpenSoundResourceId, door);
        RefreshObjectLook(door);
        if (by is null) StatusText.Text = $"You open \"{obj.Name}\".";
        FireObjectEvent(door, obj, BookTriggerConditionBased.ByAfterReadItem, by);
    }

    /// <summary>Closes it - unless someone is standing in the doorway.</summary>
    private bool TryCloseDoor(ObjectInstance door, WorldObject obj, MapCharacter? by)
    {
        var foot = BodyOf(null, door);
        var doorway = new UnitRect(foot.Left, foot.Top, foot.Width, foot.Depth);
        bool someoneThere = (PlayerBody() is { } player && player.Overlaps(doorway))
            || CharacterBodies().Any(b => b.Overlaps(doorway) && (by is null || !ReferenceEquals(b.Entity, by.Placement)));
        if (someoneThere)
        {
            if (by is null) Refuse(MousePointerKind.Blocked, $"Someone is in the way of \"{obj.Name}\".");
            return false;
        }
        door.IsOpenRuntime = false;
        PlaySoundAt(obj.Door.CloseSoundResourceId, door);
        RefreshObjectLook(door);
        if (by is null) StatusText.Text = $"You close \"{obj.Name}\".";
        FireObjectEvent(door, obj, BookTriggerConditionBased.ByEnter, by);
        return true;
    }

    /// <summary>keyring.uc UseKeyOnDoor: the right key locks or unlocks it (an open door is closed first); the wrong one doesn't fit; a magic lock takes no key.</summary>
    private void UseKeyOnDoor(WorldObject key, ObjectInstance door, WorldObject obj)
    {
        var d = obj.Door;
        if (d.MagicallyLocked || d.RequiredKeyItemId != key.Id)
        {
            StatusText.Text = $"\"{key.Name}\" doesn't fit \"{obj.Name}\".";
            FlashPointer(MousePointerKind.Blocked, playSound: d.WrongKeySoundResourceId < 0);
            PlaySoundAt(d.WrongKeySoundResourceId, door);
            PartyBark(VerboseKind.WrongKey);
            return;
        }
        if (IsDoorLocked(door))
        {
            door.IsLockedRuntime = false;
            PlaySoundAt(d.UnlockSoundResourceId >= 0 ? d.UnlockSoundResourceId : d.LockSoundResourceId, door);
            ShowFloatingTextOver(VisualOf(door), "Unlocked");
            StatusText.Text = $"You unlock \"{obj.Name}\".";
            RefreshObjectLook(door);
            RecordItem("unlockkey", ReferencedItemCategory.Object, obj.Id);
            FireObjectEvent(door, obj, BookTriggerConditionBased.ByStay);
            return;
        }
        if (IsDoorOpen(door) && !TryCloseDoor(door, obj, null)) return; // can't lock it open
        door.IsLockedRuntime = true;
        PlaySoundAt(d.LockSoundResourceId >= 0 ? d.LockSoundResourceId : d.UnlockSoundResourceId, door);
        ShowFloatingTextOver(VisualOf(door), "Locked");
        StatusText.Text = $"You lock \"{obj.Name}\".";
        RefreshObjectLook(door);
    }

    /// <summary>A lockpick on a locked door: Dexterity vs Pick-lock difficulty (as for chests); a failure breaks it. Magic locks can't be picked.</summary>
    private void UseLockpickOnDoor(WorldObject lockpick, DragOrigin source, ObjectInstance door, WorldObject obj)
    {
        var d = obj.Door;
        if (!IsDoorLocked(door)) { StatusText.Text = $"\"{obj.Name}\" isn't locked."; return; }
        if (d.MagicallyLocked) { StatusText.Text = $"\"{obj.Name}\" is magically locked - no lockpick opens it."; return; }
        int dexterity = _player is null ? 0 : Stat(_player, BuiltInStatKind.Dexterity, 10);
        int chance = Math.Clamp((dexterity - d.PickLockDifficulty) * 5 + 50, 5, 95);
        if (Rng.Next(100) < chance)
        {
            door.IsLockedRuntime = false;
            PlaySoundAt(d.UnlockSoundResourceId >= 0 ? d.UnlockSoundResourceId : d.LockSoundResourceId, door);
            ShowFloatingTextOver(VisualOf(door), "Unlocked");
            StatusText.Text = $"You pick the lock on \"{obj.Name}\". ({chance}% chance)";
            RecordItem("picklocked", ReferencedItemCategory.Object, obj.Id);
            RefreshObjectLook(door);
            FireObjectEvent(door, obj, BookTriggerConditionBased.ByStay);
            return;
        }
        UseUpOne(source);
        PlaySoundAt(d.LockpickBrokeSoundResourceId, door);
        if (_player is not null) Bark(_player, VerboseKind.LockpickFailed);
        StatusText.Text = $"You fail to pick the lock on \"{obj.Name}\", and \"{lockpick.Name}\" breaks. ({chance}% chance)";
        FireObjectEvent(door, obj, BookTriggerConditionBased.ByExit);
    }

    /// <summary>Exult (actions.cc open_door / paths.cc): a closed, unlocked door the character is next to and wants to pass.</summary>
    private ObjectInstance? ClosedUnlockedDoorIn(UnitRect foot)
    {
        foreach (var instance in _map.Objects)
        {
            if (DefinitionOf(instance) is not { Type: ObjectType.Door } || IsDoorOpen(instance) || IsDoorLocked(instance)) continue;
            if (BodyOf(null, instance).Overlaps(foot)) return instance;
        }
        return null;
    }

    // ================= Light sources =================

    private void UseLight(ObjectInstance light, WorldObject obj)
    {
        if (FireObjectEvent(light, obj, BookTriggerConditionBased.ByUseItem)) return;
        if (light.IsSpentRuntime) { StatusText.Text = $"The {obj.DisplayName(spent: true).ToLowerInvariant()} can't be lit."; return; }
        bool lit = !(light.IsLitRuntime ?? false);
        light.IsLitRuntime = lit;
        if (lit && obj.Light.BurnMinutes > 0 && light.BurnLeftRuntime < 0) light.BurnLeftRuntime = obj.Light.BurnMinutes;
        PlaySoundAt(lit ? obj.Light.LitSoundResourceId : obj.Light.OffSoundResourceId, light);
        RefreshObjectLook(light);
        StatusText.Text = lit ? $"You light \"{obj.Name}\"." : $"You put out \"{obj.Name}\".";
        FireObjectEvent(light, obj, lit ? BookTriggerConditionBased.ByAfterReadItem : BookTriggerConditionBased.ByEnter);
    }

    private void UseCarriedLight(InventoryItemInstance item, WorldObject obj)
    {
        if (TryFireEventPages(item, obj.EventPages, BookTriggerConditionBased.ByUseItem, -1, $"\"{obj.Name}\" fired")) return;
        if (item.IsSpentRuntime) { StatusText.Text = $"The {obj.DisplayName(spent: true).ToLowerInvariant()} can't be lit."; return; }
        bool lit = !(item.IsLitRuntime ?? false);
        item.IsLitRuntime = lit;
        if (lit && obj.Light.BurnMinutes > 0 && item.BurnLeftRuntime < 0) item.BurnLeftRuntime = obj.Light.BurnMinutes;
        PlaySound(lit ? obj.Light.LitSoundResourceId : obj.Light.OffSoundResourceId);
        StatusText.Text = lit ? $"You light \"{obj.Name}\"." : $"You put out \"{obj.Name}\".";
        RefreshAfterMove(); // its picture in the gump
    }

    /// <summary>Every lit light, placed or carried, burns down with the game clock; burnt out, it's spent.</summary>
    private void BurnLights(double gameMinutes)
    {
        if (gameMinutes <= 0) return;
        foreach (var instance in _map.Objects.ToList())
        {
            if (instance.IsLitRuntime != true || instance.BurnLeftRuntime < 0 || DefinitionOf(instance) is not { Type: ObjectType.LightSource } obj) continue;
            instance.BurnLeftRuntime -= gameMinutes;
            if (instance.BurnLeftRuntime > 0) continue;
            instance.IsLitRuntime = false;
            instance.IsSpentRuntime = true;
            PlaySoundAt(obj.Light.SpentSoundResourceId, instance);
            RefreshObjectLook(instance);
            FireObjectEvent(instance, obj, BookTriggerConditionBased.ByStay);
        }
        bool changed = false;
        foreach (var item in AllCarriedItems())
        {
            if (item.IsLitRuntime != true || item.BurnLeftRuntime < 0 || DefinitionOf(item) is not { Type: ObjectType.LightSource } obj) continue;
            item.BurnLeftRuntime -= gameMinutes;
            if (item.BurnLeftRuntime > 0) continue;
            item.IsLitRuntime = false;
            item.IsSpentRuntime = true;
            PlaySound(obj.Light.SpentSoundResourceId);
            changed = true;
            if (_player is not null && _player.Inventory.Any(i => ReferenceEquals(i, item) || ItemWeights.Holds(i.Contents, item)))
                StatusText.Text = $"Your {obj.Name.ToLowerInvariant()} has burnt out.";
        }
        if (changed) RefreshAfterMove();
    }

    /// <summary>Every item every character carries, at any depth.</summary>
    private IEnumerable<InventoryItemInstance> AllCarriedItems()
    {
        IEnumerable<InventoryItemInstance> Walk(IEnumerable<InventoryItemInstance> items)
        {
            foreach (var item in items)
            {
                yield return item;
                foreach (var inner in Walk(item.Contents)) yield return inner;
            }
        }
        return _characters is null ? Enumerable.Empty<InventoryItemInstance>() : _characters.Characters.SelectMany(c => Walk(c.Inventory)).ToList();
    }

    // ================= Durability =================

    /// <summary>Whether blows can hurt it (Exult effective_hps): hit points, not "Cannot be destroyed", and - a door - no key and not magically locked.</summary>
    private static bool IsBreakable(WorldObject obj) =>
        obj.HitPoints > 0 && !obj.CannotBeDestroyed && ObjectTypes.CanBeBroken(obj.Type) // plants, food, potions: never broken
        && !(obj.Type == ObjectType.Door && (obj.Door.RequiredKeyItemId >= 0 || obj.Door.MagicallyLocked));

    /// <summary>A blow at a placed object (Game_object::reduce_health). <paramref name="byPlayer"/> false: nobody's doing (an explosion nobody lit) - no crime.</summary>
    private void StrikeObject(ObjectInstance instance, WorldObject obj, int damage, MapCharacter? attacker = null, bool byPlayer = true)
    {
        var visual = VisualOf(instance);
        if (obj.Type == ObjectType.Explosive && obj.Explosive.ExplodesWhenStruck) { Explode(instance, obj, attacker is null && byPlayer); return; } // a struck powder keg goes off
        if (obj.Type == ObjectType.Window && instance.IsSpentRuntime) { ShowFloatingTextOver(visual, "no effect"); return; } // already broken
        if (!IsBreakable(obj)) { ShowFloatingTextOver(visual, "no effect"); return; }
        if (obj.Defense > 0) damage -= 1 + Rng.Next(obj.Defense);
        if (damage <= 0) { ShowFloatingTextOver(visual, "no damage"); return; }
        if (instance.HitPointsRuntime < 0) instance.HitPointsRuntime = obj.HitPoints;
        instance.HitPointsRuntime -= damage;
        if (ShowHitNumbers) ShowFloatingTextOver(visual, $"-{damage}");
        var owner = attacker is null && byPlayer ? ObjectOwner(obj) : null; // only the player's blows are a crime here
        if (owner is { } owned && _struckOwnedObjects.Add(instance)) Theft(owned.Owner, owned.FactionId);
        if (instance.HitPointsRuntime > 0) return;

        // Broken: gone from the world (Exult's DestroyObjectsUsecode) - or, a window with a Broken sprite, broken where it is.
        _playerTargetThing = null;
        BreakObject(instance, obj, owner, attacker);
    }

    private readonly HashSet<ObjectInstance> _struckOwnedObjects = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whose a placed object is now: its own owner in play (a dish served to a customer), else as its Object Editor says.</summary>
    private (CharacterDefinition? Owner, int FactionId)? ObjectOwner(WorldObject obj, ObjectInstance? instance)
    {
        int ownerId = instance is null ? -1 : instance.OwnerIdRuntime >= 0 ? instance.OwnerIdRuntime : instance.BelongsToId;
        if (ownerId < 0 || _player is null) return ObjectOwner(obj);
        var owner = _characters?.Characters.FirstOrDefault(c => c.Id == ownerId);
        if (owner is null || owner == _player || owner.IsInParty) return null;
        return (owner, owner.FactionId);
    }

    /// <summary>Whose an object is (Belong To / Faction), unless it's the party's own or nobody's.</summary>
    private (CharacterDefinition? Owner, int FactionId)? ObjectOwner(WorldObject obj)
    {
        if (_player is null) return null;
        var owner = obj.BelongToId >= 0 ? _characters?.Characters.FirstOrDefault(c => c.Id == obj.BelongToId) : null;
        if (owner == _player || owner?.IsInParty == true) return null;
        if (owner is null && (obj.FactionId < 0 || obj.FactionId == _player.FactionId)) return null;
        return (owner, owner?.FactionId >= 0 ? owner.FactionId : obj.FactionId);
    }

    // ================= Bundles (ammo, money, stackables) =================

    /// <summary>Exult Game_object::drop: the same kind of stackable thing dropped on a stack joins it, up to 100. True = it all fitted (the dropped thing is used up).</summary>
    private static bool CanCombine(WorldObject? obj) => obj is not null && (ObjectTypes.IsBundle(obj.Type) || obj.IsStackable);

    /// <summary>Into a list of contents: joins a stack of the same object if the total stays within 100. True if joined.</summary>
    private bool TryCombineIntoContents(List<InventoryItemInstance> contents, ReferencedItemCategory category, int itemId, int quantity)
    {
        if (category != ReferencedItemCategory.Object || !CanCombine(ObjectDef(itemId))) return false;
        var stack = contents.FirstOrDefault(i => i.Category == category && i.ItemId == itemId && i.Quantity + quantity <= ObjectTypes.MaxQuantity);
        if (stack is null) return false;
        stack.Quantity += quantity;
        return true;
    }
}
