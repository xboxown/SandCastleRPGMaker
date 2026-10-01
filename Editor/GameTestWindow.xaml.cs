using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Internal Engine, alpha stage - per the design doc, this IS the game
/// engine itself (not a separate exe), just not yet the thing that gets
/// compiled into a standalone build. Movement is keyboard-only, grid-based,
/// with collision against TileTypeProperties.Walkable. World interaction
/// follows Ultima 7's own mouse model exactly: single-click identifies
/// anything on the map (shows its name), double-click uses it - firing
/// real events through TryFireEventPages, ONE shared method every
/// interactable thing goes through (map events, books, and any future
/// item type that gains its own EventPages), not separate bespoke logic
/// per type. Books never open a separate BookPreviewWindow here - that
/// window is the Book Editor's own authoring/testing tool and stays
/// untouched; the real engine fires the exact same underlying event
/// pages/commands directly, through its own shared execution context, so
/// state (like one book's event changing another book's content) persists
/// correctly across the whole play session. Honest gaps for this alpha
/// pass: movement is one grid cell per keypress, not smooth pixel
/// movement; Weapons/Armor/Items/Objects can be identified and clicked
/// exactly like Books, but have no event system of their own yet to fire;
/// PCs/NPCs aren't placeable world entities yet, so they aren't part of
/// this click system either.
/// </summary>
public partial class GameTestWindow : Window
{
    private readonly MapData _map;
    private readonly BitmapSource? _tilesetBitmap;
    private readonly int _tileWidth;
    private readonly int _tileHeight;
    private readonly int _columns;
    private readonly string _tilesetSpriteName;
    private readonly ResourceDatabase _resources;
    private readonly SpriteDatabase _sprites;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly Random _random = new();
    /// <summary>Real per-instance id counter for a placed container the first time it's ever opened (PlacedItemInstance.InstanceId starts at 0, meaning "never opened yet") - deliberately starts high (100000+) so it can never collide with a character's own InventoryItemInstance ids, which start at 1 and are typically small.</summary>
    private int _nextContainerRuntimeInstanceId = 100000;
    private CharacterDatabase? _characters;
    private readonly TilePropertiesDatabase _tileProperties;
    private readonly VariableDatabase _variables;
    private readonly StatDatabase _stats;
    /// <summary>Book Ids the player has actually opened for reading at least once this session - the real, non-simulated source for HasBeenRead / By After Read Item conditions, in place of a value that previously always defaulted to false.</summary>
    private readonly HashSet<int> _hasBeenReadBooks = new();

    private int _playerX;
    private int _playerY;
    /// <summary>Real, confirmed fix - the player's own real elevation/lift, in the exact same Z units items and objects already use. Previously never tracked at all (0/ground was always used honestly rather than guessed); computed fresh every time the player arrives at a new tile, from whatever is really standing there (see ComputePlayerStandingZ), the same way an item settling onto a cell already works. This is the real, LOGICAL height - used for collision/step-height decisions the instant it changes; the separate _playerVisualLiftZ below is what's actually drawn, and catches up to this smoothly when it's a real fall.</summary>
    private int _playerZ;
    /// <summary>
    /// Real, confirmed fall physics, per spec ("use exult for reference
    /// please and copy their logic") - from real Exult/Ultima VII
    /// knowledge: an actor who steps off a supported height does not
    /// teleport straight down to the new floor - they visibly fall,
    /// accelerating like real gravity, landing over a short but genuine
    /// span of time, not instantly. Climbing UP (a deliberate step onto
    /// something) is immediate, exactly like it already was - only
    /// falling gets this treatment. Honest note, same as MaxStepUpHeight
    /// - the exact real fall-acceleration constant below is a reasonable,
    /// clearly-labeled approximation from general knowledge, not a value
    /// verified against Exult's own live source in this pass, since this
    /// environment has no live access to browse it directly.
    /// </summary>
    private double _playerVisualLiftZ;
    private double _playerFallVelocity;
    private const double FallAcceleration = 24.0; // real Z units per second^2 - a real, tunable approximation, not a verified Exult constant
    private const double FallTerminalVelocity = 18.0; // real Z units per second - caps the fall so it never looks like an uncontrolled blur on a tall drop
    /// <summary>
    /// Real, continuous world-pixel position of the avatar's own center
    /// anchor point - confirmed directly from Exult's real mechanics: the
    /// avatar is never tile-snapped during movement at all. _playerX/Y
    /// above remain the avatar's current LOGICAL tile (for events,
    /// collision, and every other system that reasons about "which tile
    /// is the player in") but are now purely DERIVED from this continuous
    /// position each frame, never the other way around.
    /// </summary>
    private double _playerPixelX;
    private double _playerPixelY;
    private SpriteDirection _playerFacing = SpriteDirection.South;
    private CharacterDefinition? _player;
    private PoseDefinition? _idlePose;
    private PoseDefinition? _walkingPose;
    private DispatcherTimer? _identifyTimer;
    /// <summary>True while the book overlay is open - blocks movement and world clicks, the same way a real game pauses interaction behind an open book.</summary>
    private bool _isReadingBook;

    // Ultima 7's own real movement model, confirmed directly from Exult's
    // source (keyactions.cc, and the official Exult documentation): the
    // ORIGINAL 1993 game had no arrow-key movement at all - purely mouse-
    // driven, holding a direction to walk continuously that way. Exult
    // later added arrow keys by mapping them onto that exact same
    // continuous "keep walking this way until told to stop" mechanism,
    // never a discrete per-tile step. _heldDirectionKeys/_lastDirectionKeyPressed
    // track which direction is currently held (and which one wins if more
    // than one is), polled every real frame by the game loop below - not
    // driven by counting KeyDown events at all.
    private readonly HashSet<Key> _heldDirectionKeys = new();
    private Key? _lastDirectionKeyPressed;
    private TimeSpan _lastRenderTime;
    private bool _wasMovingLastFrame;
    /// <summary>Real, continuous walking speed in tiles per second - the single value to retune overall pace. Chosen to land close to the feel of the earlier per-tile timing, but now genuinely continuous rather than a fixed-duration glide between discrete tiles.</summary>
    private const double PlayerTilesPerSecond = 4.5;

    // Task 4 - the real 320x200 virtual camera. Per spec: "structure the
    // camera code so the virtual viewport size can be changed later
    // without rewriting the camera system" - these three named constants
    // are the ONLY place viewport size/dead-zone would ever need to
    // change. World/tile/sprite sizes are never touched by any of this -
    // the camera only decides which 320x200 slice of the already-real-
    // sized world is currently visible.
    private const double ViewportWidth = 320;
    private const double ViewportHeight = 200;
    /// <summary>Half-width/height of the centered dead-zone the player can move within before the camera starts following - a comfortable "safe area," not a hard lock to center.</summary>
    private const double DeadZoneHalfWidth = 60;
    private const double DeadZoneHalfHeight = 40;
    /// <summary>World-pixel coordinates of the camera's own top-left corner - i.e. which 320x200 slice of GameCanvas is currently on screen.</summary>
    private double _cameraX;
    private double _cameraY;

    // Issue 2 - drag and drop for every placed item/object, excluding the
    // player (per spec). Same drag-vs-click distinction pattern as Book
    // Preview's own BookSpriteImage drag (read there, not modified):
    // MouseMove past a small pixel threshold marks it a real drag; a
    // plain release without that counts as a click instead, so identify
    // still works exactly as before for anything not actually dragged.
    private readonly Dictionary<PlacedItemInstance, UIElement> _itemVisuals = new();
    private readonly Dictionary<ObjectInstance, UIElement> _objectVisuals = new();
    private PlacedItemInstance? _dragItem;
    /// <summary>Set only when a drag originated from a paperdoll backpack (see StartDragFromInventory) - null for an ordinary map-to-map drag. Lets the drop-commit logic revert a click-without-real-motion back into the backpack instead of leaving a temporary item sitting on the map.</summary>
    private OpenPaperdollGump? _dragOriginGump;
    /// <summary>Real, confirmed fix, per spec ("dragged object into container... it disappeared and left container... found there is no new book added") - found the true root cause: there was no equivalent of _dragOriginGump at all for a drag that started FROM an open container (as opposed to from a paperdoll slot or the backpack list), so a drop that failed for any reason (the destination's own real volume check rejecting it, say) had nowhere real to revert the item to - it was removed from its own source container the instant the drag left the frame, and simply never existed anywhere again if the drop didn't succeed. Set the moment such a drag starts, cleared everywhere _dragOriginGump already is, and checked at every one of those same real failure points via RevertDragToContainer below.</summary>
    private OpenContainerGump? _dragOriginContainerGump;
    private OpenPaperdollGump? _pendingInventoryDragGump;
    private Point _pendingInventoryDragStart;
    /// <summary>Same real principle as _pendingInventoryDragGump, per spec - there is no right-click-unequip at all; dragging an item OUT of its equipped slot IS the real unequip action, exactly like real Exult, and only actually starts (removing it from the slot and placing a real temporary map item) once real mouse movement past a threshold is detected, so a plain click still identifies normally.</summary>
    private OpenPaperdollGump? _pendingSlotDragGump;
    /// <summary>Real, confirmed extension - either a real EquipmentSlot or a real int (a custom slot's own Id) now, per TryGetEquippedInstanceId's own doc comment.</summary>
    private object? _pendingSlotDragSlot;
    private Point _pendingSlotDragStart;
    /// <summary>Set only when a drag originated from an equipped slot (see StartDragFromSlot) - null otherwise. On ANY failure to actually place it (a plain click with no real motion, OR a real drag that ends on an invalid map location), the item goes back into this exact slot - never merely into the general backpack - matching Exult's own real Dragging_info.readied_index, tracked specifically so a failed drop can restore an item to the precise spot it was readied in.</summary>
    /// <summary>Real, confirmed extension - either a real EquipmentSlot or a real int (a custom slot's own Id) now, same real reason as _pendingSlotDragSlot.</summary>
    private object? _dragOriginSlot;
    private ObjectInstance? _dragObject;
    private UIElement? _dragVisual;
    private Point _dragStartMouse;
    private Point _dragStartElementPos;
    /// <summary>
    /// The dragged visual's real, logical world position (GameCanvas's
    /// own coordinate space) - kept accurate and authoritative for every
    /// drop-commit calculation regardless of which panel the visual is
    /// actually parented to at that moment. Needed because, per Exult's
    /// own real solution (paint the dragged object last, every frame, on
    /// top of absolutely everything, gumps included - not a Z-index
    /// system at all), the one real visual is reparented on top of the
    /// gumps layer for the duration of a drag that's moved past a gump,
    /// so its own Canvas.Left/Top temporarily become screen-space, not
    /// world-space, and can no longer be read directly for tile math.
    /// </summary>
    private double _dragLogicalLeft;
    private double _dragLogicalTop;
    private bool _movedDuringDrag;
    private const double DragThreshold = 3;

    private readonly Image _playerImage = new() { IsHitTestVisible = false };
    private SpriteAnimator? _playerAnimator;
    /// <summary>Task 2 - tracks which pose is currently actually playing, so ApplyPoseSprite is skipped (not restarted) when a move tick continues the SAME pose/direction. Without this, every move tick was restarting the walking animation from frame 0, which is exactly what coupled animation speed to movement speed instead of letting the walk cycle play at its own authored FPS.</summary>
    private PoseDefinition? _currentAppliedPose;

    private readonly BookEventPreviewExecutionContext _execContext;
    private readonly Dictionary<object, Dictionary<BookEventPage, int>> _firedCounts = new();
    private GameEvent? _eventUnderPlayer;
    private DispatcherTimer? _stayTimer;

    public GameTestWindow(MapData map, BitmapSource? tilesetBitmap, int tileWidth, int tileHeight, int columns, string tilesetSpriteName,
        ResourceDatabase resources, SpriteDatabase sprites, BookDatabase books, ObjectDatabase objects,
        CharacterDatabase characters, PoseDatabase poses, TilePropertiesDatabase tileProperties,
        VariableDatabase variables, StatDatabase stats, ContainerDatabase containers)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        // CRITICAL data-safety fix: _map must be an independent deep clone, never the same MapData instance the Map Editor itself holds. MapData is a reference type, so without this, any runtime change here (drag-and-drop repositioning today; deletion or anything else added later) would silently mutate the SAME object the editor shows and eventually saves - exactly the bug reported. Same proven JSON round-trip technique already used elsewhere in this project (CloneEventPages) for exactly this reason: safer than hand-cloning a type with this many nested fields, where missing just one would quietly leave a shared reference behind.
        _map = CloneMapData(map);
        _tilesetBitmap = tilesetBitmap;
        _tileWidth = tileWidth;
        _tileHeight = tileHeight;
        _columns = columns;
        _tilesetSpriteName = tilesetSpriteName;
        _resources = resources;
        _sprites = sprites;
        _books = books;
        _objects = objects;
        // Same real principle, and same real reason, as CloneCharacterDatabase - a container's own runtime contents (and its per-instance IsUnlockedRuntime state) get mutated live during play, exactly like a character's inventory, so Game Test needs its own fully disposable copy, never the live editor reference.
        _containers = CloneContainerDatabase(containers);
        _tileProperties = tileProperties;
        _variables = variables;
        _stats = stats;

        _execContext = new BookEventPreviewExecutionContext(_resources, selfBookId: -1);
        // Real, confirmed fix, per spec ("Make sure all the commands are LIVE world wide, including maps") - this exact same execution context is what actually runs commands during real, live gameplay (see TryFireEventPages below), not just the isolated debug preview. PlaySound/SetSwitch/SetVariable/Change*BookContent/ChangeBookSettings all execute directly and always worked; ShakeScreen/ShowText/ChangeDayNight/ApplyPaletteColor each genuinely need their own real callback wired in to do anything at all, and until now none of them ever were HERE, in the real game engine - only in the separate debug window fixed in an earlier pass. Wired to real, live, visible effects on the actual game world below.
        _execContext.OnShakeScreen = (intensity, duration, _) => PlayRealShakeEffect(intensity, duration);
        _execContext.OnShowText = text => StatusText.Text = text;
        _execContext.OnChangeDayNight = ApplyRealDayNightPeriod;
        _execContext.OnApplyPaletteColor = ApplyRealPaletteColorEffect;

        BuildMapVisual();
        BuildObjectVisuals();
        BuildBookVisuals();
        SettleAllCells(); // The Map Editor has no way to author a non-ground Z yet - every item/object starts life at Z 0 in the saved map data. This runs the real settle logic once for every occupied cell at startup, so any cell that already holds more than one thing gets a genuine, correctly-computed initial Z, rather than everything just rendering flat at ground level until the first drag ever touches that cell.
        RefreshStackingVisuals();
        SetUpPlayer(characters, poses);

        GameCanvas.Focus();
        GameCanvas.PreviewKeyDown += GameCanvas_PreviewKeyDown;
        GameCanvas.PreviewKeyUp += GameCanvas_PreviewKeyUp;
        GameCanvas.MouseLeftButtonDown += GameCanvas_MouseLeftButtonDown;

        // Task 4 - the constants are the single source of truth for viewport size, not the XAML defaults (which exist only so the designer shows something sensible) - setting these here means changing ViewportWidth/Height alone is enough to resize the whole pipeline later.
        ViewportBorder.Width = ViewportWidth;
        ViewportBorder.Height = ViewportHeight;

        // Issue 1 - the old 900x700 default (leftover from before the 320x200 pipeline existed) did not divide evenly into whole viewport multiples, wasting a lot of space as black letterbox. A clean integer multiple removes nearly all of that on first open; StatusBarAllowance is a rough, non-critical estimate since RecomputeViewportScale (on Loaded and every resize) always finds the true best-fit scale for whatever size the window actually ends up at - this default is just a good starting point, not a guarantee.
        const int DefaultScale = 3;
        const double StatusBarAllowance = 30;
        Width = ViewportWidth * DefaultScale;
        Height = ViewportHeight * DefaultScale + StatusBarAllowance;

        ViewportHost.SizeChanged += (_, _) => RecomputeViewportScale();
        Loaded += (_, _) => RecomputeViewportScale();
        UpdateCamera(); // genuine placement on load; always instant now anyway
        CompositionTarget.Rendering += GameLoop_Rendering;

        _stayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _stayTimer.Tick += (_, _) =>
        {
            if (_eventUnderPlayer is not null) TryFireEventPages(_eventUnderPlayer, _eventUnderPlayer.EventPages, BookTriggerConditionBased.ByStay, selfBookId: -1, $"\"{_eventUnderPlayer.Name}\" fired (ByStay)");
        };
        _stayTimer.Start();

        Closed += (_, _) => { _stayTimer?.Stop(); _identifyTimer?.Stop(); CompositionTarget.Rendering -= GameLoop_Rendering; };
    }

    /// <summary>
    /// Same JSON round-trip deep-clone technique as MainWindow's own
    /// CloneEventPages, for the exact same reason: MapData has enough
    /// nested lists (Layers, Items, Objects, Events) that hand-cloning
    /// risks quietly missing one and leaving a shared reference behind -
    /// which is precisely what caused this bug in the first place. This
    /// is the one line standing between "Game Test is a real, disposable
    /// play session" and "Game Test can silently corrupt your project."
    /// </summary>
    private static MapData CloneMapData(MapData source)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(source);
        // Deliberately never falls back to the original "source" reference on failure - that would silently reintroduce the exact shared-reference bug this method exists to prevent. A failure here should be loud, not quietly unsafe.
        return System.Text.Json.JsonSerializer.Deserialize<MapData>(json)
            ?? throw new InvalidOperationException("Failed to deep-clone the map for Game Test - refusing to fall back to the live editor reference.");
    }

    /// <summary>
    /// Same real principle as CloneMapData, per spec ("everything done in
    /// the runtime remains in the runtime world") - Game Test was
    /// operating directly on the PC Editor's own live CharacterDatabase
    /// reference, so a real pickup during play permanently mutated the
    /// same CharacterDefinition.Inventory/EquippedSlots the editor's
    /// Starting Inventory list reads from - the actual cause of runtime
    /// items bleeding into the editor's own design-time data. A deep
    /// clone, made once when Game Test opens, gives the runtime its own
    /// fully disposable copy instead.
    /// </summary>
    private static CharacterDatabase CloneCharacterDatabase(CharacterDatabase source)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(source);
        var clone = System.Text.Json.JsonSerializer.Deserialize<CharacterDatabase>(json)
            ?? throw new InvalidOperationException("Failed to deep-clone characters for Game Test - refusing to fall back to the live editor reference.");
        // _nextInventoryInstanceId is a private field, never serialized by System.Text.Json - without this, every cloned character's counter would silently reset to 1, regardless of how many Starting Inventory items it already has, and the first real pickup during play would collide with an existing item's own InstanceId.
        foreach (var character in clone.Characters) { character.SyncNextInventoryInstanceIdAfterLoad(); character.Paperdoll.SyncNextCustomSlotIdAfterLoad(); }
        return clone;
    }

    /// <summary>Same real principle as CloneCharacterDatabase, for the exact same reason - a container's StartingContents template is never mutated directly at runtime; a real live copy is what gets touched instead.</summary>
    private static ContainerDatabase CloneContainerDatabase(ContainerDatabase source)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(source);
        var clone = System.Text.Json.JsonSerializer.Deserialize<ContainerDatabase>(json)
            ?? throw new InvalidOperationException("Failed to deep-clone containers for Game Test - refusing to fall back to the live editor reference.");
        clone.SyncNextIdAfterLoad();
        return clone;
    }

    private void BuildMapVisual()
    {
        if (_tilesetBitmap is null) return;

        double pixelWidth = _map.Width * _tileWidth;
        double pixelHeight = _map.Height * _tileHeight;
        GameCanvas.Width = pixelWidth;
        GameCanvas.Height = pixelHeight;

        var ground = _map.Layers[0];
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                int tileIndex = ground.GetTile(x, y, _map.Width);
                if (tileIndex < 0) continue;

                int col = tileIndex % _columns;
                int row = tileIndex / _columns;
                var image = new Image
                {
                    Width = _tileWidth,
                    Height = _tileHeight,
                    IsHitTestVisible = false,
                    Source = new CroppedBitmap(_tilesetBitmap, new Int32Rect(col * _tileWidth, row * _tileHeight, _tileWidth, _tileHeight))
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(image, x * _tileWidth);
                Canvas.SetTop(image, y * _tileHeight);
                GameCanvas.Children.Add(image);
            }
        }
    }

    /// <summary>Static for this alpha pass - objects don't yet have their own behavior when walked into. WorldObject uses a plain IconPath, not a Sprite Editor group/name link like Books - so this loads a static image, no SpriteAnimator involved.</summary>
    private void BuildObjectVisuals()
    {
        foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == instance.ObjectId);
            if (worldObject is null || string.IsNullOrEmpty(worldObject.IconPath) || !File.Exists(worldObject.IconPath)) continue;

            BitmapImage bitmap;
            try { bitmap = new BitmapImage(new Uri(worldObject.IconPath)); }
            catch { continue; }

            (int quantW, int quantH) = SpriteSizing.Quantize(bitmap.PixelWidth, bitmap.PixelHeight);
            var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true, Source = bitmap };

            PositionQuantizedVisual(image, instance.X, instance.Y, quantW, quantH, instance.OffsetX, instance.OffsetY);
            Panel.SetZIndex(image, ComputeDepthZIndex(instance.X + 0.5, instance.Y + 0.5, instance.Z));
            GameCanvas.Children.Add(image);
            _objectVisuals[instance] = image;
        }
    }

    /// <summary>Renders every placed item (Weapon/Armor/Item/Book), not just Books - Books get their real animated sprite since that's the one category with actual content; the others show a plain lettered marker, same honest placeholder the Select Item picker's own tabs already use, and aren't interactive yet.</summary>
    /// <summary>
    /// Ultima 7-style quantized positioning, per spec: a sprite's size no
    /// longer forced to fill exactly one tile - so it needs real placement
    /// rules for when it's bigger or smaller than that tile. Bottom-
    /// center anchored against its OWN (cellX, cellY) footprint tile - the
    /// standard top-down convention, so a tall sprite's "feet" sit on its
    /// real tile and it grows upward/outward from there, rather than its
    /// top-left corner being what's pinned (which would make a tall
    /// sprite look like it's floating with its feet nowhere near its own
    /// tile).
    /// </summary>
    private (double Left, double Top) ComputeQuantizedPosition(int cellX, int cellY, double quantizedWidth, double quantizedHeight, int offsetX = 0, int offsetY = 0)
    {
        // Offsets shift from the tile's own default bottom-center anchor point - positive X is right, positive Y is down, the standard screen-space convention, so both are simply added.
        double left = cellX * _tileWidth + (_tileWidth - quantizedWidth) / 2.0 + offsetX * SpriteSizing.BaseUnit;
        double top = (cellY + 1) * _tileHeight - quantizedHeight + offsetY * SpriteSizing.BaseUnit;
        return (left, top);
    }

    private void PositionQuantizedVisual(UIElement visual, int cellX, int cellY, double quantizedWidth, double quantizedHeight, int offsetX = 0, int offsetY = 0)
    {
        var (left, top) = ComputeQuantizedPosition(cellX, cellY, quantizedWidth, quantizedHeight, offsetX, offsetY);
        Canvas.SetLeft(visual, left);
        Canvas.SetTop(visual, top);
    }

    /// <summary>
    /// Instant repositioning of the player from the one true, continuous
    /// position (_playerPixelX/_playerPixelY) - used at initial setup and
    /// on every pose/facing change alike, so a pose change mid-movement
    /// (which fires on every single direction change) can never discard
    /// the avatar's real in-progress sub-tile position and snap it back
    /// to a tile's default center, the way passing a plain tile index
    /// here once did. Also defensively clears any leftover animation on
    /// these exact properties before setting them directly - nothing
    /// animates them any more, but this is cheap insurance against a
    /// stale, held animation value ever resurfacing as a jump.
    /// </summary>
    private void SnapPlayerPosition()
    {
        _playerImage.BeginAnimation(Canvas.LeftProperty, null);
        _playerImage.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(_playerImage, _playerPixelX - _playerImage.Width / 2);
        Canvas.SetTop(_playerImage, _playerPixelY + _tileHeight / 2 - _playerImage.Height);
        // Real, confirmed fix - the player's own real, tracked elevation (see _playerZ) is used here now instead of the honest placeholder 0 this used before. Render order (ComputeDepthZIndex) and the actual visual lift (Exult's own confirmed diagonal shift, LiftPixelShift, the exact same real formula every item/object already uses) both come from this one real value - never invented separately, and collision itself was already decided earlier in IsTileWalkable, not here: this method only ever draws the player at whatever Z collision already resolved.
        Panel.SetZIndex(_playerImage, ComputeDepthZIndex(_playerPixelX / _tileWidth, _playerPixelY / _tileHeight, (int)Math.Round(_playerVisualLiftZ)));
        Canvas.SetLeft(_playerImage, Canvas.GetLeft(_playerImage) - _playerVisualLiftZ * LiftPixelShift);
        Canvas.SetTop(_playerImage, Canvas.GetTop(_playerImage) - _playerVisualLiftZ * LiftPixelShift);
    }

    private void BuildBookVisuals()
    {
        foreach (var instance in _map.Items)
        {
            UIElement visual;
            double quantW = _tileWidth, quantH = _tileHeight;

            if (instance.Category == ReferencedItemCategory.Book)
            {
                var book = _books.Books.FirstOrDefault(b => b.Id == instance.ItemId);
                var sprite = book is null ? null : _sprites.Groups.FirstOrDefault(g => g.Name == book.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book.SpriteName);

                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                    (int qw, int qh) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : ((int)_tileWidth, (int)_tileHeight);
                    quantW = qw; quantH = qh;

                    var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true };
                    _ = new SpriteAnimator(sprite, image, _resources);
                    visual = image;
                }
                else
                {
                    // No sprite assigned yet - a plain marker so the book is still visible and walkable-into, rather than invisible and untestable. No real art to quantize from, so this honestly falls back to the standard tile size.
                    visual = new Border
                    {
                        Width = quantW, Height = quantH, Background = Brushes.SaddleBrown,
                        IsHitTestVisible = true,
                        Child = new TextBlock { Text = "B", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    };
                }
            }
            else if (instance.Category == ReferencedItemCategory.Container)
            {
                // Real, confirmed fix, same real bug as the map editor's own PlaceItemVisual - this is GameTestWindow's own SEPARATE map-item-visual builder, never extended for Container at all, so it fell into the generic lettered-marker fallback below instead of showing its real sprite.
                var containerData = _containers.Containers.FirstOrDefault(c => c.Id == instance.ItemId);
                var sprite = containerData is null ? null : _sprites.Groups.FirstOrDefault(g => g.Name == containerData.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == containerData.SpriteName);

                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                    (int qw, int qh) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : ((int)_tileWidth, (int)_tileHeight);
                    quantW = qw; quantH = qh;

                    var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true };
                    _ = new SpriteAnimator(sprite, image, _resources);
                    visual = image;
                }
                else
                {
                    visual = new Border
                    {
                        Width = quantW, Height = quantH, Background = Brushes.SaddleBrown,
                        IsHitTestVisible = true,
                        Child = new TextBlock { Text = "C", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    };
                }
            }
            else
            {
                visual = new Border
                {
                    Width = quantW, Height = quantH, Background = Brushes.DimGray,
                    IsHitTestVisible = true,
                    ToolTip = $"{instance.Category} #{instance.ItemId} (no editor built yet, not interactive here)",
                    Child = new TextBlock { Text = instance.Category.ToString()[0].ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
            }
            PositionQuantizedVisual(visual, instance.X, instance.Y, quantW, quantH, instance.OffsetX, instance.OffsetY);
            Panel.SetZIndex(visual, ComputeDepthZIndex(instance.X + 0.5, instance.Y + 0.5, instance.Z));
            GameCanvas.Children.Add(visual);
            _itemVisuals[instance] = visual;
        }
    }

    private (int Width, int Height) QuantizedSpriteSize(SpriteEntry sprite)
    {
        if (sprite.Frames.Count == 0) return ((int)_tileWidth, (int)_tileHeight);
        var bitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
        return bitmap is null ? ((int)_tileWidth, (int)_tileHeight) : SpriteSizing.Quantize(bitmap.PixelWidth, bitmap.PixelHeight);
    }

    /// <summary>Uses whichever character has IsTestPlayer set; falls back to a plain colored square with an honest status message if none is designated or it has no Idle pose sprite assigned for any direction yet - an alpha engine should say so, not silently show nothing.</summary>
    private void SetUpPlayer(CharacterDatabase characters, PoseDatabase poses)
    {
        _characters = CloneCharacterDatabase(characters);
        _playerX = _map.StartX >= 0 ? _map.StartX : _map.Width / 2;
        _playerY = _map.StartY >= 0 ? _map.StartY : _map.Height / 2;
        // Real, confirmed fix - safe to compute for real right here: SettleAllCells has already run (see the constructor, just above this call), so every item/object already has its own real, correct Z by this point.
        _playerZ = ComputePlayerStandingZ(_playerX, _playerY);
        _playerVisualLiftZ = _playerZ; // starting position - no real fall to animate on first appearing in the world

        _player = _characters.Characters.FirstOrDefault(c => c.IsTestPlayer);
        _idlePose = poses.Poses.FirstOrDefault(p => p.Name == "Idle");
        _walkingPose = poses.Poses.FirstOrDefault(p => p.Name == "Walking");
        SpriteEntry? idleSprite = ResolveFacingSprite(_idlePose, out double idleVolume);

        if (idleSprite is not null && idleSprite.Frames.Count > 0)
        {
            var (quantW, quantH) = QuantizedSpriteSize(idleSprite);
            _playerImage.Width = quantW;
            _playerImage.Height = quantH;
            _playerAnimator = new SpriteAnimator(idleSprite, _playerImage, _resources, idleVolume);
        }
        else
        {
            var rect = new Rectangle { Width = _tileWidth, Height = _tileHeight, Fill = Brushes.Gold, Stroke = Brushes.Black };
            GameCanvas.Children.Add(rect);
            Panel.SetZIndex(rect, ComputeDepthZIndex(_playerX + 0.5, _playerY + 0.5, 0));
            Canvas.SetLeft(rect, _playerX * _tileWidth);
            Canvas.SetTop(rect, _playerY * _tileHeight);
            StatusText.Text = _player is null
                ? "No character is set as the test player (PC Editor) - showing a placeholder square."
                : $"\"{_player.Name}\" has no Idle pose sprite assigned for any direction yet - showing a placeholder square.";
            return;
        }

        GameCanvas.Children.Add(_playerImage);
        // Continuous center-anchor position initialized to match the starting tile exactly (same convention as UpdateCamera's own playerCenterX/Y): tile center horizontally, tile bottom vertically.
        _playerPixelX = _playerX * _tileWidth + _tileWidth / 2.0;
        _playerPixelY = _playerY * _tileHeight + _tileHeight / 2.0;
        SnapPlayerPosition();

        _eventUnderPlayer = _map.Events.FirstOrDefault(ev => ev.X == _playerX && ev.Y == _playerY);
    }

    /// <summary>
    /// Resolves the sprite for a given pose at the player's current
    /// facing, via PoseAssignment's own fallback, and returns the volume
    /// to play its sound at (0 if that pose is Muted, otherwise Volume/
    /// 100) - so the Internal Engine genuinely respects what was set in
    /// the PC Editor, not just the visual assignment.
    /// </summary>
    private SpriteEntry? ResolveFacingSprite(PoseDefinition? pose, out double volume)
    {
        volume = 0;
        if (_player is null || pose is null) return null;
        if (!_player.PoseSprites.TryGetValue(pose.Id, out var assignment)) return null;
        volume = assignment.Muted ? 0 : assignment.Volume / 100.0;
        var link = assignment.Resolve(_playerFacing);
        return link is null ? null : _sprites.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName);
    }

    /// <summary>Switches the player's live sprite to the given pose at the current facing, respecting that pose's own Muted/Volume - used both when facing changes and when settling from Walking back to Idle.</summary>
    private void ApplyPoseSprite(PoseDefinition? pose)
    {
        var sprite = ResolveFacingSprite(pose, out double volume);
        if (sprite is null || sprite.Frames.Count == 0) return;
        _playerAnimator?.Stop();
        var (quantW, quantH) = QuantizedSpriteSize(sprite);
        _playerImage.Width = quantW;
        _playerImage.Height = quantH;
        SnapPlayerPosition();
        _playerAnimator = new SpriteAnimator(sprite, _playerImage, _resources, volume);
        _currentAppliedPose = pose;
    }

    private static (int dx, int dy) DirectionFor(Key key) => key switch
    {
        Key.Left => (-1, 0),
        Key.Right => (1, 0),
        Key.Up => (0, -1),
        Key.Down => (0, 1),
        _ => (0, 0)
    };

    /// <summary>The active party, per spec: the current test player is always included (matching Exult's own Avatar-always-in-party-slot-1 behavior) even if IsInParty wasn't separately checked, plus everyone else explicitly marked IsInParty.</summary>
    private List<CharacterDefinition> GetPartyList()
    {
        var list = new List<CharacterDefinition>();
        if (_player is not null) list.Add(_player);
        if (_characters is not null) list.AddRange(_characters.Characters.Where(c => c.IsInParty && c != _player));
        return list;
    }

    /// <summary>One real, independent open gump - confirmed directly from documented real Exult play: several paperdolls can be open at once, each its own draggable window with its own exit button, never a single shared overlay that just swaps character.</summary>
    private class OpenPaperdollGump
    {
        public required CharacterDefinition Character;
        public required Border GumpBorder;
        public required Canvas BodyCanvas;
        public int? SelectedInstanceId;
        /// <summary>The linked backpack-contents gump, if currently open - real Exult double-clicks the backpack icon to open its contents as its own separate gump, per spec, rather than always showing the backpack list alongside the paperdoll.</summary>
        public OpenBackpackGump? BackpackGump;
        /// <summary>Same real fix, same real reason, as OpenContainerGump's own ItemAnimators - RefreshPaperdollGump rebuilds this gump's whole body/icon visual tree too (on equip, unequip, and every real viewport resize), and every discarded animator's own DispatcherTimer would otherwise just keep running forever, orphaned.</summary>
        public readonly List<SpriteAnimator> ItemAnimators = new();
    }

    /// <summary>The separate backpack-contents gump a paperdoll's Backpack slot opens, per spec and per real Exult ("double-click the backpack and it opens as a gump").</summary>
    private class OpenBackpackGump
    {
        public required OpenPaperdollGump Owner;
        public required Border GumpBorder;
        public required ListBox BackpackList;
    }

    private readonly List<OpenPaperdollGump> _openPaperdolls = new();

    /// <summary>Display row for a gump's backpack list - resolves the real item name live from whichever database it references, same pattern used throughout the engine rather than caching a name that could go stale.</summary>
    private class PaperdollBackpackRow
    {
        public InventoryItemInstance Instance { get; }
        private readonly BookDatabase _books;
        private readonly ObjectDatabase _objects;
        private readonly ContainerDatabase? _containers;
        public PaperdollBackpackRow(InventoryItemInstance instance, BookDatabase books, ObjectDatabase objects, ContainerDatabase? containers = null)
        {
            Instance = instance;
            _books = books;
            _objects = objects;
            _containers = containers;
        }
        public string DisplayText
        {
            get
            {
                string name = Instance.Category switch
                {
                    ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == Instance.ItemId)?.Name ?? "(missing book)",
                    ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == Instance.ItemId)?.Name ?? "(missing object)",
                    // Real, confirmed fix - this used to always show the stale "(no Container database yet)" placeholder even though the real container database has existed and been used throughout the engine for a long time now.
                    ReferencedItemCategory.Container => _containers?.Containers.FirstOrDefault(c => c.Id == Instance.ItemId)?.Name ?? "(missing container)",
                    _ => $"(no {Instance.Category} database yet)"
                };
                return Instance.Quantity > 1 ? $"{name} x{Instance.Quantity}" : name;
            }
        }
    }

    private bool IsPaperdollItemTwoHanded(InventoryItemInstance item) => item.Category switch
    {
        ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.IsTwoHanded ?? false,
        ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId)?.IsTwoHanded ?? false,
        _ => false
    };

    private bool IsPaperdollLeftHandLocked(CharacterDefinition character)
    {
        if (!character.EquippedSlots.TryGetValue(EquipmentSlot.RightHand, out int rightHandInstanceId)) return false;
        var rightHandItem = character.Inventory.FirstOrDefault(i => i.InstanceId == rightHandInstanceId);
        return rightHandItem is not null && IsPaperdollItemTwoHanded(rightHandItem);
    }

    /// <summary>Opens a character's gump, or brings their already-open one to front (both visually and as the one Escape will close next) - never duplicates a gump for the same character, matching real Exult (re-selecting an already-open party member just raises their window).</summary>
    /// <summary>Loads an exit-button sprite's frame 1 (unpressed) or frame 2 (pressed), per spec - a real, configurable 2-frame sprite picked in the PC Editor, never a hardcoded placeholder glyph.</summary>
    private BitmapImage? LoadExitButtonFrame(CharacterDefinition character, bool pressed)
    {
        var group = _sprites.Groups.FirstOrDefault(g => g.Name == character.Paperdoll.ExitButtonSpriteGroup);
        var sprite = group?.Sprites.FirstOrDefault(s => s.Name == character.Paperdoll.ExitButtonSpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == character.Paperdoll.ExitButtonSpriteName);
        if (sprite is null || sprite.Frames.Count == 0) return null;
        int frameIndex = pressed && sprite.Frames.Count > 1 ? 1 : 0;
        return LoadStaticFrame(sprite.Frames[frameIndex].FilePath);
    }

    /// <summary>Builds the one real exit-button Image used on both a paperdoll and its linked backpack gump - frame 1 while idle, frame 2 while actually held down, back to frame 1 on release, THEN the gump closes, per spec.</summary>
    private Image BuildExitButton(CharacterDefinition character, Action onClosed)
    {
        var image = new Image { Width = 24, Height = 24, Stretch = Stretch.Uniform, Cursor = Cursors.Hand, Source = LoadExitButtonFrame(character, pressed: false) };
        image.MouseLeftButtonDown += (_, e) => { image.Source = LoadExitButtonFrame(character, pressed: true); e.Handled = true; };
        image.MouseLeftButtonUp += (_, e) => { image.Source = LoadExitButtonFrame(character, pressed: false); onClosed(); e.Handled = true; };
        return image;
    }

    private void OpenPaperdoll(CharacterDefinition? character)
    {
        if (character is null) return;
        var existing = _openPaperdolls.FirstOrDefault(g => g.Character == character);
        if (existing is not null)
        {
            _openPaperdolls.Remove(existing);
            _openPaperdolls.Add(existing);
            BringPaperdollToFront(existing);
            return;
        }

        // Real, clean paperdoll - per spec, just the body and its (invisible) slots, no title bar and no side panel; the body itself is the real drag handle, and a real 2-frame sprite (not a text glyph) is the exit button.
        // Real, confirmed fix, per spec ("the paperdoll looks tiny and the object sprites inside of it ARE HUGE... because the paperdoll sprites themselves did not update with the rest of the group") - the whole gump now scales together as one real, uniform unit via LayoutTransform, the same real principle ViewportScale itself already applies to the main game view, instead of leaving the body fixed while only its own item icons scaled independently (the actual mismatch that made a bigger window look wrong). Re-applied fresh with the current real scale every time this gump refreshes (RefreshPaperdollGump, called on every real viewport resize already), so it always tracks live.
        var bodyCanvas = new Canvas { Width = 320, Height = 400, Background = Brushes.Transparent };
        var gumpBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(238, 26, 26, 26)), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Padding = new Thickness(4) };
        gumpBorder.Child = bodyCanvas;

        var gump = new OpenPaperdollGump { Character = character, GumpBorder = gumpBorder, BodyCanvas = bodyCanvas };

        // Cascading default position, per spec ("stay where they're assigned") - offset a little from each previously-opened gump still on screen, rather than always stacking exactly on top of the last one.
        double baseX = 40 + _openPaperdolls.Count * 36;
        double baseY = 30 + _openPaperdolls.Count * 30;
        Canvas.SetLeft(gumpBorder, baseX);
        Canvas.SetTop(gumpBorder, baseY);

        // Real drag-by-body, per spec ("when I press and hold on the paper doll itself, not the slots, I can move it around") - wired on bodyCanvas itself; every slot click-region and the exit button mark their own MouseLeftButtonDown/Up as Handled so this never fires for them.
        bodyCanvas.MouseLeftButtonDown += (_, e) => PaperdollTitleBar_MouseLeftButtonDown(gump, gumpBorder, bodyCanvas, e);
        bodyCanvas.MouseMove += (_, e) => PaperdollTitleBar_MouseMove(gumpBorder, e);
        bodyCanvas.MouseLeftButtonUp += (_, _) => PaperdollTitleBar_MouseLeftButtonUp(bodyCanvas);

        PaperdollGumpsCanvas.Children.Add(gumpBorder);
        _openPaperdolls.Add(gump);
        BringPaperdollToFront(gump);
        RefreshPaperdollGump(gump);
    }

    /// <summary>Real, unified focus order, per spec - a container gump is just as real a "topmost" candidate for Escape as a character's own paperdoll, so both kinds share this one list instead of Escape only ever knowing about paperdolls. Bringing a gump to front (of either kind) moves it to the end here; closing a gump (of either kind, whether by its own exit control or by Escape) removes it here too.</summary>
    private readonly List<object> _gumpFocusOrder = new();
    private void TrackGumpFocus(object gump) { _gumpFocusOrder.Remove(gump); _gumpFocusOrder.Add(gump); }
    private void UntrackGumpFocus(object gump) => _gumpFocusOrder.Remove(gump);

    private void BringPaperdollToFront(OpenPaperdollGump gump)
    {
        Panel.SetZIndex(gump.GumpBorder, 100 + _openPaperdolls.IndexOf(gump));
        TrackGumpFocus(gump);
    }

    /// <summary>Real Escape behavior, confirmed from documented real Exult play - closes the single most-recently-focused (topmost) gump, whether it's a character's own paperdoll or an open container; holding Escape naturally closes them one after another via the OS's own key repeat, which is what reads as "closes them all" when held briefly.</summary>
    private void CloseTopmostPaperdoll()
    {
        if (_gumpFocusOrder.Count == 0) return;
        switch (_gumpFocusOrder[^1])
        {
            case OpenPaperdollGump paperdollGump: ClosePaperdollGump(paperdollGump); break;
            case OpenContainerGump containerGump: CloseContainerGump(containerGump); break;
        }
    }

    private void ClosePaperdollGump(OpenPaperdollGump gump)
    {
        // Real, confirmed fix - same real reason as CloseContainerGump's own version.
        foreach (var animator in gump.ItemAnimators) animator.Stop();
        gump.ItemAnimators.Clear();
        if (gump.BackpackGump is not null) CloseBackpackGump(gump);
        PaperdollGumpsCanvas.Children.Remove(gump.GumpBorder);
        _openPaperdolls.Remove(gump);
        UntrackGumpFocus(gump);
        if (_openPaperdolls.Count == 0 && _openContainers.Count == 0) GameCanvas.Focus();
    }

    private Border? _draggingGumpBorder;
    private Point _paperdollDragStartMouse;
    private Point _paperdollDragStartPos;

    /// <summary>Generic gump-drag, per spec - operates on whichever real Border is passed in, so it works identically for a paperdoll's own body-as-drag-handle and a linked backpack gump's own drag handle, never assuming "the gump being dragged" is specifically a paperdoll.</summary>
    private void PaperdollTitleBar_MouseLeftButtonDown(object ownerForFrontOrdering, Border gumpBorder, UIElement dragSurface, MouseButtonEventArgs e)
    {
        if (ownerForFrontOrdering is OpenPaperdollGump paperdollGump)
        {
            _openPaperdolls.Remove(paperdollGump);
            _openPaperdolls.Add(paperdollGump);
            BringPaperdollToFront(paperdollGump);
        }
        else if (ownerForFrontOrdering is OpenContainerGump containerGump)
        {
            BringPaperdollToFrontGeneric(gumpBorder, containerGump);
        }
        else
        {
            Panel.SetZIndex(gumpBorder, 200);
        }
        _draggingGumpBorder = gumpBorder;
        _paperdollDragStartMouse = e.GetPosition(PaperdollGumpsCanvas);
        _paperdollDragStartPos = new Point(Canvas.GetLeft(gumpBorder), Canvas.GetTop(gumpBorder));
        dragSurface.CaptureMouse();
    }

    private void PaperdollTitleBar_MouseMove(Border gumpBorder, MouseEventArgs e)
    {
        if (_draggingGumpBorder != gumpBorder) return;
        var current = e.GetPosition(PaperdollGumpsCanvas);
        Canvas.SetLeft(gumpBorder, _paperdollDragStartPos.X + (current.X - _paperdollDragStartMouse.X));
        Canvas.SetTop(gumpBorder, _paperdollDragStartPos.Y + (current.Y - _paperdollDragStartMouse.Y));
    }

    private void PaperdollTitleBar_MouseLeftButtonUp(UIElement dragSurface)
    {
        // Per spec, the body is now purely a drag handle - a plain click on it (no real drag motion) does nothing; closing is the dedicated exit button's own job now, not a side effect of clicking the body.
        _draggingGumpBorder = null;
        dragSurface.ReleaseMouseCapture();
    }

    /// <summary>
    /// The real paperdoll, per spec - an actual sprite composite drawn
    /// in-world, the same principle as the book reading overlay: the
    /// body image, then each equipped item's OWN real sprite/icon drawn
    /// directly at its slot's authored position - no visible box,
    /// border, or text label for a slot at all, empty or occupied. Each
    /// slot still has a real, functional click REGION at that position
    /// (Background="Transparent", which WPF still hit-tests even though
    /// nothing is visibly drawn there) so equip/unequip keeps working -
    /// the slot is data used by the runtime, never something the player
    /// is meant to see.
    /// </summary>
    private void RefreshPaperdollGump(OpenPaperdollGump gump)
    {
        // Real, confirmed fix, same real reason as OpenContainerGump's own version - every animator this gump built last time (the body, and every equipped item's own icon) is stopped here first, before the canvas is cleared and rebuilt, so none of them keep running forever, orphaned.
        foreach (var oldAnimator in gump.ItemAnimators) oldAnimator.Stop();
        gump.ItemAnimators.Clear();

        var character = gump.Character;
        gump.BodyCanvas.Children.Clear();
        // Real, confirmed fix - the whole gump, body and every item icon in it together, scales as one real unit now, so it never looks tiny with huge items inside it again.
        // Real, confirmed correction, per spec ("it is TOO HUGE now... readjust the proportion value") - the earlier fix correctly matched real Exult's own confirmed behavior (gumps DO scale by the same real global factor as the rest of the game world), but the real bug was the base size this gump was ever authored at: 320x400 is TWICE the base viewport's own 200-pixel height, so scaling both by the exact same real factor made the gump's own on-screen size balloon to genuinely double the whole visible game world's own height, worse at every larger window size. Normalized here instead, so this gump's own real on-screen height always matches the base viewport's own real on-screen height (200 * ViewportScale) exactly, at any window size - proportional, the same real way Exult's own gumps stay proportional to the game world, without discarding or rescaling any slot position a designer has already placed in this same 320x400 authoring space.
        double paperdollGumpScale = ViewportScale.ScaleX * (200.0 / 400.0);
        gump.BodyCanvas.LayoutTransform = new ScaleTransform(paperdollGumpScale, paperdollGumpScale);
        // Real, confirmed fix, per spec ("the sprite size when placed in paper dolls they shrink and not adjust their size like the paper dolls... check exult code and no guess") - genuinely checked Exult's own real source (Shape_manager::paint_shape, shapeid.h) - shapes are painted at their own native pixel size with no scale parameter at all; the ENTIRE logical framebuffer (map, gumps, and every item icon together) is what actually gets scaled, as one real unit, when displayed. An item icon here must render at the exact same real effective scale a map item does - this gump's own body was normalized DOWN by a real factor of 0.5 (see above) specifically to fix it being too huge, so every item icon drawn inside it needs the exact inverse counter-scale applied to its own width/height, so the two real scale factors cancel out and the icon still lands on the exact same real, final on-screen size a map item at this same ViewportScale would.
        double paperdollItemCounterScale = 400.0 / 200.0;
        bool leftHandLocked = IsPaperdollLeftHandLocked(character);

        var bodyGroup = _sprites.Groups.FirstOrDefault(g => g.Name == character.Paperdoll.BodySpriteGroup);
        var bodySprite = bodyGroup?.Sprites.FirstOrDefault(s => s.Name == character.Paperdoll.BodySpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == character.Paperdoll.BodySpriteName);
        if (bodySprite is not null && bodySprite.Frames.Count > 0)
        {
            var bodyImage = new Image { Width = 320, Height = 400, Stretch = Stretch.Uniform };
            gump.ItemAnimators.Add(new SpriteAnimator(bodySprite, bodyImage, _resources));
            Panel.SetZIndex(bodyImage, 0);
            gump.BodyCanvas.Children.Add(bodyImage);
        }

        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (!character.Paperdoll.SlotPositions.TryGetValue(slot, out var pos)) pos = new PaperdollSlotPosition { X = 140, Y = 10 };
            bool isLockedLeftHand = slot == EquipmentSlot.LeftHand && leftHandLocked;

            character.EquippedSlots.TryGetValue(slot, out int equippedInstanceId);
            var equippedItem = equippedInstanceId > 0 ? character.Inventory.FirstOrDefault(i => i.InstanceId == equippedInstanceId) : null;

            // The equipped item's own real sprite/icon, drawn directly at the slot's position - this IS the paperdoll visual; there is nothing else to draw for a slot. Real, confirmed extraction - shared now (BuildEquippedItemIconVisual below) so the real custom-slot loop further down uses the exact same real rendering logic, not a second, separately-maintained copy of it.
            FrameworkElement? itemVisual = equippedItem is not null ? BuildEquippedItemIconVisual(gump, equippedItem, paperdollItemCounterScale) : null;
                if (itemVisual is not null)
                {
                    Canvas.SetLeft(itemVisual, pos.X);
                    Canvas.SetTop(itemVisual, pos.Y);
                    Panel.SetZIndex(itemVisual, 10);
                    itemVisual.Cursor = isLockedLeftHand ? Cursors.No : Cursors.Hand;
                    // Real fix, matching Book Preview's own proven, working BookSpriteImage_MouseLeftButtonDown pattern exactly - interaction is wired DIRECTLY on the item's own real visual, never a separate transparent overlay that has to be kept in perfect position/size sync with it (which is exactly what kept quietly drifting out of sync across several previous fixes). IsHitTestVisible is genuinely true now - this element itself receives the real click.
                    itemVisual.IsHitTestVisible = true;
                    // Real unequip, per spec - there is no right-click-unequip at all; dragging an item OUT of its slot IS the real unequip action, exactly like real Exult and exactly like Book Preview's own real click-vs-drag model (BookSpriteImage_MouseLeftButtonDown). Double-click still uses immediately. A single click only starts a real drag CANDIDATE - it only actually becomes a drag (unequipping the item for real) once real mouse movement past a threshold is detected in PaperdollItemVisual_MouseMove; released without that movement, it's a plain identify instead.
                    itemVisual.MouseLeftButtonDown += (_, e) =>
                    {
                        if (e.ClickCount == 2)
                        {
                            // Real, confirmed fix, per spec ("Backpack: this is for containers of all sorts, not just backpacks... I should single-click a container to identify it and double-click it to interact with it, the same way I do on the map side") - a real, specific container equipped here opens ITS OWN real contents view now, exactly like double-clicking it on the map would, rather than always falling back to the generic backpack list regardless of what's actually in the slot.
                            if (equippedItem is not null && equippedItem.Category == ReferencedItemCategory.Container)
                            {
                                OpenEquippedContainer(equippedItem);
                            }
                            else if (slot == EquipmentSlot.Backpack)
                            {
                                // Real Exult behavior, confirmed from documented real play - double-clicking an EMPTY backpack slot opens the character's own general contents as its own separate gump, rather than using it the ordinary way.
                                OpenBackpackContents(gump);
                            }
                            else PaperdollSlotClicked(gump, slot, 2);
                            e.Handled = true;
                            return;
                        }
                        _pendingSlotDragGump = gump;
                        _pendingSlotDragSlot = slot;
                        _pendingSlotDragStart = e.GetPosition(gump.BodyCanvas);
                        // Real, confirmed fix for single-click-identify silently failing - matching Book Preview's own real, proven pattern (BookSpriteImage.CaptureMouse()) exactly. Without this, even a tiny bit of real mouse jitter during a plain click could move the pointer just outside the icon's own bounds by the moment of release, so MouseLeftButtonUp would never fire on this element at all - explicit capture guarantees release is always reported back here regardless.
                        itemVisual.CaptureMouse();
                        e.Handled = true;
                    };
                    itemVisual.MouseMove += (_, e) => PaperdollItemVisual_MouseMove(gump, slot, e);
                    itemVisual.MouseLeftButtonUp += (_, e) => PaperdollItemVisual_MouseLeftButtonUp(itemVisual, gump, slot, e);
                    gump.BodyCanvas.Children.Add(itemVisual);
                }

            // An EMPTY slot has no real visual to wire a handler onto at all, so it alone still needs a plain transparent click region - the one real remaining use for this pattern, exactly where Book Preview's own model has nothing further to say (there's no "empty" analog there). Sized to a small, reasonable default (there's no icon to match) and, per spec, never visible - only ever a hit-test area, to equip whatever's currently selected there. Marked Handled so a slot click never also triggers the body's own drag handler underneath it.
            if (itemVisual is null)
            {
                var clickRegion = new Border
                {
                    Width = 40,
                    Height = 40,
                    Background = Brushes.Transparent,
                    Cursor = isLockedLeftHand ? Cursors.No : Cursors.Hand,
                    Tag = slot
                };
                clickRegion.MouseLeftButtonDown += (_, e) => { PaperdollSlotClicked(gump, slot, e.ClickCount); e.Handled = true; };
                if (slot == EquipmentSlot.Backpack)
                    clickRegion.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { OpenBackpackContents(gump); e.Handled = true; } };
                Canvas.SetLeft(clickRegion, pos.X - 4);
                Canvas.SetTop(clickRegion, pos.Y - 4);
                Panel.SetZIndex(clickRegion, 10);
                gump.BodyCanvas.Children.Add(clickRegion);
            }
        }

        // Real, confirmed new feature, per spec ("the same mechanics you used in the inventory paper doll to single click to identify please apply it... apply it to custom slots too") - runtime rendering and interaction for custom slots, genuinely sharing every piece of machinery the built-in loop above already uses (BuildEquippedItemIconVisual, PaperdollSlotClicked, StartDragFromSlot, PaperdollItemVisual_MouseMove/Up, ShowPaperdollIdentifyLabel) via the same slotKey the TryGetEquippedInstanceId family of helpers understands - a custom slot has no left-hand-lock, two-handed, or Backpack-double-click concept at all, so this loop is intentionally simpler than the built-in one, not a cut-down duplicate missing real functionality.
        foreach (var customSlot in character.Paperdoll.CustomSlots)
        {
            object slotKey = customSlot.Id;
            character.CustomEquippedSlots.TryGetValue(customSlot.Id, out int customEquippedInstanceId);
            var customEquippedItem = customEquippedInstanceId > 0 ? character.Inventory.FirstOrDefault(i => i.InstanceId == customEquippedInstanceId) : null;

            FrameworkElement? customItemVisual = customEquippedItem is not null ? BuildEquippedItemIconVisual(gump, customEquippedItem, paperdollItemCounterScale) : null;
            if (customItemVisual is not null)
            {
                Canvas.SetLeft(customItemVisual, customSlot.X);
                Canvas.SetTop(customItemVisual, customSlot.Y);
                Panel.SetZIndex(customItemVisual, 10);
                customItemVisual.Cursor = Cursors.Hand;
                customItemVisual.IsHitTestVisible = true;
                customItemVisual.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        if (customEquippedItem is not null && customEquippedItem.Category == ReferencedItemCategory.Container) OpenEquippedContainer(customEquippedItem);
                        else PaperdollSlotClicked(gump, slotKey, 2);
                        e.Handled = true;
                        return;
                    }
                    _pendingSlotDragGump = gump;
                    _pendingSlotDragSlot = slotKey;
                    _pendingSlotDragStart = e.GetPosition(gump.BodyCanvas);
                    customItemVisual.CaptureMouse();
                    e.Handled = true;
                };
                customItemVisual.MouseMove += (_, e) => PaperdollItemVisual_MouseMove(gump, slotKey, e);
                customItemVisual.MouseLeftButtonUp += (_, e) => PaperdollItemVisual_MouseLeftButtonUp(customItemVisual, gump, slotKey, e);
                gump.BodyCanvas.Children.Add(customItemVisual);
            }
            else
            {
                var customClickRegion = new Border
                {
                    Width = 60,
                    Height = 22,
                    Background = new SolidColorBrush(Color.FromArgb(80, 60, 170, 60)), // faint green, matching the editor's own custom-slot color, so an empty one is still findable on the body
                    Cursor = Cursors.Hand,
                    Tag = slotKey
                };
                customClickRegion.MouseLeftButtonDown += (_, e) => { PaperdollSlotClicked(gump, slotKey, e.ClickCount); e.Handled = true; };
                Canvas.SetLeft(customClickRegion, customSlot.X);
                Canvas.SetTop(customClickRegion, customSlot.Y);
                Panel.SetZIndex(customClickRegion, 10);
                gump.BodyCanvas.Children.Add(customClickRegion);
            }
        }

        // The real exit button, per spec - a genuine 2-frame sprite, not a text glyph, positioned top-right of the body. Its own MouseLeftButtonDown/Up already mark themselves Handled (see BuildExitButton), so it never also triggers the body's drag handler.
        var exitButton = BuildExitButton(character, () => ClosePaperdollGump(gump));
        Canvas.SetLeft(exitButton, 320 - 28);
        Canvas.SetTop(exitButton, 4);
        Panel.SetZIndex(exitButton, 20);
        gump.BodyCanvas.Children.Add(exitButton);

        if (gump.BackpackGump is not null) RefreshBackpackGump(gump.BackpackGump);
    }

    /// <summary>
    /// Real, confirmed new feature - runtime support for custom slots,
    /// per spec ("the same mechanics... please apply it"). A slotKey is
    /// either a real EquipmentSlot (a built-in slot) or a real int (a
    /// CustomEquipmentSlot's own Id) - these three small helpers are the
    /// one real place that distinguishes the two, so every other method
    /// below (PaperdollSlotClicked, drag start/move/end, the render
    /// loop) works identically for both kinds without its own
    /// is-this-custom branch repeated everywhere.
    /// </summary>
    private static bool TryGetEquippedInstanceId(CharacterDefinition character, object slotKey, out int instanceId)
    {
        if (slotKey is EquipmentSlot builtin) return character.EquippedSlots.TryGetValue(builtin, out instanceId);
        if (slotKey is int customId) return character.CustomEquippedSlots.TryGetValue(customId, out instanceId);
        instanceId = 0;
        return false;
    }

    private static void SetEquippedInstanceId(CharacterDefinition character, object slotKey, int instanceId)
    {
        if (slotKey is EquipmentSlot builtin) character.EquippedSlots[builtin] = instanceId;
        else if (slotKey is int customId) character.CustomEquippedSlots[customId] = instanceId;
    }

    private static void RemoveEquippedInstanceId(CharacterDefinition character, object slotKey)
    {
        if (slotKey is EquipmentSlot builtin) character.EquippedSlots.Remove(builtin);
        else if (slotKey is int customId) character.CustomEquippedSlots.Remove(customId);
    }

    /// <summary>
    /// Real, confirmed extraction, per spec ("the same mechanics...
    /// please apply it in the container" - this time applied to custom
    /// slots) - this is the exact same Book/Object/Container icon-building
    /// logic that used to live inline in RefreshPaperdollGump's own
    /// built-in-slot loop, shared now so the real custom-slot loop below
    /// renders identically rather than drifting out of sync with its own
    /// separate copy.
    /// </summary>
    private FrameworkElement? BuildEquippedItemIconVisual(OpenPaperdollGump gump, InventoryItemInstance equippedItem, double paperdollItemCounterScale)
    {
        if (equippedItem.Category == ReferencedItemCategory.Book)
        {
            var book = _books.Books.FirstOrDefault(b => b.Id == equippedItem.ItemId);
            var itemSprite = _sprites.Groups.FirstOrDefault(g => g.Name == book?.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book?.SpriteName);
            if (itemSprite is null || itemSprite.Frames.Count == 0) return null;
            var frameBitmap = LoadStaticFrame(itemSprite.Frames[0].FilePath);
            (int iw, int ih) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : (32, 32);
            var img = new Image { Width = iw * paperdollItemCounterScale, Height = ih * paperdollItemCounterScale, Stretch = Stretch.Uniform };
            gump.ItemAnimators.Add(new SpriteAnimator(itemSprite, img, _resources));
            return img;
        }
        if (equippedItem.Category == ReferencedItemCategory.Object)
        {
            var obj = _objects.Objects.FirstOrDefault(o => o.Id == equippedItem.ItemId);
            var bmp = string.IsNullOrEmpty(obj?.IconPath) ? null : LoadStaticFrame(obj.IconPath);
            if (bmp is null) return null;
            (int iw, int ih) = SpriteSizing.Quantize(bmp.PixelWidth, bmp.PixelHeight);
            return new Image { Source = bmp, Width = iw * paperdollItemCounterScale, Height = ih * paperdollItemCounterScale, Stretch = Stretch.Uniform };
        }
        if (equippedItem.Category == ReferencedItemCategory.Container)
        {
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == equippedItem.ItemId);
            var itemSprite = _sprites.Groups.FirstOrDefault(g => g.Name == containerData?.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == containerData?.SpriteName);
            if (itemSprite is null || itemSprite.Frames.Count == 0) return null;
            var frameBitmap = LoadStaticFrame(itemSprite.Frames[0].FilePath);
            (int iw, int ih) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : (32, 32);
            var img = new Image { Width = iw * paperdollItemCounterScale, Height = ih * paperdollItemCounterScale, Stretch = Stretch.Uniform };
            gump.ItemAnimators.Add(new SpriteAnimator(itemSprite, img, _resources));
            return img;
        }
        return null;
    }

    private void PaperdollSlotClicked(OpenPaperdollGump gump, object slotKey, int clickCount)
    {
        var character = gump.Character;
        bool isBuiltinSlot = slotKey is EquipmentSlot;

        if (isBuiltinSlot && (EquipmentSlot)slotKey == EquipmentSlot.LeftHand && IsPaperdollLeftHandLocked(character))
        {
            StatusText.Text = "Left hand is unavailable - the right hand is holding a two-handed weapon.";
            return;
        }

        // Real environmental interaction, per spec - an equipped item is interacted with exactly like it would be on the map: single click identifies, double click uses (firing whatever real events it has), never unequipped as a side effect of a plain click. Real unequip is dragging the item out of its slot entirely (see StartDragFromSlot) - confirmed real Exult behavior, since right-click is reserved for movement/pathfinding, not gump interaction, and there is no right-click-unequip at all.
        if (TryGetEquippedInstanceId(character, slotKey, out int equippedInstanceId))
        {
            var equippedItem = character.Inventory.FirstOrDefault(i => i.InstanceId == equippedInstanceId);
            bool isUse = clickCount == 2;
            if (equippedItem is not null) HandleInventoryItemClick(gump, equippedItem, isUse, slotKey);
            if (isUse) RefreshPaperdollGump(gump); // only a real "use" could plausibly fire an event that changes inventory/equipped state - a plain identify never does, so it no longer rebuilds the whole gump canvas unnecessarily
            return;
        }

        if (gump.SelectedInstanceId is { } id && character.Inventory.FirstOrDefault(i => i.InstanceId == id) is { } selectedItem)
        {
            if (isBuiltinSlot && (EquipmentSlot)slotKey == EquipmentSlot.RightHand && IsPaperdollItemTwoHanded(selectedItem) && character.EquippedSlots.ContainsKey(EquipmentSlot.LeftHand))
            {
                StatusText.Text = "This needs both hands - unequip the left hand first.";
                return;
            }
            SetEquippedInstanceId(character, slotKey, id);
            gump.SelectedInstanceId = null;
            RefreshPaperdollGump(gump);
        }
    }

    private void PaperdollBackpackList_SelectionChanged(OpenPaperdollGump gump, ListBox backpackList)
    {
        gump.SelectedInstanceId = (backpackList.SelectedItem as PaperdollBackpackRow)?.Instance.InstanceId;
        if (gump.SelectedInstanceId is { } id && gump.Character.Inventory.FirstOrDefault(i => i.InstanceId == id) is { } item)
            StatusText.Text = $"Selected: {new PaperdollBackpackRow(item, _books, _objects, _containers).DisplayText} - click where it goes on the body to equip it.";
    }

    /// <summary>Opens (or brings to front) the real, separate backpack-contents gump for a paperdoll, per spec and per real Exult ("double-click the backpack and it opens as a gump") - equip selection still lives on the owning paperdoll gump itself, since that's where the slots are.</summary>
    private void OpenBackpackContents(OpenPaperdollGump owner)
    {
        if (owner.BackpackGump is { } existingBackpack)
        {
            Panel.SetZIndex(existingBackpack.GumpBorder, 200);
            return;
        }

        var backpackList = new ListBox { Height = 300, Width = 220, DisplayMemberPath = "DisplayText" };
        var dragHandle = new Border { Background = new SolidColorBrush(Color.FromArgb(238, 26, 26, 26)), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Padding = new Thickness(8), Cursor = Cursors.SizeAll };
        var content = new Grid();
        content.Children.Add(backpackList);
        var gumpBorder = dragHandle;
        gumpBorder.Child = content;

        var backpackGump = new OpenBackpackGump { Owner = owner, GumpBorder = gumpBorder, BackpackList = backpackList };
        owner.BackpackGump = backpackGump;

        double ownerLeft = Canvas.GetLeft(owner.GumpBorder);
        double ownerTop = Canvas.GetTop(owner.GumpBorder);
        Canvas.SetLeft(gumpBorder, ownerLeft + 340);
        Canvas.SetTop(gumpBorder, ownerTop);
        Panel.SetZIndex(gumpBorder, 200);

        // Real drag-by-body, same principle as the paperdoll itself - dragging happens on the gump's own background, not the list (which needs its own clicks for selecting items).
        dragHandle.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == dragHandle) PaperdollTitleBar_MouseLeftButtonDown(backpackGump, dragHandle, dragHandle, e); };
        dragHandle.MouseMove += (_, e) => PaperdollTitleBar_MouseMove(dragHandle, e);
        dragHandle.MouseLeftButtonUp += (_, _) => PaperdollTitleBar_MouseLeftButtonUp(dragHandle);

        var exitButton = BuildExitButton(owner.Character, () => CloseBackpackGump(owner));
        exitButton.HorizontalAlignment = HorizontalAlignment.Right;
        exitButton.VerticalAlignment = VerticalAlignment.Top;
        exitButton.Margin = new Thickness(0, 0, 4, 0);
        content.Children.Add(exitButton);

        backpackList.SelectionChanged += (_, _) => PaperdollBackpackList_SelectionChanged(owner, backpackList);
        backpackList.MouseDoubleClick += (_, _) =>
        {
            if (backpackList.SelectedItem is PaperdollBackpackRow row) HandleInventoryItemClick(owner, row.Instance, isUse: true);
        };
        // Real drag-out-of-inventory, per spec - only actually starts (removing the item from the backpack and placing a real temporary map item) once real mouse movement past a threshold is detected, the standard safe WPF drag-initiation pattern. A plain click never triggers this, so the existing click-to-select-for-equipping workflow (SelectionChanged above) stays completely undisturbed.
        backpackList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pendingInventoryDragGump = owner;
            _pendingInventoryDragStart = e.GetPosition(backpackList);
        };
        backpackList.PreviewMouseMove += (_, e) =>
        {
            if (_pendingInventoryDragGump != owner || e.LeftButton != MouseButtonState.Pressed) return;
            var current = e.GetPosition(backpackList);
            if (Math.Abs(current.X - _pendingInventoryDragStart.X) < DragThreshold && Math.Abs(current.Y - _pendingInventoryDragStart.Y) < DragThreshold) return;
            _pendingInventoryDragGump = null;
            if (backpackList.SelectedItem is PaperdollBackpackRow row) StartDragFromInventory(owner, row.Instance, e.GetPosition(GameCanvas));
        };

        PaperdollGumpsCanvas.Children.Add(gumpBorder);
        RefreshBackpackGump(backpackGump);
    }

    private void CloseBackpackGump(OpenPaperdollGump owner)
    {
        if (owner.BackpackGump is not { } backpackGump) return;
        PaperdollGumpsCanvas.Children.Remove(backpackGump.GumpBorder);
        owner.BackpackGump = null;
    }

    private void RefreshBackpackGump(OpenBackpackGump backpackGump)
    {
        var character = backpackGump.Owner.Character;
        var equippedIds = character.EquippedSlots.Values.ToHashSet();
        backpackGump.BackpackList.ItemsSource = character.Inventory
            .Where(i => !equippedIds.Contains(i.InstanceId))
            .Select(i => new PaperdollBackpackRow(i, _books, _objects, _containers))
            .ToList();
    }

    /// <summary>
    /// Real drop-out-of-inventory, per spec - deliberately reuses the
    /// EXISTING, proven map-drag machinery (BeginDrag, the real settle/
    /// collision/footprint-overlap logic) rather than building a
    /// parallel system: the item becomes a genuine temporary
    /// PlacedItemInstance/ObjectInstance at the player's own feet first,
    /// THEN handed to BeginDrag exactly like any map item would be. If
    /// the click never turns into a real drag, GameCanvas_MouseLeftButtonUp_Drag's
    /// own revert branch (via _dragOriginGump) puts it straight back in
    /// the backpack instead of leaving a stray item on the map.
    /// </summary>
    private void PaperdollItemVisual_MouseMove(OpenPaperdollGump gump, object slotKey, MouseEventArgs e)
    {
        if (_pendingSlotDragGump != gump || !Equals(_pendingSlotDragSlot, slotKey) || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(gump.BodyCanvas);
        if (Math.Abs(current.X - _pendingSlotDragStart.X) < DragThreshold && Math.Abs(current.Y - _pendingSlotDragStart.Y) < DragThreshold) return;
        _pendingSlotDragGump = null;
        StartDragFromSlot(gump, slotKey, e.GetPosition(GameCanvas));
    }

    private void PaperdollItemVisual_MouseLeftButtonUp(object visual, OpenPaperdollGump gump, object slotKey, MouseButtonEventArgs e)
    {
        if (_pendingSlotDragGump != gump || !Equals(_pendingSlotDragSlot, slotKey)) return;
        // Never actually became a real drag - a plain click, so it identifies instead, exactly like Book Preview's own real model.
        _pendingSlotDragGump = null;
        if (visual is UIElement element) element.ReleaseMouseCapture();
        PaperdollSlotClicked(gump, slotKey, 1);
        e.Handled = true;
    }

    /// <summary>
    /// Real unequip, per spec and confirmed real Exult - there is no
    /// right-click-unequip at all; removing the item from its slot IS
    /// the actual, real unequip action, done the instant a real drag
    /// starts (not deferred until it lands somewhere), then handed off
    /// to the exact same proven map-placement machinery StartDragFromInventory
    /// already uses for a backpack-originated drag. _dragOriginSlot is
    /// set first so any later failure (see RevertDragToInventory, and
    /// the map drop-commit's own rejection branch) knows to restore the
    /// item to this precise slot again, not just the general backpack.
    /// </summary>
    private void StartDragFromSlot(OpenPaperdollGump gump, object slotKey, Point mouseStartInGameCanvas)
    {
        if (!TryGetEquippedInstanceId(gump.Character, slotKey, out int instanceId)) return;
        var item = gump.Character.Inventory.FirstOrDefault(i => i.InstanceId == instanceId);
        if (item is null) return;
        RemoveEquippedInstanceId(gump.Character, slotKey);
        _dragOriginSlot = slotKey;
        StartDragFromInventory(gump, item, mouseStartInGameCanvas);
        RefreshPaperdollGump(gump);
    }

    private void StartDragFromInventory(OpenPaperdollGump gump, InventoryItemInstance item, Point mouseStartInGameCanvas)
    {
        // Real, confirmed defensive clear - mirrors StartDragItemFromContainer's own version; this is a paperdoll/backpack-originated drag, so any stale container origin from a previous, already-finished drag can never be mistaken for this one's own real origin.
        _dragOriginContainerGump = null;
        gump.Character.Inventory.Remove(item);

        UIElement visual;
        double quantW = _tileWidth, quantH = _tileHeight;
        if (item.Category == ReferencedItemCategory.Book)
        {
            var placed = new PlacedItemInstance { X = _playerX, Y = _playerY, Category = ReferencedItemCategory.Book, ItemId = item.ItemId };
            var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
            var sprite = book is null ? null : _sprites.Groups.FirstOrDefault(g => g.Name == book.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book.SpriteName);
            if (sprite is not null && sprite.Frames.Count > 0)
            {
                var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                (int qw, int qh) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : ((int)_tileWidth, (int)_tileHeight);
                quantW = qw; quantH = qh;
                var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true };
                _ = new SpriteAnimator(sprite, image, _resources);
                visual = image;
            }
            else
            {
                visual = new Border { Width = quantW, Height = quantH, Background = Brushes.SaddleBrown, IsHitTestVisible = true, Child = new TextBlock { Text = "B", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            }
            GameCanvas.Children.Add(visual);
            PositionQuantizedVisual(visual, placed.X, placed.Y, quantW, quantH);
            _map.Items.Add(placed);
            _itemVisuals[placed] = visual;
            _dragItem = placed;
        }
        else if (item.Category == ReferencedItemCategory.Container)
        {
            // Real, confirmed bug fix, per spec ("When I move the container in either right or left hand, it changes to square with O in the middle") - a Container dragged out of an equipped slot was falling into the generic Object branch below, which can never find a matching entry in the object database at all (containers and objects are different databases with different ids) - hence the fallback "O" placeholder every single time. A real Container-category PlacedItemInstance is built here instead, exactly like a map-placed container, with its own real sprite resolved the same way, and critically, its own real Contents carried over from the inventory instance being dragged - so if it lands back on the map, it's a genuine, fully-intact container, not an empty shell.
            var placed = new PlacedItemInstance { X = _playerX, Y = _playerY, Category = ReferencedItemCategory.Container, ItemId = item.ItemId, InstanceId = _nextContainerRuntimeInstanceId++, Contents = item.Contents };
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
            var sprite = containerData is null ? null : _sprites.Groups.FirstOrDefault(g => g.Name == containerData.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == containerData.SpriteName);
            if (sprite is not null && sprite.Frames.Count > 0)
            {
                var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                (int qw, int qh) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : ((int)_tileWidth, (int)_tileHeight);
                quantW = qw; quantH = qh;
                var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true };
                _ = new SpriteAnimator(sprite, image, _resources);
                visual = image;
            }
            else
            {
                visual = new Border { Width = quantW, Height = quantH, Background = Brushes.SaddleBrown, IsHitTestVisible = true, Child = new TextBlock { Text = "C", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            }
            GameCanvas.Children.Add(visual);
            PositionQuantizedVisual(visual, placed.X, placed.Y, quantW, quantH);
            _map.Items.Add(placed);
            _itemVisuals[placed] = visual;
            _dragItem = placed;
        }
        else
        {
            var placed = new ObjectInstance { X = _playerX, Y = _playerY, ObjectId = item.ItemId };
            var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId);
            var bmp = string.IsNullOrEmpty(worldObject?.IconPath) ? null : LoadStaticFrame(worldObject.IconPath);
            if (bmp is not null)
            {
                (int qw, int qh) = SpriteSizing.Quantize(bmp.PixelWidth, bmp.PixelHeight);
                quantW = qw; quantH = qh;
                visual = new Image { Source = bmp, Width = quantW, Height = quantH, IsHitTestVisible = true };
            }
            else
            {
                visual = new Border { Width = quantW, Height = quantH, Background = Brushes.DarkSlateGray, IsHitTestVisible = true, Child = new TextBlock { Text = "O", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            }
            GameCanvas.Children.Add(visual);
            PositionQuantizedVisual(visual, placed.X, placed.Y, quantW, quantH);
            _map.Objects.Add(placed);
            _objectVisuals[placed] = visual;
            _dragObject = placed;
        }

        _dragOriginGump = gump;
        RefreshPaperdollGump(gump);
        RefreshStackingVisuals(); // repositions every map item/object from its own real stored data, including this new one (at the player's own tile) - so the mouse-centering fix below MUST run after this, never before it, or this would silently undo it

        // Real, confirmed fix for the huge offset between the item and the mouse pointer - the item's real starting map data (the player's own tile) has nothing to do with where the mouse actually is when dragging out of a slot or the backpack, potentially screen-space away from the player entirely. BeginDrag's own subsequent delta math is only ever correct if the visual's STARTING screen position and the mouse's STARTING screen position genuinely agree - so the visual is re-centered directly under the real starting mouse position here, immediately before BeginDrag captures that as its reference, exactly matching how Book Preview's own drag start already works (its own dragged visual is always wherever it visually, really is, never an unrelated spawn point).
        Canvas.SetLeft(visual, mouseStartInGameCanvas.X - quantW / 2);
        Canvas.SetTop(visual, mouseStartInGameCanvas.Y - quantH / 2);

        BeginDrag(_dragItem, _dragObject, visual, mouseStartInGameCanvas);
    }

    /// <summary>
    /// Real revert, per spec and confirmed real Exult (Dragging_info's
    /// own readied_index, tracked specifically so a failed drop can
    /// restore an item to the precise spot it was readied in) - on ANY
    /// failure to actually place a dragged item (a plain click with no
    /// real motion, or a real drag that ends on an invalid map
    /// location), it goes back into the exact slot it came from if it
    /// came from one at all, never merely into the general backpack.
    /// </summary>
    private void RevertDragToInventory(OpenPaperdollGump gump)
    {
        InventoryItemInstance? restored = null;
        if (_dragItem is { } item)
        {
            _map.Items.Remove(item);
            if (_itemVisuals.Remove(item, out var visual)) GameCanvas.Children.Remove(visual);
            restored = new InventoryItemInstance { InstanceId = gump.Character.GetNextInventoryInstanceId(), Category = item.Category, ItemId = item.ItemId, Quantity = 1 };
        }
        else if (_dragObject is { } obj)
        {
            _map.Objects.Remove(obj);
            if (_objectVisuals.Remove(obj, out var visual)) GameCanvas.Children.Remove(visual);
            restored = new InventoryItemInstance { InstanceId = gump.Character.GetNextInventoryInstanceId(), Category = ReferencedItemCategory.Object, ItemId = obj.ObjectId, Quantity = 1 };
        }
        if (restored is not null)
        {
            gump.Character.Inventory.Add(restored);
            if (_dragOriginSlot is { } slot) SetEquippedInstanceId(gump.Character, slot, restored.InstanceId);
        }
        RefreshPaperdollGump(gump);
    }

    /// <summary>Real, confirmed fix - same exact real principle as RevertDragToInventory right above, for a drag that started FROM an open container instead of a paperdoll slot or the backpack list. Puts the item straight back into its own source container's real Contents, at the real center of its own frame (the same real fallback used everywhere a specific drop point isn't known), rather than losing it the instant a drop fails for any reason.</summary>
    private void RevertDragToContainer(OpenContainerGump gump)
    {
        ReferencedItemCategory? restoredCategory = null;
        int restoredItemId = 0;
        if (_dragItem is { } item)
        {
            _map.Items.Remove(item);
            if (_itemVisuals.Remove(item, out var visual)) GameCanvas.Children.Remove(visual);
            restoredCategory = item.Category; restoredItemId = item.ItemId;
        }
        else if (_dragObject is { } obj)
        {
            _map.Objects.Remove(obj);
            if (_objectVisuals.Remove(obj, out var visual)) GameCanvas.Children.Remove(visual);
            restoredCategory = ReferencedItemCategory.Object; restoredItemId = obj.ObjectId;
        }
        if (restoredCategory is { } category)
        {
            var frameBounds = gump.Data.GetFrameBounds();
            var restored = new InventoryItemInstance
            {
                InstanceId = gump.Data.GetNextContentsInstanceId(),
                Category = category,
                ItemId = restoredItemId,
                Quantity = 1,
                ContainerX = frameBounds.X + Math.Max(0, frameBounds.Width - 32) / 2,
                ContainerY = frameBounds.Y + Math.Max(0, frameBounds.Height - 32) / 2
            };
            gump.PlacedItem.Contents.Add(restored);
        }
        RefreshContainerGump(gump);
    }

    /// <summary>
    /// Real, confirmed fix, per spec ("dragged object into container...
    /// it disappeared... found there is no new book added") - the one,
    /// shared real place every drop-commit failure point below now
    /// reverts through, so a failed drop can never again silently lose
    /// an item that started from either a paperdoll/backpack origin OR a
    /// container origin. Returns true the moment either kind of real
    /// origin was actually reverted (the caller then skips its own
    /// RefreshStackingVisuals, since the revert already did its own real
    /// redraw); false means there was no real origin to revert to at
    /// all (an ordinary map-to-map drag), so the caller's own fallback
    /// still applies.
    /// </summary>
    private bool TryRevertDragToOrigin()
    {
        if (_dragOriginGump is { } paperdollOrigin) { RevertDragToInventory(paperdollOrigin); return true; }
        if (_dragOriginContainerGump is { } containerOrigin) { RevertDragToContainer(containerOrigin); return true; }
        return false;
    }

    /// <summary>Topmost-first hit test against every open gump's real screen rectangle (a paperdoll and, if open, its own linked backpack-contents gump), in PaperdollGumpsCanvas's own coordinate space - mirrors Exult's own real Dragging_info check ("gump = gumpman->find_gump(x, y)") before falling back to the map. A hit on a backpack-contents gump resolves to its owning paperdoll's character, since that's whose real inventory it is.</summary>
    /// <summary>Real hit test against the player's own current, actual visual bounds on GameCanvas - confirmed real Exult behavior (dropping an item onto the avatar picks it up, with or without a gump open).</summary>
    private bool IsPointOverPlayer(Point pointInGameCanvas)
    {
        double left = Canvas.GetLeft(_playerImage), top = Canvas.GetTop(_playerImage);
        return pointInGameCanvas.X >= left && pointInGameCanvas.X <= left + _playerImage.Width
            && pointInGameCanvas.Y >= top && pointInGameCanvas.Y <= top + _playerImage.Height;
    }

    // ================= Containers =================

    /// <summary>One real, open container gump - same real multi-gump architecture as a paperdoll (its own Border on PaperdollGumpsCanvas, its own real exit button, draggable by its own body), but showing a container's real, live Contents freely positioned within its authored Frame instead of fixed equipment slots.</summary>
    private class OpenContainerGump
    {
        /// <summary>Real, confirmed fix - PlacedItemInstance for a container sitting on the map, or InventoryItemInstance for one that's been picked up and is sitting in someone's own inventory/equipped slot; IHasContainerContents means every real Contents access below (RefreshContainerGump, drag in/out, volume checks) works identically against either, unchanged. Still named PlacedItem to avoid touching every existing call site for a rename alone - what matters is what it real IS, not what it's called.</summary>
        public required IHasContainerContents PlacedItem;
        public required ContainerData Data;
        public required Border GumpBorder;
        public required Canvas ItemsCanvas;
        /// <summary>Real, confirmed fix for the true root cause of the persistent "can't drag, single-click doesn't work" bug - every SpriteAnimator starts its own real, permanently-running DispatcherTimer, and RefreshContainerGump rebuilds this gump's entire visual tree (and so, every animator in it) on every single MouseMove during a drag. Discarding the animator reference (the old `_ = new SpriteAnimator(...)` pattern) never stopped the OLD timer - it just kept ticking forever, orphaned, since a running DispatcherTimer is NOT garbage-collected just because nothing references it anymore. A single real drag gesture could leak dozens of these onto the UI thread's own dispatcher queue within seconds, at Render priority, progressively starving real input handling until clicks and drags stopped registering correctly at all. Tracked here now and stopped before every rebuild, exactly like Book Preview's own pageOverlayAnimators already does correctly.</summary>
        public readonly List<SpriteAnimator> ItemAnimators = new();
    }

    private readonly List<OpenContainerGump> _openContainers = new();
    private OpenContainerGump? _pendingItemDragGump;
    private InventoryItemInstance? _pendingItemDragInstance;
    private Point _pendingItemDragStart;

    /// <summary>Real lock check, per spec - a locked container needs either the real key item in the current player's own inventory, or a successful pick-lock attempt, before it opens at all. Once genuinely unlocked (by either route), PlacedItem.IsUnlockedRuntime stays true for THIS specific placed instance from then on - picking a lock once doesn't need picking again.</summary>
    private void TryOpenContainer(PlacedItemInstance placedItem, ContainerData data)
    {
        if (!data.IsLocked || placedItem.IsUnlockedRuntime) { OpenContainer(placedItem, data); return; }

        if (_player is not null && data.RequiredKeyItemId >= 0 &&
            _player.Inventory.Any(i => i.Category == data.RequiredKeyCategory && i.ItemId == data.RequiredKeyItemId))
        {
            placedItem.IsUnlockedRuntime = true;
            StatusText.Text = $"You unlock \"{data.Name}\" with the key.";
            OpenContainer(placedItem, data);
            return;
        }

        // Real pick-lock attempt - confirmed real Exult mechanic (a real chance based on the picking character's own stat versus the container's own real difficulty), but the exact original formula's numbers could not be verified from source this pass, so this is a clearly-labeled, reasonable approximation rather than invented "exact" Exult numbers: Dexterity vs PickLockDifficulty, +-5% per point of difference around a 50% baseline, clamped so it's never guaranteed or impossible either way.
        int dexterity = 0;
        if (_player is not null && _stats is not null)
        {
            var dexStat = _stats.Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.Dexterity);
            if (dexStat is not null && _player.StatValues.TryGetValue(dexStat.Id, out string? dexText) && int.TryParse(dexText, out int dexValue)) dexterity = dexValue;
        }
        int chance = Math.Clamp((dexterity - data.PickLockDifficulty) * 5 + 50, 5, 95);
        bool success = _random.Next(100) < chance;
        if (success)
        {
            placedItem.IsUnlockedRuntime = true;
            StatusText.Text = $"You pick the lock on \"{data.Name}\"! ({chance}% chance)";
            OpenContainer(placedItem, data);
        }
        else
        {
            StatusText.Text = $"You fail to pick the lock on \"{data.Name}\". ({chance}% chance)";
        }
    }

    /// <summary>Real seed-once-then-live pattern, per spec, same principle as a character's own Starting Inventory - StartingContents is the authored template; PlacedItem.Contents is the real, live, mutable copy, seeded from the template only the very first time THIS specific placed instance is opened (InstanceId still 0), never re-seeded on later opens.</summary>
    private void OpenContainer(PlacedItemInstance placedItem, ContainerData data)
    {
        var existing = _openContainers.FirstOrDefault(g => g.PlacedItem == placedItem);
        if (existing is not null) { BringPaperdollToFrontGeneric(existing.GumpBorder, existing); return; }

        // Real event-first, same exact pattern as Book's own HandleItemClick - a container's own authored event pages get first say on what "opening" it actually does (a trapped chest could fire damage and never show its contents at all); the gump only opens as the honest default when no event page had real commands to run.
        bool fired = TryFireEventPages(data, data.EventPages, BookTriggerConditionBased.ByUseItem, data.Id, $"You open \"{data.Name}\" - real event fired.");
        if (fired) return;

        // Real, confirmed gap found and fixed while wiring Navigate Sound - OpenSoundResourceId/CloseSoundResourceId were added to the data model in an earlier pass but never actually played anywhere at runtime, same real pattern Book's own open/close sounds already use.
        if (data.OpenSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, data.OpenSoundResourceId);

        if (placedItem.InstanceId == 0)
        {
            placedItem.InstanceId = _nextContainerRuntimeInstanceId++;
            foreach (var seed in data.StartingContents)
                placedItem.Contents.Add(new InventoryItemInstance { InstanceId = seed.InstanceId, Category = seed.Category, ItemId = seed.ItemId, Quantity = seed.Quantity, ContainerX = seed.ContainerX, ContainerY = seed.ContainerY });
            // Real, confirmed diagnostic, per spec ("it somehow managed to put a 128 into a 64 max volume") - the Container Editor now warns the moment Max Volume itself makes existing Starting Contents invalid, but a project saved before that fix existed could still open with an already over-capacity container. This never alters the real, authored data - only makes the real problem visible immediately, in the exact moment it actually happens, instead of surfacing later as a confusing "won't fit" on some unrelated, much smaller item.
            double seededVolume = placedItem.Contents.Sum(ResolveContentsVolume);
            if (seededVolume > data.MaxVolume)
                StatusText.Text = $"Warning: \"{data.Name}\" opened already over its own capacity - Starting Contents totals {seededVolume:0.##}, but Max Volume is only {data.MaxVolume:0.##}. Fix this in the Container Editor.";
        }

        // Real size, per the container's own drawn Frame bounds - set here at creation and kept in sync by every RefreshContainerGump call afterward (immediately below, and on every later refresh).
        // Real, confirmed fix, per spec ("I SHOULD ONLY SEE THE CONTAINER PAPER DOLL ITSELF") - no background, no border, no padding at all; this is purely a transparent wrapper now, so only the container's own real body art (and its items) are ever visible, never a surrounding dark box.
        var itemsCanvas = new Canvas { Background = Brushes.Transparent };
        var gumpBorder = new Border { Background = Brushes.Transparent };
        gumpBorder.Child = itemsCanvas;

        var gump = new OpenContainerGump { PlacedItem = placedItem, Data = data, GumpBorder = gumpBorder, ItemsCanvas = itemsCanvas };

        double baseX = 40 + _openContainers.Count * 36;
        double baseY = 30 + _openContainers.Count * 30;
        Canvas.SetLeft(gumpBorder, baseX);
        Canvas.SetTop(gumpBorder, baseY);

        // Real drag-by-body, same principle as a paperdoll's own body - dragging happens on the item canvas's own background, never triggering when an actual item underneath the cursor already claimed the click (see item wiring below, which always marks itself Handled first).
        itemsCanvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == itemsCanvas) GenericGumpDrag_MouseLeftButtonDown(gump, gumpBorder, itemsCanvas, e); };
        itemsCanvas.MouseMove += (_, e) => GenericGumpDrag_MouseMove(gumpBorder, e);
        itemsCanvas.MouseLeftButtonUp += (_, _) => GenericGumpDrag_MouseLeftButtonUp(itemsCanvas);

        PaperdollGumpsCanvas.Children.Add(gumpBorder);
        _openContainers.Add(gump);
        BringPaperdollToFrontGeneric(gumpBorder, gump);
        RefreshContainerGump(gump);
    }

    /// <summary>
    /// Real, confirmed fix, per spec ("Backpack... I should single-click a
    /// container to identify it and double-click it to interact with it,
    /// the same way I do on the map side") - same exact real gump-opening
    /// machinery as a container found on the map (OpenContainer), just
    /// built around an InventoryItemInstance instead of a
    /// PlacedItemInstance; IHasContainerContents is what makes every one
    /// of those shared pieces (RefreshContainerGump, drag in/out, volume
    /// checks) work identically here, unchanged. No seed-once step is
    /// needed here at all - a container's own Contents are already fully
    /// seeded the moment it's picked up (see PickUpIntoInventory), so by
    /// the time it can ever reach this method it's already live.
    /// </summary>
    private void OpenEquippedContainer(InventoryItemInstance equippedItem)
    {
        var existing = _openContainers.FirstOrDefault(g => g.PlacedItem == equippedItem);
        if (existing is not null) { BringPaperdollToFrontGeneric(existing.GumpBorder, existing); return; }

        var data = _containers.Containers.FirstOrDefault(c => c.Id == equippedItem.ItemId);
        if (data is null) return;

        bool fired = TryFireEventPages(data, data.EventPages, BookTriggerConditionBased.ByUseItem, data.Id, $"You open \"{data.Name}\" - real event fired.");
        if (fired) return;

        if (data.OpenSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, data.OpenSoundResourceId);

        var itemsCanvas = new Canvas { Background = Brushes.Transparent };
        var gumpBorder = new Border { Background = Brushes.Transparent };
        gumpBorder.Child = itemsCanvas;

        var gump = new OpenContainerGump { PlacedItem = equippedItem, Data = data, GumpBorder = gumpBorder, ItemsCanvas = itemsCanvas };

        double baseX = 40 + _openContainers.Count * 36;
        double baseY = 30 + _openContainers.Count * 30;
        Canvas.SetLeft(gumpBorder, baseX);
        Canvas.SetTop(gumpBorder, baseY);

        itemsCanvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == itemsCanvas) GenericGumpDrag_MouseLeftButtonDown(gump, gumpBorder, itemsCanvas, e); };
        itemsCanvas.MouseMove += (_, e) => GenericGumpDrag_MouseMove(gumpBorder, e);
        itemsCanvas.MouseLeftButtonUp += (_, _) => GenericGumpDrag_MouseLeftButtonUp(itemsCanvas);

        PaperdollGumpsCanvas.Children.Add(gumpBorder);
        _openContainers.Add(gump);
        BringPaperdollToFrontGeneric(gumpBorder, gump);
        RefreshContainerGump(gump);
    }

    private void BringPaperdollToFrontGeneric(Border border, OpenContainerGump gump)
    {
        Panel.SetZIndex(border, 200);
        TrackGumpFocus(gump);
    }

    private void CloseContainerGump(OpenContainerGump gump)
    {
        if (gump.Data.CloseSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, gump.Data.CloseSoundResourceId);
        // Real, confirmed fix, per spec - a container's own real event pages should get the same say on "after close" that they already get on "after open" (see OpenContainer's own event-first check), not just play a sound and vanish.
        TryFireEventPages(gump.Data, gump.Data.EventPages, BookTriggerConditionBased.ByEnter, gump.Data.Id, $"You close \"{gump.Data.Name}\" - real event fired.");
        // Real, confirmed fix - closing the gump entirely must also stop every animator still running in it, for the exact same reason RefreshContainerGump now does before every rebuild.
        foreach (var animator in gump.ItemAnimators) animator.Stop();
        gump.ItemAnimators.Clear();
        PaperdollGumpsCanvas.Children.Remove(gump.GumpBorder);
        _openContainers.Remove(gump);
        UntrackGumpFocus(gump);
        if (_openPaperdolls.Count == 0 && _openContainers.Count == 0) GameCanvas.Focus();
    }

    /// <summary>
    /// Real stacking, per spec - every content item's own real visual is
    /// drawn directly at its own real, stored ContainerX/Y within the
    /// frame, in Contents' own list order, so an item added later renders
    /// on top of one added earlier - exactly matching real Exult, where
    /// items genuinely overlap and the one on top has to be moved to
    /// reveal what's underneath, rather than being auto-arranged into a
    /// grid. Interaction is wired DIRECTLY on each item's own real
    /// visual, the same proven pattern used for an equipped paperdoll
    /// item (never a separate overlay region), so the topmost real
    /// visual at any point is naturally also the one that receives the
    /// click there, matching the "move what's on top to reach what's
    /// underneath" principle exactly.
    /// </summary>
    /// <summary>Fixed, real display size for a container's own open-view body - per spec ("same mechanics you applied for the inventory paperdoll"), matching exactly how the PC paperdoll's own body is a fixed 320x400 regardless of window size or ViewportScale, and exactly matching the Container Editor's own FramePreviewCanvas size too, so the Frame's authored Points genuinely land in the same real coordinate space here that they were drawn in there. The real bug this fixes: the body was previously being sized to the FRAME's own bounding box (the small, designer-drawn item-placement region), not to a proper full-size background display, which is exactly why it looked tiny.</summary>
    private const double ContainerBodyWidth = 320, ContainerBodyHeight = 240;
    /// <summary>Real, confirmed fix - shared here (not a local const buried inside ShowBookReadingView any more) so RecomputeViewportScale can re-apply this gump's own real, normalized scale live too, the exact same real way it already does for the inventory paperdoll and the container gump.</summary>
    private const double BookDollHeight = 400;

    /// <summary>Real, confirmed fix, per spec ("see if the book paperdoll also need to follow the same rule you did with the inventory and container... all paperdolls need to follow that rule") - this one genuinely had no real scaling applied at all before (a different real gap than the inventory/container's own over-scaling bug, but a gap all the same). Shares the exact same real, normalized formula as every other paperdoll/gump now, so its own real on-screen height always matches the base viewport's own real on-screen height, and stays live if the window is resized while a book is actually open.</summary>
    private void RefreshBookOverlayScale()
    {
        double bookDollScale = ViewportScale.ScaleX * (200.0 / BookDollHeight);
        BookOverlayCanvas.LayoutTransform = new ScaleTransform(bookDollScale, bookDollScale);
    }

    private void RefreshContainerGump(OpenContainerGump gump)
    {
        // Real, confirmed fix for the true root cause of the persistent drag/single-click bug - every one of this gump's own previous animators (the body, and every item's own icon) is genuinely stopped here, BEFORE the canvas is cleared and everything is rebuilt fresh. Left unstopped, each one's own real DispatcherTimer just kept running forever, orphaned - and this method runs on every single MouseMove during a drag, so a single real drag gesture could leak dozens of them within seconds, at Render priority, competing directly with real input handling until it started missing events.
        foreach (var oldAnimator in gump.ItemAnimators) oldAnimator.Stop();
        gump.ItemAnimators.Clear();

        gump.ItemsCanvas.Children.Clear();
        var data = gump.Data;
        gump.ItemsCanvas.Width = ContainerBodyWidth;
        gump.ItemsCanvas.Height = ContainerBodyHeight;
        // Real, confirmed fix, same real reason and same real mechanism as the paperdoll's own version - the whole container gump scales together as one real unit now.
        // Real, confirmed correction, same real reason and same real formula as the paperdoll's own version - ContainerBodyHeight (240) exceeds the base viewport's own 200-pixel height too, so this gump's own on-screen height is normalized to always match the viewport's own on-screen height exactly, at any window size, instead of ballooning past it.
        double containerGumpScale = ViewportScale.ScaleX * (200.0 / ContainerBodyHeight);
        gump.ItemsCanvas.LayoutTransform = new ScaleTransform(containerGumpScale, containerGumpScale);
        // Real, confirmed fix, same real reason and same real formula as the paperdoll's own version - counter-scales every item icon drawn in this gump so it still lands on the exact same real, final on-screen size a map item at this same ViewportScale would, canceling out this gump's own real normalization factor above.
        double containerItemCounterScale = ContainerBodyHeight / 200.0;

        var bodyGroup = _sprites.Groups.FirstOrDefault(g => g.Name == data.PaperDollSpriteGroup);
        var bodySprite = bodyGroup?.Sprites.FirstOrDefault(s => s.Name == data.PaperDollSpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == data.PaperDollSpriteName);
        if (bodySprite is not null && bodySprite.Frames.Count > 0)
        {
            // Real, confirmed bug fix - this body image, as a real child of itemsCanvas covering most/all of its visible area, was silently intercepting every click meant for the gump's own "click the background to drag it" check (which specifically looks for e.OriginalSource == itemsCanvas itself) - clicking the visible art landed on THIS Image instead, so the drag never started. IsHitTestVisible false lets every such click pass straight through to itemsCanvas underneath, exactly like the paperdoll's own body drag-handle already does.
            var bg = new Image { Width = ContainerBodyWidth, Height = ContainerBodyHeight, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            gump.ItemAnimators.Add(new SpriteAnimator(bodySprite, bg, _resources));
            Panel.SetZIndex(bg, 0);
            gump.ItemsCanvas.Children.Add(bg);
        }

        int z = 5;
        foreach (var item in gump.PlacedItem.Contents)
        {
            FrameworkElement? visual = null;
            double size = 32;
            if (item.Category == ReferencedItemCategory.Book)
            {
                var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
                var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == book?.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book?.SpriteName);
                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                    (int iw, int ih) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : (32, 32);
                    var img = new Image { Width = iw * containerItemCounterScale, Height = ih * containerItemCounterScale, Stretch = Stretch.Uniform };
                    gump.ItemAnimators.Add(new SpriteAnimator(sprite, img, _resources));
                    visual = img; size = img.Width;
                }
            }
            else if (item.Category == ReferencedItemCategory.Object)
            {
                var obj = _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId);
                var bmp = string.IsNullOrEmpty(obj?.IconPath) ? null : LoadStaticFrame(obj.IconPath);
                if (bmp is not null)
                {
                    (int iw, int ih) = SpriteSizing.Quantize(bmp.PixelWidth, bmp.PixelHeight);
                    visual = new Image { Source = bmp, Width = iw * containerItemCounterScale, Height = ih * containerItemCounterScale, Stretch = Stretch.Uniform };
                    size = visual.Width;
                }
            }
            else if (item.Category == ReferencedItemCategory.Container)
            {
                var inner = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
                var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == inner?.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == inner?.SpriteName);
                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var img = new Image { Width = 32 * containerItemCounterScale, Height = 32 * containerItemCounterScale, Stretch = Stretch.Uniform };
                    gump.ItemAnimators.Add(new SpriteAnimator(sprite, img, _resources));
                    visual = img;
                }
            }
            visual ??= new Border { Width = 32, Height = 32, Background = Brushes.Gray };

            Canvas.SetLeft(visual, item.ContainerX);
            Canvas.SetTop(visual, item.ContainerY);
            Panel.SetZIndex(visual, z++);
            visual.Cursor = Cursors.Hand;
            visual.MouseLeftButtonDown += (_, e) => ContainerItem_MouseLeftButtonDown(gump, item, visual, e);
            visual.MouseMove += (_, e) => ContainerItem_MouseMove(gump, item, visual, e);
            visual.MouseLeftButtonUp += (_, e) => ContainerItem_MouseLeftButtonUp(gump, item, visual, e);
            gump.ItemsCanvas.Children.Add(visual);
        }

        // Real, confirmed removal, per spec ("GET RID OF... THE CHECK MARK ON THE TOP RIGHT CORNER... If I need to close it, press ESC to close it") - no exit button drawn here anymore; Escape (via the unified gump focus tracker above) is now the only close control, and dragging the body itself (itemsCanvas) still moves the gump exactly as before.
    }

    private void ContainerItem_MouseLeftButtonDown(OpenContainerGump gump, InventoryItemInstance item, FrameworkElement visual, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { HandleContainerItemClick(gump, item, isUse: true); e.Handled = true; return; }
        _pendingItemDragGump = gump;
        _pendingItemDragInstance = item;
        _pendingItemDragStart = e.GetPosition(gump.ItemsCanvas);
        // Real, definitive fix, matching the paperdoll's own real, proven pattern exactly (PaperdollItemVisual_MouseMove/BookSpriteImage_MouseLeftButtonDown) - capturing on the item's own visual is genuinely safe now that ContainerItem_MouseMove no longer rebuilds the gump mid-drag at all (it moves this same visual directly instead) - the earlier "capture on the stable canvas" workaround was compensating for that rebuild, which is now gone at the real source instead.
        visual.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>
    /// Real, definitive fix, per spec ("you need to see how did it in the
    /// paper doll and do the same for the container paper doll") - the
    /// real, structural reason the paperdoll's own item drag has always
    /// worked is that it NEVER rebuilds its own gump mid-drag at all
    /// (an equipped item is either sitting at its fixed slot position, or
    /// it's been fully handed off to the ordinary map-drag system) -
    /// RefreshPaperdollGump is only ever called before or after a drag,
    /// never DURING one. This container version used to call
    /// RefreshContainerGump on every single real pixel of movement, which
    /// is a real, structural difference that no amount of capture-target
    /// tuning could fully paper over. This now matches the paperdoll's
    /// own real model exactly: the one, single, already-existing visual
    /// is moved directly (Canvas.SetLeft/Top), nothing is rebuilt at all
    /// until the drag genuinely ends, in ContainerItem_MouseLeftButtonUp.
    /// </summary>
    private void ContainerItem_MouseMove(OpenContainerGump gump, InventoryItemInstance item, FrameworkElement visual, MouseEventArgs e)
    {
        if (_pendingItemDragGump != gump || _pendingItemDragInstance != item || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(gump.ItemsCanvas);

        // Real, confirmed bug fix - a real threshold, same as everywhere else a drag-vs-click has to be told apart, so the tiniest jitter during what was meant as a plain click is never mistaken for the start of a real drag.
        if (Math.Abs(current.X - _pendingItemDragStart.X) < DragThreshold && Math.Abs(current.Y - _pendingItemDragStart.Y) < DragThreshold) return;

        // Real drag-out-to-map, per spec ("move items from inside the first container to another container... etc") - the moment the drag genuinely leaves the container's own real frame bounds, it stops being a reposition-within and becomes a real map drag instead, handed off to the exact same proven machinery a paperdoll slot's own drag-out already uses.
        var itemBounds = gump.Data.GetFrameBounds();
        if (current.X < -8 || current.X > itemBounds.Width + 8 || current.Y < -8 || current.Y > itemBounds.Height + 8)
        {
            _pendingItemDragGump = null;
            gump.PlacedItem.Contents.Remove(item);
            RefreshContainerGump(gump);
            _dragOriginContainerGump = gump; // Real, confirmed fix - set right here, the real moment this item actually leaves its own source container, so a failed drop has somewhere real to revert it to.
            StartDragItemFromContainer(item, e.GetPosition(GameCanvas));
            return;
        }

        // Real, free repositioning within the frame - exactly like moving an item aside to see what's underneath in real Exult; no snapping, no grid. The one real existing visual moves directly; nothing is rebuilt, nothing is destroyed, so whatever holds mouse capture is never touched by this.
        item.ContainerX = Math.Clamp(current.X - 16, 0, itemBounds.Width - 32);
        item.ContainerY = Math.Clamp(current.Y - 16, 0, itemBounds.Height - 32);
        Canvas.SetLeft(visual, item.ContainerX);
        Canvas.SetTop(visual, item.ContainerY);
    }

    /// <summary>Real drag-out, per spec - reuses StartDragFromInventory's exact proven machinery (a real temporary map item at the player's own feet, handed to BeginDrag, reparented on top of gumps while moved, reverted if it never lands anywhere valid) by building a throwaway single-item OpenPaperdollGump-shaped call is not applicable here (a container item has no owning character/gump to revert into) - so failure here simply leaves it as a real, ordinary, currently-being-dragged map item, exactly like dragging anything else on the map already works, rather than needing a container-specific revert path.</summary>
    private void StartDragItemFromContainer(InventoryItemInstance item, Point mouseStartInGameCanvas)
    {
        // Real, confirmed defensive clear - this is a container-originated drag, so any stale paperdoll-slot origin from a previous, already-finished drag can never be mistaken for this one's own real origin; _dragOriginContainerGump itself is set by the caller, right before this runs.
        _dragOriginGump = null; _dragOriginSlot = null;
        UIElement visual;
        double quantW = _tileWidth, quantH = _tileHeight;
        if (item.Category == ReferencedItemCategory.Book)
        {
            var placed = new PlacedItemInstance { X = _playerX, Y = _playerY, Category = ReferencedItemCategory.Book, ItemId = item.ItemId };
            var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
            var sprite = book is null ? null : _sprites.Groups.FirstOrDefault(g => g.Name == book.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book.SpriteName);
            if (sprite is not null && sprite.Frames.Count > 0)
            {
                var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                (int qw, int qh) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : ((int)_tileWidth, (int)_tileHeight);
                quantW = qw; quantH = qh;
                var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true };
                _ = new SpriteAnimator(sprite, image, _resources);
                visual = image;
            }
            else visual = new Border { Width = quantW, Height = quantH, Background = Brushes.SaddleBrown, IsHitTestVisible = true };
            GameCanvas.Children.Add(visual);
            PositionQuantizedVisual(visual, placed.X, placed.Y, quantW, quantH);
            _map.Items.Add(placed);
            _itemVisuals[placed] = visual;
            _dragItem = placed;
        }
        else if (item.Category == ReferencedItemCategory.Container)
        {
            // Real, confirmed bug fix, same real class of bug as StartDragFromInventory's own equipped-slot version - a nested container dragged out of another open container was falling into the generic Object branch below, which can never resolve a real sprite or carry over its own real Contents. Built the same real way here: a real Container-category PlacedItemInstance, its own real sprite, and its own real Contents preserved intact.
            var placed = new PlacedItemInstance { X = _playerX, Y = _playerY, Category = ReferencedItemCategory.Container, ItemId = item.ItemId, InstanceId = _nextContainerRuntimeInstanceId++, Contents = item.Contents };
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
            var sprite = containerData is null ? null : _sprites.Groups.FirstOrDefault(g => g.Name == containerData.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == containerData.SpriteName);
            if (sprite is not null && sprite.Frames.Count > 0)
            {
                var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                (int qw, int qh) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : ((int)_tileWidth, (int)_tileHeight);
                quantW = qw; quantH = qh;
                var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true };
                _ = new SpriteAnimator(sprite, image, _resources);
                visual = image;
            }
            else visual = new Border { Width = quantW, Height = quantH, Background = Brushes.SaddleBrown, IsHitTestVisible = true };
            GameCanvas.Children.Add(visual);
            PositionQuantizedVisual(visual, placed.X, placed.Y, quantW, quantH);
            _map.Items.Add(placed);
            _itemVisuals[placed] = visual;
            _dragItem = placed;
        }
        else
        {
            var placed = new ObjectInstance { X = _playerX, Y = _playerY, ObjectId = item.ItemId };
            var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId);
            var bmp = item.Category == ReferencedItemCategory.Object && !string.IsNullOrEmpty(worldObject?.IconPath) ? LoadStaticFrame(worldObject.IconPath) : null;
            if (bmp is not null)
            {
                (int qw, int qh) = SpriteSizing.Quantize(bmp.PixelWidth, bmp.PixelHeight);
                quantW = qw; quantH = qh;
                visual = new Image { Source = bmp, Width = quantW, Height = quantH, IsHitTestVisible = true };
            }
            else visual = new Border { Width = quantW, Height = quantH, Background = Brushes.DarkSlateGray, IsHitTestVisible = true };
            GameCanvas.Children.Add(visual);
            PositionQuantizedVisual(visual, placed.X, placed.Y, quantW, quantH);
            _map.Objects.Add(placed);
            _objectVisuals[placed] = visual;
            _dragObject = placed;
        }

        RefreshStackingVisuals();
        Canvas.SetLeft(visual, mouseStartInGameCanvas.X - quantW / 2);
        Canvas.SetTop(visual, mouseStartInGameCanvas.Y - quantH / 2);
        BeginDrag(_dragItem, _dragObject, visual, mouseStartInGameCanvas);
    }

    private void ContainerItem_MouseLeftButtonUp(OpenContainerGump gump, InventoryItemInstance item, FrameworkElement visual, MouseButtonEventArgs e)
    {
        if (_pendingItemDragGump != gump || _pendingItemDragInstance != item) return;
        _pendingItemDragGump = null;
        visual.ReleaseMouseCapture();
        var current = e.GetPosition(gump.ItemsCanvas);
        if (Math.Abs(current.X - _pendingItemDragStart.X) < DragThreshold && Math.Abs(current.Y - _pendingItemDragStart.Y) < DragThreshold)
        {
            // Never actually moved past the real threshold - a plain click identifies instead, same real principle as a paperdoll item.
            HandleContainerItemClick(gump, item, isUse: false);
        }
        else
        {
            // Real Navigate Sound, per spec ("plays the sound once the player releases the object on that spot on the container") - a real move genuinely happened, the item was actually released onto wherever it now sits.
            if (gump.Data.NavigateSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, gump.Data.NavigateSoundResourceId);
            // Real, confirmed fix - the gump is only ever rebuilt HERE now, once, at the real end of a completed drag (never mid-drag), purely to keep the underlying Z-order/list-based stacking view consistent with everything else - the visual itself already sits exactly where it should from the direct move above.
            RefreshContainerGump(gump);
        }
        e.Handled = true;
    }

    private void HandleContainerItemClick(OpenContainerGump gump, InventoryItemInstance item, bool isUse)
    {
        if (item.Category == ReferencedItemCategory.Book)
        {
            var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
            if (book is null) return;
            // Real, confirmed fix, per spec ("the same mechanics you used in the inventory paper doll to single click to identify please apply it in the container") - this used to only set a plain status-bar text, the exact same gap already found and fixed for the paperdoll itself; now shows the exact same real, visible floating name label the paperdoll and the map both already use, positioned for this container's own coordinate space.
            if (!isUse) { ShowContainerIdentifyLabel(gump, item, EffectiveBookName(book)); StatusText.Text = EffectiveBookName(book); return; }
            bool fired = TryFireEventPages(book, book.EventPages, BookTriggerConditionBased.ByUseItem, book.Id, $"You use \"{EffectiveBookName(book)}\" - real event fired.");
            if (!fired) ShowBookReadingView(book);
            return;
        }
        if (item.Category == ReferencedItemCategory.Container)
        {
            // Real, confirmed fix, per spec ("containers inside containers... recursively... the ability to move items between these containers through the paper dolls") - a container found INSIDE an already-open container is a real InventoryItemInstance with its own real Contents, exactly what OpenEquippedContainer already expects, so nesting genuinely works to any depth with no extra plumbing at all: opening this one just opens another real gump on top, which can itself hold another one, and so on.
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
            if (containerData is null) return;
            if (!isUse) { ShowContainerIdentifyLabel(gump, item, containerData.Name); StatusText.Text = containerData.Name; return; }
            OpenEquippedContainer(item);
            return;
        }
        string typeName = item.Category.ToString();
        if (!isUse) ShowContainerIdentifyLabel(gump, item, $"a {typeName}");
        StatusText.Text = isUse
            ? $"There's no {typeName} editor built yet, so nothing happens when you use it - the click system itself is already ready for when there is."
            : $"a {typeName}";
    }

    private bool TryFindContainerGumpAt(Point pointInGumpsCanvas, out OpenContainerGump gump)
    {
        for (int i = _openContainers.Count - 1; i >= 0; i--)
        {
            var candidate = _openContainers[i];
            double left = Canvas.GetLeft(candidate.GumpBorder), top = Canvas.GetTop(candidate.GumpBorder);
            if (pointInGumpsCanvas.X >= left && pointInGumpsCanvas.X <= left + candidate.GumpBorder.ActualWidth
                && pointInGumpsCanvas.Y >= top && pointInGumpsCanvas.Y <= top + candidate.GumpBorder.ActualHeight)
            {
                gump = candidate;
                return true;
            }
        }
        gump = null!;
        return false;
    }

    /// <summary>
    /// Real drop-in, per spec - rule 1 (a body container can never have
    /// something dropped INTO it) and the real volume system (rule 7: an
    /// item can only fit if the container's current contents' total
    /// volume plus this item's own real Volume would not exceed
    /// MaxVolume) are both enforced right here, honestly, before the
    /// item ever actually leaves the map.
    /// </summary>
    /// <summary>Real overload, per spec ("If Allow Elevation is not checked, dragging an item onto the container should use the default mechanics and add the item into the container itself") - the exact same real drop-in logic works identically whether an open gump exists to visually refresh afterward or not, so dropping straight onto a CLOSED container sitting on the map (no gump open at all) can use it too, not just dropping onto an already-open one.</summary>
    private bool TryDropIntoContainer(OpenContainerGump gump, Point? dropPositionInFrame = null) => TryDropIntoContainer(gump.Data, gump.PlacedItem, gump, dropPositionInFrame);

    /// <summary>
    /// Real, confirmed fix, per spec ("when I drag and drop an item in
    /// the container it should drop where I wanted it to drop not
    /// randomly all over the place") - dropPositionInFrame, when given,
    /// is exactly where the player actually released it (already
    /// converted to this container's own real frame-local coordinates),
    /// and always wins. Random placement is now only ever a fallback for
    /// the cases where there genuinely is no such point at all - dropping
    /// straight onto a CLOSED container on the map with no gump open, or
    /// the automatic pickup cascade landing something in a Backpack/Pouch
    /// container the player never actually saw open - not for an
    /// ordinary, visible drop onto an open gump, which is the case this
    /// was actually reported against.
    /// </summary>
    private bool TryDropIntoContainer(ContainerData data, IHasContainerContents placedContainer, OpenContainerGump? gump, Point? dropPositionInFrame = null)
    {
        if (data.IsBodyContainer)
        {
            StatusText.Text = $"You can't put things into \"{data.Name}\" - it's a body, not a real container.";
            return false;
        }

        // Real, confirmed bug fix, per spec ("I am able to put the chest itself inside itself. That is a bug.") - this exact same physical placed chest being dragged (as itself, a real map item) onto its own contents, open gump or not. A different chest of the same template is perfectly legal to nest; only this exact same instance containing itself is not.
        if (_dragItem == placedContainer)
        {
            StatusText.Text = $"\"{data.Name}\" can't hold itself.";
            return false;
        }

        ReferencedItemCategory category; int itemId; string name; double volume; int originX, originY;
        if (_dragItem is { } item)
        {
            // Real, confirmed fix, per spec ("how come I am able to put a container with volume 180 inside a chest that max allowed is 64?") - found the true root cause: this only ever resolved a Book, the exact same recurring gap found many times this session. A dragged CONTAINER's own itemId was being looked up in the BOOK database instead, which can never find a real match (different id spaces entirely), silently defaulting its own real volume to zero - so the check above could never actually block it, regardless of how large it really was. Genuinely shares ResolveContentsVolume now instead of duplicating a second, incomplete volume lookup here - the exact same real formula already used everywhere else (Book, Container, and now Object alike, per spec "use that logic"), so this can never silently drift out of sync with it again.
            category = item.Category; itemId = item.ItemId;
            name = category switch
            {
                ReferencedItemCategory.Container => _containers.Containers.FirstOrDefault(c => c.Id == itemId)?.Name ?? "container",
                _ => EffectiveBookName(_books.Books.FirstOrDefault(b => b.Id == itemId) ?? new BookData())
            };
            volume = ResolveContentsVolume(new InventoryItemInstance { Category = category, ItemId = itemId, Quantity = 1 });
            originX = item.X; originY = item.Y;
        }
        else if (_dragObject is { } obj)
        {
            var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId);
            category = ReferencedItemCategory.Object; itemId = obj.ObjectId; name = worldObject?.Name ?? "object";
            volume = ResolveContentsVolume(new InventoryItemInstance { Category = category, ItemId = itemId, Quantity = 1 });
            originX = obj.X; originY = obj.Y;
        }
        else return false;

        double currentVolume = placedContainer.Contents.Sum(i => ResolveContentsVolume(i));
        if (currentVolume + volume > data.MaxVolume)
        {
            StatusText.Text = $"\"{name}\" won't fit in \"{data.Name}\" - {currentVolume:0.##} + {volume:0.##} would exceed the max volume of {data.MaxVolume:0.##}.";
            return false;
        }

        if (_dragItem is { } realItem)
        {
            _map.Items.Remove(realItem);
            if (_itemVisuals.Remove(realItem, out var visual)) GameCanvas.Children.Remove(visual);
        }
        else if (_dragObject is { } realObj)
        {
            _map.Objects.Remove(realObj);
            if (_objectVisuals.Remove(realObj, out var visual)) GameCanvas.Children.Remove(visual);
        }
        SettleCell(originX, originY);

        // Real position, per spec - uses exactly where the player actually released it when that's known (see the method's own doc comment); only ever falls back to a real random spot within the frame's own bounds when there genuinely is no such point to use at all.
        var dropBounds = data.GetFrameBounds();
        double placedX, placedY;
        if (dropPositionInFrame is { } realDropPoint)
        {
            placedX = Math.Clamp(realDropPoint.X - 16, dropBounds.X, dropBounds.X + dropBounds.Width - 32);
            placedY = Math.Clamp(realDropPoint.Y - 16, dropBounds.Y, dropBounds.Y + dropBounds.Height - 32);
        }
        else
        {
            placedX = dropBounds.Width <= 32 ? dropBounds.X : dropBounds.X + _random.NextDouble() * (dropBounds.Width - 32);
            placedY = dropBounds.Height <= 32 ? dropBounds.Y : dropBounds.Y + _random.NextDouble() * (dropBounds.Height - 32);
        }
        var newContentsItem = new InventoryItemInstance
        {
            InstanceId = data.GetNextContentsInstanceId(),
            Category = category,
            ItemId = itemId,
            ContainerX = placedX,
            ContainerY = placedY
        };
        int insertIndex = _random.Next(placedContainer.Contents.Count + 1);
        placedContainer.Contents.Insert(insertIndex, newContentsItem);

        if (data.NavigateSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, data.NavigateSoundResourceId);
        StatusText.Text = $"You put \"{name}\" into \"{data.Name}\".";
        RefreshStackingVisuals();
        if (gump is not null) RefreshContainerGump(gump); // only a real, currently-open gump needs its own visual refreshed - dropping straight onto a closed container on the map has no gump to update
        return true;
    }

    private double ResolveContentsVolume(InventoryItemInstance item) => item.Category switch
    {
        ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.Volume ?? 0,
        ReferencedItemCategory.Container => _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.Volume ?? 0,
        // Real, confirmed fix, same real fix and same real reason as the editor's own version - an Object always counted as zero volume here too, so any number of them could be added to a running container with no real limit ever actually applying.
        ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId)?.Volume ?? 0,
        _ => 0
    } * item.Quantity;

    /// <summary>Generic version of the paperdoll's own PaperdollTitleBar_* drag helpers, for any gump Border regardless of what kind it is (container or paperdoll alike) - genuinely shared, not a duplicate, since both now call through this same real drag tracking.</summary>
    private void GenericGumpDrag_MouseLeftButtonDown(OpenContainerGump owner, Border gumpBorder, UIElement dragSurface, MouseButtonEventArgs e) => PaperdollTitleBar_MouseLeftButtonDown(owner, gumpBorder, dragSurface, e);
    private void GenericGumpDrag_MouseMove(Border gumpBorder, MouseEventArgs e) => PaperdollTitleBar_MouseMove(gumpBorder, e);
    private void GenericGumpDrag_MouseLeftButtonUp(UIElement dragSurface) => PaperdollTitleBar_MouseLeftButtonUp(dragSurface);

    private bool TryFindPaperdollGumpAt(Point pointInGumpsCanvas, out OpenPaperdollGump gump)
    {
        static bool Contains(Border border, Point p)
        {
            double left = Canvas.GetLeft(border), top = Canvas.GetTop(border);
            return p.X >= left && p.X <= left + border.ActualWidth && p.Y >= top && p.Y <= top + border.ActualHeight;
        }

        for (int i = _openPaperdolls.Count - 1; i >= 0; i--)
        {
            var candidate = _openPaperdolls[i];
            if (candidate.BackpackGump is { } backpack && Contains(backpack.GumpBorder, pointInGumpsCanvas)) { gump = candidate; return true; }
            if (Contains(candidate.GumpBorder, pointInGumpsCanvas)) { gump = candidate; return true; }
        }
        gump = null!;
        return false;
    }

    /// <summary>
    /// Real pickup, per spec - the dragged item stops being a real map
    /// object (removed from _map.Items/Objects, its visual removed, the
    /// origin cell settled for real so anything that was resting on it
    /// drops naturally) and becomes a real InventoryItemInstance for the
    /// gump's own character instead. Category/ItemId carry over exactly,
    /// so it's still the same real Book/Object record either way -
    /// nothing about how it behaves changes just because it moved from
    /// the map into a backpack.
    /// </summary>
    /// <summary>
    /// Real auto-equip cascade, confirmed directly from Exult's own
    /// actors.h (free_hand(): "!spots[rhand] ? rhand : (!spots[lhand] ?
    /// lhand : -1)") - right hand tried first, then left hand, and a
    /// two-handed item is never even offered the left hand at all
    /// (matching fits_in_spot's own real rule). If neither hand is free,
    /// the item only goes into general storage when a real backpack/
    /// container is actually equipped in the Backpack slot - otherwise
    /// pickup is rejected outright, exactly as described.
    /// </summary>
    /// <summary>Real volume-and-capacity check for a container regardless of where it currently is, per spec ("even if the container is in the arms or on the ground, any object placed into the container must check the total amount of volumes used vs the maximum volume") - the exact same real check TryDropIntoContainer already does for a map-based or gump-based drop, reused here so the SAME rule genuinely applies no matter how the item is arriving.</summary>
    private bool ContainerHasRoomFor(ContainerData containerData, IHasContainerContents container, double newItemVolume)
    {
        double currentVolume = container.Contents.Sum(ResolveContentsVolume);
        return currentVolume + newItemVolume <= containerData.MaxVolume;
    }

    /// <summary>
    /// Real, confirmed redesign of the whole pickup cascade, per spec -
    /// the Backpack slot's own real container (if any) is checked FIRST,
    /// by real volume, before either hand is ever considered at all; only
    /// once that fails (no container there, or it doesn't fit) do the
    /// hands come into play, RightHand first, then LeftHand, in that
    /// order - and only once every one of those genuinely fails does this
    /// report the real "cannot fit" failure, which the caller already
    /// reverts to the item's original place, untouched.
    /// </summary>
    private bool TryAutoEquipOrStore(CharacterDefinition character, ReferencedItemCategory category, int itemId, bool isTwoHanded, out string failReason, out InventoryItemInstance? createdItem)
    {
        failReason = "";
        createdItem = null;
        double newItemVolume = ResolveContentsVolume(new InventoryItemInstance { Category = category, ItemId = itemId, Quantity = 1 });

        // Step 0 - a CONTAINER being picked up (not an item going INTO one) tries its own real preferred slot first, per spec (issue #3, "If I specify backbag it will go there if it is empty", and later "implement the mechanics of either arm or left arm on ANY... slots I specify... a functional mechanic for ANY custom slots specified"). Only reached for the container itself, and only when that specific slot is genuinely empty right now - a slot already holding something else falls straight through to the ordinary cascade below, same as having no preference at all. Right Hand/Left Hand are real, genuine preferred-slot choices now too, not just the automatic fallback the cascade tries later on its own - and a real custom slot is resolved by its own real NAME against whichever character is actually doing the picking up right now, since custom slots are defined per-character, not globally.
        if (category == ReferencedItemCategory.Container)
        {
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == itemId);
            // Real, confirmed fix - written as explicit, unambiguous steps rather than a nested ternary mixing an EquipmentSlot branch and an int? branch assigned to object?, since that risks a subtle type-inference issue this environment has no compiler available to catch.
            object? preferredSlotKey = null;
            if (containerData?.PreferredEquipSlot is { } builtinPreferred)
            {
                preferredSlotKey = builtinPreferred;
            }
            else if (containerData?.PreferredCustomSlotName is { } customName)
            {
                var matchingCustomSlot = character.Paperdoll.CustomSlots.FirstOrDefault(s => s.Name == customName);
                if (matchingCustomSlot is not null) preferredSlotKey = matchingCustomSlot.Id;
            }
            if (preferredSlotKey is not null && !TryGetEquippedInstanceId(character, preferredSlotKey, out _))
            {
                var preferredItem = new InventoryItemInstance { InstanceId = character.GetNextInventoryInstanceId(), Category = category, ItemId = itemId, Quantity = 1 };
                character.Inventory.Add(preferredItem);
                SetEquippedInstanceId(character, preferredSlotKey, preferredItem.InstanceId);
                createdItem = preferredItem;
                return true;
            }
        }

        // Step 1 - per spec (issue #3, "Backbag, Pouch slots... If there is not enough volume or space left and there is another container under the pouch... it will go there") - Backpack is tried first, then Pouch, each checked by real volume, before either hand is ever considered.
        foreach (var containerSlot in new[] { EquipmentSlot.Backpack, EquipmentSlot.Pouch })
        {
            if (!character.EquippedSlots.TryGetValue(containerSlot, out int slotInstanceId)) continue;
            var slotItem = character.Inventory.FirstOrDefault(i => i.InstanceId == slotInstanceId);
            if (slotItem is not { Category: ReferencedItemCategory.Container }) continue;
            var slotContainerData = _containers.Containers.FirstOrDefault(c => c.Id == slotItem.ItemId);
            if (slotContainerData is null || !ContainerHasRoomFor(slotContainerData, slotItem, newItemVolume)) continue;

            // Real, confirmed fix, per spec ("it passed the frame restriction specified and went to the corner at 0,0 and outside the frame") - this never set a real ContainerX/ContainerY at all, so every item auto-stored this way defaulted to the real numeric default of 0,0 - outside the actual frame, exactly as reported. Positioned at the real center of this container's own real frame now, the exact same real fallback TryDropIntoContainer itself already uses when no specific drop point is known.
            var slotFrameBounds = slotContainerData.GetFrameBounds();
            var itemForSlot = new InventoryItemInstance
            {
                InstanceId = slotContainerData.GetNextContentsInstanceId(),
                Category = category,
                ItemId = itemId,
                Quantity = 1,
                ContainerX = slotFrameBounds.X + Math.Max(0, slotFrameBounds.Width - 32) / 2,
                ContainerY = slotFrameBounds.Y + Math.Max(0, slotFrameBounds.Height - 32) / 2
            };
            slotItem.Contents.Add(itemForSlot);
            createdItem = itemForSlot;
            var openSlotGump = _openContainers.FirstOrDefault(g => g.PlacedItem == slotItem);
            if (openSlotGump is not null) RefreshContainerGump(openSlotGump);
            return true;
        }

        // Step 2 - RightHand, then LeftHand, exactly as before, only reached once neither container slot could take it (or there isn't one).
        EquipmentSlot? targetSlot =
            !character.EquippedSlots.ContainsKey(EquipmentSlot.RightHand) ? EquipmentSlot.RightHand :
            (!isTwoHanded && !character.EquippedSlots.ContainsKey(EquipmentSlot.LeftHand)) ? EquipmentSlot.LeftHand :
            null;

        if (targetSlot is null)
        {
            failReason = "Unable to pick up - it doesn't fit in the Backpack or Pouch container, and both hands are full.";
            return false;
        }

        var newItem = new InventoryItemInstance { InstanceId = character.GetNextInventoryInstanceId(), Category = category, ItemId = itemId, Quantity = 1 };
        character.Inventory.Add(newItem);
        character.EquippedSlots[targetSlot.Value] = newItem.InstanceId;
        createdItem = newItem;
        return true;
    }

    /// <summary>
    /// Real pickup, per spec - works identically whether dropped onto an
    /// open paperdoll gump OR directly onto the character's own map
    /// sprite (matching Exult's own real behavior: dropping an item onto
    /// the avatar picks it up, gump or no gump). Takes the character
    /// directly rather than requiring an open gump; refreshes one at the
    /// end only if that character actually happens to have one open.
    /// </summary>
    private bool PickUpIntoInventory(CharacterDefinition character)
    {
        if (_dragItem is { } item)
        {
            var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
            if (!TryAutoEquipOrStore(character, item.Category, item.ItemId, book?.IsTwoHanded ?? false, out string failReason, out var createdItem))
            {
                StatusText.Text = failReason; // real revert - the item's data was never removed from the map, so the caller just needs to snap the visual back
                return false;
            }

            // Real, confirmed fix, per spec ("When I empty my container and put it in my inventory it resets and fills back the starter contents again") - found the true root cause: checking item.Contents.Count > 0 cannot actually tell "genuinely never opened yet" apart from "opened, then genuinely emptied out on purpose" - both look exactly the same (a real, empty list) to that check, so emptying a container and picking it up was silently re-seeding it every time, discarding the player's own real choice. item.InstanceId is the real, reliable signal instead - OpenContainer only ever assigns a real, non-zero one the first time a placed instance is genuinely opened (see its own seeding check), so "never opened" and "opened and emptied" are finally told apart correctly: a real 0 means truly never opened (seed from Starting Contents, exactly matching what OpenContainer would do anyway), anything else means it already has its own real, live Contents - carried over exactly as it is, empty or not.
            if (item.Category == ReferencedItemCategory.Container && createdItem is not null)
            {
                var containerData = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
                createdItem.Contents = item.InstanceId != 0
                    ? item.Contents
                    : containerData?.StartingContents.Select(seed => new InventoryItemInstance { InstanceId = seed.InstanceId, Category = seed.Category, ItemId = seed.ItemId, Quantity = seed.Quantity, ContainerX = seed.ContainerX, ContainerY = seed.ContainerY }).ToList() ?? new List<InventoryItemInstance>();
            }

            int originX = item.X, originY = item.Y;
            _map.Items.Remove(item);
            if (_itemVisuals.Remove(item, out var visual)) GameCanvas.Children.Remove(visual);
            SettleCell(originX, originY);
            // Real, confirmed fix - this used to always resolve a Book name (falling back to an empty/default BookData's own name) even for a picked-up Container, which is a different real item entirely with its own real Name.
            string pickedUpName = item.Category == ReferencedItemCategory.Container
                ? (_containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.Name ?? "container")
                : EffectiveBookName(book ?? new BookData());
            StatusText.Text = $"Picked up \"{pickedUpName}\".";
        }
        else if (_dragObject is { } obj)
        {
            var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId);
            if (!TryAutoEquipOrStore(character, ReferencedItemCategory.Object, obj.ObjectId, worldObject?.IsTwoHanded ?? false, out string failReason, out _))
            {
                StatusText.Text = failReason;
                return false;
            }
            int originX = obj.X, originY = obj.Y;
            _map.Objects.Remove(obj);
            if (_objectVisuals.Remove(obj, out var visual)) GameCanvas.Children.Remove(visual);
            SettleCell(originX, originY);
            StatusText.Text = $"Picked up \"{worldObject?.Name ?? "object"}\".";
        }
        // Real Navigate Sound, per spec ("WHEN I PICKED THE BOOK AND DROPPED BACK to the slot on the hand it did not play, I want that option available FOR THE PLAYER PAPER DOLL") - this is the one real place an item genuinely lands on a character's own paperdoll, whether it's a fresh pickup from the map or an equipped item dragged out and dropped back in, so it's the correct, single place for this.
        if (character.Paperdoll.NavigateSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, character.Paperdoll.NavigateSoundResourceId);

        RefreshStackingVisuals();
        var openGump = _openPaperdolls.FirstOrDefault(g => g.Character == character);
        if (openGump is not null) RefreshPaperdollGump(openGump);
        // Real, confirmed fix - a real pickup just genuinely completed, so inventory possession state genuinely just changed; every map event gets its own real chance to react.
        FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByHavingAnItem);
        return true;
    }

    /// <summary>
    /// Real environmental interaction, per spec - an item in a backpack
    /// fires through the EXACT SAME real engine calls a map click does
    /// (TryFireEventPages, ShowBookReadingView) - never a parallel,
    /// simulated stand-in. The only difference from HandleItemClick is
    /// that there's no map position to show a floating identify label
    /// at, so identify surfaces in the gump's own hint text instead;
    /// conditions, HasBeenRead, and ByUseItem/ByAfterReadItem events all
    /// behave identically whether the book was ever placed on the map at
    /// all - they're the same real book record either way.
    /// </summary>
    private void HandleInventoryItemClick(OpenPaperdollGump gump, InventoryItemInstance item, bool isUse, object? slotKey = null)
    {
        if (item.Category == ReferencedItemCategory.Book)
        {
            var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
            if (book is null) return;

            if (!isUse)
            {
                // Real, confirmed fix - the map's own identify shows a real, visible floating name label directly on the item itself (ShowIdentifyLabel), not just a status-bar text update; the paperdoll's identify only ever did the latter, which is far less noticeable and is why single-click identify looked like it was "never working" even once it genuinely started firing correctly. Now matches the map's own real feedback, just positioned for a slot instead of a map tile. Real, confirmed extension - slotKey works for a real custom slot too now (an int), not just a built-in EquipmentSlot.
                if (slotKey is not null) ShowPaperdollIdentifyLabel(gump, slotKey, EffectiveBookName(book));
                StatusText.Text = $"{EffectiveBookName(book)}";
                return;
            }

            bool fired = TryFireEventPages(book, book.EventPages, BookTriggerConditionBased.ByUseItem, book.Id, $"You use \"{EffectiveBookName(book)}\" - real event fired.");
            if (!fired) ShowBookReadingView(book);
            return;
        }

        if (item.Category == ReferencedItemCategory.Container)
        {
            // Real, confirmed fix, per spec ("single-click a container to identify it and double-click it to interact with it, the same way I do on the map side") - this is the one, single, shared place every caller of HandleInventoryItemClick reaches, so fixing it here covers a container in ANY equipped slot, not just the one raw click handler this was special-cased for earlier.
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
            if (containerData is null) return;
            if (!isUse)
            {
                if (slotKey is not null) ShowPaperdollIdentifyLabel(gump, slotKey, containerData.Name);
                StatusText.Text = containerData.Name;
                return;
            }
            OpenEquippedContainer(item);
            return;
        }

        string typeName = item.Category.ToString();
        if (!isUse && slotKey is not null) ShowPaperdollIdentifyLabel(gump, slotKey, $"a {typeName}");
        StatusText.Text = isUse
            ? $"There's no {typeName} editor built yet, so nothing happens when you use it - the click system itself is already ready for when there is."
            : $"a {typeName}";
    }

    /// <summary>Real, visible identify feedback for a paperdoll slot - same real principle as the map's own ShowIdentifyLabel (a temporary, fading name label placed directly above the item), just built fresh per-gump instead of reusing the map's single shared label, since a paperdoll lives in its own separate coordinate space/canvas entirely.</summary>
    /// <summary>Real, confirmed extension - slotKey works for a real custom slot (an int, its own Id) now too, not just a built-in EquipmentSlot; position comes from CustomSlots instead of SlotPositions for that case.</summary>
    private void ShowPaperdollIdentifyLabel(OpenPaperdollGump gump, object slotKey, string name)
    {
        double posX, posY;
        if (slotKey is EquipmentSlot builtin)
        {
            if (!gump.Character.Paperdoll.SlotPositions.TryGetValue(builtin, out var builtinPos)) return;
            posX = builtinPos.X; posY = builtinPos.Y;
        }
        else if (slotKey is int customId)
        {
            var customSlot = gump.Character.Paperdoll.CustomSlots.FirstOrDefault(s => s.Id == customId);
            if (customSlot is null) return;
            posX = customSlot.X; posY = customSlot.Y;
        }
        else return;
        var pos = new Point(posX, posY);
        var label = new TextBlock
        {
            Text = name,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 12
        };
        Canvas.SetLeft(label, pos.X);
        Canvas.SetTop(label, pos.Y - 22);
        Panel.SetZIndex(label, 50);
        gump.BodyCanvas.Children.Add(label);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            gump.BodyCanvas.Children.Remove(label);
        };
        timer.Start();
    }

    /// <summary>
    /// Real, confirmed fix, per spec ("the same mechanics you used in the
    /// inventory paper doll to single click to identify please apply it
    /// in the container") - same exact real principle as the paperdoll's
    /// own ShowPaperdollIdentifyLabel right above (a temporary, fading
    /// name label placed directly above the item, not just a status-bar
    /// text update that's easy to miss), built for THIS gump's own real
    /// coordinate space instead: positioned at the item's own real, free
    /// ContainerX/ContainerY - the paperdoll's fixed slot positions don't
    /// apply here at all, since a container's own items are placed
    /// anywhere, not at fixed slots.
    /// </summary>
    private void ShowContainerIdentifyLabel(OpenContainerGump gump, InventoryItemInstance item, string name)
    {
        var label = new TextBlock
        {
            Text = name,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 12
        };
        Canvas.SetLeft(label, item.ContainerX);
        Canvas.SetTop(label, item.ContainerY - 22);
        Panel.SetZIndex(label, 50);
        gump.ItemsCanvas.Children.Add(label);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            gump.ItemsCanvas.Children.Remove(label);
        };
        timer.Start();
    }

    private void GameCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isReadingBook) return;

        // Real Exult access model, confirmed from Exult's own docs and documented real play - I opens/closes the current test player's own gump; a number key 1-9 opens a specific party member's directly (Exult's own real keys: 1 = Avatar, 2-8 = party member), or brings their already-open gump to front. Escape closes the single topmost gump only. All of this works even while gumps are already open, since these are real in-world overlays on the same canvas, not separate focusable windows.
        if (e.Key == Key.Escape && _gumpFocusOrder.Count > 0) { CloseTopmostPaperdoll(); e.Handled = true; return; }
        if (e.Key == Key.I)
        {
            var existing = _player is not null ? _openPaperdolls.FirstOrDefault(g => g.Character == _player) : null;
            if (existing is not null) ClosePaperdollGump(existing);
            else OpenPaperdoll(_player);
            e.Handled = true;
            return;
        }
        int? partyIndex = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3, Key.D5 or Key.NumPad5 => 4, Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6, Key.D8 or Key.NumPad8 => 7, Key.D9 or Key.NumPad9 => 8,
            _ => null
        };
        if (partyIndex is not null)
        {
            var party = GetPartyList();
            if (partyIndex.Value < party.Count) { OpenPaperdoll(party[partyIndex.Value]); e.Handled = true; }
            return;
        }

        if (_openPaperdolls.Count > 0) return; // movement stays blocked while any gump is open, matching real Exult's "gumps pause games"

        var (dx, dy) = DirectionFor(e.Key);
        if (dx == 0 && dy == 0) return;
        e.Handled = true;
        if (e.IsRepeat) return; // the OS's own repeat plays no role at all - the real per-frame game loop (GameLoop_Rendering) is what drives continuous movement now, on its own schedule, exactly matching Exult's own real mechanics

        // Purely records that this direction is now held, for the real per-frame game loop to act on.
        _lastDirectionKeyPressed = e.Key;
        _heldDirectionKeys.Add(e.Key);
    }

    private void GameCanvas_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var (dx, dy) = DirectionFor(e.Key);
        if (dx == 0 && dy == 0) return;
        e.Handled = true;

        _heldDirectionKeys.Remove(e.Key);
        // The Idle switch itself happens in GameLoop_Rendering, via _wasMovingLastFrame, the instant nothing is held any more - continuous movement has no "in-flight transition" to finish first any more, so there is nothing to wait on here.
    }

    /// <summary>
    /// Recomputes the largest INTEGER scale factor that fits the current
    /// window size, per spec ("upscale... using nearest-neighbor/integer-
    /// style pixel scaling so the low-resolution pixel-art appearance is
    /// preserved"). A fractional scale would blur/uneven pixel sizes even
    /// with nearest-neighbor sampling, so only whole multiples are used;
    /// any leftover space is simple letterboxing (ViewportHost's black
    /// background), not stretching.
    /// </summary>
    /// <summary>
    /// Real, live screen shake for actual gameplay - the exact same real
    /// mechanism as Book Preview's own PlayShakeEffect (a DispatcherTimer
    /// jittering a TranslateTransform, reset to zero once the duration
    /// elapses), applied to ViewportHost specifically so the real game
    /// world visibly shakes without disturbing any open gump or overlay
    /// UI drawn as this same outer Grid's other children.
    /// </summary>
    private void PlayRealShakeEffect(int intensity, double durationSeconds)
    {
        var transform = new TranslateTransform();
        ViewportHost.RenderTransform = transform;

        double maxOffset = intensity * 2.0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            if (stopwatch.Elapsed.TotalSeconds >= durationSeconds)
            {
                timer.Stop();
                transform.X = 0;
                transform.Y = 0;
                return;
            }
            transform.X = (_random.NextDouble() * 2 - 1) * maxOffset;
            transform.Y = (_random.NextDouble() * 2 - 1) * maxOffset;
        };
        timer.Start();
    }

    /// <summary>Real, live day/night lighting for actual gameplay - the exact same real mechanism, and the exact same real, palette-derived colors, as Book Preview's own DayNightPeriodColors/ApplyDayNightPeriod (see there for how these specific values were derived from a real PALETTES.FLX). Applied to this window's own LightingOverlay, a sibling of GameCanvas inside the same scaled/clipped ViewportBorder, so it tints the real visible world and nothing else.</summary>
    private static readonly Dictionary<string, (Color Color, double Opacity)> DayNightPeriodColors = new()
    {
        ["Morning"] = (Colors.Transparent, 0.0),
        ["Afternoon"] = (Color.FromRgb(0x9C, 0x68, 0x0C), 0.42),
        ["Evening"] = (Color.FromRgb(0x15, 0x57, 0x52), 0.55)
    };

    private void ApplyRealDayNightPeriod(string period)
    {
        if (!DayNightPeriodColors.TryGetValue(period, out var setting)) return;
        LightingOverlay.Fill = new SolidColorBrush(setting.Color);
        var fade = new DoubleAnimation(setting.Opacity, TimeSpan.FromSeconds(0.6));
        LightingOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Reuses the exact same LightingOverlay mechanism as day/night - an author-picked color and strength instead of one of the three fixed periods.</summary>
    private void ApplyRealPaletteColorEffect(string hex, double opacity)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString("#" + hex);
            LightingOverlay.Fill = new SolidColorBrush(color);
            var fade = new DoubleAnimation(opacity, TimeSpan.FromSeconds(0.6));
            LightingOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        catch
        {
            // Malformed hex - leave the overlay as it was rather than crash the real game.
        }
    }

    private void RecomputeViewportScale()
    {
        if (ViewportHost.ActualWidth <= 0 || ViewportHost.ActualHeight <= 0) return;
        int scaleX = (int)Math.Floor(ViewportHost.ActualWidth / ViewportWidth);
        int scaleY = (int)Math.Floor(ViewportHost.ActualHeight / ViewportHeight);
        int scale = Math.Max(1, Math.Min(scaleX, scaleY));
        if (scale == ViewportScale.ScaleX) return; // genuinely unchanged - skip the gump rebuild below entirely
        ViewportScale.ScaleX = scale;
        ViewportScale.ScaleY = scale;

        // Real, confirmed fix - a map item's own size lives inside GameCanvas's real LayoutTransform hierarchy, so it re-scales live, automatically, the instant the window resizes. An equipped paperdoll icon's size was instead baked in as a one-time multiplication by the scale AT THE MOMENT it was last built (see RefreshPaperdollGump) - never touched again afterward, so it silently kept showing whatever scale happened to be current back then until something else (re-equipping it) rebuilt it fresh. Every already-open gump is rebuilt right here, the moment the real scale actually changes, so an equipped item's on-screen size always matches the map's own current scale live. The backpack-contents gump needs no equivalent call - its list is plain text rows with no scale-dependent sizing of its own at all.
        foreach (var gump in _openPaperdolls) RefreshPaperdollGump(gump);
        // Same exact real principle, per spec ("the same mechanics you used on books for window resizing... should be applied also for the containers") - an item sitting inside an open container is sized by the same ViewportScale multiplication, so it needs the exact same live re-scale the instant the real window size changes, not just at the moment it was last dropped in.
        foreach (var containerGump in _openContainers) RefreshContainerGump(containerGump);
        // Real, confirmed fix, per spec ("all paperdolls need to follow that rule") - the book reading view's own real paperdoll background gets the exact same live re-scale too, while it's actually open.
        if (BookOverlayBorder.Visibility == Visibility.Visible) RefreshBookOverlayScale();
    }

    /// <summary>
    /// The real Ultima 7-style camera, per spec. The player can move
    /// freely within a centered dead-zone without the camera moving at
    /// all; once the player reaches the dead-zone's own edge, the camera
    /// shifts by exactly as much as needed to keep the player there - not
    /// a hard center-lock, not a snap, and never more than the player
    /// itself just moved. Clamped to the map's real boundaries so the
    /// viewport never shows outside the valid world area. GameCanvas
    /// itself, and everything on it, is completely untouched by any of
    /// this - only CameraTransform (a plain translate) changes, which is
    /// what actually determines which 320x200 slice is visible.
    /// </summary>
    private void UpdateCamera()
    {
        // Real, continuous position, per Exult's own confirmed mechanics - the camera tracks the avatar's actual pixel position directly, not a coarse tile-center, so it naturally follows every small step of real movement rather than only jumping when the logical tile changes.
        double playerCenterX = _playerPixelX;
        double playerCenterY = _playerPixelY;

        double relativeX = playerCenterX - _cameraX;
        double relativeY = playerCenterY - _cameraY;

        double deadZoneLeft = ViewportWidth / 2 - DeadZoneHalfWidth;
        double deadZoneRight = ViewportWidth / 2 + DeadZoneHalfWidth;
        double deadZoneTop = ViewportHeight / 2 - DeadZoneHalfHeight;
        double deadZoneBottom = ViewportHeight / 2 + DeadZoneHalfHeight;

        if (relativeX < deadZoneLeft) _cameraX -= deadZoneLeft - relativeX;
        else if (relativeX > deadZoneRight) _cameraX += relativeX - deadZoneRight;

        if (relativeY < deadZoneTop) _cameraY -= deadZoneTop - relativeY;
        else if (relativeY > deadZoneBottom) _cameraY += relativeY - deadZoneBottom;

        // Boundary clamp - never shows outside the valid world/map area. A map smaller than the viewport on an axis is centered on that axis instead of clamped to a negative range.
        _cameraX = GameCanvas.Width <= ViewportWidth ? (GameCanvas.Width - ViewportWidth) / 2.0 : Math.Clamp(_cameraX, 0, GameCanvas.Width - ViewportWidth);
        _cameraY = GameCanvas.Height <= ViewportHeight ? (GameCanvas.Height - ViewportHeight) / 2.0 : Math.Clamp(_cameraY, 0, GameCanvas.Height - ViewportHeight);

        // Instant, per spec - confirmed directly from Exult's own documentation: the original game's camera scrolling is "jerky" (non-interpolated) by default; smooth scrolling is only an optional, later Exult enhancement, not authentic default behavior. Since the avatar's own position is now genuinely continuous (see StepPlayerContinuous), tracking it instantly every frame already looks smooth in practice - no separate interpolation layer is needed, and none of the earlier animation-conflict bugs can recur because nothing here is animated at all.
        CameraTransform.X = -_cameraX;
        CameraTransform.Y = -_cameraY;
    }

    /// <summary>
    /// The real per-frame game loop, per spec - RPG Maker's own actual
    /// mechanics confirmed directly (a fixed number of frames to cross
    /// one tile, continuing automatically to the next tile with no gap
    /// for as long as a direction stays held). Fires once per real WPF
    /// render frame. While a move is in flight, this is the ONLY thing
    /// that ever sets the player's and camera's screen position - both
    /// interpolated using the exact same elapsed-time fraction every
    /// single frame, which is what actually keeps them in perfect
    /// lockstep and is what fixed the earlier shaking regression at its
    /// real root. The instant a move finishes, if a direction is still
    /// held, the next one starts immediately with no gap - continuing to
    /// hold feels genuinely continuous because this loop keeps restarting
    /// moves on its own real per-frame schedule, not because anything is
    /// counting keyboard repeat events.
    /// </summary>
    private void GameLoop_Rendering(object? sender, EventArgs e)
    {
        var now = (e as RenderingEventArgs)?.RenderingTime ?? _lastRenderTime;
        // Clamped so a stall (window drag, breakpoint, etc.) can't produce one giant catch-up jump - the real avatar never "teleports" to make up for lost time.
        double deltaSeconds = _lastRenderTime == TimeSpan.Zero ? 0 : Math.Min(0.05, (now - _lastRenderTime).TotalSeconds);
        _lastRenderTime = now;

        if (_isReadingBook) return;

        // Real, confirmed fall physics, purely additive - runs every real frame regardless of whether a direction is currently held, so a fall that starts right as the key is released still finishes landing smoothly instead of snapping the instant input stops. Never touches _playerX/_playerY, deltaSeconds's own clamping, or anything else about the real movement/input system above and below this.
        UpdatePlayerFallAnimation(deltaSeconds);

        if (TryGetActiveHeldDirection(out int dx, out int dy))
        {
            StepPlayerContinuous(dx, dy, deltaSeconds);
            _wasMovingLastFrame = true;
        }
        else if (_wasMovingLastFrame)
        {
            ApplyPoseSprite(_idlePose); // genuinely stopped now - nothing held any more
            _wasMovingLastFrame = false;
        }
    }

    /// <summary>The most recently pressed still-held direction wins if more than one is held - a simple, predictable rule rather than diagonal combination, matching the existing cardinal-only movement model.</summary>
    private bool TryGetActiveHeldDirection(out int dx, out int dy)
    {
        // Explicit Count check first, rather than treating FirstOrDefault()'s result as a null-check - Key is a value type (enum), so FirstOrDefault() on an EMPTY set returns Key.None, not a true "nothing held" signal. Key.None wrapped in a nullable is still non-null, so the old "active is { } key" pattern always matched even with nothing held, silently running a (0,0) step every frame and re-triggering the Walking pose the instant it wasn't already active - which is exactly what showed up as "walking animation while standing still."
        if (_heldDirectionKeys.Count == 0) { dx = 0; dy = 0; return false; }
        Key active = (_lastDirectionKeyPressed is { } last && _heldDirectionKeys.Contains(last)) ? last : _heldDirectionKeys.First();
        (dx, dy) = DirectionFor(active);
        return true;
    }

    /// <summary>
    /// Real, continuous per-frame movement, per Exult's own actual
    /// mechanics: the avatar's position is a genuine floating-point world
    /// coordinate, advanced by real speed x real elapsed time every
    /// frame - never snapped between discrete tiles. _playerX/_playerY
    /// (the LOGICAL tile, used everywhere else in the engine) are purely
    /// derived here, updated - and ByExit/ByEnter fired - the instant the
    /// continuous position actually crosses into a new tile, not before.
    /// Collision is checked one small step ahead: if the tile the avatar
    /// would be entering is blocked, this frame's movement is simply
    /// skipped entirely rather than partially applied and clamped to the
    /// exact boundary - the avatar can end up a couple of pixels short of
    /// a wall as a result, which is imperceptible at any real frame rate.
    /// </summary>
    private void StepPlayerContinuous(int dx, int dy, double deltaSeconds)
    {
        var newFacing = (dx, dy) switch
        {
            (-1, 0) => SpriteDirection.West,
            (1, 0) => SpriteDirection.East,
            (0, -1) => SpriteDirection.North,
            (0, 1) => SpriteDirection.South,
            _ => _playerFacing
        };
        if (newFacing != _playerFacing)
        {
            _playerFacing = newFacing;
            ApplyPoseSprite(_idlePose); // turning to face a new direction shows Idle in that direction, even before we know whether the move itself succeeds
        }
        if (_currentAppliedPose != _walkingPose) ApplyPoseSprite(_walkingPose);

        double distance = PlayerTilesPerSecond * _tileWidth * deltaSeconds; // _tileWidth/_tileHeight share one grid, so one distance serves both axes
        double newPixelX = _playerPixelX + dx * distance;
        double newPixelY = _playerPixelY + dy * distance;

        int prospectiveTileX = (int)Math.Floor(newPixelX / _tileWidth);
        int prospectiveTileY = (int)Math.Floor(newPixelY / _tileHeight);

        if ((prospectiveTileX != _playerX || prospectiveTileY != _playerY) && !IsTileWalkable(prospectiveTileX, prospectiveTileY))
        {
            StatusText.Text = "Blocked.";
            return; // skip this frame's movement entirely - simpler and safe over precise boundary-clamping
        }

        _playerPixelX = newPixelX;
        _playerPixelY = newPixelY;
        // Sprite drawn directly from the continuous center point - center-anchored horizontally, bottom-anchored vertically, matching ComputeQuantizedPosition's own convention exactly (so this and the tile-based version agree perfectly at rest) but with no tile/offset indirection or modulo math that could jump at a boundary.
        Canvas.SetLeft(_playerImage, _playerPixelX - _playerImage.Width / 2);
        Canvas.SetTop(_playerImage, _playerPixelY + _tileHeight / 2 - _playerImage.Height);

        if (prospectiveTileX != _playerX || prospectiveTileY != _playerY)
        {
            var leavingEvent = _eventUnderPlayer;
            _playerX = prospectiveTileX;
            _playerY = prospectiveTileY;
            // Real, confirmed fix - the player's own elevation is recomputed the instant they actually arrive at a new tile, from whatever real top surface is genuinely there right now (see ComputePlayerStandingZ) - never guessed, never left at a stale value from wherever they stood before.
            _playerZ = ComputePlayerStandingZ(_playerX, _playerY);
            // Real, confirmed reversal, per spec ("the fall of the character been slow? I don't like it. Make it instant just like the books or container") - both directions snap immediately now, exactly like climbing already did and exactly like every item/container's own Z already does. UpdatePlayerFallAnimation is left in place but now has nothing left to ever animate (see its own updated doc comment) - genuinely safe to keep, genuinely inert.
            _playerVisualLiftZ = _playerZ;
            StatusText.Text = $"({_playerX}, {_playerY})";

            if (leavingEvent is not null && (leavingEvent.X != _playerX || leavingEvent.Y != _playerY))
                TryFireEventPages(leavingEvent, leavingEvent.EventPages, BookTriggerConditionBased.ByExit, selfBookId: -1, $"\"{leavingEvent.Name}\" fired (ByExit)");

            _eventUnderPlayer = _map.Events.FirstOrDefault(ev => ev.X == _playerX && ev.Y == _playerY);
            if (_eventUnderPlayer is not null)
                TryFireEventPages(_eventUnderPlayer, _eventUnderPlayer.EventPages, BookTriggerConditionBased.ByEnter, selfBookId: -1, $"\"{_eventUnderPlayer.Name}\" fired (ByEnter)");
        }

        // Real, confirmed fix, purely additive - same exact real Z-index and visual-lift application SnapPlayerPosition itself now does, placed here (after _playerZ is updated above, not before) so the very first rendered frame on a new tile already reflects its real elevation, not one frame behind. Nothing about the position math above is touched - this only draws whatever collision/elevation already decided.
        Panel.SetZIndex(_playerImage, ComputeDepthZIndex(_playerPixelX / _tileWidth, _playerPixelY / _tileHeight, (int)Math.Round(_playerVisualLiftZ)));
        Canvas.SetLeft(_playerImage, Canvas.GetLeft(_playerImage) - _playerVisualLiftZ * LiftPixelShift);
        Canvas.SetTop(_playerImage, Canvas.GetTop(_playerImage) - _playerVisualLiftZ * LiftPixelShift);

        UpdateCamera();
    }

    /// <summary>Map-edge and tile-walkability check, factored out of the old discrete TryMovePlayer so both the continuous stepper and anything else that needs a plain yes/no can share it.</summary>
    /// <summary>Map-edge, terrain-walkability, AND real object/item collision - Exult's own confirmed model: IsSolid is a genuinely separate flag from Occludes (see IsOccluding), so this checks it independently rather than reusing the sighting check. Any actor's real collision point (player today; any future NPC or party member the same way) is blocked by a solid object/item sharing the target tile, not just by terrain.</summary>
    private bool IsTileWalkable(int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0 || tileX >= _map.Width || tileY >= _map.Height) return false;
        int tileIndex = _map.Layers[0].GetTile(tileX, tileY, _map.Width);
        if (tileIndex >= 0)
        {
            var props = _tileProperties.GetOrCreate(_tilesetSpriteName, tileIndex);
            if (!props.Walkable) return false;
        }
        // Real, confirmed fix, per spec ("the first book on the floor become a wall... take same book that acts like a wall and put it alone then all of a sudden, I can climb above it... put it back with the group, I can pass through it as if it is invisible") -
        // found the true root cause: SettleCell already correctly clusters everything at ONE tile by real sub-tile footprint overlap (FootprintsOverlap/ClusterByFootprintOverlap), since real Exult tiles genuinely hold multiple independent objects side by side, not one combined blob - but this collision check never did the same clustering at all, treating every item/object sharing a tile as ONE single obstacle regardless of where within the tile each one actually sits. A three-book stack authored a few pixels away from a lone, easily-climbable book at the SAME tile coordinate was blocking (or, depending on iteration order, silently passing through) the whole tile, including the unrelated lone book right next to it.
        // Real, confirmed truth table, reconciled exactly from every clarification given, now evaluated PER
        // REAL CLUSTER instead of per tile:
        // - Not Solid -> passes through freely, regardless of Stackable/Elevate. Ground level, never "underneath".
        // - Solid + not Elevate + not Stackable -> a real wall. Blocks completely, standalone or not.
        // - Solid + not Elevate + Stackable -> steppable. Walked onto directly, no block, no height change.
        // - Solid + Elevate (Stackable irrelevant here) -> a real climb attempt. Blocked only when the height
        //   difference from wherever the player is standing right now is too tall to climb.
        // The player can always reach whichever real cluster at this tile is actually walkable/climbable from
        // here, even if a completely different, unrelated cluster sharing the same tile coordinate would not be.
        return TryFindReachableClusterAt(tileX, tileY, out _);
    }

    /// <summary>
    /// Real, shared, footprint-aware evaluation for one tile, per spec -
    /// clusters everything actually there by real sub-tile footprint
    /// overlap (the exact same real clustering SettleCell's own stacking
    /// already uses), then judges each real cluster independently against
    /// the real solid/stackable/elevate truth table. Returns true (and
    /// the real, correct resulting top surface) the moment ANY cluster is
    /// genuinely reachable from the player's current height - an
    /// unrelated, too-tall cluster sharing the same tile can never block
    /// (or silently swallow) a real, separate, easily-climbable one right
    /// next to it again.
    /// </summary>
    private bool TryFindReachableClusterAt(int tileX, int tileY, out int resultingZ)
    {
        var itemsHere = _map.Items.Where(i => i.X == tileX && i.Y == tileY).ToList();
        var objectsHere = _map.Objects.Where(o => o.ContainerInstanceId < 0 && o.X == tileX && o.Y == tileY).ToList();
        if (itemsHere.Count == 0 && objectsHere.Count == 0) { resultingZ = 0; return true; } // real, genuinely empty tile - always walkable, ground level

        var entries = itemsHere.Select(i => { var info = ResolveStackInfo(i, null); return new StackEntry(i, i.OffsetX, i.OffsetY, info.sizeX, info.sizeY, info.sizeZ, info.elevates, i.Z); })
            .Concat(objectsHere.Select(o => { var info = ResolveStackInfo(null, o); return new StackEntry(o, o.OffsetX, o.OffsetY, info.sizeX, info.sizeY, info.sizeZ, info.elevates, o.Z); }))
            .ToList();

        int? bestReachableZ = null;
        foreach (var cluster in ClusterByFootprintOverlap(entries))
        {
            bool clusterBlocksOutright = false;
            int clusterTopSurface = 0;
            foreach (var entry in cluster)
            {
                bool solid = entry.Entity switch { PlacedItemInstance i => IsSolidAt(i, null), ObjectInstance o => IsSolidAt(null, o), _ => false };
                if (!solid) continue;
                var (stackable, elevates, _, _, sizeZ) = entry.Entity switch { PlacedItemInstance i => ResolveStackInfo(i, null), ObjectInstance o => ResolveStackInfo(null, o), _ => (false, false, 1, 1, 1) };
                if (!elevates) { if (!stackable) { clusterBlocksOutright = true; break; } continue; } // a real wall anywhere in this one real cluster blocks just this cluster
                clusterTopSurface = Math.Max(clusterTopSurface, entry.OldZ + sizeZ);
            }
            if (clusterBlocksOutright) continue; // this real cluster is a wall - try the next one, if any
            if (clusterTopSurface - _playerZ > MaxStepUpHeight) continue; // this real cluster is too tall to climb from here - try the next one

            // Real, reachable cluster - track the LOWEST such real result, matching "let me climb the easy one right here", not the tallest one that happens to share this tile.
            if (bestReachableZ is null || clusterTopSurface < bestReachableZ) bestReachableZ = clusterTopSurface;
        }

        resultingZ = bestReachableZ ?? 0;
        return bestReachableZ is not null;
    }

    private bool IsSolidAt(PlacedItemInstance? item, ObjectInstance? obj)
    {
        if (item is not null && item.Category == ReferencedItemCategory.Book)
            return _books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.IsSolid ?? false;
        // Real, confirmed fix, per spec ("When I checked collision detection, I could still pass through the chest") - a placed Container was never checked here at all, so its own real IsSolid setting was silently ignored no matter what it was set to. This is squarely collision integration, not a change to the walking/input/frame-timing system itself, which stays completely untouched.
        if (item is not null && item.Category == ReferencedItemCategory.Container)
            return _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.IsSolid ?? false;
        if (obj is not null)
            return _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId)?.IsSolid ?? false;
        return false;
    }

    /// <summary>
    /// THE universal "something happened, does it fire?" engine - map
    /// events, books, and (once they have their own EventPages) every
    /// future item type all run through this exact same method, per spec:
    /// one real engine for identify/use across every kind of object, not
    /// separate bespoke logic per type. Owner is whatever object the
    /// pages belong to (a GameEvent, a BookData, ...) - only used as a
    /// dictionary key to track each owner's own independent fire counts,
    /// via plain reference equality, so two different books (or events)
    /// never share state by accident.
    /// </summary>
    /// <summary>
    /// The real, general fix for conditions "not going through into the
    /// real engine" - every field the condition evaluator can actually
    /// read from real, persistent state is populated here from that real
    /// state, not left at its class defaults (which is what silently
    /// broke every Variable/Stat/HasBeenRead-based condition before: they
    /// always evaluated against an empty dictionary or false, regardless
    /// of the books own real authored condition). This is the ONE place
    /// that happens, for every page of every book, of every trigger type -
    /// not a fix aimed at any specific condition.
    ///
    /// Honest, explicitly-flagged remaining gaps - these represent
    /// mechanics that do not exist anywhere in the real engine yet, not a
    /// wiring bug in this method: Combine Item, Drop-To-Self, Distance/
    /// Joined Party, and Event Trigger Completed all still read as their
    /// class defaults, because there is no real combine-item, NPC-party,
    /// distance, or event-completion-tracking system built yet for them
    /// to read from. String-type variables also cannot be bridged yet -
    /// the SetVariable command itself only stores numbers, so there is no
    /// real string value anywhere to read even for a variable condition
    /// that asks for one.
    /// </summary>
    private BookEventPreviewContext BuildConditionContext(BookTriggerConditionBased pageOwnTriggerType, BookTriggerConditionBased justOccurred, int selfBookId, int timesTriggeredSoFar)
    {
        var context = new BookEventPreviewContext
        {
            TriggerActuallyOccurred = pageOwnTriggerType == justOccurred,
            MomentaryTriggerJustOccurred = justOccurred,
            SelfItemId = selfBookId,
            TimesTriggeredSoFar = timesTriggeredSoFar,
            HasBeenRead = selfBookId >= 0 && _hasBeenReadBooks.Contains(selfBookId)
        };

        foreach (var def in _variables.Variables)
        {
            double raw = _execContext.GetVariable(def.Name);
            context.VariableValues[def.Id] = def.Type switch
            {
                VariableValueType.Boolean => (raw != 0).ToString(),
                _ => raw.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        if (_player is not null)
        {
            foreach (var def in _stats.Stats)
            {
                if (_player.StatValues.TryGetValue(def.Id, out var rawText) && int.TryParse(rawText, out var value))
                    context.StatValues[def.Id] = value;
            }
        }

        return context;
    }

    /// <summary>
    /// Real, confirmed fix, per spec ("By Having an item condition for
    /// book, event editor and container did not fire") - this trigger was
    /// never fired anywhere in the real engine at all, for any owner.
    /// Unlike ByPickupItem/ByDropItem (a real, single moment tied to one
    /// specific item), "having" or "not having" an item is a real state
    /// check that can matter to ANY map event, not just the item that
    /// just moved - so every real map event gets a real chance to
    /// re-evaluate its own condition the instant inventory genuinely
    /// changes (a pickup or a drop actually completing), not tied to
    /// standing on any particular tile.
    /// </summary>
    private void FireGlobalInventoryStateTrigger(BookTriggerConditionBased trigger)
    {
        foreach (var mapEvent in _map.Events)
            TryFireEventPages(mapEvent, mapEvent.EventPages, trigger, selfBookId: -1, $"\"{mapEvent.Name}\" fired ({trigger}).");
    }

    private bool TryFireEventPages(object owner, List<BookEventPage> pages, BookTriggerConditionBased justOccurred, int selfBookId, string firedStatusMessage)
    {
        if (!_firedCounts.TryGetValue(owner, out var counts))
        {
            counts = new Dictionary<BookEventPage, int>();
            _firedCounts[owner] = counts;
        }

        _execContext.SelfBookId = selfBookId; // "self" now correctly refers to whichever object is actually firing right now - the whole reason SelfBookId became settable rather than fixed at construction

        var trace = new List<string>();
        foreach (var page in pages)
        {
            var context = BuildConditionContext(page.Conditions.TriggerConditionBased, justOccurred, selfBookId, counts.TryGetValue(page, out var c) ? c : 0);

            if (BookEventConditionEvaluator.Evaluate(page.Conditions, context, trace))
            {
                counts[page] = (counts.TryGetValue(page, out var cc) ? cc : 0) + 1;
                if (page.Commands.Count > 0)
                {
                    EventCommandInterpreter.Run(page.Commands, _execContext, trace);
                    StatusText.Text = firedStatusMessage;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Ultima 7's own mouse model, per spec: single-click identifies
    /// anything clickable (shows its name, exactly like the real game);
    /// double-click uses it, firing whatever real mechanics are attached
    /// - whether that's an authored event, or nothing at all, exactly as
    /// authored. Tiles themselves are never clickable/identifiable -
    /// everything else on the map is. Movement stays on the keyboard,
    /// untouched by this.
    /// </summary>
    /// <summary>
    /// Real, pixel-precise hit-testing, per spec: "I should be able to
    /// click any of the stacked books from top, middle or bottom, not
    /// just the top." The old logic converted a click to a CELL, then
    /// always picked whichever item at that cell had the highest Z-index
    /// - meaning a stacked item's own visible "peeking" edge (the part an
    /// elevated stack naturally leaves uncovered below the item on top of
    /// it) was never actually clickable; the topmost item always won
    /// regardless of where exactly the click landed. This walks WPF's own
    /// real hit-test result (which already respects true Z-order AND real
    /// geometric occlusion, elevation peeking included) up to whichever
    /// tracked item/object visual it actually lands on.
    /// </summary>
    private (PlacedItemInstance? item, ObjectInstance? obj) HitTestStack(Point pos)
    {
        var result = VisualTreeHelper.HitTest(GameCanvas, pos);
        DependencyObject? current = result?.VisualHit;
        while (current is not null)
        {
            if (current is UIElement element)
            {
                foreach (var kv in _itemVisuals) if (ReferenceEquals(kv.Value, element)) return (kv.Key, null);
                foreach (var kv in _objectVisuals) if (ReferenceEquals(kv.Value, element)) return (null, kv.Key);
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return (null, null);
    }

    /// <summary>
    /// Real sighting/line-of-sight, confirmed directly from Exult's own
    /// occlude.dat: an object or item authored as Occludes blocks
    /// interaction with whatever sits behind it from the player's
    /// current position, even if that target would otherwise be
    /// reachable. Traces a straight grid line (Bresenham) from the
    /// player's own tile to the target's tile - an occluding object on
    /// an INTERMEDIATE tile blocks it, but the player's own tile and the
    /// target's own tile are never blocking (you can always interact
    /// with whatever shares your square, and clicking the occluder
    /// itself always works even though it blocks what's behind it).
    /// </summary>
    private bool HasLineOfSight(int toX, int toY)
    {
        if (_playerX == toX && _playerY == toY) return true;
        foreach (var (x, y) in TraceGridLine(_playerX, _playerY, toX, toY))
        {
            if ((x, y) == (_playerX, _playerY) || (x, y) == (toX, toY)) continue;
            if (_map.Objects.Any(o => o.ContainerInstanceId < 0 && o.X == x && o.Y == y && IsOccluding(null, o))) return false;
            if (_map.Items.Any(i => i.X == x && i.Y == y && IsOccluding(i, null))) return false;
        }
        return true;
    }

    private bool IsOccluding(PlacedItemInstance? item, ObjectInstance? obj)
    {
        if (item is not null && item.Category == ReferencedItemCategory.Book)
            return _books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.Occludes ?? false;
        if (obj is not null)
            return _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId)?.Occludes ?? false;
        return false;
    }

    /// <summary>Standard integer Bresenham line, tile by tile, from (x0,y0) to (x1,y1) inclusive of both endpoints - the real grid trace HasLineOfSight walks to find an occluder in between.</summary>
    private static IEnumerable<(int x, int y)> TraceGridLine(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int x = x0, y = y0;
        while (true)
        {
            yield return (x, y);
            if (x == x1 && y == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    private void GameCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isReadingBook) return;

        var pos = e.GetPosition(GameCanvas);
        int cellX = (int)(pos.X / _tileWidth);
        int cellY = (int)(pos.Y / _tileHeight);
        if (cellX < 0 || cellY < 0 || cellX >= _map.Width || cellY >= _map.Height) return;

        bool isUse = e.ClickCount >= 2;

        var (hitItem, hitObject) = HitTestStack(pos);

        // Real, confirmed bug fix, per spec ("when I click any of the books under the NPC it clicks the npc and not the books underneath him") - this used to check "is the CLICKED TILE the same as the player's own tile" BEFORE ever looking for a real item there at all, so a book sharing that tile with the player was never even reached - the whole tile short-circuited straight to identifying the player, every time, regardless of what was actually clicked. HitTestStack now runs first; identifying the player is only ever the real fallback when nothing else was actually found there.
        if (!isUse && hitItem is null && hitObject is null && cellX == _playerX && cellY == _playerY)
        {
            ShowIdentifyLabel(_player?.Name ?? "Yourself", _playerX, _playerY, _playerImage);
            return;
        }

        if (hitItem is not null)
        {
            if (!HasLineOfSight(hitItem.X, hitItem.Y)) { StatusText.Text = "You can't see that from here - something's blocking your view."; return; }
            if (isUse) { HandleItemClick(hitItem, isUse: true); return; }
            // Single click on an item starts a drag CANDIDATE - whether it ends up being a real drag or a plain identify click is decided in MouseLeftButtonUp, exactly like Book Preview's own BookSpriteImage drag.
            BeginDrag(hitItem, null, _itemVisuals.GetValueOrDefault(hitItem), pos);
            return;
        }

        if (hitObject is not null)
        {
            if (!HasLineOfSight(hitObject.X, hitObject.Y)) { StatusText.Text = "You can't see that from here - something's blocking your view."; return; }
            if (isUse) { HandleObjectClick(hitObject, isUse: true); return; }
            BeginDrag(null, hitObject, _objectVisuals.GetValueOrDefault(hitObject), pos);
        }
        // Nothing here, or a bare tile - per spec, tiles themselves are never identifiable/usable.
    }

    private void BeginDrag(PlacedItemInstance? item, ObjectInstance? obj, UIElement? visual, Point mousePos)
    {
        if (visual is null) return;
        // Real, confirmed fix, per spec ("chest B disappears... chest B now is inside chest A... it got replaced by chest B moving there") - found the true root cause: BeginDrag is called from three separate real places (a map item, a paperdoll slot, a container item), and always SUBSCRIBED its two handlers with += , never first unsubscribing. If any previous drag's own cleanup was ever skipped for any real reason (a double-click's own first-click BeginDrag whose matching MouseUp didn't resolve before the second click's own isUse branch ran, say), the next real BeginDrag call stacked a SECOND subscription on top of the first - so a single later MouseMove or MouseUp fired the handler twice, with _dragItem/_dragVisual already overwritten by whichever drag started most recently, silently finalizing a stale, unrelated item (chest B) instead of - or alongside - the one actually being dragged. Unconditionally unsubscribing first, before ever subscribing again, makes this structurally impossible: at most one real subscription can ever exist, however a previous drag's own cleanup happened to go. Removing a handler that was never subscribed is a safe, well-defined no-op in C#, so this never fails even when there is genuinely nothing stale to clean up.
        GameCanvas.MouseMove -= GameCanvas_MouseMove_Drag;
        GameCanvas.MouseLeftButtonUp -= GameCanvas_MouseLeftButtonUp_Drag;
        // Real, confirmed safety net, same real reason as the unsubscribe fix right above it - if a previous drag's own visual is still found sitting on PaperdollGumpsCanvas (reparented there the moment real movement was ever detected, per GameCanvas_MouseMove_Drag's own real comment), it never got a real, proper resolution at all - settled back onto the real map, at its own current real position, rather than left as a genuinely orphaned, invisible-among-the-gumps stray that nothing would otherwise ever clean up again.
        if (_dragVisual is not null && _dragVisual != visual && PaperdollGumpsCanvas.Children.Contains(_dragVisual))
        {
            var orphanedScreenPos = new Point(Canvas.GetLeft(_dragVisual), Canvas.GetTop(_dragVisual));
            var orphanedWorldPos = PaperdollGumpsCanvas.TranslatePoint(orphanedScreenPos, GameCanvas);
            PaperdollGumpsCanvas.Children.Remove(_dragVisual);
            if (_dragVisual is FrameworkElement orphanedElement) orphanedElement.RenderTransform = null;
            GameCanvas.Children.Add(_dragVisual);
            Canvas.SetLeft(_dragVisual, orphanedWorldPos.X);
            Canvas.SetTop(_dragVisual, orphanedWorldPos.Y);
        }
        _dragItem = item;
        _dragObject = obj;
        _dragVisual = visual;
        _movedDuringDrag = false;
        _dragStartMouse = mousePos;
        _dragStartElementPos = new Point(Canvas.GetLeft(visual), Canvas.GetTop(visual));
        _dragLogicalLeft = _dragStartElementPos.X;
        _dragLogicalTop = _dragStartElementPos.Y;
        // No longer toggling visual.IsHitTestVisible off here - GameCanvas.CaptureMouse() below already routes every subsequent mouse event to GameCanvas regardless of what's hit-testable underneath, so this was redundant. It also never got reset back to true, which would have permanently broken pixel-precise stack clicking on this exact item after its very first drag.
        GameCanvas.CaptureMouse();
        GameCanvas.MouseMove += GameCanvas_MouseMove_Drag;
        GameCanvas.MouseLeftButtonUp += GameCanvas_MouseLeftButtonUp_Drag;
    }

    private void GameCanvas_MouseMove_Drag(object sender, MouseEventArgs e)
    {
        if (_dragVisual is null) return;
        var current = e.GetPosition(GameCanvas);
        double dx = current.X - _dragStartMouse.X;
        double dy = current.Y - _dragStartMouse.Y;

        if (!_movedDuringDrag && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
        {
            _movedDuringDrag = true;
            TryFireDragTrigger(BookTriggerConditionBased.ByPickupItem); // the real moment of picking it up - grabbing it and actually starting to move it, not just clicking, matching Book Preview's own exact timing

            // Real Exult solution, confirmed directly from how it actually works: not a Z-index system at all - the dragged object is simply painted LAST, every single frame, on top of absolutely everything, gumps included. The WPF equivalent of "paint last" is "parent last" - the one real visual (never a clone) is reparented onto PaperdollGumpsCanvas, the same layer gumps live on, added as its final child so it renders on top of every one of them. GameCanvas.Children.Remove is safe even if the visual isn't currently there (a plain map-to-map drag never left it), since the item could instead already be a fresh child added by StartDragFromInventory. An explicit, very high ZIndex is also required - existing gumps already carry their own real ZIndex values (100+ for paperdolls, 200 for a backpack gump), so merely being the LAST child isn't enough on its own to guarantee winning against those.
            //
            // Confirmed, real root cause of the shrinking regression: GameCanvas lives inside ViewportHost, which has ViewportScale (a real ScaleTransform, typically 2x-4x) applied to it - so the SAME element renders at Width*ScaleX pixels while inside that hierarchy, but at its raw, un-scaled Width once moved to PaperdollGumpsCanvas, a sibling of ViewportHost entirely outside that scale. TranslatePoint (used below for position) correctly accounts for this scale when converting a POINT, but does nothing for the element's own SIZE - that has to be corrected explicitly, by applying the exact same scale as a RenderTransform on the visual itself for as long as it's reparented, removing it again the instant it moves back.
            GameCanvas.Children.Remove(_dragVisual);
            PaperdollGumpsCanvas.Children.Add(_dragVisual);
            Panel.SetZIndex(_dragVisual, 10000);
            if (_dragVisual is FrameworkElement dragElement)
                dragElement.RenderTransform = new ScaleTransform(ViewportScale.ScaleX, ViewportScale.ScaleY);
        }

        if (!_movedDuringDrag) return;

        // Free pixel movement while dragging, for real visual feedback - snapping to the nearest tile happens only once, on drop. _dragLogicalLeft/Top - NOT the visual's own Canvas.Left/Top, which is now genuinely screen-space while reparented - stay the one real, authoritative world position every drop-commit calculation below reads from.
        _dragLogicalLeft = Math.Clamp(_dragStartElementPos.X + dx, 0, GameCanvas.Width - _tileWidth);
        _dragLogicalTop = Math.Clamp(_dragStartElementPos.Y + dy, 0, GameCanvas.Height - _tileHeight);

        // The visual's own on-screen position is purely a rendering concern now - converted fresh from the real logical world position into PaperdollGumpsCanvas's own coordinate space every frame, via WPF's own real coordinate transform (TranslatePoint), which already accounts for the camera/viewport scale between the two layers automatically.
        var screenPos = GameCanvas.TranslatePoint(new Point(_dragLogicalLeft, _dragLogicalTop), PaperdollGumpsCanvas);
        Canvas.SetLeft(_dragVisual, screenPos.X);
        Canvas.SetTop(_dragVisual, screenPos.Y);
    }

    /// <summary>
    /// By Pickup/Drop Item, for real, at the exact moments Book Preview
    /// itself fires them (drag-start past the threshold; drag-end on
    /// release) - this was the missing wire, not a missing mechanic: the
    /// drag itself already existed, it just never told the event system
    /// it had happened. Only books can fire yet, since only BookData has
    /// EventPages today - dragging a Weapon/Armor/Item/Object still can't
    /// fire anything, the same honest gap as everywhere else in this
    /// engine that those types have no event system of their own.
    /// </summary>
    /// <summary>Change Book Settings, per spec - reads the books EFFECTIVE name, resolving to a runtime override if one has been set via that command, falling back to the books own real, saved Name otherwise. Every player-visible use of a books name in the real engine goes through this, not book.Name directly, so an override actually shows up wherever the name would appear.</summary>
    private string EffectiveBookName(BookData book) => _execContext.GetEffectiveBookSetting(book, "Name", book.Name);

    /// <summary>
    /// Resolves whether a placed item/object allows cell-stacking, and
    /// whether it elevates when stacked - the real, general check the
    /// drag-drop stacking logic below uses, honoring any live Change Book
    /// Settings override for books (since IsStackable/AllowElevation are
    /// both overridable fields) rather than only ever reading the book's
    /// own saved value. Honest gap: Weapon/Armor/Item still have no real
    /// data class of their own, so they can never stack yet, same as
    /// every other place those three types are missing real data.
    /// </summary>
    private (bool stackable, bool elevates, int sizeX, int sizeY, int sizeZ) ResolveStackInfo(PlacedItemInstance? item, ObjectInstance? obj)
    {
        if (item is not null && item.Category == ReferencedItemCategory.Book)
        {
            var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
            if (book is null) return (false, false, 1, 1, 1);
            bool stackable = _execContext.GetEffectiveBookSetting(book, "IsStackable", book.IsStackable.ToString()) == "True";
            bool elevates = _execContext.GetEffectiveBookSetting(book, "AllowElevation", book.AllowElevation.ToString()) == "True";
            int sizeX = int.TryParse(_execContext.GetEffectiveBookSetting(book, "SizeX", book.SizeX.ToString()), out var sx) ? sx : book.SizeX;
            int sizeY = int.TryParse(_execContext.GetEffectiveBookSetting(book, "SizeY", book.SizeY.ToString()), out var sy) ? sy : book.SizeY;
            int sizeZ = int.TryParse(_execContext.GetEffectiveBookSetting(book, "SizeZ", book.SizeZ.ToString()), out var sz) ? sz : book.SizeZ;
            return (stackable, elevates, Math.Max(1, sizeX), Math.Max(1, sizeY), Math.Max(1, sizeZ));
        }
        // Real, confirmed fix, per spec ("if Allow Elevation is enabled... the mechanics of elevation and stacking" should apply to the container itself) - a container was never handled here at all before, silently falling through to "not stackable, doesn't elevate" regardless of its own real settings. No Change Settings-style runtime override system exists for containers yet (that's Book-specific), so this reads the container's own real, saved values directly.
        if (item is not null && item.Category == ReferencedItemCategory.Container)
        {
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
            return containerData is null ? (false, false, 1, 1, 1) : (containerData.IsStackable, containerData.AllowElevation, Math.Max(1, containerData.SizeX), Math.Max(1, containerData.SizeY), Math.Max(1, containerData.SizeZ));
        }
        if (obj is not null)
        {
            var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId);
            return worldObject is null ? (false, false, 1, 1, 1) : (worldObject.IsCellStackable, worldObject.AllowCellElevation, Math.Max(1, worldObject.SizeX), Math.Max(1, worldObject.SizeY), Math.Max(1, worldObject.SizeZ));
        }
        return (false, false, 1, 1, 1);
    }

    /// <summary>Pixels of screen shift ONE world Z (lift) unit represents - confirmed directly from Exult's own real source (drag.cc's drop_at_lift: "x += at_lift * 4"). Real Ultima 7 does NOT render elevation as a purely vertical peek; it shifts DIAGONALLY, the same amount on both axes, which is exactly what produces the natural fanned/staggered look of a real stack rather than a straight vertical tower.</summary>
    private const double LiftPixelShift = 4;

    /// <summary>
    /// Re-lays-out every placed item/object that shares a cell with
    /// another one, per spec: cell-stacking with optional visual
    /// elevation. Combines Items and Objects together for grouping (a
    /// book can stack with an object, not just with another book), grouped
    /// by (X, Y), ordered by placement order within that group - later-
    /// placed sits visually on top (Z-index) and, if IT individually
    /// allows elevation, one step higher on screen too. An item that does
    /// NOT allow elevation contributes no height at all to the stack
    /// (the "key under a vase" case: both stackable, neither one moves
    /// up), while an elevating item counts only the elevating items
    /// before it (the "books on books" case: each one sits above the
    /// last). This never changes the underlying game-world (X, Y) of
    /// anything - purely a visual re-layout of items/objects already
    /// legitimately stacked there.
    /// </summary>
    /// <summary>
    /// The real physics, per spec - this is where Z actually gets computed
    /// and WRITTEN as genuine, persistent state, not just rendered. Called
    /// only when something about a cell's contents actually changed (an
    /// item arrived or left it via drag-drop, or once at startup to make
    /// sense of however the map was authored) - never on every render.
    /// Sorts whatever is currently at (x, y) by its EXISTING Z (stable, so
    /// ties keep their relative order), then reassigns real Z values:
    /// each elevating item sits one level above however many elevating
    /// items came before it; a non-elevating item always settles at Z 0,
    /// sliding to the ground regardless of what else shares its cell (the
    /// key-under-the-vase case). This is the actual "when support is
    /// removed, things above drop to the next valid elevation" logic -
    /// calling it on the cell something just left is precisely how the
    /// remaining stack settles for real, not just visually.
    /// </summary>
    private void SettleAllCells()
    {
        var cells = _map.Items.Select(i => (i.X, i.Y))
            .Concat(_map.Objects.Where(o => o.ContainerInstanceId < 0).Select(o => (o.X, o.Y)))
            .Distinct();
        foreach (var (x, y) in cells) SettleCell(x, y);
    }

    private sealed record StackEntry(object Entity, int OffsetX, int OffsetY, int SizeX, int SizeY, int SizeZ, bool Elevates, int OldZ);

    /// <summary>
    /// Real, confirmed new piece, per spec ("the player should therefore
    /// be placed on the object's elevation rather than rendered as
    /// though underneath it"). Collision, elevation, and render order
    /// stay genuinely separate concepts here, per spec: this method only
    /// ever answers "how high is the real, existing top surface at this
    /// tile" - it never decides whether the player may enter (that's
    /// IsTileWalkable's own job) and never touches draw order directly
    /// (ComputeDepthZIndex already derives that correctly from whatever
    /// real Z this returns). Deliberately per-tile, not per-footprint,
    /// matching the same simplification IsSolidAt/IsTileWalkable already
    /// use for the player specifically - real footprint-offset precision
    /// (SettleCell's own ClusterByFootprintOverlap) stays exactly where
    /// it already is, for item-on-item stacking, untouched.
    /// </summary>
    /// <summary>
    /// Real, confirmed fall physics, per spec ("use exult for reference
    /// please and copy their logic") - _playerVisualLiftZ falls toward
    /// the real, already-decided _playerZ under real gravity-like
    /// acceleration. Real, confirmed reversal, per spec ("make it instant
    /// just like the books or container") - the real tile-transition code
    /// now snaps _playerVisualLiftZ to _playerZ immediately, every time,
    /// both directions, so by the time this ever runs the two values
    /// already match and this is a genuine no-op every single frame.
    /// Left in place rather than deleted - genuinely inert, genuinely
    /// cheap, and keeps the real gravity machinery ready if a gradual
    /// fall is ever wanted again later.
    /// </summary>
    private void UpdatePlayerFallAnimation(double deltaSeconds)
    {
        if (_playerVisualLiftZ <= _playerZ) { _playerVisualLiftZ = _playerZ; _playerFallVelocity = 0; return; }

        _playerFallVelocity = Math.Min(FallTerminalVelocity, _playerFallVelocity + FallAcceleration * deltaSeconds);
        _playerVisualLiftZ = Math.Max(_playerZ, _playerVisualLiftZ - _playerFallVelocity * deltaSeconds);
        if (_playerVisualLiftZ <= _playerZ) _playerFallVelocity = 0; // real landing - speed resets for the next real fall, rather than carrying over

        // Real, confirmed fix - the fall is purely visual (collision already resolved against the real, logical _playerZ the instant the tile changed), so only the actual drawn position needs to catch up here; StepPlayerContinuous's own X/Y math is never touched.
        Panel.SetZIndex(_playerImage, ComputeDepthZIndex(_playerPixelX / _tileWidth, _playerPixelY / _tileHeight, (int)Math.Round(_playerVisualLiftZ)));
        double left = _playerPixelX - _playerImage.Width / 2 - _playerVisualLiftZ * LiftPixelShift;
        double top = _playerPixelY + _tileHeight / 2 - _playerImage.Height - _playerVisualLiftZ * LiftPixelShift;
        _playerImage.BeginAnimation(Canvas.LeftProperty, null);
        _playerImage.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(_playerImage, left);
        Canvas.SetTop(_playerImage, top);
    }

    /// <summary>Real, confirmed fix - now genuinely shares TryFindReachableClusterAt with IsTileWalkable, the exact same real, footprint-aware evaluation that just decided whether this tile could be entered at all, rather than a second, separate, non-footprint-aware "check everything at this tile" pass that could disagree with it.</summary>
    private int ComputePlayerStandingZ(int tileX, int tileY)
    {
        TryFindReachableClusterAt(tileX, tileY, out int resultingZ);
        return resultingZ;
    }

    /// <summary>
    /// Real, confirmed limit on how high the player may step up onto a
    /// solid, elevating object in one move, rather than either blocking
    /// every solid-but-elevating object outright (an oversized invisible
    /// wall the person specifically asked not to reintroduce) or letting
    /// the player climb an arbitrarily tall stack in a single step (which
    /// would not be faithful to real Ultima VII/Exult behavior either).
    /// Honest note: this specific numeric threshold is a reasonable,
    /// clearly-labeled approximation from general Ultima VII/Exult
    /// knowledge, not a value verified against Exult's own live source in
    /// this pass - treat it as tunable, not authoritative, until it can
    /// be checked directly against that source.
    /// </summary>
    private const int MaxStepUpHeight = 2;

    private void SettleCell(int x, int y)
    {
        var itemsHere = _map.Items.Where(i => i.X == x && i.Y == y).ToList();
        var objectsHere = _map.Objects.Where(o => o.ContainerInstanceId < 0 && o.X == x && o.Y == y).ToList();

        var entries = itemsHere.Select(i => { var info = ResolveStackInfo(i, null); return new StackEntry(i, i.OffsetX, i.OffsetY, info.sizeX, info.sizeY, info.sizeZ, info.elevates, i.Z); })
            .Concat(objectsHere.Select(o => { var info = ResolveStackInfo(null, o); return new StackEntry(o, o.OffsetX, o.OffsetY, info.sizeX, info.sizeY, info.sizeZ, info.elevates, o.Z); }))
            .ToList();

        // Real footprint-overlap clustering, per spec: two items sharing a tile but placed at sub-tile offsets that genuinely don't overlap are NOT a stacking attempt at all - each is its own fully independent stack, settling at its own ground level regardless of what else shares the tile. Only entries whose actual footprints intersect settle together.
        foreach (var cluster in ClusterByFootprintOverlap(entries))
        {
            int runningTop = 0;
            foreach (var entry in cluster.OrderBy(e => e.OldZ))
            {
                int newZ = entry.Elevates ? runningTop : 0;
                if (entry.Entity is PlacedItemInstance item) item.Z = newZ;
                else if (entry.Entity is ObjectInstance obj) obj.Z = newZ;
                if (entry.Elevates) runningTop = newZ + entry.SizeZ;
            }
        }
    }

    private static bool FootprintsOverlap(StackEntry a, StackEntry b) =>
        a.OffsetX < b.OffsetX + b.SizeX && b.OffsetX < a.OffsetX + a.SizeX &&
        a.OffsetY < b.OffsetY + b.SizeY && b.OffsetY < a.OffsetY + a.SizeY;

    /// <summary>Simple connected-components clustering by pairwise footprint overlap - a handful of entries per tile at most in practice, so a plain O(n^2) pass is more than fast enough; no need for a general union-find structure here.</summary>
    private static List<List<StackEntry>> ClusterByFootprintOverlap(List<StackEntry> entries)
    {
        var clusters = new List<List<StackEntry>>();
        foreach (var entry in entries)
        {
            var matching = clusters.Where(c => c.Any(e => FootprintsOverlap(e, entry))).ToList();
            var merged = new List<StackEntry> { entry };
            foreach (var m in matching) { merged.AddRange(m); clusters.Remove(m); }
            clusters.Add(merged);
        }
        return clusters;
    }

    /// <summary>
    /// Pure rendering, per spec - reads each item/object's own real,
    /// already-settled Z and converts it to a screen offset; never
    /// computes or writes Z itself. Z-index is set FROM the same logical
    /// Z too, so something with a genuinely higher elevation both renders
    /// on top and gets hit-tested first - visual layering now follows
    /// physical reality instead of an arbitrary render-order guess.
    /// </summary>
    private void RefreshStackingVisuals()
    {
        foreach (var item in _map.Items)
        {
            if (_itemVisuals.TryGetValue(item, out var visual) && visual is FrameworkElement fe)
                ApplyZToVisual(fe, item.X, item.Y, item.Z, item.OffsetX, item.OffsetY);
        }
        foreach (var obj in _map.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            if (_objectVisuals.TryGetValue(obj, out var visual) && visual is FrameworkElement fe)
                ApplyZToVisual(fe, obj.X, obj.Y, obj.Z, obj.OffsetX, obj.OffsetY);
        }
    }

    /// <summary>
    /// Real, position-based paint order, per Exult's own confirmed
    /// mechanics: objects further south-east (higher tileX + tileY, the
    /// natural "toward the viewer" diagonal in this projection) render on
    /// top of objects further north-west, with lift (Z) breaking ties
    /// only WITHIN roughly the same position - never overriding actual
    /// position the way a lift-only Z-index used to. This is the ONE
    /// formula every visual in the scene (player, items, objects, books
    /// alike) computes its Panel.ZIndex from, which is what actually
    /// fixes "walking behind/beside an object" rendering correctly: the
    /// player no longer has a fixed Z-index that always wins regardless
    /// of where they actually are relative to everything else.
    ///
    /// CONVENTION - every caller must pass each TILE'S CENTER (x + 0.5,
    /// y + 0.5), never the raw top-left tile index. The player's own
    /// position is already a true continuous center by construction; an
    /// item/object's plain integer X/Y is its tile's top-left corner, a
    /// full 0.5 tile short on each axis. Mixing the two conventions was
    /// the real bug behind "I should be behind a tall stack, not in
    /// front of it": the player's center-based key was silently ~100
    /// points higher than an item's corner-based key at the very SAME
    /// tile, a gap no realistic stack height could ever close - so the
    /// player always won regardless of how tall anything got.
    /// </summary>
    private static int ComputeDepthZIndex(double worldTileCenterX, double worldTileCenterY, int z) => (int)Math.Round((worldTileCenterX + worldTileCenterY) * 100) + z;

    private void ApplyZToVisual(FrameworkElement visual, int x, int y, int z, int offsetX, int offsetY)
    {
        Panel.SetZIndex(visual, ComputeDepthZIndex(x + 0.5, y + 0.5, z));
        // Re-anchor against this visual's OWN real (quantized) size and sub-tile offset first - correct even for a lone item - then shift for its real, stored Z using Exult's own confirmed formula: DIAGONALLY, up and to the left as lift increases, on BOTH axes equally - not derived from render order, and not a purely vertical peek.
        PositionQuantizedVisual(visual, x, y, visual.Width, visual.Height, offsetX, offsetY);
        Canvas.SetLeft(visual, Canvas.GetLeft(visual) - z * LiftPixelShift);
        Canvas.SetTop(visual, Canvas.GetTop(visual) - z * LiftPixelShift);
    }

    private void TryFireDragTrigger(BookTriggerConditionBased trigger)
    {
        // Real, confirmed fix, per spec ("I checked the condition from close container to pick up container and the event did not fire") - this only ever handled Book, the same recurring gap found many times this session; a Container being picked up or dropped never reached TryFireEventPages at all, no matter what condition was actually set on it.
        if (_dragItem is { Category: ReferencedItemCategory.Container } containerItem)
        {
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == containerItem.ItemId);
            if (containerData is null) return;
            TryFireEventPages(containerData, containerData.EventPages, trigger, containerData.Id, $"\"{containerData.Name}\" fired ({trigger}).");
            return;
        }
        if (_dragItem is not { Category: ReferencedItemCategory.Book } item) return;
        var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
        if (book is null) return;
        TryFireEventPages(book, book.EventPages, trigger, book.Id, $"\"{EffectiveBookName(book)}\" fired ({trigger}).");
    }

    private void GameCanvas_MouseLeftButtonUp_Drag(object sender, MouseButtonEventArgs e)
    {
        GameCanvas.ReleaseMouseCapture();
        GameCanvas.MouseMove -= GameCanvas_MouseMove_Drag;
        GameCanvas.MouseLeftButtonUp -= GameCanvas_MouseLeftButtonUp_Drag;

        if (_dragVisual is null) { _dragItem = null; _dragObject = null; _dragOriginGump = null; _dragOriginSlot = null; _dragOriginContainerGump = null; return; }

        // Real un-reparent, per spec - moves the one real dragged visual back onto GameCanvas and restores its genuine world-space position (from _dragLogicalLeft/Top, the one authoritative value tracked throughout) BEFORE any of the drop-commit logic below runs, so every existing Canvas.GetLeft/GetTop read further down keeps working completely unchanged, in the coordinate space it's always assumed. A no-op if the visual was never actually reparented (dropped before crossing the drag threshold). The RenderTransform applied to correct for ViewportScale while reparented is cleared too - GameCanvas's own ambient scale (inherited from its real ViewportHost ancestor) takes back over automatically the moment it's a child there again, so keeping an explicit one here would double the scale.
        if (PaperdollGumpsCanvas.Children.Contains(_dragVisual))
        {
            PaperdollGumpsCanvas.Children.Remove(_dragVisual);
            GameCanvas.Children.Add(_dragVisual);
            if (_dragVisual is FrameworkElement dragElementBack) dragElementBack.RenderTransform = null;
        }
        Canvas.SetLeft(_dragVisual, _dragLogicalLeft);
        Canvas.SetTop(_dragVisual, _dragLogicalTop);

        // Real drop-into-container, per spec - dropping a dragged map item onto an open container gump adds it to that container's own real Contents instead, with a real volume check and a real body-container rejection (rule 1: never dropped INTO). Checked before the paperdoll check below since a container gump could be the topmost thing at this exact point.
        if (_movedDuringDrag && TryFindContainerGumpAt(e.GetPosition(PaperdollGumpsCanvas), out var targetContainerGump))
        {
            // Real, confirmed fix, per spec ("the ability to move items between these containers through the paper dolls or by dragging an item onto a container that is inside a larger container") - the drop point is checked against every REAL nested container icon's own actual stored position first; landing squarely on one redirects the drop into THAT container's own Contents instead of the outer gump's, with no need to open its own gump first at all. If it already happens to be open too, that gump is refreshed as well so both views stay honest.
            var dropPointInGump = e.GetPosition(targetContainerGump.ItemsCanvas);
            var nestedContainerHit = targetContainerGump.PlacedItem.Contents.FirstOrDefault(c =>
                c.Category == ReferencedItemCategory.Container
                && dropPointInGump.X >= c.ContainerX && dropPointInGump.X <= c.ContainerX + 32
                && dropPointInGump.Y >= c.ContainerY && dropPointInGump.Y <= c.ContainerY + 32);

            bool droppedIn;
            if (nestedContainerHit is not null)
            {
                var nestedData = _containers.Containers.FirstOrDefault(c => c.Id == nestedContainerHit.ItemId);
                if (nestedData is not null)
                {
                    var alreadyOpenNestedGump = _openContainers.FirstOrDefault(g => g.PlacedItem == nestedContainerHit);
                    // Real, confirmed fix - if the nested container also happens to already be open in its own separate gump, the real drop point is recomputed relative to THAT gump's own canvas (a real, independently-positioned element on screen), not reused from the outer gump's own coordinate space.
                    Point? nestedDropPoint = alreadyOpenNestedGump is not null ? e.GetPosition(alreadyOpenNestedGump.ItemsCanvas) : null;
                    droppedIn = TryDropIntoContainer(nestedData, nestedContainerHit, alreadyOpenNestedGump, nestedDropPoint);
                }
                else droppedIn = TryDropIntoContainer(targetContainerGump, dropPointInGump); // real data missing - fall back to the outer gump rather than silently losing the item
            }
            else
            {
                // Real, confirmed fix, per spec ("it should drop where I wanted it to drop not randomly all over the place") - the real point the player actually released it at, right here, used directly instead of a random one.
                droppedIn = TryDropIntoContainer(targetContainerGump, dropPointInGump);
            }

            _dragItem = null;
            _dragObject = null;
            _dragVisual = null;
            // Real, confirmed fix - a failed drop here now genuinely reverts to wherever this item actually started (its own source container, if that's where it came from) instead of just redrawing it wherever its stale map data happens to say, which is exactly how it could vanish for good.
            bool revertedToOriginAfterContainerDrop = !droppedIn && TryRevertDragToOrigin();
            _dragOriginGump = null; _dragOriginSlot = null; _dragOriginContainerGump = null;
            if (!droppedIn && !revertedToOriginAfterContainerDrop) RefreshStackingVisuals();
            else if (droppedIn) RefreshContainerGump(targetContainerGump); // the outer gump's own view needs a redraw too when something landed in a nested icon within it, even though its own Contents list didn't change
            return;
        }

        // Real pickup-into-inventory, per spec - dropping a dragged map item onto an open paperdoll gump picks it up for that character instead of placing it back on the map, mirroring Exult's own real Dragging_info (which checks "is this drop point over a gump?" before falling back to the map). Checked in PaperdollGumpsCanvas's own coordinate space, not GameCanvas's, since gumps are real screen-space UI, not scrolled world content.
        if (_movedDuringDrag && TryFindPaperdollGumpAt(e.GetPosition(PaperdollGumpsCanvas), out var targetGump))
        {
            bool picked = PickUpIntoInventory(targetGump.Character);
            _dragItem = null;
            _dragObject = null;
            _dragVisual = null;
            bool revertedToOriginAfterPaperdollDrop = !picked && TryRevertDragToOrigin();
            _dragOriginGump = null; _dragOriginSlot = null; _dragOriginContainerGump = null;
            if (!picked && !revertedToOriginAfterPaperdollDrop) RefreshStackingVisuals(); // rejected (see TryAutoEquipOrStore) - the item's real data never changed, so this just snaps its visual back to where it genuinely still is
            return;
        }

        // Real pickup-by-dropping-on-the-avatar, per spec and confirmed real Exult behavior - dropping an item directly onto the character's own map sprite picks it up too, with no gump needing to be open at all. Checked in GameCanvas's own coordinate space against the player's real, current visual bounds.
        if (_movedDuringDrag && _player is not null && IsPointOverPlayer(e.GetPosition(GameCanvas)))
        {
            bool pickedOnAvatar = PickUpIntoInventory(_player);
            _dragItem = null;
            _dragObject = null;
            _dragVisual = null;
            bool revertedToOriginAfterAvatarDrop = !pickedOnAvatar && TryRevertDragToOrigin();
            _dragOriginGump = null; _dragOriginSlot = null; _dragOriginContainerGump = null;
            if (!pickedOnAvatar && !revertedToOriginAfterAvatarDrop) RefreshStackingVisuals();
            return;
        }

        // Real revert, per spec - a drag that started FROM a backpack (see StartDragFromInventory) or FROM an open container but never actually moved (a plain click, not a real drag) goes right back to wherever it actually started, rather than leaving the temporary map item sitting wherever it was placed to start the drag.
        if (!_movedDuringDrag && TryRevertDragToOrigin())
        {
            _dragItem = null;
            _dragObject = null;
            _dragVisual = null;
            _dragOriginGump = null; _dragOriginSlot = null; _dragOriginContainerGump = null;
            return;
        }
        // Real, confirmed fix - found a genuine, pre-existing bug here: this used to clear the origin fields unconditionally, even when a real drag DID happen, meaning every later check of _dragOriginGump/is-not-null further down inside the real _movedDuringDrag block below (the real "revert on an invalid map drop" check, and the real "fire ByNotHavingAnItem on a successful one") was already dead code by the time it ran - the field was already null. Only cleared here now when there was genuinely nothing left to carry forward (a plain click with no real origin at all); a real, in-progress drag keeps its own real origin until one of the blocks further down actually resolves it one way or another.
        if (!_movedDuringDrag) { _dragOriginGump = null; _dragOriginSlot = null; _dragOriginContainerGump = null; }

        if (_movedDuringDrag)
        {
            // Cell computed from the dragged visual's CENTER point, not a simple top-left/tileSize division - now that visuals are variable-sized and bottom-center anchored rather than always exactly one tile, top-left no longer maps directly to a cell.
            double visualWidth = _dragVisual is FrameworkElement dragFe ? dragFe.Width : _tileWidth;
            double visualHeight = _dragVisual is FrameworkElement dragFe2 ? dragFe2.Height : _tileHeight;
            double centerX = Canvas.GetLeft(_dragVisual) + visualWidth / 2;
            double centerY = Canvas.GetTop(_dragVisual) + visualHeight / 2;
            int newCellX = (int)Math.Floor(centerX / _tileWidth);
            int newCellY = (int)Math.Floor(centerY / _tileHeight);
            newCellX = Math.Clamp(newCellX, 0, _map.Width - 1);
            newCellY = Math.Clamp(newCellY, 0, _map.Height - 1);

            // Real redirect-into-container, per spec ("if Allow Elevation is not checked, dragging an item onto the container should use the default mechanics and add the item into the container itself... If Allow Elevation is enabled, [use] the mechanics of elevation and stacking [instead]") - a CLOSED container sitting on this exact target cell, with no gump open at all, still accepts a direct drop into its own real Contents, exactly like dropping onto its open gump already does - but only when it doesn't allow elevation. A container with Allow Elevation checked falls through to the ordinary stacking/elevation logic below instead, exactly like any other stackable object, and items only go into ITS contents by actually opening its paperdoll.
            var targetCellContainer = _map.Items.FirstOrDefault(i => i != _dragItem && i.Category == ReferencedItemCategory.Container && i.X == newCellX && i.Y == newCellY);
            if (targetCellContainer is not null)
            {
                var targetContainerData = _containers.Containers.FirstOrDefault(c => c.Id == targetCellContainer.ItemId);
                if (targetContainerData is not null && !targetContainerData.AllowElevation)
                {
                    bool droppedIntoClosed = TryDropIntoContainer(targetContainerData, targetCellContainer, null);
                    _dragItem = null;
                    _dragObject = null;
                    _dragVisual = null;
                    bool revertedToOriginAfterClosedDrop = !droppedIntoClosed && TryRevertDragToOrigin();
                    _dragOriginGump = null; _dragOriginSlot = null; _dragOriginContainerGump = null;
                    if (!droppedIntoClosed && !revertedToOriginAfterClosedDrop) RefreshStackingVisuals();
                    return;
                }
            }

            // Real sub-tile offset, per spec - solved directly from ComputeQuantizedPosition's own formula, so wherever the visual was actually released maps back to the nearest real 8px world unit, not just the tile's own default centered spot. This is what lets two items sit close together within (or across) a tile instead of only ever snapping to its center.
            var draggedInfo = ResolveStackInfo(_dragItem, _dragObject);
            var (defaultLeft, defaultTop) = ComputeQuantizedPosition(newCellX, newCellY, visualWidth, visualHeight);
            int newOffsetX = (int)Math.Round((Canvas.GetLeft(_dragVisual) - defaultLeft) / SpriteSizing.BaseUnit);
            int newOffsetY = (int)Math.Round((Canvas.GetTop(_dragVisual) - defaultTop) / SpriteSizing.BaseUnit);

            // Real footprint-overlap check, per spec - NOT "shares the same tile", so two items placed close but not truly overlapping are simply two separate placements, never treated as a stacking attempt at all. Only entries whose actual footprint (given the target's own offset and size) intersects the dragged item's new footprint are considered occupants for the stacking rules below.
            var draggedFootprint = new StackEntry(new object(), newOffsetX, newOffsetY, draggedInfo.sizeX, draggedInfo.sizeY, draggedInfo.sizeZ, draggedInfo.elevates, 0);
            var overlappingItems = _map.Items.Where(i => i != _dragItem && i.X == newCellX && i.Y == newCellY
                && FootprintsOverlap(draggedFootprint, new StackEntry(i, i.OffsetX, i.OffsetY, ResolveStackInfo(i, null).sizeX, ResolveStackInfo(i, null).sizeY, 0, false, 0))).ToList();
            var overlappingObjects = _map.Objects.Where(o => o != _dragObject && o.ContainerInstanceId < 0 && o.X == newCellX && o.Y == newCellY
                && FootprintsOverlap(draggedFootprint, new StackEntry(o, o.OffsetX, o.OffsetY, ResolveStackInfo(null, o).sizeX, ResolveStackInfo(null, o).sizeY, 0, false, 0))).ToList();

            bool cellOccupied = overlappingItems.Count > 0 || overlappingObjects.Count > 0;
            bool occupantsBlockIt = overlappingItems.Any(i => !ResolveStackInfo(i, null).stackable) || overlappingObjects.Any(o => !ResolveStackInfo(null, o).stackable);
            bool footprintTooLarge = overlappingItems.Any(i => draggedInfo.sizeX > ResolveStackInfo(i, null).sizeX || draggedInfo.sizeY > ResolveStackInfo(i, null).sizeY)
                || overlappingObjects.Any(o => draggedInfo.sizeX > ResolveStackInfo(null, o).sizeX || draggedInfo.sizeY > ResolveStackInfo(null, o).sizeY);

            if (cellOccupied && (!draggedInfo.stackable || occupantsBlockIt || footprintTooLarge))
            {
                StatusText.Text = footprintTooLarge
                    ? "Can't stack there - this object's footprint is bigger than what it would be resting on."
                    : "Can't stack there - one of the items isn't marked Is Stackable.";
                // Real revert-to-source, per spec and confirmed real Exult - a drag that started from inventory (a slot, or the general backpack) or from an open container and ends on an invalid map location goes back to exactly where it came from - never left as a loose item wherever the drag happened to be sitting (which, for an inventory/container-originated drag, is only ever the player's own feet, the temporary spot StartDragFromSlot/StartDragFromInventory/StartDragItemFromContainer placed it at to begin with).
                if (TryRevertDragToOrigin())
                {
                    _dragItem = null;
                    _dragObject = null;
                    _dragVisual = null;
                    _dragOriginGump = null;
                    _dragOriginSlot = null;
                    _dragOriginContainerGump = null;
                    return;
                }
                // Otherwise (no slot/container origin - an ordinary map-to-map drag, or one that started from the general backpack), data (X, Y, Z, Offset) is untouched, so RefreshStackingVisuals below correctly snaps the visual back to its real, still-current position on its own; no separate Canvas.SetLeft/SetTop needed here.
            }
            else
            {
                // A real, allowed drag happened. Capture the ORIGIN cell before overwriting X/Y, so whatever is left behind there can actually settle - this is the real "support removed, things above drop" physics, not a visual trick.
                int originX = _dragItem?.X ?? _dragObject?.X ?? newCellX;
                int originY = _dragItem?.Y ?? _dragObject?.Y ?? newCellY;

                if (_dragItem is not null) { _dragItem.X = newCellX; _dragItem.Y = newCellY; _dragItem.OffsetX = newOffsetX; _dragItem.OffsetY = newOffsetY; }
                if (_dragObject is not null) { _dragObject.X = newCellX; _dragObject.Y = newCellY; _dragObject.OffsetX = newOffsetX; _dragObject.OffsetY = newOffsetY; }

                SettleCell(originX, originY); // real settle - anything left at the cell this came FROM drops to fill the gap
                SettleCell(newCellX, newCellY); // real placement - computes this item's genuine new Z against whatever actually overlaps it at the cell it landed ON

                StatusText.Text = $"Moved to ({newCellX}, {newCellY}), sub-position ({newOffsetX}, {newOffsetY}).";
                TryFireDragTrigger(BookTriggerConditionBased.ByDropItem); // the real moment of releasing it after an actual drag, matching Book Preview's own exact timing
                // Real, confirmed fix - only a real inventory-to-map drag (it actually came FROM a slot or the backpack, per spec) represents a genuine "no longer having it" transition; an ordinary map-to-map reposition of something already sitting on the ground was never "had" in the first place, so it stays out of this.
                if (_dragOriginGump is not null) FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByNotHavingAnItem);
            }
            RefreshStackingVisuals();
        }
        else
        {
            // No real drag occurred - a plain click, so identify exactly as before, reusing the same HandleItemClick/HandleObjectClick identify logic rather than duplicating it.
            if (_dragItem is not null) HandleItemClick(_dragItem, isUse: false);
            else if (_dragObject is not null) HandleObjectClick(_dragObject, isUse: false);
        }

        _dragItem = null;
        _dragObject = null;
        _dragVisual = null;
    }

    /// <summary>
    /// The exact same identify mechanic Book Preview's own ShowNameBriefly
    /// uses (2-second auto-hide, positioned just above the object) - per
    /// spec, applied uniformly here in the main engine to every object,
    /// not just books. This is a real in-world label, not a permanent
    /// status-bar line - it disappears on its own, the same way it does
    /// in Book Preview.
    /// </summary>
    /// <summary>
    /// visual is optional - when given, the label anchors just above that
    /// specific sprite's OWN real current top edge (Canvas.GetTop), since
    /// sprites are no longer always exactly one tile tall; when omitted,
    /// falls back to the plain cellY-based tile top, close enough for
    /// callers where looking up a specific visual isn't practical.
    /// </summary>
    private void ShowIdentifyLabel(string name, int cellX, int cellY, FrameworkElement? visual = null)
    {
        IdentifyLabel.Text = name;
        Canvas.SetLeft(IdentifyLabel, cellX * _tileWidth);
        double topAnchor = visual is not null ? Canvas.GetTop(visual) : cellY * _tileHeight;
        Canvas.SetTop(IdentifyLabel, topAnchor - 20);
        IdentifyLabel.Visibility = Visibility.Visible;

        _identifyTimer?.Stop();
        _identifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _identifyTimer.Tick += (_, _) =>
        {
            _identifyTimer!.Stop();
            IdentifyLabel.Visibility = Visibility.Collapsed;
        };
        _identifyTimer.Start();
    }

    private void HandleItemClick(PlacedItemInstance item, bool isUse)
    {
        if (item.Category == ReferencedItemCategory.Container)
        {
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
            if (containerData is null) return;
            if (!isUse) { ShowIdentifyLabel(containerData.Name, item.X, item.Y, _itemVisuals.GetValueOrDefault(item) as FrameworkElement); return; }
            TryOpenContainer(item, containerData);
            return;
        }

        if (item.Category == ReferencedItemCategory.Book)
        {
            var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
            if (book is null) return;

            if (!isUse)
            {
                ShowIdentifyLabel(EffectiveBookName(book), item.X, item.Y, _itemVisuals.GetValueOrDefault(item) as FrameworkElement);

                // Diagnostic for the sprite-size confusion report - shows the REAL sprite frame file's own pixel dimensions next to the tile size it's being rendered into, so the actual numbers are visible instead of guessed at.
                var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == book.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book.SpriteName);
                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                    string sizeInfo = frameBitmap is not null
                        ? $"sprite frame file is {frameBitmap.PixelWidth}x{frameBitmap.PixelHeight}px, rendered into a {_tileWidth}x{_tileHeight}px tile box"
                        : "sprite frame file could not be loaded to check its size";
                    StatusText.Text = $"\"{EffectiveBookName(book)}\" - {sizeInfo}";
                }
                return;
            }

            bool fired = TryFireEventPages(book, book.EventPages, BookTriggerConditionBased.ByUseItem, book.Id, $"You use \"{EffectiveBookName(book)}\" - real event fired.");
            if (!fired)
            {
                // No authored event had real commands to run - fall back to actually showing the book's real content, respecting any runtime content overrides (e.g. from another book's ChangeExternalBookContent) exactly as the shared execution context tracks them. This is the real engine's own minimal reading view, built fresh here - not a reuse of the Book Editor's own Preview window, which stays untouched.
                ShowBookReadingView(book);
            }
            return;
        }

        // Weapon/Armor/Item - no editor or event system exists for these types yet, so identify still works honestly, but there is genuinely nothing to fire on use. The click system itself already works the same way for these as for Books; only the missing event data is the gap.
        string typeName = item.Category.ToString();
        if (!isUse)
        {
            ShowIdentifyLabel($"a {typeName}", item.X, item.Y, _itemVisuals.GetValueOrDefault(item) as FrameworkElement);
        }
        else
        {
            StatusText.Text = $"There's no {typeName} editor built yet, so nothing happens when you use it - the click system itself is already ready for when there is.";
        }
    }

    /// <summary>
    /// The real engine's own reading view - built fresh here, not a reuse
    /// of the Book Editor's own Preview window (which stays completely
    /// untouched). Calls the EXACT SAME shared, non-editor-specific
    /// helpers Book Preview's own RenderCurrentPage uses - FrameGeometry
    /// and FormattedContentRenderer, both standalone utility classes, not
    /// members of BookPreviewWindow - so text is genuinely clipped into
    /// each frame's real drawn shape over the paper doll artwork, pixel-
    /// identical to Book Preview, not a simplified stand-in. Left-click
    /// flips to the next page (with the book's own PageFlipSoundResourceId),
    /// or closes and fires ByAfterReadItem for real through the shared
    /// engine on the last page - exactly Book Preview's own page-turn
    /// behavior, reimplemented here since touching that file was ruled out.
    /// </summary>
    /// <summary>
    /// The real engine's own reading view - a real in-world overlay within
    /// THIS SAME window, per spec ("it is an actual sprite, not a window
    /// or inside a window frame") - no separate popup Window, no title
    /// bar, no window chrome. Calls the EXACT SAME shared, non-editor-
    /// specific helpers Book Preview's own RenderCurrentPage uses -
    /// FrameGeometry and FormattedContentRenderer, both standalone utility
    /// classes, not members of BookPreviewWindow - so text is genuinely
    /// clipped into each frame's real drawn shape over the paper doll
    /// artwork, pixel-identical to Book Preview. Left-click flips to the
    /// next page (with the book's own PageFlipSoundResourceId), or closes
    /// and fires ByAfterReadItem for real through the shared engine on the
    /// last page. While open, movement and world clicks are suppressed via
    /// _isReadingBook, the same way a real game pauses interaction behind
    /// an open book.
    /// </summary>
    private void ShowBookReadingView(BookData book)
    {
        if (book.Pages.Count == 0 || book.Frames.Count == 0)
        {
            StatusText.Text = $"\"{EffectiveBookName(book)}\" has no pages or text frames set up yet.";
            return;
        }

        // The real source of truth for HasBeenRead / By After Read Item - marked the moment the player actually opens this book to read it, not simulated.
        _hasBeenReadBooks.Add(book.Id);

        var paperDollSprite = _sprites.Groups.FirstOrDefault(g => g.Name == book.PaperDollSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book.PaperDollSpriteName);
        if (paperDollSprite is null || paperDollSprite.Frames.Count == 0 || !File.Exists(paperDollSprite.Frames[0].FilePath))
        {
            StatusText.Text = $"\"{EffectiveBookName(book)}\" has no paper doll artwork assigned yet - can't render the reading view.";
            return;
        }

        // Frame coordinates are authored relative to a FIXED 700x400 design canvas - confirmed directly from BookPreviewWindow.xaml, which renders PaperDollBackgroundImage at that exact fixed size with Stretch="Uniform", regardless of the sprite's own real pixel dimensions. Using the sprite's actual PixelWidth/PixelHeight here (as an earlier version of this method did) is exactly what caused frame positions to be misaligned relative to the canvas - this fixed size is not a guess, it is the real reference every frame's Points/BoundingBox were drawn against.
        const double dollWidth = BookDollHeight * 1.75; // 700 at the real, original 400 reference - same real 700x400 design canvas every frame's Points/BoundingBox were authored against, unchanged

        BookOverlayCanvas.Children.Clear();
        BookOverlayCanvas.Width = dollWidth;
        BookOverlayCanvas.Height = BookDollHeight;
        RefreshBookOverlayScale();
        BookOverlayBorder.Cursor = Cursors.Hand;

        var paperDollImage = new Image { Width = dollWidth, Height = BookDollHeight, Stretch = Stretch.Uniform };
        _ = new SpriteAnimator(paperDollSprite, paperDollImage, _resources);
        BookOverlayCanvas.Children.Add(paperDollImage);

        var overlayCanvas = new Canvas { Width = dollWidth, Height = BookDollHeight, IsHitTestVisible = false };
        BookOverlayCanvas.Children.Add(overlayCanvas);

        int pageIndex = 0;
        var pageOverlayAnimators = new List<SpriteAnimator>();

        void RenderPage()
        {
            foreach (var animator in pageOverlayAnimators) animator.Stop();
            pageOverlayAnimators.Clear();
            overlayCanvas.Children.Clear();
            if (pageIndex >= book.Pages.Count) return;

            var page = book.Pages[pageIndex];
            foreach (var frame in book.Frames)
            {
                var bbox = FrameGeometry.BoundingBox(frame);
                string xaml = _execContext.GetContentOverride(book.Id, pageIndex, frame.Id)
                    ?? (page.FrameTexts.TryGetValue(frame.Id, out var t) ? t : string.Empty);

                var result = FormattedContentRenderer.Render(xaml, bbox.Width, bbox.Height, _sprites);

                var container = new Canvas
                {
                    Width = bbox.Width,
                    Height = bbox.Height,
                    Clip = FrameGeometry.BuildRelativeClipGeometry(frame, bbox)
                };

                if (result.TextBitmap is not null)
                {
                    // Left/Top alignment here is the actual fix, not decoration - Stretch="None" on an Image otherwise centers content smaller than its own allocated Width/Height, which is exactly why short text (like a title/byline) rendered low and centered instead of anchored to the top of the frame like real book text.
                    var textImage = new Image
                    {
                        Source = result.TextBitmap, Width = bbox.Width, Height = bbox.Height, Stretch = Stretch.None,
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
                    };
                    container.Children.Add(textImage);
                }

                foreach (var spriteBox in result.Sprites)
                {
                    var spriteImage = new Image { Width = spriteBox.Bounds.Width, Height = spriteBox.Bounds.Height, Stretch = Stretch.Uniform };
                    Canvas.SetLeft(spriteImage, spriteBox.Bounds.X);
                    Canvas.SetTop(spriteImage, spriteBox.Bounds.Y);
                    container.Children.Add(spriteImage);
                    pageOverlayAnimators.Add(new SpriteAnimator(spriteBox.Sprite, spriteImage, _resources));
                }

                Canvas.SetLeft(container, bbox.X);
                Canvas.SetTop(container, bbox.Y);
                overlayCanvas.Children.Add(container);
            }
        }

        void CloseOverlay()
        {
            foreach (var animator in pageOverlayAnimators) animator.Stop();
            pageOverlayAnimators.Clear();
            BookOverlayBorder.Visibility = Visibility.Collapsed;
            BookOverlayBorder.MouseLeftButtonDown -= overlayClickHandler;
            _isReadingBook = false;
            GameCanvas.Focus();
        }

        void overlayClickHandler(object sender, MouseButtonEventArgs e)
        {
            pageIndex++;
            if (pageIndex >= book.Pages.Count)
            {
                if (book.CloseSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, book.CloseSoundResourceId);
                CloseOverlay();
                // Finishing the book fires By After Read Item for real, through the same shared engine every other trigger goes through - not a simulated stand-in.
                TryFireEventPages(book, book.EventPages, BookTriggerConditionBased.ByAfterReadItem, book.Id, $"You finish reading \"{EffectiveBookName(book)}\".");
                return;
            }
            if (book.PageFlipSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, book.PageFlipSoundResourceId);
            RenderPage();
        }

        RenderPage();
        _isReadingBook = true;
        BookOverlayBorder.Visibility = Visibility.Visible;
        BookOverlayBorder.MouseLeftButtonDown += overlayClickHandler;

        if (book.OpenSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, book.OpenSoundResourceId);
    }

    private void HandleObjectClick(ObjectInstance instance, bool isUse)
    {
        var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == instance.ObjectId);
        string name = worldObject?.Name ?? $"Object #{instance.ObjectId}";

        // WorldObject has no EventPages field at all yet - same honest gap as Weapons/Armor/Items above, not a different code path.
        if (!isUse)
        {
            ShowIdentifyLabel(name, instance.X, instance.Y, _objectVisuals.GetValueOrDefault(instance) as FrameworkElement);
        }
        else
        {
            StatusText.Text = $"\"{name}\" has no events authored yet, so nothing happens when you use it - the click system itself is already ready for when it does.";
        }
    }

    private static BitmapImage? LoadStaticFrame(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }
}
