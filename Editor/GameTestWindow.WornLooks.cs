using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Serpent Isle's paperdoll (Exult's Paperdoll_gump, SI): on a paperdoll set to "Worn on the body", what's worn is
/// drawn on the body - each thing's worn look over the body's picture - in Exult's painting order, and picked up by
/// its own pixels. A thing without a worn look shows its icon in its slot, as Exult falls back to the item's shape.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Exult's Paperdoll_gump::paint order: backpack, cloak, legs, feet, body, belt, pouch, head, neck, arms, rings, gloves, left hand, right hand.</summary>
    private static int WornOrder(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Backpack => 0, EquipmentSlot.Back => 1, EquipmentSlot.Legs => 2, EquipmentSlot.Feet => 3, EquipmentSlot.Body => 4,
        EquipmentSlot.Belt => 5, EquipmentSlot.Pouch => 6, EquipmentSlot.Head => 7, EquipmentSlot.Neck => 8, EquipmentSlot.Arms => 9,
        EquipmentSlot.Ring1 => 10, EquipmentSlot.Ring2 => 11, EquipmentSlot.Gloves => 12, EquipmentSlot.LeftHand => 13, _ => 14 // the right hand last
    };

    private WornLookSettings? WornOf(InventoryItemInstance item) => item.Category switch
    {
        ReferencedItemCategory.Object => ObjectDef(item.ItemId)?.Worn,
        ReferencedItemCategory.Container => ContainerDef(item.ItemId)?.Worn,
        ReferencedItemCategory.Book => BookDef(item.ItemId)?.Worn,
        _ => null
    };

    /// <summary>Its worn look in this slot - in the left hand or on a woman if it has one, else its own - over the body's picture; null when it has none.</summary>
    private FrameworkElement? BuildWornVisual(OpenPaperdollGump gump, CharacterDefinition character, InventoryItemInstance item, EquipmentSlot slot, SpriteEntry? bodySprite)
    {
        if (WornOf(item) is not { } worn) return null;
        var link = slot == EquipmentSlot.LeftHand && worn.LeftHand.IsSet ? worn.LeftHand
            : character.Gender == CharacterGender.Female && worn.Female.IsSet ? worn.Female
            : worn.Look;
        if (!link.IsSet || ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName) is not { Frames.Count: > 0 } sprite) return null;
        var picture = LoadStaticFrame(sprite.Frames[0].FilePath);
        if (picture is null) return null;
        // The body is fitted into the paperdoll's 320 x 400 (Stretch Uniform): the worn look is scaled and placed the same way, over it.
        var body = bodySprite is { Frames.Count: > 0 } ? LoadStaticFrame(bodySprite.Frames[0].FilePath) : null;
        double scale = body is { PixelWidth: > 0, PixelHeight: > 0 } ? Math.Min(320.0 / body.PixelWidth, 400.0 / body.PixelHeight) : 1;
        double insetX = body is null ? 0 : (320 - body.PixelWidth * scale) / 2, insetY = body is null ? 0 : (400 - body.PixelHeight * scale) / 2;
        var image = new AlphaHitImage { Width = picture.PixelWidth * scale, Height = picture.PixelHeight * scale, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        gump.ItemAnimators.Add(new SpriteAnimator(sprite, image, _resources));
        Canvas.SetLeft(image, insetX + worn.OffsetX * scale);
        Canvas.SetTop(image, insetY + worn.OffsetY * scale);
        return image;
    }
}
