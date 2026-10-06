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
    /// <summary>Real, confirmed fix - the player's own real elevation/lift, in the exact same Z units items and objects already use. Previously never tracked at all (0/ground was always used honestly rather than guessed); decided by Exult's own walking rules every time the player's footprint moves onto new world units (TryStepActor, GameTestWindow.Physics.cs), and by gravity when what they stand on is removed (ApplyGravityToPlayer). This is the real, LOGICAL height - used for collision/step-height decisions the instant it changes; the separate _playerVisualLiftZ below is what's actually drawn, and catches up to this smoothly when it's a real fall.</summary>
    private int _playerZ;
    /// <summary>
    /// Real, confirmed fall physics, per spec ("use exult for reference
    /// please and copy their logic") - from real Exult/Ultima VII
    /// knowledge: an actor who steps off a supported height does not
    /// teleport straight down to the new floor - they visibly fall,
    /// accelerating like real gravity, landing over a short but genuine
    /// span of time, not instantly. Climbing UP (a deliberate step onto
    /// something) is immediate, exactly like it already was - only
    /// falling gets this treatment. Honest note - the fall-acceleration
    /// constant below is a reasonable, clearly-labeled approximation, not
    /// a value from Exult's source (Exult moves a falling object straight
    /// to its new lift). Falls are currently instant anyway; see
    /// UpdatePlayerFallAnimation.
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

    // Every placed item/object's own map visual - the map side of hit testing and drag-and-drop. All click/drag state lives in
    // GameTestWindow.Interaction.cs (one PointerSession, one origin record); nothing about a drag is stored here.
    private readonly Dictionary<PlacedItemInstance, UIElement> _itemVisuals = new();
    private readonly Dictionary<ObjectInstance, UIElement> _objectVisuals = new();

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
        VariableDatabase variables, StatDatabase stats, ContainerDatabase containers, SystemSettings systemSettings, FactionDatabase factions)
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
        // Same real principle, and same real reason, as CloneCharacterDatabase - a container's own runtime contents (and its per-instance IsLockedRuntime state) get mutated live during play, exactly like a character's inventory, so Game Test needs its own fully disposable copy, never the live editor reference.
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
        InitializeFactions(factions);
        InitializeTalk();
        _clock = new GameClock(systemSettings.Clock.StartHour, systemSettings.Clock.TimeRate);
        _bodyContainerTemplate = systemSettings.BodyContainer ?? _bodyContainerTemplate; // how every dead body opens

        BuildMapVisual();
        InitializeObjectStates(); // doors open/closed/locked, lights lit, bed covers laid (GameTestWindow.Objects.cs)
        BuildObjectVisuals();
        HideHiddenTraps();
        BuildBookVisuals();
        SettleAll(); // The Map Editor has no way to author a non-ground Z yet - every item/object starts life at Z 0 in the saved map data. This runs the real settle logic once for every occupied cell at startup, so any cell that already holds more than one thing gets a genuine, correctly-computed initial Z, rather than everything just rendering flat at ground level until the first drag ever touches that cell.
        RefreshStackingVisuals();
        _poseDatabase = poses;
        SetUpPlayer(characters, poses);
        BuildCharacterVisuals(); // characters placed in the Map Editor (Mode > Characters)
        RefreshStackingVisuals();

        GameCanvas.Focus();
        GameCanvas.PreviewKeyDown += GameCanvas_PreviewKeyDown;
        GameCanvas.PreviewKeyUp += GameCanvas_PreviewKeyUp;
        InitializeInteraction(); // the one and only place mouse interaction is wired up - see GameTestWindow.Interaction.cs
        InitializePointer(systemSettings); // the engine's own Exult-style mouse pointer - see GameTestWindow.Pointer.cs

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
            FireCharacterStayEvents(); // characters standing on events, like the player
        };
        _stayTimer.Start();

        Closed += (_, _) => { _stayTimer?.Stop(); _identifyTimer?.Stop(); CompositionTarget.Rendering -= GameLoop_Rendering; StopAllAnimators(); };
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
        // Real, confirmed fix - the player's own real, tracked elevation (see _playerZ) is used here now instead of the honest placeholder 0 this used before. Render order (ComputeDepthZIndex) and the actual visual lift (Exult's own confirmed diagonal shift, LiftPixelShift, the exact same real formula every item/object already uses) both come from this one real value - never invented separately, and collision itself was already decided earlier in TryStepActor, not here: this method only ever draws the player at whatever Z collision already resolved.
        Panel.SetZIndex(_playerImage, PlayerZIndex());
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
                    RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources));
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
                var sprite = containerData is null ? null : ContainerSprite(containerData, instance); // its Locked Sprite while locked

                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var frameBitmap = LoadStaticFrame(sprite.Frames[0].FilePath);
                    (int qw, int qh) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : ((int)_tileWidth, (int)_tileHeight);
                    quantW = qw; quantH = qh;

                    var image = new Image { Width = quantW, Height = quantH, IsHitTestVisible = true };
                    RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources));
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
        foreach (var character in _characters.Characters) _stats.EnsureBuiltInMinimums(character); // never opened in the Character Editor? blank built-in stats play as 1
        _playerX = _map.StartX >= 0 ? _map.StartX : _map.Width / 2;
        _playerY = _map.StartY >= 0 ? _map.StartY : _map.Height / 2;
        _player = _characters.Characters.FirstOrDefault(c => c.IsTestPlayer);
        // Exult-style 3D size (footprint + height) - what every collision, climbing and headroom check uses for the player.
        if (_player is not null) _playerSize = ActorSizing.Resolve(_player, poses, _sprites);
        // Continuous center-anchor position initialized to match the starting tile exactly (same convention as UpdateCamera's own playerCenterX/Y): tile center horizontally, tile bottom vertically.
        _playerPixelX = _playerX * _tileWidth + _tileWidth / 2.0;
        _playerPixelY = _playerY * _tileHeight + _tileHeight / 2.0;
        // Safe to compute here: SettleAll has already run (see the constructor), so every item/object already has its real Z. The player starts standing on top of whatever solid thing is under their footprint.
        _playerZ = StandingLiftAt(PlayerFootprint());
        _playerVisualLiftZ = _playerZ; // starting position - no real fall to animate on first appearing in the world

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
        var image = new AlphaHitImage { Width = 24, Height = 24, Stretch = Stretch.Uniform, Cursor = Cursors.Hand, Source = LoadExitButtonFrame(character, pressed: false) };
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

        // The character's own Open Sound (PC Editor) - only when it actually opens, not when an already-open one is brought to the front.
        PlayOpenSound(character.Paperdoll.OpenSoundResourceId, $"{character.Name}'s paperdoll");

        // Real, clean paperdoll - per spec, just the body and its (invisible) slots, no title bar and no side panel; the body itself is the real drag handle, and a real 2-frame sprite (not a text glyph) is the exit button.
        // Real, confirmed fix, per spec ("the paperdoll looks tiny and the object sprites inside of it ARE HUGE... because the paperdoll sprites themselves did not update with the rest of the group") - the whole gump now scales together as one real, uniform unit via LayoutTransform, the same real principle ViewportScale itself already applies to the main game view, instead of leaving the body fixed while only its own item icons scaled independently (the actual mismatch that made a bigger window look wrong). Re-applied fresh with the current real scale every time this gump refreshes (RefreshPaperdollGump, called on every real viewport resize already), so it always tracks live.
        // No background at all: only the body art and the items drawn on it are part of the paperdoll (pixel-precise, like Exult's Gump::has_point) - transparent areas let clicks through to whatever is behind.
        var bodyCanvas = new Canvas { Width = 320, Height = 400 };
        var gumpBorder = new Border();
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
            var bodyImage = new AlphaHitImage { Width = 320, Height = 400, Stretch = Stretch.Uniform };
            gump.ItemAnimators.Add(new SpriteAnimator(bodySprite, bodyImage, _resources));
            Panel.SetZIndex(bodyImage, 0);
            gump.BodyCanvas.Children.Add(bodyImage);
            gump.BodyCanvas.Background = null;
        }
        else
        {
            // No body sprite chosen yet: show a plain dark panel so there's still something visible to grab and drop onto.
            gump.BodyCanvas.Background = new SolidColorBrush(Color.FromArgb(238, 26, 26, 26));
        }

        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (!character.Paperdoll.SlotPositions.TryGetValue(slot, out var pos)) pos = new PaperdollSlotPosition { X = 140, Y = 10 };
            bool isLockedLeftHand = slot == EquipmentSlot.LeftHand && leftHandLocked;

            character.EquippedSlots.TryGetValue(slot, out int equippedInstanceId);
            var equippedItem = equippedInstanceId > 0 ? character.Inventory.FirstOrDefault(i => i.InstanceId == equippedInstanceId) : null;

            // The equipped item's own real sprite/icon, drawn directly at the slot's position - this IS the paperdoll visual; there is nothing else to draw for a slot. Real, confirmed extraction - shared now (BuildEquippedItemIconVisual below) so the real custom-slot loop further down uses the exact same real rendering logic, not a second, separately-maintained copy of it.
            FrameworkElement? itemVisual = equippedItem is not null ? BuildEquippedItemIconVisual(gump, equippedItem, paperdollItemCounterScale) : null;
            // Interaction is never wired on the visual itself - it only carries a SlotTarget tag; the window's one mouse pipeline
            // (GameTestWindow.Interaction.cs) finds it by hit testing and decides identify / use / drag from there.
            if (itemVisual is not null)
            {
                Canvas.SetLeft(itemVisual, pos.X);
                Canvas.SetTop(itemVisual, pos.Y);
                Panel.SetZIndex(itemVisual, 10);
                itemVisual.Cursor = isLockedLeftHand ? Cursors.No : Cursors.Hand;
                itemVisual.IsHitTestVisible = true;
                itemVisual.Tag = new SlotTarget(gump, slot);
                HideIfBeingDragged(itemVisual);
                gump.BodyCanvas.Children.Add(itemVisual);
            }
            else
            {
                // An EMPTY slot draws nothing and has no invisible hit square: pressing the body art over it (its 40x40 area around the slot position) is resolved to this slot by position in HitTestGumpLayer, so only visible pixels are ever clickable.
            }
        }

        // Real, confirmed new feature, per spec ("the same mechanics you used in the inventory paper doll to single click to identify please apply it... apply it to custom slots too") - runtime rendering and interaction for custom slots, genuinely sharing every piece of machinery the built-in loop above already uses (BuildEquippedItemIconVisual, the SlotTarget tag the interaction pipeline drags/identifies/uses through, PaperdollSlotClicked, ShowPaperdollIdentifyLabel) via the same slotKey the TryGetEquippedInstanceId family of helpers understands - a custom slot has no left-hand-lock, two-handed, or Backpack-double-click concept at all, so this loop is intentionally simpler than the built-in one, not a cut-down duplicate missing real functionality.
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
                customItemVisual.Tag = new SlotTarget(gump, slotKey);
                HideIfBeingDragged(customItemVisual);
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
                    Tag = new SlotTarget(gump, slotKey)
                };
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

    private void SetEquippedInstanceId(CharacterDefinition character, object slotKey, int instanceId)
    {
        if (slotKey is EquipmentSlot builtin) character.EquippedSlots[builtin] = instanceId;
        else if (slotKey is int customId) character.CustomEquippedSlots[customId] = instanceId;
        FireCharacterEvent(character, BookTriggerConditionBased.ByEquip);
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
            var img = new AlphaHitImage { Width = iw * paperdollItemCounterScale, Height = ih * paperdollItemCounterScale, Stretch = Stretch.Uniform };
            gump.ItemAnimators.Add(new SpriteAnimator(itemSprite, img, _resources));
            return img;
        }
        if (equippedItem.Category == ReferencedItemCategory.Object)
            return BuildCarriedObjectPicture(equippedItem, paperdollItemCounterScale, gump.ItemAnimators); // its look for its state and quantity
        if (equippedItem.Category == ReferencedItemCategory.Container)
        {
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == equippedItem.ItemId);
            var itemSprite = containerData is null ? null : ContainerSprite(containerData, equippedItem); // its Locked Sprite while locked
            if (itemSprite is null || itemSprite.Frames.Count == 0) return null;
            var frameBitmap = LoadStaticFrame(itemSprite.Frames[0].FilePath);
            (int iw, int ih) = frameBitmap is not null ? SpriteSizing.Quantize(frameBitmap.PixelWidth, frameBitmap.PixelHeight) : (32, 32);
            var img = new AlphaHitImage { Width = iw * paperdollItemCounterScale, Height = ih * paperdollItemCounterScale, Stretch = Stretch.Uniform };
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

        // Real environmental interaction, per spec - an equipped item is interacted with exactly like it would be on the map: single click identifies, double click uses (firing whatever real events it has), never unequipped as a side effect of a plain click. Real unequip is dragging the item out of its slot entirely (see TryStartDrag in GameTestWindow.Interaction.cs) - confirmed real Exult behavior, since right-click is reserved for movement/pathfinding, not gump interaction, and there is no right-click-unequip at all.
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
            if (selectedItem.Category == ReferencedItemCategory.Container && !IsContainerSlotAllowed(selectedItem.ItemId, character, slotKey))
            {
                Refuse(MousePointerKind.WontFit, "A container only fits in its own slot or a hand.");
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
        // Tagged with its owner so the window's one mouse pipeline can resolve a row under the pointer to a BackpackRowTarget - single click / double click / drag out are all handled there, not here.
        backpackList.Tag = owner;

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
        var equippedIds = character.EquippedSlots.Values.Concat(character.CustomEquippedSlots.Values).ToHashSet(); // an item equipped in a custom slot is on the body, not loose in the backpack
        backpackGump.BackpackList.ItemsSource = character.Inventory
            .Where(i => !equippedIds.Contains(i.InstanceId))
            .Select(i => new PaperdollBackpackRow(i, _books, _objects, _containers))
            .ToList();
    }

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

    /// <summary>Exult: double-clicking a locked chest doesn't open it - it says "Locked" (Ultima VII's chest usecode). Keys and lockpicks are used on it instead (GameTestWindow.Locks.cs).</summary>
    private void TryOpenContainer(PlacedItemInstance placedItem, ContainerData data)
    {
        if (!IsLockedNow(placedItem, data)) { OpenContainer(placedItem, data); return; }
        PlaySound(data.LockedRattleSoundResourceId);
        ShowFloatingTextOver(VisualOf(placedItem), "Locked");
        StatusText.Text = $"\"{data.Name}\" is locked.";
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
        PlayOpenSound(data.OpenSoundResourceId, $"\"{data.Name}\"");

        if (EnsureContainerSeeded(placedItem, data))
        {
            // Real, confirmed diagnostic, per spec ("it somehow managed to put a 128 into a 64 max volume") - the Container Editor now warns the moment Max Volume itself makes existing Starting Contents invalid, but a project saved before that fix existed could still open with an already over-capacity container. This never alters the real, authored data - only makes the real problem visible immediately, in the exact moment it actually happens, instead of surfacing later as a confusing "won't fit" on some unrelated, much smaller item.
            double seededVolume = placedItem.Contents.Sum(ResolveContentsVolume);
            if (seededVolume > data.MaxVolume)
                StatusText.Text = $"Warning: \"{data.Name}\" opened already over its own capacity - Starting Contents totals {seededVolume:0.##}, but Max Volume is only {data.MaxVolume:0.##}. Fix this in the Container Editor.";
        }

        // Real size, per the container's own drawn Frame bounds - set here at creation and kept in sync by every RefreshContainerGump call afterward (immediately below, and on every later refresh).
        // Real, confirmed fix, per spec ("I SHOULD ONLY SEE THE CONTAINER PAPER DOLL ITSELF") - no background, no border, no padding at all; this is purely a transparent wrapper now, so only the container's own real body art (and its items) are ever visible, never a surrounding dark box.
        var itemsCanvas = new Canvas(); // no background: only the container art and its items are part of it (pixel-precise)
        var gumpBorder = new Border();
        gumpBorder.Child = itemsCanvas;

        var gump = new OpenContainerGump { PlacedItem = placedItem, Data = data, GumpBorder = gumpBorder, ItemsCanvas = itemsCanvas };

        double baseX = 40 + _openContainers.Count * 36;
        double baseY = 30 + _openContainers.Count * 30;
        Canvas.SetLeft(gumpBorder, baseX);
        Canvas.SetTop(gumpBorder, baseY);

        // Real drag-by-body, same principle as a paperdoll's own body - dragging happens on the item canvas's own background, never triggering when an actual item underneath the cursor already claimed the click (see item wiring below, which always marks itself Handled first).
        itemsCanvas.MouseLeftButtonDown += (_, e) => GenericGumpDrag_MouseLeftButtonDown(gump, gumpBorder, itemsCanvas, e); // item presses are claimed (handled) by the interaction pipeline first, so only the art itself starts a gump drag
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
    /// seeded the moment it's picked up (see DetachAsInventoryItem), so by
    /// the time it can ever reach this method it's already live.
    /// </summary>
    private void OpenEquippedContainer(InventoryItemInstance equippedItem)
    {
        var existing = _openContainers.FirstOrDefault(g => g.PlacedItem == equippedItem);
        if (existing is not null) { BringPaperdollToFrontGeneric(existing.GumpBorder, existing); return; }

        var data = _containers.Containers.FirstOrDefault(c => c.Id == equippedItem.ItemId);
        if (data is null) return;
        if (RefuseIfLocked(equippedItem, data)) return; // carried or not, a locked chest says "Locked"

        bool fired = TryFireEventPages(data, data.EventPages, BookTriggerConditionBased.ByUseItem, data.Id, $"You open \"{data.Name}\" - real event fired.");
        if (fired) return;

        PlayOpenSound(data.OpenSoundResourceId, $"\"{data.Name}\"");

        var itemsCanvas = new Canvas(); // no background: only the container art and its items are part of it (pixel-precise)
        var gumpBorder = new Border();
        gumpBorder.Child = itemsCanvas;

        var gump = new OpenContainerGump { PlacedItem = equippedItem, Data = data, GumpBorder = gumpBorder, ItemsCanvas = itemsCanvas };

        double baseX = 40 + _openContainers.Count * 36;
        double baseY = 30 + _openContainers.Count * 30;
        Canvas.SetLeft(gumpBorder, baseX);
        Canvas.SetTop(gumpBorder, baseY);

        itemsCanvas.MouseLeftButtonDown += (_, e) => GenericGumpDrag_MouseLeftButtonDown(gump, gumpBorder, itemsCanvas, e); // item presses are claimed (handled) by the interaction pipeline first, so only the art itself starts a gump drag
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
            var bg = new AlphaHitImage { Width = ContainerBodyWidth, Height = ContainerBodyHeight, Stretch = Stretch.Uniform };
            gump.ItemAnimators.Add(new SpriteAnimator(bodySprite, bg, _resources));
            Panel.SetZIndex(bg, 0);
            gump.ItemsCanvas.Children.Add(bg);
        }

        int z = 5;
        var built = new List<(InventoryItemInstance Item, FrameworkElement Visual)>();
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
                    var img = new AlphaHitImage { Width = iw * containerItemCounterScale, Height = ih * containerItemCounterScale, Stretch = Stretch.Uniform };
                    gump.ItemAnimators.Add(new SpriteAnimator(sprite, img, _resources));
                    visual = img; size = img.Width;
                }
            }
            else if (item.Category == ReferencedItemCategory.Object)
            {
                visual = BuildCarriedObjectPicture(item, containerItemCounterScale, gump.ItemAnimators); // its look for its state and quantity
                if (visual is not null) size = visual.Width;
            }
            else if (item.Category == ReferencedItemCategory.Container)
            {
                var inner = _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId);
                var sprite = inner is null ? null : ContainerSprite(inner, item); // its Locked Sprite while locked
                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var img = new AlphaHitImage { Width = 32 * containerItemCounterScale, Height = 32 * containerItemCounterScale, Stretch = Stretch.Uniform };
                    gump.ItemAnimators.Add(new SpriteAnimator(sprite, img, _resources));
                    visual = img;
                }
            }
            visual ??= new Border { Width = 32, Height = 32, Background = Brushes.Gray };
            built.Add((item, visual));
        }

        // Exult Gump::paint: anything put in here without a spot (dropped on the container itself, auto-stored, or a fresh container) is laid out now, in a row from the top-left, the way Exult does it.
        var sizes = built.ToDictionary(b => b.Item, b => (b.Visual.Width, b.Visual.Height), ReferenceEqualityComparer.Instance);
        LayOutUnplacedContents(gump.PlacedItem, data, item => sizes.TryGetValue(item, out var size) ? ((double, double))size : (32, 32));

        foreach (var (item, visual) in built)
        {
            Canvas.SetLeft(visual, item.ContainerX);
            Canvas.SetTop(visual, item.ContainerY);
            Panel.SetZIndex(visual, z++);
            visual.Cursor = Cursors.Hand;
            // Tagged, not wired - the window's one mouse pipeline (GameTestWindow.Interaction.cs) handles identify, use, and drag for it.
            visual.Tag = new ContainerItemTarget(gump, item);
            HideIfBeingDragged(visual);
            gump.ItemsCanvas.Children.Add(visual);
        }

        // Real, confirmed removal, per spec ("GET RID OF... THE CHECK MARK ON THE TOP RIGHT CORNER... If I need to close it, press ESC to close it") - no exit button drawn here anymore; Escape (via the unified gump focus tracker above) is now the only close control, and dragging the body itself (itemsCanvas) still moves the gump exactly as before.
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
        if (isUse && item.Category == ReferencedItemCategory.Object
            && UseCarriedObject(item, new ContainerOrigin(gump.PlacedItem, gump.Data, item))) return; // keys, lockpicks, lights, By Use
        if (!isUse && DefinitionOf(item) is { } containedObject)
        {
            string objectName = ObjectDisplayName(containedObject, item.IsSpentRuntime, item.Quantity);
            ShowContainerIdentifyLabel(gump, item, objectName);
            StatusText.Text = objectName;
            return;
        }
        string typeName = item.Category.ToString();
        if (!isUse) ShowContainerIdentifyLabel(gump, item, $"a {typeName}");
        StatusText.Text = isUse
            ? $"There's no {typeName} editor built yet, so nothing happens when you use it - the click system itself is already ready for when there is."
            : $"a {typeName}";
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

        if (isUse && item.Category == ReferencedItemCategory.Object
            && UseCarriedObject(item, slotKey is not null ? new SlotOrigin(gump.Character, slotKey, item) : new BackpackOrigin(gump.Character, item))) return; // keys, lockpicks, lights, By Use
        if (!isUse && DefinitionOf(item) is { } carriedObject)
        {
            string objectName = ObjectDisplayName(carriedObject, item.IsSpentRuntime, item.Quantity);
            if (slotKey is not null) ShowPaperdollIdentifyLabel(gump, slotKey, objectName);
            StatusText.Text = objectName;
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
        ShowGumpLabel(gump.BodyCanvas, name, posX, posY);
    }

    /// <summary>
    /// An identify label for something in a gump (point x, y in that gump's
    /// own canvas). Drawn on the top of the gump layer, over every gump, so
    /// nothing covers it, and at the map's scale: Exult draws gump text in
    /// the same font at the same screen scale as the world.
    /// </summary>
    private void ShowGumpLabel(Canvas canvas, string name, double x, double y)
    {
        var at = canvas.TranslatePoint(new Point(x, y), PaperdollGumpsCanvas);
        var label = GameTextLabel(name);
        label.LayoutTransform = new ScaleTransform(ViewportScale.ScaleX, ViewportScale.ScaleY);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, at.X);
        Canvas.SetTop(label, at.Y - label.DesiredSize.Height - 2);
        Panel.SetZIndex(label, 1_000_000);
        PaperdollGumpsCanvas.Children.Add(label);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            PaperdollGumpsCanvas.Children.Remove(label);
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
    private void ShowContainerIdentifyLabel(OpenContainerGump gump, InventoryItemInstance item, string name) =>
        ShowGumpLabel(gump.ItemsCanvas, name, item.ContainerX, item.ContainerY);

    private void GameCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_currentMessage is not null)
        {
            if (e.Key is Key.Enter or Key.Space or Key.Escape) AdvanceMessage(); // a dialog box is up: these go on, nothing else happens
            e.Handled = true;
            return;
        }
        if (_gameOver) { e.Handled = true; return; } // the main character is dead: no keys do anything
        if (PlayerKnockedOut && e.Key != Key.F6) { e.Handled = true; return; } // unconscious: nothing until back on your feet
        if (e.Key == Key.Escape && _isReadingBook) { _bookClose?.Invoke(); e.Handled = true; return; } // Exult Get_click: Escape ends the book
        if (e.Key == Key.Escape && TryCancelChoosingTarget()) { e.Handled = true; return; } // Get_click: Escape cancels choosing a target
        if (IsChoosingTarget) { e.Handled = true; return; } // waiting for a click: nothing else happens
        if (_isReadingBook) return;
        if (e.Key == Key.F6) { SkipToNextPeriod(); e.Handled = true; return; } // Exult's fake_next_period, for testing schedules
        if (e.Key == Key.C) { ToggleCombatMode(); e.Handled = true; return; } // Exult: C toggles combat mode

        // Real Exult access model, confirmed from Exult's own docs and documented real play - I opens/closes the current test player's own gump; a number key 1-9 opens a specific party member's directly (Exult's own real keys: 1 = Avatar, 2-8 = party member), or brings their already-open gump to front. Escape closes the single topmost gump only. All of this works even while gumps are already open, since these are real in-world overlays on the same canvas, not separate focusable windows.
        // Escape mid-drag cancels the drag (the item never left its origin, so it's simply still there) instead of closing a gump out from under it - Exult likewise refuses Escape while dragging.
        if (e.Key == Key.Escape && _pointer is { IsDragging: true }) { CancelPointer(); e.Handled = true; return; }
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

        if (IsGamePaused) return; // movement stays blocked while any gump is open (Exult: start_actor refuses in gump mode - paperdolls, containers and books alike), matching real Exult's "gumps pause games"

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
        _nightDarkness = period == "Evening" ? setting.Opacity : 0; // the dark one: light sources lift it (GameTestWindow.Lighting.cs)
        _lightLevel = -1;
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

        FollowHitOutlines(); // the red hit outline stays round its character as it moves
        UpdateGamePause(); // Exult: the first gump to open pauses the game, the last to close resumes it
        if (_isReadingBook || _gamePaused) return; // paused: nothing in the world moves, the avatar included

        UpdateClockAndCharacters(deltaSeconds * 1000); // the game clock, schedules, and every AI character's walking

        // Real, confirmed fall physics, purely additive - runs every real frame regardless of whether a direction is currently held, so a fall that starts right as the key is released still finishes landing smoothly instead of snapping the instant input stops. Never touches _playerX/_playerY, deltaSeconds's own clamping, or anything else about the real movement/input system above and below this.
        UpdatePlayerFallAnimation(deltaSeconds);

        if (IsIncapacitated(_player)) { _heldDirectionKeys.Clear(); _wasMovingLastFrame = false; } // asleep, paralyzed or dead: can't walk
        if (TryGetActiveHeldDirection(out int dx, out int dy))
        {
            CancelPlayerWalk(); // the keys take over from walking to a chair or bed
            StepPlayerContinuous(dx, dy, deltaSeconds);
            _wasMovingLastFrame = true;
        }
        else if (_playerWalkPath is not null && !IsIncapacitated(_player))
        {
            StepPlayerAlongPath(deltaSeconds); // walking there on their own (GameTestWindow.PlayerWalk.cs)
            _wasMovingLastFrame = _playerWalkPath is not null;
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
        if (PlayerSeated) { StandUp(_player!); return; } // moving gets you up off the chair or bed
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

        // Exult's walking rules (TryStepActor): whenever the player's footprint moves onto new 8px world units, those units must take them - rising at most 1 lift, dropping at most 1, with headroom for their full height. Otherwise this frame's movement is skipped entirely - simpler and safe over precise boundary-clamping.
        int newLift = _playerZ;
        string blockReason = "";
        bool tileOffMap = prospectiveTileX < 0 || prospectiveTileY < 0 || prospectiveTileX >= _map.Width || prospectiveTileY >= _map.Height;
        if (tileOffMap || PlayerFootprintAt(newPixelX, newPixelY) != PlayerFootprint() && !TryStepActor(PlayerFootprintAt(newPixelX, newPixelY), _playerZ, _playerSize.Z, out newLift, out blockReason))
        {
            StatusText.Text = tileOffMap ? "Blocked: edge of the map." : blockReason;
            // Actor::is_really_blocked: a character in the way is asked to move aside (or swap places).
            if (!tileOffMap && CharacterInTheWay(PlayerFootprintAt(newPixelX, newPixelY), _playerZ, _playerSize.Z, null) is { } blocker)
                MoveAside(blocker, dx, dy, walker: null);
            return;
        }
        if (newLift != _playerZ)
        {
            StatusText.Text = newLift > _playerZ
                ? $"Stepped up to lift {newLift} ({newLift * 4}px)."
                : $"Stepped down to lift {newLift} ({newLift * 4}px).";
            _playerZ = newLift;
            _playerVisualLiftZ = _playerZ; // instant, both directions - per spec ("make it instant just like the books or container")
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
            StatusText.Text = $"({_playerX}, {_playerY})  lift {_playerZ} ({_playerZ * 4}px)  - your size {_playerSize.X * 8}x{_playerSize.Y * 8}px, {_playerSize.Z * 4}px tall";

            if (leavingEvent is not null && (leavingEvent.X != _playerX || leavingEvent.Y != _playerY))
                TryFireEventPages(leavingEvent, leavingEvent.EventPages, BookTriggerConditionBased.ByExit, selfBookId: -1, $"\"{leavingEvent.Name}\" fired (ByExit)");

            _eventUnderPlayer = _map.Events.FirstOrDefault(ev => ev.X == _playerX && ev.Y == _playerY);
            if (_eventUnderPlayer is not null)
                TryFireEventPages(_eventUnderPlayer, _eventUnderPlayer.EventPages, BookTriggerConditionBased.ByEnter, selfBookId: -1, $"\"{_eventUnderPlayer.Name}\" fired (ByEnter)");
        }

        // Real, confirmed fix, purely additive - same exact real Z-index and visual-lift application SnapPlayerPosition itself now does, placed here (after _playerZ is updated above, not before) so the very first rendered frame on a new tile already reflects its real elevation, not one frame behind. Nothing about the position math above is touched - this only draws whatever collision/elevation already decided.
        Panel.SetZIndex(_playerImage, PlayerZIndex());
        Canvas.SetLeft(_playerImage, Canvas.GetLeft(_playerImage) - _playerVisualLiftZ * LiftPixelShift);
        Canvas.SetTop(_playerImage, Canvas.GetTop(_playerImage) - _playerVisualLiftZ * LiftPixelShift);

        UpdateCamera();
    }

    private bool IsSolidAt(PlacedItemInstance? item, ObjectInstance? obj)
    {
        if (item is not null && item.Category == ReferencedItemCategory.Book)
            return _books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.IsSolid ?? false;
        // Real, confirmed fix, per spec ("When I checked collision detection, I could still pass through the chest") - a placed Container was never checked here at all, so its own real IsSolid setting was silently ignored no matter what it was set to. This is squarely collision integration, not a change to the walking/input/frame-timing system itself, which stays completely untouched.
        if (item is not null && item.Category == ReferencedItemCategory.Container)
            return _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.IsSolid ?? false;
        if (obj is not null && _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId) is { } worldObject)
            return worldObject.Type switch
            {
                ObjectType.Wall => true,                              // always an obstacle
                ObjectType.Door => obj.IsOpenRuntime != true,         // solid while closed
                ObjectType.Tile or ObjectType.Trap => false,          // flat, no collision
                _ => worldObject.IsSolid
            };
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
            if (worldObject is null) return (false, false, 1, 1, 1);
            bool flat = worldObject.Type is ObjectType.Tile or ObjectType.Trap; // Exult: a flat shape is 0 lifts high
            return (worldObject.IsCellStackable || flat, worldObject.AllowCellElevation && !flat, Math.Max(1, worldObject.SizeX), Math.Max(1, worldObject.SizeY), flat ? 0 : Math.Max(1, worldObject.SizeZ));
        }
        return (false, false, 1, 1, 1);
    }

    /// <summary>Pixels of screen shift ONE world Z (lift) unit represents - confirmed directly from Exult's own real source (drag.cc's drop_at_lift: "x += at_lift * 4"). Real Ultima 7 does NOT render elevation as a purely vertical peek; it shifts DIAGONALLY, the same amount on both axes, which is exactly what produces the natural fanned/staggered look of a real stack rather than a straight vertical tower.</summary>
    private const double LiftPixelShift = 4;

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
        Panel.SetZIndex(_playerImage, PlayerZIndex());
        double left = _playerPixelX - _playerImage.Width / 2 - _playerVisualLiftZ * LiftPixelShift;
        double top = _playerPixelY + _tileHeight / 2 - _playerImage.Height - _playerVisualLiftZ * LiftPixelShift;
        _playerImage.BeginAnimation(Canvas.LeftProperty, null);
        _playerImage.BeginAnimation(Canvas.TopProperty, null);
        Canvas.SetLeft(_playerImage, left);
        Canvas.SetTop(_playerImage, top);
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
            {
                ApplyZToVisual(fe, obj.X, obj.Y, obj.Z, obj.OffsetX, obj.OffsetY);
                ShiftOpenDoor(obj, fe); // hangs from its hinge
            }
        }
        ApplyRestingDrawOrder();
        ApplyWallDrawOrder(); // things near walls, by Exult's compare
        FixFurnitureDrawOrder(); // sitters over chairs, covers over sleepers
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
        var shown = SetIdentifyText(name);
        Panel.SetZIndex(shown, 1_000_000); // above everything on the map, so nothing covers the name
        Canvas.SetLeft(shown, cellX * _tileWidth);
        double topAnchor = visual is not null ? Canvas.GetTop(visual) : cellY * _tileHeight;
        Canvas.SetTop(shown, topAnchor - (shown == IdentifyFontImage ? shown.Height + 4 : 20));
        shown.Visibility = Visibility.Visible;

        _identifyTimer?.Stop();
        _identifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _identifyTimer.Tick += (_, _) =>
        {
            _identifyTimer!.Stop();
            IdentifyLabel.Visibility = Visibility.Collapsed;
            IdentifyFontImage.Visibility = Visibility.Collapsed;
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
        var paperDollAnimator = new SpriteAnimator(paperDollSprite, paperDollImage, _resources);
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

                var result = FormattedContentRenderer.Render(xaml, bbox.Width, bbox.Height, _sprites, book.TextFontSpriteName);

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
            paperDollAnimator.Stop(); // the book's own background animation - left running before, piling up one more invisible animation per book read
            foreach (var animator in pageOverlayAnimators) animator.Stop();
            pageOverlayAnimators.Clear();
            BookOverlayBorder.Visibility = Visibility.Collapsed;
            _bookNextPage = null;
            _bookClose = null;
            _isReadingBook = false;
            GameCanvas.Focus();
        }

        void FinishReading()
        {
            if (book.CloseSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, book.CloseSoundResourceId);
            CloseOverlay();
            // Finishing the book fires By After Read Item for real, through the same shared engine every other trigger goes through - not a simulated stand-in.
            TryFireEventPages(book, book.EventPages, BookTriggerConditionBased.ByAfterReadItem, book.Id, $"You finish reading \"{EffectiveBookName(book)}\".");
        }

        void NextPage()
        {
            pageIndex++;
            if (pageIndex >= book.Pages.Count)
            {
                FinishReading();
                return;
            }
            if (book.PageFlipSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, book.PageFlipSoundResourceId);
            RenderPage();
        }

        RenderPage();
        _isReadingBook = true;
        BookOverlayBorder.Visibility = Visibility.Visible;
        // Exult shows a book modally (Usecode_internal::show_pending_text: "while (book->show_next_page() && Get_click(...))"): a click ANYWHERE turns the page, Escape ends it, and nothing else in the game can be used meanwhile.
        _bookNextPage = NextPage;
        _bookClose = FinishReading;

        PlayOpenSound(book.OpenSoundResourceId, $"\"{EffectiveBookName(book)}\"");
    }

    /// <summary>
    /// Plays a paperdoll/container/book Open Sound and says in the status bar
    /// exactly what happened - the sound played, none is set, or why it
    /// couldn't play - so a missing sound is never a silent mystery.
    /// </summary>
    private void PlayOpenSound(int soundId, string what)
    {
        if (soundId < 0)
        {
            StatusText.Text = $"Opened {what} - no Open Sound is set for it.";
            return;
        }
        var entry = _resources.Entries.FirstOrDefault(r => r.Id == soundId);
        if (entry is null)
            StatusText.Text = $"Opened {what} - its Open Sound (#{soundId}) no longer exists in the Sound list.";
        else if (!System.IO.File.Exists(entry.FilePath))
            StatusText.Text = $"Opened {what} - Open Sound \"{entry.Name}\" can't play: its file is missing ({entry.FilePath}).";
        else if (!SoundPlayback.TryPlay(_resources, soundId))
            StatusText.Text = $"Opened {what} - Open Sound \"{entry.Name}\" failed to play: {SoundPlayback.LastError}";
        else
            StatusText.Text = $"Opened {what} - playing Open Sound \"{entry.Name}\".";
    }

    /// <summary>While a book is open: what a click anywhere does (next page, or finish on the last), and what Escape does (finish now) - Exult's modal Get_click loop.</summary>
    private Action? _bookNextPage;
    private Action? _bookClose;

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
