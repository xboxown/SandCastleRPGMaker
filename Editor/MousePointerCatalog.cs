using System.IO;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Shared facts about the engine's mouse pointers, used by the System Settings window and by Game Test alike.</summary>
public static class MousePointerCatalog
{
    /// <summary>Every pointer, in the order shown in settings, with when Exult shows it.</summary>
    public static readonly (MousePointerKind Kind, string Name, string When)[] All =
    {
        (MousePointerKind.Hand, "Hand", "The normal pointer - always shown, and while dragging."),
        (MousePointerKind.RedX, "Red X", "Flashed when something is refused: it can't be put there, can't go inside itself, can't be grabbed."),
        (MousePointerKind.GreenSelect, "Green Select", "Choosing a target (e.g. what to use an item on)."),
        (MousePointerKind.TooHeavy, "Too Heavy", "Flashed when something is too heavy to carry or can't be moved."),
        (MousePointerKind.OutOfRange, "Out of Range", "Flashed when something is out of reach or out of sight."),
        (MousePointerKind.OutOfAmmo, "Out of Ammo", "Flashed when a weapon has no ammunition."),
        (MousePointerKind.WontFit, "Won't Fit", "Flashed when something won't fit (a full or locked container, full hands)."),
        (MousePointerKind.GreenSquare, "Green Square", "The green square marker."),
        (MousePointerKind.Blocked, "Blocked", "Flashed when a drop is blocked (too high to reach from where you stand)."),
        (MousePointerKind.Attack, "Attack", "Shown instead of the hand while in combat mode (C), so you know a double-click attacks (Exult shows its red combat arrows)."),
    };

    /// <summary>
    /// Ultima 8 projects: the direction arrows, as Pentagram's mouse (Mouse::getMouseFrame) - an arrow from the Avatar toward
    /// the pointer: while walking with the right mouse button, short (a step), medium (a walk) or long (a run) by how far the
    /// pointer is; in combat mode the combat arrows (not while running). Step and Run may be left empty: Walk's arrows then.
    /// </summary>
    public static readonly (string Set, string Name, string When)[] U8ArrowSets =
    {
        ("Walk", "Walking", "While walking with the right mouse button held: the arrow points the way the Avatar goes."),
        ("Step", "Stepping (optional)", "The pointer close to the Avatar - a step (Pentagram's short arrows). Empty: the walking arrows."),
        ("Run", "Running (optional)", "The pointer far from the Avatar - a run (Pentagram's long arrows). Empty: the walking arrows."),
        ("Combat", "Combat mode", "In combat mode (C), always - pointing from the Avatar to the pointer (Pentagram's red combat arrows). Empty: the walking arrows."),
    };

    /// <summary>The eight directions an arrow points, as on the screen (North: up the screen).</summary>
    public static readonly SpriteDirection[] ArrowDirections =
        { SpriteDirection.North, SpriteDirection.NorthEast, SpriteDirection.East, SpriteDirection.SouthEast, SpriteDirection.South, SpriteDirection.SouthWest, SpriteDirection.West, SpriteDirection.NorthWest };

    public static string ArrowKey(string set, SpriteDirection direction) => $"{set}.{direction}";

    public static string ArrowLabel(SpriteDirection direction) => direction switch
    {
        SpriteDirection.North => "↑ Up", SpriteDirection.NorthEast => "↗ Up-right", SpriteDirection.East => "→ Right", SpriteDirection.SouthEast => "↘ Down-right",
        SpriteDirection.South => "↓ Down", SpriteDirection.SouthWest => "↙ Down-left", SpriteDirection.West => "← Left", _ => "↖ Up-left"
    };

    /// <summary>A refusal's own pointer (Too Heavy, Out of Range...): in an Ultima 8 project, with no sprite of its own, the X shows instead.</summary>
    public static bool IsRefusal(MousePointerKind kind) =>
        kind is MousePointerKind.TooHeavy or MousePointerKind.OutOfRange or MousePointerKind.OutOfAmmo or MousePointerKind.WontFit or MousePointerKind.Blocked;

    /// <summary>Exult (mouse.cc Mouse::flash_shape): a flashed pointer shows for 600 ms, then the previous pointer returns.</summary>
    public const int FlashMilliseconds = 600;

    public static SpriteEntry? Resolve(SpriteDatabase sprites, MousePointerSprite? link)
    {
        if (link is null || string.IsNullOrEmpty(link.SpriteName)) return null;
        return sprites.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName)
            ?? sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == link.SpriteName);
    }

    /// <summary>One line describing a sprite's own animation and sound, exactly as set in the Sprite Editor.</summary>
    public static string Describe(SpriteEntry sprite, ResourceDatabase resources)
    {
        if (sprite.Frames.Count <= 1)
        {
            int still = sprite.Frames.Count == 1 ? sprite.Frames[0].SoundResourceId : -1;
            return still >= 0 ? $"1 frame (still) - sound: {SoundName(resources, still)}" : "1 frame (still) - no sound";
        }
        var sounds = sprite.Frames.Select((f, i) => (f.SoundResourceId, Frame: i + 1)).Where(t => t.SoundResourceId >= 0)
            .Select(t => $"frame {t.Frame}: {SoundName(resources, t.SoundResourceId)}").ToList();
        return $"{sprite.Frames.Count} frames - {sprite.Fps} fps - {(sprite.Loop ? "looping" : "plays once")} - "
            + (sounds.Count == 0 ? "no sound" : "sound on " + string.Join(", ", sounds));
    }

    private static string SoundName(ResourceDatabase resources, int id) => resources.Entries.FirstOrDefault(r => r.Id == id)?.Name ?? $"#{id}";

    /// <summary>
    /// Plays a sprite's first frame's own sound right now. SpriteAnimator
    /// deliberately doesn't play frame 1's sound when an animation is first
    /// built (only when it loops back round), so without this a one-frame
    /// pointer with a sound on its frame would never be heard when flashed.
    /// </summary>
    public static void PlayFirstFrameSound(SpriteEntry sprite, ResourceDatabase resources)
    {
        if (sprite.Frames.Count > 0 && sprite.Frames[0].SoundResourceId >= 0)
            SoundPlayback.TryPlay(resources, sprite.Frames[0].SoundResourceId);
    }

    /// <summary>The pixel size of a sprite's first frame (or its single image, for a plain static sprite), read from the file header only.</summary>
    public static (int Width, int Height)? PixelSize(SpriteEntry sprite)
    {
        string path = sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath;
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
