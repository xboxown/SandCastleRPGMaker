// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's reach and throwing, as Pentagram (ScummVM's ultima8: Item::canReach, Item::getThrowRange,
/// GameMapGump::onMouseDouble / StartDraggingItem / DropItem) - enforced, not Ultima 7's walk-up reach:
///  - Reach: the gap between the two footprints, along each axis, at most 128 (Ultima 8's units: a floor tile, one map
///    cell here), and a clear line - feet to feet, middle to middle, or eye height to its top - through nothing solid.
///    Using, picking up, opening, giving, dropping close by. Talking: beside them (32) - Pentagram lets a double-click on
///    anyone through; this project asks that you stand beside them.
///  - Throwing: let go beyond reach and it's thrown, if it's within its throw range ((64 - weight + Strength)² / 2) with a
///    clear path; it flies at 64 - weight + Strength, off by up to 4 x (30 - Dexterity) (at least 20), and falls where it
///    lands. Too far: a red X.
/// </summary>
public partial class U8GameWindow
{
    private const int U8ReachUnits = 128, U8TalkUnits = 32;

    /// <summary>Ultima 8 units in one 8px footprint unit here: a map cell is one Ultima 8 floor tile, 128 units.</summary>
    private double U8UnitsPerFootUnit => 8.0 * 128 / Math.Max(1, _tileWidth);

    /// <summary>Item::canReach: within range (the footprints' gap) and a clear line of sight.</summary>
    private bool CanReachU8(Volume target, int rangeUnits, object? targetEntity = null, Volume? stuckIn = null)
    {
        var me = PlayerVolume();
        int range = (int)Math.Floor(rangeUnits / U8UnitsPerFootUnit);
        int gapX = Math.Max(0, Math.Max(target.X - (me.X + me.W), me.X - (target.X + target.W)));
        int gapY = Math.Max(0, Math.Max(target.Y - (me.Y + me.D), me.Y - (target.Y + target.D)));
        if (gapX > range || gapY > range) return false;
        return LineOfSightU8(me, target, targetEntity, stuckIn);
    }

    /// <summary>
    /// Item::canReach's sweeps: feet to feet, middle to middle (from eye height when tall), eye height to the top - any one
    /// clear will do. What's solid blocks (walls up to the roof); not the target, nor what a staying thing is stuck in.
    /// </summary>
    private bool LineOfSightU8(Volume me, Volume target, object? targetEntity, Volume? stuckIn)
    {
        var query = new ReachQuery(this);
        int meX = me.X + me.W / 2, meY = me.Y + me.D / 2, toX = target.X + target.W / 2, toY = target.Y + target.D / 2;
        int eye = me.H > 2 ? me.Z + me.H - 1 : me.Z;
        var lines = new[]
        {
            (new Tile3(meX, meY, me.Z), new Tile3(toX, toY, target.Z)),
            (new Tile3(meX, meY, eye), new Tile3(toX, toY, target.Z + target.H / 2)),
            (new Tile3(meX, meY, eye), new Tile3(toX, toY, target.Z + Math.Max(0, target.H - 1)))
        };
        foreach (var (from, to) in lines)
        {
            bool clear = true;
            foreach (var p in ZombieLine(from, to))
            {
                if (target.Has(p) || me.Has(p)) continue;
                if (query.BlockingAt(p) is not { } blocker) continue;
                if (targetEntity is not null && ReferenceEquals(blocker.Entity, targetEntity)) continue;
                if (stuckIn is { } s && blocker.Left < s.X + s.W && s.X < blocker.Right && blocker.Top < s.Y + s.D && s.Y < blocker.Bottom) continue;
                clear = false;
                break;
            }
            if (clear) return true;
        }
        return false;
    }

    /// <summary>Talking to someone: you stand beside them (and can see them).</summary>
    private bool IsBesideU8(PlacedCharacter placed)
    {
        if (_mapCharacters.FirstOrDefault(mc => mc.Placement == placed) is not { } mc) return false;
        var foot = CharacterFootprint(mc);
        return CanReachU8(new Volume(foot.Left, foot.Top, mc.Lift, foot.Width, foot.Depth, mc.Size.Z), U8TalkUnits, placed);
    }

    /// <summary>A map cell's middle, as a spot to reach (dropping there).</summary>
    private Volume CellSpot(int cellX, int cellY, int lift)
    {
        int units = Math.Max(1, _tileWidth / SpriteSizing.BaseUnit);
        return new Volume(cellX * units + units / 2, cellY * units + units / 2, lift, 1, 1, 1);
    }

    // ---------- Throwing ----------

    private sealed class ThrownThing
    {
        public required DragOrigin Origin;
        public required PointerSession Session;
        public required Point LandOnCanvas;      // where it's let go of, on the canvas (as the drop would have it)
        public required FrameworkElement Picture;
        public required double FromX, FromY, ToX, ToY, DurationMs, ApexPx;
        public double ElapsedMs;
    }

    private ThrownThing? _thrown;
    private bool _landingThrow;

    /// <summary>
    /// GameMapGump::DropItem, can't reach: thrown - within its throw range and with a clear path - toward where it was let
    /// go of, off by the thrower's clumsiness. True: it's in the air (it lands by itself); false: refused.
    /// </summary>
    private bool TryThrowU8(DragOrigin origin, PointerSession session, Point mouseInWorld, int cellX, int cellY)
    {
        if (_player is null) return false;
        double weight = PayloadWeight(origin);
        int strength = Stat(_player, BuiltInStatKind.Strength, 10), dexterity = Stat(_player, BuiltInStatKind.Dexterity, 10);
        int speed = Math.Max(1, (int)Math.Round(64 - weight + strength));
        int throwRange = speed * speed / 2; // Item::getThrowRange
        var spot = CellSpot(cellX, cellY, _playerZ);
        var me = PlayerVolume();
        int range = (int)Math.Floor(throwRange / U8UnitsPerFootUnit);
        if (Math.Max(0, Math.Max(spot.X - (me.X + me.W), me.X - (spot.X + spot.W))) > range || Math.Max(0, Math.Max(spot.Y - (me.Y + me.D), me.Y - (spot.Y + spot.D))) > range)
        {
            Refuse(MousePointerKind.RedX, "Too far to throw that.");
            return false;
        }
        if (!LineOfSightU8(me, spot, null, null)) { Refuse(MousePointerKind.Blocked, "Something is in the way."); return false; }

        // Off by the thrower's clumsiness (Ultima 8 units, as map pixels): a cell is 128.
        int inaccuracy = Math.Max(20, 4 * (30 - dexterity));
        double unitsToPx = _tileWidth / 128.0;
        double offX = Random.Shared.Next(-inaccuracy, inaccuracy + 1) * unitsToPx, offY = Random.Shared.Next(-inaccuracy, inaccuracy + 1) * unitsToPx;
        var land = new Point(mouseInWorld.X + offX - offY, mouseInWorld.Y + (offX + offY) / 2); // the map's offset in the diamond view
        var landOnMap = GameCanvas.ToMap(land);

        var (category, itemId) = PayloadIdentity(origin);
        var picture = BuildItemIcon(category, itemId, null, PayloadObject(origin));
        picture.IsHitTestVisible = false;
        U8ProjectionCanvas.SetPlacement(picture, U8Placement.Center);
        Panel.SetZIndex(picture, AboveEverythingZIndex - 2);
        GameCanvas.Children.Add(picture);

        // MissileTracker: speed a frame (Ultima 8's 30 a second), gravity 4 - the arc's top g x frames² / 8, half a pixel a unit up.
        double distanceUnits = Math.Sqrt(Math.Pow(landOnMap.X - _playerPixelX, 2) + Math.Pow(landOnMap.Y - _playerPixelY, 2)) / unitsToPx;
        double frames = Math.Max(4, distanceUnits / speed);
        _thrown = new ThrownThing
        {
            Origin = origin, Session = session, LandOnCanvas = land, Picture = picture,
            FromX = _playerPixelX, FromY = _playerPixelY - _tileHeight / 2.0, ToX = landOnMap.X, ToY = landOnMap.Y,
            DurationMs = frames * 1000 / 30, ApexPx = Math.Min(160, 4 * frames * frames / 8 * 0.5)
        };
        PlaceThrown(_thrown);
        // The thing itself waits out of sight where it was till it lands (the drag shows its source again when it ends).
        Dispatcher.BeginInvoke(() => { if (_thrown is { } t && ThrownSourceVisual(t) is { } v) v.Visibility = Visibility.Hidden; });
        StatusText.Text = $"You throw \"{PayloadName(origin)}\".";
        return true;
    }

    private UIElement? ThrownSourceVisual(ThrownThing thrown) => thrown.Origin switch
    {
        MapItemOrigin m => VisualOf(m.Item),
        MapObjectOrigin o => VisualOf(o.Obj),
        _ => null
    };

    private void PlaceThrown(ThrownThing thrown)
    {
        double t = Math.Clamp(thrown.ElapsedMs / thrown.DurationMs, 0, 1);
        double x = thrown.FromX + (thrown.ToX - thrown.FromX) * t, y = thrown.FromY + (thrown.ToY - thrown.FromY) * t;
        double rise = thrown.ApexPx * 4 * t * (1 - t); // up and down again (a lift's shift is a straight rise in the diamond view)
        var picture = thrown.Picture;
        Canvas.SetLeft(picture, x - rise - picture.Width / 2);
        Canvas.SetTop(picture, y - rise - picture.Height / 2);
    }

    /// <summary>Each frame: whatever is thrown flies on; landed, it's put down where it came to (the drop, without the reach).</summary>
    private void UpdateThrownU8(double elapsedMs)
    {
        if (_thrown is not { } thrown) return;
        thrown.ElapsedMs += elapsedMs;
        PlaceThrown(thrown);
        if (thrown.ElapsedMs < thrown.DurationMs) return;
        GameCanvas.Children.Remove(thrown.Picture);
        _thrown = null;
        if (ThrownSourceVisual(thrown) is { } source) source.Visibility = Visibility.Visible;
        _landingThrow = true;
        bool landed;
        try { landed = DropOnMap(thrown.Origin, thrown.Session, GameCanvas.TranslatePoint(thrown.LandOnCanvas, PaperdollGumpsCanvas)); }
        finally { _landingThrow = false; }
        if (!landed) StatusText.Text = $"\"{PayloadName(thrown.Origin)}\" bounces back.";
    }
}
