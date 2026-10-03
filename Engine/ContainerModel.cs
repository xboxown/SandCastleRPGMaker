namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A container: chest, bag, drawer, corpse/body, anything that holds other
/// items. Inherits every shared placeable-item property from
/// PlaceableItemBase - Name, BelongToId ("belongs to"), Weight, Volume (this
/// container's OWN size when carried inside something else), Destructible +
/// DestructibleSoundResourceId ("destroyed sound"), DestroyedSpriteGroup/Name
/// ("destroyed animation"), SpriteGroup/Name (its own world sprite), Occludes,
/// IsSolid, IsTwoHanded, IsStackable/AllowElevation - exactly the same real
/// parent relationship Book already uses, per spec, not a separate,
/// reinvented set of fields.
/// </summary>
public class ContainerData : PlaceableItemBase
{
    /// <summary>Real Exult distinction, per spec (rule 1 vs rule 2) - a body container (a corpse, an Avatar's own body) lets you take, use, and identify what's inside, but never lets you drop something INTO it. A normal container (chest, bag, drawer) allows both directions freely.</summary>
    public bool IsBodyContainer { get; set; } = false;

    /// <summary>
    /// Exult objects have hit points (Shape_info obj_hp): attacked in combat
    /// mode they lose them, and at 0 the container is destroyed and what it
    /// held falls out where it stood (Container_game_object::remove_this ->
    /// extract_contents). Only when it can be destroyed (Destructible, shown
    /// in the editor as the opposite: "Cannot be destroyed").
    /// </summary>
    public int HitPoints { get; set; } = 20;
    /// <summary>Not in Exult (objects there have only hit points): each blow is reduced by 1..Defense, the way armour reduces blows to characters.</summary>
    public int Defense { get; set; }

    /// <summary>Real Exult volume/capacity system, per spec - the total volume this container can hold, separate from its OWN Volume (inherited from PlaceableItemBase, which is how much space IT takes up when carried inside something else). An item can only be dropped in while CurrentVolume() + that item's own real Volume would not exceed this.</summary>
    public double MaxVolume { get; set; } = 20;

    /// <summary>The container's own open-view background artwork (static or animated, from Sprite Editor) - same real pattern as Book's own PaperDollSpriteGroup/Name, shown behind the items when the container is open.</summary>
    public string PaperDollSpriteGroup { get; set; } = string.Empty;
    public string PaperDollSpriteName { get; set; } = string.Empty;

    /// <summary>The real, drawn boundary marking where items may appear inside the open view, per spec - reuses BookTextFrame directly (same real rectangle/ellipse/polygon/freehand shape system Book's own frames already use), never a separate, reinvented rectangle-only type.</summary>
    public BookTextFrame Frame { get; set; } = new() { Points = new List<BookPoint> { new(60, 45), new(260, 45), new(260, 195), new(60, 195) } };

    /// <summary>Real bounding box derived from Frame.Points - used wherever the runtime/editor needs a plain rectangle to size a canvas or clamp an item's position, regardless of the frame's own real drawn shape (a polygon or freehand outline is still visually accurate; only the functional placement LIMIT falls back to its bounding box).</summary>
    public (double X, double Y, double Width, double Height) GetFrameBounds()
    {
        if (Frame.Points.Count == 0) return (60, 45, 200, 150);
        double minX = Frame.Points.Min(p => p.X), maxX = Frame.Points.Max(p => p.X);
        double minY = Frame.Points.Min(p => p.Y), maxY = Frame.Points.Max(p => p.Y);
        return (minX, minY, Math.Max(20, maxX - minX), Math.Max(20, maxY - minY));
    }



    public int OpenSoundResourceId { get; set; } = -1;
    public int CloseSoundResourceId { get; set; } = -1;
    /// <summary>Real, per spec - plays once the player releases an item onto a real spot inside this container's own open view, not when the container itself opens/closes.</summary>
    public int NavigateSoundResourceId { get; set; } = -1;

    /// <summary>
    /// Real, confirmed new field, per spec (issue #3, "Add another drop
    /// down list called location... Backbag, Pouch slots and custom
    /// slots as the location to where that container goes") - which
    /// equipment slot THIS specific container prefers to go into the
    /// moment it's picked up, tried before the ordinary Right/Left Hand
    /// cascade. Null means "no preference, use the ordinary cascade
    /// exactly as before". Custom slots are a real, separate, larger
    /// piece of work (a whole extensible-slot system) not built yet, so
    /// this only offers the two real, existing container-holding slots
    /// for now - Backpack and Pouch - honestly, rather than offering a
    /// custom-slot option that doesn't functionally exist yet.
    /// </summary>
    public EquipmentSlot? PreferredEquipSlot { get; set; }

    /// <summary>
    /// Real, confirmed extension, per spec ("implement the mechanics of
    /// either arm or left arm on ANY... slots I specify... make sure it
    /// is a functional mechanic for ANY custom slots specified") - a
    /// custom slot is a real, per-CHARACTER thing (each character
    /// defines its own), while this preference is authored once on the
    /// container itself, with no single character in mind yet - so this
    /// stores the custom slot's own real NAME, not an id tied to one
    /// specific character's own list. At pickup time, whichever real
    /// character is actually picking it up is searched for a custom slot
    /// with this exact name; PreferredEquipSlot above and this are
    /// mutually exclusive - only one is ever set by the editor's own
    /// Location dropdown at a time.
    /// </summary>
    public string? PreferredCustomSlotName { get; set; }

    /// <summary>Exult lock mechanics (keyring.uc KeyInternal / UseKeyOnChest): whether it starts locked. In play it's locked or unlocked by using its key on it (an Object marked Use As: Key), or unlocked with a lockpick.</summary>
    public bool IsLocked { get; set; } = false;
    public ReferencedItemCategory RequiredKeyCategory { get; set; } = ReferencedItemCategory.Object;
    public int RequiredKeyItemId { get; set; } = -1;
    /// <summary>Exult's SOUND_KEY: plays when a key turns in this container's lock or a lockpick opens it.</summary>
    public int LockSoundResourceId { get; set; } = -1;

    /// <summary>Real pick-lock difficulty, per spec - a real, tunable parameter for how hard THIS container is to pick, compared against the picking character's own real stat at attempt time. Higher = a harder lock, needing a higher stat to have a real chance.</summary>
    public int PickLockDifficulty { get; set; } = 10;

    /// <summary>The container's real event: one or more pages, each with its own conditions and command list - the exact same real system Book already uses (BookEventPage), per spec ("IDENTICAL event editor").</summary>
    public List<BookEventPage> EventPages { get; set; } = new();

    /// <summary>Variables scoped to just this container - same real pattern as Book's own LocalVariables.</summary>
    public List<VariableDefinition> LocalVariables { get; set; } = new();
    private int _nextLocalVariableId = 1;
    public int GetNextLocalVariableId() => _nextLocalVariableId++;
    public void SyncLocalVariableIdAfterLoad() => _nextLocalVariableId = LocalVariables.Count == 0 ? 1 : LocalVariables.Max(v => v.Id) + 1;

    /// <summary>Real starting contents, per spec ("put items in the container from the book editor and they appear in the run time side, just like... the PC starting inventory") - authored here, seeded into the real runtime ObjectInstance the first time this container is placed and opened, exactly mirroring CharacterDefinition's own Inventory/Starting Inventory pattern.</summary>
    public List<InventoryItemInstance> StartingContents { get; set; } = new();
    private int _nextContentsInstanceId = 1;
    public int GetNextContentsInstanceId() => _nextContentsInstanceId++;
    public void SyncNextContentsInstanceIdAfterLoad() => _nextContentsInstanceId = StartingContents.Count == 0 ? 1 : StartingContents.Max(i => i.InstanceId) + 1;
}

public class ContainerDatabase
{
    public List<ContainerData> Containers { get; set; } = new();

    private int _nextId = 1;

    public ContainerData CreateNew()
    {
        var container = new ContainerData { Id = _nextId++, Name = $"Container {_nextId - 1}" };
        Containers.Add(container);
        return container;
    }

    public void SyncNextIdAfterLoad()
    {
        _nextId = Containers.Count == 0 ? 1 : Containers.Max(c => c.Id) + 1;
        foreach (var container in Containers)
        {
            container.SyncLocalVariableIdAfterLoad();
            container.SyncNextContentsInstanceIdAfterLoad();
        }
    }
}
