using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class MainWindow : Window
{
    private string? _currentProjectFilePath;
    private Guid? _currentProjectId;
    private string? _currentProjectFolder;
    private string? _currentProjectName;
    private string? _currentProjectPlatform;
    private DateTime _currentProjectCreatedAtUtc;
    private BitmapSource? _tilesetBitmap;
    private TilesetDefinition? _tileset;
    private int _paletteColumns;
    private int _paletteRows;
    private int _selectedTileIndex = -1;
    /// <summary>Ctrl-click adds/removes a tile here without disturbing _selectedTileIndex (which stays "the tile you paint with"). This set is purely for bulk Duplicate/Delete from the right-click menu - non-contiguous, any tiles at any positions, per spec.</summary>
    private readonly HashSet<int> _multiSelectedTileIndices = new();
    private readonly List<Rectangle> _multiSelectHighlights = new();

    private MapData? _map;
    private ObjectDatabase _objectDatabase = new();
    private CurrencyDatabase _currencyDatabase = new();
    private string? _objectDatabasePath;

    private TilePropertiesDatabase _tilePropertiesDatabase = new();
    private string? _tilePropertiesPath;

    private ResourceDatabase _resourceDatabase = new();
    private VideoDatabase _videoDatabase = new();
    private FactionDatabase _factionDatabase = new();
    private CharacterDatabase _characterDatabase = new();
    private SpriteDatabase _spriteDatabase = new();
    private BookDatabase _bookDatabase = new();
    private ContainerDatabase _containerDatabase = new();
    private SystemSettings _systemSettings = new();
    private VariableDatabase _variableDatabase = new();
    private StatDatabase _statDatabase = StatDatabase.CreateDefault();
    private PoseDatabase _poseDatabase = PoseDatabase.CreateDefault();

    private enum EditMode { Map, Event, StartPosition, Item, Character, Object, Free, Zone, House }
    private EditMode _editMode = EditMode.Map;

    private enum DrawShape { Single, Line, Rectangle, Circle }
    private DrawShape _drawShape = DrawShape.Single;

    // Visual elements currently drawn on the map canvas, keyed by (x, y) cell.
    // Kept so painting a cell again just swaps its image instead of piling up
    // duplicate elements.
    private readonly Dictionary<(int x, int y), Image> _mapCellVisuals = new();
    /// <summary>Tracks which map cells currently have a LIVE animator running, so repainting or erasing a cell can Stop() the old one first - SpriteAnimator starts its own DispatcherTimer immediately and runs forever until told to stop, so leaving a stale one behind on repaint would leak a running timer per overwritten cell.</summary>
    private readonly Dictionary<(int x, int y), SpriteAnimator> _mapCellAnimators = new();
    /// <summary>Same idea for the palette's own animated tile previews - cleared and re-stopped at the start of every DrawPalette() call, since that method already rebuilds the whole visual tree from scratch each time.</summary>
    private readonly List<SpriteAnimator> _paletteTileAnimators = new();

    // Placed object visuals, keyed by InstanceId (several objects can share a cell later).
    private readonly Dictionary<int, Image> _mapObjectVisuals = new();

    // Placed event marker visuals, keyed by GameEvent.Id.
    private readonly Dictionary<int, UIElement> _mapEventVisuals = new();

    private int _selectedObjectId = -1;

    // Shape-drag state (Draw menu: Line/Rectangle/Circle) - start cell recorded
    // on mouse-down, shape is stamped on mouse-up.
    private (int x, int y)? _shapeStartCell;
    private readonly List<Rectangle> _shapePreviewVisuals = new();
    private SolidColorBrush? _shapePreviewBrush;
    private bool _isFreehandPainting; // click-and-drag with Draw=Single paints continuously, like a normal pencil tool - no modifier key needed

    // Ctrl+drag "pick up and move" state, for object instances or events.
    private GameEvent? _draggingEvent;
    private Border? _startPositionVisual;
    private Rectangle? _hoverCursorVisual;
    /// <summary>Each placed book / container's picture - one per placement (Free Placement lets several share a cell).</summary>
    private readonly Dictionary<PlacedItemInstance, UIElement> _mapItemVisuals = new(ReferenceEqualityComparer.Instance);
    /// <summary>Which event marker keyboard shortcuts (Ctrl+X, Delete) act on - set whenever a mouse click (left or right) lands on an existing marker.</summary>
    private GameEvent? _selectedMapEvent;
    /// <summary>Cut/Duplicate both populate this - Paste (mouse menu or Ctrl+V) creates a new event from it wherever you target next.</summary>
    private List<BookEventPage>? _copiedEventPages;

    private Rectangle? _paletteHighlight;

    public MainWindow()
    {
        InitializeComponent();
        BuildToolIcons(); // the editors as icons after Zoom (MainWindow.Toolbar.cs)
        Title += $" [{BuildInfo.CurrentBuild}]";
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        InitZoom();
        InitFloors();
        ProjectSession.Bind(() => _factionDatabase, () => _characterDatabase, () => _spriteDatabase, (owner, characterId) =>
        {
            var editor = new CharacterEditorWindow(_characterDatabase, _statDatabase, _poseDatabase, _spriteDatabase, _bookDatabase, _objectDatabase, _resourceDatabase)
            { Owner = owner, InitialCharacterId = characterId };
            editor.ShowDialog();
            RefreshCharactersPalette();
            PlaceAllCharacterVisualsIfMapShown();
        });
        ProjectSession.ResourcesProvider = () => _resourceDatabase;
        ProjectSession.VideosProvider = () => _videoDatabase;
        ProjectSession.ProjectIdProvider = () => _currentProjectId;
        ProjectSession.VariablesProvider = () => _variableDatabase;
        ProjectSession.BooksProvider = () => _bookDatabase;
        ProjectSession.ObjectsProvider = () => _objectDatabase;
        ProjectSession.PosesProvider = () => _poseDatabase;
        ProjectSession.CurrenciesProvider = () => _currencyDatabase;
        ProjectSession.ContainersProvider = () => _containerDatabase;
        ProjectSession.HousesProvider = () => _houses; // MainWindow.Houses.cs
        ProjectSession.StatsProvider = () => _statDatabase;
        ProjectSession.CharactersProvider = () => _characterDatabase;
        ProjectSession.MapSnapshotProvider = SnapshotOpenMap;
        ProjectSession.MapFloorPictureProvider = (floor, seeInside) => RenderMapPicture(floor, seeInside);
        ProjectSession.MapEditorFloorProvider = () => _currentFloor;
        ProjectSession.PlacedSpotProvider = characterId =>
            _map?.Characters.FirstOrDefault(c => c.CharacterId == characterId) is { } placed ? (placed.X, placed.Y) : null;
        ProjectSession.ZonesProvider = () => _map?.Zones;
        ProjectSession.MapThingsProvider = (x, y) =>
        {
            var things = new List<(string Key, string Name)>();
            if (_map is null) return things;
            foreach (var o in _map.Objects.Where(o => o.ContainerInstanceId < 0 && o.X == x && o.Y == y))
                things.Add(($"Object:{o.InstanceId}:{o.ObjectId}", _objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId)?.Name ?? "object"));
            foreach (var i in _map.Items.Where(i => i.X == x && i.Y == y))
                things.Add(($"Item:{i.Category}:{i.ItemId}", i.Category == ReferencedItemCategory.Book
                    ? _bookDatabase.Books.FirstOrDefault(b => b.Id == i.ItemId)?.Name ?? "book"
                    : _containerDatabase.Containers.FirstOrDefault(c => c.Id == i.ItemId)?.Name ?? "container"));
            return things;
        };
        ProjectSession.SettingsProvider = () => _systemSettings;
        ProjectSession.TileSourceProvider = TileSource;
        ProjectSession.WorldsProvider = () => _worlds;
        ProjectSession.WorldTileProvider = TileSourceFor;
        ProjectSession.SheetProvider = name => Sheet(name) is { } sheet ? (sheet.Bitmap, sheet.Columns) : null;
        ProjectSession.EventNamesProvider = () => (_map?.Events.Select(e => e.Name) ?? Enumerable.Empty<string>())
            .Concat(_objectDatabase.Objects.Select(o => o.Name)).Concat(_bookDatabase.Books.Select(b => b.Name))
            .Concat(_containerDatabase.Containers.Select(c => c.Name)).Concat(_characterDatabase.Characters.Select(c => c.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
    }

    /// <summary>A picture of the whole map on the Map Editor's floor, seen inside, for picking a cell from other windows (MainWindow.MapPicture.cs).</summary>
    private (System.Windows.Media.Imaging.BitmapSource, int, int, int, int)? SnapshotOpenMap() =>
        RenderMapPicture(_currentFloor, seeInside: true) is { } p ? (p.Picture, p.TileWidth, p.TileHeight, p.Columns, p.Rows) : null;

    // ---------- Tileset loading ----------

    private void RefreshTilesetCombo()
    {
        var tilesetGroup = _spriteDatabase.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.TilesetGroupName);
        var selected = TilesetCombo.SelectedItem as SpriteEntry;
        TilesetCombo.ItemsSource = null;
        TilesetCombo.ItemsSource = tilesetGroup?.Sprites ?? new List<SpriteEntry>();
        if (selected is not null && tilesetGroup is not null && tilesetGroup.Sprites.Contains(selected))
            TilesetCombo.SelectedItem = selected;
    }

    private void TilesetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TilesetCombo.SelectedItem is not SpriteEntry sprite) return;
        if (sprite.Frames.Count == 0)
        {
            MessageBox.Show($"\"{sprite.Name}\" has no frames yet - add at least one tile image to it in the Sprite Editor first.",
                "Empty Tileset", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // A map already painted: this sheet is added to it (or shown again) to paint with - never swapped in under the painted tiles.
        if (_map is not null && _tileset is not null && (_map.Layers.Any(l => l.TileIndices.Any(t => t >= 0))
            || string.Equals(_map.Tileset.TilesetSpriteName, sprite.Name, StringComparison.Ordinal) && _tilesetBitmap is not null))
        {
            if (!ShowSheetInPalette(sprite))
            {
                TilesetCombo.SelectionChanged -= TilesetCombo_SelectionChanged;
                TilesetCombo.SelectedItem = TilesheetSprite(_paletteSheetName.Length > 0 ? _paletteSheetName : _map.Tileset.TilesetSpriteName);
                TilesetCombo.SelectionChanged += TilesetCombo_SelectionChanged;
            }
            return;
        }

        var (bitmap, tileWidth, tileHeight, columns, rows) = BuildCompositeTilesetBitmap(sprite);
        if (bitmap is null)
        {
            MessageBox.Show($"Could not build a tileset from \"{sprite.Name}\" - one or more of its frame images could not be loaded.",
                "Tileset Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _tilesetBitmap = bitmap;
        bool tilesetGenuinelyChanged = _map is not null && !string.Equals(_map.Tileset.TilesetSpriteName, sprite.Name, StringComparison.Ordinal);
        bool mapHasPaintedTiles = _map is not null && _map.Layers.Any(l => l.TileIndices.Any(t => t >= 0));

        if (tilesetGenuinelyChanged && mapHasPaintedTiles)
        {
            var result = MessageBox.Show(
                $"This map already has tiles painted using a different tileset. Switching to \"{sprite.Name}\" will re-interpret every already-painted tile's index against this new tileset's own arrangement - existing tiles will very likely show the wrong picture, even though their position will now be correctly aligned. Continue?",
                "Switch Tileset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                // Revert the combo selection without triggering this handler again.
                TilesetCombo.SelectionChanged -= TilesetCombo_SelectionChanged;
                TilesetCombo.SelectedItem = _spriteDatabase.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.TilesetGroupName)?.Sprites.FirstOrDefault(s => s.Name == _map!.Tileset.TilesetSpriteName);
                TilesetCombo.SelectionChanged += TilesetCombo_SelectionChanged;
                return;
            }
        }

        _tileset = new TilesetDefinition
        {
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            TilesetSpriteName = sprite.Name,
            ExtraSheetNames = _map?.Tileset.ExtraSheetNames ?? new List<string>() // the map's other sheets stay
        };
        _sheetCache.Clear();
        _sheetCache[sprite.Name] = (bitmap, columns, rows);
        _paletteSheetName = sprite.Name;
        _paletteSlot = 0;
        _paletteColumns = columns;
        _paletteRows = rows;

        DrawPalette();
        StatusText.Text = $"Tileset: {sprite.Name} ({sprite.Frames.Count} tiles, {columns}x{rows} grid)";

        // The real fix for Issue #2/#3: a tileset switch changes TileWidth/
        // TileHeight, and without rebuilding the map canvas, its grid lines
        // and MapCanvas.Width/Height stay at the PREVIOUS tileset's
        // dimensions while newly-painted tiles use the new ones - that
        // mismatch is exactly what produced tiles rendering outside the
        // visible grid bounds. Also keep the map's own saved Tileset field
        // in sync, so a Save after switching doesn't persist a stale
        // reference that points at the wrong tileset on the next Load.
        if (_map is not null)
        {
            _map.Tileset = _tileset;
            BuildMapCanvas();
        }
    }

    /// <summary>
    /// Builds one working bitmap from a Tileset-group sprite's Frames, laid
    /// out row-major in a roughly-square grid - this is what lets the
    /// existing bitmap-slicing renderer (DrawPalette, PaintCellVisual,
    /// CroppedBitmap) keep working completely unchanged; only WHERE the
    /// source bitmap comes from has changed, not the 70-odd places that
    /// consume it. Tile size comes from the first frame's real image
    /// dimensions, not a manually-typed guess that could mismatch it.
    /// </summary>
    private (BitmapSource? Bitmap, int TileWidth, int TileHeight, int Columns, int Rows) BuildCompositeTilesetBitmap(SpriteEntry sprite)
    {
        var images = new List<BitmapImage>();
        foreach (var frame in sprite.Frames)
        {
            if (!File.Exists(frame.FilePath)) return (null, 0, 0, 0, 0);
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(frame.FilePath, UriKind.Absolute);
                bmp.EndInit();
                images.Add(bmp);
            }
            catch
            {
                return (null, 0, 0, 0, 0);
            }
        }

        int tileWidth = images[0].PixelWidth;
        int tileHeight = images[0].PixelHeight;
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(images.Count)));
        int rows = (int)Math.Ceiling(images.Count / (double)columns);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (int i = 0; i < images.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                dc.DrawImage(images[i], new Rect(col * tileWidth, row * tileHeight, tileWidth, tileHeight));
            }
        }

        var target = new RenderTargetBitmap(columns * tileWidth, rows * tileHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return (target, tileWidth, tileHeight, columns, rows);
    }

    private void DrawPalette()
    {
        if (_tilesetBitmap is null || _tileset is null)
            return;

        PaletteCanvas.Children.Clear();
        _paletteHighlight = null;
        _selectedTileIndex = -1;

        var image = new Image
        {
            Source = _tilesetBitmap,
            Width = _tilesetBitmap.PixelWidth,
            Height = _tilesetBitmap.PixelHeight
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, 0);
        Canvas.SetTop(image, 0);
        PaletteCanvas.Children.Add(image);

        // Grid lines over the palette so individual tiles are easy to see.
        for (int col = 0; col <= _paletteColumns; col++)
        {
            var line = new Line
            {
                X1 = col * _tileset.TileWidth,
                Y1 = 0,
                X2 = col * _tileset.TileWidth,
                Y2 = _paletteRows * _tileset.TileHeight,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                Opacity = 0.4
            };
            PaletteCanvas.Children.Add(line);
        }
        for (int row = 0; row <= _paletteRows; row++)
        {
            var line = new Line
            {
                X1 = 0,
                Y1 = row * _tileset.TileHeight,
                X2 = _paletteColumns * _tileset.TileWidth,
                Y2 = row * _tileset.TileHeight,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                Opacity = 0.4
            };
            PaletteCanvas.Children.Add(line);
        }

        PaletteCanvas.Width = _paletteColumns * _tileset.TileWidth;
        PaletteCanvas.Height = _paletteRows * _tileset.TileHeight;
        // -= before += so repeated DrawPalette() calls (every Duplicate/
        // Delete/tileset switch calls this) never accumulate a second,
        // third, etc. copy of the same handler - that would have caused
        // double/triple-firing clicks the more tiles got edited in one
        // session, easy to miss until it was tested heavily.
        PaletteCanvas.MouseLeftButtonDown -= PaletteCanvas_MouseLeftButtonDown;
        PaletteCanvas.MouseLeftButtonDown += PaletteCanvas_MouseLeftButtonDown;
        PaletteCanvas.MouseRightButtonDown -= PaletteCanvas_MouseRightButtonDown;
        PaletteCanvas.MouseRightButtonDown += PaletteCanvas_MouseRightButtonDown;

        // Live animation for any tile with a linked animation sprite -
        // drawn on top of that grid cell's static image, same idea as
        // PaintCellVisual on the map canvas. Stop the previous batch first;
        // DrawPalette() rebuilds its whole visual tree from scratch every
        // call, and a stale SpriteAnimator's DispatcherTimer keeps running
        // forever otherwise, same leak risk fixed on the map canvas side.
        foreach (var animator in _paletteTileAnimators) animator.Stop();
        _paletteTileAnimators.Clear();

        var tilesetSpriteForAnimation = GetCurrentTilesetSprite();
        if (tilesetSpriteForAnimation is not null)
        {
            for (int i = 0; i < tilesetSpriteForAnimation.Frames.Count; i++)
            {
                var animationSprite = GetLinkedAnimationSprite(i);
                if (animationSprite is null || animationSprite.Frames.Count == 0) continue;

                int col = i % _paletteColumns;
                int row = i / _paletteColumns;
                var animatedImage = new Image
                {
                    Width = _tileset.TileWidth,
                    Height = _tileset.TileHeight,
                    IsHitTestVisible = false
                };
                RenderOptions.SetBitmapScalingMode(animatedImage, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(animatedImage, col * _tileset.TileWidth);
                Canvas.SetTop(animatedImage, row * _tileset.TileHeight);
                PaletteCanvas.Children.Add(animatedImage);
                _paletteTileAnimators.Add(new SpriteAnimator(animationSprite, animatedImage, _resourceDatabase));
            }
        }

        // Small bottom-right glyph on any tile with a Properties label set,
        // so what's already been assigned is visible at a glance without
        // needing to right-click and check every tile individually.
        var tilesetSprite = GetCurrentTilesetSprite();
        if (tilesetSprite is not null)
        {
            for (int i = 0; i < tilesetSprite.Frames.Count; i++)
            {
                var props = _tilePropertiesDatabase.GetOrCreate(tilesetSprite.Name, i);
                string? glyph = GetTileGlyph(props);
                if (glyph is null) continue;

                int col = i % _paletteColumns;
                int row = i / _paletteColumns;
                var glyphText = new TextBlock
                {
                    Text = glyph,
                    FontSize = 11,
                    IsHitTestVisible = false
                };
                glyphText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(glyphText, (col + 1) * _tileset.TileWidth - glyphText.DesiredSize.Width - 1);
                Canvas.SetTop(glyphText, (row + 1) * _tileset.TileHeight - glyphText.DesiredSize.Height - 1);
                PaletteCanvas.Children.Add(glyphText);
            }
        }
    }

    private void PaletteCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_tileset is null) return;

        Point p = e.GetPosition(PaletteCanvas);
        int col = (int)(p.X / _tileset.TileWidth);
        int row = (int)(p.Y / _tileset.TileHeight);

        if (col < 0 || col >= _paletteColumns || row < 0 || row >= _paletteRows)
            return;

        int clickedIndex = row * _paletteColumns + col;
        var sprite = GetCurrentTilesetSprite();
        if (sprite is null || clickedIndex >= sprite.Frames.Count) return; // grid cell past the tileset's real tile count

        // Double-click a tile: same destination as picking Animation from
        // the right-click menu - "opens the spritesheet as well if I want
        // to" per spec, as a faster shortcut to the same place.
        if (e.ClickCount >= 2)
        {
            var props = _tilePropertiesDatabase.GetOrCreate(sprite.Name, clickedIndex);
            OpenTileAnimation(sprite, clickedIndex, props);
            return;
        }

        _selectedTileIndex = clickedIndex;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_multiSelectedTileIndices.Remove(clickedIndex))
                _multiSelectedTileIndices.Add(clickedIndex);
        }
        else
        {
            _multiSelectedTileIndices.Clear();
            _multiSelectedTileIndices.Add(clickedIndex);
        }

        if (_paletteHighlight is null)
        {
            _paletteHighlight = new Rectangle
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            PaletteCanvas.Children.Add(_paletteHighlight);
        }

        _paletteHighlight.Width = _tileset.TileWidth;
        _paletteHighlight.Height = _tileset.TileHeight;
        Canvas.SetLeft(_paletteHighlight, col * _tileset.TileWidth);
        Canvas.SetTop(_paletteHighlight, row * _tileset.TileHeight);

        RedrawMultiSelectHighlights();

        StatusText.Text = _multiSelectedTileIndices.Count > 1
            ? $"{_multiSelectedTileIndices.Count} tiles selected (Ctrl-click to add/remove more; painting still uses tile #{_selectedTileIndex})."
            : $"Selected tile #{_selectedTileIndex} (col {col}, row {row}).";
    }

    /// <summary>Draws a thin cyan outline on every Ctrl-selected tile besides the primary yellow one, so a multi-selection is visually obvious without needing a status-bar count to confirm it.</summary>
    private void RedrawMultiSelectHighlights()
    {
        foreach (var rect in _multiSelectHighlights) PaletteCanvas.Children.Remove(rect);
        _multiSelectHighlights.Clear();
        if (_tileset is null) return;

        foreach (int index in _multiSelectedTileIndices)
        {
            if (index == _selectedTileIndex) continue; // primary tile already has the yellow highlight
            int col = index % _paletteColumns;
            int row = index / _paletteColumns;
            var rect = new Rectangle
            {
                Width = _tileset.TileWidth,
                Height = _tileset.TileHeight,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(rect, col * _tileset.TileWidth);
            Canvas.SetTop(rect, row * _tileset.TileHeight);
            PaletteCanvas.Children.Add(rect);
            _multiSelectHighlights.Add(rect);
        }
    }

    private void PaletteCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_tileset is null) return;

        Point p = e.GetPosition(PaletteCanvas);
        int col = (int)(p.X / _tileset.TileWidth);
        int row = (int)(p.Y / _tileset.TileHeight);
        if (col < 0 || col >= _paletteColumns || row < 0 || row >= _paletteRows) return;

        int tileIndex = row * _paletteColumns + col;
        var sprite = GetCurrentTilesetSprite();
        if (sprite is null || tileIndex >= sprite.Frames.Count) return; // clicked a grid cell past the tileset's real tile count

        // Bulk Duplicate/Delete apply to the whole multi-selection only when
        // the right-clicked tile is actually part of it - right-clicking a
        // tile outside the current selection means "just this one," per
        // how every selection-based tool (file explorers included) behaves.
        var bulkIndices = _multiSelectedTileIndices.Contains(tileIndex) && _multiSelectedTileIndices.Count > 1
            ? _multiSelectedTileIndices.OrderBy(i => i).ToList()
            : new List<int> { tileIndex };

        ShowTileContextMenu(sprite, tileIndex, bulkIndices);
    }

    private SpriteEntry? GetCurrentTilesetSprite()
    {
        if (_tileset is null) return null;
        var group = _spriteDatabase.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.TilesetGroupName);
        // The sheet shown in the palette (any of the map's sheets).
        return group?.Sprites.FirstOrDefault(s => s.Name == (_paletteSheetName.Length > 0 ? _paletteSheetName : _tileset.TilesetSpriteName));
    }

    /// <summary>The right-click tile menu from the design document: Edit Tile Image, Rename, Duplicate, Delete, a Properties quick-preset submenu, Custom... (the full TilePropertiesWindow), and Animation (new) - links this one tile cell to its own dedicated animation sprite.</summary>
    private void ShowTileContextMenu(SpriteEntry tilesetSprite, int tileIndex, List<int> bulkIndices)
    {
        var props = _tilePropertiesDatabase.GetOrCreate(tilesetSprite.Name, tileIndex);
        var menu = new ContextMenu();
        bool isBulk = bulkIndices.Count > 1;

        var animationItem = new MenuItem
        {
            Header = string.IsNullOrEmpty(props.AnimationSpriteName) ? "Animation..." : $"Animation... (linked: {props.AnimationSpriteName})",
            FontWeight = FontWeights.Bold
        };
        animationItem.Click += (_, _) => OpenTileAnimation(tilesetSprite, tileIndex, props);
        menu.Items.Add(animationItem);

        var renameItem = new MenuItem { Header = "Rename..." };
        renameItem.Click += (_, _) => RenameTile(props);
        menu.Items.Add(renameItem);

        var duplicateItem = new MenuItem { Header = isBulk ? $"Duplicate ({bulkIndices.Count} tiles)" : "Duplicate" };
        duplicateItem.Click += (_, _) => DuplicateTiles(tilesetSprite, bulkIndices);
        menu.Items.Add(duplicateItem);

        var deleteItem = new MenuItem
        {
            Header = isBulk ? $"Delete ({bulkIndices.Count} tiles)" : "Delete",
            IsEnabled = tilesetSprite.Frames.Count > bulkIndices.Count // can't delete every remaining tile - at least one must survive
        };
        deleteItem.Click += (_, _) => DeleteTiles(tilesetSprite, bulkIndices);
        menu.Items.Add(deleteItem);

        menu.Items.Add(new Separator());

        var propertiesMenu = new MenuItem { Header = "Properties" };
        foreach (var (label, walkable) in new (string Label, bool Walkable)[]
        {
            ("Walkable", true), ("Blocks Movement", false), ("Water", false),
            ("Swamp", true), ("Sand", true), ("Road", true),
            ("Grass", true), ("Dirt", true), ("Snow", true)
        })
        {
            var presetItem = new MenuItem { Header = label, IsCheckable = true, IsChecked = props.TerrainLabel == label };
            presetItem.Click += (_, _) =>
            {
                props.TerrainLabel = label;
                props.Walkable = walkable;
                StatusText.Text = $"Tile #{tileIndex}: set to \"{label}\" ({(walkable ? "walkable" : "blocked")}).";
            };
            propertiesMenu.Items.Add(presetItem);
        }

        // Custom effects defined via the Custom... editor (either of its two
        // tabs) show up here automatically, marked "(C)" so they're never
        // confused with the nine hardcoded presets above - this menu is
        // rebuilt fresh every time it opens, reading CustomEffects live, so
        // a brand-new custom effect appears the very next time you
        // right-click, with no separate "refresh" step needed.
        if (props.CustomEffects.Count > 0)
        {
            propertiesMenu.Items.Add(new Separator());
            foreach (var effect in props.CustomEffects.ToList())
            {
                var customItem = new MenuItem { Header = $"(C) {effect.Name}", IsCheckable = true, IsChecked = props.TerrainLabel == effect.Name };

                var applyItem = new MenuItem { Header = "Apply to this tile" };
                applyItem.Click += (_, _) =>
                {
                    props.TerrainLabel = effect.Name;
                    StatusText.Text = $"Tile #{tileIndex}: set to custom effect \"{effect.Name}\".";
                };
                customItem.Items.Add(applyItem);

                var editItem = new MenuItem { Header = "Edit..." };
                editItem.Click += (_, _) =>
                {
                    var dialog = new TilePropertiesWindow(props, _objectDatabase.Objects, _resourceDatabase, _variableDatabase, _bookDatabase, _objectDatabase, _spriteDatabase, _statDatabase, _containerDatabase) { Owner = this };
                    dialog.ShowDialog();
                };
                customItem.Items.Add(editItem);

                var deleteEffectItem = new MenuItem { Header = "Delete" };
                deleteEffectItem.Click += (_, _) =>
                {
                    props.CustomEffects.Remove(effect);
                    if (props.TerrainLabel == effect.Name) props.TerrainLabel = string.Empty;
                    StatusText.Text = $"Deleted custom effect \"{effect.Name}\".";
                };
                customItem.Items.Add(deleteEffectItem);

                propertiesMenu.Items.Add(customItem);
            }
        }
        menu.Items.Add(propertiesMenu);

        var customEditorItem = new MenuItem { Header = "Custom..." };
        customEditorItem.Click += (_, _) =>
        {
            var dialog = new TilePropertiesWindow(props, _objectDatabase.Objects, _resourceDatabase, _variableDatabase, _bookDatabase, _objectDatabase, _spriteDatabase, _statDatabase, _containerDatabase) { Owner = this };
            dialog.ShowDialog();
        };
        menu.Items.Add(customEditorItem);

        menu.PlacementTarget = PaletteCanvas;
        menu.IsOpen = true;
    }

    /// <summary>Maps a tile's current Properties label to a short glyph for the palette overlay. Custom effect names (anything not one of the nine hardcoded presets) fall back to "(C)" - same marker the right-click menu uses for them, so the two stay visually consistent.</summary>
    private static string? GetTileGlyph(TileTypeProperties props)
    {
        if (string.IsNullOrEmpty(props.TerrainLabel)) return null;
        return props.TerrainLabel switch
        {
            "Walkable" => null, // the default state - not worth cluttering every ordinary tile with an icon
            "Blocks Movement" => "\U0001F6AB",
            "Water" => "\U0001F30A",
            "Swamp" => "\U0001F40A",
            "Sand" => "\U0001F3DC",
            "Road" => "\U0001F6E4",
            "Grass" => "\U0001F33F",
            "Dirt" => "\U0001F7EB",
            "Snow" => "\u2744",
            _ => "(C)"
        };
    }

    private void RenameTile(TileTypeProperties props)
    {
        var name = SimpleInputWindow.Show(this, "Tile name:", "Rename Tile", props.TerrainLabel);
        if (name is null) return;
        props.TerrainLabel = name.Trim();
        StatusText.Text = $"Tile #{props.TileIndex} renamed to \"{props.TerrainLabel}\".";
    }

    private void DuplicateTiles(SpriteEntry tilesetSprite, List<int> tileIndices)
    {
        int firstNewIndex = tilesetSprite.Frames.Count;
        foreach (int index in tileIndices)
        {
            var original = tilesetSprite.Frames[index];
            tilesetSprite.Frames.Add(new SpriteFrame { FilePath = original.FilePath, DurationMs = original.DurationMs });
        }
        RecompositeCurrentTileset();
        StatusText.Text = tileIndices.Count == 1
            ? $"Duplicated tile #{tileIndices[0]} as new tile #{firstNewIndex}."
            : $"Duplicated {tileIndices.Count} tiles as new tiles #{firstNewIndex}-#{tilesetSprite.Frames.Count - 1}.";
    }

    private void DeleteTiles(SpriteEntry tilesetSprite, List<int> tileIndices)
    {
        string message = tileIndices.Count == 1
            ? $"Delete tile #{tileIndices[0]}? Every tile after it shifts down by one index - any map already painted with tile #{tileIndices[0]} or higher from this tileset, or any Properties set on those tiles, may end up wrong afterward. This cannot be undone."
            : $"Delete these {tileIndices.Count} tiles? Every remaining tile shifts index to fill the gaps - any map already painted with an affected tile from this tileset, or any Properties set on those tiles, may end up wrong afterward. This cannot be undone.";
        var result = MessageBox.Show(message, "Delete Tile" + (tileIndices.Count > 1 ? "s" : ""), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // Highest index first, so removing one doesn't shift the meaning of
        // an index still waiting to be removed later in the same pass.
        foreach (int index in tileIndices.OrderByDescending(i => i))
        {
            tilesetSprite.Frames.RemoveAt(index);
            _tilePropertiesDatabase.Remove(tilesetSprite.Name, index);
        }
        _multiSelectedTileIndices.Clear();
        RecompositeCurrentTileset();
        StatusText.Text = tileIndices.Count == 1
            ? $"Deleted tile #{tileIndices[0]} from \"{tilesetSprite.Name}\"."
            : $"Deleted {tileIndices.Count} tiles from \"{tilesetSprite.Name}\".";
    }

    private void RecompositeCurrentTileset()
    {
        var sprite = GetCurrentTilesetSprite();
        if (sprite is null || sprite.Frames.Count == 0) return;
        var (bitmap, tileWidth, tileHeight, columns, rows) = BuildCompositeTilesetBitmap(sprite);
        if (bitmap is null || _tileset is null) return;
        _sheetCache[sprite.Name] = (bitmap, columns, rows); // its edited tiles, for the map too
        _tilesetBitmap = bitmap;
        _tileset.TileWidth = tileWidth;
        _tileset.TileHeight = tileHeight;
        _paletteColumns = columns;
        _paletteRows = rows;
        DrawPalette();
    }

    /// <summary>
    /// This tile's own dedicated animation sprite lives in a plain,
    /// ordinary group ("TileAnimations"), NOT SpriteDatabase.
    /// TilesetGroupName - it's a real animation sequence for one tile
    /// (ocean waves, grass in the wind), not a tilesheet of distinct
    /// tiles, so it gets the sound UI and normal Frame-means-animation
    /// treatment every other sprite gets. Auto-created the first time,
    /// seeded with the tile's current image as a starting frame rather
    /// than opening blank.
    /// </summary>
    private void OpenTileAnimation(SpriteEntry tilesetSprite, int tileIndex, TileTypeProperties props)
    {
        var animGroup = _spriteDatabase.GetOrCreateGroup("TileAnimations");

        SpriteEntry? animSprite = null;
        if (!string.IsNullOrEmpty(props.AnimationSpriteName))
            animSprite = animGroup.Sprites.FirstOrDefault(s => s.Name == props.AnimationSpriteName);

        if (animSprite is null)
        {
            string baseName = $"{tilesetSprite.Name}_Tile{tileIndex}";
            string name = baseName;
            int suffix = 1;
            while (animGroup.Sprites.Any(s => s.Name == name)) name = $"{baseName}_{suffix++}";

            string seedImagePath = tilesetSprite.Frames[tileIndex].FilePath;
            animSprite = new SpriteEntry { Name = name, FilePath = seedImagePath };
            animSprite.Frames.Add(new SpriteFrame { FilePath = seedImagePath });
            animGroup.Sprites.Add(animSprite);
            props.AnimationSpriteName = name;
        }

        var editor = new SpriteFrameEditorWindow(animSprite, _resourceDatabase) { Owner = this };
        editor.ShowDialog();
    }

    // ---------- Objects palette ----------

    // ---------- Menu: File / Edit ----------

    /// <summary>Flips every sprite's display flag so the Sprite Editor shows "Integrated Into System" instead of a real path - called after every point where shapes.sdbx is guaranteed to be in sync with _spriteDatabase (New, Open, and Save Project).</summary>
    private void MarkSpritesIntegrated()
    {
        foreach (var group in _spriteDatabase.Groups)
            foreach (var sprite in group.Sprites)
                sprite.IsProjectIntegrated = true;
    }

    /// <summary>Same idea as MarkSpritesIntegrated, for every sound/music entry - the Sound Editor's listing was still showing a real file path even after a save had genuinely embedded the bytes into audios.sdbx, which is exactly the gap MarkSpritesIntegrated already closed for sprites.</summary>
    private void MarkAudiosIntegrated()
    {
        foreach (var entry in _resourceDatabase.Entries)
            entry.IsProjectIntegrated = true;
    }

    private void NewProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewProjectWindow { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Result || dialog.ProjectFilePath is null) return;

        // A brand-new project's shapes.sdbx/objects.sdbx/audios.sdbx were just
        // written empty by NewProjectWindow - start this session with equally-
        // empty in-memory databases so they actually match what's on disk.
        // Factions/Variables/TileProperties too, now that they live in .msrp
        // alongside everything else instead of surviving only in memory.
        _spriteDatabase = new SpriteDatabase();
        _spriteDatabase.EnsureBuiltInGroups();
        RefreshTilesetCombo();
        _bookDatabase = new BookDatabase();
        _containerDatabase = new ContainerDatabase();
        _systemSettings = new SystemSettings();
        _resourceDatabase = new ResourceDatabase();
        _videoDatabase = new VideoDatabase();
        _factionDatabase = new FactionDatabase();
        _characterDatabase = new CharacterDatabase();
        _variableDatabase = new VariableDatabase();
        _statDatabase = StatDatabase.CreateDefault();
        _poseDatabase = PoseDatabase.CreateDefault();
        _tilePropertiesDatabase = new TilePropertiesDatabase();
        LoadHouses(null);

        SetActiveProject(dialog.ProjectFilePath, dialog.ProjectId, dialog.ProjectName, dialog.Platform, dialog.CreatedAtUtc);
    }

    private void OpenProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var pick = new OpenFileDialog { Filter = "Sandcastle Project (*.msrp)|*.msrp" };
        if (pick.ShowDialog() != true) return;
        LoadProjectFromPath(pick.FileName);
    }

    /// <summary>Loads a project from its .msrp (and the .sdbx files beside it) - used by File > Open and by a built standalone game. False if it couldn't be opened (the reason was already shown).</summary>
    private bool LoadProjectFromPath(string msrpPath)
    {
        try
        {
            var payload = ProjectFiles.LoadManifest(msrpPath);
            var manifest = payload.Manifest;
            string folder = DataFolderOverride ?? System.IO.Path.GetDirectoryName(msrpPath) ?? string.Empty; // a built game: its media files beside its .exe
            string shapePath = System.IO.Path.Combine(folder, "shapes.sdbx");
            string objectsPath = System.IO.Path.Combine(folder, "objects.sdbx");
            string audiosPath = System.IO.Path.Combine(folder, "audios.sdbx");

            // Extracted sprite/audio files land in a per-session temp folder,
            // not back into the project folder itself - between an Open and
            // the next Save, they're ordinary loose-file-backed data again,
            // exactly like before any project existed (per spec: nothing about
            // "Integrated Into System" is true until a Save has happened).
            string extractRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SandcastleRpgMaker", manifest.ProjectId.ToString("N"));
            var sprites = ProjectFiles.LoadShapeSdbx(shapePath, manifest.ProjectId, System.IO.Path.Combine(extractRoot, "sprites"));
            var books = ProjectFiles.LoadObjectsSdbx(objectsPath, manifest.ProjectId);
            var resources = ProjectFiles.LoadAudiosSdbx(audiosPath, manifest.ProjectId, System.IO.Path.Combine(extractRoot, "audio"));

            _spriteDatabase = sprites;
            _spriteDatabase.EnsureBuiltInGroups(); // older projects, saved before today, won't have this group in their file yet
            RefreshTilesetCombo();
            _bookDatabase = books;
            _resourceDatabase = resources;
            _factionDatabase = payload.Factions;
            _characterDatabase = payload.Characters;
            _containerDatabase = payload.Containers;
            _containerDatabase.SyncNextIdAfterLoad();
            _systemSettings = payload.Settings ?? new SystemSettings();
            _variableDatabase = payload.Variables;
            _statDatabase = payload.Stats;
            _poseDatabase = payload.Poses;
            _poseDatabase.EnsureBuiltInPoses(); // repairs a project saved by an earlier build whose PoseDatabase predates the full 11-pose set
            _tilePropertiesDatabase = payload.TileProperties;
            _currencyDatabase = payload.Currencies;
            _videoDatabase = payload.Videos ?? new VideoDatabase();
            VideoStore.Load(System.IO.Path.Combine(folder, "videos.sdbx"), manifest.ProjectId, _videoDatabase, System.IO.Path.Combine(extractRoot, "videos")); // none before videos existed
            _objectDatabase = payload.Objects;
            // A project saved before objects lived in it: its map may still point at an .objdb.json - bring those in once (the next Save keeps them in the project).
            if (_objectDatabase.Objects.Count == 0 && payload.Map is { ObjectDatabasePath.Length: > 0 } oldMap && File.Exists(oldMap.ObjectDatabasePath))
            {
                try { _objectDatabase = ObjectDatabaseSerializer.Load(oldMap.ObjectDatabasePath); } catch { /* unreadable: start with none */ }
            }
            LoadWorlds(payload.Map, payload.Worlds); // the starting world and the others (Maps tab)
            LoadHouses(payload.Houses); // the House Editor's houses (MainWindow.Houses.cs)
            _map = payload.Map;
            if (_map is not null) ShowMapInEditor();
            SetActiveProject(msrpPath, manifest.ProjectId, manifest.ProjectName, manifest.Platform, manifest.CreatedAtUtc);
            return true;
        }
        catch (Exception ex) when (IsGameHost)
        {
            // A built game that can't start: its error number and words (System Settings > Errors).
            GameDataException.ShowPlain(GameErrors.Classify(ex, ex is ProjectFileException ? GameErrors.DataDamaged : GameErrors.CantStart), ex.Message, msrpPath,
                System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Game"));
        }
        catch (ProjectFileException ex)
        {
            MessageBox.Show(ex.Message, "Open Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"This project could not be opened: {ex.Message}", "Open Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return false;
    }


    /// <summary>Shows the current map (_map) in the editor: its tileset, the palette, the map itself.</summary>
    private void ShowMapInEditor()
    {
        if (_map is null) return;
        _tileset = _map.Tileset;
        try
        {
            var tilesetGroup = _spriteDatabase.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.TilesetGroupName);
            var tilesetSprite = tilesetGroup?.Sprites.FirstOrDefault(s => s.Name == _tileset.TilesetSpriteName);
            if (tilesetSprite is not null)
            {
                var (bitmap, tileWidth, tileHeight, columns, rows) = BuildCompositeTilesetBitmap(tilesetSprite);
                if (bitmap is not null)
                {
                    _sheetCache.Clear();
                    _sheetCache[tilesetSprite.Name] = (bitmap, columns, rows);
                    _paletteSheetName = tilesetSprite.Name;
                    _paletteSlot = 0;
                    _tilesetBitmap = bitmap;
                    _tileset.TileWidth = tileWidth;
                    _tileset.TileHeight = tileHeight;
                    _paletteColumns = columns;
                    _paletteRows = rows;
                    DrawPalette();
                    TilesetCombo.SelectedItem = tilesetSprite;
                }
            }
        }
        catch
        {
            // Tileset sprite's frame images not found this session - map data itself still loaded fine, just can't paint a palette from it until they're available again.
        }

        BuildMapCanvas();
    }

    private void SaveProjectMenuItem_Click(object sender, RoutedEventArgs e) => SaveProject();

    /// <summary>Saves the open project (.msrp and its .sdbx files). False if there is none or saving failed (the reason was already shown).</summary>
    private bool SaveProject()
    {
        if (_currentProjectFilePath is null || _currentProjectId is null || _currentProjectFolder is null) return false;

        try
        {
            string shapePath = System.IO.Path.Combine(_currentProjectFolder, "shapes.sdbx");
            string objectsPath = System.IO.Path.Combine(_currentProjectFolder, "objects.sdbx");
            string audiosPath = System.IO.Path.Combine(_currentProjectFolder, "audios.sdbx");

            ProjectFiles.SaveShapeSdbx(shapePath, _currentProjectId.Value, _spriteDatabase);
            ProjectFiles.SaveObjectsSdbx(objectsPath, _currentProjectId.Value, _bookDatabase);
            ProjectFiles.SaveAudiosSdbx(audiosPath, _currentProjectId.Value, _resourceDatabase);
            VideoStore.Save(System.IO.Path.Combine(_currentProjectFolder, "videos.sdbx"), _currentProjectId.Value, _videoDatabase);

            // .msrp is re-saved every time too now, not written once and
            // left alone - it's a real, growing data file (factions,
            // variables, stats, tile properties), not a one-time manifest.
            var payload = new ManifestPayload
            {
                Manifest = new ProjectManifest
                {
                    ProjectId = _currentProjectId.Value,
                    ProjectName = _currentProjectName ?? string.Empty,
                    Platform = _currentProjectPlatform ?? "Windows",
                    CreatedAtUtc = _currentProjectCreatedAtUtc
                },
                Factions = _factionDatabase,
                Characters = _characterDatabase,
                Containers = _containerDatabase,
                Objects = _objectDatabase,
                Currencies = _currencyDatabase,
                Videos = _videoDatabase,
                Settings = _systemSettings,
                Variables = _variableDatabase,
                Stats = _statDatabase,
                Poses = _poseDatabase,
                TileProperties = _tilePropertiesDatabase,
                Map = _worlds.FirstOrDefault() ?? _map, // the starting world (Maps tab)
                Worlds = _worlds.Skip(1).ToList(),
                Houses = _houses // the House Editor's houses (MainWindow.Houses.cs)
            };
            ProjectFiles.SaveManifest(_currentProjectFilePath, payload);

            MarkSpritesIntegrated();
            MarkAudiosIntegrated();

            StatusText.Text = $"Project saved to {_currentProjectFolder}";
            return true;
        }
        catch (ProjectFileException ex)
        {
            MessageBox.Show(ex.Message, "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the project: {ex.Message}", "Save Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return false;
    }

    /// <summary>Common finish line for both New and Open: record which project is active and unlock everything except Save was previously locked behind.</summary>
    private void SetActiveProject(string projectFilePath, Guid projectId, string projectName, string platform, DateTime createdAtUtc)
    {
        _currentProjectFilePath = projectFilePath;
        _currentProjectId = projectId;
        _currentProjectFolder = System.IO.Path.GetDirectoryName(projectFilePath) ?? string.Empty;
        _currentProjectName = projectName;
        _currentProjectPlatform = platform;
        _currentProjectCreatedAtUtc = createdAtUtc;
        MarkSpritesIntegrated();
        MarkAudiosIntegrated();

        SaveProjectMenuItem.IsEnabled = true;
        EditMenu.IsEnabled = true;
        ModeMenu.IsEnabled = true;
        GameMenu.IsEnabled = true;
        RefreshCharactersPalette();
        RefreshObjectPalette();
        DrawMenu.IsEnabled = true;
        ToolsMenu.IsEnabled = true;
        MainToolBar.IsEnabled = true;
        MainContentGrid.IsEnabled = true;

        Title = $"Sandcastle RPG Maker - Map Editor - {System.IO.Path.GetFileNameWithoutExtension(projectFilePath)}";
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void UndoMenuItem_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Undo/redo history is planned for a later milestone.", "Not yet implemented",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void RedoMenuItem_Click(object sender, RoutedEventArgs e) => UndoMenuItem_Click(sender, e);

    // ---------- Menu: Mode ----------

    private ReferencedItemCategory _selectedItemCategory = ReferencedItemCategory.Book;

    /// <summary>Clears every top-level mode checkmark (Map/Event/Start Position/each Item type) before a new one is set - one shared place instead of four near-duplicate lists, so adding a future mode only means editing here once.</summary>
    private void ClearModeChecks()
    {
        MapModeMenuItem.IsChecked = false;
        EventModeMenuItem.IsChecked = false;
        StartPositionModeMenuItem.IsChecked = false;
        CharacterModeMenuItem.IsChecked = false;
        ObjectModeMenuItem.IsChecked = false;
        FreeModeMenuItem.IsChecked = false;
        ZoneModeMenuItem.IsChecked = false;
        if (_houseModeItem is not null) _houseModeItem.IsChecked = false; // MainWindow.Houses.cs
        if (_vehicleModeItem is not null) _vehicleModeItem.IsChecked = false;
        HideHouseGhost();
        _selectedZone = null;
        DrawZones(); // zones show only in Zones mode
        ClearFreeSelection();
        ItemTypeWeaponMenuItem.IsChecked = false;
        ItemTypeArmorMenuItem.IsChecked = false;
        ItemTypeItemMenuItem.IsChecked = false;
        ItemTypeBookMenuItem.IsChecked = false;
    }

    private void MapModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _editMode = EditMode.Map;
        ClearModeChecks();
        MapModeMenuItem.IsChecked = true;
        StatusText.Text = "Map mode: paint tiles / place objects.";
    }

    private void EventModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _editMode = EditMode.Event;
        ClearModeChecks();
        EventModeMenuItem.IsChecked = true;
        StatusText.Text = "Event mode: left-click empty space to place an event marker, double-click an existing marker to open it, right-click to delete it.";
    }

    private void StartPositionModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _editMode = EditMode.StartPosition;
        ClearModeChecks();
        StartPositionModeMenuItem.IsChecked = true;
        StatusText.Text = "Start Position mode: click a cell to place the player's start point - only one can exist, placing a new one moves it.";
    }

    /// <summary>
    /// Shared handler for all four Item submenu entries (Weapons/Armors/
    /// Items/Books) - picking any one both switches to Item mode AND sets
    /// which type the map-click picker defaults to, per spec ("a small
    /// dropdown box appears specifying the type... then I plot"). Weapons/
    /// Armors/Items have no real editor yet (same honest placeholder the
    /// Select Item picker itself already shows), so picking one of those
    /// still works mechanically but the picker's own tab will say so.
    /// Objects deliberately isn't offered here - it already has its own
    /// established placement system (the Objects palette tab); adding a
    /// second path to place the same thing would just be confusing.
    /// </summary>
    private void ItemTypeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _selectedItemCategory = sender switch
        {
            var s when s == ItemTypeWeaponMenuItem => ReferencedItemCategory.Weapon,
            var s when s == ItemTypeArmorMenuItem => ReferencedItemCategory.Armor,
            var s when s == ItemTypeItemMenuItem => ReferencedItemCategory.Item,
            var s when s == ItemTypeContainerMenuItem => ReferencedItemCategory.Container,
            _ => ReferencedItemCategory.Book
        };

        _editMode = EditMode.Item;
        ClearModeChecks();
        ((MenuItem)sender).IsChecked = true;
        StatusText.Text = $"Item mode ({_selectedItemCategory}s): click a cell to pick one from your database and place it there, right-click a placed item to remove it.";
    }

    // ---------- Menu: Draw ----------

    private void DrawShapeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        DrawSingleMenuItem.IsChecked = false;
        DrawLineMenuItem.IsChecked = false;
        DrawRectangleMenuItem.IsChecked = false;
        DrawCircleMenuItem.IsChecked = false;

        if (sender == DrawSingleMenuItem) { _drawShape = DrawShape.Single; DrawSingleMenuItem.IsChecked = true; }
        else if (sender == DrawLineMenuItem) { _drawShape = DrawShape.Line; DrawLineMenuItem.IsChecked = true; }
        else if (sender == DrawRectangleMenuItem) { _drawShape = DrawShape.Rectangle; DrawRectangleMenuItem.IsChecked = true; }
        else if (sender == DrawCircleMenuItem) { _drawShape = DrawShape.Circle; DrawCircleMenuItem.IsChecked = true; }

        StatusText.Text = $"Draw shape: {_drawShape}.";
    }

    // ---------- Menu: Tools ----------

    private void ResourcesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new ResourcesWindow { Owner = this };
        window.Database.Entries.AddRange(_resourceDatabase.Entries);
        window.Database.SyncNextIdAfterLoad();
        window.ShowDialog();
        _resourceDatabase = window.Database;
    }

    private void FactionEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new FactionEditorWindow(_factionDatabase) { Owner = this };
        window.ShowDialog();
        _factionDatabase = window.Database;
    }

    private void SpriteEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new SpriteEditorWindow(_spriteDatabase, _resourceDatabase) { Owner = this };
        window.ShowDialog();
        _spriteDatabase = window.Database;
        RefreshTilesetCombo(); // a tileset sprite may have just been added/edited/removed
    }

    private void BookEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new BookEditorWindow(_bookDatabase, _spriteDatabase, _factionDatabase, _resourceDatabase, _objectDatabase, _variableDatabase, _statDatabase, _containerDatabase) { Owner = this };
        window.ShowDialog();
        _bookDatabase = window.Database;
    }

    private void ContainerEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new ContainerEditorWindow(_containerDatabase, _spriteDatabase, _bookDatabase, _objectDatabase, _resourceDatabase, _variableDatabase, _statDatabase, _characterDatabase) { Owner = this };
        window.ShowDialog();
        _containerDatabase = window.Database;
    }

    private void SoundMixerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var mixer = new SoundMixerWindow(_resourceDatabase) { Owner = this };
        mixer.Show();
    }

    private void SoundEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new SoundListWindow(_resourceDatabase) { Owner = this };
        window.ShowDialog();
    }

    private void VideoLibraryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new VideoLibraryWindow(_videoDatabase) { Owner = this };
        window.ShowDialog();
    }

    private void VariableEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new VariableEditorWindow(_variableDatabase) { Owner = this };
        window.ShowDialog();
    }

    private void StatEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new StatEditorWindow(_statDatabase) { Owner = this };
        window.ShowDialog();
    }

    private void CharacterEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new CharacterEditorWindow(_characterDatabase, _statDatabase, _poseDatabase, _spriteDatabase, _bookDatabase, _objectDatabase, _resourceDatabase) { Owner = this };
        window.ShowDialog();
        // Names, poses and sprites may have changed - the palette and every placed character show them as they are now.
        RefreshCharactersPalette();
        PlaceAllCharacterVisualsIfMapShown();
    }

    private void PlaceAllCharacterVisualsIfMapShown()
    {
        if (_map is null || _tileset is null) return;
        foreach (var visual in _mapCharacterVisuals.Values) MapCanvas.Children.Remove(visual);
        if (_characterSelectionVisual is not null) MapCanvas.Children.Remove(_characterSelectionVisual);
        PlaceAllCharacterVisuals();
        UpdateCharacterSelectionVisual();
    }

    private void SystemSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new SystemSettingsWindow(_systemSettings, _spriteDatabase, _resourceDatabase) { Owner = this };
        window.ShowDialog();
    }

    private void TestEngineMenuItem_Click(object sender, RoutedEventArgs e) => LaunchGameTest();

    /// <summary>
    /// Game > Build...: makes a standalone game - the engine's program files
    /// copied to a folder of your choice, with this project's .msrp and .sdbx
    /// files merged into the game's .exe (GameBundle). The project is saved
    /// first, so the game is exactly what you see. It can never be built into
    /// the project's own folder or the editor's folder.
    /// </summary>
    private void BuildGameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProjectFilePath is null || _currentProjectFolder is null)
        {
            MessageBox.Show("Open or create a project first.", "Build", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_map is null || _tileset is null || _tilesetBitmap is null)
        {
            MessageBox.Show("The game needs a map with a tileset - create or load one first.", "Build", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var platform = new BuildPlatformWindow { Owner = this }; // which platform (BuildPlatformWindow.cs)
        if (platform.ShowDialog() != true || platform.Picked != BuildPlatform.Windows) return;
        if (MessageBox.Show("Building saves the project first, so the game is exactly what you have now. Continue?", "Build", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject()) return;

        string gameName = string.Concat((_currentProjectName is { Length: > 0 } n ? n : "Game").Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var pick = new SaveFileDialog
        {
            Title = "Where should the game be built?",
            Filter = "Game (*.exe)|*.exe",
            FileName = gameName + ".exe",
            OverwritePrompt = true
        };
        if (pick.ShowDialog(this) != true) return;

        string targetFolder = System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(pick.FileName)!);
        string projectFolder = System.IO.Path.GetFullPath(_currentProjectFolder);
        string engineFolder = System.IO.Path.GetFullPath(AppContext.BaseDirectory);
        if (IsSameOrInside(targetFolder, projectFolder))
        {
            MessageBox.Show("The game can't be built into the project's own folder (or a folder inside it) - that's where its source lives. Choose a different folder.", "Build", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (IsSameOrInside(targetFolder, engineFolder) || IsSameOrInside(engineFolder, targetFolder))
        {
            MessageBox.Show("The game can't be built into the editor's own folder. Choose a different folder.", "Build", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? engineExe = Environment.ProcessPath;
        if (engineExe is null || !System.IO.File.Exists(engineExe))
        {
            MessageBox.Show("Couldn't find the engine's own program file to build from.", "Build", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            GameBundle.Build(engineFolder, engineExe, pick.FileName, projectFolder, System.IO.Path.GetFileName(_currentProjectFilePath));
            StatusText.Text = $"Game built: {pick.FileName}";
            MessageBox.Show($"Your game was built:\n{pick.FileName}\n\nRun that .exe to play it. Its project is inside the .exe; its sprites, sounds, objects and videos are in the \"{GameBundle.DataFolderName(pick.FileName)}\" folder beside it - they only work with this game. " +
                "Keep everything in that folder together - the other files are the engine it runs on. The player's PC needs the free Microsoft .NET 8 Desktop Runtime.", "Build", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The game could not be built: {ex.Message}", "Build", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private static bool IsSameOrInside(string folder, string parent)
    {
        string a = System.IO.Path.TrimEndingDirectorySeparator(folder) + System.IO.Path.DirectorySeparatorChar;
        string b = System.IO.Path.TrimEndingDirectorySeparator(parent) + System.IO.Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A built game starting up: this window stays hidden; the embedded
    /// project is loaded exactly as File > Open would, and the game runs in
    /// its own window titled with the project's name. Closing it ends the
    /// program. False if the project couldn't be loaded or has no map.
    /// </summary>
    /// <summary>A built game: the folder its media files (.sdbx) are read from, beside its .exe (GameBundle).</summary>
    public string? DataFolderOverride { get; init; }

    /// <summary>This window only hosts a built game (never shown): its errors are the game's (System Settings > Errors).</summary>
    public bool IsGameHost { get; init; }

    public bool RunStandaloneGame(string msrpPath)
    {
        if (!LoadProjectFromPath(msrpPath)) return false;
        if (_map is null || _tileset is null || _tilesetBitmap is null)
        {
            GameDataException.ShowPlain(GameErrors.CantStart, "it has no map to play.", msrpPath, _currentProjectName ?? "Game");
            return false;
        }
        RunStandalone(NewGameWindow(atTitle: true, shipped: true));
        return true;
    }

    /// <summary>A fresh game from the project as it is (never a game already played): at its Title Screen or straight in; a built game's or Game Test's.</summary>
    private GameTestWindow NewGameWindow(bool atTitle, bool shipped) => NewGameWindow(_worlds.FirstOrDefault() ?? _map!, atTitle, shipped);

    private GameTestWindow NewGameWindow(MapData start, bool atTitle, bool shipped) =>
        new(start, Sheet(start.Tileset.TilesetSpriteName)?.Bitmap ?? PrimarySheet()?.Bitmap ?? _tilesetBitmap!, start.Tileset.TileWidth, start.Tileset.TileHeight,
            Sheet(start.Tileset.TilesetSpriteName)?.Columns ?? PrimarySheet()?.Columns ?? _paletteColumns,
            start.Tileset.TilesetSpriteName, _resourceDatabase, _spriteDatabase, _bookDatabase, _objectDatabase,
            _characterDatabase, _poseDatabase, _tilePropertiesDatabase, _variableDatabase, _statDatabase, _containerDatabase, _systemSettings, _factionDatabase)
        {
            StartAtTitle = atTitle,
            IsShippedGame = shipped,
            Worlds = _worlds,
            Title = shipped ? (_currentProjectName is { Length: > 0 } name ? name : "Game") : "Sandcastle RPG Maker - Game Test (Internal Engine, Alpha)",
            WindowStartupLocation = shipped ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

    /// <summary>What comes after a game window closes: a loaded save carrying on, a fresh game at the title (Return To Title) or straight in (New Game), or nothing.</summary>
    private GameTestWindow? NextGameWindow(GameTestWindow closed, bool shipped) =>
        closed.ReplacedBy ?? (closed.ReturnToTitleRequested ? NewGameWindow(atTitle: true, shipped) : closed.RestartRequested ? NewGameWindow(atTitle: false, shipped) : null);

    /// <summary>A built game's window: closing it ends the program - unless another game window carries on (a loaded save, Return To Title, New Game).</summary>
    private void RunStandalone(GameTestWindow game)
    {
        var display = _systemSettings.Display;
        if (display.Mode == GameScreenMode.FullScreen)
        {
            game.WindowStyle = WindowStyle.None; // no frame or title bar: the whole screen
            game.ResizeMode = ResizeMode.NoResize;
            game.WindowState = WindowState.Maximized;
        }
        else
        {
            int scale = Math.Clamp(display.WindowScale, 2, 6);
            game.SizeToContent = SizeToContent.Manual;
            game.Width = 320 * scale + 16;
            game.Height = 200 * scale + 39;
        }
        game.Closed += (_, _) =>
        {
            if (NextGameWindow(game, shipped: true) is { } next) RunStandalone(next);
            else Application.Current.Shutdown();
        };
        Application.Current.MainWindow = game;
        game.Show();
    }

    private void LaunchGameTest(bool atTitle = false)
    {
        if (_map is null || _tileset is null)
        {
            MessageBox.Show("Create or load a map first.", "Test the Engine", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_tilesetBitmap is null)
        {
            MessageBox.Show("Select a tileset first.", "Test the Engine", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // A loaded save carries on in a new window built from it (GameTestWindow.SaveLoad.cs); Return To Title and New Game start a fresh one.
        for (GameTestWindow? game = NewGameWindow(atTitle || _systemSettings.Title.InGameTest, shipped: false); game is not null; game = NextGameWindow(game, shipped: false))
        {
            game.Owner = this;
            game.ShowDialog();
        }
    }

    private void TestTitleScreenMenuItem_Click(object sender, RoutedEventArgs e) => LaunchGameTest(atTitle: true);

    private void TitleScreenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var window = new SystemSettingsWindow(_systemSettings, _spriteDatabase, _resourceDatabase, startTab: "Title Screen") { Owner = this };
        window.ShowDialog();
    }

    private void SaveTilePropertiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Tile Properties JSON (*.tileprops.json)|*.tileprops.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        TilePropertiesSerializer.Save(_tilePropertiesDatabase, dialog.FileName);
        _tilePropertiesPath = dialog.FileName;
        StatusText.Text = $"Saved tile properties to {dialog.FileName}";
    }

    private void LoadTilePropertiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Tile Properties JSON (*.tileprops.json)|*.tileprops.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        _tilePropertiesDatabase = TilePropertiesSerializer.Load(dialog.FileName);
        _tilePropertiesPath = dialog.FileName;
        StatusText.Text = $"Loaded tile properties from {dialog.FileName}";
    }

    // ---------- Line / rectangle / circle cell math ----------

    private static IEnumerable<(int x, int y)> CellsForLine(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        int x = x0, y = y0;
        while (true)
        {
            yield return (x, y);
            if (x == x1 && y == y1) yield break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
    }

    private static IEnumerable<(int x, int y)> CellsForRectangle(int x0, int y0, int x1, int y1)
    {
        int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
        int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                yield return (x, y);
    }

    private static IEnumerable<(int x, int y)> CellsForCircle(int cx, int cy, int edgeX, int edgeY)
    {
        double radius = Math.Sqrt(Math.Pow(edgeX - cx, 2) + Math.Pow(edgeY - cy, 2));
        int r = Math.Max(1, (int)Math.Round(radius));

        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                double dist = Math.Sqrt(Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2));
                if (dist <= r + 0.5)
                    yield return (x, y);
            }
        }
    }

    // ---------- Map creation ----------


    private void BuildMapCanvas()
    {
        if (_map is null || _tileset is null)
            return;
        // Placed objects sharing a number would hide one another here: each gets its own.
        if (_map.EnsureUniqueInstanceIds() is int renumbered and > 0)
            Dispatcher.BeginInvoke(() => StatusText.Text = $"{renumbered} placed object{(renumbered == 1 ? "" : "s")} shared a number with another and {(renumbered == 1 ? "was" : "were")} hidden here - now shown. Save the map to keep it.");

        MapCanvas.Children.Clear();
        foreach (var animator in _mapCellAnimators.Values) animator.Stop();
        _mapCellAnimators.Clear();
        _mapCellVisuals.Clear();
        _mapObjectVisuals.Clear();
        _landingVisuals.Clear();
        _mapEventVisuals.Clear();
        _startPositionVisual = null;
        _hoverCursorVisual = null;
        _houseGhost = null; // gone with MapCanvas.Children.Clear() above
        _houseGhostOf = null;
        _placedHouseVisuals.Clear();
        _shapePreviewVisuals.Clear(); // the squares themselves are gone with MapCanvas.Children.Clear() above; this just drops the now-stale references
        _shapeStartCell = null;
        _mapItemVisuals.Clear();
        foreach (var animator in _itemHoverAnimators) animator.Stop();
        _itemHoverAnimators.Clear();

        double pixelWidth = _map.Width * _tileset.TileWidth;
        double pixelHeight = _map.Height * _tileset.TileHeight;
        MapCanvas.Width = pixelWidth;
        MapCanvas.Height = pixelHeight;

        // Grid lines.
        for (int col = 0; col <= _map.Width; col++)
        {
            MapCanvas.Children.Add(new Line
            {
                X1 = col * _tileset.TileWidth, Y1 = 0,
                X2 = col * _tileset.TileWidth, Y2 = pixelHeight,
                Stroke = Brushes.Gray, StrokeThickness = GridLineThickness, Opacity = 0.5, Tag = GridLineTag
            });
        }
        for (int row = 0; row <= _map.Height; row++)
        {
            MapCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = row * _tileset.TileHeight,
                X2 = pixelWidth, Y2 = row * _tileset.TileHeight,
                Stroke = Brushes.Gray, StrokeThickness = GridLineThickness, Opacity = 0.5, Tag = GridLineTag
            });
        }

        // The floors below the picked one, darkened (MainWindow.Floors.cs).
        DrawLowerFloorTiles();

        // Paint the picked floor's tiles (used after Load Map, and on changing floor).
        var ground = _map.LayerForFloor(_currentFloor);
        for (int y = 0; ground is not null && y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                int tileIndex = ground.GetTile(x, y, _map.Width);
                if (tileIndex >= 0)
                    PaintCellVisual(x, y, tileIndex);
            }
        }

        // Draw any objects already placed on this map (on top of the tile layer).
        foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0))
            PlaceObjectVisual(instance);

        // Draw any events already placed on this map (on top of everything else).
        foreach (var gameEvent in _map.Events)
            PlaceEventVisual(gameEvent);

        // Restore the Start Position marker, if this map already has one.
        if (_map.StartX >= 0 && _map.StartY >= 0 && _map.StartFloor == _currentFloor)
            PlaceStartPositionVisual(_map.StartX, _map.StartY);

        // Restore any books already placed on this map.
        foreach (var itemInstance in _map.Items)
            PlaceItemVisual(itemInstance);

        // Characters standing on this map, in their poses.
        PlaceAllCharacterVisuals();

        // Houses plotted on it, as their houses are now (MainWindow.Houses.cs).
        DrawPlacedHouses();

        // -= before += for the same reason as the palette's own handlers -
        // BuildMapCanvas() can genuinely run more than once in a session
        // (New Map, Load Map, Open Project), and Children.Clear() only
        // removes visual elements, not event subscriptions on MapCanvas
        // itself - without this, a second call would double-fire every
        // click.
        MapCanvas.MouseLeftButtonDown -= MapCanvas_MouseLeftButtonDown;
        MapCanvas.MouseLeftButtonDown += MapCanvas_MouseLeftButtonDown;
        MapCanvas.MouseLeftButtonUp -= MapCanvas_MouseLeftButtonUp;
        MapCanvas.MouseLeftButtonUp += MapCanvas_MouseLeftButtonUp;
        MapCanvas.MouseMove -= MapCanvas_MouseMove;
        MapCanvas.MouseMove += MapCanvas_MouseMove;
        MapCanvas.MouseLeave -= MapCanvas_MouseLeave;
        MapCanvas.MouseLeave += MapCanvas_MouseLeave;
        MapCanvas.MouseRightButtonDown -= MapCanvas_MouseRightButtonDown;
        MapCanvas.MouseRightButtonDown += MapCanvas_MouseRightButtonDown;

        // Ground-truth diagnostic for Issue #2/#3 investigation: exact real
        // numbers, not visual estimation. If the visible grid looks larger
        // than what this reports, that's the concrete proof of a real
        // mismatch rather than a rendering illusion.
        DrawZones(); // the canvas was rebuilt (a floor picked): the zones go back on top
        StatusText.Text = $"Map built: {_map.Width}x{_map.Height} cells, {_tileset.TileWidth}x{_tileset.TileHeight}px tiles -> canvas {pixelWidth}x{pixelHeight}px. Last valid cell: col {_map.Width - 1}, row {_map.Height - 1}.";
    }

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_map is null || _tileset is null) return;

        var (x, y) = CellFromPoint(e.GetPosition(MapCanvas));
        if (!IsInsideMap(x, y)) return;

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        // Ctrl + double-click on a container: what THIS one holds (MainWindow.Free.cs).
        if (ctrl && ContainerAtPoint(e.GetPosition(MapCanvas)) is { } clickedContainer)
        {
            // The first click of the double-click does nothing (it would otherwise put the picked thing on the chest); drag something onto a container to put it on top.
            if (e.ClickCount >= 2) EditPlacedContainerContents(clickedContainer);
            else StatusText.Text = "Ctrl + double-click a container to fill it. (To put something on top of it, drag it there.)";
            return;
        }

        if (_editMode == EditMode.Zone)
        {
            ZoneMode_MouseLeftButtonDown(x, y, e.ClickCount);
            return;
        }

        if (_editMode == EditMode.House)
        {
            HouseMode_Click(x, y); // MainWindow.Houses.cs
            return;
        }

        if (_editMode == EditMode.Free)
        {
            FreeMode_MouseLeftButtonDown(e.GetPosition(MapCanvas), ctrl);
            return;
        }

        if (_editMode == EditMode.Character)
        {
            CharacterMode_MouseLeftButtonDown(x, y);
            return;
        }

        if (_editMode == EditMode.Object)
        {
            ObjectMode_MouseLeftButtonDown(x, y);
            return;
        }

        if (_editMode == EditMode.Item)
        {
            if (_map.Items.Any(i => i.X == x && i.Y == y && FloorOf(i) == _currentFloor))
            {
                StatusText.Text = "There's already an item at this cell - right-click it to remove it first.";
                return;
            }

            var picked = PromptForItem(_selectedItemCategory);
            if (picked is null) return;

            var instance = new PlacedItemInstance { Category = picked.Category, ItemId = picked.ItemId, X = x, Y = y, Z = MapFloors.BaseLift(_currentFloor) };
            _map.Items.Add(instance);
            RedrawAfterSettle(instance); // comes to rest on what's under it (a tile, a table), as the game will start it
            StatusText.Text = $"Placed \"{picked.DisplayName}\" at ({x}, {y}).";
            return;
        }

        if (_editMode == EditMode.StartPosition)
        {
            _map.StartFloor = _currentFloor;
            PlaceStartPositionVisual(x, y);
            StatusText.Text = $"Start Position set to ({x}, {y}) on floor {_currentFloor}.";
            return;
        }

        if (_editMode == EditMode.Event)
        {
            if (ctrl)
            {
                // Pick up the event at this cell so it can be dropped elsewhere on mouse-up.
                var existing = _map.Events.FirstOrDefault(ev => ev.X == x && ev.Y == y && ev.Floor == _currentFloor);
                if (existing is null) return;

                _draggingEvent = existing;
                if (_mapEventVisuals.TryGetValue(existing.Id, out var visual))
                {
                    MapCanvas.Children.Remove(visual);
                    _mapEventVisuals.Remove(existing.Id);
                }
                return;
            }

            var eventAtCell = _map.Events.FirstOrDefault(ev => ev.X == x && ev.Y == y && ev.Floor == _currentFloor);
            if (eventAtCell is not null) _selectedMapEvent = eventAtCell; // click on an existing marker selects it for keyboard shortcuts, without opening it

            // Per spec: click places a marker, double-click opens it for
            // editing - these are two distinct actions, not one click that
            // does both. A double-click on empty space has nothing to open.
            if (e.ClickCount >= 2)
            {
                if (eventAtCell is not null) OpenOrCreateEvent(x, y);
                return;
            }

            if (eventAtCell is null)
            {
                int newId = _map.GetNextEventId();
                var newEvent = new GameEvent { Id = newId, Name = $"Event {newId}", X = x, Y = y, Floor = _currentFloor };
                _map.Events.Add(newEvent);
                PlaceEventVisual(newEvent);
                _selectedMapEvent = newEvent;
            }
            // else: single-click on an existing marker does nothing else - double-click is what opens it.
            return;
        }

        // --- Map mode ---
        // Tileset painting.
        if (_selectedTileIndex < 0)
        {
            StatusText.Text = "Select a tile from the palette first.";
            return;
        }

        if (_drawShape == DrawShape.Single)
        {
            CurrentFloorLayer.SetTile(x, y, _map.Width, PaintTileIndex);
            PaintCellVisual(x, y, PaintTileIndex);
            _isFreehandPainting = true; // per spec: click-and-drag paints continuously like a normal pencil tool, no modifier key needed
        }
        else
        {
            // Line/Rectangle/Circle: remember the start cell, stamp the shape on mouse-up.
            _shapeStartCell = (x, y);
        }
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_map is null || _tileset is null) return;

        // Hover cursor: always updates regardless of mode or painting state, so "where will my click land" is answered everywhere on the map, not just while painting tiles.
        var rawPoint = e.GetPosition(MapCanvas);
        var (hoverX, hoverY) = CellFromPoint(rawPoint);
        bool inBounds = IsInsideMap(hoverX, hoverY);
        _lastHoverCell = (hoverX, hoverY);
        if (_editMode == EditMode.House) { HouseMode_MouseMove(hoverX, hoverY); return; } // the house follows the mouse (MainWindow.Houses.cs)

        // Live diagnostic for Issue #2/#3: the exact raw pixel position AND the cell it resolves to, in or out of bounds - hover to the spot where the cursor disappears and this shows precisely what's being computed there.
        StatusText.Text = inBounds
            ? $"Hover: pixel ({rawPoint.X:F0}, {rawPoint.Y:F0}) -> cell (col {hoverX}, row {hoverY})"
            : $"Hover: pixel ({rawPoint.X:F0}, {rawPoint.Y:F0}) -> cell (col {hoverX}, row {hoverY}) - OUT OF BOUNDS (map is {_map.Width}x{_map.Height})";

        if (inBounds)
        {
            EnsureHoverCursorVisual();
            if (_hoverCursorVisual is not null)
            {
                Canvas.SetLeft(_hoverCursorVisual, hoverX * _tileset.TileWidth);
                Canvas.SetTop(_hoverCursorVisual, hoverY * _tileset.TileHeight);
                _hoverCursorVisual.Visibility = Visibility.Visible;
            }
        }
        else if (_hoverCursorVisual is not null)
        {
            _hoverCursorVisual.Visibility = Visibility.Collapsed;
        }

        // Live shape preview: only while a Line/Rectangle/Circle drag is actually in progress (left button still held).
        if (_shapeStartCell is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateShapePreview(start.x, start.y, hoverX, hoverY);
        }

        if (_editMode == EditMode.Object) { ObjectMode_MouseMove(hoverX, hoverY, e.LeftButton); return; }
        if (_editMode == EditMode.Free) { FreeMode_MouseMove(rawPoint, e.LeftButton); return; }
        if (_editMode == EditMode.Zone) { ZoneMode_MouseMove(hoverX, hoverY, e.LeftButton); return; }

        if (!_isFreehandPainting) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _isFreehandPainting = false;
            return;
        }
        if (_selectedTileIndex < 0) return;

        var (x, y) = CellFromPoint(e.GetPosition(MapCanvas));
        if (!IsInsideMap(x, y)) return;

        CurrentFloorLayer.SetTile(x, y, _map.Width, PaintTileIndex);
        PaintCellVisual(x, y, PaintTileIndex);
    }

    private void MapCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoverCursorVisual is not null) _hoverCursorVisual.Visibility = Visibility.Collapsed;
        if (_houseGhost is not null) _houseGhost.Visibility = Visibility.Collapsed;
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_map is null || _tileset is null) return;

        _isFreehandPainting = false;

        var (x, y) = CellFromPoint(e.GetPosition(MapCanvas));

        if (ZoneMode_MouseLeftButtonUp(x, y)) return;
        if (FreeMode_MouseLeftButtonUp(e.GetPosition(MapCanvas))) return;
        if (CharacterMode_MouseLeftButtonUp(x, y)) return;
        if (ObjectMode_MouseLeftButtonUp(x, y)) return;

        if (_draggingEvent is not null)
        {
            if (IsInsideMap(x, y))
            {
                _draggingEvent.X = x;
                _draggingEvent.Y = y;
            }
            PlaceEventVisual(_draggingEvent);
            _draggingEvent = null;
            return;
        }

        if (_shapeStartCell is { } start && IsInsideMap(x, y) && _selectedTileIndex >= 0 && PaletteTabs.SelectedIndex == 0)
        {
            IEnumerable<(int x, int y)> cells = _drawShape switch
            {
                DrawShape.Line => CellsForLine(start.x, start.y, x, y),
                DrawShape.Rectangle => CellsForRectangle(start.x, start.y, x, y),
                DrawShape.Circle => CellsForCircle(start.x, start.y, x, y),
                _ => Array.Empty<(int, int)>()
            };

            foreach (var (cx, cy) in cells)
            {
                if (!IsInsideMap(cx, cy)) continue;
                CurrentFloorLayer.SetTile(cx, cy, _map.Width, PaintTileIndex);
                PaintCellVisual(cx, cy, PaintTileIndex);
            }
        }
        _shapeStartCell = null;
        ClearShapePreview();
    }

    private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_map is null) return;

        var (x, y) = CellFromPoint(e.GetPosition(MapCanvas));
        if (!IsInsideMap(x, y)) return;

        if (_editMode == EditMode.House)
        {
            HouseMode_RightClick(x, y); // MainWindow.Houses.cs
            return;
        }

        if (_editMode == EditMode.Event)
        {
            var existing = _map.Events.FirstOrDefault(ev => ev.X == x && ev.Y == y && ev.Floor == _currentFloor);
            if (existing is not null) _selectedMapEvent = existing;
            ShowMapEventContextMenu(existing, x, y);
            return;
        }

        if (_editMode == EditMode.Zone)
        {
            ZoneMode_MouseRightButtonDown(x, y);
            return;
        }

        if (_editMode == EditMode.Free)
        {
            FreeMode_MouseRightButtonDown(e.GetPosition(MapCanvas));
            return;
        }

        if (_editMode == EditMode.Character)
        {
            CharacterMode_MouseRightButtonDown(x, y);
            return;
        }

        if (_editMode == EditMode.Object)
        {
            ObjectMode_MouseRightButtonDown(x, y, e.GetPosition(MapCanvas));
            return;
        }

        if (_editMode == EditMode.Item)
        {
            var itemInstance = _map.Items.FirstOrDefault(i => i.X == x && i.Y == y && FloorOf(i) == _currentFloor);
            if (itemInstance is null) return;

            _map.Items.Remove(itemInstance);
            if (_mapItemVisuals.Remove(itemInstance, out var visual))
                MapCanvas.Children.Remove(visual);
            StatusText.Text = $"Removed item at ({x}, {y}).";
            return;
        }

        {
            CurrentFloorLayer.SetTile(x, y, _map.Width, -1);

            if (_mapCellAnimators.TryGetValue((x, y), out var animator))
            {
                animator.Stop();
                _mapCellAnimators.Remove((x, y));
            }

            if (_mapCellVisuals.TryGetValue((x, y), out var tileImg))
            {
                MapCanvas.Children.Remove(tileImg);
                _mapCellVisuals.Remove((x, y));
            }
        }
    }

    /// <summary>
    /// Deep-clones event pages via a JSON round-trip rather than manual
    /// field-by-field copying - BookEventConditions has enough nested
    /// sub-condition types (Complex Condition groups especially) that
    /// hand-cloning risks quietly missing one and having a "copy" secretly
    /// share state with its original. The same JsonSerializer already used
    /// throughout this project handles it correctly in one line.
    /// </summary>
    private static List<BookEventPage> CloneEventPages(List<BookEventPage> source)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<List<BookEventPage>>(json) ?? new List<BookEventPage>();
    }

    private void ShowMapEventContextMenu(GameEvent? eventAtCell, int x, int y)
    {
        var menu = new ContextMenu();

        var duplicateItem = new MenuItem { Header = "Duplicate", IsEnabled = eventAtCell is not null };
        duplicateItem.Click += (_, _) => { if (eventAtCell is not null) DuplicateMapEvent(eventAtCell); };
        menu.Items.Add(duplicateItem);

        var cutItem = new MenuItem { Header = "Cut", IsEnabled = eventAtCell is not null };
        cutItem.Click += (_, _) => { if (eventAtCell is not null) CutMapEvent(eventAtCell); };
        menu.Items.Add(cutItem);

        var copyItem = new MenuItem { Header = "Copy", IsEnabled = eventAtCell is not null };
        copyItem.Click += (_, _) => { if (eventAtCell is not null) CopyMapEvent(eventAtCell); };
        menu.Items.Add(copyItem);

        var pasteItem = new MenuItem { Header = "Paste", IsEnabled = _copiedEventPages is not null && eventAtCell is null };
        pasteItem.Click += (_, _) => PasteMapEvent(x, y);
        menu.Items.Add(pasteItem);

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete", IsEnabled = eventAtCell is not null };
        deleteItem.Click += (_, _) => { if (eventAtCell is not null) DeleteMapEvent(eventAtCell); };
        menu.Items.Add(deleteItem);

        menu.PlacementTarget = MapCanvas;
        menu.IsOpen = true;
    }

    private void DuplicateMapEvent(GameEvent source)
    {
        if (_map is null) return;

        // Offset by one cell so the duplicate doesn't sit exactly on top of
        // the original, hiding it - falls back to stacking on the same
        // cell only if there's genuinely nowhere else to put it.
        int newX = source.X, newY = source.Y;
        if (IsInsideMap(source.X + 1, source.Y)) newX = source.X + 1;
        else if (IsInsideMap(source.X, source.Y + 1)) newY = source.Y + 1;

        int newId = _map.GetNextEventId();
        var duplicate = new GameEvent
        {
            Id = newId,
            Name = $"{source.Name} (Copy)",
            X = newX,
            Y = newY,
            Floor = source.Floor,
            EventPages = CloneEventPages(source.EventPages)
        };
        _map.Events.Add(duplicate);
        PlaceEventVisual(duplicate);
        _selectedMapEvent = duplicate;
        StatusText.Text = $"Duplicated \"{source.Name}\" to ({newX}, {newY}).";
    }

    private void CutMapEvent(GameEvent source)
    {
        _copiedEventPages = CloneEventPages(source.EventPages);
        string name = source.Name;
        DeleteMapEvent(source, announce: false);
        StatusText.Text = $"Cut \"{name}\" - right-click empty space (or Ctrl+V near a selected event) to paste it.";
    }

    /// <summary>Unlike Cut, the source event survives - lets the same event be pasted more than once without needing to Duplicate-then-edit each copy separately.</summary>
    private void CopyMapEvent(GameEvent source)
    {
        _copiedEventPages = CloneEventPages(source.EventPages);
        StatusText.Text = $"Copied \"{source.Name}\" - right-click empty space (or Ctrl+V near a selected event) to paste it, as many times as you like.";
    }

    private void PasteMapEvent(int x, int y)
    {
        if (_map is null || _copiedEventPages is null) return;
        if (_map.Events.Any(ev => ev.X == x && ev.Y == y && ev.Floor == _currentFloor))
        {
            MessageBox.Show("There's already an event at this cell.", "Paste Event", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int newId = _map.GetNextEventId();
        var pasted = new GameEvent
        {
            Id = newId,
            Name = $"Event {newId}",
            X = x,
            Y = y,
            Floor = _currentFloor,
            EventPages = CloneEventPages(_copiedEventPages) // clone again - pasting more than once must never have every copy share the same page objects
        };
        _map.Events.Add(pasted);
        PlaceEventVisual(pasted);
        _selectedMapEvent = pasted;
        StatusText.Text = $"Pasted event at ({x}, {y}).";
    }

    private void DeleteMapEvent(GameEvent target, bool announce = true)
    {
        if (_map is null) return;
        _map.Events.Remove(target);
        if (_mapEventVisuals.TryGetValue(target.Id, out var visual))
        {
            MapCanvas.Children.Remove(visual);
            _mapEventVisuals.Remove(target.Id);
        }
        if (_selectedMapEvent == target) _selectedMapEvent = null;
        if (announce) StatusText.Text = $"Deleted \"{target.Name}\".";
    }

    /// <summary>
    /// Ctrl+X/Delete act on whichever event a mouse click last selected -
    /// unambiguous, since there's exactly one. Ctrl+V has no click position
    /// to work from, so it pastes next to that same selected event (same
    /// offset rule as Duplicate) rather than guessing a location - pasting
    /// onto a specific empty cell still works precisely via the mouse menu.
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            LaunchGameTest();
            e.Handled = true;
            return;
        }

        if (_editMode == EditMode.Character && _map is not null)
        {
            if (CharacterMode_KeyDown(e)) e.Handled = true;
            return;
        }

        if (_editMode == EditMode.Object && _map is not null)
        {
            if (ObjectMode_KeyDown(e)) e.Handled = true;
            return;
        }

        if (_editMode == EditMode.Free && _map is not null)
        {
            if (FreeMode_KeyDown(e)) e.Handled = true;
            return;
        }

        if (_editMode != EditMode.Event || _map is null) return;

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl && e.Key == Key.X && _selectedMapEvent is not null)
        {
            CutMapEvent(_selectedMapEvent);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.C && _selectedMapEvent is not null)
        {
            CopyMapEvent(_selectedMapEvent);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.V && _copiedEventPages is not null && _selectedMapEvent is not null)
        {
            int x = _selectedMapEvent.X, y = _selectedMapEvent.Y;
            if (IsInsideMap(x + 1, y)) x++;
            else if (IsInsideMap(x, y + 1)) y++;
            PasteMapEvent(x, y);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedMapEvent is not null)
        {
            DeleteMapEvent(_selectedMapEvent);
            e.Handled = true;
        }
    }

    private (int x, int y) CellFromPoint(Point p)
    {
        if (_tileset is null) return (-1, -1);
        return ((int)(p.X / _tileset.TileWidth), (int)(p.Y / _tileset.TileHeight));
    }

    private bool IsInsideMap(int x, int y)
        => _map is not null && x >= 0 && x < _map.Width && y >= 0 && y < _map.Height;

    /// <summary>Looks up whether this tile index (in the currently-selected tileset) has a real, existing animation linked to it via Animation... in the right-click menu. Returns null for a plain static tile - the normal, unlinked case.</summary>
    private SpriteEntry? GetLinkedAnimationSprite(int tileIndex)
    {
        var tilesetSprite = GetCurrentTilesetSprite();
        if (tilesetSprite is null) return null;
        var props = _tilePropertiesDatabase.GetOrCreate(tilesetSprite.Name, tileIndex);
        if (string.IsNullOrEmpty(props.AnimationSpriteName)) return null;

        var animGroup = _spriteDatabase.Groups.FirstOrDefault(g => g.Name == "TileAnimations");
        return animGroup?.Sprites.FirstOrDefault(s => s.Name == props.AnimationSpriteName);
    }

    private void PaintCellVisual(int x, int y, int tileIndex)
    {
        if (_tilesetBitmap is null || _tileset is null) return;

        // Stop and clear this cell's old animator (if any) before doing
        // anything else - whether it's about to become a different
        // animated tile or a plain static one, a stale animator must never
        // keep running in the background after its cell changes.
        if (_mapCellAnimators.TryGetValue((x, y), out var oldAnimator))
        {
            oldAnimator.Stop();
            _mapCellAnimators.Remove((x, y));
        }

        if (!_mapCellVisuals.TryGetValue((x, y), out var image))
        {
            image = new Image
            {
                Width = _tileset.TileWidth,
                Height = _tileset.TileHeight,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image, x * _tileset.TileWidth);
            Canvas.SetTop(image, y * _tileset.TileHeight);
            MapCanvas.Children.Add(image);
            _mapCellVisuals[(x, y)] = image;
        }

        var animationSprite = LinkedAnimationOfMapTile(tileIndex); // its own sheet's tile animation
        if (animationSprite is not null && animationSprite.Frames.Count > 0)
        {
            _mapCellAnimators[(x, y)] = new SpriteAnimator(animationSprite, image, _resourceDatabase);
            return;
        }

        image.Source = TileSource(tileIndex); // from whichever of the map's tilesheets it's from
    }

    /// <summary>
    /// Same Ultima 7-style bottom-center anchoring GameTestWindow uses -
    /// shared here (not shared code, since the two windows don't share a
    /// base class, but identical logic) specifically so what you see
    /// while placing something in the Map Editor is what you'll actually
    /// see in Game Test, not a different, older "always fills one tile"
    /// rendering left behind in just one of the two places.
    /// </summary>
    private void PositionQuantizedVisual(UIElement visual, int cellX, int cellY, double quantizedWidth, double quantizedHeight)
    {
        if (_tileset is null) return;
        double left = cellX * _tileset.TileWidth + (_tileset.TileWidth - quantizedWidth) / 2.0;
        double top = (cellY + 1) * _tileset.TileHeight - quantizedHeight;
        Canvas.SetLeft(visual, left);
        Canvas.SetTop(visual, top);
    }

    private void OpenOrCreateEvent(int x, int y)
    {
        if (_map is null) return;

        var existing = _map.Events.FirstOrDefault(ev => ev.X == x && ev.Y == y && ev.Floor == _currentFloor);
        bool isNew = existing is null;

        var target = existing ?? new GameEvent
        {
            Id = _map.GetNextEventId(),
            Name = $"Event {_map.GetNextEventId()}",
            X = x,
            Y = y,
            Floor = _currentFloor
        };

        var dialog = new EventEditorWindow(target, _resourceDatabase, _variableDatabase, _bookDatabase, _objectDatabase, _spriteDatabase, _statDatabase, _containerDatabase) { Owner = this };
        bool? result = dialog.ShowDialog();

        if (isNew)
        {
            if (result == true)
            {
                _map.Events.Add(target);
                PlaceEventVisual(target);
            }
            // else: brand new event cancelled - nothing to add.
        }
        else if (result == true)
        {
            PlaceEventVisual(target); // refresh tooltip/label in case the name changed
        }
    }

    private void PlaceEventVisual(GameEvent gameEvent)
    {
        if (_tileset is null) return;

        if (_mapEventVisuals.TryGetValue(gameEvent.Id, out var old))
            MapCanvas.Children.Remove(old);
        if (gameEvent.Floor != _currentFloor) { _mapEventVisuals.Remove(gameEvent.Id); return; } // only the picked floor's events show

        // Pulsating green, PS4-light style: a continuous, looping brightness
        // fade on the fill color itself (not just opacity), so it genuinely
        // reads as "glowing" rather than just fading in and out.
        var dimGreen = Color.FromArgb(160, 20, 140, 20);
        var brightGreen = Color.FromArgb(220, 60, 255, 60);
        var fillBrush = new SolidColorBrush(dimGreen);
        var pulse = new ColorAnimation
        {
            From = dimGreen,
            To = brightGreen,
            Duration = TimeSpan.FromSeconds(0.9),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        fillBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);

        var marker = new Border
        {
            Width = _tileset.TileWidth,
            Height = _tileset.TileHeight,
            Background = fillBrush,
            BorderBrush = Brushes.LimeGreen,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            ToolTip = gameEvent.EventPages.Count > 0
                ? $"{gameEvent.Name} ({gameEvent.EventPages[0].Conditions.TriggerConditionBased}, {gameEvent.EventPages.Count} page(s))"
                : $"{gameEvent.Name} (no pages yet)",
            Child = new TextBlock
            {
                Text = "E",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(marker, gameEvent.X * _tileset.TileWidth);
        Canvas.SetTop(marker, gameEvent.Y * _tileset.TileHeight);
        Panel.SetZIndex(marker, MarkerZIndex); // Above tiles and objects.

        MapCanvas.Children.Add(marker);
        _mapEventVisuals[gameEvent.Id] = marker;
    }

    /// <summary>
    /// Same pulsing-glow visual language as the "E" event marker
    /// (PlaceEventVisual), purple instead of green and labeled "S" - per
    /// spec, only one can ever exist, so this always removes whatever
    /// marker was there before rather than adding a second one.
    /// </summary>
    /// <summary>
    /// Pulsating white outline square that follows the mouse over the map,
    /// showing exactly which cell would be affected by a click right now -
    /// per spec, deliberately hollow (Fill=Transparent) rather than a
    /// solid filled square, so it never gets visually confused with the
    /// filled "E"/"S" markers. Same pulsing-brush technique those use,
    /// applied to Stroke instead of Fill.
    /// </summary>
    /// <summary>
    /// Live shape preview for Line/Rectangle/Circle - per spec, dragging
    /// should show pulsating white outline squares over every cell that
    /// WOULD be painted if released right now, updating as the mouse
    /// moves, with the real paint only committed on mouse-up. Reuses the
    /// exact same CellsForLine/CellsForRectangle/CellsForCircle helpers
    /// the commit step already uses, so the preview can never show a
    /// different shape than what actually gets painted. One shared
    /// pulsing brush across every square in the preview, not one
    /// animation per square - cheap regardless of how large the shape is.
    /// </summary>
    private void UpdateShapePreview(int startX, int startY, int currentX, int currentY)
    {
        ClearShapePreview();
        if (_tileset is null) return;

        if (_shapePreviewBrush is null)
        {
            var dimWhite = Color.FromArgb(200, 255, 255, 255);
            var brightWhite = Color.FromArgb(255, 255, 255, 255);
            _shapePreviewBrush = new SolidColorBrush(dimWhite);
            var pulse = new ColorAnimation
            {
                From = dimWhite,
                To = brightWhite,
                Duration = TimeSpan.FromSeconds(0.5),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            _shapePreviewBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);
        }

        IEnumerable<(int x, int y)> cells = _drawShape switch
        {
            DrawShape.Line => CellsForLine(startX, startY, currentX, currentY),
            DrawShape.Rectangle => CellsForRectangle(startX, startY, currentX, currentY),
            DrawShape.Circle => CellsForCircle(startX, startY, currentX, currentY),
            _ => Array.Empty<(int, int)>()
        };

        foreach (var (cx, cy) in cells)
        {
            if (!IsInsideMap(cx, cy)) continue;
            var square = new Rectangle
            {
                Width = _tileset.TileWidth,
                Height = _tileset.TileHeight,
                Fill = Brushes.Transparent,
                Stroke = _shapePreviewBrush,
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(square, cx * _tileset.TileWidth);
            Canvas.SetTop(square, cy * _tileset.TileHeight);
            Panel.SetZIndex(square, MarkerZIndex + 4); // just below the hover cursor (25), above everything else
            MapCanvas.Children.Add(square);
            _shapePreviewVisuals.Add(square);
        }
    }

    private void ClearShapePreview()
    {
        foreach (var square in _shapePreviewVisuals) MapCanvas.Children.Remove(square);
        _shapePreviewVisuals.Clear();
    }

    private void EnsureHoverCursorVisual()
    {
        if (_hoverCursorVisual is not null && MapCanvas.Children.Contains(_hoverCursorVisual)) return;
        if (_tileset is null) return;

        var dimWhite = Color.FromArgb(220, 255, 255, 255);
        var brightWhite = Color.FromArgb(255, 255, 255, 255);
        var strokeBrush = new SolidColorBrush(dimWhite);
        var pulse = new ColorAnimation
        {
            From = dimWhite,
            To = brightWhite,
            Duration = TimeSpan.FromSeconds(0.6),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        strokeBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);

        _hoverCursorVisual = new Rectangle
        {
            Width = _tileset.TileWidth,
            Height = _tileset.TileHeight,
            Fill = Brushes.Transparent,
            Stroke = strokeBrush,
            StrokeThickness = 2,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(_hoverCursorVisual, MarkerZIndex + 5); // above even the "S" marker (21) - always visible regardless of what's underneath
        MapCanvas.Children.Add(_hoverCursorVisual);
    }

    private void PlaceStartPositionVisual(int x, int y)
    {
        if (_tileset is null || _map is null) return;

        if (_startPositionVisual is not null)
            MapCanvas.Children.Remove(_startPositionVisual);

        var dimPurple = Color.FromArgb(160, 110, 20, 140);
        var brightPurple = Color.FromArgb(220, 190, 60, 255);
        var fillBrush = new SolidColorBrush(dimPurple);
        var pulse = new ColorAnimation
        {
            From = dimPurple,
            To = brightPurple,
            Duration = TimeSpan.FromSeconds(0.9),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        fillBrush.BeginAnimation(SolidColorBrush.ColorProperty, pulse);

        var marker = new Border
        {
            Width = _tileset.TileWidth,
            Height = _tileset.TileHeight,
            Background = fillBrush,
            BorderBrush = Brushes.MediumPurple,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            ToolTip = "Start Position - where the player appears when the game is tested.",
            Child = new TextBlock
            {
                Text = "S",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(marker, x * _tileset.TileWidth);
        Canvas.SetTop(marker, y * _tileset.TileHeight);
        Panel.SetZIndex(marker, MarkerZIndex + 1); // Above even the "E" event markers - there's only ever one of these, it should never get visually buried.

        MapCanvas.Children.Add(marker);
        _startPositionVisual = marker;
        _map.StartX = x;
        _map.StartY = y;
    }

    /// <summary>Blue "B" marker for a book placed on the map - flat color, not pulsing, since a book is a persistent placed object like a WorldObject, not a live event trigger point like "E" or "S".</summary>
    /// <summary>Books get their real animated sprite (same SpriteAnimator every other book-sprite view uses) since they already have one; Weapon/Armor/Item show a plain lettered marker since no editor/sprite system exists for those yet - same honest placeholder the Select Item picker's own tabs already show.</summary>
    /// <summary>Tracks any SpriteAnimator currently running from a Shift+hover preview, so a map rebuild mid-hover (rare, but possible) stops it rather than leaving an orphaned timer running forever.</summary>
    private readonly List<SpriteAnimator> _itemHoverAnimators = new();

    /// <summary>
    /// Static by default (first frame only) - an editor map full of
    /// constantly-animating markers is noisy and makes it harder to read
    /// the actual tile layout underneath. Hold Shift and hover a marker
    /// with a real sprite to preview its animation live; letting go or
    /// moving away reverts it to the static frame. The lettered fallback
    /// only appears when there's genuinely no sprite assigned - not a bug
    /// case, an honest "nothing to show yet" state.
    /// </summary>
    private void PlaceItemVisual(PlacedItemInstance instance)
    {
        if (_tileset is null) return;
        if (_mapItemVisuals.Remove(instance, out var oldVisual)) MapCanvas.Children.Remove(oldVisual);

        SpriteEntry? sprite = null;
        string tooltip;

        if (instance.Category == ReferencedItemCategory.Book)
        {
            var bookData = _bookDatabase.Books.FirstOrDefault(b => b.Id == instance.ItemId);
            sprite = bookData is null ? null : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == bookData.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == bookData.SpriteName);
            tooltip = bookData is not null ? $"{bookData.Name} (hold Shift and hover to preview animation)" : $"Book #{instance.ItemId} (not found)";
        }
        else if (instance.Category == ReferencedItemCategory.Container)
        {
            // Real, confirmed fix - a container is placed exactly like a book (same real PlacedItemInstance), so it gets the exact same real sprite/preview treatment here, not the generic lettered fallback that only ever covered Book by name.
            var containerData = _containerDatabase.Containers.FirstOrDefault(c => c.Id == instance.ItemId);
            // A container that starts locked shows its Locked Sprite (if it has one), as it will when the game starts.
            sprite = containerData is null ? null
                : (containerData.IsLocked && !string.IsNullOrEmpty(containerData.LockedSpriteName)
                    ? _spriteDatabase.Groups.FirstOrDefault(g => g.Name == containerData.LockedSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == containerData.LockedSpriteName)
                    : null)
                  ?? _spriteDatabase.Groups.FirstOrDefault(g => g.Name == containerData.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == containerData.SpriteName);
            tooltip = containerData is not null ? $"{containerData.Name} (hold Shift and hover to preview animation)" : $"Container #{instance.ItemId} (not found)";
        }
        else
        {
            tooltip = $"{instance.Category} #{instance.ItemId} (no editor built yet, per spec - placement still works)";
        }

        UIElement marker;
        double markerW = _tileset.TileWidth, markerH = _tileset.TileHeight;
        if (sprite is not null && sprite.Frames.Count > 0 && File.Exists(sprite.Frames[0].FilePath))
        {
            string firstFramePath = sprite.Frames[0].FilePath;
            var firstFrameBitmap = LoadStaticFrame(firstFramePath);
            (int quantW, int quantH) = firstFrameBitmap is not null ? SpriteSizing.Quantize(firstFrameBitmap.PixelWidth, firstFrameBitmap.PixelHeight) : (_tileset.TileWidth, _tileset.TileHeight);
            markerW = quantW; markerH = quantH;

            var image = new Image
            {
                Width = quantW,
                Height = quantH,
                IsHitTestVisible = true, // needs real hit-testing to receive the hover events below - clicks still bubble up to MapCanvas normally since nothing here marks them Handled
                ToolTip = tooltip,
                Source = firstFrameBitmap
            };

            SpriteAnimator? liveAnimator = null;
            image.MouseMove += (_, _) =>
            {
                bool shiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (shiftHeld && liveAnimator is null)
                {
                    liveAnimator = new SpriteAnimator(sprite, image, _resourceDatabase);
                    _itemHoverAnimators.Add(liveAnimator);
                }
                else if (!shiftHeld && liveAnimator is not null)
                {
                    liveAnimator.Stop();
                    _itemHoverAnimators.Remove(liveAnimator);
                    liveAnimator = null;
                    image.Source = LoadStaticFrame(firstFramePath);
                }
            };
            image.MouseLeave += (_, _) =>
            {
                if (liveAnimator is null) return;
                liveAnimator.Stop();
                _itemHoverAnimators.Remove(liveAnimator);
                liveAnimator = null;
                image.Source = LoadStaticFrame(firstFramePath);
            };

            marker = image;
        }
        else
        {
            string letter = instance.Category.ToString()[0].ToString();
            var fill = instance.Category switch
            {
                ReferencedItemCategory.Book => Color.FromArgb(200, 30, 90, 200),
                ReferencedItemCategory.Container => Color.FromArgb(200, 150, 100, 40),
                _ => Color.FromArgb(200, 120, 120, 120)
            };
            var border = instance.Category switch
            {
                ReferencedItemCategory.Book => Brushes.DeepSkyBlue,
                ReferencedItemCategory.Container => Brushes.SaddleBrown,
                _ => Brushes.Silver
            };
            marker = BuildLetteredItemMarker(letter, fill, border, tooltip);
        }

        PositionPlacedVisual(marker, instance.X, instance.Y, markerW, markerH, instance.OffsetX, instance.OffsetY, instance.Z);
        Panel.SetZIndex(marker, 15); // Above tiles/objects, below the "E"/"S" markers - an item is something you walk up to and interact with, not a trigger zone.
        var onFloor = FitToFloor(marker, FloorOf(instance));
        if (onFloor is null) return; // on a floor above the picked one

        MapCanvas.Children.Add(marker);
        _mapItemVisuals[instance] = marker;
        if (onFloor == true) ApplyFreeDrawOrder();
        if (ReferenceEquals(_freeSelected, instance)) UpdateFreeSelectionVisual();
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

    private Border BuildLetteredItemMarker(string letter, Color fill, Brush border, string tooltip)
    {
        if (_tileset is null) return new Border();
        return new Border
        {
            Width = _tileset.TileWidth,
            Height = _tileset.TileHeight,
            Background = new SolidColorBrush(fill),
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            ToolTip = tooltip,
            Child = new TextBlock { Text = letter, Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    /// <summary>Opens the real, shared Select Item picker (same one Complex Condition and everywhere else uses) defaulting to whichever type is currently chosen in the Item submenu - not a separate custom picker.</summary>
    private ItemReference? PromptForItem(ReferencedItemCategory category)
    {
        var picker = new SelectItemPickerWindow(_bookDatabase, _objectDatabase, _containerDatabase, _spriteDatabase, new ItemReference { Category = category }) { Owner = this };
        return picker.ShowDialog() == true ? picker.Result : null;
    }

    // ---------- Object database ----------

    private void ObjectDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        // The Object Editor edits the project's own objects in place - saved with the project.
        new ObjectEditorWindow(_objectDatabase, _spriteDatabase, _resourceDatabase, _bookDatabase, _containerDatabase, _variableDatabase, _statDatabase)
        {
            Owner = this, TileWidth = _tileset?.TileWidth ?? 32, TileHeight = _tileset?.TileHeight ?? 32
        }.ShowDialog();
        RefreshObjectPalette();
        RefreshAllObjectVisuals();
    }

    private void CommonEventsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Saved with the project's System Settings; Call Common Event runs one.
        new CommonEventsWindow(_systemSettings.CommonEvents, _resourceDatabase, _variableDatabase, _bookDatabase, _objectDatabase, _containerDatabase, _spriteDatabase) { Owner = this }.ShowDialog();
    }

    private void PaperDollEditorMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Paperdolls are saved with the project's objects; an object opens one (Object Editor > Opens paperdoll).
        new PaperDollEditorWindow(_objectDatabase, _spriteDatabase, _resourceDatabase, _bookDatabase, _containerDatabase, _variableDatabase, _statDatabase) { Owner = this }.ShowDialog();
    }

    private void WallDesignerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Wall sets are saved with the project's objects; a Wall object links to one (Object Editor).
        new WallDesignerWindow(_objectDatabase, _spriteDatabase, _tileset?.TileWidth ?? 32, _tileset?.TileHeight ?? 32) { Owner = this }.ShowDialog();
        RefreshObjectPalette();
        if (_map is not null) BuildMapCanvas(); // placed sets show their pieces as they are now
    }

    private void StaircaseDesignerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Staircases are saved with the project's objects; a Stairs object links to one (Object Editor).
        new StaircaseDesignerWindow(_objectDatabase, _spriteDatabase, _resourceDatabase, _tileset?.TileWidth ?? 32, _tileset?.TileHeight ?? 32) { Owner = this }.ShowDialog();
        RefreshObjectPalette();
        RefreshAllObjectVisuals();
    }

    private void CurrencyEditorMenuItem_Click(object sender, RoutedEventArgs e) =>
        new CurrencyEditorWindow(_currencyDatabase) { Owner = this }.ShowDialog();

    // ---------- Save / Load ----------


}
