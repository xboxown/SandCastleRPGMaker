// What an object's paperdoll slots hold, drawn on the object itself in the world (PaperDoll Editor > a Slot > Show on the object) - every engine.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A test-tube rack, a machine with its parts, a lamp with its bulb: the items in its paperdoll's slots (those set to Show on the
/// object) are drawn on the object in the world, each at its slot's spot on the object's picture, in its own look (or the look the
/// slot gives it there). Each is its own thing - a click names it, a double click uses it - while the object stays one object:
/// moved, picked up or carried, its slots go with it. What's put in or taken out (in the paperdoll, by events) shows at once.
/// </summary>
internal static class ObjectSlotPictures
{
    /// <summary>One item drawn on its object - its picture's Tag, so a click on it knows which.</summary>
    public sealed record Piece(int PartId, InventoryItemInstance Item);

    /// <summary>The paperdoll of an object, if any of its slots show on the object.</summary>
    public static DollDesign? ShownDesign(WorldObject? obj, ObjectDatabase objects) =>
        obj is { DollDesignId: >= 0 } && objects.DollDesigns.FirstOrDefault(d => d.Id == obj.DollDesignId) is { } design && design.Parts.Any(Shows) ? design : null;

    public static bool Shows(DollPart part) => part.ShowOnObject && part.Kind == DollPartKind.Slot;

    /// <summary>What's shown on it now, for knowing when to draw it again ("" = nothing).</summary>
    public static string Signature(ObjectInstance instance, ObjectDatabase objects)
    {
        if (instance.DollRuntime is not { } state || ShownDesign(DefinitionOf(instance, objects), objects) is not { } design) return string.Empty;
        return string.Join(";", design.Parts.Where(Shows).Select(p => state.Parts.TryGetValue(p.Id, out var r)
            ? $"{p.Id}:{r.Visible}:{string.Join(",", r.Items.Select(i => $"{i.InstanceId}/{i.ItemId}/{i.FrameRuntime}/{i.LiquidIdRuntime}"))}" : $"{p.Id}"));
    }

    private static WorldObject? DefinitionOf(ObjectInstance instance, ObjectDatabase objects) => objects.Objects.FirstOrDefault(o => o.Id == instance.ObjectId);

    /// <summary>
    /// The object's picture with its slots' items on it (a canvas the picture's size), or the picture as it was when none show.
    /// <paramref name="ownLook"/>: an item's own look in the world.
    /// </summary>
    public static FrameworkElement Wrap(FrameworkElement body, SpriteEntry? bodySprite, ObjectInstance instance, WorldObject? obj,
        ObjectDatabase objects, SpriteDatabase sprites, ResourceDatabase resources, List<SpriteAnimator>? animators, Func<InventoryItemInstance, SpriteEntry?> ownLook)
    {
        if (ShownDesign(obj, objects) is not { } design || double.IsNaN(body.Width) || double.IsNaN(body.Height)) return body;
        instance.DollRuntime ??= DollState.StartOf(design); // as if opened: its slots hold their starting items
        var state = instance.DollRuntime;
        // The picture is drawn fitted into its box (Uniform): the same scale and inset for what's on it.
        var first = DollLook.Frame(bodySprite);
        double scale = first is null ? 1 : Math.Min(body.Width / Math.Max(1, first.PixelWidth), body.Height / Math.Max(1, first.PixelHeight));
        double insetX = first is null ? 0 : (body.Width - first.PixelWidth * scale) / 2, insetY = first is null ? 0 : (body.Height - first.PixelHeight * scale) / 2;
        var holder = new Canvas { Width = body.Width, Height = body.Height, IsHitTestVisible = true };
        holder.Children.Add(body);
        foreach (var part in design.Parts.Where(Shows))
        {
            var runtime = state.Of(part);
            if (!runtime.Visible) continue;
            for (int i = 0; i < runtime.Items.Count; i++)
            {
                var item = runtime.Items[i];
                var fit = DollLook.FitFor(part, item.Category, item.ItemId);
                var sprite = DollLook.SpriteOf(fit?.ObjectSprite, sprites) ?? ownLook(item);
                if (DollLook.Frame(sprite) is not { } frame) continue;
                var picture = new Image { Width = frame.PixelWidth * scale, Height = frame.PixelHeight * scale, Stretch = Stretch.Fill, IsHitTestVisible = true, Tag = new Piece(part.Id, item) };
                RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
                picture.Source = frame;
                if (sprite!.Frames.Count > 1 && animators is not null) animators.Add(new SpriteAnimator(sprite, picture, resources, null));
                Canvas.SetLeft(picture, insetX + (part.ObjectX + i * part.ObjectStepX) * scale);
                Canvas.SetTop(picture, insetY + (part.ObjectY + i * part.ObjectStepY) * scale);
                holder.Children.Add(picture);
            }
        }
        return holder;
    }

    /// <summary>The item a click landed on, if it was one drawn on this object's picture.</summary>
    public static Piece? Clicked(DependencyObject? hit, UIElement visual)
    {
        for (var at = hit; at is not null && !ReferenceEquals(at, visual); at = VisualTreeHelper.GetParent(at))
            if (at is FrameworkElement { Tag: Piece piece }) return piece;
        return null;
    }

    /// <summary>Still in its slot (it may have been taken out since the click).</summary>
    public static bool StillThere(ObjectInstance instance, Piece piece) =>
        instance.DollRuntime is { } state && state.Parts.TryGetValue(piece.PartId, out var runtime) && runtime.Items.Contains(piece.Item);
}
