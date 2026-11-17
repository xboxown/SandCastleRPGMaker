using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Game > Build > PlayStation 3: one installer (.pkg) - never a disc image (that is the PS2's). It holds PARAM.SFO, ICON0.PNG and
/// USRDIR: EBOOT.BIN (the PS3 player, Runtimes/PS3/EBOOT.elf built from the Player folder by build_ps3.sh, signed here for this
/// game's own content ID) and Game_Data. On a PS3 running HEN or a custom firmware: Package Manager > Install Package Files; put on
/// the hard drive's packages folder, the PKG deletes itself once installed. Engine/Ps3Package.cs writes it.
/// </summary>
public partial class MainWindow
{
    private async void BuildForPs3()
    {
        string runtime = Path.Combine(AppContext.BaseDirectory, "Runtimes", "PS3");
        string elfPath = Path.Combine(runtime, "EBOOT.elf");
        if (!File.Exists(elfPath))
        {
            MessageBox.Show($"The PlayStation 3 player is missing from:\n{runtime}\n\nReinstall the editor.", "Build for PlayStation 3", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (MessageBox.Show("Building saves the project first, so the game is exactly what you have now. Continue?", "Build for PlayStation 3", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject() || _currentProjectFolder is null) return;
        var payload = CurrentPayload();
        string contentId = Ps3Package.ContentId(payload.Manifest.ProjectId), titleId = Ps3Package.TitleId(payload.Manifest.ProjectId);
        var pick = new SaveFileDialog { Title = "Save the PlayStation 3 installer", Filter = "PS3 package (*.pkg)|*.pkg", FileName = contentId + ".pkg", DefaultExt = ".pkg" };
        if (pick.ShowDialog(this) != true) return;
        string pkg = pick.FileName;
        if (IsSameOrInside(pkg, Path.GetFullPath(_currentProjectFolder)))
        {
            MessageBox.Show("The installer can't be saved in the project's own folder. Choose a different folder.", "Build for PlayStation 3", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string work = Path.Combine(Path.GetTempPath(), "SandcastlePs3_" + Guid.NewGuid().ToString("N"));
        string title = string.IsNullOrWhiteSpace(_currentProjectName) ? "Sandcastle Game" : _currentProjectName!;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            StatusText.Text = "PlayStation 3: packing the game's data...";
            var entries = await Ps3Entries(work, elfPath, payload, title, contentId, titleId);

            long bytes = entries.Where(e => !e.IsFolder).Sum(e => e.Length) + 0x10000;
            string? drive = Path.GetPathRoot(Path.GetFullPath(pkg));
            if (drive is not null && new DriveInfo(drive) is { IsReady: true } d && d.AvailableFreeSpace < bytes)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"The installer needs {Gb(bytes)}, and {drive} has only {Gb(d.AvailableFreeSpace)} free. Free some room or choose another drive.", "Build for PlayStation 3", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int lastPercent = -1;
            var progress = new Progress<double>(p =>
            {
                int percent = (int)(p * 100);
                if (percent != lastPercent) { lastPercent = percent; StatusText.Text = $"PlayStation 3: writing the installer... {percent}%"; }
            });
            IProgress<double> report = progress;
            await Task.Run(() => Ps3Package.WritePkg(pkg, contentId, entries, p => report.Report(p)));

            long size = new FileInfo(pkg).Length;
            StatusText.Text = $"PlayStation 3 installer built: {pkg} ({Gb(size)})";
            MessageBox.Show(
                $"The PlayStation 3 installer is built:\n{pkg}\n\nSize: {Gb(size)}.  Title ID: {titleId}.\n\n"
                + "To install it, on a PS3 running HEN or a custom firmware:\n"
                + " 1. Copy the .pkg to the root of a USB stick (FAT32), or to /dev_hdd0/packages on the PS3's hard drive.\n"
                + " 2. On the PS3: Game > Package Manager > Install Package Files, and pick it.\n"
                + " Installed from the hard drive's packages folder, the .pkg deletes itself once the game is installed.\n\n"
                + $"The game then appears under Game as \"{title}\". "
                + (IsUltima6 ? "This is an Ultima 6 game: play it with a USB keyboard and mouse plugged into the PS3." : "Play it with the PS3 controller (a USB keyboard and mouse work too).")
                + " Its saves are kept beside it on the PS3's hard drive.",
                "Build for PlayStation 3", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "PlayStation 3: the installer could not be built.";
            MessageBox.Show($"The installer could not be built: {ex.Message}", "Build for PlayStation 3", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { /* a temp folder: Windows clears it in time */ }
        }
    }

    /// <summary>What goes in the PKG: ICON0.PNG, PARAM.SFO, and USRDIR with EBOOT.BIN (the player signed for this game) and Game_Data
    /// (written into work/Game_Data). Build > PlayStation 3 and the test hook both make the package from this.</summary>
    private async Task<List<Ps3Package.Entry>> Ps3Entries(string work, string elfPath, ManifestPayload payload, string title, string contentId, string titleId)
    {
        string data = Path.Combine(work, "Game_Data");
        Directory.CreateDirectory(data);
        PlayerBookText.UsedFonts.Clear();
        PlayerPack.Export(data, payload, _bookDatabase, _spriteDatabase, _resourceDatabase, _videoDatabase, xaml => PlayerBookText.Convert(xaml, _spriteDatabase));
        PlayerFontPack.Write(data, payload.Manifest.ProjectId, PlayerBookText.UsedFonts, _spriteDatabase, hires: true); // the books' own sizes, as on Windows
        byte[] elf = File.ReadAllBytes(elfPath);
        byte[] eboot = await Task.Run(() => Ps3Package.SignEboot(elf, contentId));
        var entries = new List<Ps3Package.Entry>
        {
            new("ICON0.PNG", null, Ps3Icon(title)),
            new("PARAM.SFO", null, Ps3Package.ParamSfo(title, titleId)),
            new("USRDIR", null),
            new("USRDIR/EBOOT.BIN", null, eboot),
            new("USRDIR/Game_Data", null),
        };
        Ps3Package.AddFolder(entries, data, "USRDIR/Game_Data/");
        return entries;
    }

    /// <summary>
    /// Testing only (the PS3 player's tests): SANDCASTLE_BUILD_PS3_PKG=file.pkg with a project on the command line - the package
    /// Build > PlayStation 3 makes, written there without a question asked, and the editor closes. The project isn't saved or changed.
    /// </summary>
    internal async void BuildPs3PackageForTesting(string pkg)
    {
        string work = Path.Combine(Path.GetTempPath(), "SandcastlePs3_" + Guid.NewGuid().ToString("N"));
        try
        {
            // testing: SANDCASTLE_BUILD_PS3_CRT=1 - this package with the CRT effect on (in memory only: the project isn't saved)
            if (Environment.GetEnvironmentVariable("SANDCASTLE_BUILD_PS3_CRT") == "1") _systemSettings.Display.CrtEffect = true;
            var payload = CurrentPayload();
            string contentId = Ps3Package.ContentId(payload.Manifest.ProjectId), titleId = Ps3Package.TitleId(payload.Manifest.ProjectId);
            string title = string.IsNullOrWhiteSpace(_currentProjectName) ? "Sandcastle Game" : _currentProjectName!;
            var entries = await Ps3Entries(work, Path.Combine(AppContext.BaseDirectory, "Runtimes", "PS3", "EBOOT.elf"), payload, title, contentId, titleId);
            await Task.Run(() => Ps3Package.WritePkg(pkg, contentId, entries));
            File.WriteAllText(pkg + ".ok", $"{title}\n{titleId}\n{contentId}\n");
        }
        catch (Exception ex) { File.WriteAllText(pkg + ".failed", ex.ToString()); }
        finally { try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { } }
        Application.Current.Shutdown();
    }

    /// <summary>ICON0.PNG: 320 x 176, the game's title on a dark band (the XMB shows it under Game).</summary>
    private static byte[] Ps3Icon(string title)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(20, 24, 40), Color.FromRgb(60, 40, 20), 90), null, new Rect(0, 0, 320, 176));
            var text = new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Georgia"), 30, Brushes.White, 1.0)
            { MaxTextWidth = 300, TextAlignment = TextAlignment.Center, MaxTextHeight = 160, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(text, new Point(10, (176 - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(320, 176, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        png.Save(ms);
        return ms.ToArray();
    }
}
