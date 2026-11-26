using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// An Ultima 6 project (File > New Project > Style: Ultima 6 - also Savage Empire and Martian Dreams): its games run in
/// their own engine, U6GameWindow (Editor/U6: copied from the Ultima 7 engine at build 523 and changed there only).
/// Ultima 7 and Ultima 8 are untouched by it. These are the Ultima 6 engine's doors: Game Test and a built game.
/// </summary>
public partial class MainWindow
{
    private bool IsUltima6 => _projectStyle == GameStyle.Ultima6;

    /// <summary>The toolbar's PaperDoll Editor button (MainWindow.Toolbar.cs) - the HUD Editor's in an Ultima 6 project.</summary>
    private System.Windows.Controls.Button? _paperDollToolButton;

    /// <summary>Ultima 6 projects: the HUD Editor takes the PaperDoll Editor's place, in the Tools menu and on the toolbar.</summary>
    private System.Windows.Controls.MenuItem? _levelingMenuItem;
    /// <summary>Ultima 6 projects: the Spell Editor - the magic as spell systems (SpellEditorWindow.cs).</summary>
    private System.Windows.Controls.MenuItem? _spellMenuItem;

    private void ApplyStyleToTools()
    {
        PaperDollMenuItem.Header = IsUltima6 ? "HUD Editor..." : "PaperDoll Editor...";
        // Ultima 6: the Leveling Editor - the project's own leveling, made of events (LevelingEditorWindow.cs).
        if (_levelingMenuItem is null)
        {
            _levelingMenuItem = new System.Windows.Controls.MenuItem { Header = "Leveling Editor..." };
            _levelingMenuItem.Click += (_, _) => new LevelingEditorWindow(_systemSettings.Ultima6, _spriteDatabase) { Owner = this }.ShowDialog();
            ToolsMenu.Items.Insert(0, _levelingMenuItem);
        }
        _levelingMenuItem.Visibility = IsUltima6 ? Visibility.Visible : Visibility.Collapsed;
        if (_spellMenuItem is null)
        {
            _spellMenuItem = new System.Windows.Controls.MenuItem { Header = "Spell Editor..." };
            _spellMenuItem.Click += (_, _) => new SpellEditorWindow(_systemSettings.Ultima6.SpellSystems, _objectDatabase, _systemSettings.CommonEvents) { Owner = this }.ShowDialog();
            ToolsMenu.Items.Insert(0, _spellMenuItem);
        }
        _spellMenuItem.Visibility = IsUltima6 ? Visibility.Visible : Visibility.Collapsed;
        // Ultima 6: no upper floors - one floor; going up or down is another map (Maps tab, Change Map).
        FloorCombo.Visibility = IsUltima6 ? Visibility.Collapsed : Visibility.Visible;
        if (MainToolBar.Items.IndexOf(FloorCombo) is int fi && fi > 0 && MainToolBar.Items[fi - 1] is UIElement floorLabel) floorLabel.Visibility = FloorCombo.Visibility;
        if (IsUltima6 && _currentFloor != 1) { _settingFloorCombo = true; FloorCombo.SelectedIndex = 0; _settingFloorCombo = false; _currentFloor = 1; if (_map is not null && _tileset is not null) BuildMapCanvas(); }
        ApplyMachineToTools(); // Ultima 6 Apple IIgs mode: Convert Audio for the Sound Mixer, no Video Library (MainWindow.AppleIIgs.cs)
        SortToolsMenu(); // its new name in its A-Z place
        if (_paperDollToolButton is null) return;
        var tip = new System.Windows.Controls.StackPanel { MaxWidth = 280 };
        tip.Children.Add(new System.Windows.Controls.TextBlock { Text = IsUltima6 ? "HUD Editor" : "PaperDoll Editor", FontWeight = FontWeights.Bold });
        tip.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = IsUltima6 ? "The game's screen, as Ultima 6: its frame, where the world shows, the text area, the inventory and portrait panel, the command buttons."
                : "Paperdolls and every window of the game: inventories, the save screen, the title, prompts.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0)
        });
        _paperDollToolButton.ToolTip = tip;
    }

    private void OpenHudEditor() =>
        new HudEditorWindow(_systemSettings.Ultima6.Hud, _spriteDatabase, _statDatabase, _resourceDatabase, _bookDatabase, _objectDatabase, _containerDatabase, _variableDatabase) { Owner = this }.ShowDialog();

    private U6GameWindow NewU6GameWindow(bool atTitle, bool shipped) => NewU6GameWindow(_worlds.FirstOrDefault() ?? _map!, atTitle, shipped);

    private U6GameWindow NewU6GameWindow(MapData start, bool atTitle, bool shipped, CharacterDatabase? characters = null, List<MapData>? worlds = null) =>
        new(start, Sheet(start.Tileset.TilesetSpriteName)?.Bitmap ?? PrimarySheet()?.Bitmap ?? _tilesetBitmap!, start.Tileset.TileWidth, start.Tileset.TileHeight,
            Sheet(start.Tileset.TilesetSpriteName)?.Columns ?? PrimarySheet()?.Columns ?? _paletteColumns,
            start.Tileset.TilesetSpriteName, _resourceDatabase, _spriteDatabase, _bookDatabase, _objectDatabase,
            characters ?? _characterDatabase, _poseDatabase, _tilePropertiesDatabase, _variableDatabase, _statDatabase, _containerDatabase, _systemSettings, _factionDatabase)
        {
            StartAtTitle = atTitle,
            IsShippedGame = shipped,
            Worlds = worlds ?? _worlds,
            Title = shipped ? (_currentProjectName is { Length: > 0 } name ? name : "Game") : "Sandcastle RPG Maker - Game Test (Ultima 6 Engine, Alpha)",
            WindowStartupLocation = shipped ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

    private U6GameWindow? NextU6GameWindow(U6GameWindow closed, bool shipped) =>
        closed.ScenarioRequestedId is int scenarioId && _systemSettings.Scenarios.Find(scenarioId) is { } scenario ? ScenarioU6GameWindow(scenario) :
        closed.ReplacedBy ?? (closed.ReturnToTitleRequested ? NewU6GameWindow(atTitle: true, shipped) : closed.RestartRequested ? NewU6GameWindow(atTitle: false, shipped) : null);

    /// <summary>A built Ultima 6 game's window: closing it ends the program - unless another game window carries on.</summary>
    private void RunStandaloneU6(U6GameWindow game)
    {
        var display = _systemSettings.Display;
        if (display.Mode == GameScreenMode.FullScreen)
        {
            game.WindowStyle = WindowStyle.None;
            game.ResizeMode = ResizeMode.NoResize;
            game.WindowState = WindowState.Maximized;
        }
        else
        {
            int scale = Math.Clamp(display.WindowScale, 2, 6);
            game.SizeToContent = SizeToContent.Manual;
            game.Width = 320 * scale + 16;
            game.Height = 200 * scale + 39;
        }
        game.Closed += (_, _) =>
        {
            if (NextU6GameWindow(game, shipped: true) is { } next) RunStandaloneU6(next);
            else Application.Current.Shutdown();
        };
        Application.Current.MainWindow = game;
        game.Show();
    }

    /// <summary>Game Test of an Ultima 6 project: its own engine.</summary>
    private void LaunchU6GameTest(bool atTitle)
    {
        var firstScenario = atTitle ? null : _systemSettings.Scenarios.StartScenario; // the Scenario Developer's choice for F5 (MainWindow.Scenarios.cs)
        for (U6GameWindow? game = firstScenario is not null ? ScenarioU6GameWindow(firstScenario) : NewU6GameWindow(atTitle || _systemSettings.Title.InGameTest, shipped: false); game is not null; game = NextU6GameWindow(game, shipped: false))
        {
            game.Owner = this;
            game.ShowDialog();
        }
    }
}
