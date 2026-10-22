using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Build for the Sandcastle player's platforms (Linux, Mac, Amiga...): the
/// game's data folder the player reads ("Game_Data": game.sdbx, sprites,
/// pictures, sounds, videos - Engine/PlayerPack.cs, all encrypted).
/// Amiga 68k and MorphOS: a whole drawer named after the game - the player
/// program "Game" (compiled from the repo's Player folder and shipped with the
/// editor in Runtimes/Amiga68k or Runtimes/MorphOS), its icon "Game.info" (the splash scene,
/// 64x64) and Game_Data beside it. Linux and Mac (preview): only the data;
/// the player is built on that machine from the Player folder.
/// </summary>
public partial class MainWindow
{
    /// <summary>The compiled players shipped with the editor, by platform (none yet: built on that machine).</summary>
    private static string? ShippedPlayer(BuildPlatform platform) => platform switch
    {
        BuildPlatform.Amiga68k => Path.Combine(AppContext.BaseDirectory, "Runtimes", "Amiga68k", "Game"),
        BuildPlatform.MorphOS => Path.Combine(AppContext.BaseDirectory, "Runtimes", "MorphOS", "Game"),
        _ => null
    };

    private void BuildForPlayer(BuildPlatform platform)
    {
        string? player = ShippedPlayer(platform);
        if (player is not null && !File.Exists(player))
        {
            MessageBox.Show($"The player program for this machine is missing:\n{player}\n\nReinstall the editor (or build the player from the Player folder).", "Build", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (MessageBox.Show("Building saves the project first, so the game is exactly what you have now. Continue?", "Build", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject() || _currentProjectFolder is null) return;
        var pick = new OpenFolderDialog
        {
            Title = player is not null
                ? "Which folder should the game go into? (a drawer named after the game is made in it)"
                : "Which folder should the game's data go into? (a \"Game_Data\" folder is made in it)"
        };
        if (pick.ShowDialog(this) != true) return;
        string folder = player is not null ? Path.Combine(pick.FolderName, AmigaName(_currentProjectName)) : pick.FolderName;
        string target = Path.Combine(folder, "Game_Data");
        if (IsSameOrInside(target, Path.GetFullPath(_currentProjectFolder)))
        {
            MessageBox.Show("The game can't be built into the project's own folder. Choose a different folder.", "Build", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            PlayerPack.Export(target, CurrentPayload(), _bookDatabase, _spriteDatabase, _resourceDatabase, _videoDatabase, xaml => PlayerBookText.Convert(xaml, _spriteDatabase));
            string machine = platform switch
            {
                BuildPlatform.Linux => "Linux", BuildPlatform.MacModern => "Mac", BuildPlatform.MacPowerPC => "PowerPC Mac", BuildPlatform.MorphOS => "MorphOS",
                BuildPlatform.AmigaOS4 => "AmigaOS 4", BuildPlatform.Amiga68k => "Amiga (68k)", BuildPlatform.AmigaVampire => "Amiga (Vampire)", _ => platform.ToString()
            };
            if (player is not null)
            {
                // The program and its icon: double-clicked from the Workbench, it reads Game_Data beside it.
                File.Copy(player, Path.Combine(folder, "Game"), overwrite: true);
                File.WriteAllBytes(Path.Combine(folder, "Game.info"), AmigaIcon.DiskObject(64, 64, SplashScene.IconPixels(64)));
                StatusText.Text = $"Game built for the {machine}: {folder}";
                string needs = platform switch
                {
                    BuildPlatform.MorphOS => "Needs MorphOS 3.x and 128 MB of free memory. Sound plays through AHI.",
                    _ => "Needs an RTG card (Picasso96 or CyberGraphX), a 68060 or 68080 with its FPU (68060.library installed), and 128 MB of RAM. Sound plays through Paula; put a file named \"ahi\" beside Game for AHI instead."
                };
                string where = platform == BuildPlatform.Amiga68k ? "hard drive (or a WinUAE / AmiKit shared folder)" : "disk";
                MessageBox.Show($"The game is built in:\n{folder}\n\nCopy that whole drawer to the {machine}'s {where} and double-click \"Game\".\n{needs}",
                                "Build", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            StatusText.Text = $"Game data built: {target}";
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

    /// <summary>A drawer name the Amiga takes: no characters its file systems refuse, at most 30 long (FFS), never empty.</summary>
    private static string AmigaName(string? name)
    {
        var chars = (name ?? string.Empty).Where(c => c >= 32 && c < 127 && c != ':' && c != '/' && c != '"' && c != '*' && c != '?' && c != '<' && c != '>' && c != '|' && c != '\\').ToArray();
        string clean = new string(chars).Trim().TrimEnd('.');
        if (clean.Length > 30) clean = clean[..30].TrimEnd();
        return clean.Length == 0 ? "Game" : clean;
    }
}
