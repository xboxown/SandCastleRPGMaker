using System.Windows;

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
        bool isGame;
        string msrpPath;
        string? dataFolder;
        try { isGame = GameBundle.TryExtractEmbeddedGame(Environment.ProcessPath, out msrpPath, out dataFolder); }
        catch (GameDataException ex)
        {
            MessageBox.Show(ex.Message, System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Game"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }
        if (isGame)
        {
            var host = new MainWindow { DataFolderOverride = dataFolder }; // never shown - it only loads the embedded project, exactly like File > Open
            if (!host.RunStandaloneGame(msrpPath)) Shutdown();
            return;
        }
        var editor = new MainWindow();
        editor.Closed += (_, _) => Shutdown();
        MainWindow = editor;
        editor.Show();
    }
}
