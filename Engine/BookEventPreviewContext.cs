namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Simulated game state for the Preview button to test a page's conditions
/// against. There is no real running game yet - VariableDatabase/StatDatabase
/// are just DECLARATIONS (name + type), never runtime values - so this is
/// the only source of truth Preview has. Session-only, never saved: it's a
/// test fixture, not project data.
/// </summary>
public class BookEventPreviewContext
{
    /// <summary>VariableId -> simulated value as raw text (parsed per that condition row's own Mode: numeric for Variable, literal for String, "True"/"False" for Boolean).</summary>
    public Dictionary<int, string> VariableValues { get; set; } = new();

    /// <summary>StatId -> simulated value.</summary>
    public Dictionary<int, int> StatValues { get; set; } = new();

    public bool CombineItemSlot1Present { get; set; }
    public bool CombineItemSlot2Present { get; set; }
    public bool EventCompletedActual { get; set; }

    /// <summary>Shared by both Distance Condition and Joined Party's proximity check, since neither has any other source of a real position in the editor.</summary>
    public double SimulatedDistance { get; set; }

    public bool TriggerActuallyOccurred { get; set; } = true;
    public bool UntilConditionStillMet { get; set; } = true;
    public int TimesTriggeredSoFar { get; set; }

    /// <summary>Was the item Drop To Self targets actually dropped on this book - simulated, since there's no real drop-onto-another-item mechanic to observe yet. Replaces an earlier "assumed PASS" that was never actually checked against anything.</summary>
    public bool DropToSelfItemPresent { get; set; }

    /// <summary>Which momentary trigger type (Use/Pickup/Drop) just happened this instant, if any - drives both the top-level Trigger Conditions Based check and any Complex Condition sub-condition referencing the same trigger types.</summary>
    public BookTriggerConditionBased? MomentaryTriggerJustOccurred { get; set; }

    /// <summary>Persistent "has been read at least once this session" flag for By After Read Item - used both at the top level and inside a Complex Condition.</summary>
    public bool HasBeenRead { get; set; }

    /// <summary>This book's own database Id, needed to check whether a trigger-type sub-condition's Select Item target is "this book itself" rather than some other item this context can't speak to.</summary>
    public int SelfItemId { get; set; } = -1;
}
