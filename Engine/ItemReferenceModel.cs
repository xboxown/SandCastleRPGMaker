namespace SandcastleRpgMaker.Engine;

/// <summary>Which catalog a referenced item comes from. Weapon/Armor/Item don't have their own editors yet - those tabs will show empty until built, rather than pretending data exists.</summary>
public enum ReferencedItemCategory { Weapon, Armor, Item, Book, Object, Container }

/// <summary>A stable pointer to one specific item somewhere in the project (a book, a world object, eventually a weapon/armor/item), used wherever an event condition needs to target a particular thing rather than "any item".</summary>
public class ItemReference
{
    public ReferencedItemCategory Category { get; set; } = ReferencedItemCategory.Book;
    public int ItemId { get; set; } = -1;

    /// <summary>Cached for display without a database lookup - refreshed whenever the reference is (re)picked.</summary>
    public string DisplayName { get; set; } = string.Empty;
}
