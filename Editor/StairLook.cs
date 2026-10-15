using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How a designed staircase (Staircase Designer) looks and stands - one rule
/// for the designer, the Map Editor and the game. Its picture is all its
/// steps drawn together, padded so it is anchored on its cell exactly like
/// any object's sprite (centred, bottom edge on the cell's bottom edge):
/// then every step lands where the designer put it.
/// </summary>
internal static class StairLook
{
    /// <summary>The picture's size, and where the anchor cell's top-left corner falls inside it.</summary>
    public readonly record struct Layout(double Width, double Height, double OriginX, double OriginY);

    public static StairDesign? DesignOf(WorldObject? obj, ObjectDatabase objects) =>
        obj is { Type: ObjectType.Stairs, Stairs: { Kind: StairsKind.Stairs, DesignId: >= 0 } }
            ? objects.StairDesigns.FirstOrDefault(d => d.Id == obj.Stairs.DesignId) : null;

    /// <summary>The steps for a placed copy facing this way, or null when the object isn't a designed staircase.</summary>
    public static StairPose? PoseOf(WorldObject? obj, ObjectDatabase objects, SpriteDirection facing) => DesignOf(obj, objects)?.Resolve(facing);

    public static SpriteEntry? SpriteOf(StairStep step, SpriteDatabase sprites) => ObjectLooks.Find(sprites, step.Sprite.SpriteGroup, step.Sprite.SpriteName);

    public static BitmapImage? FirstFrameOf(StairStep step, SpriteDatabase sprites)
    {
        var sprite = SpriteOf(step, sprites);
        if (sprite is null) return null;
        string path = sprite.Frames.Count > 0 ? sprite.Frames[Math.Clamp(step.Frame, 0, sprite.Frames.Count - 1)].FilePath : sprite.FilePath; // a landing's tile: its frame
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>A step's picture size in pixels (a step without a sprite: one tile).</summary>
    public static Size PictureSize(StairStep step, SpriteDatabase sprites, int tileWidth, int tileHeight) =>
        FirstFrameOf(step, sprites) is { } bitmap ? new Size(bitmap.PixelWidth, bitmap.PixelHeight) : new Size(tileWidth, tileHeight);

    public static Layout LayoutOf(StairPose pose, SpriteDatabase sprites, int tileWidth, int tileHeight)
    {
        double minX = 0, maxX = tileWidth, minY = 0;
        foreach (var step in pose.Steps)
        {
            var size = PictureSize(step, sprites, tileWidth, tileHeight);
            minX = Math.Min(minX, step.PictureX);
            maxX = Math.Max(maxX, step.PictureX + size.Width);
            minY = Math.Min(minY, step.PictureY);
        }
        // Centred on the cell (as every sprite is anchored), whole 8px units wide and high.
        double half = Math.Max(tileWidth / 2.0 - minX, maxX - tileWidth / 2.0);
        half = Math.Ceiling(half / (SpriteSizing.BaseUnit / 2.0)) * (SpriteSizing.BaseUnit / 2.0);
        double height = Math.Ceiling((tileHeight - minY) / SpriteSizing.BaseUnit) * SpriteSizing.BaseUnit;
        return new Layout(half * 2, height, half - tileWidth / 2.0, height - tileHeight);
    }

    /// <summary>The steps in paint order: the ones further back first (Exult paints what is behind first), lower before higher.</summary>
    public static IEnumerable<(StairStep Step, int Index)> PaintOrder(StairPose pose) =>
        pose.Steps.Select((step, index) => (step, index))
            .OrderBy(t => t.step.FootY + t.step.SizeY + t.step.FootX + t.step.SizeX)
            .ThenBy(t => t.step.TopLift);

    /// <summary>All the steps as one still picture (Map Editor, Object Editor, palettes).</summary>
    public static BitmapSource? Bitmap(StairPose pose, SpriteDatabase sprites, int tileWidth, int tileHeight)
    {
        if (pose.Steps.Count == 0) return null;
        var layout = LayoutOf(pose, sprites, tileWidth, tileHeight);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            foreach (var (step, _) in PaintOrder(pose))
            {
                if (FirstFrameOf(step, sprites) is not { } frame) continue;
                dc.DrawImage(frame, new Rect(layout.OriginX + step.PictureX, layout.OriginY + step.PictureY, frame.PixelWidth, frame.PixelHeight));
            }
        }
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.NearestNeighbor);
        var bitmap = new RenderTargetBitmap((int)layout.Width, (int)layout.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>All the steps as live pictures (the game): each animates and plays its sounds as any sprite does; each picture's Tag is its step's index.</summary>
    public static Canvas Visual(StairPose pose, SpriteDatabase sprites, ResourceDatabase resources, int tileWidth, int tileHeight, List<SpriteAnimator>? animators)
    {
        var layout = LayoutOf(pose, sprites, tileWidth, tileHeight);
        var canvas = new Canvas { Width = layout.Width, Height = layout.Height, Background = null, IsHitTestVisible = true };
        foreach (var (step, index) in PaintOrder(pose))
        {
            var sprite = SpriteOf(step, sprites);
            var size = PictureSize(step, sprites, tileWidth, tileHeight);
            var image = new AlphaHitImage { Width = size.Width, Height = size.Height, Tag = index, Source = FirstFrameOf(step, sprites) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            if (sprite is not null && sprite.Frames.Count > 1 && !step.IsLanding && step.Frame == 0) animators?.Add(new SpriteAnimator(sprite, image, resources)); // a landing's tile stays its tile
            Canvas.SetLeft(image, layout.OriginX + step.PictureX);
            Canvas.SetTop(image, layout.OriginY + step.PictureY);
            canvas.Children.Add(image);
        }
        return canvas;
    }

    /// <summary>The step whose picture is under this point of the staircase's picture (the front-most), or -1.</summary>
    public static int StepAt(Canvas visual, Point p)
    {
        foreach (var image in visual.Children.OfType<FrameworkElement>().Reverse())
        {
            var rect = new Rect(Canvas.GetLeft(image), Canvas.GetTop(image), image.Width, image.Height);
            if (rect.Contains(p) && image.Tag is int index) return index;
        }
        return -1;
    }
}
