namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Adding to a character's Starting Inventory in the editor, by the same
/// rules as picking something up in play (GameTestWindow.DropOnCharacter):
///  - A container goes in its own Location slot (Container Editor) if free,
///    else the right hand, else the left hand (not while the right holds
///    something two-handed). It never goes into a bag. It arrives holding its
///    own Starting Contents, like a chest picked up off the map.
///  - Anything else goes into the container in the Backpack slot, then the
///    one in the Pouch slot, while it has room (used volume + the item's
///    volume within Max Volume); else the right hand (a two-handed item
///    needs both hands free), else the left hand.
///  - With nowhere left, it can't be added.
/// </summary>
public static class StartingInventoryRules
{
    /// <summary>"No spot yet" inside a container - laid out automatically when its window is drawn (the game's own UnplacedSpot).</summary>
    public const double UnplacedSpot = -100000;

    /// <summary>Adds one of the item to the character. Null on success, otherwise why it couldn't be added.</summary>
    public static string? TryAdd(CharacterDefinition character, ReferencedItemCategory category, int itemId,
        ContainerDatabase containers, BookDatabase books, ObjectDatabase objects)
    {
        var item = new InventoryItemInstance { Category = category, ItemId = itemId };
        string name = NameOf(category, itemId, containers, books, objects);

        if (category == ReferencedItemCategory.Container)
        {
            var data = containers.Containers.FirstOrDefault(c => c.Id == itemId);
            if (data is null) return $"\"{name}\" no longer exists.";
            object? slot = LocationSlot(data, character) is { } location && IsFree(character, location) ? location
                : IsFree(character, EquipmentSlot.RightHand) ? EquipmentSlot.RightHand
                : IsFree(character, EquipmentSlot.LeftHand) && !LeftHandLocked(character, containers, books, objects) ? EquipmentSlot.LeftHand
                : null;
            if (slot is null) return $"No room for \"{name}\": its own slot and both hands are already full.";
            item.Contents = data.StartingContents.Select(seed => new InventoryItemInstance
            {
                InstanceId = seed.InstanceId, Category = seed.Category, ItemId = seed.ItemId, Quantity = seed.Quantity,
                ContainerX = seed.ContainerX, ContainerY = seed.ContainerY
            }).ToList();
            Equip(character, item, slot);
            return null;
        }

        double volume = VolumeOf(item, containers, books, objects);
        foreach (var bagSlot in new[] { EquipmentSlot.Backpack, EquipmentSlot.Pouch })
        {
            if (!character.EquippedSlots.TryGetValue(bagSlot, out int bagId)) continue;
            var bag = character.Inventory.FirstOrDefault(i => i.InstanceId == bagId);
            if (bag is not { Category: ReferencedItemCategory.Container }) continue;
            var bagData = containers.Containers.FirstOrDefault(c => c.Id == bag.ItemId);
            if (bagData is null || bagData.IsBodyContainer) continue;
            double used = bag.Contents.Sum(i => VolumeOf(i, containers, books, objects));
            if (used + volume > bagData.MaxVolume) continue;
            item.InstanceId = bag.Contents.Count == 0 ? 1 : bag.Contents.Max(i => i.InstanceId) + 1;
            item.ContainerX = item.ContainerY = UnplacedSpot;
            bag.Contents.Add(item);
            return null;
        }

        bool twoHanded = IsTwoHanded(item, containers, books, objects);
        object? hand = IsFree(character, EquipmentSlot.RightHand) && (!twoHanded || IsFree(character, EquipmentSlot.LeftHand)) ? EquipmentSlot.RightHand
            : !twoHanded && !LeftHandLocked(character, containers, books, objects) && IsFree(character, EquipmentSlot.LeftHand) ? EquipmentSlot.LeftHand
            : null;
        if (hand is null) return $"No room for \"{name}\": it doesn't fit in the Backpack or Pouch container, and both hands are full.";
        Equip(character, item, hand);
        return null;
    }

    /// <summary>Removes an item from the character: from the container holding it, or from the character (clearing its slot; a container goes with everything in it).</summary>
    public static void Remove(CharacterDefinition character, InventoryItemInstance item, IHasContainerContents? heldIn)
    {
        if (heldIn is not null) { heldIn.Contents.Remove(item); return; }
        character.Inventory.Remove(item);
        foreach (var slot in character.EquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) character.EquippedSlots.Remove(slot);
        foreach (var slot in character.CustomEquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) character.CustomEquippedSlots.Remove(slot);
    }

    /// <summary>The slot an item is equipped in, as a name ("Backpack", a custom slot's name), or null.</summary>
    public static string? SlotNameOf(CharacterDefinition character, InventoryItemInstance item)
    {
        foreach (var kv in character.EquippedSlots) if (kv.Value == item.InstanceId) return kv.Key.ToString();
        foreach (var kv in character.CustomEquippedSlots)
            if (kv.Value == item.InstanceId) return character.Paperdoll.CustomSlots.FirstOrDefault(s => s.Id == kv.Key)?.Name ?? "custom slot";
        return null;
    }

    public static string NameOf(ReferencedItemCategory category, int itemId, ContainerDatabase containers, BookDatabase books, ObjectDatabase objects) => category switch
    {
        ReferencedItemCategory.Book => books.Books.FirstOrDefault(b => b.Id == itemId)?.Name ?? "(missing book)",
        ReferencedItemCategory.Object => objects.Objects.FirstOrDefault(o => o.Id == itemId)?.Name ?? "(missing object)",
        ReferencedItemCategory.Container => containers.Containers.FirstOrDefault(c => c.Id == itemId)?.Name ?? "(missing container)",
        _ => $"(no {category} database yet)"
    };

    private static void Equip(CharacterDefinition character, InventoryItemInstance item, object slot)
    {
        item.InstanceId = character.GetNextInventoryInstanceId();
        character.Inventory.Add(item);
        if (slot is EquipmentSlot builtin) character.EquippedSlots[builtin] = item.InstanceId;
        else if (slot is int customId) character.CustomEquippedSlots[customId] = item.InstanceId;
    }

    /// <summary>The container's Location for this character: a built-in slot, or this character's custom slot of that name (an int id), or null.</summary>
    private static object? LocationSlot(ContainerData data, CharacterDefinition character)
    {
        if (data.PreferredEquipSlot is { } builtin) return builtin;
        if (data.PreferredCustomSlotName is { } customName) return character.Paperdoll.CustomSlots.FirstOrDefault(s => s.Name == customName)?.Id;
        return null;
    }

    private static bool IsFree(CharacterDefinition character, object slot) => slot switch
    {
        EquipmentSlot builtin => !character.EquippedSlots.ContainsKey(builtin),
        int customId => !character.CustomEquippedSlots.ContainsKey(customId),
        _ => false
    };

    private static bool LeftHandLocked(CharacterDefinition character, ContainerDatabase containers, BookDatabase books, ObjectDatabase objects) =>
        character.EquippedSlots.TryGetValue(EquipmentSlot.RightHand, out int rightId)
        && character.Inventory.FirstOrDefault(i => i.InstanceId == rightId) is { } right
        && IsTwoHanded(right, containers, books, objects);

    private static bool IsTwoHanded(InventoryItemInstance item, ContainerDatabase containers, BookDatabase books, ObjectDatabase objects) => item.Category switch
    {
        ReferencedItemCategory.Book => books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.IsTwoHanded ?? false,
        ReferencedItemCategory.Object => objects.Objects.FirstOrDefault(o => o.Id == item.ItemId)?.IsTwoHanded ?? false,
        ReferencedItemCategory.Container => containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.IsTwoHanded ?? false,
        _ => false
    };

    /// <summary>The game's ResolveContentsVolume: the item's Volume times its quantity.</summary>
    private static double VolumeOf(InventoryItemInstance item, ContainerDatabase containers, BookDatabase books, ObjectDatabase objects) => item.Category switch
    {
        ReferencedItemCategory.Book => books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.Volume ?? 0,
        ReferencedItemCategory.Container => containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.Volume ?? 0,
        ReferencedItemCategory.Object => objects.Objects.FirstOrDefault(o => o.Id == item.ItemId)?.Volume ?? 0,
        _ => 0
    } * item.Quantity;
}
