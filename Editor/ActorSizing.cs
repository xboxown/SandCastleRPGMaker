using System.IO;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A character's Exult-style 3D size (footprint X x Y in 8px world units,
/// height Z in lifts). In Exult every shape's size is data; here a
/// character's SizeX/SizeY/SizeZ are that data, and 0 means "work it out
/// from the Idle sprite" so nothing has to be drawn or typed by hand.
///
/// The automatic estimate follows Exult's own projection: a shape is drawn
/// over its footprint, and every lift of height draws 4 pixels further up
/// (drag.cc drop_at_lift: "x += at_lift * 4"). So a sprite H pixels tall
/// standing on a footprint D units deep is about (H - D*8) / 4 lifts tall.
/// The footprint is taken as half the sprite's width - a standing figure's
/// feet cover roughly the middle half of its drawn width. These are only
/// starting values, editable in the PC Editor.
/// </summary>
public static class ActorSizing
{
    /// <summary>Used when there is no Idle sprite to measure - one unit square, a typical upright figure's height.</summary>
    public static readonly (int X, int Y, int Z) Fallback = (1, 1, 4);

    /// <summary>The size the engine should use: the character's own values, with any 0 filled in automatically.</summary>
    public static (int X, int Y, int Z) Resolve(CharacterDefinition character, PoseDatabase poses, SpriteDatabase sprites)
    {
        if (character.SizeX > 0 && character.SizeY > 0 && character.SizeZ > 0) return (character.SizeX, character.SizeY, character.SizeZ);
        var auto = ComputeAutomatic(character, poses, sprites);
        return (character.SizeX > 0 ? character.SizeX : auto.X,
                character.SizeY > 0 ? character.SizeY : auto.Y,
                character.SizeZ > 0 ? character.SizeZ : auto.Z);
    }

    public static (int X, int Y, int Z) ComputeAutomatic(CharacterDefinition character, PoseDatabase poses, SpriteDatabase sprites)
    {
        var idle = poses.Poses.FirstOrDefault(p => p.Name == "Idle");
        if (idle is null || !character.PoseSprites.TryGetValue(idle.Id, out var assignment)) return Fallback;
        var link = assignment.Resolve(SpriteDirection.South);
        var sprite = link is null ? null : sprites.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName);
        if (sprite is null || sprite.Frames.Count == 0) return Fallback;
        var size = ReadPixelSize(sprite.Frames[0].FilePath);
        if (size is null) return Fallback;
        var (width, height) = SpriteSizing.Quantize(size.Value.Width, size.Value.Height);
        return FromSpritePixels(width, height);
    }

    public static (int X, int Y, int Z) FromSpritePixels(int width, int height)
    {
        int footprint = Math.Max(1, (int)Math.Round(width / (2.0 * SpriteSizing.BaseUnit)));
        int lifts = Math.Max(1, (int)Math.Round((height - footprint * SpriteSizing.BaseUnit) / 4.0));
        return (footprint, footprint, lifts);
    }

    private static (int Width, int Height)? ReadPixelSize(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch
        {
            return null;
        }
    }
}
