namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Real Ultima 7 / Exult equipment slots, confirmed directly from Exult's
/// own source (combat.cc: lhand, rhand, belt, backpack are real, verbatim
/// slot names used there) plus the standard additional body slots the
/// same paperdoll system uses (head, neck, body, arms, gloves, legs,
/// feet, rings) - not invented, the same real category set BG/SI's own
/// paperdoll works from. Arms and Gloves are deliberately separate
/// slots, per spec - upper-arm/bracer armor and hand coverings are two
/// different worn items, not one. There is deliberately no separate
/// "both hands" slot - confirmed from Exult's own real ChangeLog ("make
/// sure non-party members equip a shield if they have one and aren't
/// using a two handed weapon") and forum discussion of ranged weapons
/// needing a free hand to reload: a two-handed (or two-hands-required
/// ranged) item simply occupies RightHand and, while it's there, makes
/// LeftHand unavailable - see GameTestWindow's own PaperdollSlotClicked/
/// IsPaperdollLeftHandLocked for the real enforcement, not a separate
/// slot pretending to be two slots at once.
/// </summary>
public enum EquipmentSlot
{
    Head,
    Neck,
    Body,
    Arms,
    Gloves,
    Belt,
    Back,
    Legs,
    Feet,
    RightHand,
    LeftHand,
    Ring1,
    Ring2,
    Backpack,
    /// <summary>Real, confirmed new slot, per spec ("Add one more slot: Pouch. This is for small containers") - a second, separate container-holding slot alongside Backpack, same real rules (checked by real volume before falling back to hands).</summary>
    Pouch
}

/// <summary>
/// One item a character is carrying - in their backpack, or currently
/// equipped in a slot (EquippedSlots below points at this same instance
/// by InstanceId; an item isn't duplicated between "inventory" and
/// "equipped", it just gets a slot assigned). Mirrors PlacedItemInstance's
/// own ItemId/Category pattern (a reference into the Book/Weapon/Armor/
/// Item database, not a copy of the data itself) for the same reason:
/// one real source of truth per item definition.
/// </summary>
/// <summary>Real, shared shape, per spec - both a placed map container (PlacedItemInstance) and a picked-up, inventory-held one (InventoryItemInstance) are genuinely "a container with real, live Contents" the exact same way; this lets the one, single, already-proven container-gump machinery (RefreshContainerGump, drag in/out, volume checks) work identically against either kind, without ever needing to know or care which one it actually has.</summary>
public interface IHasContainerContents
{
    List<InventoryItemInstance> Contents { get; set; }
}

/// <summary>Something that can carry an object's paperdoll state (PaperDoll Editor): a placed object, or a carried one.</summary>
public interface IHasDollState
{
    DollState? DollRuntime { get; set; }
}

public class InventoryItemInstance : IHasContainerContents, IHasDollState
{
    public int InstanceId { get; set; }
    public ReferencedItemCategory Category { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; } = 1;

    /// <summary>Real, free-form position within a container's own Frame boundary - per spec, items appear on top of each other inside a container, at wherever they were actually dropped, not in a fixed slot grid. Meaningless for a character's own Inventory/EquippedSlots (never read there); only used when this instance lives inside a container's Contents.</summary>
    public double ContainerX { get; set; } = 0;
    public double ContainerY { get; set; } = 0;

    /// <summary>Real, confirmed fix - only meaningful when this instance's own Category is Container: what THIS specific container is itself holding, exactly the same real Contents concept a placed container has (PlacedItemInstance.Contents), just carried here instead once it's been picked up and is sitting in someone's own Inventory/EquippedSlots. Without this, picking up a chest silently lost everything inside it - there was nowhere for it to live at all.</summary>
    public List<InventoryItemInstance> Contents { get; set; } = new();
    /// <summary>A spellbook's spells learned by this copy (Learn Spell) - used when its type doesn't start with all its spells.</summary>
    public List<string>? LearnedSpellsRuntime { get; set; }

    /// <summary>A carried light source's state in play (lit, minutes of burning left, burnt out) - travels with it between the map and inventories, as an ObjectInstance's does.</summary>
    public bool? IsLitRuntime { get; set; }
    public double BurnLeftRuntime { get; set; } = -1;
    public bool IsSpentRuntime { get; set; }
    /// <summary>Frame shown, for objects whose frames are states (a bed cover).</summary>
    public int FrameRuntime { get; set; }
    /// <summary>Which way it faces (a chair turned east stays turned east in a backpack and when put back down) - Exult keeps an object's frame wherever it goes.</summary>
    public SpriteDirection FacingRuntime { get; set; } = SpriteDirection.South;
    /// <summary>Hit points left once struck (-1 = untouched) - damage goes with it.</summary>
    public int HitPointsRuntime { get; set; } = -1;
    /// <summary>A keychain's keys, a useable object's charges left, a liquid container's liquid - they go with it (see ObjectInstance).</summary>
    public List<int>? KeyIdsRuntime { get; set; }
    public int ChargesRuntime { get; set; } = -1;
    public int? LiquidIdRuntime { get; set; }
    /// <summary>Whose it is (a placed copy's Belongs to), carried with it: Mary's shears stay Mary's in a worker's hand and when put back. -1 = as the editor says.</summary>
    public int BelongsToId { get; set; } = -1;

    /// <summary>Per-instance lock state in play - only meaningful for a Container. Null until a key or lockpick changes it (it's then as the container's IsLocked says). Travels with the container wherever it goes, as in Exult, where a locked chest is its own shape.</summary>
    public bool? IsLockedRuntime { get; set; }

    /// <summary>An object's own paperdoll as it is now (a radio's door open, its tape in) - goes with it (see ObjectInstance).</summary>
    public DollState? DollRuntime { get; set; }
}

/// <summary>Where one equipment slot's icon draws on the paperdoll canvas when something is equipped there - real, authored, per-character pixel position (not derived from anything), so the designer can genuinely move each slot around to fit their own paperdoll body art, per spec.</summary>
public class PaperdollSlotPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Real, confirmed new type, per spec - a designer-named slot beyond the fixed built-in EquipmentSlot list, with its own real, independently-draggable position on this paperdoll. Id exists purely so a rename never orphans whatever a character already has equipped there (CharacterDefinition.CustomEquippedSlots is keyed by this Id, not by the display Name, which can change freely).</summary>
public class CustomEquipmentSlot
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Slot";
    public double X { get; set; } = 140;
    public double Y { get; set; } = 10;
}

/// <summary>
/// A character's real paperdoll setup - confirmed directly from Exult's
/// own paperdoll.vga + paperdoll_info.txt model: a base body sprite
/// (chosen from the project's own sprite library, same as FaceSprite
/// already is) with each equipment slot's icon position authored
/// separately on top of it. Defaults lay every slot out in a simple
/// vertical list so a freshly-created character has a usable, visible
/// starting layout rather than every slot stacked at (0,0).
/// </summary>
public class PaperdollDefinition
{
    public string BodySpriteGroup { get; set; } = string.Empty;
    public string BodySpriteName { get; set; } = string.Empty;

    /// <summary>Real, configurable exit-button art, per spec - a 2-frame sprite (frame 1 = unpressed, frame 2 = pressed while held down, returning to frame 1 on release before the gump actually closes), picked from the sprite library exactly like the body is, rather than a hardcoded placeholder glyph.</summary>
    public string ExitButtonSpriteGroup { get; set; } = string.Empty;
    public string ExitButtonSpriteName { get; set; } = string.Empty;

    /// <summary>Real, per spec - plays once the player releases an item onto a real slot on this character's own paperdoll (dropping it back into a hand, say), same real principle as a container's own Navigate Sound.</summary>
    public int NavigateSoundResourceId { get; set; } = -1;

    /// <summary>Plays when this character's paperdoll is opened in the game (a Sound resource Id; -1 = none).</summary>
    public int OpenSoundResourceId { get; set; } = -1;

    public Dictionary<EquipmentSlot, PaperdollSlotPosition> SlotPositions { get; set; } = DefaultLayout();

    /// <summary>Real, confirmed new feature, per spec ("Add a feature for custom slots and the ability to delete custom slots by selecting one and pressing Delete... Custom slots are colored green") - designer-defined slots beyond the fixed built-in list, each with its own real name and its own real, draggable position, exactly like a built-in slot.</summary>
    public List<CustomEquipmentSlot> CustomSlots { get; set; } = new();

    /// <summary>Built-in slots this paperdoll doesn't have (one ring, no pouch...): not drawn, nothing goes in them. They can be turned back on, never deleted.</summary>
    public List<EquipmentSlot> DisabledSlots { get; set; } = new();

    public bool IsSlotShown(EquipmentSlot slot) => !DisabledSlots.Contains(slot);

    /// <summary>What this character carries (stones), and the most they can (2 x Strength) - numbers drawn on the paperdoll in a font.</summary>
    public PaperdollReadout WeightCurrent { get; set; } = new() { X = 170, Y = 330, Align = ReadoutAlign.Right };
    public PaperdollReadout WeightMax { get; set; } = new() { X = 180, Y = 330 };
    /// <summary>The same as a bar, a pie, or the frames of a sprite (empty to full).</summary>
    public PaperdollGauge WeightGauge { get; set; } = new() { X = 120, Y = 360 };

    /// <summary>Free labels anywhere on the paperdoll: typed text, or the value of a global variable (Paperdoll layout > Text and numbers).</summary>
    public List<PaperdollLabel> Labels { get; set; } = new();

    /// <summary>Exult's Actor_gump buttons: combat on/off and the disk (save / load) are the player's only; the heart (stats), the halo (protect this one) and the combat behavior are on everyone's.</summary>
    public PaperdollButton CombatButton { get; set; } = new() { X = 230, Y = 360 };
    public PaperdollButton CombatModeButton { get; set; } = new() { X = 60, Y = 360 };
    public PaperdollButton StatsButton { get; set; } = new() { X = 140, Y = 360 };
    public PaperdollButton DiskButton { get; set; } = new() { X = 260, Y = 360 };
    public PaperdollButton HaloButton { get; set; } = new() { X = 260, Y = 300 };

    /// <summary>The stats paperdoll (Z, or the heart).</summary>
    public StatsDollDefinition StatsDoll { get; set; } = new();

    private int _nextCustomSlotId = 1;
    /// <summary>Never serialized (a plain field, not a property) - resynced after load the same real way every other per-instance id counter in this project already is, from whatever the highest existing id actually is.</summary>
    public int GetNextCustomSlotId()
    {
        if (CustomSlots.Count > 0) _nextCustomSlotId = Math.Max(_nextCustomSlotId, CustomSlots.Max(s => s.Id) + 1); // never reuse an id after a load
        return _nextCustomSlotId++;
    }
    public void SyncNextCustomSlotIdAfterLoad() => _nextCustomSlotId = CustomSlots.Count == 0 ? 1 : CustomSlots.Max(s => s.Id) + 1;

    private static Dictionary<EquipmentSlot, PaperdollSlotPosition> DefaultLayout()
    {
        var slots = Enum.GetValues<EquipmentSlot>();
        var layout = new Dictionary<EquipmentSlot, PaperdollSlotPosition>();
        for (int i = 0; i < slots.Length; i++)
            layout[slots[i]] = new PaperdollSlotPosition { X = 140, Y = 10 + i * 28 };
        return layout;
    }
}

public enum ReadoutAlign { Left, Center, Right }

/// <summary>A number drawn on a paperdoll (carried stones, the most they can carry): where, in which font (Sprite Editor > Fonts), how big, tinted how.</summary>
public class PaperdollReadout
{
    public bool Enabled { get; set; }
    /// <summary>Where it's drawn, in the paperdoll's 320x400 layout: its left edge, centre or right edge (Align), and its top.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public ReadoutAlign Align { get; set; } = ReadoutAlign.Left;
    /// <summary>A font of the Sprite Editor's Fonts group; empty = the in-game font.</summary>
    public string FontName { get; set; } = string.Empty;
    public int Scale { get; set; } = 1;
    /// <summary>#RRGGBB, or empty for the font's own colours.</summary>
    public string Tint { get; set; } = string.Empty;
}

public enum PaperdollLabelKind { Text, Number }

/// <summary>A label placed on the paperdoll, drawn like the weight numbers (font, size, tint, alignment): text typed in, or a global variable's value, kept up to date in play.</summary>
public class PaperdollLabel : PaperdollReadout
{
    public PaperdollLabelKind Kind { get; set; } = PaperdollLabelKind.Text;
    public string Text { get; set; } = "Text";
    /// <summary>Number: the global variable shown (Variables), by id; -1 none.</summary>
    public int VariableId { get; set; } = -1;
}

public enum GaugeKind { Bar, Pie, SpriteFrames }

/// <summary>An amount shown as a picture (how full the pack is): a bar filling left to right, a pie, or a sprite whose frames go from empty (first) to full (last) - like the bundle pictures of gold and arrows.</summary>
public class PaperdollGauge
{
    public bool Enabled { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public GaugeKind Kind { get; set; } = GaugeKind.Bar;
    public double Width { get; set; } = 80;
    public double Height { get; set; } = 10;
    public string FillColor { get; set; } = "#C8A040";
    public string BackColor { get; set; } = "#402818";
    public SpriteLink Sprite { get; set; } = new();
}
