using System.Text.Json;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// What a scenario (Engine/ScenarioModel.cs) does to a game about to start in Game Test: both engines start from a copy of the
/// project with it applied, so nothing of it can reach the project or a built game.
/// </summary>
internal static class ScenarioRules
{
    public sealed record Prepared(MapData Start, List<MapData> Worlds, CharacterDatabase Characters);

    public static Prepared Prepare(Scenario scenario, List<MapData> worlds, CharacterDatabase characters)
    {
        var copiedWorlds = worlds.Select(Copy).ToList();
        var copiedCharacters = Copy(characters);
        var start = copiedWorlds.FirstOrDefault(w => w.Id == scenario.WorldId) ?? copiedWorlds[0];
        if (scenario.X >= 0 && scenario.Y >= 0 && scenario.X < start.Width && scenario.Y < start.Height)
        {
            start.StartX = scenario.X;
            start.StartY = scenario.Y;
            start.StartFloor = Math.Clamp(scenario.Floor, 1, MapFloors.MaxFloors);
        }

        int beside = 0;
        foreach (var rule in scenario.Characters)
        {
            if (copiedCharacters.Characters.FirstOrDefault(c => c.Id == rule.CharacterId) is not { } character) continue;
            foreach (var (stat, value) in rule.Stats) character.StatValues[stat] = value;
            if (rule.OnlyTheseItems)
            {
                character.Inventory.Clear();
                character.EquippedSlots.Clear();
                character.CustomEquippedSlots.Clear();
            }
            foreach (var item in rule.Items.Where(i => i.ItemId >= 0))
                character.Inventory.Add(new InventoryItemInstance { InstanceId = character.GetNextInventoryInstanceId(), Category = item.Category, ItemId = item.ItemId, Quantity = Math.Max(1, item.Quantity) });
            if (character.IsTestPlayer) continue;

            if (rule.Dead)
            {
                character.IsInParty = false;
                foreach (var placement in copiedWorlds.SelectMany(w => w.Characters).Where(p => p.CharacterId == character.Id)) { placement.Dead = true; placement.BodyInstanceId = -1; }
                continue;
            }
            if (rule.Party == ScenarioPartyRule.NotJoined) character.IsInParty = false;
            if (rule.Party != ScenarioPartyRule.Joined) continue;
            // In the party: it starts with the main character, wherever it was placed. (A character placed more than once: this is the one that goes.)
            character.IsInParty = true;
            foreach (var world in copiedWorlds) world.Characters.RemoveAll(p => p.CharacterId == character.Id);
            start.Characters.Add(new PlacedCharacter
            {
                Id = start.GetNextCharacterPlacementId(), CharacterId = character.Id,
                X = Math.Clamp(start.StartX + beside % 3 - 1, 0, start.Width - 1), Y = Math.Clamp(start.StartY + 1 + beside / 3, 0, start.Height - 1), Floor = start.StartFloor
            });
            beside++;
        }
        return new Prepared(start, copiedWorlds, copiedCharacters);
    }

    /// <summary>A variable's value as the game's scripts hold it: true / false as 1 / 0, numbers as numbers, anything else as words.</summary>
    public static object ValueOf(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" => 1.0,
        "false" => 0.0,
        _ => value
    };

    /// <summary>This key press is this key name ("F2", "Ctrl+2"), exactly: Ctrl+F2 isn't F2.</summary>
    public static bool Matches(string? keyName, Key pressed)
    {
        if (string.IsNullOrEmpty(keyName) || !U8Keys.IsKeyName(keyName)) return false;
        var (key, held) = U8KeyCapture.Parse(keyName);
        return key == pressed && Keyboard.Modifiers == held;
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
