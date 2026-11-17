using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The Linux build's installer: one .deb for Ubuntu 24.04 and newer - double-clicked, Ubuntu's App Center installs it (and
/// SDL2 with it, if it isn't there), and the game appears with the other applications. It holds:
///   /opt/&lt;package&gt;/&lt;package&gt;      the player (Runtimes/Linux/Game, built by Player/build_linux.sh in Ubuntu 24.04)
///   /opt/&lt;package&gt;/Game_Data        the game's data
///   /usr/share/applications/&lt;package&gt;.desktop, /usr/share/icons/hicolor/256x256/apps/&lt;package&gt;.png
/// Its saves go in the player's own ~/.local/share/Sandcastle RPG Maker/&lt;package&gt; (SANDCASTLE_INSTALLED).
/// A .deb is an "ar" archive of debian-binary, control.tar.gz and data.tar.gz - written here, no Linux tools needed.
/// </summary>
public static class LinuxDeb
{
    /// <summary>The package's name from the game's: lower-case letters, digits and dashes ("sandcastle-my-game").</summary>
    public static string PackageName(string title)
    {
        var sb = new StringBuilder();
        foreach (char ch in title.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        string name = sb.ToString().Trim('-');
        if (name.Length == 0) name = "game";
        if (name.Length > 40) name = name[..40].Trim('-');
        return "sandcastle-" + name;
    }

    /// <summary>Writes the .deb: the player program, the game's data folder (dataFolder), its icon (a PNG) and its name.</summary>
    public static void Write(string debPath, string title, string playerPath, string dataFolder, byte[] iconPng, string version = "1.0")
    {
        string package = PackageName(title);
        string work = Path.Combine(Path.GetTempPath(), "SandcastleDeb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // ---- data.tar.gz: what's installed ----
            string data = Path.Combine(work, "data.tar.gz");
            long installedBytes = 0;
            var stamp = DateTimeOffset.UtcNow;
            using (var file = File.Create(data))
            using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            using (var tar = new TarWriter(gz, TarEntryFormat.Gnu, leaveOpen: false))
            {
                void Dir(string path) => tar.WriteEntry(new GnuTarEntry(TarEntryType.Directory, "./" + path + "/") { Mode = (UnixFileMode)0b111_101_101, ModificationTime = stamp, Uid = 0, Gid = 0, UserName = "root", GroupName = "root" });
                void FileFrom(string path, Stream content, long length, bool executable)
                {
                    var e = new GnuTarEntry(TarEntryType.RegularFile, "./" + path) { Mode = executable ? (UnixFileMode)0b111_101_101 : (UnixFileMode)0b110_100_100, ModificationTime = stamp, Uid = 0, Gid = 0, UserName = "root", GroupName = "root", DataStream = content };
                    tar.WriteEntry(e);
                    installedBytes += length;
                }
                foreach (var d in new[] { "opt", $"opt/{package}", "usr", "usr/share", "usr/share/applications", "usr/share/icons", "usr/share/icons/hicolor", "usr/share/icons/hicolor/256x256", "usr/share/icons/hicolor/256x256/apps" }) Dir(d);
                using (var player = File.OpenRead(playerPath)) FileFrom($"opt/{package}/{package}", player, player.Length, executable: true);
                AddFolder(dataFolder, $"opt/{package}/Game_Data", Dir, (p, s, n) => FileFrom(p, s, n, false));
                var desktop = Encoding.UTF8.GetBytes(
                    "[Desktop Entry]\nType=Application\n" +
                    $"Name={OneLine(title)}\nComment=A game made with Sandcastle RPG Maker\n" +
                    $"Exec=/opt/{package}/{package}\nPath=/opt/{package}\nIcon={package}\nTerminal=false\nCategories=Game;RolePlaying;\n");
                using (var ms = new MemoryStream(desktop)) FileFrom($"usr/share/applications/{package}.desktop", ms, desktop.Length, false);
                using (var ms = new MemoryStream(iconPng)) FileFrom($"usr/share/icons/hicolor/256x256/apps/{package}.png", ms, iconPng.Length, false);
            }
            // ---- control.tar.gz: what it is, what it needs ----
            string control = Path.Combine(work, "control.tar.gz");
            var controlText = Encoding.UTF8.GetBytes(
                $"Package: {package}\nVersion: {version}\nSection: games\nPriority: optional\nArchitecture: amd64\n" +
                "Depends: libc6 (>= 2.38), libsdl2-2.0-0 (>= 2.0.20)\n" +
                $"Installed-Size: {Math.Max(1, installedBytes / 1024)}\n" +
                "Maintainer: Sandcastle RPG Maker <games@sandcastle.invalid>\n" +
                $"Description: {OneLine(title)}\n A game made with Sandcastle RPG Maker.\n");
            using (var file = File.Create(control))
            using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            using (var tar = new TarWriter(gz, TarEntryFormat.Gnu, leaveOpen: false))
            {
                tar.WriteEntry(new GnuTarEntry(TarEntryType.Directory, "./") { Mode = (UnixFileMode)0b111_101_101, ModificationTime = stamp, UserName = "root", GroupName = "root" });
                tar.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, "./control") { Mode = (UnixFileMode)0b110_100_100, ModificationTime = stamp, UserName = "root", GroupName = "root", DataStream = new MemoryStream(controlText) });
            }
            // ---- the .deb: "ar" - debian-binary first, then control, then data ----
            using var deb = File.Create(debPath);
            deb.Write(Encoding.ASCII.GetBytes("!<arch>\n"));
            ArMember(deb, "debian-binary", new MemoryStream(Encoding.ASCII.GetBytes("2.0\n")), 4, stamp);
            using (var c = File.OpenRead(control)) ArMember(deb, "control.tar.gz", c, c.Length, stamp);
            using (var d = File.OpenRead(data)) ArMember(deb, "data.tar.gz", d, d.Length, stamp);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* a temp folder */ }
        }
    }

    private static string OneLine(string s) => s.Replace('\n', ' ').Replace('\r', ' ').Trim();

    /// <summary>A folder's files and folders into the tar, under the path given.</summary>
    private static void AddFolder(string folder, string under, Action<string> dir, Action<string, Stream, long> file)
    {
        dir(under);
        foreach (var f in Directory.GetFiles(folder).OrderBy(x => x, StringComparer.Ordinal))
            using (var s = File.OpenRead(f)) file($"{under}/{Path.GetFileName(f)}", s, s.Length);
        foreach (var d in Directory.GetDirectories(folder).OrderBy(x => x, StringComparer.Ordinal))
            AddFolder(d, $"{under}/{Path.GetFileName(d)}", dir, file);
    }

    /// <summary>One member of an "ar" archive: its 60-byte header (name, date, owner, mode, size), its bytes, padded to even.</summary>
    private static void ArMember(Stream to, string name, Stream content, long length, DateTimeOffset stamp)
    {
        string header = name.PadRight(16) + stamp.ToUnixTimeSeconds().ToString().PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "100644".PadRight(8) + length.ToString().PadRight(10) + "`\n";
        to.Write(Encoding.ASCII.GetBytes(header));
        content.CopyTo(to);
        if (length % 2 == 1) to.WriteByte((byte)'\n');
    }
}
