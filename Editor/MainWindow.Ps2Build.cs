using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Game > Build > PlayStation 2: the game as one disc image (ISO) - the PS2 player (Runtimes/PS2/GAME.ELF, built from the
/// Player folder by build_ps2.sh), SYSTEM.CNF, and the game's data in \DATA\ - with the ESR patch already on it
/// (Engine/Ps2Iso.cs). Its size is the game's own; more than 8 GB is refused.
/// </summary>
public partial class MainWindow
{
    private async void BuildForPs2()
    {
        string runtime = Path.Combine(AppContext.BaseDirectory, "Runtimes", "PS2");
        string elfPath = Path.Combine(runtime, "GAME.ELF"), esrPath = Path.Combine(runtime, "esr_dvd_video.bin");
        if (!File.Exists(elfPath) || !File.Exists(esrPath))
        {
            MessageBox.Show($"The PlayStation 2 player is missing from:\n{runtime}\n\nReinstall the editor.", "Build for PlayStation 2", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (MessageBox.Show("Building saves the project first, so the game is exactly what you have now. Continue?", "Build for PlayStation 2", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject() || _currentProjectFolder is null) return;
        var pick = new SaveFileDialog { Title = "Save the PlayStation 2 disc image", Filter = "Disc image (*.iso)|*.iso", FileName = AmigaName(_currentProjectName) + ".iso", DefaultExt = ".iso" };
        if (pick.ShowDialog(this) != true) return;
        string iso = pick.FileName;
        if (IsSameOrInside(iso, Path.GetFullPath(_currentProjectFolder)))
        {
            MessageBox.Show("The disc image can't be saved in the project's own folder. Choose a different folder.", "Build for PlayStation 2", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string work = Path.Combine(Path.GetTempPath(), "SandcastlePs2_" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(work, "Game_Data");
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            StatusText.Text = "PlayStation 2: packing the game's data...";
            Directory.CreateDirectory(data);
            PlayerBookText.UsedFonts.Clear();
            var payload = CurrentPayload();
            PlayerPack.Export(data, payload, _bookDatabase, _spriteDatabase, _resourceDatabase, _videoDatabase, xaml => PlayerBookText.Convert(xaml, _spriteDatabase));
            PlayerFontPack.Write(data, payload.Manifest.ProjectId, PlayerBookText.UsedFonts, _spriteDatabase, hires: true); // the books' own sizes, as on Windows
            // The sounds raw for the PS2 (MP3 and WAV as plain 16-bit stereo 44,100 Hz): nothing for it to decode as they play.
            IProgress<string> soundStatus = new Progress<string>(s => StatusText.Text = s);
            var resources = _resourceDatabase;
            int rawSounds = await Task.Run(() => Ps2Sounds.Rewrite(data, payload.Manifest.ProjectId, resources, Path.Combine(work, "Sounds"), soundStatus));
            StatusText.Text = "PlayStation 2: packing the game's data...";

            if (Directory.GetDirectories(data).Length > 0) throw new InvalidDataException("The game's data has folders in it - the PlayStation 2 disc takes only files.");
            var files = new DirectoryInfo(data).GetFiles().OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => new Ps2Iso.DiscFile(Ps2Iso.Ps2Name(f.Name), f.FullName, f.Length)).ToList();
            byte[] elf = File.ReadAllBytes(elfPath);
            string bootName = Ps2Iso.BootName(payload.Manifest.ProjectId.ToString("N"));
            long bytes = Ps2Iso.ImageBytes(files, elf.Length, bootName);

            // ---- The limits: 8 GB the disc, under 4 GB each file ----
            if (bytes > Ps2Iso.MaxBytes)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show(
                    $"This game is too big for the PlayStation 2.\n\nIts disc would be {Gb(bytes)}. The most a PlayStation 2 disc can hold is 8 GB (a dual-layer DVD), so it can't be built for the PS2.\n\n"
                    + "Make the project smaller, then build again - for example:\n"
                    + "  - delete sprites, pictures, sounds, music and videos the game doesn't use;\n"
                    + "  - shorten videos or make them smaller (lower resolution or quality);\n"
                    + "  - use compressed music and sounds (OGG) instead of WAV.\n\n"
                    + "What takes the room:\n" + string.Join("\n", files.OrderByDescending(f => f.Length).Select(f => $"  {f.DiscName}: {Gb(f.Length)}")),
                    "Build for PlayStation 2 - over 8 GB", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "PlayStation 2: not built - the game is over 8 GB.";
                return;
            }
            if (files.FirstOrDefault(f => f.Length > Ps2Iso.MaxFileBytes) is { } huge)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"\"{huge.DiscName}\" alone is {Gb(huge.Length)}: a PlayStation 2 disc can't hold one file of 4 GB or more.\n\nMake the project smaller (fewer or smaller {(huge.DiscName.StartsWith("VIDEOS") ? "videos" : huge.DiscName.StartsWith("SOUNDS") ? "sounds and music" : "pictures")}), then build again.",
                    "Build for PlayStation 2", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "PlayStation 2: not built - one file is over 4 GB.";
                return;
            }
            string? drive = Path.GetPathRoot(Path.GetFullPath(iso));
            if (drive is not null && new DriveInfo(drive) is { IsReady: true } d && d.AvailableFreeSpace < bytes + (File.Exists(iso) ? new FileInfo(iso).Length : 0))
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"The disc image needs {Gb(bytes)}, and {drive} has only {Gb(d.AvailableFreeSpace)} free. Free some room or choose another drive.", "Build for PlayStation 2", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            byte[] esr = File.ReadAllBytes(esrPath);
            string volume = _currentProjectName ?? "SANDCASTLE";
            int lastPercent = -1;
            var progress = new Progress<double>(p =>
            {
                int percent = (int)(p * 100);
                if (percent != lastPercent) { lastPercent = percent; StatusText.Text = $"PlayStation 2: writing the disc image... {percent}%"; }
            });
            IProgress<double> report = progress;
            await Task.Run(() => Ps2Iso.Write(iso, volume, elf, bootName, files, esr, p => report.Report(p)));

            bool patched = Ps2Iso.HasEsrPatch(iso);
            long size = new FileInfo(iso).Length;
            StatusText.Text = $"PlayStation 2 disc image built: {iso} ({Gb(size)})";
            string disc = size > Ps2Iso.SingleLayerBytes
                ? "It is bigger than a single-layer DVD (4.7 GB): burn it to a dual-layer DVD (DVD+R DL)."
                : "Burn it to a DVD-R or DVD+R (even a small game goes on a DVD, not a CD).";
            MessageBox.Show(
                $"The PlayStation 2 disc image is built:\n{iso}\n\nSize: {Gb(size)} (the most is 8 GB).\nESR patch: {(patched ? "on - nothing else to patch" : "MISSING - please report this")}.\n\n"
                + $"{disc} Burn slowly (4x or less) for the PS2's drive. On the PS2, start ESR from FreeMcBoot with the disc in.\n"
                + "It also runs as it is in PCSX2, or from OPL (put it in the DVD folder).\n\n"
                + (IsUltima6 ? "This is an Ultima 6 game: it is played with a USB keyboard and mouse plugged into the PS2." : "It is played with the PS2 controller.")
                + (rawSounds > 0 ? $"\n\nSOUNDS: {rawSounds} sound and music file(s) (MP3 / WAV) are on the disc as IMA ADPCM (stereo, 44,100 Hz) - far lighter for the PS2 to play than MP3, "
                    + "and a quarter of plain sound to read off the disc, so the music keeps up even from a slow disc. Your own sound files are not changed." : "")
                + "\n\nHOW IT LOOKS ON THE PS2: the PS2 shows the game at 320 x 200 (each pixel doubled on the TV). Drawing it finer, as Windows does, "
                + "would make it run at about a third of the speed, so:\n"
                + " - Art drawn finer than 320 x 200 (x2, x3, x4) is shrunk on the PS2 and loses pixels. For pixel-perfect art on the PS2, draw it at x1.\n"
                + " - Paperdoll windows (the PaperDoll Editor's: Save / Load, prompts, menus) are laid out 320 x 240 and shown in the game's 200 rows: 5/6 size. "
                + "The PS2 shrinks each one smoothly (every row blended in, none lost), so they look a little softer than on Windows; thin (1-pixel) lines and small letters "
                + "on them stay readable but soft. Your art itself is not changed.",
                "Build for PlayStation 2", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "PlayStation 2: the disc image could not be built.";
            MessageBox.Show($"The disc image could not be built: {ex.Message}", "Build for PlayStation 2", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { /* a temp folder: Windows clears it in time */ }
        }
    }

    private static string Gb(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.00} GB" : $"{bytes / 1e6:0.0} MB";
}
