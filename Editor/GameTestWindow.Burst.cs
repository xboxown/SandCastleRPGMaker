// Ultima 7 engine (GameTestWindow): the same as the Ultima 8 engine's U8GameWindow.Burst.cs.
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Things that fly and bounce (Pentagram: Actor::dieU8's pieces, Item::hurl, GravityProcess's bounce): a real object in the air, drawn on its own,
/// which comes down, bounces, turns back from walls and is put on the map where it stops - an ordinary object from then on.
///  - Bursting on death (Character Editor > Health): the body stays as always; its pieces are Junk objects thrown out round it.
/// Also: what lies on a thing, and how heavy that is.
/// </summary>
public partial class GameTestWindow
{
    private sealed class HurledPiece
    {
        public required InventoryItemInstance Gift;
        public required Image Picture;
        public double X, Y, VX, VY, Height, VZ;
        public int Floor, Bounces;
    }

    private const double HurlGravity = 520, HurlBounceKeeps = 0.5, HurlFrictionKeeps = 0.6; // pixels a second; what a bounce leaves of the speed
    private const int HurlMostBounces = 3;
    private readonly List<HurledPiece> _hurled = new();

    /// <summary>A thing into the air from a point, with its speeds (pixels a second): it lands where it stops. False: nothing to draw.</summary>
    private bool HurlThing(InventoryItemInstance gift, double x, double y, int lift, double vx, double vy, double vz)
    {
        if (ObjectDef(gift) is not { } def || ObjectLooks.SpriteFor(def, StateOf(gift), _sprites) is not { Frames.Count: > 0 } sprite) return false;
        var (width, height) = QuantizedSpriteSize(sprite);
        var picture = new Image { Width = width, Height = height, IsHitTestVisible = false, Source = LoadStaticFrame(sprite.Frames[0].FilePath) };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(picture, AboveEverythingZIndex - 3);
        GameCanvas.Children.Add(picture);
        var piece = new HurledPiece { Gift = gift, Picture = picture, X = x, Y = y, VX = vx, VY = vy, VZ = vz, Height = lift * LiftPixelShift, Floor = MapFloors.FloorOfLift(lift) };
        _hurled.Add(piece);
        PlaceHurled(piece);
        return true;
    }

    private void PlaceHurled(HurledPiece piece)
    {
        Canvas.SetLeft(piece.Picture, piece.X - piece.Height - piece.Picture.Width / 2);
        Canvas.SetTop(piece.Picture, piece.Y - piece.Height - piece.Picture.Height / 2);
    }

    /// <summary>Each frame: on through the air; it bounces off the ground and turns back from what is solid; stopped, it's put down there.</summary>
    private void UpdateHurled(double elapsedMs)
    {
        if (_hurled.Count == 0) return;
        double seconds = Math.Min(0.1, elapsedMs / 1000.0);
        var world = WorldNow();
        foreach (var piece in _hurled.ToList())
        {
            double groundPx = MapFloors.BaseLift(piece.Floor) * LiftPixelShift;
            double nx = piece.X + piece.VX * seconds, ny = piece.Y + piece.VY * seconds;
            int liftNow = (int)Math.Round(piece.Height / Math.Max(1, LiftPixelShift));
            var foot = new UnitRect((int)Math.Floor(nx / SpriteSizing.BaseUnit), (int)Math.Floor(ny / SpriteSizing.BaseUnit), 1, 1);
            bool off = nx < 0 || ny < 0 || nx >= _map.Width * _tileWidth || ny >= _map.Height * _tileHeight;
            if (off || world.Overlapping(foot).Any(b => b.Solid && b.Entity is not PlacedCharacter && liftNow >= b.Z && liftNow < b.TopLift))
            {
                piece.VX = -piece.VX * HurlFrictionKeeps; // a wall: back the other way
                piece.VY = -piece.VY * HurlFrictionKeeps;
            }
            else { piece.X = nx; piece.Y = ny; }
            piece.VZ -= HurlGravity * seconds;
            piece.Height += piece.VZ * seconds;
            if (piece.Height <= groundPx)
            {
                piece.Height = groundPx;
                piece.Bounces++;
                piece.VZ = -piece.VZ * HurlBounceKeeps;
                piece.VX *= HurlFrictionKeeps;
                piece.VY *= HurlFrictionKeeps;
                if (piece.Bounces >= HurlMostBounces || piece.VZ < 30) { LandHurled(piece); continue; }
            }
            PlaceHurled(piece);
        }
    }

    private void LandHurled(HurledPiece piece)
    {
        _hurled.Remove(piece);
        GameCanvas.Children.Remove(piece.Picture);
        int x = Math.Clamp((int)Math.Floor(piece.X / _tileWidth), 0, _map.Width - 1), y = Math.Clamp((int)Math.Floor(piece.Y / _tileHeight), 0, _map.Height - 1);
        PlaceNewThing(piece.Gift, x, y, piece.Floor);
        RefreshAfterMove();
        SoakLog($"landed: {ObjectDef(piece.Gift)?.Name} at ({x}, {y})");
    }

    /// <summary>Actor::dieU8's explode: its Junk pieces thrown out round where it fell. The body stays, as always.</summary>
    private void BurstOnDeath(CharacterDefinition character, double x, double y, int lift)
    {
        foreach (var kind in character.DeathPieces)
            for (int n = 0; n < Math.Max(1, kind.Count); n++)
                foreach (var gift in MakeGifts(ReferencedItemCategory.Object, kind.ObjectId, 1))
                {
                    double angle = Rng.NextDouble() * 2 * Math.PI, speed = 40 + Rng.Next(90);
                    HurlThing(gift, x, y, lift, Math.Cos(angle) * speed, Math.Sin(angle) * speed, 110 + Rng.Next(100));
                }
    }

    // ---------- What lies on a thing ----------

    /// <summary>What rests on a placed thing, and on what rests on it (Item::getSurfaceWeight's list), without the thing itself.</summary>
    private List<object> ThingsOn(object thing, List<CharacterDefinition> people)
    {
        var found = new List<object>();
        var world = WorldNow();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance) { thing };
        void Above(object entity)
        {
            var body = BodyOf(entity as PlacedItemInstance, entity as ObjectInstance);
            var rect = new UnitRect(body.Left, body.Top, body.Width, body.Depth);
            foreach (var other in world.Overlapping(rect))
            {
                if (other.Entity is not (ObjectInstance or PlacedItemInstance) || other.Z != body.TopLift || !seen.Add(other.Entity)) continue; // things only: not a wall's slab
                found.Add(other.Entity);
                Above(other.Entity);
            }
            foreach (var who in AllLivingCharacters().ToList())
                if (LiftOf(who) == body.TopLift && body.Overlaps(FootOf(who)) && !people.Contains(who)) people.Add(who);
        }
        Above(thing);
        return found;
    }

    /// <summary>All that weighs on a placed thing: objects and containers on it, and whoever stands on it (their Body weight).</summary>
    private double WeightOn(object thing)
    {
        var people = new List<CharacterDefinition>();
        double total = 0;
        foreach (var on in ThingsOn(thing, people))
            total += on switch
            {
                ObjectInstance o => (DefinitionOf(o)?.Weight ?? 0) * Math.Max(1, o.Quantity),
                PlacedItemInstance { Category: ReferencedItemCategory.Container } i => ContainerDef(i.ItemId)?.Weight ?? 0,
                _ => 0
            };
        return total + people.Sum(p => p.Body.Weight);
    }
}
