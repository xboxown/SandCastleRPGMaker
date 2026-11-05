// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's world is real (the user's rule): for every character alike, not only the main one.
///  - Real water (Tile Properties > Ultima 8): at the ground (lift 0), over water, a character drowns - it sinks with a splash and is gone,
///    body, belongings and all, never to come back. A thing that comes down in it splashes and is gone for good (in play: never the
///    editor's data). Bigger things, bigger splashes. Anyone higher up - on a platform, a bridge, a boat - is safe.
///  - Lava: a character in it burns (Fire damage, by its own rule for Fire); a thing in it burns away.
///  - Real falls: a character falling from a height is hurt by it, and a high fall kills (GravityProcess::actorFallStoppedU8).
/// Characters other than the main one never walk in by themselves; the main character can, as the player chooses (not a planned walk).
/// Things put in the water in the Map Editor (a bridge's posts, a boat) stay: only what comes down in it in play is lost.
/// </summary>
public partial class U8GameWindow
{
    // ---------- What is under a footprint ----------

    /// <summary>Real water or lava when every part of the footprint is over it (at the ground); else none - half on the shore is on the shore.</summary>
    private U8Liquid LiquidUnder(UnitRect foot)
    {
        var found = U8Liquid.None;
        for (int y = foot.Top; y < foot.Bottom; y++)
            for (int x = foot.Left; x < foot.Right; x++)
            {
                int cellX = x / UnitsPerTileX, cellY = y / UnitsPerTileY;
                if (!InsideMap(cellX, cellY)) return U8Liquid.None;
                int tileIndex = _map.Layers[0].GetTile(cellX, cellY, _map.Width);
                var liquid = tileIndex < 0 ? U8Liquid.None : TilePropertiesOf(tileIndex).U8Liquid;
                if (liquid == U8Liquid.None) return U8Liquid.None;
                if (liquid == U8Liquid.Lava || found == U8Liquid.None) found = liquid;
            }
        return found;
    }

    /// <summary>While the main character's walk is planned (clicked somewhere, an event's walk): water and lava are never on the way.</summary>
    private bool _planningPlayerWalk;

    /// <summary>A step onto liquid at the ground: the main character may (the player chose it); everyone else won't walk in by themselves.</summary>
    private bool LiquidStepAllowed(UnitRect to, object? mover, out bool liquid)
    {
        liquid = LiquidUnder(to) != U8Liquid.None;
        if (!liquid) return true;
        return mover is null && !_planningPlayerWalk;
    }

    // ---------- Each frame ----------

    private sealed class Sinking { public required CharacterDefinition Who; public double LeftMs; }
    private readonly List<Sinking> _sinking = new();
    private readonly Dictionary<CharacterDefinition, double> _lavaBurnMs = new(ReferenceEqualityComparer.Instance);
    private double _liquidCheckMs;
    private List<ObjectInstance>? _liquidObjectsSeen;
    private readonly HashSet<object> _placedInLiquid = new(ReferenceEqualityComparer.Instance);
    private bool _mapHasLiquid;
    private bool _playerVanished;

    private void UpdateLiquids(double elapsedMs)
    {
        UpdateSplashes(elapsedMs);
        foreach (var sinking in _sinking.ToList())
            if ((sinking.LeftMs -= elapsedMs) <= 0) { _sinking.Remove(sinking); Vanish(sinking.Who); }
        if (_playerVanished && _player is not null && !IsDead(_player)) { _playerVanished = false; _playerImage.Visibility = Visibility.Visible; } // raised by an event

        if (!ReferenceEquals(_liquidObjectsSeen, _map.Objects)) NoteWhatWasPlacedInLiquid(); // a new map, or a game loaded
        if (!_mapHasLiquid || (_liquidCheckMs += elapsedMs) < 200) return;
        double seconds = _liquidCheckMs / 1000.0;
        _liquidCheckMs = 0;
        foreach (var who in AllLivingCharacters().ToList()) CharacterInLiquid(who, seconds);
        ThingsInLiquid();
    }

    /// <summary>What the map starts with in water or lava stays there (a bridge's posts, a moored boat): only what comes down in it later is lost.</summary>
    private void NoteWhatWasPlacedInLiquid()
    {
        _liquidObjectsSeen = _map.Objects;
        _placedInLiquid.Clear();
        _mapHasLiquid = _tileProperties.ByTileKey.Values.Any(p => p.U8Liquid != U8Liquid.None);
        if (!_mapHasLiquid) return;
        foreach (var o in _map.Objects) if (o.ContainerInstanceId < 0 && ThingLiquid(o, null) != U8Liquid.None) _placedInLiquid.Add(o);
        foreach (var i in _map.Items) if (ThingLiquid(null, i) != U8Liquid.None) _placedInLiquid.Add(i);
    }

    private U8Liquid ThingLiquid(ObjectInstance? obj, PlacedItemInstance? item)
    {
        var body = BodyOf(item, obj);
        if (body.Z > 0) return U8Liquid.None;
        return LiquidUnder(new UnitRect(body.Left, body.Top, Math.Max(1, body.Width), Math.Max(1, body.Depth)));
    }

    private bool InLiquidSafe(CharacterDefinition who)
    {
        if (IsDead(who) || who.Immortal || _sinking.Any(s => s.Who == who)) return true;
        if (who == _player) return PlayerLevitating || _driving is not null || _jump is not null || _u8Hanging || _playerVisualLiftZ > _playerZ || PlayerSeated;
        if (MapCharacterOf(who) is not { } mapCharacter) return true;
        return IsFlier(mapCharacter) || who.Movement == MovementKind.Sea || _seats.ContainsKey(who);
    }

    private void CharacterInLiquid(CharacterDefinition who, double seconds)
    {
        if (LiftOf(who) > 0 || InLiquidSafe(who)) { _lavaBurnMs.Remove(who); return; }
        var liquid = LiquidUnder(FootOf(who));
        if (liquid == U8Liquid.Water) { StartDrowning(who); return; }
        if (liquid != U8Liquid.Lava) { _lavaBurnMs.Remove(who); return; }
        // Lava: it burns, as often as System Settings > Ultima 8 says, by its own rule for Fire.
        double every = Math.Max(0.1, U8Rules.LavaEverySeconds) * 1000;
        double burned = _lavaBurnMs.GetValueOrDefault(who) + seconds * 1000;
        bool first = !_lavaBurnMs.ContainsKey(who);
        if (first) { var (x, y) = PixelOf(who); Splash(x, y, 0, 2, U8Liquid.Lava); }
        while (burned >= every || first)
        {
            if (!first) burned -= every;
            first = false;
            int damage = AfterDamageResponse(who, Math.Max(1, U8Rules.LavaDamage), DamageType.Fire);
            if (damage > 0) DamageAllowed(who, damage, "burned in lava", DamageType.Fire);
            if (IsDead(who)) break;
            if (HasArtFor(who, "Burn")) ShowPose(who, "Burn", restart: false);
        }
        _lavaBurnMs[who] = burned;
    }

    // ---------- Drowning ----------

    /// <summary>Into the water: a splash, the Drown pose if it has one, and it is gone.</summary>
    private void StartDrowning(CharacterDefinition who)
    {
        var (x, y) = PixelOf(who);
        Splash(x, y, 0, 2.5, U8Liquid.Water);
        double ms = HasArtFor(who, "Drown") ? Math.Max(400, PoseLengthMs(who, "Drown")) : 300;
        if (HasArtFor(who, "Drown")) ShowPose(who, "Drown", restart: true);
        if (who == _player) { _u8StunMs = ms + 100; _heldDirectionKeys.Clear(); CancelPlayerWalk(); }
        else if (MapCharacterOf(who) is { } mapCharacter) { StopWalking(mapCharacter); mapCharacter.Npc.WaitMs = ms + 1000; }
        _sinking.Add(new Sinking { Who = who, LeftMs = ms });
        SoakLog($"drowning: {who.Name}");
    }

    /// <summary>Gone for good: no body, nothing it carried left; its By Death events still run. The main character: the game is over.</summary>
    private CharacterDefinition? _vanishing;

    private void Vanish(CharacterDefinition who)
    {
        if (IsDead(who)) return;
        who.Inventory.Clear();
        who.EquippedSlots.Clear();
        who.CustomEquippedSlots.Clear();
        _vanishing = who;
        try { Die(who, "drowned"); }
        finally { _vanishing = null; }
        if (who == _player) { _playerImage.Visibility = Visibility.Hidden; _playerVanished = true; }
        SoakLog($"drowned: {who.Name} is gone");
    }

    /// <summary>Die's end for one who drowned: out of the world, with no body (called from Die).</summary>
    private bool VanishedInstead(MapCharacter mapCharacter)
    {
        if (!ReferenceEquals(_vanishing, mapCharacter.Character)) return false;
        if (mapCharacter.Visual is { } visual) { UnregisterWorldAnimator(visual); GameCanvas.Children.Remove(visual); }
        _mapCharacters.Remove(mapCharacter);
        mapCharacter.Placement.Dead = true;
        mapCharacter.Placement.BodyInstanceId = -1;
        StatusText.Text = $"{mapCharacter.Character.Name} has drowned.";
        return true;
    }

    // ---------- Things in water and lava ----------

    private void ThingsInLiquid()
    {
        bool gone = false;
        foreach (var o in _map.Objects.ToList())
        {
            if (o.ContainerInstanceId >= 0 || o.Z > 0 || _placedInLiquid.Contains(o) || o.WallSetPiece || _flying.Any(f => ReferenceEquals(f.Instance, o))) continue;
            if (_pointer is { IsDragging: true } && ReferenceEquals(DraggedEntity(), o)) continue;
            var liquid = ThingLiquid(o, null);
            if (liquid == U8Liquid.None) continue;
            var def = DefinitionOf(o);
            var body = BodyOf(null, o);
            SplashOver(body, def?.Weight ?? 1, liquid);
            RemoveMapObject(o);
            gone = true;
            SoakLog($"{(liquid == U8Liquid.Water ? "sank" : "burned up")}: {def?.Name}");
        }
        foreach (var i in _map.Items.ToList())
        {
            if (i.Z > 0 || _placedInLiquid.Contains(i)) continue;
            if (_pointer is { IsDragging: true } && ReferenceEquals(DraggedEntity(), i)) continue;
            var liquid = ThingLiquid(null, i);
            if (liquid == U8Liquid.None) continue;
            SplashOver(BodyOf(i, null), i.Category == ReferencedItemCategory.Container ? ContainerDef(i.ItemId)?.Weight ?? 5 : 1, liquid);
            foreach (var gump in _openContainers.Where(g => ReferenceEquals(g.PlacedItem, i)).ToList()) CloseContainerGump(gump);
            _map.Items.Remove(i);
            if (_itemVisuals.Remove(i, out var visual)) { GameCanvas.Children.Remove(visual); UnregisterWorldAnimator(visual); }
            gone = true;
            SoakLog($"{(liquid == U8Liquid.Water ? "sank" : "burned up")}: item {i.Category} {i.ItemId}");
        }
        if (gone) { SettleWorld(); RefreshAfterMove(); }
    }

    private object? DraggedEntity() => _pointer?.Origin switch { MapObjectOrigin m => m.Obj, MapItemOrigin i => i.Item, _ => null };

    /// <summary>A splash the size of the thing: its footprint and height, and its weight.</summary>
    private void SplashOver(Body body, double weight, U8Liquid liquid)
    {
        double size = Math.Clamp(Math.Sqrt(Math.Max(1, body.Width * body.Depth * Math.Max(1, body.Height))) / 2.0 + weight / 40.0, 0.5, 4);
        double x = (body.Left + body.Width / 2.0) * SpriteSizing.BaseUnit, y = (body.Top + body.Depth / 2.0) * SpriteSizing.BaseUnit;
        Splash(x, y, 0, size, liquid);
    }

    // ---------- Splashes ----------

    private sealed class SplashFx { public required FrameworkElement Picture; public double LeftMs, TotalMs; public List<(Ellipse Drop, double VX, double VY, double VZ)>? Drops; public double X, Y, Size; }
    private readonly List<SplashFx> _splashes = new();

    /// <summary>A splash (or lava's flare) there, as big as size: the sprite System Settings > Ultima 8 names, scaled; none named, drops of water (or fire)
    /// thrown up and a ring. Its sound: the big one from size 2 up, when there is one.</summary>
    private void Splash(double x, double y, int lift, double size, U8Liquid liquid)
    {
        bool lava = liquid == U8Liquid.Lava;
        int sound = lava ? U8Rules.LavaSoundId : size >= 2 && U8Rules.BigSplashSoundId >= 0 ? U8Rules.BigSplashSoundId : U8Rules.SplashSoundId;
        if (sound >= 0) PlaySound(sound);
        var link = lava ? U8Rules.LavaSprite : U8Rules.SplashSprite;
        var chosen = link.IsSet ? ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName)
            : _sprites.Groups.FirstOrDefault(g => g.Name == (lava ? SpriteDatabase.LavaFlareGroupName : SpriteDatabase.WaterSplashGroupName))?.Sprites.FirstOrDefault(s => s.Frames.Count > 0); // the locked group's first
        if (chosen is { Frames.Count: > 0 } sprite)
        {
            var (width, height) = QuantizedSpriteSize(sprite);
            var image = new Image { Width = width * size, Height = height * size, IsHitTestVisible = false, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var animator = new SpriteAnimator(sprite, image, _resources);
            RegisterWorldAnimator(image, animator);
            Panel.SetZIndex(image, AboveEverythingZIndex - 3);
            GameCanvas.Children.Add(image);
            PlaceEffect(image, (x, y + image.Height / 2 - _tileHeight / 2.0), lift);
            double total = Math.Max(200, sprite.Frames.Sum(f => Math.Max(1, f.DurationMs)));
            _splashes.Add(new SplashFx { Picture = image, LeftMs = total, TotalMs = total, X = x, Y = y, Size = size });
            return;
        }
        // No sprite: a ring, and drops thrown up that fall back.
        var color = lava ? Color.FromRgb(255, 120, 20) : Color.FromRgb(170, 210, 255);
        var ring = new Ellipse { Width = 8 * size, Height = 4 * size, Stroke = new SolidColorBrush(color), StrokeThickness = 1, IsHitTestVisible = false };
        Panel.SetZIndex(ring, AboveEverythingZIndex - 3);
        U8ProjectionCanvas.SetPlacement(ring, U8Placement.Center);
        GameCanvas.Children.Add(ring);
        var drops = new List<(Ellipse, double, double, double)>();
        int count = (int)Math.Round(4 + 4 * size);
        for (int i = 0; i < count; i++)
        {
            var drop = new Ellipse { Width = 2, Height = 2, Fill = new SolidColorBrush(color), IsHitTestVisible = false };
            Panel.SetZIndex(drop, AboveEverythingZIndex - 2);
            U8ProjectionCanvas.SetPlacement(drop, U8Placement.Center);
            GameCanvas.Children.Add(drop);
            double angle = Rng.NextDouble() * 2 * Math.PI, speed = 10 + Rng.Next(20) * Math.Sqrt(size);
            drops.Add((drop, Math.Cos(angle) * speed, Math.Sin(angle) * speed * 0.5, 40 + Rng.Next(40) * Math.Sqrt(size)));
        }
        _splashes.Add(new SplashFx { Picture = ring, LeftMs = 700, TotalMs = 700, Drops = drops, X = x, Y = y, Size = size });
        UpdateSplash(_splashes[^1], 0);
    }

    private void UpdateSplashes(double elapsedMs)
    {
        foreach (var fx in _splashes.ToList())
        {
            fx.LeftMs -= elapsedMs;
            if (fx.LeftMs > 0) { UpdateSplash(fx, elapsedMs); continue; }
            _splashes.Remove(fx);
            if (fx.Picture is Image image) UnregisterWorldAnimator(image);
            GameCanvas.Children.Remove(fx.Picture);
            if (fx.Drops is { } drops) foreach (var d in drops) GameCanvas.Children.Remove(d.Drop);
        }
    }

    private void UpdateSplash(SplashFx fx, double elapsedMs)
    {
        if (fx.Drops is null) return;
        double t = (fx.TotalMs - fx.LeftMs) / 1000.0, grow = 1 + 2 * t;
        fx.Picture.Width = 8 * fx.Size * grow;
        fx.Picture.Height = 4 * fx.Size * grow;
        fx.Picture.Opacity = Math.Max(0, fx.LeftMs / fx.TotalMs);
        Canvas.SetLeft(fx.Picture, fx.X - fx.Picture.Width / 2);
        Canvas.SetTop(fx.Picture, fx.Y - fx.Picture.Height / 2);
        foreach (var (drop, vx, vy, vz) in fx.Drops)
        {
            double up = Math.Max(0, vz * t - 120 * t * t);
            Canvas.SetLeft(drop, fx.X + vx * t - up);
            Canvas.SetTop(drop, fx.Y + vy * t - up);
            drop.Opacity = fx.Picture.Opacity;
        }
    }

    // ---------- Real falls, for every character ----------

    /// <summary>Map_chunk::gravity for everyone else: what they stood on is gone, they fall onto what's left under them.</summary>
    private void ApplyGravityToCharacters()
    {
        foreach (var mapCharacter in _mapCharacters.ToList())
        {
            if (IsDead(mapCharacter.Character) || IsFlier(mapCharacter) || _seats.ContainsKey(mapCharacter.Character)) continue;
            int ground = GroundBelow(mapCharacter);
            if (ground >= mapCharacter.Lift) continue;
            int from = mapCharacter.Lift;
            StopWalking(mapCharacter);
            mapCharacter.Lift = ground;
            PositionCharacterVisual(mapCharacter);
            if (from - ground > ActorMaxDrop) CharacterFell(mapCharacter, from, ground);
        }
    }

    /// <summary>GravityProcess::actorFallStoppedU8 for any character: 80 units or more (10 lifts) hurts, (height - 72) / 4; 104 or more (13 lifts) kills.
    /// The main character's own falls go through LandU8; this is everyone else's.</summary>
    private void CharacterFell(MapCharacter mapCharacter, int fromLift, int toLift)
    {
        var who = mapCharacter.Character;
        int height = (fromLift - toLift) * U8UnitsPerLift;
        SoakLog($"fell: {who.Name} {fromLift - toLift} lifts");
        if (height >= 80)
        {
            int damage = AfterDamageResponse(who, Math.Max(1, height < 104 ? (height - 72) / 4 : 100000), DamageType.Falling);
            if (damage > 0) { _specialWound = true; try { Damage(who, damage, "a fall"); } finally { _specialWound = false; } }
        }
        if (!IsDead(who) && !IsIncapacitated(who) && HasArtFor(who, "Land")) { ShowPose(who, "Land", restart: true); mapCharacter.Npc.WaitMs = Math.Max(mapCharacter.Npc.WaitMs, PoseLengthMs(who, "Land")); }
    }
}
