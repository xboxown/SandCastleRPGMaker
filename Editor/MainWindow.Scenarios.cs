using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Game > Scenario Developer, and starting a Game Test from a scenario: on a copy of the project with the scenario applied (ScenarioRules).</summary>
public partial class MainWindow
{
    private void ScenarioDeveloperMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_worlds.Count == 0)
        {
            MessageBox.Show("Create or load a map first.", "Scenario Developer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ScenarioDeveloperWindow(_systemSettings.Scenarios, _worlds, _characterDatabase, _variableDatabase, _statDatabase, _bookDatabase, _objectDatabase, _containerDatabase, _spriteDatabase) { Owner = this }.ShowDialog();
    }

    private GameTestWindow ScenarioGameWindow(Scenario scenario)
    {
        var prepared = ScenarioRules.Prepare(scenario, _worlds, _characterDatabase);
        var game = NewGameWindow(prepared.Start, atTitle: false, shipped: false, prepared.Characters, prepared.Worlds);
        game.PlayingScenario = scenario;
        game.Title += $" - {scenario.Name}";
        return game;
    }

    private U8GameWindow ScenarioU8GameWindow(Scenario scenario)
    {
        var prepared = ScenarioRules.Prepare(scenario, _worlds, _characterDatabase);
        var game = NewU8GameWindow(prepared.Start, atTitle: false, shipped: false, prepared.Characters, prepared.Worlds);
        game.PlayingScenario = scenario;
        game.Title += $" - {scenario.Name}";
        return game;
    }
}
