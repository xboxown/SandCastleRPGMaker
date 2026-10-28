// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Upper floors (MapFloors): floor 2-9 tiles painted in the Map Editor.
///
/// - Physics: each run of painted tiles along a row is one solid slab one
///   lift thick, just under its floor (floor 2: lift 4, so people on it stand
///   at lift 5). You walk on it like any solid top, things put down over it
///   rest on it (a surface, like a table), and for whoever is under it, it is
///   the ceiling - Exult's Map_chunk::is_roof (solid 4+ lifts over the feet).
/// - Drawing: a tile on floor N is drawn at its floor's lift, shifted 4px up
///   and left per lift like everything raised (gamewin.cc: lift * 4), and in
///   its floor's draw band - over everything on the floors below it.
/// - Inside: under a slab, it and everything on its floor and above is hidden
///   (gamerend.cc skip_above), so the room shows.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>One run of painted tiles on an upper floor, along one row: a slab of floor to stand on, and a ceiling for whoever is below.</summary>
    private sealed class FloorSlab
    {
        public required int Floor { get; init; }
    }

    /// <summary>Draw bands: everything on floor N is drawn over everything on the floors below it.</summary>
    private const int FloorZIndexBand = 10_000_000;
    /// <summary>Name labels, floating text: over every floor's band.</summary>
    private const int AboveEverythingZIndex = 1_000_000_000;

    private List<Body> _floorSlabBodies = new();
    private readonly List<(int SlabLift, UIElement Visual)> _floorTileVisuals = new();

    /// <summary>The upper floors' slabs, built once (the tiles never change in play).</summary>
    private void BuildFloorSlabs()
    {
        _floorSlabBodies = new List<Body>();
        WorldChanged();
        foreach (var layer in _map.UpperFloorLayers)
        {
            int slabLift = MapFloors.SlabLift(layer.Floor);
            for (int y = 0; y < _map.Height; y++)
            {
                int x = 0;
                while (x < _map.Width)
                {
                    if (layer.GetTile(x, y, _map.Width) < 0) { x++; continue; }
                    int start = x;
                    while (x < _map.Width && layer.GetTile(x, y, _map.Width) >= 0) x++;
                    _floorSlabBodies.Add(new Body(new FloorSlab { Floor = layer.Floor },
                        start * UnitsPerTileX, y * UnitsPerTileY, (x - start) * UnitsPerTileX, UnitsPerTileY,
                        slabLift, 1, Solid: true, Elevates: true, Stackable: true));
                }
            }
        }
    }

    /// <summary>The upper floors' tiles, each drawn at its floor's lift and in its floor's draw band.</summary>
    private void BuildUpperFloorVisuals()
    {
        _floorTileVisuals.Clear();
        BuildLandingVisuals();
        if (_tilesetBitmap is null) return;
        foreach (var layer in _map.UpperFloorLayers)
        {
            int lift = MapFloors.BaseLift(layer.Floor);
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    int tileIndex = layer.GetTile(x, y, _map.Width);
                    if (tileIndex < 0) continue;
                    var image = new Image
                    {
                        Width = _tileWidth,
                        Height = _tileHeight,
                        IsHitTestVisible = true, // it catches clicks: what's under the floor (the room below) can't be identified or used through it
                        Source = TileSource(tileIndex) // from whichever of the map's tilesheets it's from
                    };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                    Canvas.SetLeft(image, x * _tileWidth - lift * LiftPixelShift);
                    Canvas.SetTop(image, y * _tileHeight - lift * LiftPixelShift);
                    Panel.SetZIndex(image, SlabZIndex(layer.Floor, MapFloors.SlabLift(layer.Floor))); // under everything standing on it, over the floors below - except what stands in front of it (U8GameWindow.UpperFloorOrder.cs)
                    GameCanvas.Children.Add(image);
                    _floorTileVisuals.Add((MapFloors.SlabLift(layer.Floor), image));
                    _upperTileImages[(layer.Floor, x, y)] = image;
                }
            }
        }
    }

    /// <summary>
    /// Staircases' landing tiles: each drawn on its own, at its own lift and in
    /// its floor's draw band, and hidden under a roof with that floor's tiles -
    /// as Exult paints and skips every object by its own lift (gamerend.cc
    /// skip_lift), so a landing at the top of the stairs is part of the floor
    /// above, not of the ground-floor staircase.
    /// </summary>
    private void BuildLandingVisuals()
    {
        foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            if (DefinitionOf(instance) is not { } obj || StairLook.PoseOf(obj, _objects, instance.Facing) is not { } pose) continue;
            foreach (var landing in pose.Steps.Where(s => s.IsLanding))
            {
                if (StairLook.LandingImage(landing, _sprites) is not { } image) continue;
                image.IsHitTestVisible = true; // as the floor's tiles: nothing under it is clicked through it
                // Where the staircase's own picture would have put it: the anchor cell, the step's picture offset (which carries its lift), the staircase's own lift.
                Canvas.SetLeft(image, instance.X * _tileWidth + instance.OffsetX * SpriteSizing.BaseUnit + landing.PictureX - instance.Z * LiftPixelShift);
                Canvas.SetTop(image, instance.Y * _tileHeight + instance.OffsetY * SpriteSizing.BaseUnit + landing.PictureY - instance.Z * LiftPixelShift);
                Panel.SetZIndex(image, SlabZIndex(StairLook.LandingFloor(landing, instance.Z), instance.Z + Math.Max(0, landing.TopLift - 1))); // with the floor's tiles
                GameCanvas.Children.Add(image);
                _floorTileVisuals.Add((instance.Z + Math.Max(0, landing.TopLift - 1), image)); // its slab's lift: hidden under a roof as the floor's tiles are
            }
        }
    }

    /// <summary>Which floor's draw band something at this lift is in.</summary>
    private static int FloorBand(int lift) => (MapFloors.FloorOfLift(Math.Max(0, lift)) - 1) * FloorZIndexBand;

    /// <summary>
    /// The lift a walker placed on this footprint stands at, on the given floor:
    /// on top of the highest solid thing under it that starts below the floor
    /// above (its slab is a ceiling, not something to stand on), or the ground.
    /// </summary>
    private int StandingLiftOnFloor(UnitRect foot, int floor, object? self = null)
    {
        int ceilingFrom = MapFloors.BaseLift(floor) + MapFloors.LiftsPerFloor - 1; // the next floor's slab
        return WorldBodies()
            .Where(b => b.Solid && b.Overlaps(foot) && !ReferenceEquals(b.Entity, self) && b.Entity is not PlacedCharacter && b.Z < ceilingFrom)
            .Select(b => b.TopLift).DefaultIfEmpty(0).Max();
    }

    /// <summary>For gravity: only what starts below the ceiling of the floor something is on can hold it up - never the slab of the floor above.</summary>
    private static bool BelowCeilingOf(Body support, int lift) =>
        support.Z < MapFloors.BaseLift(MapFloors.FloorOfLift(Math.Max(0, lift))) + MapFloors.LiftsPerFloor - 1;
}
