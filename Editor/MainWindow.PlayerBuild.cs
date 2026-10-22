using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Build for the Sandcastle player's platforms (Linux, Mac...): the game's
/// data folder the player reads ("Game_Data": game.json, sprites.json,
/// pictures - Engine/PlayerPack.cs). Preview: the player program itself is
/// built from the repo's Player folder (make on Linux or Mac).
/// </summary>
public partial class MainWindow
{
    private void BuildForPlayer(BuildPlatform platform)
    {
        if (MessageBox.Show("Building saves the project first, so the game is exactly what you have now. Continue?", "Build", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject() || _currentProjectFolder is null) return;
        var pick = new OpenFolderDialog { Title = "Which folder should the game's data go into? (a \"Game_Data\" folder is made in it)" };
        if (pick.ShowDialog(this) != true) return;
        string target = System.IO.Path.Combine(pick.FolderName, "Game_Data");
        if (IsSameOrInside(target, System.IO.Path.GetFullPath(_currentProjectFolder)))
        {
            MessageBox.Show("The game can't be built into the project's own folder. Choose a different folder.", "Build", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            if (System.IO.Directory.Exists(target)) System.IO.Directory.Delete(target, recursive: true);
            PlayerPack.Export(target, CurrentPayload(), _bookDatabase, _spriteDatabase, _resourceDatabase, _videoDatabase, xaml => PlayerBookText.Convert(xaml, _spriteDatabase));
            StatusText.Text = $"Game data built: {target}";
            string machine = platform switch
            {
                BuildPlatform.Linux => "Linux", BuildPlatform.MacModern => "Mac", BuildPlatform.MacPowerPC => "PowerPC Mac", BuildPlatform.MorphOS => "MorphOS",
                BuildPlatform.AmigaOS4 => "AmigaOS 4", BuildPlatform.Amiga68k => "Amiga (68k)", BuildPlatform.AmigaVampire => "Amiga (Vampire)", _ => platform.ToString()
            };
            MessageBox.Show($"The game's data is in:\n{target}\n\nOn the {machine}, put Game_Data beside the Sandcastle player for that machine (built from the Player folder) and run the player.", "Build", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The game could not be built: {ex.Message}", "Build", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
