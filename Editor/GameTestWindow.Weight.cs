using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Exult's carry limit (drag.cc Check_weight, actors.cc get_max_weight,
/// contain.cc get_weight): when something lands on a character - dropped on
/// them, readied in a slot, or put into a container they carry (at any
/// depth) - and that character is the main character or a party member,
/// everything they'd then carry may not weigh more than 2 x Strength. Too
/// much: the Too Heavy pointer flashes and the drop is refused. Characters
/// outside the party are never weighed, and moving things about on the
/// ground is never weighed either (a heavy chest can be dragged across the
/// floor). Weights are counted by ItemWeights: a container weighs itself
/// plus everything inside, unless it's a bag of holding.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Check_weight: true if <paramref name="owner"/> (null = nobody) can take what's being dropped.</summary>
    private bool CheckWeight(DragOrigin origin, CharacterDefinition? owner)
    {
        if (owner is null || !(owner == _player || owner.IsInParty)) return true; // "Not a party member, so okay."
        var payload = PayloadObject(origin);
        double carried = ItemWeights.Carried(owner, _containers, _books, _objects, excluding: payload); // Exult lifts the dragged thing off first
        double adding = PayloadWeight(origin);
        double max = ItemWeights.MaxCarry(Stat(owner, BuiltInStatKind.Strength, 10));
        if (carried + adding <= max) return true;
        Refuse(MousePointerKind.TooHeavy, $"Too heavy: \"{PayloadName(origin)}\" weighs {adding:0.##}, and {owner.Name} already carries {carried:0.##} of the {max:0.##} they can (2 x Strength).");
        return false;
    }

    /// <summary>What the dragged thing weighs, with everything inside it.</summary>
    private double PayloadWeight(DragOrigin origin)
    {
        switch (PayloadObject(origin))
        {
            case InventoryItemInstance carried:
                return ItemWeights.WeightOf(carried, _containers, _books, _objects);
            case ObjectInstance obj:
                return ItemWeights.OwnWeight(ReferencedItemCategory.Object, obj.ObjectId, _containers, _books, _objects) * Math.Max(1, obj.Quantity);
            case PlacedItemInstance placed:
                double weight = ItemWeights.OwnWeight(placed.Category, placed.ItemId, _containers, _books, _objects);
                if (placed.Category == ReferencedItemCategory.Container && !ItemWeights.WeighsOnlyItself(placed.Category, placed.ItemId, _containers))
                {
                    // Never opened: its Starting Contents are in it all the same.
                    var contents = placed.InstanceId == 0 ? SeedContentsOf(placed) : placed.Contents;
                    weight += contents.Sum(i => ItemWeights.WeightOf(i, _containers, _books, _objects));
                }
                return weight;
            default:
                return 0;
        }
    }

    /// <summary>get_outermost for a container: the character carrying it (at any depth), or null if it's on the map.</summary>
    private CharacterDefinition? CarrierOf(IHasContainerContents container)
    {
        if (container is not InventoryItemInstance || _characters is null) return null;
        return _characters.Characters.FirstOrDefault(c => ItemWeights.Holds(c.Inventory, container));
    }
}
