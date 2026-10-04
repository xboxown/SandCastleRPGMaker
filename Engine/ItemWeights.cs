namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Weight, as Exult counts it (contain.cc Container_game_object::get_weight,
/// actors.cc Actor::get_max_weight, drag.cc Check_weight):
///  - A thing weighs its own Weight times its quantity; a container also
///    weighs everything inside it, at any depth - unless it has
///    extradimensional storage (a bag of holding), which weighs only itself.
///  - A character can carry 2 x Strength: everything it holds, wears, and
///    has inside the containers it carries.
///  - Only party members (the main character included) are held to it.
/// Used by both the game and the Character Editor's Starting Inventory.
/// </summary>
public static class ItemWeights
{
    /// <summary>Actor::get_max_weight: 2 x Strength.</summary>
    public static double MaxCarry(int strength) => 2.0 * strength;

    /// <summary>The weight of one of this item, not counting anything inside it.</summary>
    public static double OwnWeight(ReferencedItemCategory category, int itemId, ContainerDatabase containers, BookDatabase books, ObjectDatabase objects) => category switch
    {
        ReferencedItemCategory.Book => books.Books.FirstOrDefault(b => b.Id == itemId)?.Weight ?? 0,
        ReferencedItemCategory.Container => containers.Containers.FirstOrDefault(c => c.Id == itemId)?.Weight ?? 0,
        ReferencedItemCategory.Object => objects.Objects.FirstOrDefault(o => o.Id == itemId)?.Weight ?? 0,
        _ => 0
    };

    /// <summary>Whether a container's contents add nothing to its weight (extradimensional storage).</summary>
    public static bool WeighsOnlyItself(ReferencedItemCategory category, int itemId, ContainerDatabase containers) =>
        category == ReferencedItemCategory.Container && containers.Containers.FirstOrDefault(c => c.Id == itemId)?.ExtradimensionalStorage == true;

    /// <summary>Container_game_object::get_weight: the item times its quantity, plus (a container) everything inside it. <paramref name="excluding"/> is left out wherever it is.</summary>
    public static double WeightOf(InventoryItemInstance item, ContainerDatabase containers, BookDatabase books, ObjectDatabase objects, object? excluding = null)
    {
        if (ReferenceEquals(item, excluding)) return 0;
        double weight = OwnWeight(item.Category, item.ItemId, containers, books, objects) * Math.Max(1, item.Quantity);
        if (item.Category == ReferencedItemCategory.Container && !WeighsOnlyItself(item.Category, item.ItemId, containers))
            weight += item.Contents.Sum(inside => WeightOf(inside, containers, books, objects, excluding));
        return weight;
    }

    /// <summary>Everything a character carries (held, worn, and inside the containers it carries). <paramref name="excluding"/> - the thing being moved - isn't counted, as Exult lifts it off before it's dropped again.</summary>
    public static double Carried(CharacterDefinition character, ContainerDatabase containers, BookDatabase books, ObjectDatabase objects, object? excluding = null) =>
        character.Inventory.Sum(item => WeightOf(item, containers, books, objects, excluding));

    /// <summary>Whether the item is anywhere in these contents, at any depth.</summary>
    public static bool Holds(IEnumerable<InventoryItemInstance> contents, object target) =>
        contents.Any(i => ReferenceEquals(i, target) || Holds(i.Contents, target));
}
