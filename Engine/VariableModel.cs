namespace SandcastleRpgMaker.Engine;

public enum VariableValueType { Integer, Float, Decimal, String, Boolean }

public class VariableDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Variable";
    public VariableValueType Type { get; set; } = VariableValueType.Integer;
}

/// <summary>
/// Project-wide variable list, shared the same way Sprites/Factions/Resources
/// are - any editor's condition system can reference a variable by id. There
/// is no dedicated Variable Editor window yet; variables are created inline
/// from the picker that condition panels use, which is enough to be usable
/// end-to-end even before a full management UI exists.
/// </summary>
public class VariableDatabase
{
    public List<VariableDefinition> Variables { get; set; } = new();
    private int _nextId = 1;

    public int GetNextId() => _nextId++;

    public void SyncNextIdAfterLoad()
    {
        _nextId = Variables.Count == 0 ? 1 : Variables.Max(v => v.Id) + 1;
    }
}
