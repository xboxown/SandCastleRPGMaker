using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Everything an event can test (Advance IF, a Complex Condition's Game
/// Condition, a page's "Until Condition Met"), numbered as the designer's
/// list: 1-27 for every event, then the ones about doors, containers and
/// lights. "Equipped / Unequipped" are what is so now; "Equip / Unequip"
/// are things that happened (an item put on or taken off).
/// </summary>
public enum GameConditionKind
{
    Variable = 1,
    Equipped,
    NotHavingItem,
    Gender,
    Age,
    Destroyed,
    Stolen,
    Used,
    Stat,
    ExplosiveIgnited,
    SeesCharacter,
    Unequipped,
    CharacterKilled,
    SoundPlayed,
    Picklocked,
    LeverOnOff,
    TimeDate,
    SoundStopped,
    UnequipAction,
    EquipAction,
    SoldTo,
    TradedTo,
    TradedWithin,
    EventExecuted,
    EventPageExecuted,
    EventNamePageExecuted,
    HavingItem,
    // ---- Doors, containers, lights ----
    DoorOpen,
    UnlockedByKey,
    MagicallyUnlocked,
    LightBurnedOut,
    LightPutOut,
    LightLit
}

/// <summary>One test, with whichever of its settings its kind uses.</summary>
public class GameCondition
{
    public GameConditionKind Kind { get; set; } = GameConditionKind.Variable;
    /// <summary>The opposite: true when it isn't so.</summary>
    public bool Not { get; set; }
    /// <summary>In a list: how it joins the next one.</summary>
    public ComplexLogicOperator NextOperator { get; set; } = ComplexLogicOperator.And;

    /// <summary>Who: a character's id, MainPlayer (the one playing, "you"), or Anyone.</summary>
    public int CharacterId { get; set; } = MainPlayer;
    public const int MainPlayer = -2, Anyone = -1;
    /// <summary>The item / object it's about (Select Item).</summary>
    public ItemReference? Item { get; set; }
    public string VariableName { get; set; } = string.Empty;
    public ConditionComparisonOperator Comparison { get; set; } = ConditionComparisonOperator.Equal;
    /// <summary>A value to compare with (a number, or True / False, or text).</summary>
    public string Value { get; set; } = string.Empty;
    public int StatId { get; set; } = -1;
    public CharacterGender Gender { get; set; } = CharacterGender.Male;
    /// <summary>A number: an age, at least how many times, an hour, a page...</summary>
    public int Number { get; set; } = 1;
    /// <summary>A second number: an hour "to", within how many hours (0 = ever).</summary>
    public int Number2 { get; set; }
    public int SoundId { get; set; } = -1;
    /// <summary>On (true) or off (false) - a lever, a light, a door open.</summary>
    public bool On { get; set; } = true;
    public string EventName { get; set; } = string.Empty;

    public GameCondition Clone() => JsonSerializer.Deserialize<GameCondition>(JsonSerializer.Serialize(this)) ?? new GameCondition();
}

public static class GameConditions
{
    /// <summary>The name of each kind, as the designer's list.</summary>
    public static string Name(GameConditionKind kind) => kind switch
    {
        GameConditionKind.Variable => "Variable",
        GameConditionKind.Equipped => "Equipped item",
        GameConditionKind.NotHavingItem => "Not having an item",
        GameConditionKind.Gender => "Gender",
        GameConditionKind.Age => "Age",
        GameConditionKind.Destroyed => "Destroyed object",
        GameConditionKind.Stolen => "Stolen object",
        GameConditionKind.Used => "Used object",
        GameConditionKind.Stat => "Stat (alignment...)",
        GameConditionKind.ExplosiveIgnited => "Ignited explosive",
        GameConditionKind.SeesCharacter => "Seeing a character",
        GameConditionKind.Unequipped => "Unequipped item (carried, not worn)",
        GameConditionKind.CharacterKilled => "Character killed",
        GameConditionKind.SoundPlayed => "Sound played",
        GameConditionKind.Picklocked => "Picklocked door / container",
        GameConditionKind.LeverOnOff => "Switch / button / lever on or off",
        GameConditionKind.TimeDate => "Time / date",
        GameConditionKind.SoundStopped => "Sound stopped",
        GameConditionKind.UnequipAction => "Unequip (an item taken off)",
        GameConditionKind.EquipAction => "Equip (an item put on)",
        GameConditionKind.SoldTo => "Sold item to a character",
        GameConditionKind.TradedTo => "Traded item to a character",
        GameConditionKind.TradedWithin => "Traded item within a period",
        GameConditionKind.EventExecuted => "Event executed",
        GameConditionKind.EventPageExecuted => "Event page number executed",
        GameConditionKind.EventNamePageExecuted => "Event name + page number executed",
        GameConditionKind.HavingItem => "Having an item",
        GameConditionKind.DoorOpen => "Door / container open",
        GameConditionKind.UnlockedByKey => "Unlocked by key",
        GameConditionKind.MagicallyUnlocked => "Magically unlocked",
        GameConditionKind.LightBurnedOut => "Light burnt out",
        GameConditionKind.LightPutOut => "Light put out",
        GameConditionKind.LightLit => "Light lit",
        _ => kind.ToString()
    };

    public static readonly GameConditionKind[] All = Enum.GetValues<GameConditionKind>();

    /// <summary>A list joined by And / Or / Xor, left to right (empty: true).</summary>
    public static bool EvaluateAll(IReadOnlyList<GameCondition> conditions, Func<GameCondition, bool> evaluate)
    {
        if (conditions.Count == 0) return true;
        bool result = evaluate(conditions[0]);
        for (int i = 1; i < conditions.Count; i++)
        {
            bool next = evaluate(conditions[i]);
            result = conditions[i - 1].NextOperator switch
            {
                ComplexLogicOperator.Or => result || next,
                ComplexLogicOperator.Xor => result ^ next,
                _ => result && next
            };
        }
        return result;
    }

    /// <summary>An Advance IF command's condition (stored as JSON in its "Condition" parameter).</summary>
    public static GameCondition? FromCommand(EventCommand command)
    {
        try { return command.Get("Condition") is { Length: > 0 } json ? JsonSerializer.Deserialize<GameCondition>(json) : null; }
        catch { return null; }
    }

    public static void ToCommand(EventCommand command, GameCondition condition) => command.Parameters["Condition"] = JsonSerializer.Serialize(condition);

    public static bool Compare(double left, ConditionComparisonOperator op, double right) => ScheduleRule.Compare(left, op, right);

    public static string Symbol(ConditionComparisonOperator op) => op switch
    {
        ConditionComparisonOperator.Equal => "=", ConditionComparisonOperator.NotEqual => "!=", ConditionComparisonOperator.GreaterThan => ">",
        ConditionComparisonOperator.GreaterOrEqual => ">=", ConditionComparisonOperator.LessThan => "<", _ => "<="
    };
}

/// <summary>An execution context that can test game conditions (the game itself; the editor's preview can't).</summary>
public interface IGameConditionContext
{
    bool EvaluateGameCondition(GameCondition condition);
}
