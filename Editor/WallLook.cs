using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How a wall set (Wall Designer) looks - one rule for the designer, the Map
/// Editor and the Object Editor. Each piece is drawn exactly where the game
/// draws that object placed at the set's cell with the piece's offset and
/// lift (centred on the cell, bottom on its bottom edge, then 8px a unit and
/// 4px up-left a lift); the whole picture is padded so it is anchored on its
/// cell like any sprite. In play the pieces are the real objects
/// (GameTestWindow.WallSets.cs).
/// </summary>
internal static class WallLook
{
    public readonly record struct Layout(double Width, double Height, double OriginX, double OriginY);
    private const int LiftPixels = 4;

    public static WallDesign? DesignOf(WorldObject? obj, ObjectDatabase objects) => WallSets.DesignOf(obj, objects);

    /// <summary>The pieces for a placed copy facing this way, or null when the object isn't a wall set.</summary>
    public static WallPose? PoseOf(WorldObject? obj, ObjectDatabase objects, SpriteDirection facing) => WallSets.PoseOf(obj, objects, facing);

    /// <summary>A piece's picture: its object's sprite in its starting state, first frame.</summary>
    public static BitmapImage? PictureOf(WallPiece piece, ObjectDatabase objects, SpriteDatabase sprites)
    {
        if (objects.Objects.FirstOrDefault(o => o.Id == piece.ObjectId) is not { } obj) return null;
        var sprite = ObjectLooks.SpriteFor(obj, ObjectLooks.DefaultState(obj), sprites);
        if (sprite is null) return null;
        string path = sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath;
        return FrameCache.Get(path);
    }

    /// <summary>Where a piece's picture is drawn, from the anchor cell's top-left (a piece without a picture: one tile).</summary>
    public static Rect PictureRect(WallPiece piece, ObjectDatabase objects, SpriteDatabase sprites, int tileWidth, int tileHeight)
    {
        var (width, height) = PictureOf(piece, objects, sprites) is { } bitmap ? SpriteSizing.Quantize(bitmap.PixelWidth, bitmap.PixelHeight) : (tileWidth, tileHeight);
        return new Rect((tileWidth - width) / 2.0 + piece.OffsetX * SpriteSizing.BaseUnit - piece.Lift * LiftPixels,
                        tileHeight - height + piece.OffsetY * SpriteSizing.BaseUnit - piece.Lift * LiftPixels, width, height);
    }

    /// <summary>A piece's footprint (its object's solid box) on the ground, in pixels from the anchor cell's top-left - as the game places it.</summary>
    public static Rect FootRect(WallPiece piece, ObjectDatabase objects, int tileWidth, int tileHeight)
    {
        var obj = objects.Objects.FirstOrDefault(o => o.Id == piece.ObjectId);
        int unitsX = Math.Max(1, tileWidth / SpriteSizing.BaseUnit), unitsY = Math.Max(1, tileHeight / SpriteSizing.BaseUnit);
        int sizeX = Math.Max(1, obj?.SizeX ?? unitsX), sizeY = Math.Max(1, obj?.SizeY ?? 1);
        int left = (int)Math.Floor((unitsX - sizeX) / 2.0) + piece.OffsetX + (obj?.SolidOffsetX ?? 0);
        int top = unitsY - sizeY + piece.OffsetY + (obj?.SolidOffsetY ?? 0);
        return new Rect(left * SpriteSizing.BaseUnit, top * SpriteSizing.BaseUnit, sizeX * SpriteSizing.BaseUnit, sizeY * SpriteSizing.BaseUnit);
    }

    /// <summary>The pieces in paint order: further back first, lower before higher (as the game sorts them).</summary>
    public static IEnumerable<(WallPiece Piece, int Index)> PaintOrder(WallPose pose, ObjectDatabase objects, int tileWidth, int tileHeight) =>
        pose.Pieces.Select((piece, index) => (piece, index))
            .OrderBy(t => FootRect(t.piece, objects, tileWidth, tileHeight).Bottom)
            .ThenBy(t => t.piece.Lift)
            .ThenBy(t => FootRect(t.piece, objects, tileWidth, tileHeight).Left);

    public static Layout LayoutOf(WallPose pose, ObjectDatabase objects, SpriteDatabase sprites, int tileWidth, int tileHeight)
    {
        double minX = 0, maxX = tileWidth, minY = 0;
        foreach (var piece in pose.Pieces)
        {
            var rect = PictureRect(piece, objects, sprites, tileWidth, tileHeight);
            minX = Math.Min(minX, rect.Left);
            maxX = Math.Max(maxX, rect.Right);
            minY = Math.Min(minY, rect.Top);
        }
        // Centred on the cell, bottom edge on the cell's bottom (as every sprite is anchored - the designer keeps every piece above that line); whole 8px units.
        double half = Math.Max(tileWidth / 2.0 - minX, maxX - tileWidth / 2.0);
        half = Math.Ceiling(half / (SpriteSizing.BaseUnit / 2.0)) * (SpriteSizing.BaseUnit / 2.0);
        double height = Math.Ceiling((tileHeight - minY) / SpriteSizing.BaseUnit) * SpriteSizing.BaseUnit;
        return new Layout(half * 2, height, half - tileWidth / 2.0, height - tileHeight);
    }

    /// <summary>The whole set as one still picture (Map Editor, Object Editor, palettes); null when it has no pieces.</summary>
    public static BitmapSource? Bitmap(WallPose pose, ObjectDatabase objects, SpriteDatabase sprites, int tileWidth, int tileHeight)
    {
        if (pose.Pieces.Count == 0) return null;
        var layout = LayoutOf(pose, objects, sprites, tileWidth, tileHeight);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            foreach (var (piece, _) in PaintOrder(pose, objects, tileWidth, tileHeight))
            {
                var rect = PictureRect(piece, objects, sprites, tileWidth, tileHeight);
                rect.Offset(layout.OriginX, layout.OriginY);
                if (PictureOf(piece, objects, sprites) is { } frame) dc.DrawImage(frame, rect);
                else dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 120, 90, 60)), new Pen(Brushes.Tan, 1), rect);
            }
        }
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.NearestNeighbor);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)layout.Width), Math.Max(1, (int)layout.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }
}
