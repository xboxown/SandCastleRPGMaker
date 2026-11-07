// GameTestWindow: what an object's paperdoll slots hold, drawn on the object in the world (ObjectSlotPictures).
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class GameTestWindow
{
    /// <summary>The item last clicked on an object's picture (a tube in its rack) - with the object it's on.</summary>
    private (ObjectInstance On, ObjectSlotPictures.Piece Piece)? _clickedSlotItem;
    private readonly Dictionary<ObjectInstance, string> _slotsShown = new(ReferenceEqualityComparer.Instance);

    private SpriteEntry? OwnLookOf(InventoryItemInstance item) =>
        DefinitionOf(item) is { } obj ? ObjectLooks.SpriteFor(obj, StateOf(item), _sprites) : null;

    private FrameworkElement WithSlotItems(FrameworkElement body, SpriteEntry? sprite, ObjectInstance instance, WorldObject? obj, List<SpriteAnimator>? animators)
    {
        var shown = ObjectSlotPictures.Wrap(body, sprite, instance, obj, _objects, _sprites, _resources, animators, OwnLookOf);
        _slotsShown[instance] = ObjectSlotPictures.Signature(instance, _objects);
        return shown;
    }

    private void RememberSlotItem(ObjectInstance instance, DependencyObject? hit, UIElement visual) =>
        _clickedSlotItem = ObjectSlotPictures.Clicked(hit, visual) is { } piece ? (instance, piece) : null;

    /// <summary>The item on this object that the last click landed on, if it's still there.</summary>
    private ObjectSlotPictures.Piece? SlotItemClicked(ObjectInstance instance) =>
        _clickedSlotItem is { } c && ReferenceEquals(c.On, instance) && ObjectSlotPictures.StillThere(instance, c.Piece) ? c.Piece : null;

    /// <summary>A click (or double click) on an item drawn on its object: named, or used as an item in a paperdoll is. True: done.</summary>
    private bool SlotItemOnObjectClicked(ObjectInstance instance, bool isUse)
    {
        if (SlotItemClicked(instance) is not { } piece || instance.DollRuntime is not { } state) return false;
        _clickedSlotItem = null;
        if (!isUse)
        {
            ShowIdentifyLabel(DollItemName(piece.Item), instance.X, instance.Y, _objectVisuals.GetValueOrDefault(instance) as FrameworkElement);
            return true;
        }
        if (piece.Item.Category == ReferencedItemCategory.Object) UseCarriedObject(piece.Item, new DollItemOrigin(state, piece.PartId, piece.Item));
        else StatusText.Text = $"\"{DollItemName(piece.Item)}\" - nothing happens.";
        return true;
    }

    /// <summary>A paperdoll changed: its object in the world shows what its slots hold now.</summary>
    private void SlotsOnObjectChanged(DollState state)
    {
        if (_map.Objects.FirstOrDefault(o => ReferenceEquals(o.DollRuntime, state)) is not { } placed || !_objectVisuals.ContainsKey(placed)) return;
        if (ObjectSlotPictures.Signature(placed, _objects) == _slotsShown.GetValueOrDefault(placed, "")) return;
        RefreshObjectLook(placed);
    }
}
