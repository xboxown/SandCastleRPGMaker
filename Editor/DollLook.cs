using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How a paperdoll's parts (PaperDoll Editor) look - one rule for the editor
/// and the game: a part shows the sprite of its current look (animated, with
/// its sounds, as the Sprite Editor made it); a part without a sprite takes
/// its own Width x Height.
/// </summary>
internal static class DollLook
{
    public static DollDesign? DesignOf(WorldObject? obj, ObjectDatabase objects) =>
        obj is { DollDesignId: >= 0 } ? objects.DollDesigns.FirstOrDefault(d => d.Id == obj.DollDesignId) : null;

    public static SpriteEntry? SpriteOf(SpriteLink? link, SpriteDatabase sprites) =>
        link is { IsSet: true } ? ObjectLooks.Find(sprites, link.SpriteGroup, link.SpriteName) : null;

    /// <summary>The sprite of one of a part's looks (null: none set).</summary>
    public static SpriteEntry? LookSprite(DollPart part, int look, SpriteDatabase sprites) =>
        look >= 0 && look < part.Looks.Count ? SpriteOf(part.Looks[look].Sprite, sprites) : null;

    public static BitmapImage? Frame(SpriteEntry? sprite, int frame = 0)
    {
        if (sprite is null) return null;
        string path = sprite.Frames.Count > 0 ? sprite.Frames[Math.Clamp(frame, 0, sprite.Frames.Count - 1)].FilePath : sprite.FilePath;
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

    /// <summary>A part's size for a look: its sprite's, else its own Width x Height.</summary>
    public static Size SizeOf(DollPart part, int look, SpriteDatabase sprites)
    {
        if (part.Kind is DollPartKind.Slot or DollPartKind.ItemArea or DollPartKind.Hotspot or DollPartKind.Text)
            return new Size(Math.Max(4, part.Width), Math.Max(4, part.Height));
        if (part.Kind == DollPartKind.Slider) look = 0; // the track
        return Frame(LookSprite(part, look, sprites)) is { } bitmap ? new Size(bitmap.PixelWidth, bitmap.PixelHeight) : new Size(Math.Max(4, part.Width), Math.Max(4, part.Height));
    }

    /// <summary>Where a slider's knob sits along its track for a value (top-left of the knob, relative to the part).</summary>
    public static Point KnobAt(DollPart part, int value, Size track, Size knob)
    {
        double span = Math.Max(1, part.Max - part.Min);
        double t = Math.Clamp((value - part.Min) / span, 0, 1);
        return part.Vertical
            ? new Point((track.Width - knob.Width) / 2, (track.Height - knob.Height) * (1 - t)) // up = more
            : new Point((track.Width - knob.Width) * t, (track.Height - knob.Height) / 2);
    }

    /// <summary>The value a click this far along a slider's track means.</summary>
    public static int ValueAt(DollPart part, Point inPart, Size track)
    {
        double t = part.Vertical ? 1 - inPart.Y / Math.Max(1, track.Height) : inPart.X / Math.Max(1, track.Width);
        return part.Min + (int)Math.Round(Math.Clamp(t, 0, 1) * (part.Max - part.Min));
    }

    /// <summary>The value a Display shows: the slider it follows, else its own.</summary>
    public static int DisplayValue(DollPart display, DollDesign design, Func<DollPart, int> valueOf) =>
        display.ShowsPartId >= 0 && design.Part(display.ShowsPartId) is { } source ? valueOf(source) : valueOf(display);

    /// <summary>The lid covering this part, closed, if any.</summary>
    public static DollPart? ClosedLidOver(DollPart part, DollDesign design, Func<DollPart, (int Look, bool Visible)> stateOf) =>
        design.Parts.FirstOrDefault(lid => lid.Kind == DollPartKind.Lid && lid.Covers.Contains(part.Id) && stateOf(lid) is { Look: 0, Visible: true });

    /// <summary>A Text part's words as a picture, in its font (the in-game font when none is picked), tinted; null when there's no font with glyphs.</summary>
    public static BitmapSource? TextPicture(DollPart part, SpriteDatabase sprites)
    {
        var font = SpriteFont.For(sprites, string.IsNullOrEmpty(part.FontName) ? SpriteDatabase.InGameFontName : part.FontName);
        if (font is null || string.IsNullOrEmpty(part.Text)) return null;
        Brush? tint = null;
        if (!string.IsNullOrWhiteSpace(part.TextColor))
            try { tint = new SolidColorBrush((Color)ColorConverter.ConvertFromString(part.TextColor)); } catch { tint = null; }
        return font.RenderText(part.Text, Math.Max(1, part.TextScale), tint, Math.Max(8, part.Width));
    }

    /// <summary>What an item reference is called, from the databases.</summary>
    public static string ItemName(ItemReference item, ObjectDatabase objects, BookDatabase books, ContainerDatabase containers) => item.Category switch
    {
        ReferencedItemCategory.Object => objects.Objects.FirstOrDefault(o => o.Id == item.ItemId)?.Name ?? $"Object #{item.ItemId}",
        ReferencedItemCategory.Book => books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.Name ?? $"Book #{item.ItemId}",
        ReferencedItemCategory.Container => containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.Name ?? $"Container #{item.ItemId}",
        _ => string.IsNullOrEmpty(item.DisplayName) ? item.Category.ToString() : item.DisplayName
    };

    /// <summary>An item's own sprite (an object's, a book's, a container's paperdoll-free look).</summary>
    public static SpriteEntry? ItemSprite(ReferencedItemCategory category, int itemId, ObjectDatabase objects, BookDatabase books, ContainerDatabase containers, SpriteDatabase sprites) => category switch
    {
        ReferencedItemCategory.Object => objects.Objects.FirstOrDefault(o => o.Id == itemId) is { } o ? ObjectLooks.Find(sprites, o.SpriteGroup, o.SpriteName) : null,
        ReferencedItemCategory.Book => books.Books.FirstOrDefault(b => b.Id == itemId) is { } b ? ObjectLooks.Find(sprites, b.SpriteGroup, b.SpriteName) : null,
        ReferencedItemCategory.Container => containers.Containers.FirstOrDefault(c => c.Id == itemId) is { } c ? ObjectLooks.Find(sprites, c.SpriteGroup, c.SpriteName) : null,
        _ => null
    };

    /// <summary>The fit a Slot uses for an item (null: the slot doesn't take it). A slot listing nothing takes anything, in its own look.</summary>
    public static DollSlotFit? FitFor(DollPart slot, ReferencedItemCategory category, int itemId)
    {
        if (slot.Kind != DollPartKind.Slot) return null;
        if (slot.Accepts.Count == 0) return new DollSlotFit { Item = new ItemReference { Category = category, ItemId = itemId } };
        return slot.Accepts.FirstOrDefault(a => a.Item.Category == category && a.Item.ItemId == itemId);
    }
}
