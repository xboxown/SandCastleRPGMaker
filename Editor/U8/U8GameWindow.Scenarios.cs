// Ultima 8 engine (U8GameWindow) - the same as the Ultima 7 engine's GameTestWindow.Scenarios.cs.
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Scenario Developer in play (Game > Scenario Developer), Game Test only: a game started from a scenario sets its variables before
/// anything runs, and each scenario's own key, or the Next Scenario key, ends this game and starts that scenario fresh.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>The scenario this game started from.</summary>
    public Scenario? PlayingScenario { get; set; }
    /// <summary>Set when a scenario's key was pressed: the window closes and Game Test starts that one.</summary>
    public int? ScenarioRequestedId { get; private set; }
    /// <summary>The scenario played last, for the Next Scenario key: a window opened by Change Map or a load carries on from it.</summary>
    private static int _lastScenarioId = -1;

    private void ApplyScenarioVariables()
    {
        if (IsShippedGame || PlayingScenario is not { } scenario) return;
        _lastScenarioId = scenario.Id;
        Activate(); // the window before it has just closed: the keys must come here, not to the Map Editor behind
        GameCanvas.Focus();
        foreach (var variable in scenario.Variables) SetScriptVariable(variable.Name, ScenarioRules.ValueOf(variable.Value));
        // What each carries, made as Give Item makes it: a container with its starting contents, what doesn't stack one by one.
        foreach (var rule in scenario.Characters)
        {
            if (_characters?.Characters.FirstOrDefault(c => c.Id == rule.CharacterId) is not { } character) continue;
            if (rule.Dead) continue;
            // Bags first, whatever order they were listed in, so what follows can go inside them.
            foreach (var item in rule.Items.Where(i => i.ItemId >= 0).OrderBy(i => i.Category == ReferencedItemCategory.Container ? 0 : 1))
                foreach (var gift in MakeGifts(item.Category, item.ItemId, Math.Clamp(item.Quantity, 1, ObjectTypes.MaxQuantity)))
                {
                    _settingUpScenario = true;
                    bool carried = HandOver(gift, character);
                    _settingUpScenario = false;
                    if (!carried) // no bag or hand has room, or too heavy: it can't be carried, so it's at their feet, in sight
                    {
                        PutDownAt(gift, character);
                        SoakLog($"scenario: {character.Name} can't carry {item.DisplayName}: at their feet");
                    }
                }
            SoakLog($"scenario: {character.Name} carries " + string.Join(", ", character.Inventory.Select(i => $"{i.Quantity} of {i.Category} {i.ItemId} holding {i.Contents.Count}")) + "; in slots " + string.Join(", ", character.EquippedSlots.Select(s => $"{s.Key}")));
        }
    }

    /// <summary>Handing out a scenario's items as the game starts: no pick-up sound for each.</summary>
    private bool _settingUpScenario;

    private Scenario? _scenarioKeyScenario;
    private Key _scenarioKeyHeld;

    /// <summary>A scenario's key, or the Next Scenario key, going down: noted, and nothing else in the game gets it.</summary>
    private void ScenarioKeyDown(KeyEventArgs e)
    {
        if (IsShippedGame) return;
        var settings = _systemSettings.Scenarios;
        if (settings.Scenarios.Count == 0) return;
        var pressed = U8KeyCapture.RealKey(e);
        if (_scenarioKeyScenario is not null && pressed == _scenarioKeyHeld) { e.Handled = true; return; } // held: the keyboard's own repeat
        var scenario = settings.Scenarios.FirstOrDefault(s => s.On && ScenarioRules.Matches(s.Key, pressed));
        if (scenario is null && ScenarioRules.Matches(settings.NextKey, pressed)) scenario = settings.After(_lastScenarioId);
        if (scenario is null) return;
        (_scenarioKeyScenario, _scenarioKeyHeld) = (scenario, pressed);
        e.Handled = true;
    }

    /// <summary>
    /// That key coming back up: this game ends and the scenario starts. On the way up, not down: a window closed with a key still
    /// down never hears it come up, and the next press of the same key would count as the keyboard's repeat and do nothing.
    /// </summary>
    private void ScenarioKeyUp(KeyEventArgs e)
    {
        if (_scenarioKeyScenario is not { } scenario || U8KeyCapture.RealKey(e) != _scenarioKeyHeld) return;
        _scenarioKeyScenario = null;
        e.Handled = true;
        ScenarioRequestedId = scenario.Id;
        Close();
    }
}
