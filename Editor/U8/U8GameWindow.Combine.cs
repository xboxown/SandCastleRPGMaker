// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Combining, for every object: an item dropped onto an object - on the
/// map, in a container's window, on a paperdoll slot or the backpack list -
/// runs that object's By Item Combined To It event first (a page can be set
/// to one item only). If a page ran, that's what happened: the dropped item
/// stays where it came from unless the event used it up (the Paperdoll
/// command's "Use up the item just combined" works here too). With no such
/// page, the drop goes on as before. And By Hitting Target, for ammo and
/// projectiles: a character or an object hit.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>The item being combined onto an object while its event runs.</summary>
    private DragOrigin? _combinedOrigin;

    private static bool HasPagesFor(WorldObject obj, BookTriggerConditionBased trigger) =>
        obj.EventPages.Any(p => p.Conditions.TriggerConditionBased == trigger && p.Commands.Count > 0);

    /// <summary>True when an object under the drop took it (its By Item Combined To It ran).</summary>
    private bool TryCombineOntoObject(DragOrigin origin, Point screen)
    {
        object? owner = null;
        WorldObject? target = null;
        if (HitTestGumpLayer(screen, out var gump, out var hitTarget))
        {
            if (gump is OpenDollGump) return false; // a paperdoll combines on its parts (U8GameWindow.Dolls.cs)
            InventoryItemInstance? item = hitTarget switch
            {
                ContainerItemTarget ci => ci.Item,
                BackpackRowTarget row => row.Item,
                SlotTarget slot when TryGetEquippedInstanceId(slot.Gump.Character, slot.SlotKey, out int id) => slot.Gump.Character.Inventory.FirstOrDefault(i => i.InstanceId == id),
                _ => null
            };
            if (item is not null && DefinitionOf(item) is { } def) { owner = item; target = def; }
        }
        else if (FindMapTarget(screen) is MapEntityTarget { Entity: ObjectInstance onMap } && DefinitionOf(onMap) is { } def)
        {
            owner = onMap;
            target = def;
        }
        if (owner is null || target is null || ReferenceEquals(owner, PayloadObject(origin))) return false;
        // Sowing: a seed dropped on its soil (Plant: Sown from / in) - the soil becomes the plant, at its first stage.
        if (owner is ObjectInstance soil && PayloadIdentity(origin) is (ReferencedItemCategory.Object, int seedId) && PlantSownWith(seedId, target.Id) is { } sown)
        {
            if (!IsReachable(soil)) { Refuse(MousePointerKind.Blocked, CantReachMessage); return true; }
            if (ObjectOwner(target, soil) is { } field) Theft(field.Owner, field.FactionId); // someone else's field
            Sow(soil, sown, _player);
            UseUpOne(origin);
            StatusText.Text = $"You sow \"{sown.Name}\".";
            return true;
        }
        if (!HasPagesFor(target, BookTriggerConditionBased.ByCombineItem)) return false;
        if (owner is ObjectInstance placed && !IsReachable(placed)) { Refuse(MousePointerKind.Blocked, CantReachMessage); return true; }
        var (category, itemId) = PayloadIdentity(origin);
        var pages = target.EventPages.Where(p => p.Conditions.TriggerConditionBased != BookTriggerConditionBased.ByCombineItem
            || p.Conditions.TriggerItemReference is not { ItemId: >= 0 } wanted || (wanted.Category == category && wanted.ItemId == itemId)).ToList();
        var previous = _combinedOrigin;
        _combinedOrigin = origin;
        bool ran;
        try { ran = TryFireEventPages(owner, pages, BookTriggerConditionBased.ByCombineItem, -1, $"\"{PayloadName(origin)}\" combined with \"{target.Name}\"."); }
        finally { _combinedOrigin = previous; }
        if (ran) RefreshAfterMove();
        return ran;
    }

    /// <summary>By Hitting Target: ammo or a projectile hit a character or an object (a page can wait for one or the other).</summary>
    private void FireHitTarget(object owner, WorldObject obj, HitTargetKind hit, MapCharacter? victim)
    {
        if (!HasPagesFor(obj, BookTriggerConditionBased.ByHitTarget)) return;
        var pages = obj.EventPages.Where(p => p.Conditions.TriggerConditionBased != BookTriggerConditionBased.ByHitTarget
            || p.Conditions.HitTarget == HitTargetKind.Any || p.Conditions.HitTarget == hit).ToList();
        var (previous, previousStep) = (_activator, _activatedByCharacterStep);
        _activator = victim;
        _activatedByCharacterStep = victim is not null;
        try { TryFireEventPages(owner, pages, BookTriggerConditionBased.ByHitTarget, -1, $"\"{obj.Name}\" hit {(hit == HitTargetKind.Character ? "someone" : "something")}."); }
        finally { (_activator, _activatedByCharacterStep) = (previous, previousStep); }
    }
}
