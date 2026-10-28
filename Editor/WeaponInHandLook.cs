using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How a weapon in the hand is chosen and turned (Exult's Actor::paint_weapon, figure_weapon_pos) - shared by the game
/// windows and the Sprite Editor's weapon preview, so the preview shows exactly what the game draws.
/// </summary>
internal static class WeaponInHandLook
{
    /// <summary>
    /// Which weapon frame a character frame shows, and whether mirrored. Its own choice, else: Exult's - held, or through
    /// an attack the swing pointing south, east, north (figure_weapon_pos: 4, 3, 2), mirrored facing east or west;
    /// smooth - the direction's held frame, or its swing frame for this point of the attack.
    /// </summary>
    public static (int Frame, bool Mirrored) FrameFor(WeaponInHandSettings settings, SpriteFrame frame, int index, int count, SpriteDirection facing, bool attacking)
    {
        if (!settings.Smooth)
        {
            bool mirrored = facing is SpriteDirection.East or SpriteDirection.West;
            if (frame.WeaponFrame >= 0) return (frame.WeaponFrame, mirrored);
            if (!attacking) return (0, mirrored);
            int phase = Math.Min(2, index * 3 / Math.Max(1, count));
            return (3 - phase, mirrored);
        }
        if (frame.WeaponFrame >= 0) return (frame.WeaponFrame, false);
        int swing = Math.Clamp(settings.SwingFrames, 1, 8), first = (int)facing * (1 + swing);
        if (!attacking) return (first, false);
        return (first + 1 + Math.Min(swing - 1, index * swing / Math.Max(1, count)), false);
    }

    /// <summary>Exult's reflected frames swap x and y (about the grip): a character facing east or west.</summary>
    public static Transform MirrorAboutGrip(double gripX, double gripY) => new MatrixTransform(0, 1, 1, 0, gripX - gripY, gripY - gripX);

    /// <summary>
    /// Where the weapon's drawn pixels land in a character frame, in that frame's own pixels (the game scales the body and
    /// the weapon alike, so this holds at any size) - null: no hand point or no picture. The Sprite Editor warns when it
    /// reaches out of the frame.
    /// </summary>
    public static Rect? WeaponBounds(WeaponInHandSettings settings, SpriteEntry sprite, int index, SpriteEntry weapon, SpriteDirection facing, bool attacking)
    {
        if (index < 0 || index >= sprite.Frames.Count || weapon.Frames.Count == 0) return null;
        var frame = sprite.Frames[index];
        if (!frame.HasHand) return null;
        var (weaponFrame, mirrored) = FrameFor(settings, frame, index, sprite.Frames.Count, facing, attacking);
        var heldFrame = weapon.Frames[Math.Clamp(weaponFrame, 0, weapon.Frames.Count - 1)];
        if (FrameCache.Get(heldFrame.FilePath) is not { } picture || OpaqueBounds(picture) is not { } drawn) return null;
        double gripX = heldFrame.GripX >= 0 ? heldFrame.GripX : picture.PixelWidth / 2.0;
        double gripY = heldFrame.GripY >= 0 ? heldFrame.GripY : picture.PixelHeight / 2.0;
        if (mirrored) drawn = new Rect(drawn.Y + gripX - gripY, drawn.X + gripY - gripX, drawn.Height, drawn.Width); // x and y swapped about the grip
        return new Rect(frame.HandX - gripX + drawn.X, frame.HandY - gripY + drawn.Y, drawn.Width, drawn.Height);
    }

    private static readonly ConditionalWeakTable<BitmapSource, StrongBox<Rect?>> Opaque = new();

    /// <summary>The box round a picture's pixels that aren't see-through (null: none), worked out once per picture.</summary>
    private static Rect? OpaqueBounds(BitmapSource picture)
    {
        if (Opaque.TryGetValue(picture, out var known)) return known.Value;
        var source = picture.Format == PixelFormats.Bgra32 ? picture : new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (pixels[y * stride + x * 4 + 3] != 0)
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        Rect? bounds = right < 0 ? null : new Rect(left, top, right - left + 1, bottom - top + 1);
        Opaque.AddOrUpdate(picture, new StrongBox<Rect?>(bounds));
        return bounds;
    }
}
