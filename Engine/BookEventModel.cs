namespace SandcastleRpgMaker.Engine;

/// <summary>What kind of item-interaction can trigger a book's event page. Book-specific - see design doc "Trigger Conditions Based". Always is the default - "Select Item" only appears for the other 6.</summary>
public enum BookTriggerConditionBased
{
    Always,
    ByTradeItem,
    ByUseItem,
    ByDropItem,
    ByDestroyItem,
    ByAfterReadItem,
    ByPickupItem,
    ByHavingAnItem,
    ByNotHavingAnItem,
    /// <summary>Player steps onto this event/tile's cell. Genuinely active from day one - no simulated data required beyond what the Debug window's existing generic "Trigger occurred" checkbox already provides.</summary>
    ByEnter,
    /// <summary>Fires repeatedly while the player remains on the cell - same "OnStay" concept as CustomTileEffect's own trigger, now available as a full scripted event condition too, not just a damage-tick rule.</summary>
    ByStay,
    /// <summary>Player steps off the cell.</summary>
    ByExit,

    // ---- Character events only (Character Editor > Event) ----
    /// <summary>Something was equipped in one of this character's slots.</summary>
    ByEquip,
    /// <summary>Something left one of this character's slots.</summary>
    ByUnEquip,
    /// <summary>This character was hit in combat (a blow that landed).</summary>
    ByHit,
    /// <summary>This character caught an ailment (poisoned, asleep, paralyzed, charmed, cursed).</summary>
    ByAilmentEffect,
    /// <summary>This character died.</summary>
    ByDeath,

    // ---- Paperdoll parts only (PaperDoll Editor > a part's events) ----
    /// <summary>An item (from the world, a pack, or the paperdoll itself) was dropped on this part.</summary>
    ByCombineItem,
    /// <summary>Another part of the paperdoll (one that can be dragged) was dropped on this part.</summary>
    ByCombinePart,

    // ---- Ammo and projectiles ----
    /// <summary>It hit its target - a character or an object (BookEventConditions.HitTarget says which).</summary>
    ByHitTarget,

    // ---- Ultima 8 projects only (Pentagram's usecode events) ----
    /// <summary>An object was looked at (a single click).</summary>
    ByLook,
    /// <summary>An object was struck.</summary>
    ByGotHit,
    /// <summary>This character cast a spell.</summary>
    ByCast,
    /// <summary>This character came onto the screen.</summary>
    ByEnterView,
    /// <summary>This character left the screen.</summary>
    ByLeaveView,
    /// <summary>Something of this character's was stolen.</summary>
    ByStolen
}

/// <summary>What By Hitting Target waits for.</summary>
public enum HitTargetKind { Any, Character, Object }

public static class DollTriggers
{
    /// <summary>Triggers shown only where an editor names them: a paperdoll part's (combining), an object's (By Item Combined To It, By Hitting Target).</summary>
    public static readonly BookTriggerConditionBased[] All = { BookTriggerConditionBased.ByCombineItem, BookTriggerConditionBased.ByCombinePart, BookTriggerConditionBased.ByHitTarget };

    public static bool IsDollTrigger(BookTriggerConditionBased trigger) => All.Contains(trigger);
}

public static class CharacterTriggers
{
    /// <summary>The triggers a character's own events use - and only a character's.</summary>
    public static readonly BookTriggerConditionBased[] All =
    {
        BookTriggerConditionBased.ByEquip, BookTriggerConditionBased.ByUnEquip, BookTriggerConditionBased.ByHit,
        BookTriggerConditionBased.ByAilmentEffect, BookTriggerConditionBased.ByDeath
    };

    public static bool IsCharacterTrigger(BookTriggerConditionBased trigger) => All.Contains(trigger);
}

public enum ConditionComparisonOperator { Equal, NotEqual, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual }

public enum ConditionLogicOperator { And, Or }

// ---------- Checkmark 1: In A Condition ----------

public enum VariableConditionMode { Variable, String, Boolean }

public enum VariableScope { Local, Global }

/// <summary>One row of the "In A Condition" list - references a real project variable (by id, so a rename elsewhere doesn't break it) plus the value being compared against.</summary>
public class VariableConditionEntry
{
    public VariableConditionMode Mode { get; set; } = VariableConditionMode.Variable;
    public VariableScope Scope { get; set; } = VariableScope.Local;
    public int VariableId { get; set; } = -1;
    public string VariableDisplayName { get; set; } = string.Empty;
    public ConditionComparisonOperator Comparison { get; set; } = ConditionComparisonOperator.Equal;

    /// <summary>The comparison value, stored as text regardless of mode (parsed per-mode: number for Variable, raw string for String, "True"/"False" for Boolean).</summary>
    public string Value { get; set; } = string.Empty;

    public ConditionLogicOperator NextOperator { get; set; } = ConditionLogicOperator.And;
}

// ---------- Checkmark 2: Event Trigger Completed ----------

/// <summary>Points at one specific page of one specific book's event - "the list of all events made in the editor" the design doc describes.</summary>
public class BookEventPageReference
{
    public int BookId { get; set; } = -1;
    public int PageIndex { get; set; } = -1;
    public string DisplayName { get; set; } = string.Empty;
}

// ---------- Checkmark 3: Stat Condition ----------

/// <summary>One row of the stat comparison list - one per stat in the global stat list; IsActive means this row actually participates (an unchecked/blank row is ignored).</summary>
public class StatConditionEntry
{
    public int StatId { get; set; }
    public bool IsActive { get; set; }
    public ConditionComparisonOperator Comparison { get; set; } = ConditionComparisonOperator.Equal;
    public int Value { get; set; }
}

// ---------- Checkmark 4: Combine Item Condition ----------

/// <summary>One of the two item slots in Combine Item Condition - IsSelf means "this book itself", which disables picking anything else for this slot.</summary>
public class CombineItemSlot
{
    public bool IsSelf { get; set; }
    public ItemReference? Item { get; set; }
}

// ---------- Checkmark 7: Complex Condition ----------

public enum ComplexSubConditionType
{
    InACondition, EventTriggerCompleted, StatCondition, CombineItemCondition, DropToSelfCondition, JoinedPartyCondition,
    Always, ByTradeItem, ByUseItem, ByDropItem, ByDestroyItem, ByAfterReadItem, ByPickupItem, ByHavingAnItem, ByNotHavingAnItem,
    /// <summary>Any of the game conditions (Advance IF's list: variables, items, characters, what has happened...).</summary>
    GameCondition
}

/// <summary>And/Or/Xor - Xor added beyond the base spec (true only when exactly one side is true), since it's a natural extra given the other two were already there.</summary>
public enum ComplexLogicOperator { And, Or, Xor }

/// <summary>
/// One atomic building block inside a Complex Condition - reuses the exact
/// same data types the top-level checkmarks use, just one instance at a
/// time instead of "all of them always shown", so any number of the same
/// type can be combined (e.g. multiple Combine Item checks, or multiple
/// Event references) alongside each other with And/Or/Xor.
/// </summary>
public class ComplexSubCondition
{
    public ComplexSubConditionType Type { get; set; } = ComplexSubConditionType.InACondition;

    /// <summary>NOT this sub-condition's result - added beyond the base spec for cheap extra expressiveness.</summary>
    public bool Invert { get; set; }

    /// <summary>How this connects to the NEXT sub-condition in the list (ignored on the last one).</summary>
    public ComplexLogicOperator NextOperator { get; set; } = ComplexLogicOperator.And;

    // Only the field matching Type above is relevant/populated.
    public VariableConditionEntry InACondition { get; set; } = new();
    public BookEventPageReference? EventCompletedReference { get; set; }
    public bool EventCompletedExpectedState { get; set; } = true;
    public StatConditionEntry StatCondition { get; set; } = new();
    public CombineItemSlot CombineItemSlot1 { get; set; } = new();
    public CombineItemSlot CombineItemSlot2 { get; set; } = new();
    public ItemReference? DropToSelfItemReference { get; set; }
    public int JoinedPartyNpcId { get; set; } = -1;
    public string JoinedPartyNpcName { get; set; } = string.Empty;

    /// <summary>Select Item target for the new trigger-type sub-conditions that need one (Trade/Drop/Destroy/Pickup/Having/Not Having) - mirrors BookEventConditions.TriggerItemReference at the top level. Always/Use/After Read don't use this - they're inherently about the book itself.</summary>
    public ItemReference? TriggerItemReference { get; set; }

    /// <summary>Type GameCondition: the condition.</summary>
    public GameCondition Game { get; set; } = new();
}

/// <summary>
/// One named, independent, reusable Complex Condition group - a whole
/// bundle of sub-conditions combined with And/Or/Xor. Multiple groups can
/// exist per event page (each named and edited separately), matching
/// "group all of that under one category with a name... add as many
/// complex conditions as you like".
/// </summary>
public class ComplexConditionGroup
{
    public string Name { get; set; } = "New Complex Condition";
    public List<ComplexSubCondition> SubConditions { get; set; } = new();
}

public enum CycleConditionType { Once, SpecificNumberOfTimes, UntilConditionMet, Always }

/// <summary>
/// Everything that determines WHEN one of a book's event pages is active -
/// the "left side" panel. Deliberately book-specific rather than shared,
/// per the architecture: the reusable Event Editor engine only owns the
/// command list (the "right side"); each consuming editor (Book, later NPC,
/// Item, etc.) defines and owns its own conditions.
///
/// Condition Trigger Type is a set of independently-checkable conditions
/// (any combination allowed), not a single choice - each flag below turns
/// its own sub-editor on/off. ComplexCondition is deliberately left
/// unbuilt/disabled for now, per explicit instruction to save it for last.
/// </summary>
public class BookEventConditions
{
    public BookTriggerConditionBased TriggerConditionBased { get; set; } = BookTriggerConditionBased.Always;

    /// <summary>Which specific item this trigger targets - only relevant/visible when TriggerConditionBased isn't Always.</summary>
    public ItemReference? TriggerItemReference { get; set; }

    /// <summary>By Enter / By Stay / By Exit: who stepping on it sets it off - "" anyone, "Player" the main character only, "Party" anyone in the party, or a character's id.</summary>
    public string SteppedOnBy { get; set; } = "";

    /// <summary>A paperdoll part's By Combined With A Part: only when this part (by id) is dragged onto it (-1 = any part).</summary>
    public int CombinedPartId { get; set; } = -1;

    /// <summary>By Hitting Target: only when it hits a character, an object, or either.</summary>
    public HitTargetKind HitTarget { get; set; } = HitTargetKind.Any;

    // Checkmark 1: In A Condition
    public bool ConditionInACondition { get; set; }
    public List<VariableConditionEntry> VariableConditions { get; set; } = new();

    // Checkmark 2: Event Trigger Completed
    public bool ConditionEventTriggerCompleted { get; set; }
    public BookEventPageReference? EventCompletedReference { get; set; }
    /// <summary>True = the referenced event must have COMPLETED for this one to run. False = this one runs WHILE the referenced event is still executing, and stops once it finishes.</summary>
    public bool EventCompletedExpectedState { get; set; } = true;

    // Checkmark 3: Stat Condition
    public bool ConditionStatCondition { get; set; }
    public List<StatConditionEntry> StatConditions { get; set; } = new();

    // Checkmark 4: Combine Item Condition
    public bool ConditionCombineItem { get; set; }
    public CombineItemSlot CombineItemSlot1 { get; set; } = new();
    public CombineItemSlot CombineItemSlot2 { get; set; } = new();

    // Checkmark 5: Drop To Self Condition - fires when THIS specific item is dropped
    // on the book. Originally spec'd as "no parameter" (any item), corrected per
    // feedback to need the same Select Item picker used elsewhere.
    public bool ConditionDropToSelf { get; set; }
    public ItemReference? DropToSelfItemReference { get; set; }

    // Checkmark 6: Joined Party Condition - despite the name, this is a PROXIMITY trigger
    // (fires when the player and this book are physically close), not literal party membership.
    public bool ConditionJoinedParty { get; set; }
    public int JoinedPartyNpcId { get; set; } = -1;
    public string JoinedPartyNpcName { get; set; } = string.Empty;

    // Checkmark 8: Distance Condition - a standalone numeric proximity
    // threshold (distance from the player to this book), independent of
    // Joined Party above (which is keyed to a specific NPC instead).
    // Float, 0.00 to 4,000,000,000 inclusive.
    public bool ConditionDistance { get; set; }
    public double DistanceValue { get; set; }

    // Checkmark 7: Complex Condition
    public bool ConditionComplex { get; set; }
    public List<ComplexConditionGroup> ComplexConditions { get; set; } = new();

    public CycleConditionType CycleCondition { get; set; } = CycleConditionType.Once;
    /// <summary>Until Condition Met: the page keeps going until these (And / Or / Xor) are met.</summary>
    public List<GameCondition> UntilConditions { get; set; } = new();
    public int CycleSpecificTimes { get; set; } = 1;
}

/// <summary>
/// One page of a book's event: its own conditions (when it's active) and its
/// own command list (what it does) - RPG-Maker-style multi-page events. The
/// Commands list reuses the SAME EventCommand/EventCommandType the shared
/// engine defines, so the command side is genuinely shared across every
/// future editor even though BookEventPage itself is book-specific.
/// </summary>
public class BookEventPage
{
    public BookEventConditions Conditions { get; set; } = new();
    public List<EventCommand> Commands { get; set; } = new();
}
