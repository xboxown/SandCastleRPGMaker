using System.Windows;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A project's Style (File > New Project): Ultima 7 - the engine built so far, GameTestWindow, untouched - or Ultima 8,
/// whose games run in their own engine, U8GameWindow (Editor/U8: copied from the Ultima 7 engine and changed there only).
/// The editor is the same for both; what Ultima 8 adds shows only in an Ultima 8 project. These are the Ultima 8
/// engine's doors: Game Test and a built game.
/// </summary>
public partial class MainWindow
{
    private string _projectStyle = GameStyle.Ultima7;
    private bool IsUltima8 => _projectStyle == GameStyle.Ultima8;

    private U8GameWindow NewU8GameWindow(bool atTitle, bool shipped) => NewU8GameWindow(_worlds.FirstOrDefault() ?? _map!, atTitle, shipped);

    private U8GameWindow NewU8GameWindow(MapData start, bool atTitle, bool shipped, CharacterDatabase? characters = null, List<MapData>? worlds = null) =>
        new(start, Sheet(start.Tileset.TilesetSpriteName)?.Bitmap ?? PrimarySheet()?.Bitmap ?? _tilesetBitmap!, start.Tileset.TileWidth, start.Tileset.TileHeight,
            Sheet(start.Tileset.TilesetSpriteName)?.Columns ?? PrimarySheet()?.Columns ?? _paletteColumns,
            start.Tileset.TilesetSpriteName, _resourceDatabase, _spriteDatabase, _bookDatabase, _objectDatabase,
            characters ?? _characterDatabase, _poseDatabase, _tilePropertiesDatabase, _variableDatabase, _statDatabase, _containerDatabase, _systemSettings, _factionDatabase)
        {
            StartAtTitle = atTitle,
            IsShippedGame = shipped,
            Worlds = worlds ?? _worlds,
            Title = shipped ? (_currentProjectName is { Length: > 0 } name ? name : "Game") : "Sandcastle RPG Maker - Game Test (Ultima 8 Engine, Alpha)",
            WindowStartupLocation = shipped ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

    private U8GameWindow? NextU8GameWindow(U8GameWindow closed, bool shipped) =>
        closed.ScenarioRequestedId is int scenarioId && _systemSettings.Scenarios.Find(scenarioId) is { } scenario ? ScenarioU8GameWindow(scenario) :
        closed.ReplacedBy ?? (closed.ReturnToTitleRequested ? NewU8GameWindow(atTitle: true, shipped) : closed.RestartRequested ? NewU8GameWindow(atTitle: false, shipped) : null);

    /// <summary>A built Ultima 8 game's window: closing it ends the program - unless another game window carries on.</summary>
    private void RunStandaloneU8(U8GameWindow game)
    {
        var display = _systemSettings.Display;
        if (display.Mode == GameScreenMode.FullScreen)
        {
            game.WindowStyle = WindowStyle.None;
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
            if (NextU8GameWindow(game, shipped: true) is { } next) RunStandaloneU8(next);
            else Application.Current.Shutdown();
        };
        Application.Current.MainWindow = game;
        game.Show();
    }

    /// <summary>Game Test of an Ultima 8 project: its own engine.</summary>
    private void LaunchU8GameTest(bool atTitle)
    {
        var firstScenario = atTitle ? null : _systemSettings.Scenarios.StartScenario; // the Scenario Developer's choice for F5 (MainWindow.Scenarios.cs)
        for (U8GameWindow? game = firstScenario is not null ? ScenarioU8GameWindow(firstScenario) : NewU8GameWindow(atTitle || _systemSettings.Title.InGameTest, shipped: false); game is not null; game = NextU8GameWindow(game, shipped: false))
        {
            game.Owner = this;
            game.ShowDialog();
        }
    }

    /// <summary>
    /// A map cell's size in map pixels. Ultima 7: the tile's. Ultima 8: its tiles are 2:1 diamonds over square cells, tile
    /// height x tile height, as its game lays them out (U8ProjectionCanvas) - the Map Editor shows them in that diamond view
    /// (MapCanvas.Diamond), everything placed in these square map coordinates.
    /// </summary>
    private int CellW => IsUltima8 ? _tileset!.TileHeight : _tileset!.TileWidth;
    private int CellH => _tileset!.TileHeight;

    /// <summary>Room above an Ultima 8 map's north corner in the editor, in cells - for walls and things standing tall there.</summary>
    private const int DiamondHeadroomCells = 8;

    /// <summary>The map canvas set up for this map: the diamond view in an Ultima 8 project, else as it always was. Returns its size.</summary>
    private void SetUpMapView(double mapPixelWidth, double mapPixelHeight)
    {
        MapCanvas.Diamond = IsUltima8;
        if (!IsUltima8)
        {
            MapCanvas.Width = mapPixelWidth;
            MapCanvas.Height = mapPixelHeight;
            return;
        }
        MapCanvas.CellSize = CellH;
        MapCanvas.OriginX = mapPixelHeight; // the map's west corner at the left edge
        MapCanvas.OriginY = DiamondHeadroomCells * CellH;
        MapCanvas.Width = mapPixelWidth + mapPixelHeight;
        MapCanvas.Height = (mapPixelWidth + mapPixelHeight) / 2 + MapCanvas.OriginY;
    }

    /// <summary>Something lying flat on the map (grid, marker, highlight): in an Ultima 8 map turned into its cell's diamond - a letter on it kept upright.</summary>
    private T FlatOnMap<T>(T element) where T : FrameworkElement
    {
        if (!IsUltima8) return element;
        U8ProjectionCanvas.SetPlacement(element, U8Placement.Flat);
        if (element is System.Windows.Controls.Border { Child: FrameworkElement letter })
        {
            letter.RenderTransformOrigin = new Point(0.5, 0.5);
            letter.RenderTransform = U8ProjectionCanvas.UprightTransform;
        }
        return element;
    }

    /// <summary>Where a picture on the map is drawn on the screen: in the Ultima 8 view, where the canvas laid it out; else its own box.</summary>
    private Rect DrawnRect(FrameworkElement visual) =>
        MapCanvas.Diamond ? System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(visual)
            : new Rect(System.Windows.Controls.Canvas.GetLeft(visual), System.Windows.Controls.Canvas.GetTop(visual), visual.Width, visual.Height);

    /// <summary>
    /// The box (Canvas.Left / Top) a w x h picture of a thing needs to be drawn with its top-left at this point on the screen.
    /// Ultima 8 view: things stand upright on their cell's near corner (U8Placement.Thing), so the point its stand point is
    /// drawn at goes back to the map. Else the point as it is.
    /// </summary>
    private Point PictureCornerFor(Point screenTopLeft, double width, double height)
    {
        if (!MapCanvas.Diamond) return screenTopLeft;
        var stand = MapCanvas.ToMap(new Point(screenTopLeft.X + width / 2, screenTopLeft.Y + height)); // (left + w/2 + cell/2, top + h)
        return new Point(stand.X - width / 2 - CellW / 2.0, stand.Y - height);
    }

    /// <summary>
    /// An outline <paramref name="pad"/> pixels round a picture, wherever it's drawn: Ultima 7, its box less the pad. Ultima 8
    /// view: placed as the picture is (thing or character), its anchor moved by the pad, so it lands round it exactly.
    /// </summary>
    private void PlaceOutline(FrameworkElement outline, FrameworkElement visual, double pad)
    {
        double left = System.Windows.Controls.Canvas.GetLeft(visual), top = System.Windows.Controls.Canvas.GetTop(visual);
        if (!MapCanvas.Diamond)
        {
            System.Windows.Controls.Canvas.SetLeft(outline, left - pad);
            System.Windows.Controls.Canvas.SetTop(outline, top - pad);
            return;
        }
        System.Windows.Controls.Canvas.SetLeft(outline, left);
        System.Windows.Controls.Canvas.SetTop(outline, top);
        U8ProjectionCanvas.SetPlacement(outline, U8ProjectionCanvas.GetPlacement(visual));
        // The picture's own point that sits on its spot (its anchor, else its bottom-middle), the same point of the outline.
        var hot = U8ProjectionCanvas.GetAnchor(visual) ?? new Point(0.5, 1);
        double w = visual.Width, h = visual.Height;
        U8ProjectionCanvas.SetAnchor(outline, new Point((hot.X * w + pad) / (w + 2 * pad), (hot.Y * h + pad) / (h + 2 * pad)));
    }

    /// <summary>
    /// Ultima 8: a placed character in the editor as its game draws it - its picture at its own size, standing in the middle
    /// of its cell's diamond, on its frame's stand point when it has one.
    /// </summary>
    private void StandLikeU8Character(FrameworkElement visual, SpriteEntry? sprite, BitmapSource? picture)
    {
        if (!IsUltima8) return;
        U8ProjectionCanvas.SetPlacement(visual, U8Placement.Character);
        if (picture is null) return;
        visual.Width = picture.PixelWidth;
        visual.Height = picture.PixelHeight;
        if (sprite is { Frames.Count: > 0 } && sprite.Frames[0] is { HasAnchor: true } first)
            U8ProjectionCanvas.SetAnchor(visual, new Point(first.AnchorX / (double)picture.PixelWidth, first.AnchorY / (double)picture.PixelHeight));
    }

    /// <summary>The style shown after the project's name in the title bar.</summary>
    private string StyleTitleSuffix => IsUltima8 ? "  [Ultima 8 style]" : string.Empty;
}
