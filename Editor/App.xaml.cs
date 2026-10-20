using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class App : Application
{
    /// <summary>
    /// The same program is both the editor and every game built with it: if
    /// a project has been merged into this .exe (Game > Build...), it starts
    /// straight into that game; otherwise it's the editor.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            // A bug while a game runs: error 900 in the game's error paperdoll, and the game goes on (it can be saved or loaded) - unless it keeps happening.
            var game = Windows.OfType<GameTestWindow>().FirstOrDefault(w => w.IsActive) ?? Windows.OfType<GameTestWindow>().FirstOrDefault();
            if (game is not null && game.UnexpectedError(args.Exception)) { args.Handled = true; return; }
            if (game is { IsShippedGame: false })
            {
                // Game Test: the game closes, the editor (and its unsaved work) stays.
                GameDataException.ShowPlain(GameErrors.Unexpected, args.Exception.Message, null, game.Title);
                args.Handled = true;
                game.Close();
                return;
            }
            if (game is not null || Windows.OfType<MainWindow>().All(w => w.IsGameHost))
            {
                GameDataException.ShowPlain(GameErrors.Unexpected, args.Exception.Message, null, game?.Title ?? "Game");
                args.Handled = true;
                Shutdown();
            }
        };
        bool isGame;
        string msrpPath;
        string? dataFolder;
        try { isGame = GameBundle.TryExtractEmbeddedGame(Environment.ProcessPath, out msrpPath, out dataFolder); }
        catch (GameDataException ex)
        {
            GameDataException.ShowPlain(ex.Number, ex.Message, ex.MsrpPath, System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Game"));
            Shutdown();
            return;
        }
        if (isGame)
        {
            var host = new MainWindow { DataFolderOverride = dataFolder, IsGameHost = true }; // never shown - it only loads the embedded project, exactly like File > Open
            if (!host.RunStandaloneGame(msrpPath)) Shutdown();
            return;
        }
        SplashWindow.ShowWhileLoading(); // the splash screen, on its own thread while the editor loads (SplashWindow.cs)
        var editor = new MainWindow();
        editor.Closed += (_, _) => Shutdown();
        editor.ContentRendered += (_, _) => SplashWindow.CloseWhenEditorShown();
        MainWindow = editor;
        editor.Show();
    }
}
