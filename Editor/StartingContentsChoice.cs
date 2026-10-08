using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// One entry of a Starting Contents / starting inventory "kind" drop-down:
/// Book, Container, or one of the object types (Food, Key, Potion, Table...).
/// Every object type is listed, so the author picks the kind first and then
/// only that kind's objects - all of them are still saved as Object items.
/// </summary>
public sealed record StartingContentsChoice(string Name, ReferencedItemCategory Category, ObjectType? Type)
{
    public override string ToString() => Name;

    /// <summary>Book, Container, then every object type in the Object Editor's own order (the retired Roof type left out).</summary>
    public static List<StartingContentsChoice> All() =>
        new[] { new StartingContentsChoice("Book", ReferencedItemCategory.Book, null), new StartingContentsChoice("Container", ReferencedItemCategory.Container, null) }
            .Concat(ObjectTypes.All.Where(t => t != ObjectType.Roof).Select(t => new StartingContentsChoice(ObjectTypes.Name(t), ReferencedItemCategory.Object, t)))
            .ToList();

    /// <summary>The objects of this kind (empty for Book / Container - their own lists come from their own databases).</summary>
    public IEnumerable<WorldObject> ObjectsOf(IEnumerable<WorldObject> objects) =>
        Type is { } type ? objects.Where(o => o.Type == type) : Enumerable.Empty<WorldObject>();
}
