using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Game > Build > Ubuntu: one .deb (Engine/LinuxDeb.cs) for Ubuntu 24.04 and newer - double-clicked, Ubuntu's App Center installs
/// it, SDL2 with it if needed, and the game appears with the other applications. The player is Runtimes/Linux/Game (built by
/// Player/build_linux.sh in an Ubuntu 24.04 system, so it runs on 24.04 and everything newer).
/// </summary>
public partial class MainWindow
{
    private static string LinuxPlayer => Path.Combine(AppContext.BaseDirectory, "Runtimes", "Linux", "Game");

    private async void BuildForLinux()
    {
        if (!File.Exists(LinuxPlayer))
        {
            MessageBox.Show($"The Linux player is missing from:\n{Path.GetDirectoryName(LinuxPlayer)}\n\nReinstall the editor.", "Build for Ubuntu", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (MessageBox.Show("Building saves the project first, so the game is exactly what you have now. Continue?", "Build for Ubuntu", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject() || _currentProjectFolder is null) return;
        string title = string.IsNullOrWhiteSpace(_currentProjectName) ? "Sandcastle Game" : _currentProjectName!;
        string package = LinuxDeb.PackageName(title);
        var pick = new SaveFileDialog { Title = "Save the Ubuntu installer", Filter = "Ubuntu package (*.deb)|*.deb", FileName = $"{package}_1.0_amd64.deb", DefaultExt = ".deb" };
        if (pick.ShowDialog(this) != true) return;
        string deb = pick.FileName;
        if (IsSameOrInside(deb, Path.GetFullPath(_currentProjectFolder)))
        {
            MessageBox.Show("The installer can't be saved in the project's own folder. Choose a different folder.", "Build for Ubuntu", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string work = Path.Combine(Path.GetTempPath(), "SandcastleLinux_" + Guid.NewGuid().ToString("N"));
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            StatusText.Text = "Ubuntu: packing the game's data...";
            string data = ExportLinuxData(work);
            StatusText.Text = "Ubuntu: writing the installer...";
            byte[] icon = LinuxIcon(title);
            await Task.Run(() => LinuxDeb.Write(deb, title, LinuxPlayer, data, icon));
            long size = new FileInfo(deb).Length;
            StatusText.Text = $"Ubuntu installer built: {deb} ({Gb(size)})";
            MessageBox.Show(
                $"The Ubuntu installer is built:\n{deb}\n\nSize: {Gb(size)}.  For Ubuntu 24.04 and newer (64-bit).\n\n" +
                "To install it on Ubuntu:\n" +
                " 1. Copy the .deb to the Ubuntu computer.\n" +
                " 2. Double-click it - the App Center opens - and click Install (it asks for your password).\n" +
                "    Anything else the game needs is installed with it.\n" +
                $" 3. \"{title}\" is then with your other applications: open Show Apps and click it.\n\n" +
                "To remove it later: App Center > Manage, or right-click it in Show Apps. Its saves are kept in your home folder\n" +
                $"(~/.local/share/Sandcastle RPG Maker/{package}).",
                "Build for Ubuntu", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ubuntu: the installer could not be built.";
            MessageBox.Show($"The installer could not be built: {ex.Message}", "Build for Ubuntu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { /* a temp folder */ }
        }
    }

    /// <summary>The game's data for the Linux player, into work/Game_Data (its fonts at their own sizes - the player draws hi-res).</summary>
    private string ExportLinuxData(string work)
    {
        string data = Path.Combine(work, "Game_Data");
        Directory.CreateDirectory(data);
        var payload = CurrentPayload();
        PlayerBookText.UsedFonts.Clear();
        PlayerPack.Export(data, payload, _bookDatabase, _spriteDatabase, _resourceDatabase, _videoDatabase, xaml => PlayerBookText.Convert(xaml, _spriteDatabase));
        PlayerFontPack.Write(data, payload.Manifest.ProjectId, PlayerBookText.UsedFonts, _spriteDatabase, hires: true);
        return data;
    }

    /// <summary>The game's icon in Ubuntu's applications: 256 x 256, its title on a dark tile.</summary>
    private static byte[] LinuxIcon(string title)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(30, 34, 60), Color.FromRgb(90, 60, 25), 90), null, new Rect(8, 8, 240, 240), 36, 36);
            var text = new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Georgia"), 34, Brushes.White, 1.0)
            { MaxTextWidth = 210, TextAlignment = TextAlignment.Center, MaxTextHeight = 200, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(text, new Point(23, (256 - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        png.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Testing only: SANDCASTLE_BUILD_LINUX_DEB=file.deb with a project on the command line - the .deb Build > Ubuntu makes,
    /// without a question, and the editor closes. The project isn't saved or changed.</summary>
    internal async void BuildLinuxDebForTesting(string deb)
    {
        string work = Path.Combine(Path.GetTempPath(), "SandcastleLinux_" + Guid.NewGuid().ToString("N"));
        try
        {
            string title = string.IsNullOrWhiteSpace(_currentProjectName) ? "Sandcastle Game" : _currentProjectName!;
            string data = ExportLinuxData(work);
            byte[] icon = LinuxIcon(title);
            await Task.Run(() => LinuxDeb.Write(deb, title, LinuxPlayer, data, icon));
            File.WriteAllText(deb + ".ok", $"{title}\n{LinuxDeb.PackageName(title)}\n");
        }
        catch (Exception ex) { File.WriteAllText(deb + ".failed", ex.ToString()); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { } }
        Application.Current.Shutdown();
    }
}
