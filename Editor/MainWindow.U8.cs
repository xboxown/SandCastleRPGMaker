using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A project's Style (File > New Project): Ultima 7 - the engine built so far, GameTestWindow, untouched - or Ultima 8,
/// whose games run in their own engine, U8GameWindow (Editor/U8: copied from the Ultima 7 engine and changed there only).
/// The editor is the same for both; what Ultima 8 adds shows only in an Ultima 8 project. These are the Ultima 8
/// engine's doors: Game Test and a built game.
/// </summary>
public partial class MainWindow
{
    private string _projectStyle = GameStyle.Ultima7;
    private bool IsUltima8 => _projectStyle == GameStyle.Ultima8;

    private U8GameWindow NewU8GameWindow(bool atTitle, bool shipped) => NewU8GameWindow(_worlds.FirstOrDefault() ?? _map!, atTitle, shipped);

    private U8GameWindow NewU8GameWindow(MapData start, bool atTitle, bool shipped) =>
        new(start, Sheet(start.Tileset.TilesetSpriteName)?.Bitmap ?? PrimarySheet()?.Bitmap ?? _tilesetBitmap!, start.Tileset.TileWidth, start.Tileset.TileHeight,
            Sheet(start.Tileset.TilesetSpriteName)?.Columns ?? PrimarySheet()?.Columns ?? _paletteColumns,
            start.Tileset.TilesetSpriteName, _resourceDatabase, _spriteDatabase, _bookDatabase, _objectDatabase,
            _characterDatabase, _poseDatabase, _tilePropertiesDatabase, _variableDatabase, _statDatabase, _containerDatabase, _systemSettings, _factionDatabase)
        {
            StartAtTitle = atTitle,
            IsShippedGame = shipped,
            Worlds = _worlds,
            Title = shipped ? (_currentProjectName is { Length: > 0 } name ? name : "Game") : "Sandcastle RPG Maker - Game Test (Ultima 8 Engine, Alpha)",
            WindowStartupLocation = shipped ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

    private U8GameWindow? NextU8GameWindow(U8GameWindow closed, bool shipped) =>
        closed.ReplacedBy ?? (closed.ReturnToTitleRequested ? NewU8GameWindow(atTitle: true, shipped) : closed.RestartRequested ? NewU8GameWindow(atTitle: false, shipped) : null);

    /// <summary>A built Ultima 8 game's window: closing it ends the program - unless another game window carries on.</summary>
    private void RunStandaloneU8(U8GameWindow game)
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
            if (NextU8GameWindow(game, shipped: true) is { } next) RunStandaloneU8(next);
            else Application.Current.Shutdown();
        };
        Application.Current.MainWindow = game;
        game.Show();
    }

    /// <summary>Game Test of an Ultima 8 project: its own engine.</summary>
    private void LaunchU8GameTest(bool atTitle)
    {
        for (U8GameWindow? game = NewU8GameWindow(atTitle || _systemSettings.Title.InGameTest, shipped: false); game is not null; game = NextU8GameWindow(game, shipped: false))
        {
            game.Owner = this;
            game.ShowDialog();
        }
    }

    /// <summary>The style shown after the project's name in the title bar.</summary>
    private string StyleTitleSuffix => IsUltima8 ? "  [Ultima 8 style]" : string.Empty;
}
