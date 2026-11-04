// Ultima 8 engine (U8GameWindow) only.
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's paperdoll while something is dragged over it (Pentagram's PaperdollGump::DraggingItem and PaintThis): the slot the item
/// would go to shows it see-through (PaintInvisible) until the button is let go or the pointer leaves. A worn item taken off is gone
/// from its slot at once, as here already: the original unequips at the start of the drag.
/// </summary>
public partial class U8GameWindow
{
    private const double EquipPreviewOpacity = 0.5;

    private FrameworkElement? _equipPreview;
    private Canvas? _equipPreviewCanvas;
    private object? _equipPreviewSlot;

    /// <summary>Each move of a drag: the see-through item on the slot under the pointer, if it would go there.</summary>
    private void UpdateEquipPreview(PointerSession session, Point screen)
    {
        if (session.Origin is not { } origin) return;
        foreach (var gump in Enumerable.Reverse(_openPaperdolls))
        {
            var inBody = PaperdollGumpsCanvas.TranslatePoint(screen, gump.BodyCanvas);
            if (inBody.X < 0 || inBody.Y < 0 || inBody.X > gump.BodyCanvas.ActualWidth || inBody.Y > gump.BodyCanvas.ActualHeight) continue;
            if (FindClosestFreeSlot(gump, inBody, origin) is { } slot) { ShowEquipPreview(session, origin, gump, slot); return; }
            break;
        }
        RemoveEquipPreview();
    }

    private void ShowEquipPreview(PointerSession session, DragOrigin origin, OpenPaperdollGump gump, object slot)
    {
        if (_equipPreview is not null && ReferenceEquals(_equipPreviewCanvas, gump.BodyCanvas) && Equals(_equipPreviewSlot, slot)) return;
        RemoveEquipPreview();
        var (category, itemId) = PayloadIdentity(origin);
        if (BuildItemIcon(category, itemId, session.GhostAnimators, PayloadObject(origin)) is not FrameworkElement icon) return;
        var paperdoll = gump.Character.Paperdoll;
        double x, y;
        if (slot is EquipmentSlot builtIn) (x, y) = paperdoll.SlotPositions.TryGetValue(builtIn, out var at) ? (at.X, at.Y) : (140, 10);
        else if (slot is int customId && paperdoll.CustomSlots.FirstOrDefault(c => c.Id == customId) is { } custom) (x, y) = (custom.X, custom.Y);
        else return;
        icon.Opacity = EquipPreviewOpacity;
        icon.IsHitTestVisible = false;
        Canvas.SetLeft(icon, x);
        Canvas.SetTop(icon, y);
        Panel.SetZIndex(icon, 10000);
        gump.BodyCanvas.Children.Add(icon);
        (_equipPreview, _equipPreviewCanvas, _equipPreviewSlot) = (icon, gump.BodyCanvas, slot);
    }

    private void RemoveEquipPreview()
    {
        if (_equipPreview is not null) _equipPreviewCanvas?.Children.Remove(_equipPreview);
        (_equipPreview, _equipPreviewCanvas, _equipPreviewSlot) = (null, null, null);
    }
}
