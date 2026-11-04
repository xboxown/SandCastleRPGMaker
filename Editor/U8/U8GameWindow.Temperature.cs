// Ultima 8 engine (U8GameWindow) - the same as the Ultima 7 engine's GameTestWindow.Temperature.cs.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Cold and Hot zones (Map Editor > Zones; Serpent Isle's freezing, Exult's Actor::check_temperature). Once a game minute, for everyone:
/// in a Cold zone, with worn warmth under 100 (figure_warmth: -75 plus each worn thing's Warmth), it gets colder by 1 + (100 - warmth) / 20,
/// up to 63; with 100 or more, or out of the zone, it warms up. A third of the time it says how it feels, and from 50 it's hurt: 1, then
/// from 60 1 to 3. At 50 or more wounds don't mend. Heat is the same with each worn thing's Cooling: not in Exult, asked for to match.
/// A zone can lie inside another: a Cold zone at a cave's mouth is both.
/// </summary>
public partial class U8GameWindow
{
    private const int MaxExposure = 63, BaseProtection = -75, EnoughProtection = 100, HurtsFrom = 50;

    /// <summary>Exult's figure_warmth: what's worn on the head, back, feet, body, hands and legs, and any custom slot.</summary>
    private int WornProtection(CharacterDefinition character, Func<ArmorSettings, int> of)
    {
        int total = BaseProtection;
        foreach (var (slot, item) in ItemsInSlots(character))
        {
            if (slot is EquipmentSlot built && built is not (EquipmentSlot.Head or EquipmentSlot.Back or EquipmentSlot.Feet or EquipmentSlot.Body or EquipmentSlot.Gloves or EquipmentSlot.Legs)) continue;
            if (ObjectDef(item) is { Type: ObjectType.Armor } worn) total += of(worn.Armor);
        }
        return total;
    }

    private void TemperatureMinute(CharacterDefinition character)
    {
        var vitals = VitalsOf(character);
        var (x, y) = CellOf(character);
        int floor = MapFloors.FloorOfLift(LiftOf(character));
        vitals.Cold = TemperatureStep(character, vitals.Cold, !character.Survival.ColdImmune && Zones.IsCold(_map.Zones, x, y, floor), WornProtection(character, a => a.Warmth),
            VerboseKind.FeelingCold, VerboseKind.VeryCold, VerboseKind.Frostbite, VerboseKind.Frozen, VerboseKind.WarmingUp, "the cold");
        if (IsDead(character)) return;
        vitals.Heat = TemperatureStep(character, vitals.Heat, !character.Survival.HeatImmune && Zones.IsHot(_map.Zones, x, y, floor), WornProtection(character, a => a.Cooling),
            VerboseKind.FeelingHot, VerboseKind.VeryHot, VerboseKind.Heatstroke, VerboseKind.Scorched, VerboseKind.CoolingDown, "the heat");
    }

    /// <summary>check_temperature: one minute of it. The new temperature.</summary>
    private int TemperatureStep(CharacterDefinition character, int temperature, bool inside, int protection, VerboseKind low, VerboseKind mid, VerboseKind high, VerboseKind worst, VerboseKind easing, string cause)
    {
        bool says = Rng.Next(3) == 0;
        if (!inside)
        {
            if (temperature == 0) return 0;
            temperature -= Math.Min(5, temperature);
            if (says) Bark(character, easing);
            return temperature;
        }
        if (protection >= EnoughProtection)
        {
            if (temperature == 0) return 0;
            temperature -= Math.Min(1 + (protection - EnoughProtection) / 10, temperature);
            if (says) Bark(character, easing);
            return temperature;
        }
        temperature = Math.Min(MaxExposure, temperature + 1 + (EnoughProtection - protection) / 20);
        SoakLog($"temperature: {character.Name} {cause} {temperature}, protection {protection}");
        if (!says) return temperature;
        switch (temperature / 10)
        {
            case 0: case 1: Bark(character, low); break;
            case 2: Bark(character, mid); break;
            case 3: case 4: Bark(character, high); break;
            case 5: Bark(character, high); Damage(character, 1, cause); break;
            default: Bark(character, worst); Damage(character, 1 + Rng.Next(3), cause); break;
        }
        return temperature;
    }

    /// <summary>mend_wounds: at 50 or more, wounds don't mend.</summary>
    private bool TooColdOrHotToMend(CharacterDefinition character)
    {
        var vitals = VitalsOf(character);
        return (vitals.Cold >= HurtsFrom && !character.Survival.ColdImmune) || (vitals.Heat >= HurtsFrom && !character.Survival.HeatImmune);
    }
}
