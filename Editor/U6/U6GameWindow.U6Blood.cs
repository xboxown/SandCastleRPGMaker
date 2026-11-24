// Ultima 6 engine (U6GameWindow): the blood a death leaves - Ultima 6, The Savage Empire and Martian Dreams (not Ultima 7's bleeding).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6 / Savage Empire / Martian Dreams: a character that dies lies in its sleeping look with blood under it. The blood
/// is the one sprite of the Sprite Editor's Blood folder (a random one of its frames); it can be looked at ("blood") but
/// not taken or moved - the body can - and it is gone once it is out of view. No bleeding from blows (that is Ultima 7's).
/// </summary>
public partial class U6GameWindow
{
    /// <summary>The Blood folder's one sprite (an Ultima 6 project has only it).</summary>
    private SpriteEntry? U6BloodSprite =>
        _sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.BloodGroupName)?.Sprites is { } list
            ? list.FirstOrDefault(s => s.Name == SpriteDatabase.U6BloodSpriteName && s.Frames.Count > 0) ?? list.FirstOrDefault(s => s.Frames.Count > 0)
            : null;

    /// <summary>Blood where someone died: under the body, a random frame of the Blood sprite.</summary>
    private void DropDeathBlood(int cellX, int cellY, int lift)
    {
        if (U6BloodSprite is not { } blood) return;
        int ux = (int)Math.Floor((cellX * _tileWidth + _tileWidth / 2.0) / SpriteSizing.BaseUnit);
        int uy = (int)Math.Floor((cellY * _tileHeight + _tileHeight / 2.0) / SpriteSizing.BaseUnit);
        var puddle = new BloodPuddle
        {
            UnitX = ux, UnitY = uy, Lift = lift, Blood = blood, Death = true,
            Frame = Rng.Next(blood.Frames.Count),
            Visual = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill, RenderTransformOrigin = new Point(0.5, 0.5) }
        };
        RenderOptions.SetBitmapScalingMode(puddle.Visual, BitmapScalingMode.NearestNeighbor);
        _puddles.Add(puddle);
        GameCanvas.Children.Add(puddle.Visual);
        ShowPuddle(puddle);
    }

    /// <summary>Death's blood on this cell (Look names it when nothing else is there).</summary>
    private bool DeathBloodAt(int cx, int cy) =>
        _puddles.Any(p => p.Death && (int)Math.Floor(p.UnitX * SpriteSizing.BaseUnit / (double)_tileWidth) == cx && (int)Math.Floor(p.UnitY * SpriteSizing.BaseUnit / (double)_tileHeight) == cy);

    /// <summary>Death's blood out of view is gone (the sweep, every 2 seconds).</summary>
    private void SweepDeathBlood()
    {
        foreach (var puddle in _puddles.Where(p => p.Death).ToList())
        {
            double x = puddle.UnitX * SpriteSizing.BaseUnit, y = puddle.UnitY * SpriteSizing.BaseUnit;
            bool seen = x >= _cameraX - _tileWidth && x <= _cameraX + ViewportWidth + _tileWidth && y >= _cameraY - _tileHeight && y <= _cameraY + ViewportHeight + _tileHeight;
            if (seen) continue;
            GameCanvas.Children.Remove(puddle.Visual);
            _puddles.Remove(puddle);
        }
    }

    /// <summary>A body's look: its sleeping pose (Ultima 6), else its Body pose.</summary>
    private string? U6BodySpriteKey(CharacterDefinition character, SpriteDirection facing)
    {
        ResolveCharacterSprite(character, PoseByName("Sleeping"), facing, out _, out string? sleeping);
        if (sleeping is not null) return sleeping;
        ResolveCharacterSprite(character, PoseByName("Body"), facing, out _, out string? body);
        return body;
    }
}
