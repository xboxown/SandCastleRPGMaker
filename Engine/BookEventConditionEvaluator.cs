namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Evaluates one page's BookEventConditions for real, against a supplied
/// BookEventPreviewContext. Every checkbox that's turned on is AND'd
/// together with the others - the design doc describes them as
/// independently checkable, and AND-of-all-active-conditions is the
/// standard reading of that (an OR reading would make most combinations of
/// checked boxes trivially always pass). trace collects one readable line
/// per condition actually evaluated, in order, so Preview can show exactly
/// why a page did or didn't fire instead of just a final yes/no.
/// </summary>
public static class BookEventConditionEvaluator
{
    public static bool Evaluate(BookEventConditions c, BookEventPreviewContext ctx, List<string> trace)
    {
        bool result = true;

        if (c.TriggerConditionBased != BookTriggerConditionBased.Always)
        {
            bool pass = ctx.TriggerActuallyOccurred;
            trace.Add($"Trigger ({c.TriggerConditionBased}): simulated occurred = {pass} -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionInACondition)
        {
            bool pass = EvaluateVariableConditions(c.VariableConditions, ctx, trace);
            trace.Add($"In A Condition -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionEventTriggerCompleted)
        {
            bool pass = ctx.EventCompletedActual == c.EventCompletedExpectedState;
            trace.Add($"Event Trigger Completed: expected {c.EventCompletedExpectedState}, simulated {ctx.EventCompletedActual} -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionStatCondition)
        {
            bool pass = EvaluateStatConditions(c.StatConditions, ctx, trace);
            trace.Add($"Stat Condition -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionCombineItem)
        {
            bool slot1 = c.CombineItemSlot1.IsSelf || ctx.CombineItemSlot1Present;
            bool slot2 = c.CombineItemSlot2.IsSelf || ctx.CombineItemSlot2Present;
            bool pass = slot1 && slot2;
            trace.Add($"Combine Item Condition: slot1={slot1}, slot2={slot2} -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionDropToSelf)
        {
            bool targetIsSelf = c.DropToSelfItemReference is not { } target
                || (target.Category == ReferencedItemCategory.Book && target.ItemId == ctx.SelfItemId);
            bool pass = targetIsSelf && ctx.DropToSelfItemPresent;
            trace.Add($"Drop To Self Condition: simulated item present = {ctx.DropToSelfItemPresent} -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionJoinedParty)
        {
            // No explicit range field exists on this condition (only an NPC
            // reference) - reusing the same simulated distance Distance
            // Condition uses, against a fixed "close" default, is the most
            // honest stand-in available without inventing a field that isn't
            // actually part of the model.
            const double assumedCloseRange = 32.0;
            bool pass = ctx.SimulatedDistance <= assumedCloseRange;
            trace.Add($"Joined Party Condition (proximity): simulated distance {ctx.SimulatedDistance:0.00} <= {assumedCloseRange:0.00} (assumed range) -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionDistance)
        {
            bool pass = ctx.SimulatedDistance <= c.DistanceValue;
            trace.Add($"Distance Condition: simulated {ctx.SimulatedDistance:0.00} <= {c.DistanceValue:0.00} -> {Verdict(pass)}");
            result &= pass;
        }

        if (c.ConditionComplex)
        {
            bool pass = EvaluateComplex(c, ctx, trace);
            trace.Add($"Complex Condition -> {Verdict(pass)}");
            result &= pass;
        }

        bool cyclePass = EvaluateCycle(c, ctx, trace);
        result &= cyclePass;

        return result;
    }

    private static bool EvaluateVariableConditions(List<VariableConditionEntry> entries, BookEventPreviewContext ctx, List<string> trace)
    {
        if (entries.Count == 0) return true;

        bool result = EvaluateOneVariableCondition(entries[0], ctx, trace);
        for (int i = 1; i < entries.Count; i++)
        {
            bool next = EvaluateOneVariableCondition(entries[i], ctx, trace);
            result = entries[i - 1].NextOperator == ConditionLogicOperator.And ? result && next : result || next;
        }
        return result;
    }

    private static bool EvaluateOneVariableCondition(VariableConditionEntry entry, BookEventPreviewContext ctx, List<string> trace)
    {
        string simulated = ctx.VariableValues.TryGetValue(entry.VariableId, out var v) ? v : "0";
        bool pass;

        if (entry.Mode == VariableConditionMode.String)
        {
            pass = CompareStrings(simulated, entry.Value, entry.Comparison);
        }
        else if (entry.Mode == VariableConditionMode.Boolean)
        {
            bool.TryParse(simulated, out bool simBool);
            bool.TryParse(entry.Value, out bool targetBool);
            pass = entry.Comparison == ConditionComparisonOperator.NotEqual ? simBool != targetBool : simBool == targetBool;
        }
        else
        {
            double.TryParse(simulated, out double simNum);
            double.TryParse(entry.Value, out double targetNum);
            pass = CompareNumbers(simNum, targetNum, entry.Comparison);
        }

        trace.Add($"  Variable \"{entry.VariableDisplayName}\" {entry.Comparison} \"{entry.Value}\" (simulated: \"{simulated}\") -> {Verdict(pass)}");
        return pass;
    }

    private static bool EvaluateStatConditions(List<StatConditionEntry> entries, BookEventPreviewContext ctx, List<string> trace)
    {
        bool result = true;
        foreach (var entry in entries)
        {
            if (!entry.IsActive) continue;
            int simulated = ctx.StatValues.TryGetValue(entry.StatId, out var v) ? v : 0;
            bool pass = CompareNumbers(simulated, entry.Value, entry.Comparison);
            trace.Add($"  Stat #{entry.StatId} {entry.Comparison} {entry.Value} (simulated: {simulated}) -> {Verdict(pass)}");
            result &= pass;
        }
        return result;
    }

    /// <summary>
    /// Multiple rule groups combine with AND - each group is itself an
    /// And/Or/Xor chain of sub-conditions, evaluated left to right using
    /// each sub-condition's own NextOperator, mirroring exactly how the
    /// top-level In A Condition list already chains its own entries.
    /// Reuses the SAME shared simulated-value fields every top-level
    /// checkmark already uses (VariableValues, StatValues, etc.) rather
    /// than inventing a second, parallel simulation system - a Complex
    /// sub-condition of type Stat Condition, say, is testing the exact
    /// same simulated stat a top-level Stat Condition checkbox would.
    /// </summary>
    private static bool EvaluateComplex(BookEventConditions c, BookEventPreviewContext ctx, List<string> trace)
    {
        if (c.ComplexConditions.Count == 0)
        {
            trace.Add("  (no rule groups defined - vacuously PASS)");
            return true;
        }

        bool overall = true;
        foreach (var group in c.ComplexConditions)
        {
            bool groupResult = EvaluateComplexGroup(group, ctx, trace);
            trace.Add($"  Group \"{group.Name}\" -> {Verdict(groupResult)}");
            overall &= groupResult;
        }
        return overall;
    }

    private static bool EvaluateComplexGroup(ComplexConditionGroup group, BookEventPreviewContext ctx, List<string> trace)
    {
        if (group.SubConditions.Count == 0) return true;

        bool result = EvaluateComplexSubCondition(group.SubConditions[0], ctx, trace);
        for (int i = 1; i < group.SubConditions.Count; i++)
        {
            bool next = EvaluateComplexSubCondition(group.SubConditions[i], ctx, trace);
            result = group.SubConditions[i - 1].NextOperator switch
            {
                ComplexLogicOperator.And => result && next,
                ComplexLogicOperator.Or => result || next,
                ComplexLogicOperator.Xor => result ^ next,
                _ => result && next
            };
        }
        return result;
    }

    private static bool EvaluateComplexSubCondition(ComplexSubCondition sub, BookEventPreviewContext ctx, List<string> trace)
    {
        bool raw = sub.Type switch
        {
            ComplexSubConditionType.InACondition => EvaluateOneVariableCondition(sub.InACondition, ctx, trace),
            ComplexSubConditionType.EventTriggerCompleted => ctx.EventCompletedActual == sub.EventCompletedExpectedState,
            ComplexSubConditionType.StatCondition => EvaluateOneStatEntry(sub.StatCondition, ctx, trace),
            ComplexSubConditionType.CombineItemCondition =>
                (sub.CombineItemSlot1.IsSelf || ctx.CombineItemSlot1Present) && (sub.CombineItemSlot2.IsSelf || ctx.CombineItemSlot2Present),
            ComplexSubConditionType.DropToSelfCondition => EvaluateDropToSelfSub(sub, ctx),
            ComplexSubConditionType.JoinedPartyCondition => ctx.SimulatedDistance <= 32.0,
            _ => EvaluateTriggerTypeSubCondition(sub, ctx)
        };

        bool final = sub.Invert ? !raw : raw;
        trace.Add($"  Sub-condition {sub.Type}{(sub.Invert ? " (inverted)" : string.Empty)} -> {Verdict(final)}");
        return final;
    }

    private static bool EvaluateOneStatEntry(StatConditionEntry entry, BookEventPreviewContext ctx, List<string> trace)
    {
        if (!entry.IsActive) return true; // an inactive row inside a Complex sub-condition contributes nothing, same as at the top level
        int simulated = ctx.StatValues.TryGetValue(entry.StatId, out var v) ? v : 0;
        return CompareNumbers(simulated, entry.Value, entry.Comparison);
    }

    private static bool EvaluateDropToSelfSub(ComplexSubCondition sub, BookEventPreviewContext ctx)
    {
        bool targetIsSelf = sub.TriggerItemReference is not { } target
            || (target.Category == ReferencedItemCategory.Book && target.ItemId == ctx.SelfItemId);
        return targetIsSelf && ctx.DropToSelfItemPresent;
    }

    /// <summary>The 9 trigger types available inside a Complex Condition - same rules as the top-level Trigger Conditions Based dropdown. All are genuinely active: Trade/Destroy/Having/Not-Having are simulated the same generic way as Use/Pickup/Drop (via the Debug window's "simulate this trigger" combo), and Trade/Destroy/Having/Not-Having additionally respect their own Select Item target when one is set.</summary>
    private static bool EvaluateTriggerTypeSubCondition(ComplexSubCondition sub, BookEventPreviewContext ctx)
    {
        bool matchesTarget = sub.TriggerItemReference is not { } target
            || (target.Category == ReferencedItemCategory.Book && target.ItemId == ctx.SelfItemId);

        return sub.Type switch
        {
            ComplexSubConditionType.Always => true,
            ComplexSubConditionType.ByAfterReadItem => ctx.HasBeenRead,
            ComplexSubConditionType.ByUseItem => ctx.MomentaryTriggerJustOccurred == BookTriggerConditionBased.ByUseItem,
            ComplexSubConditionType.ByPickupItem => matchesTarget && ctx.MomentaryTriggerJustOccurred == BookTriggerConditionBased.ByPickupItem,
            ComplexSubConditionType.ByDropItem => matchesTarget && ctx.MomentaryTriggerJustOccurred == BookTriggerConditionBased.ByDropItem,
            ComplexSubConditionType.ByTradeItem => matchesTarget && ctx.MomentaryTriggerJustOccurred == BookTriggerConditionBased.ByTradeItem,
            ComplexSubConditionType.ByDestroyItem => matchesTarget && ctx.MomentaryTriggerJustOccurred == BookTriggerConditionBased.ByDestroyItem,
            ComplexSubConditionType.ByHavingAnItem => matchesTarget && ctx.MomentaryTriggerJustOccurred == BookTriggerConditionBased.ByHavingAnItem,
            ComplexSubConditionType.ByNotHavingAnItem => matchesTarget && ctx.MomentaryTriggerJustOccurred == BookTriggerConditionBased.ByNotHavingAnItem,
            _ => false
        };
    }

    private static bool EvaluateCycle(BookEventConditions c, BookEventPreviewContext ctx, List<string> trace)
    {
        bool pass = c.CycleCondition switch
        {
            CycleConditionType.Once => ctx.TimesTriggeredSoFar == 0,
            CycleConditionType.SpecificNumberOfTimes => ctx.TimesTriggeredSoFar < c.CycleSpecificTimes,
            CycleConditionType.UntilConditionMet => ctx.UntilConditionStillMet,
            CycleConditionType.Always => true,
            _ => true
        };
        trace.Add($"Cycle ({c.CycleCondition}): simulated times triggered so far = {ctx.TimesTriggeredSoFar} -> {Verdict(pass)}");
        return pass;
    }

    private static bool CompareNumbers(double a, double b, ConditionComparisonOperator op) => op switch
    {
        ConditionComparisonOperator.Equal => a == b,
        ConditionComparisonOperator.NotEqual => a != b,
        ConditionComparisonOperator.GreaterThan => a > b,
        ConditionComparisonOperator.GreaterOrEqual => a >= b,
        ConditionComparisonOperator.LessThan => a < b,
        ConditionComparisonOperator.LessOrEqual => a <= b,
        _ => false
    };

    private static bool CompareStrings(string a, string b, ConditionComparisonOperator op)
    {
        int c = string.Compare(a, b, StringComparison.Ordinal);
        return op switch
        {
            ConditionComparisonOperator.Equal => c == 0,
            ConditionComparisonOperator.NotEqual => c != 0,
            ConditionComparisonOperator.GreaterThan => c > 0,
            ConditionComparisonOperator.GreaterOrEqual => c >= 0,
            ConditionComparisonOperator.LessThan => c < 0,
            ConditionComparisonOperator.LessOrEqual => c <= 0,
            _ => false
        };
    }

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";
}
