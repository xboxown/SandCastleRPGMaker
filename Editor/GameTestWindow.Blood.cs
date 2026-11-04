using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Bleeding, ported from Exult (actors.cc Actor::reduce_health / bleed,
/// npctime.cc Npc_bleed_timer, actors.cc set_property / die):
///  - A blow that lands (normal damage) on a character with a blood type
///    starts it bleeding when rand() % 10 &lt; damage, unless there are already
///    2 or more puddles within 1 tile of it.
///  - Starting to bleed drips at once; then every 500-1000 ms for 60-180 s.
///    Healing (health going up) or dying stops it.
///  - A drip: if the tile it stands on already has a puddle, that puddle grows
///    one size (frames 1-4), until the largest. Otherwise a new puddle of
///    size 1 or 2. Each change picks mirrored or not at random (Exult's
///    frame | 32).
///  - Puddles are temporary objects: Exult drops them when their part of the
///    map leaves memory - here, once they're more than two screens away.
/// The blood type is the character's (Character Editor > Health), a sprite in
/// the Sprite Editor's Blood group. A character with none never bleeds.
/// </summary>
public partial class GameTestWindow
{
    private sealed class Bleeding
    {
        public double RemainingMs, NextDripMs;
        public int LastHp;
    }

    private sealed class BloodPuddle
    {
        public required int UnitX, UnitY, Lift;
        public required SpriteEntry Blood;
        public int Frame;
        public bool Mirrored;
        public required Image Visual;
        /// <summary>A trail's: HP it takes from whoever steps on it (0: none), and the kind it never hurts.</summary>
        public int Hurts;
        public int MadeByKind = -1;
    }

    private readonly Dictionary<CharacterDefinition, Bleeding> _bleeding = new();
    private readonly List<BloodPuddle> _puddles = new();
    private readonly Dictionary<string, BitmapImage?> _bloodFrames = new();
    private double _puddleSweepMs;

    private SpriteEntry? BloodOf(CharacterDefinition character) =>
        _sprites.FindBlood(character.BloodSpriteName) is { Frames.Count: > 0 } blood ? blood : null;

    /// <summary>The 8px world unit under the character's feet.</summary>
    private (int X, int Y) FeetUnit(CharacterDefinition character)
    {
        var (x, y) = PixelOf(character);
        return ((int)Math.Floor(x / SpriteSizing.BaseUnit), (int)Math.Floor((y + _tileHeight / 2.0 - 1) / SpriteSizing.BaseUnit));
    }

    /// <summary>reduce_health: a blow of <paramref name="damage"/> may start the character bleeding.</summary>
    private void MaybeStartBleeding(CharacterDefinition character, int damage)
    {
        if (BloodOf(character) is null || IsDead(character)) return;
        var (ux, uy) = FeetUnit(character);
        int nearby = _puddles.Count(p => Math.Abs(p.UnitX - ux) <= 1 && Math.Abs(p.UnitY - uy) <= 1);
        if (Rng.Next(10) < damage && nearby < 2) StartBleeding(character);
    }

    /// <summary>Npc_timer_list::start_bleeding: drips now, then for 1-3 minutes.</summary>
    private void StartBleeding(CharacterDefinition character)
    {
        _bleeding[character] = new Bleeding
        {
            RemainingMs = 60000 + Rng.Next(120000),
            NextDripMs = 500 + Rng.Next(500),
            LastHp = GetHp(character) ?? 0
        };
        Bleed(character);
    }

    /// <summary>Npc_bleed_timer::handle_event, for everyone bleeding; and the temporary puddles' clean-up.</summary>
    private void UpdateBleeding(double elapsedMs)
    {
        foreach (var (character, state) in _bleeding.ToList())
        {
            int hp = GetHp(character) ?? 0;
            state.RemainingMs -= elapsedMs;
            if (IsDead(character) || hp > state.LastHp || state.RemainingMs <= 0) { _bleeding.Remove(character); continue; } // dead, healed, or long enough
            state.LastHp = hp;
            state.NextDripMs -= elapsedMs;
            if (state.NextDripMs > 0) continue;
            state.NextDripMs = 500 + Rng.Next(500);
            Bleed(character);
        }

        if ((_puddleSweepMs -= elapsedMs) > 0) return;
        _puddleSweepMs = 2000;
        double cx = _cameraX + ViewportWidth / 2.0, cy = _cameraY + ViewportHeight / 2.0;
        foreach (var puddle in _puddles.Where(p => Math.Abs(p.UnitX * SpriteSizing.BaseUnit - cx) > 2.5 * ViewportWidth
                                                || Math.Abs(p.UnitY * SpriteSizing.BaseUnit - cy) > 2.5 * ViewportHeight).ToList())
        {
            GameCanvas.Children.Remove(puddle.Visual);
            _puddles.Remove(puddle);
        }
    }

    /// <summary>Actor::bleed: grow the puddle on this tile, or start a new small one.</summary>
    private void Bleed(CharacterDefinition character)
    {
        if (BloodOf(character) is not { } blood) return;
        var (ux, uy) = FeetUnit(character);
        DropPuddle(blood, ux, uy, LiftOf(character));
    }

    /// <summary>Actor::bleed(first_frame, last_frame, loc): grow this kind of puddle on this spot, or start a new small one.</summary>
    private void DropPuddle(SpriteEntry blood, int ux, int uy, int lift, int hurts = 0, int madeByKind = -1)
    {
        int last = Math.Min(SpriteDatabase.BloodSizes, blood.Frames.Count) - 1;
        if (_puddles.FirstOrDefault(p => p.UnitX == ux && p.UnitY == uy && p.Lift == lift && p.Blood == blood) is { } puddle)
        {
            if (puddle.Frame < last)
            {
                puddle.Frame++;
                puddle.Mirrored = Rng.Next(2) == 1;
                ShowPuddle(puddle);
            }
            return;
        }
        var fresh = new BloodPuddle
        {
            UnitX = ux, UnitY = uy, Lift = lift, Blood = blood, Hurts = hurts, MadeByKind = madeByKind,
            Frame = Rng.Next(Math.Min(2, last + 1)), // "Lets start with a smaller pool."
            Mirrored = Rng.Next(2) == 1,
            Visual = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill, RenderTransformOrigin = new Point(0.5, 0.5) }
        };
        RenderOptions.SetBitmapScalingMode(fresh.Visual, BitmapScalingMode.NearestNeighbor);
        _puddles.Add(fresh);
        GameCanvas.Children.Add(fresh.Visual);
        ShowPuddle(fresh);
    }

    /// <summary>Draws a puddle at its size, centred on its tile, on the ground under everything standing there.</summary>
    private void ShowPuddle(BloodPuddle puddle)
    {
        string path = puddle.Blood.Frames[Math.Min(puddle.Frame, puddle.Blood.Frames.Count - 1)].FilePath;
        if (!_bloodFrames.TryGetValue(path, out var bitmap)) _bloodFrames[path] = bitmap = LoadStaticFrame(path);
        var image = puddle.Visual;
        image.Source = bitmap;
        image.Width = bitmap?.PixelWidth ?? 0;
        image.Height = bitmap?.PixelHeight ?? 0;
        image.RenderTransform = new ScaleTransform(puddle.Mirrored ? -1 : 1, 1);
        double centerX = puddle.UnitX * SpriteSizing.BaseUnit + SpriteSizing.BaseUnit / 2.0;
        double centerY = puddle.UnitY * SpriteSizing.BaseUnit + SpriteSizing.BaseUnit / 2.0;
        Canvas.SetLeft(image, centerX - image.Width / 2 - puddle.Lift * LiftPixelShift);
        Canvas.SetTop(image, centerY - image.Height / 2 - puddle.Lift * LiftPixelShift);
        // On the ground: just above the map tiles. Up on something: just under whatever stands on that spot.
        Panel.SetZIndex(image, puddle.Lift == 0 ? 1 : ComputeDepthZIndex(centerX / _tileWidth, centerY / _tileHeight, puddle.Lift) - 1);
    }
}
