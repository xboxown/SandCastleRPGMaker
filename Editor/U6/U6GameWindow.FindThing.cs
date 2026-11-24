using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Things that appear during play (a body, something dropped or created) named by a handle in a variable:
/// Find Thing puts one there (a character's body, the nearest of an item to someone or a spot, the thing at a spot),
/// every Thing field can say "From a variable" (Pickup Item, Use, Delete Object, Move Object, Lock, Light...),
/// and Thing Info reads where it is and what it is. A handle is a number: an object's own number on the map, a book's
/// or container's (a body's) 1000000 + its number; -1 = none. The C player makes the same numbers.
/// </summary>
public partial class U6GameWindow
{
    private const int ItemHandleBase = 1000000;

    private void RegisterFindThingCommands(Dictionary<EventCommandType, Func<EventCommand, bool>> h)
    {
        h[EventCommandType.FindThing] = FindThingCommand;
        h[EventCommandType.ThingInfo] = ThingInfoCommand;
    }

    /// <summary>A thing's handle (a placed book or container that was never opened gets its number now - as opening it would).</summary>
    private int HandleOf(object thing)
    {
        switch (thing)
        {
            case ObjectInstance o:
                if (o.InstanceId <= 0) o.InstanceId = _map.GetNextInstanceId();
                return o.InstanceId;
            case PlacedItemInstance i:
                if (i.InstanceId == 0)
                {
                    if (i.Category == ReferencedItemCategory.Container && ContainerDef(i.ItemId) is { } data) EnsureContainerSeeded(i, data);
                    else i.InstanceId = _nextContainerRuntimeInstanceId++;
                }
                return ItemHandleBase + i.InstanceId;
            default: return -1;
        }
    }

    /// <summary>The thing a handle names, if it is on the map.</summary>
    private object? ThingByHandle(int handle)
    {
        if (handle <= 0) return null;
        if (handle < ItemHandleBase) return _map.Objects.FirstOrDefault(o => o.InstanceId == handle && o.ContainerInstanceId < 0);
        return _map.Items.FirstOrDefault(i => i.InstanceId == handle - ItemHandleBase);
    }

    /// <summary>A Thing field set to "From a variable": the thing its handle names.</summary>
    private object? ThingFromVariable(EventCommand command)
    {
        string name = command.Get("Thing")[4..];
        return ThingByHandle((int)Math.Round(ScriptValues.Number(ScriptExpression.GetVariable(_execContext, name))));
    }

    private (int X, int Y, int Z) WhereIsThing(object thing) => thing switch
    {
        ObjectInstance o => (o.X, o.Y, o.Z),
        PlacedItemInstance i => (i.X, i.Y, i.Z),
        _ => (-1, -1, 0)
    };

    private string NameOfMapThing(object thing) => thing switch
    {
        ObjectInstance o => ObjectDef(o.ObjectId)?.Name ?? "",
        PlacedItemInstance { Category: ReferencedItemCategory.Book } b => BookDef(b.ItemId)?.Name ?? "",
        PlacedItemInstance c => ContainerDef(c.ItemId)?.Name ?? "",
        _ => ""
    };

    private bool FindThingCommand(EventCommand c)
    {
        object? found = null;
        string find = c.Get("Find");
        var (category, itemId) = ItemFieldOf(c);
        bool IsTheItem(object t) => t switch
        {
            ObjectInstance o => category == ReferencedItemCategory.Object && o.ObjectId == itemId,
            PlacedItemInstance i => i.Category == category && i.ItemId == itemId,
            _ => false
        };
        IEnumerable<object> OnMap() => _map.Objects.Where(o => o.ContainerInstanceId < 0).Cast<object>().Concat(_map.Items);
        object? NearestTo(int x, int y, int floor) => OnMap().Where(IsTheItem)
            .OrderBy(t => MapFloors.FloorOfLift(WhereIsThing(t).Z) == floor ? 0 : 1)
            .ThenBy(t => CellsApart((WhereIsThing(t).X, WhereIsThing(t).Y), (x, y))).FirstOrDefault();
        int.TryParse(c.Get("AtX"), out int sx);
        int.TryParse(c.Get("AtY"), out int sy);
        int sFloor = int.TryParse(c.Get("AtFloor"), out int f) ? f : 1;
        if (find.StartsWith("The body"))
        {
            if (CharacterDefinitionFor(c.Get("Who")) is { } who && _bodies.TryGetValue(who, out var body) && _map.Items.Contains(body.Body)) found = body.Body;
        }
        else if (find.StartsWith("The nearest of an item to someone"))
        {
            if (CharacterDefinitionFor(c.Get("Who")) is { } who && category is not null)
            {
                var (x, y) = CellOf(who);
                found = NearestTo(x, y, MapFloors.FloorOfLift(LiftOf(who)));
            }
        }
        else if (find.StartsWith("The nearest of an item to a spot"))
        {
            if (category is not null) found = NearestTo(sx, sy, sFloor);
        }
        else // the thing at a spot: the one on top
            found = OnMap().Where(t => WhereIsThing(t).X == sx && WhereIsThing(t).Y == sy && MapFloors.FloorOfLift(WhereIsThing(t).Z) == sFloor)
                .Where(t => t is not ObjectInstance o || ObjectDef(o.ObjectId)?.Type is not (ObjectType.Tile or ObjectType.Roof))
                .OrderByDescending(t => WhereIsThing(t).Z).FirstOrDefault();
        SetScriptVariable(c.Get("Variable"), (double)(found is null ? -1 : HandleOf(found)));
        return found is not null;
    }

    private bool ThingInfoCommand(EventCommand c)
    {
        var thing = ThingByHandle((int)Math.Round(FormulaNumber(c, "Value")));
        var (x, y, z) = thing is null ? (-1, -1, 0) : WhereIsThing(thing);
        if (c.Get("XVariable").Length > 0) SetScriptVariable(c.Get("XVariable"), (double)x);
        if (c.Get("YVariable").Length > 0) SetScriptVariable(c.Get("YVariable"), (double)y);
        if (c.Get("FloorVariable").Length > 0) SetScriptVariable(c.Get("FloorVariable"), (double)(thing is null ? -1 : MapFloors.FloorOfLift(z)));
        if (c.Get("NameVariable").Length > 0) SetScriptVariable(c.Get("NameVariable"), thing is null ? "" : NameOfMapThing(thing));
        return thing is not null;
    }

    /// <summary>Cast Spell running a Common Event: its caster (Who) and target (Other) as Who keys; SpellCaster / SpellTarget get their numbers.</summary>
    private (string Caster, string Target) SpellRolesOf(EventCommand c)
    {
        var caster = CharacterDefinitionFor(c.Get("Who") is { Length: > 0 } w ? w : WhoChoice.Activator);
        var target = c.Get("Other").Length > 0 ? CharacterDefinitionFor(c.Get("Other")) : null;
        SetScriptVariable(SpellRoles.Caster, (double)(caster?.Id ?? -1));
        SetScriptVariable(SpellRoles.Target, (double)(target?.Id ?? -1));
        string Key(CharacterDefinition? who) => who is null ? "-1" : ReferenceEquals(who, _player) ? WhoChoice.Player : who.Id.ToString();
        return (Key(caster), Key(target));
    }
}
