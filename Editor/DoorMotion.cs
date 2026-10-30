using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How a door looks as it opens and closes (DoorOpening) - shared by the game windows and the Object Editor's preview, so the
/// preview moves exactly as the game does. Swing: the open sprite at its hinge, at once (Ultima VII). Animation: an opening (or
/// closing) animation played once where the closed door is. Slide: the closed door glides by its slide, hidden past where it
/// was drawn when it slides into the wall. Fade: the closed door fades out (and back in), leaving the open sprite, if any.
/// </summary>
internal static class DoorMotion
{
    /// <summary>Whether it moves at all (a swinging door changes at once).</summary>
    public static bool Moves(DoorSettings d) => d.Opening != DoorOpening.Swing;

    /// <summary>Gentle at both ends.</summary>
    public static double Ease(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

    /// <summary>The picture played or moved while it opens (or closes), and whether an animation plays backwards: null - nothing to play.</summary>
    public static (SpriteEntry Sprite, bool Backwards)? MovingPicture(WorldObject door, bool opening, SpriteDatabase sprites)
    {
        var d = door.Door;
        switch (d.Opening)
        {
            case DoorOpening.Animation:
                if (!opening && ObjectLooks.Find(sprites, d.ClosingSprite.SpriteGroup, d.ClosingSprite.SpriteName) is { Frames.Count: > 0 } closing) return (closing, false);
                return ObjectLooks.Find(sprites, d.OpeningSprite.SpriteGroup, d.OpeningSprite.SpriteName) is { Frames.Count: > 0 } anim ? (anim, !opening) : null;
            case DoorOpening.Slide:
            case DoorOpening.Fade:
                return ObjectLooks.Find(sprites, door.SpriteGroup, door.SpriteName) is { Frames.Count: > 0 } closed ? (closed, false) : null;
            default:
                return null;
        }
    }

    /// <summary>How long it takes: an animation's frames once through, else the door's own time.</summary>
    public static double DurationMs(DoorSettings d, SpriteEntry picture) =>
        d.Opening == DoorOpening.Animation ? Math.Max(1, picture.Frames.Sum(f => Math.Max(1, f.DurationMs))) : Math.Max(50, d.MotionMs);

    /// <summary>An animation's frame this far into it (backwards: from its last frame).</summary>
    public static int FrameAt(SpriteEntry picture, double elapsedMs, bool backwards)
    {
        int count = picture.Frames.Count, index = count - 1;
        double t = 0;
        for (int i = 0; i < count; i++)
        {
            t += Math.Max(1, picture.Frames[i].DurationMs);
            if (elapsedMs < t) { index = i; break; }
        }
        return backwards ? count - 1 - index : index;
    }

    /// <summary>
    /// The moving picture, this far open (0 closed, 1 open): slid by that much of the slide (still hidden past where the closed
    /// door is drawn, when it slides into the wall), or faded by that much. An animation shows its frame (FrameAt) instead.
    /// </summary>
    public static void Apply(FrameworkElement picture, DoorSettings d, double open)
    {
        switch (d.Opening)
        {
            case DoorOpening.Slide:
                double dx = d.SlideX * open, dy = d.SlideY * open;
                picture.RenderTransform = new TranslateTransform(dx, dy);
                // The clip moves with the picture: set back by the slide, it stays where the closed door is.
                double w = double.IsNaN(picture.Width) ? picture.ActualWidth : picture.Width, h = double.IsNaN(picture.Height) ? picture.ActualHeight : picture.Height;
                picture.Clip = d.SlideHidden ? new RectangleGeometry(new Rect(-dx, -dy, w, h)) : null;
                break;
            case DoorOpening.Fade:
                picture.Opacity = 1 - open;
                break;
        }
    }

    /// <summary>
    /// An open door's picture where it stays (the game's ShiftOpenDoor): a swinging (or animated) door's open sprite at its
    /// hinge, a slid door slid all the way, a faded door gone - unless it has an open sprite, which shows as it is.
    /// </summary>
    public static void ApplyOpen(FrameworkElement visual, DoorSettings d)
    {
        switch (d.Opening)
        {
            case DoorOpening.Swing:
            case DoorOpening.Animation:
                Canvas.SetLeft(visual, Canvas.GetLeft(visual) + d.OpenOffsetX);
                Canvas.SetTop(visual, Canvas.GetTop(visual) + d.OpenOffsetY);
                break;
            case DoorOpening.Slide:
                Apply(visual, d, 1);
                break;
            case DoorOpening.Fade:
                if (!d.OpenSprite.IsSet) visual.Opacity = 0;
                break;
        }
    }
}
