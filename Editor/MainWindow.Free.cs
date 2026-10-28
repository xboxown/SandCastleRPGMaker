using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map Editor: Free Placement (Mode > Free Placement) - objects, books and
/// containers put down anywhere, not tied to the tile grid: in 8px steps,
/// Exult's own unit (each thing's OffsetX/OffsetY), which is also what the
/// game's collision, stacking and reach work in.
///
/// - Click: the thing picked in the Objects palette (Books and Containers
///   have their own tabs) goes down centred on the mouse. Hold and drag to
///   paint: another one goes down each time the brush has moved clear of the
///   last one, like a stamp brush.
/// - Stacking is the game's own (GameTestWindow.Physics.cs SettleAll): what
///   allows elevation rests on top of whatever elevating thing is under it;
///   anything put over a table (or a floor) lies on its top. The editor
///   stores the resulting lifts, and draws things raised by them (4px up and
///   left per lift), so a pile looks here as it will in the game. As in the
///   game's drop (drag.cc drop_on_map), the spot under the mouse is tried
///   at each lift from the top down, so letting go over a table top puts the
///   thing on the top where the picture was.
/// - Press a placed thing and drag it to move it (Ctrl+click places on top
///   of it instead). Right-click: its starting state, Put on top of the
///   pile, Put underneath, Cut / Copy / Paste / Delete.
/// - Ctrl + double-click a container (in any mode): edit what THIS one holds.
/// </summary>
public partial class MainWindow
{
    private const int CharacterZIndex = 200_000;
    private const int SelectionZIndex = 300_000;
    private const int MarkerZIndex = 400_000;
    /// <summary>Highest lift above a floor a pile is looked at when placing (Exult lets the avatar drop 5 lifts up; piles in the editor may be taller).</summary>
    private const int MaxPileLifts = 40;

    private (ReferencedItemCategory Category, int Id)? _freeItemPick;
    private object? _freeSelected;
    private object? _freeDragging;
    private Vector _freeDragGrab;
    private bool _freePainting;
    private Rect _lastStampRect;
    private object? _freeCopied;
    private Point _lastFreeMouse;
    private Border? _freeSelectionVisual;
    private bool _freeDrawOrderPending;

    private sealed class FreePaletteEntry
    {
        public required string Name { get; init; }
        public required ReferencedItemCategory Category { get; init; }
        public required int Id { get; init; }
        public BitmapSource? Icon { get; init; }
        public string SpriteGroup { get; init; } = string.Empty;
        public string SpriteName { get; init; } = string.Empty;
    }

    private void FreeModeMenuItem_Click(object sender, RoutedEventArgs e) => EnterFreeMode();

    private void EnterFreeMode()
    {
        _editMode = EditMode.Free;
        ClearModeChecks();
        FreeModeMenuItem.IsChecked = true;
        PaletteTabs.SelectedItem = ObjectsPaletteTab;
        StatusText.Text = "Free Placement: pick an object, book or container in the Objects tab, click anywhere to put it down (8px steps), hold and drag to paint. Drag a placed one to move it; Ctrl+click puts another on top; right-click for its state, the pile order, Cut/Copy/Paste/Delete. Ctrl + double-click a container to fill it.";
    }

    private BitmapSource? FirstFrameOf(string group, string name)
    {
        var sprite = _spriteDatabase.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name)
                     ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == name);
        return sprite is { Frames.Count: > 0 } ? LoadStaticFrame(sprite.Frames[0].FilePath) : null;
    }

    // ================= Physics, as the game does it =================

    /// <summary>One thing as the game's stacking sees it (GameTestWindow.Physics.cs Body): footprint in 8px units, lifts, and its flags.</summary>
    private readonly record struct EditorBody(object Entity, int Left, int Top, int Width, int Depth, int Z, int Height, bool Elevates, bool Surface, bool IsFloor, bool Under)
    {
        public int TopLift => Z + Height;
        public bool Overlaps(EditorBody o) => Left < o.Left + o.Width && o.Left < Left + Width && Top < o.Top + o.Depth && o.Top < Top + Depth;
    }

    private int UnitsPerTileX => Math.Max(1, (_tileset?.TileWidth ?? 32) / SpriteSizing.BaseUnit);
    private int UnitsPerTileY => Math.Max(1, (_tileset?.TileHeight ?? 32) / SpriteSizing.BaseUnit);

    /// <summary>GameTestWindow.ResolveStackInfo + ResolveSolidOffset, from the definitions: does it elevate, is it a surface (table, solid tile), its size and where its solid box sits.</summary>
    private (bool Elevates, bool Surface, bool IsTile, int SizeX, int SizeY, int SizeZ, int SolidX, int SolidY) StackInfoOf(ReferencedItemCategory category, int id)
    {
        if (category == ReferencedItemCategory.Book && _bookDatabase.Books.FirstOrDefault(b => b.Id == id) is { } book)
            return (book.AllowElevation, false, false, Math.Max(1, book.SizeX), Math.Max(1, book.SizeY), Math.Max(1, book.SizeZ), book.SolidOffsetX, book.SolidOffsetY);
        if (category == ReferencedItemCategory.Container && _containerDatabase.Containers.FirstOrDefault(c => c.Id == id) is { } container)
            return (container.AllowElevation, false, false, Math.Max(1, container.SizeX), Math.Max(1, container.SizeY), Math.Max(1, container.SizeZ), container.SolidOffsetX, container.SolidOffsetY);
        if (category == ReferencedItemCategory.Object && _objectDatabase.Objects.FirstOrDefault(o => o.Id == id) is { } obj)
        {
            if (obj is { Type: ObjectType.Tile, IsFloorTile: true, IsSolid: false }) return (false, false, true, Math.Max(1, obj.SizeX), Math.Max(1, obj.SizeY), 0, obj.SolidOffsetX, obj.SolidOffsetY); // part of the floor: flat, holds nothing up
            bool solidTile = obj.Type == ObjectType.Tile && obj.IsSolid;
            bool flat = obj.Type == ObjectType.Trap || (obj.Type == ObjectType.Tile && !obj.IsSolid); // Exult: a flat shape is 0 lifts high
            return (solidTile || (obj.AllowCellElevation && !flat), solidTile || obj.Type == ObjectType.Table, obj.Type == ObjectType.Tile,
                Math.Max(1, obj.SizeX), Math.Max(1, obj.SizeY), flat ? 0 : Math.Max(1, obj.SizeZ), obj.SolidOffsetX, obj.SolidOffsetY);
        }
        return (false, false, false, 1, 1, 1, 0, 0);
    }

    /// <summary>The game's FootprintAt: horizontally centred on its tile, bottom edge on the tile's bottom edge, then its 8px offset and solid-box offset.</summary>
    private (int Left, int Top) FootprintCorner(int cellX, int cellY, int offsetX, int offsetY, int sizeX, int sizeY) =>
        (cellX * UnitsPerTileX + (int)Math.Floor((UnitsPerTileX - sizeX) / 2.0) + offsetX, (cellY + 1) * UnitsPerTileY - sizeY + offsetY);

    private static (ReferencedItemCategory Category, int Id) IdentityOf(object entity) => entity switch
    {
        ObjectInstance o => (ReferencedItemCategory.Object, o.ObjectId),
        PlacedItemInstance i => (i.Category, i.ItemId),
        _ => (ReferencedItemCategory.Object, -1)
    };

    private EditorBody BodyOf(object entity)
    {
        var (category, id) = IdentityOf(entity);
        var info = StackInfoOf(category, id);
        var (x, y, ox, oy, z, under) = entity switch
        {
            ObjectInstance o => (o.X, o.Y, o.OffsetX, o.OffsetY, o.Z, o.LiesUnder),
            PlacedItemInstance i => (i.X, i.Y, i.OffsetX, i.OffsetY, i.Z, i.LiesUnder),
            _ => (0, 0, 0, 0, 0, false)
        };
        var (left, top) = FootprintCorner(x, y, ox + info.SolidX, oy + info.SolidY, info.SizeX, info.SizeY);
        if (IsPinned(entity)) return new EditorBody(entity, left, top, info.SizeX, info.SizeY, z, info.SizeZ, false, false, false, false); // on the wall: holds nothing up
        if (entity is ObjectInstance slabTile && z >= MapFloors.BaseLift(2) && z == MapFloors.BaseLift(MapFloors.FloorOfLift(z))
            && _objectDatabase.Objects.FirstOrDefault(d => d.Id == slabTile.ObjectId) is { Type: ObjectType.Tile, IsSolid: true, IsFloorTile: true })
            return new EditorBody(entity, left, top, info.SizeX, info.SizeY, z - 1, 1, true, true, true, under); // the floor itself: a slab under its floor's height, as the game has it
        return new EditorBody(entity, left, top, info.SizeX, info.SizeY, z, info.SizeZ, info.Elevates, info.Surface, info.IsTile && info.Surface, under);
    }

    private bool IsTileObject(object entity) =>
        entity is ObjectInstance o && _objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId)?.Type == ObjectType.Tile;

    private bool IsFloorTileObject(object entity) =>
        entity is ObjectInstance o && _objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId) is { Type: ObjectType.Tile, IsFloorTile: true };

    private static bool IsHanging(object entity) => entity is ObjectInstance { HangsOnWall: true } or PlacedItemInstance { HangsOnWall: true };
    private static bool IsStaysPut(object entity) => entity is ObjectInstance { StaysPut: true } or PlacedItemInstance { StaysPut: true };
    /// <summary>Hung on the wall or staying where it's put: kept at its height, never falling.</summary>
    private static bool IsPinned(object entity) => IsHanging(entity) || IsStaysPut(entity);

    /// <summary>How many lifts above its floor something is.</summary>
    private static int RaiseOf(int z) => z - MapFloors.BaseLift(MapFloors.FloorOfLift(Math.Max(0, z)));

    /// <summary>How high a hung thing can go above its floor (the floor above starts at 5).</summary>
    private const int MaxHangingLifts = MapFloors.LiftsPerFloor - 1;

    /// <summary>Upper floors' painted tiles: a slab one lift thick under each floor (GameTestWindow.Floors.cs).</summary>
    private IEnumerable<EditorBody> FloorSlabBodies()
    {
        if (_map is null) yield break;
        foreach (var layer in _map.UpperFloorLayers)
            for (int y = 0; y < _map.Height; y++)
                for (int x = 0; x < _map.Width; x++)
                    if (layer.GetTile(x, y, _map.Width) >= 0)
                        yield return new EditorBody(layer, x * UnitsPerTileX, y * UnitsPerTileY, UnitsPerTileX, UnitsPerTileY, MapFloors.SlabLift(layer.Floor), 1, true, true, true, false);
    }

    /// <summary>What's on the map, in the game's own order (books and containers, then objects).</summary>
    private IEnumerable<object> MapThings() =>
        _map is null ? Enumerable.Empty<object>() : _map.Items.Cast<object>().Concat(_map.Objects.Where(o => o.ContainerInstanceId < 0));

    private static bool BelowCeiling(EditorBody support, int lift) =>
        support.Z < MapFloors.BaseLift(MapFloors.FloorOfLift(Math.Max(0, lift))) + MapFloors.LiftsPerFloor - 1;

    /// <summary>Where something with these flags comes to rest over this footprint (the game's SettleAll rule), given what's already settled.</summary>
    private static int RestingZ(IEnumerable<EditorBody> settled, EditorBody body)
    {
        int floorBase = MapFloors.BaseLift(MapFloors.FloorOfLift(Math.Max(0, body.Z))); // as the game: never below its own floor, never held up by a floor below
        var supports = settled.Where(b => BelowCeiling(b, body.Z) && !b.Under && b.Overlaps(body) && b.Z >= floorBase - 1).ToList();
        if (body.Under) return Math.Max(floorBase, supports.Where(b => b.IsFloor).Select(b => b.TopLift).DefaultIfEmpty(0).Max()); // on the floor itself
        return Math.Max(floorBase, body.Elevates
            ? supports.Where(b => b.Elevates).Select(b => b.TopLift).DefaultIfEmpty(0).Max()
            : supports.Where(b => b.Surface).Select(b => b.TopLift).DefaultIfEmpty(0).Max()); // lying flat: on a table top or a floor
    }

    /// <summary>
    /// Gravity for the whole map, exactly as the game runs it on start
    /// (SettleAll): lowest first, each thing comes to rest on what's under it.
    /// Returns the things whose lift changed.
    /// </summary>
    private List<object> SettleEditorWorld(object? settleLast = null)
    {
        var changed = new List<object>();
        if (_map is null) return changed;
        var things = MapThings().Select((entity, index) => (Body: BodyOf(entity), Index: index)).OrderBy(t => t.Body.Z).ThenBy(t => t.Index).Select(t => t.Body).ToList();
        if (settleLast is not null && things.FindIndex(b => ReferenceEquals(b.Entity, settleLast)) is >= 0 and var lastIndex)
        {
            var last = things[lastIndex]; // Put on top of the pile: it comes to rest after everything else, on top of them
            things.RemoveAt(lastIndex);
            things.Add(last);
        }
        var settled = FloorSlabBodies().ToList();
        foreach (var body in things)
        {
            if (body.Entity is ObjectInstance tile && _objectDatabase.Objects.FirstOrDefault(o => o.Id == tile.ObjectId)?.Type == ObjectType.Tile) { settled.Add(body); continue; } // tiles have no gravity
            if (IsPinned(body.Entity)) { settled.Add(body); continue; } // hung on the wall, or staying where it's put: never falls
            int z = RestingZ(settled, body);
            if (z != body.Z) { SetZOf(body.Entity, z); changed.Add(body.Entity); }
            settled.Add(body with { Z = z });
        }
        return changed;
    }

    private static void SetZOf(object entity, int z)
    {
        if (entity is ObjectInstance o) o.Z = z;
        else if (entity is PlacedItemInstance i) i.Z = z;
    }

    // ================= Placing =================

    /// <summary>The picture size the picked thing (or a placed one) is drawn at.</summary>
    private (double Width, double Height) PictureSizeOf(ReferencedItemCategory category, int id)
    {
        BitmapSource? picture = category switch
        {
            ReferencedItemCategory.Object => _objectDatabase.Objects.FirstOrDefault(o => o.Id == id) is { } obj ? ObjectIcon(obj) : null,
            ReferencedItemCategory.Book => _bookDatabase.Books.FirstOrDefault(b => b.Id == id) is { } book ? FirstFrameOf(book.SpriteGroup, book.SpriteName) : null,
            ReferencedItemCategory.Container => _containerDatabase.Containers.FirstOrDefault(c => c.Id == id) is { } c ? FirstFrameOf(c.SpriteGroup, c.SpriteName) : null,
            _ => null
        };
        if (picture is null) return (_tileset?.TileWidth ?? 32, _tileset?.TileHeight ?? 32);
        var (w, h) = SpriteSizing.Quantize(picture.PixelWidth, picture.PixelHeight);
        return (w, h);
    }

    private (ReferencedItemCategory Category, int Id)? PickedThing =>
        _selectedObjectId >= 0 ? (ReferencedItemCategory.Object, _selectedObjectId) : _freeItemPick;

    /// <summary>
    /// Where a thing goes if its picture's top-left is let go of at this
    /// point: its cell (from the picture's centre, as the game's ComputeDropCell),
    /// its 8px offset, and the lift it comes to rest at. As drag.cc drop_on_map
    /// does, the point is tried at each lift from the top down (a thing at lift
    /// L is drawn L*4px up-left of its spot), and the first lift where it really
    /// rests there wins - so over a table top it lands on the top, under the mouse.
    /// </summary>
    private (int X, int Y, int OffsetX, int OffsetY, int Z) ResolveFreeSpot(ReferencedItemCategory category, int id, double left, double top, double width, double height, object? self)
    {
        int floorBase = MapFloors.BaseLift(_currentFloor);
        var info = StackInfoOf(category, id);
        var others = MapThings().Where(t => !ReferenceEquals(t, self)).Select(BodyOf).Concat(FloorSlabBodies()).ToList();
        (int, int, int, int, int) At(int lift)
        {
            double l = left + lift * 4, t = top + lift * 4;
            int cellX = Math.Clamp((int)Math.Floor((l + width / 2) / _tileset!.TileWidth), 0, _map!.Width - 1);
            int cellY = Math.Clamp((int)Math.Floor((t + height / 2) / _tileset.TileHeight), 0, _map.Height - 1);
            double defaultLeft = cellX * _tileset.TileWidth + (_tileset.TileWidth - width) / 2.0;
            double defaultTop = (cellY + 1) * _tileset.TileHeight - height;
            int ox = (int)Math.Round((l - defaultLeft) / SpriteSizing.BaseUnit), oy = (int)Math.Round((t - defaultTop) / SpriteSizing.BaseUnit);
            var (fl, ft) = FootprintCorner(cellX, cellY, ox + info.SolidX, oy + info.SolidY, info.SizeX, info.SizeY);
            var body = new EditorBody(self ?? new object(), fl, ft, info.SizeX, info.SizeY, floorBase + lift, info.SizeZ, info.Elevates, info.Surface, false, false);
            return (cellX, cellY, ox, oy, RestingZ(others, body));
        }
        // Only the lifts of the picked floor are tried: lift 5 above it is the next
        // floor's base, where anything "rests" by that floor's own clamp - a drop
        // there sent the thing up to a floor above (out of sight in the editor).
        if (category == ReferencedItemCategory.Object && _objectDatabase.Objects.FirstOrDefault(o => o.Id == id) is { Type: ObjectType.Tile, IsFloorTile: true })
        {
            var ground = At(0); // part of the floor: always at the floor's own height, whatever stands there
            return (ground.Item1, ground.Item2, ground.Item3, ground.Item4, floorBase);
        }
        int highest = _currentFloor >= MapFloors.MaxFloors ? MaxPileLifts : MapFloors.LiftsPerFloor - 1;
        for (int lift = highest; lift > 0; lift--)
        {
            var spot = At(lift);
            if (spot.Item5 == floorBase + lift) return spot;
        }
        return At(0);
    }

    private object? PlaceFreeThing(ReferencedItemCategory category, int id, double left, double top, double width, double height, ObjectInstance? objectTemplate = null, PlacedItemInstance? itemTemplate = null)
    {
        if (_map is null || _tileset is null) return null;
        var (x, y, ox, oy, z) = ResolveFreeSpot(category, id, left, top, width, height, null);
        object entity;
        if (category == ReferencedItemCategory.Object)
        {
            var instance = objectTemplate is not null ? CopyOf(objectTemplate) : new ObjectInstance { ObjectId = id, Quantity = 1, ContainerInstanceId = -1 };
            (instance.X, instance.Y, instance.OffsetX, instance.OffsetY, instance.Z) = (x, y, ox, oy, z);
            if (instance.HangsOnWall || instance.StaysPut) instance.Z = MapFloors.BaseLift(_currentFloor) + RaiseOf(objectTemplate!.Z); // a pasted hung (or staying) thing at the same height
            instance.InstanceId = _map.GetNextInstanceId();
            _map.Objects.Add(instance);
            entity = instance;
        }
        else
        {
            var item = itemTemplate is not null ? CopyOfItem(itemTemplate) : new PlacedItemInstance { Category = category, ItemId = id };
            (item.X, item.Y, item.OffsetX, item.OffsetY, item.Z) = (x, y, ox, oy, z);
            if (item.HangsOnWall || item.StaysPut) item.Z = MapFloors.BaseLift(_currentFloor) + RaiseOf(itemTemplate!.Z);
            _map.Items.Add(item);
            entity = item;
        }
        RedrawAfterSettle(entity);
        SelectFree(entity);
        int floor = MapFloors.FloorOfLift(z);
        StatusText.Text = floor < _currentFloor
            ? $"Placed \"{NameOfThing(entity)}\" - nothing to stand on here on floor {_currentFloor}, so it fell to floor {floor}."
            : $"Placed \"{NameOfThing(entity)}\" at ({x}, {y}) +({ox * 8}, {oy * 8})px, lift {z - MapFloors.BaseLift(floor)}.";
        return entity;
    }

    private static PlacedItemInstance CopyOfItem(PlacedItemInstance source) => new()
    {
        Category = source.Category, ItemId = source.ItemId, OffsetX = source.OffsetX, OffsetY = source.OffsetY, IsLockedRuntime = source.IsLockedRuntime, HangsOnWall = source.HangsOnWall, StaysPut = source.StaysPut, BelongsToId = source.BelongsToId,
        OwnContents = source.OwnContents?.Select(c => new InventoryItemInstance { InstanceId = c.InstanceId, Category = c.Category, ItemId = c.ItemId, Quantity = c.Quantity, ContainerX = c.ContainerX, ContainerY = c.ContainerY }).ToList()
    };

    /// <summary>After something was put down, moved or removed: gravity, then every picture whose lift changed is redrawn.</summary>
    private void RedrawAfterSettle(params object[] touched) => RedrawAfterSettleLast(null, touched);

    private void RedrawAfterSettleLast(object? settleLast, params object[] touched)
    {
        var redraw = SettleEditorWorld(settleLast).Concat(touched).Distinct(ReferenceEqualityComparer.Instance).ToList();
        foreach (var entity in redraw)
        {
            if (entity is ObjectInstance o && _map!.Objects.Contains(o)) PlaceObjectVisual(o);
            else if (entity is PlacedItemInstance i && _map!.Items.Contains(i)) PlaceItemVisual(i);
        }
        UpdateFreeSelectionVisual();
    }

    private string NameOfThing(object entity) => entity switch
    {
        ObjectInstance o => ObjectName(o),
        PlacedItemInstance { Category: ReferencedItemCategory.Book } i => _bookDatabase.Books.FirstOrDefault(b => b.Id == i.ItemId)?.Name ?? "book",
        PlacedItemInstance { Category: ReferencedItemCategory.Container } i => _containerDatabase.Containers.FirstOrDefault(c => c.Id == i.ItemId)?.Name ?? "container",
        PlacedItemInstance i => i.Category.ToString(),
        _ => "something"
    };

    // ================= Drawing =================

    /// <summary>Like PositionQuantizedVisual, plus the 8px offset and the lift above its floor (drawn 4px up and left per lift, as the game).</summary>
    private void PositionPlacedVisual(UIElement visual, int cellX, int cellY, double width, double height, int offsetX, int offsetY, int z)
    {
        if (_tileset is null) return;
        int raised = z - MapFloors.BaseLift(MapFloors.FloorOfLift(z));
        Canvas.SetLeft(visual, cellX * _tileset.TileWidth + (_tileset.TileWidth - width) / 2.0 + offsetX * SpriteSizing.BaseUnit - raised * 4);
        Canvas.SetTop(visual, (cellY + 1) * _tileset.TileHeight - height + offsetY * SpriteSizing.BaseUnit - raised * 4);
    }

    private FrameworkElement? VisualOfThing(object entity) => entity switch
    {
        ObjectInstance o => _mapObjectVisuals.GetValueOrDefault(o.InstanceId) as FrameworkElement,
        PlacedItemInstance i => _mapItemVisuals.GetValueOrDefault(i) as FrameworkElement,
        _ => null
    };

    private bool OnCurrentFloor(object entity) => entity switch
    {
        ObjectInstance o => o.ContainerInstanceId < 0 && FloorOf(o) == _currentFloor,
        PlacedItemInstance i => FloorOf(i) == _currentFloor,
        _ => false
    };

    /// <summary>Asks for the draw order to be worked out again once the current batch of changes is done (one pass, however many things were redrawn).</summary>
    private void ApplyFreeDrawOrder()
    {
        if (_freeDrawOrderPending) return;
        _freeDrawOrderPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _freeDrawOrderPending = false; ApplyFreeDrawOrderNow(); }));
    }

    /// <summary>
    /// The picked floor's objects, books and containers in the game's paint
    /// order: by position (further down and right is in front), whatever
    /// rests on something always after it (objs.cc Game_object::compare), and
    /// what's put underneath below everything over it. Flat things (tiles,
    /// traps) under all of it.
    /// </summary>
    /// <summary>The grid squares (8 x 8 units) a footprint covers.</summary>
    private static IEnumerable<(int, int)> SquaresOf(EditorBody body)
    {
        int x1 = (body.Left + Math.Max(1, body.Width) - 1) >> 3, y1 = (body.Top + Math.Max(1, body.Depth) - 1) >> 3;
        for (int y = body.Top >> 3; y <= y1; y++)
            for (int x = body.Left >> 3; x <= x1; x++)
                yield return (x, y);
    }

    private void ApplyFreeDrawOrderNow()
    {
        if (_map is null) return;
        var bodies = MapThings().Where(OnCurrentFloor).Select(BodyOf).Where(b => VisualOfThing(b.Entity) is not null).OrderBy(b => b.Z).ToList();
        var order = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        // Each thing is compared only with those whose footprints share a grid square (8 x 8 units) with its own -
        // the only ones the checks below can ever match - not with every thing on the map (49 000 x 49 000).
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < bodies.Count; i++)
            foreach (var square in SquaresOf(bodies[i]))
            {
                if (!grid.TryGetValue(square, out var list)) grid[square] = list = new List<int>(4);
                list.Add(i);
            }
        IEnumerable<EditorBody> Near(EditorBody body)
        {
            var seen = new HashSet<int>();
            foreach (var square in SquaresOf(body))
                if (grid.TryGetValue(square, out var list))
                    foreach (int i in list)
                        if (seen.Add(i)) yield return bodies[i];
        }
        foreach (var body in bodies)
        {
            // Flat things and Tile objects lie under everything on their floor, as the game draws them (ApplyRestingDrawOrder).
            int index = IsFloorTileObject(body.Entity) ? -1 // part of the floor: under everything on it, other tiles too
                : body.Height == 0 || IsTileObject(body.Entity) ? body.Z % MapFloors.LiftsPerFloor : 10 + (body.Left + body.Width + body.Top + body.Depth) * 10 + body.Z % MapFloors.LiftsPerFloor;
            foreach (var below in Near(body))
                if (!ReferenceEquals(below.Entity, body.Entity) && below.TopLift <= body.Z && below.Overlaps(body) && order.TryGetValue(below.Entity, out int belowIndex))
                    index = Math.Max(index, belowIndex + 1);
            order[body.Entity] = index;
        }
        foreach (var body in bodies.Where(b => b.Under))
            foreach (var over in Near(body))
                if (!ReferenceEquals(over.Entity, body.Entity) && !over.Under && over.Overlaps(body))
                    order[body.Entity] = Math.Min(order[body.Entity], order[over.Entity] - 1);
        // Hung on the wall (or staying where it's put, the same): on the face of whatever its picture is over.
        foreach (var body in bodies.Where(b => IsPinned(b.Entity)))
        {
            if (VisualOfThing(body.Entity) is not { } hungVisual) continue;
            var picture = new Rect(Canvas.GetLeft(hungVisual), Canvas.GetTop(hungVisual), hungVisual.Width, hungVisual.Height);
            foreach (var other in bodies)
                if (!ReferenceEquals(other.Entity, body.Entity) && !IsPinned(other.Entity) && VisualOfThing(other.Entity) is { } otherVisual
                    && picture.IntersectsWith(new Rect(Canvas.GetLeft(otherVisual), Canvas.GetTop(otherVisual), otherVisual.Width, otherVisual.Height)))
                    order[body.Entity] = Math.Max(order[body.Entity], order[other.Entity] + 1);
        }
        foreach (var (entity, index) in order)
            if (VisualOfThing(entity) is { } visual) Panel.SetZIndex(visual, Math.Clamp(1000 + index, 1, CharacterZIndex - 1));
    }

    private void SelectFree(object? entity)
    {
        _freeSelected = entity;
        UpdateFreeSelectionVisual();
    }

    private void ClearFreeSelection() => SelectFree(null);

    private void UpdateFreeSelectionVisual()
    {
        if (_freeSelectionVisual is not null) MapCanvas.Children.Remove(_freeSelectionVisual);
        _freeSelectionVisual = null;
        if (_freeSelected is null || VisualOfThing(_freeSelected) is not { } visual) return;
        _freeSelectionVisual = new Border { BorderBrush = Brushes.Yellow, BorderThickness = new Thickness(1), IsHitTestVisible = false, Width = visual.Width + 2, Height = visual.Height + 2 };
        Canvas.SetLeft(_freeSelectionVisual, Canvas.GetLeft(visual) - 1);
        Canvas.SetTop(_freeSelectionVisual, Canvas.GetTop(visual) - 1);
        Panel.SetZIndex(_freeSelectionVisual, SelectionZIndex);
        MapCanvas.Children.Add(_freeSelectionVisual);
    }

    /// <summary>The topmost object, book or container on the picked floor whose picture is under this point.</summary>
    private object? ThingAtPoint(Point p, Func<object, bool>? filter = null) =>
        MapThings().Where(OnCurrentFloor).Where(t => filter is null || filter(t))
            .Select(t => (Thing: t, Visual: VisualOfThing(t)))
            .Where(t => t.Visual is not null && new Rect(Canvas.GetLeft(t.Visual), Canvas.GetTop(t.Visual), t.Visual.Width, t.Visual.Height).Contains(p))
            .OrderByDescending(t => Panel.GetZIndex(t.Visual!))
            .Select(t => t.Thing).FirstOrDefault();

    /// <summary>Everything whose picture is under this point on the picked floor, top first.</summary>
    private List<object> ThingsAtPoint(Point p) =>
        MapThings().Where(OnCurrentFloor)
            .Select(t => (Thing: t, Visual: VisualOfThing(t)))
            .Where(t => t.Visual is not null && new Rect(Canvas.GetLeft(t.Visual), Canvas.GetTop(t.Visual), t.Visual.Width, t.Visual.Height).Contains(p))
            .OrderByDescending(t => Panel.GetZIndex(t.Visual!))
            .Select(t => t.Thing).ToList();

    /// <summary>The menu's title line: the thing's name, and whether it hangs on the wall.</summary>
    private string MenuTitleOf(object entity) => NameOfThing(entity) + (IsPinned(entity) ? $"  ({(IsHanging(entity) ? "hangs on the wall" : "stays where it's put")}, {RaiseOf(ZOf(entity))} lift{(RaiseOf(ZOf(entity)) == 1 ? "" : "s")} up)" : "");

    /// <summary>"Something else here": the other things under the mouse, to open the menu of the one meant (a picture drawn behind a wall).</summary>
    private void AddOtherThingsHere(ContextMenu menu, object current, List<object> here, Action<object> openFor)
    {
        var others = here.Where(t => !ReferenceEquals(t, current)).ToList();
        if (others.Count == 0) return;
        var other = new MenuItem { Header = "Something else here" };
        foreach (var thing in others)
        {
            var choice = new MenuItem { Header = MenuTitleOf(thing) };
            var t = thing;
            choice.Click += (_, _) => Dispatcher.BeginInvoke(() => openFor(t)); // after this menu has closed
            other.Items.Add(choice);
        }
        menu.Items.Add(other);
        menu.Items.Add(new Separator());
    }

    private PlacedItemInstance? ContainerAtPoint(Point p) =>
        ThingAtPoint(p, t => t is PlacedItemInstance { Category: ReferencedItemCategory.Container }) as PlacedItemInstance;

    // ================= Mouse =================

    private void FreeMode_MouseLeftButtonDown(Point p, bool ctrl)
    {
        _lastFreeMouse = p;
        if (!ctrl && ThingAtPoint(p) is { } hit && VisualOfThing(hit) is { } visual)
        {
            SelectFree(hit);
            _freeDragging = hit;
            _freeDragGrab = p - new Point(Canvas.GetLeft(visual), Canvas.GetTop(visual));
            return;
        }
        if (PickedThing is not { } picked)
        {
            StatusText.Text = "Pick an object, book or container in the Objects tab first.";
            return;
        }
        var (w, h) = PictureSizeOf(picked.Category, picked.Id);
        if (PlaceFreeThing(picked.Category, picked.Id, p.X - w / 2, p.Y - h / 2, w, h) is { } placed && VisualOfThing(placed) is { } placedVisual)
        {
            _freePainting = true;
            _lastStampRect = new Rect(p.X - w / 2, p.Y - h / 2, w, h);
        }
    }

    private void FreeMode_MouseMove(Point p, MouseButtonState left)
    {
        _lastFreeMouse = p;
        if (left != MouseButtonState.Pressed)
        {
            _freePainting = false;
            if (_freeDragging is not null) FreeMode_MouseLeftButtonUp(p); // let go outside the map: it lands where the mouse came back
            return;
        }
        if (_freeDragging is { } dragged && VisualOfThing(dragged) is { } visual)
        {
            Canvas.SetLeft(visual, p.X - _freeDragGrab.X); // follows the mouse; it settles where it's let go
            Canvas.SetTop(visual, p.Y - _freeDragGrab.Y);
            UpdateFreeSelectionVisual();
            return;
        }
        if (!_freePainting || PickedThing is not { } picked) return;
        var (w, h) = PictureSizeOf(picked.Category, picked.Id);
        var stamp = new Rect(p.X - w / 2, p.Y - h / 2, w, h);
        if (stamp.IntersectsWith(_lastStampRect)) return; // the brush hasn't cleared the last one yet
        if (PlaceFreeThing(picked.Category, picked.Id, stamp.X, stamp.Y, w, h) is not null) _lastStampRect = stamp;
    }

    private bool FreeMode_MouseLeftButtonUp(Point p)
    {
        if (_editMode != EditMode.Free) return false;
        _freePainting = false;
        if (_freeDragging is not { } dragged) return true;
        _freeDragging = null;
        if (VisualOfThing(dragged) is not { } visual) return true;
        var (category, id) = IdentityOf(dragged);
        bool hanging = IsPinned(dragged);
        int raise = RaiseOf(dragged switch { ObjectInstance d => d.Z, PlacedItemInstance d => d.Z, _ => 0 });
        // A hung (or staying) thing is moved by where its picture is now, at the same height.
        var (x, y, ox, oy, z) = ResolveFreeSpot(category, id, p.X - _freeDragGrab.X + (hanging ? raise * 4 : 0), p.Y - _freeDragGrab.Y + (hanging ? raise * 4 : 0), visual.Width, visual.Height, dragged);
        if (hanging) z = MapFloors.BaseLift(_currentFloor) + raise;
        switch (dragged)
        {
            case ObjectInstance o: (o.X, o.Y, o.OffsetX, o.OffsetY, o.Z, o.LiesUnder) = (x, y, ox, oy, z, false); break;
            case PlacedItemInstance i: (i.X, i.Y, i.OffsetX, i.OffsetY, i.Z, i.LiesUnder) = (x, y, ox, oy, z, false); break;
        }
        RedrawAfterSettle(dragged);
        StatusText.Text = $"Moved \"{NameOfThing(dragged)}\" to ({x}, {y}) +({ox * 8}, {oy * 8})px.";
        return true;
    }

    private void FreeMode_MouseRightButtonDown(Point p) => OpenFreeMenu(p, null);

    private void OpenFreeMenu(Point p, object? chosen)
    {
        _lastFreeMouse = p;
        var here = ThingsAtPoint(p);
        var hit = chosen ?? here.FirstOrDefault();
        SelectFree(hit);
        var menu = new ContextMenu();
        if (hit is not null)
        {
            menu.Items.Add(new MenuItem { Header = MenuTitleOf(hit), IsEnabled = false });
            AddOtherThingsHere(menu, hit, here, t => OpenFreeMenu(p, t));
            if (hit is ObjectInstance o && _objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId) is { } obj)
                AddObjectStateMenuItems(menu, obj, o);
            if (hit is PlacedItemInstance { Category: ReferencedItemCategory.Container } chest && _containerDatabase.Containers.FirstOrDefault(c => c.Id == chest.ItemId) is { } data)
                AddContainerMenuItems(menu, chest, data);
            if (hit is PlacedItemInstance placedItem) AddBelongsToMenu(menu, placedItem);
            menu.Items.Add(new Separator());
            var onTop = new MenuItem { Header = "Put on top of the pile" };
            onTop.Click += (_, _) =>
            {
                SetLiesUnder(hit, false);
                RedrawAfterSettleLast(hit, hit); // settles after everything else: it ends up on everything under it
                StatusText.Text = $"\"{NameOfThing(hit)}\" is on top of the pile.";
            };
            bool under = hit is ObjectInstance { LiesUnder: true } or PlacedItemInstance { LiesUnder: true };
            var underneath = new MenuItem { Header = "Put underneath (under the table, holds nothing up)", IsCheckable = true, IsChecked = under };
            underneath.Click += (_, _) =>
            {
                SetLiesUnder(hit, !under);
                RedrawAfterSettle(hit);
                StatusText.Text = !under ? $"\"{NameOfThing(hit)}\" lies underneath what's here." : $"\"{NameOfThing(hit)}\" is an ordinary part of the pile again.";
            };
            menu.Items.Add(onTop);
            menu.Items.Add(underneath);
            menu.Items.Add(new Separator());

            AddHangingMenuItems(menu, hit);
            menu.Items.Add(new Separator());
        }
        var cut = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X", IsEnabled = hit is not null };
        cut.Click += (_, _) => { if (hit is not null) { _freeCopied = hit; DeleteFreeThing(hit); } };
        var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C", IsEnabled = hit is not null };
        copy.Click += (_, _) => { if (hit is not null) CopyFreeThing(hit); };
        var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = _freeCopied is not null };
        paste.Click += (_, _) => PasteFreeThing(p);
        var delete = new MenuItem { Header = "Delete", InputGestureText = "Del", IsEnabled = hit is not null };
        delete.Click += (_, _) => { if (hit is not null) DeleteFreeThing(hit); };
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.PlacementTarget = MapCanvas;
        menu.IsOpen = true;
    }

    private static int ZOf(object entity) => entity switch { ObjectInstance o => o.Z, PlacedItemInstance i => i.Z, _ => 0 };

    /// <summary>"Hangs on the wall" and "Hanging height" in a placed thing's right-click menu (Free Placement and Objects mode).</summary>
    private void AddHangingMenuItems(ContextMenu menu, object entity)
    {
        bool hanging = IsHanging(entity), staying = IsStaysPut(entity);
        var hang = new MenuItem { Header = "Hangs on the wall (stays where it is, never falls)", IsCheckable = true, IsChecked = hanging };
        hang.Click += (_, _) => SetHanging(entity, !hanging);
        menu.Items.Add(hang);
        var stay = new MenuItem { Header = "Stays where it's put (like hung on the wall - and can be used and picked up)", IsCheckable = true, IsChecked = staying,
            ToolTip = "A potion on top of a cabinet, a dagger on a shelf: it sticks exactly where it's put and is drawn over what it's on, as a hung thing - and the player reaches it over what it's stuck in or on, to use it or pick it up." };
        stay.Click += (_, _) => SetStaysPut(entity, !staying);
        menu.Items.Add(stay);
        var height = new MenuItem { Header = "Height", IsEnabled = hanging || staying };
        int current = RaiseOf(ZOf(entity));
        for (int lifts = 0; lifts <= MaxHangingLifts; lifts++)
        {
            int l = lifts;
            var choice = new MenuItem { Header = l == 0 ? "On the floor (0px)" : $"{l} lift{(l == 1 ? "" : "s")} up ({l * 4}px)", IsCheckable = true, IsChecked = l == current };
            choice.Click += (_, _) => SetHangingHeight(entity, l);
            height.Items.Add(choice);
        }
        menu.Items.Add(height);
    }

    /// <summary>Hangs it on the wall (it stays exactly where it is, at its height) or takes it down (it falls onto what's under it).</summary>
    private void SetHanging(object entity, bool value)
    {
        if (entity is ObjectInstance o) (o.HangsOnWall, o.StaysPut) = (value, value ? false : o.StaysPut);
        else if (entity is PlacedItemInstance i) (i.HangsOnWall, i.StaysPut) = (value, value ? false : i.StaysPut);
        if (value) SetLiesUnder(entity, false);
        RedrawAfterSettle(entity);
        UpdateFreeSelectionVisual();
        if (entity is ObjectInstance hungObject && ReferenceEquals(_selectedObjectInstance, hungObject)) UpdateObjectSelectionVisual();
        StatusText.Text = value
            ? $"\"{NameOfThing(entity)}\" hangs on the wall: it stays right here and never falls. Page Up / Page Down raise and lower it."
            : $"\"{NameOfThing(entity)}\" is taken down: it rests on whatever is under it.";
    }

    /// <summary>Stays where it's put (it keeps its height, never falls, and is otherwise an ordinary thing) or not (it falls onto what's under it).</summary>
    private void SetStaysPut(object entity, bool value)
    {
        if (entity is ObjectInstance o) (o.StaysPut, o.HangsOnWall) = (value, value ? false : o.HangsOnWall);
        else if (entity is PlacedItemInstance i) (i.StaysPut, i.HangsOnWall) = (value, value ? false : i.HangsOnWall);
        if (value) SetLiesUnder(entity, false);
        RedrawAfterSettle(entity);
        UpdateFreeSelectionVisual();
        if (entity is ObjectInstance stayObject && ReferenceEquals(_selectedObjectInstance, stayObject)) UpdateObjectSelectionVisual();
        StatusText.Text = value
            ? $"\"{NameOfThing(entity)}\" stays where it's put: it never falls, and can still be picked up. Page Up / Page Down raise and lower it."
            : $"\"{NameOfThing(entity)}\" no longer stays put: it rests on whatever is under it.";
    }

    /// <summary>A hung (or staying) thing's height above its floor (0 to 4 lifts, 4px each - drawn up and left, as the game draws it). Neither yet: it's hung.</summary>
    private void SetHangingHeight(object entity, int lifts)
    {
        lifts = Math.Clamp(lifts, 0, MaxHangingLifts);
        int z = MapFloors.BaseLift(MapFloors.FloorOfLift(Math.Max(0, ZOf(entity)))) + lifts;
        if (entity is ObjectInstance o) (o.HangsOnWall, o.Z) = (!o.StaysPut, z);
        else if (entity is PlacedItemInstance i) (i.HangsOnWall, i.Z) = (!i.StaysPut, z);
        RedrawAfterSettle(entity);
        UpdateFreeSelectionVisual();
        StatusText.Text = IsStaysPut(entity)
            ? $"\"{NameOfThing(entity)}\" stays {lifts} lift{(lifts == 1 ? "" : "s")} ({lifts * 4}px) up."
            : $"\"{NameOfThing(entity)}\" hangs {lifts} lift{(lifts == 1 ? "" : "s")} ({lifts * 4}px) up the wall.";
    }

    private static void SetLiesUnder(object entity, bool value)
    {
        if (entity is ObjectInstance o) o.LiesUnder = value;
        else if (entity is PlacedItemInstance i) i.LiesUnder = value;
    }

    /// <summary>
    /// "Belongs to" for this one placed copy - Mary's loom, the baker's flour
    /// sack, Tom's shears. Taking or using it is stealing from them, and a
    /// character at work uses its own things first (then anything nobody owns
    /// in its work zone). "As the editor says" falls back to the Object /
    /// Container Editor's Belong To.
    /// </summary>
    private void AddBelongsToMenu(ContextMenu menu, object entity)
    {
        int current = entity switch { ObjectInstance o => o.BelongsToId, PlacedItemInstance i => i.BelongsToId, _ => -1 };
        string Name(int id) => _characterDatabase.Characters.FirstOrDefault(c => c.Id == id)?.Name ?? "(nobody)";
        var belongs = new MenuItem { Header = current >= 0 ? $"Belongs to ({Name(current)})" : "Belongs to (as the editor says)" };
        void Set(int id)
        {
            if (entity is ObjectInstance o) o.BelongsToId = id;
            else if (entity is PlacedItemInstance i) i.BelongsToId = id;
            StatusText.Text = id >= 0 ? $"This \"{NameOfThing(entity)}\" belongs to {Name(id)}." : $"This \"{NameOfThing(entity)}\" belongs to whoever the editor says.";
        }
        var asEditor = new MenuItem { Header = "(as the editor says)", IsCheckable = true, IsChecked = current < 0 };
        asEditor.Click += (_, _) => Set(-1);
        belongs.Items.Add(asEditor);
        belongs.Items.Add(new Separator());
        foreach (var character in _characterDatabase.Characters.Where(c => !c.IsTestPlayer).OrderBy(c => c.Name))
        {
            int id = character.Id;
            var choice = new MenuItem { Header = character.Name, IsCheckable = true, IsChecked = current == id };
            choice.Click += (_, _) => Set(id);
            belongs.Items.Add(choice);
        }
        menu.Items.Add(belongs);
    }

    private void AddContainerMenuItems(ContextMenu menu, PlacedItemInstance chest, ContainerData data)
    {
        bool locked = chest.IsLockedRuntime ?? data.IsLocked;
        var startsLocked = new MenuItem { Header = "Starts locked", IsCheckable = true, IsChecked = locked };
        startsLocked.Click += (_, _) => { chest.IsLockedRuntime = !locked; PlaceItemVisual(chest); UpdateFreeSelectionVisual(); StatusText.Text = $"\"{data.Name}\" starts {(!locked ? "locked" : "unlocked")}."; };
        menu.Items.Add(startsLocked);
        var contents = new MenuItem { Header = chest.OwnContents is null ? "What's inside this one... (Ctrl + double-click)" : $"What's inside this one ({chest.OwnContents.Count} - its own)... (Ctrl + double-click)" };
        contents.Click += (_, _) => EditPlacedContainerContents(chest);
        menu.Items.Add(contents);
        if (chest.OwnContents is not null)
        {
            var reset = new MenuItem { Header = "Back to the Container Editor's Starting Contents" };
            reset.Click += (_, _) => { chest.OwnContents = null; StatusText.Text = $"This \"{data.Name}\" starts with the Container Editor's Starting Contents again."; };
            menu.Items.Add(reset);
        }
    }

    // ================= Keys and clipboard =================

    private bool FreeMode_KeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.X && _freeSelected is { } toCut) { _freeCopied = toCut; DeleteFreeThing(toCut); return true; }
        if (ctrl && e.Key == Key.C && _freeSelected is { } toCopy) { CopyFreeThing(toCopy); return true; }
        if (ctrl && e.Key == Key.V && _freeCopied is not null) { PasteFreeThing(_lastFreeMouse); return true; }
        if (e.Key == Key.Delete && _freeSelected is { } toDelete) { DeleteFreeThing(toDelete); return true; }
        // Page Up / Page Down: the selected hung (or staying) thing higher or lower.
        if (e.Key is Key.PageUp or Key.PageDown && _freeSelected is { } toRaise)
        {
            if (!IsPinned(toRaise))
            {
                StatusText.Text = $"\"{NameOfThing(toRaise)}\" isn't hung on the wall nor staying where it's put - Page Up / Page Down only move those. Right-click > Hangs on the wall, or Stays where it's put.";
                return true;
            }
            SetHangingHeight(toRaise, RaiseOf(ZOf(toRaise)) + (e.Key == Key.PageUp ? 1 : -1));
            return true;
        }
        return false;
    }

    private void CopyFreeThing(object entity)
    {
        _freeCopied = entity switch { ObjectInstance o => CopyOf(o), PlacedItemInstance i => CopyOfItem(i), _ => null };
        StatusText.Text = $"Copied \"{NameOfThing(entity)}\" - Ctrl+V (or right-click > Paste) puts it down at the mouse.";
    }

    private void PasteFreeThing(Point p)
    {
        if (_freeCopied is null) return;
        var (category, id) = IdentityOf(_freeCopied);
        var (w, h) = PictureSizeOf(category, id);
        PlaceFreeThing(category, id, p.X - w / 2, p.Y - h / 2, w, h, _freeCopied as ObjectInstance, _freeCopied as PlacedItemInstance);
    }

    private void DeleteFreeThing(object entity)
    {
        if (_map is null) return;
        string name = NameOfThing(entity);
        if (entity is ObjectInstance o) DeleteObjectInstance(o, announce: false);
        else if (entity is PlacedItemInstance i)
        {
            _map.Items.Remove(i);
            if (_mapItemVisuals.Remove(i, out var visual)) MapCanvas.Children.Remove(visual);
        }
        if (ReferenceEquals(_freeSelected, entity)) SelectFree(null);
        RedrawAfterSettle(); // what rested on it falls
        StatusText.Text = $"Removed \"{name}\".";
    }

    // ================= A container's own contents (step 4) =================

    /// <summary>Ctrl + double-click a container on the map: what THIS one holds - a copy of the Container Editor's Starting Contents, edited for this placement only.</summary>
    private void EditPlacedContainerContents(PlacedItemInstance chest)
    {
        if (_containerDatabase.Containers.FirstOrDefault(c => c.Id == chest.ItemId) is not { } data) return;
        var result = ContainerEditorWindow.EditPlacementContents(this, _containerDatabase, data, chest.OwnContents ?? data.StartingContents,
            _spriteDatabase, _bookDatabase, _objectDatabase, _resourceDatabase, _variableDatabase, _statDatabase, _characterDatabase);
        if (result is null) return;
        chest.OwnContents = result;
        StatusText.Text = $"This \"{data.Name}\" now starts with its own {result.Count} thing(s) - the Container Editor's Starting Contents are unchanged.";
    }
}
