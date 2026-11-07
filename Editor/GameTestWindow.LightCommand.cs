// GameTestWindow: the Light Source command - light, put out, burn out, make fresh, minutes left (a flashlight and its batteries).
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class GameTestWindow
{
    /// <summary>The Light Source object the command means: this object (whose event runs - also from a part of its paperdoll), or a thing on the map.</summary>
    private object? LightOf(EventCommand command)
    {
        if (command.Get("Which") == "A thing on the map (below)") return ThingOf(command);
        if (_firingOwner is ObjectInstance or InventoryItemInstance) return _firingOwner;
        return (_dollContext ?? _lastDollContext)?.Owner;
    }

    private bool LightSourceCommand(EventCommand command)
    {
        var owner = LightOf(command);
        var obj = owner switch { ObjectInstance o => DefinitionOf(o), InventoryItemInstance i => DefinitionOf(i), _ => null };
        if (obj is not { Type: ObjectType.LightSource }) { StatusText.Text = "Light Source: that isn't a Light Source object (Object Editor > Type)."; return false; }
        bool lit, spent; double left;
        switch (owner)
        {
            case ObjectInstance o: lit = o.IsLitRuntime == true; spent = o.IsSpentRuntime; left = o.BurnLeftRuntime; break;
            case InventoryItemInstance i: lit = i.IsLitRuntime == true; spent = i.IsSpentRuntime; left = i.BurnLeftRuntime; break;
            default: return false;
        }
        string doing = command.Get("Do");
        if (doing == "Light or put out (switch)") doing = lit ? "Put it out" : "Light it";
        switch (doing)
        {
            case "Light it":
                if (spent || lit) return true; // burnt out: it can't be (Make it fresh first); lit: already
                SetLight(owner, true, false, obj.Light.BurnMinutes > 0 && left < 0 ? obj.Light.BurnMinutes : left);
                break;
            case "Put it out":
                if (!lit) return true;
                if (owner is ObjectInstance placed) { PutOutLight(placed, obj); return true; }
                SetLight(owner, false, false, left);
                break;
            case "Burn it out (spent: can't be lit)": SetLight(owner, false, true, left); break;
            case "Make it fresh (can be lit, full time)": SetLight(owner, lit, false, obj.Light.BurnMinutes > 0 ? obj.Light.BurnMinutes : -1); break;
            case "Set its minutes left": SetLight(owner, lit, spent, Math.Max(0, FormulaNumber(command, "Minutes"))); break;
            case "Read its minutes left": SetScriptVariable(command.Get("Variable"), (double)(left < 0 ? obj.Light.BurnMinutes : left)); break;
            case "Read: lit (1) or not (0)": SetScriptVariable(command.Get("Variable"), lit ? 1.0 : 0.0); break;
            case "Read: burnt out (1) or not (0)": SetScriptVariable(command.Get("Variable"), spent ? 1.0 : 0.0); break;
        }
        return true;

        void SetLight(object? who, bool on, bool burnt, double minutes)
        {
            bool wasLit = lit;
            switch (who)
            {
                case ObjectInstance o: o.IsLitRuntime = on; o.IsSpentRuntime = burnt; o.BurnLeftRuntime = minutes; break;
                case InventoryItemInstance i: i.IsLitRuntime = on; i.IsSpentRuntime = burnt; i.BurnLeftRuntime = minutes; break;
            }
            if (on && !wasLit) PlaySound(obj.Light.LitSoundResourceId);
            else if (!on && wasLit) PlaySound(burnt ? obj.Light.SpentSoundResourceId : obj.Light.OffSoundResourceId);
            if (who is ObjectInstance placedLight) RefreshObjectLook(placedLight);
            RefreshAfterMove(); // the light it gives, the pack's pictures
        }
    }

    /// <summary>An object's event run with its own paperdoll at hand (Paperdoll... commands work in it) - By Burnt Out of a flashlight empties its battery slots.</summary>
    private bool FireWithOwnDoll(IHasDollState owner, WorldObject obj, Func<bool> fire) =>
        DollLook.DesignOf(obj, _objects) is { } design
            ? WithDoll(new DollContext { State = DollStateOf(owner, design), Design = design, Owner = owner, Obj = obj }, fire)
            : fire();
}
