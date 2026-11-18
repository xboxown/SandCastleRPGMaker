using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6 Apple IIgs mode (File > New Project > Style: Ultima 6 Apple IIgs): an Ultima 6 project whose game runs on a real
/// Apple IIgs - GS/OS and an accelerator required - on its own 65816 player (Player/iigs). The same editor, kept inside the
/// IIgs's limits (AppleIIgsLimits): 16 colours, 16 x 16 tiles, maps up to 256 x 256, SND sounds (Tools > Convert Audio in the
/// Sound Mixer's place), no videos. Build makes a .2mg hard disk; Test the Engine runs it in an Apple IIgs emulator
/// (Game > Apple IIgs Emulator...) with the editor locked until the emulator is closed.
/// </summary>
public partial class MainWindow
{
    private string _projectMachine = GameMachine.Any;
    private bool IsAppleIIgs => IsUltima6 && _projectMachine == GameMachine.AppleIIgs;
    private readonly Dictionary<string, Button> _toolButtons = new();
    private MenuItem? _convertAudioMenuItem, _iigsCheckMenuItem, _iigsEmulatorMenuItem;
    private Separator? _iigsSeparator;
    private Button? _convertAudioToolButton;

    /// <summary>The player's program on the disk (an S16 GS/OS application) and the folder it is kept in beside the editor.</summary>
    private const string IIgsPlayerName = "SANDCASTLE";
    private static string IIgsRuntimeFolder => Path.Combine(AppContext.BaseDirectory, "Runtimes", "AppleIIgs");

    private void ApplyMachineToTools()
    {
        bool iigs = IsAppleIIgs;
        SoundMixerMenuItem.Visibility = iigs ? Visibility.Collapsed : Visibility.Visible;
        VideoLibraryMenuItem.Visibility = iigs ? Visibility.Collapsed : Visibility.Visible;
        if (_toolButtons.TryGetValue("Sound Mixer", out var mixer)) mixer.Visibility = SoundMixerMenuItem.Visibility;
        if (_toolButtons.TryGetValue("Video Library", out var video)) video.Visibility = VideoLibraryMenuItem.Visibility;
        if (_convertAudioMenuItem is null)
        {
            _convertAudioMenuItem = new MenuItem { Header = "Convert Audio...", ToolTip = "Apple IIgs: makes SND sounds (8-bit, mono, 22 kHz or less) from WAV, MP3, OGG, FLAC or IFF files and puts them in the Sound Editor as snd1, snd2..." };
            _convertAudioMenuItem.Click += (_, _) => OpenConvertAudio();
            ToolsMenu.Items.Insert(0, _convertAudioMenuItem);
            _iigsSeparator = new Separator();
            _iigsCheckMenuItem = new MenuItem { Header = "Apple IIgs Check...", ToolTip = "Everything in the project the Apple IIgs can't hold: map sizes, colours, sprite sizes, sounds" };
            _iigsCheckMenuItem.Click += (_, _) => ShowAppleIIgsCheck(onlyIfProblems: false);
            _iigsEmulatorMenuItem = new MenuItem { Header = "Apple IIgs Emulator...", ToolTip = "The emulator Test the Engine runs the game in (KEGS), the GS/OS disk it starts from, and the IIgs ROM" };
            _iigsEmulatorMenuItem.Click += (_, _) => new AppleIIgsEmulatorWindow { Owner = this }.ShowDialog();
            GameMenu.Items.Add(_iigsSeparator);
            GameMenu.Items.Add(_iigsCheckMenuItem);
            GameMenu.Items.Add(_iigsEmulatorMenuItem);
            if (_toolButtons.TryGetValue("Sound Mixer", out var mixerButton))
            {
                var tip = new StackPanel { MaxWidth = 280 };
                tip.Children.Add(new TextBlock { Text = "Convert Audio", FontWeight = FontWeights.Bold });
                tip.Children.Add(new TextBlock { Text = "Apple IIgs: turn sound files into SND sounds for the game (8-bit, mono, 22 kHz or less).", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                _convertAudioToolButton = new Button { Content = new Image { Source = ToolIcons.For("Sound Mixer"), Width = 28, Height = 28 }, ToolTip = tip, Padding = new Thickness(2), Margin = new Thickness(1, 0, 1, 0) };
                _convertAudioToolButton.Click += (_, _) => OpenConvertAudio();
                MainToolBar.Items.Insert(MainToolBar.Items.IndexOf(mixerButton), _convertAudioToolButton);
            }
        }
        var shown = iigs ? Visibility.Visible : Visibility.Collapsed;
        _convertAudioMenuItem.Visibility = _iigsCheckMenuItem!.Visibility = _iigsEmulatorMenuItem!.Visibility = _iigsSeparator!.Visibility = shown;
        if (_convertAudioToolButton is not null) _convertAudioToolButton.Visibility = shown;
        BuildGameMenuItem.ToolTip = iigs ? "Build the game as an Apple IIgs hard disk image (.2mg) with GS/OS's ProDOS inside" : "Build a standalone game: choose the platform and where";
        TestEngineMenuItem.Header = iigs ? "Test the Engine (Apple IIgs emulator)" : "Test the Engine";
        TestTitleScreenMenuItem.Visibility = iigs ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenConvertAudio() => new ConvertAudioWindow(_resourceDatabase) { Owner = this }.ShowDialog();

    // ---- The check ------------------------------------------------------------------------------------------------

    /// <summary>Everything the Apple IIgs can't hold; empty = the game fits.</summary>
    private List<string> AppleIIgsProblems()
    {
        var problems = new List<string>();
        var maps = _worlds.Count > 0 ? _worlds : _map is null ? new List<MapData>() : new List<MapData> { _map };
        if (maps.Count == 0) problems.Add("There is no map yet (Maps tab > Add Map).");
        if (maps.Count > AppleIIgsLimits.MaxMaps) problems.Add($"{maps.Count} maps: the Apple IIgs game holds {AppleIIgsLimits.MaxMaps} at most.");
        foreach (var m in maps)
        {
            if (m.Width > AppleIIgsLimits.MaxMapWidth || m.Height > AppleIIgsLimits.MaxMapHeight)
                problems.Add($"Map \"{m.MapName}\" is {m.Width} x {m.Height}: at most {AppleIIgsLimits.MaxMapWidth} x {AppleIIgsLimits.MaxMapHeight} (Maps tab > Resize).");
            if (m.Tileset.TileWidth != AppleIIgsLimits.TileSize || m.Tileset.TileHeight != AppleIIgsLimits.TileSize)
                problems.Add($"Map \"{m.MapName}\": its tiles are {m.Tileset.TileWidth} x {m.Tileset.TileHeight} - the Apple IIgs's are 16 x 16 (Sprite Editor > Tileset).");
            if (m.Characters.Count > AppleIIgsLimits.MaxCharactersPerMap) problems.Add($"Map \"{m.MapName}\" has {m.Characters.Count} characters: {AppleIIgsLimits.MaxCharactersPerMap} at most.");
            if (m.Objects.Count > AppleIIgsLimits.MaxObjectsPerMap) problems.Add($"Map \"{m.MapName}\" has {m.Objects.Count} objects: {AppleIIgsLimits.MaxObjectsPerMap} at most.");
        }
        if (_videoDatabase.Videos.Count > 0) problems.Add($"Note: the project has {_videoDatabase.Videos.Count} video(s): the Apple IIgs plays none - they are left out of the game.");
        foreach (var r in _resourceDatabase.Entries.Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music))
        {
            var (ok, why) = IIgsSoundOk(r);
            if (!ok) problems.Add($"Sound \"{r.Name}\": {why} - make it again with Tools > Convert Audio.");
        }
        int sounds = _resourceDatabase.Entries.Count(r => r.Kind is ResourceKind.Sound or ResourceKind.Music);
        if (sounds > AppleIIgsLimits.MaxSounds) problems.Add($"{sounds} sounds: {AppleIIgsLimits.MaxSounds} at most.");
        try
        {
            var tiles = new HashSet<string>();
            foreach (var m in maps) foreach (var key in CellKeys(m)) tiles.Add(key);
            if (tiles.Count > AppleIIgsLimits.MaxTiles) problems.Add($"The maps use {tiles.Count} different tiles: the Apple IIgs game holds {AppleIIgsLimits.MaxTiles}.");
            else if (maps.Count > 0 && _tileset is not null)
            {
                // The colours: the IIgs shows 16 at once, from 4096.
                var colours = new HashSet<ushort>();
                foreach (var key in tiles.Take(400))
                    foreach (var part in key.Length == 0 ? Array.Empty<string>() : key.Split('|'))
                    {
                        int colon = part.LastIndexOf(':');
                        if (TilePixels(part[..colon], int.Parse(part[(colon + 1)..])) is { } px)
                            foreach (uint c in px) if ((c >> 24) >= 128) colours.Add(SuperHiRes.To12Bit(c));
                    }
                if (colours.Count > AppleIIgsLimits.Colors)
                    problems.Add($"Note: the map's tiles use {colours.Count} colours; the Apple IIgs shows {AppleIIgsLimits.Colors} at once (from 4096), so Build picks the {AppleIIgsLimits.Colors} that show them best. Draw with 16 colours for the look you want.");
            }
        }
        catch (Exception ex) { problems.Add("The tiles couldn't be read: " + ex.Message); }
        if (PlayerCharacter() is null) problems.Add("No Avatar: tick Test Player on a character (Character Editor) - the game is played as them.");
        problems.AddRange(PictureSizeProblems());
        // Talk: the commands the IIgs player doesn't do yet are left out of it.
        var leftOut = new SortedSet<string>();
        foreach (var who in _characterDatabase.Characters.Where(c => c.TalkCommands.Count > 0))
            foreach (var type in AppleIIgsTalk.Compile(who.TalkCommands, _ => 0, out _).Unsupported) leftOut.Add(type);
        if (leftOut.Count > 0) problems.Add($"Note: in the characters' talk, these commands aren't on the Apple IIgs yet and are left out there: {string.Join(", ", leftOut)}. Show Text with replies, Set / Conditional Branch on variables and switches, Labels and Stop Conversation work.");
        var hudDesign = _systemSettings.Ultima6.Hud;
        if (hudDesign.First(HudPieceKind.GameView) is not null && (hudDesign.Width != AppleIIgsLimits.ScreenWidth || hudDesign.Height != AppleIIgsLimits.ScreenHeight))
            problems.Add($"The HUD is {hudDesign.Width} x {hudDesign.Height}: the Apple IIgs's screen is {AppleIIgsLimits.ScreenWidth} x {AppleIIgsLimits.ScreenHeight} (HUD Editor).");
        if (hudDesign.First(HudPieceKind.GameView) is { } gv && (gv.Width / AppleIIgsLimits.TileSize > 20 || gv.Height / AppleIIgsLimits.TileSize > 12))
            problems.Add("The HUD's Game View is bigger than the screen holds (at most 20 x 12 cells of 16 x 16).");
        return problems;
    }

    /// <summary>Pictures the IIgs can't show: a character's look over 32 x 32, a portrait over 56 x 64, anything bigger than the 320 x 200 screen.</summary>
    private List<string> PictureSizeProblems()
    {
        var problems = new List<string>();
        var sizes = new Dictionary<string, (int W, int H)>(StringComparer.OrdinalIgnoreCase);
        (int W, int H) Size(string file)
        {
            if (sizes.TryGetValue(file, out var known)) return known;
            try
            {
                using var stream = File.OpenRead(file);
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                return sizes[file] = (frame.PixelWidth, frame.PixelHeight);
            }
            catch { return sizes[file] = (0, 0); }
        }
        SpriteEntry? Find(string group, string name) =>
            _spriteDatabase.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name)
            ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == name);
        (int W, int H) Biggest(SpriteEntry sprite) => sprite.Frames.Where(f => File.Exists(f.FilePath)).Select(f => Size(f.FilePath)).DefaultIfEmpty((W: 0, H: 0)).MaxBy(s => s.W * s.H);
        foreach (var who in _characterDatabase.Characters)
        {
            foreach (var link in who.PoseSprites.Values.SelectMany(p => p.Directions.Values).DistinctBy(l => l.SpriteGroup + "/" + l.SpriteName))
                if (Find(link.SpriteGroup, link.SpriteName) is { } sprite && Biggest(sprite) is var (w, h) && (w > AppleIIgsLimits.MaxSpriteWidth || h > AppleIIgsLimits.MaxSpriteHeight))
                {
                    problems.Add($"{who.Name}'s look \"{sprite.Name}\" is {w} x {h}: the Apple IIgs's characters are at most {AppleIIgsLimits.MaxSpriteWidth} x {AppleIIgsLimits.MaxSpriteHeight} (16 x 16 as Ultima 6's people, 32 x 32 for big monsters).");
                    break;
                }
            if (who.FaceSpriteName.Length > 0 && Find(who.FaceSpriteGroup, who.FaceSpriteName) is { } face && Biggest(face) is var (fw, fh) && (fw > AppleIIgsLimits.PortraitWidth || fh > AppleIIgsLimits.PortraitHeight))
                problems.Add($"{who.Name}'s portrait \"{face.Name}\" is {fw} x {fh}: the Apple IIgs's portraits are at most {AppleIIgsLimits.PortraitWidth} x {AppleIIgsLimits.PortraitHeight}, as Ultima 6's.");
        }
        foreach (var sprite in _spriteDatabase.Groups.Where(g => g.Name != SpriteDatabase.TilesetGroupName).SelectMany(g => g.Sprites))
            if (Biggest(sprite) is var (w, h) && (w > AppleIIgsLimits.ScreenWidth || h > AppleIIgsLimits.ScreenHeight))
                problems.Add($"Sprite \"{sprite.Name}\" is {w} x {h}: bigger than the Apple IIgs's screen ({AppleIIgsLimits.ScreenWidth} x {AppleIIgsLimits.ScreenHeight}) - the HUD and everything in it must fit it.");
        return problems;
    }

    /// <summary>A sound the IIgs can play: an 8-bit mono WAV of 22050 a second or less, at most 60 KB.</summary>
    private static (bool Ok, string Why) IIgsSoundOk(ResourceEntry r)
    {
        try
        {
            if (!File.Exists(r.FilePath)) return (false, "its file is missing");
            using var reader = new NAudio.Wave.WaveFileReader(r.FilePath);
            var f = reader.WaveFormat;
            if (f.Encoding != NAudio.Wave.WaveFormatEncoding.Pcm || f.BitsPerSample != 8 || f.Channels != 1) return (false, $"it is {f.BitsPerSample}-bit {(f.Channels == 1 ? "mono" : "stereo")}, not 8-bit mono");
            if (f.SampleRate > AppleIIgsLimits.MaxSampleRate) return (false, $"{f.SampleRate} a second - 22050 at most");
            if (reader.Length > AppleIIgsLimits.MaxSoundBytes) return (false, $"{reader.Length / 1024} KB - {AppleIIgsLimits.MaxSoundBytes / 1024} KB at most (the IIgs's sound chip holds 64 KB); it is cut there");
            return (true, "");
        }
        catch { return (false, "it isn't a WAV file"); }
    }

    /// <summary>Shows the check; true = nothing in the way.</summary>
    private bool ShowAppleIIgsCheck(bool onlyIfProblems)
    {
        var problems = AppleIIgsProblems();
        bool blocking = problems.Any(p => !p.StartsWith("Note:"));
        if (problems.Count == 0)
        {
            if (!onlyIfProblems) MessageBox.Show(this, "Everything fits the Apple IIgs.", "Apple IIgs Check", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        string text = string.Join("\n", problems.Take(25).Select(p => "- " + p)) + (problems.Count > 25 ? $"\n...and {problems.Count - 25} more." : "");
        MessageBox.Show(this, (blocking ? "The Apple IIgs can't hold this yet:\n\n" : "Note:\n\n") + text, "Apple IIgs Check", MessageBoxButton.OK, blocking ? MessageBoxImage.Warning : MessageBoxImage.Information);
        return !blocking;
    }

    // ---- The game, as the IIgs player reads it ------------------------------------------------------------------

    private CharacterDefinition? PlayerCharacter() => _characterDatabase.Characters.FirstOrDefault(c => c.IsTestPlayer);

    /// <summary>Each cell of a map: what is drawn there, bottom to top (its layers), as one key - the same key, the same IIgs tile.</summary>
    private static IEnumerable<string> CellKeys(MapData m)
    {
        var layers = m.Layers.Where(l => l.Floor <= 1).ToList();
        for (int y = 0; y < m.Height; y++)
            for (int x = 0; x < m.Width; x++)
                yield return CellKey(m, layers, x, y);
    }

    private static string CellKey(MapData m, List<MapLayer> layers, int x, int y)
    {
        var parts = new List<string>();
        foreach (var l in layers)
        {
            int t = l.GetTile(x, y, m.Width);
            if (t >= 0) parts.Add(m.Tileset.SheetNameOf(t) + ":" + TileSheets.Local(t));
        }
        return string.Join("|", parts);
    }

    private static uint[] Pixels(BitmapSource source, out int w, out int h)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth; h = bgra.PixelHeight;
        var px = new uint[w * h];
        bgra.CopyPixels(px, w * 4, 0);
        return px;
    }

    private uint[]? TilePixels(string sheetName, int local)
    {
        if (Sheet(sheetName) is not { } sheet) return null;
        int s = AppleIIgsLimits.TileSize, col = local % sheet.Columns, row = local / sheet.Columns;
        if ((col + 1) * s > sheet.Bitmap.PixelWidth || (row + 1) * s > sheet.Bitmap.PixelHeight) return null;
        return Pixels(new CroppedBitmap(sheet.Bitmap, new Int32Rect(col * s, row * s, s, s)), out _, out _);
    }

    /// <summary>The Avatar's look, one 16 x 16 picture a facing (south, north, east, west): its Idle (or first) pose.</summary>
    private List<uint[]> AvatarPictures()
    {
        var who = PlayerCharacter();
        return who is null ? new List<uint[]>() : LookFiles(who).Select(f => f is not null && File.Exists(f) ? FitInto16(f) : new uint[256]).ToList();
    }

    /// <summary>A character's picture files for south, north, east and west: its Idle (or Walking, or first) pose, the first frame.</summary>
    private List<string?> LookFiles(CharacterDefinition who)
    {
        var list = new List<string?>();
        var pose = _poseDatabase.Poses.FirstOrDefault(p => p.Name == "Idle" && who.PoseSprites.ContainsKey(p.Id))
                   ?? _poseDatabase.Poses.FirstOrDefault(p => p.Name == "Walking" && who.PoseSprites.ContainsKey(p.Id));
        PoseAssignment? assignment = pose is not null ? who.PoseSprites[pose.Id] : who.PoseSprites.Values.FirstOrDefault();
        foreach (var facing in new[] { SpriteDirection.South, SpriteDirection.North, SpriteDirection.East, SpriteDirection.West })
        {
            var link = assignment?.Resolve(facing);
            var sprite = link is null ? null : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName)
                                             ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == link.SpriteName);
            list.Add(sprite?.Frames.FirstOrDefault()?.FilePath);
        }
        return list;
    }

    /// <summary>A picture fitted into w x h (shrunk or grown keeping its shape, in the middle); the rest see-through.</summary>
    private static uint[] FitPicture(string file, int w, int h)
    {
        var img = new BitmapImage();
        img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(file)); img.EndInit();
        var src = Pixels(img, out int sw, out int sh);
        double k = Math.Min((double)w / sw, (double)h / sh);
        int dw = Math.Max(1, (int)(sw * k)), dh = Math.Max(1, (int)(sh * k)), ox = (w - dw) / 2, oy = (h - dh) / 2;
        var dst = new uint[w * h];
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
                dst[(oy + y) * w + ox + x] = src[Math.Min(sh - 1, (int)(y / k)) * sw + Math.Min(sw - 1, (int)(x / k))];
        return dst;
    }

    /// <summary>A sprite as the IIgs player draws it: at most 32 x 32 (shrunk, keeping its shape), its width a multiple of 4 (padded each side).</summary>
    private static (uint[] Px, int W, int H) FitSprite(string file)
    {
        var img = new BitmapImage();
        img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(file)); img.EndInit();
        var src = Pixels(img, out int w, out int h);
        double scale = Math.Min(1.0, Math.Min((double)AppleIIgsLimits.MaxSpriteWidth / w, (double)AppleIIgsLimits.MaxSpriteHeight / h));
        int dw = Math.Max(1, (int)(w * scale)), dh = Math.Max(1, (int)(h * scale));
        int pw = (dw + 3) / 4 * 4, left = (pw - dw) / 2;
        var dst = new uint[pw * dh];
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
                dst[y * pw + left + x] = src[Math.Min(h - 1, (int)(y / scale)) * w + Math.Min(w - 1, (int)(x / scale))];
        return (dst, pw, dh);
    }

    /// <summary>A picture made to fit 16 x 16: shrunk (keeping its shape) if bigger, standing on the bottom, in the middle.</summary>
    private static uint[] FitInto16(string file)
    {
        var img = new BitmapImage();
        img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(file)); img.EndInit();
        var src = Pixels(img, out int w, out int h);
        var dst = new uint[256];
        double scale = Math.Min(1.0, Math.Min(16.0 / w, 16.0 / h));
        int dw = Math.Max(1, (int)(w * scale)), dh = Math.Max(1, (int)(h * scale)), ox = (16 - dw) / 2, oy = 16 - dh;
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
                dst[(oy + y) * 16 + ox + x] = src[Math.Min(h - 1, (int)(y / scale)) * w + Math.Min(w - 1, (int)(x / scale))];
        return dst;
    }

    private AppleIIgsGamePack MakeAppleIIgsPack()
    {
        var pack = new AppleIIgsGamePack { Title = _currentProjectName ?? "Game" };
        var start = _worlds.FirstOrDefault() ?? _map ?? throw new InvalidOperationException("There is no map.");
        var layers = start.Layers.Where(l => l.Floor <= 1).ToList();
        // Every different cell picture, composed from its layers.
        var keys = new Dictionary<string, int>();
        var tilePixels = new List<uint[]>();
        var flags = new List<byte>();
        var map = new ushort[start.Width * start.Height];
        for (int y = 0; y < start.Height; y++)
            for (int x = 0; x < start.Width; x++)
            {
                string key = CellKey(start, layers, x, y);
                if (!keys.TryGetValue(key, out int n))
                {
                    if (keys.Count >= AppleIIgsLimits.MaxTiles) throw new InvalidOperationException($"More than {AppleIIgsLimits.MaxTiles} different tiles.");
                    var px = new uint[256];
                    bool walkable = true, water = false;
                    foreach (var part in key.Length == 0 ? Array.Empty<string>() : key.Split('|'))
                    {
                        int colon = part.LastIndexOf(':');
                        string sheet = part[..colon];
                        int local = int.Parse(part[(colon + 1)..]);
                        if (TilePixels(sheet, local) is { } top)
                            for (int i = 0; i < 256; i++) if ((top[i] >> 24) >= 128) px[i] = top[i];
                        if (_tilePropertiesDatabase.ByTileKey.TryGetValue($"{sheet}:{local}", out var props)) { walkable &= props.Walkable; water |= props.IsWater; }
                    }
                    n = keys[key] = tilePixels.Count;
                    tilePixels.Add(px);
                    flags.Add((byte)((walkable ? 1 : 0) | (water ? 2 : 0)));
                }
                map[y * start.Width + x] = (ushort)n;
            }
        var avatar = AvatarPictures();
        while (avatar.Count < 4) avatar.Add(new uint[256]);
        // Objects and characters on the starting map, each picture once (Engine/AppleIIgs.cs: sprites).
        var spriteIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var spritePixels = new List<(uint[] Px, int W, int H)>();
        int SpriteOf(string? file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return -1;
            if (spriteIndex.TryGetValue(file, out int known)) return known;
            spritePixels.Add(FitSprite(file));
            return spriteIndex[file] = spritePixels.Count - 1;
        }
        var objects = new List<(int X, int Y, int Sprite, int Flags)>();
        foreach (var instance in start.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            var def = _objectDatabase.Objects.FirstOrDefault(o => o.Id == instance.ObjectId);
            if (def is null || def.Type == ObjectType.Roof || instance.Z >= MapFloors.BaseLift(2)) continue; // Ultima 6: no roofs, no upper floors
            var state = new ObjectState(instance.IsOpenRuntime ?? false, instance.IsLockedRuntime ?? false, instance.IsLitRuntime ?? false, instance.IsSpentRuntime,
                Math.Max(1, instance.Quantity), instance.Facing, Stage: instance.FrameRuntime, LiquidId: instance.LiquidIdRuntime ?? -1);
            var sprite = ObjectLooks.SpriteFor(def, state, _spriteDatabase);
            string? file = sprite?.Frames.FirstOrDefault()?.FilePath ?? (string.IsNullOrEmpty(def.IconPath) ? null : def.IconPath);
            int n = SpriteOf(file);
            if (n < 0 || instance.X < 0 || instance.Y < 0 || instance.X >= start.Width || instance.Y >= start.Height) continue;
            objects.Add((instance.X, instance.Y, n, def.IsSolid ? 1 : 0));
        }
        var characters = new List<(int X, int Y, int Sprite, int Flags, int Facing, int Talk, int Portrait)>();
        // Talk: each character's Talk commands made into the IIgs's talk code once (Engine/AppleIIgsTalk.cs); variables and switches by name.
        var variableIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _variableDatabase.Variables) variableIndex.TryAdd(v.Name, variableIndex.Count);
        int VariableOf(string name) { if (!variableIndex.TryGetValue(name, out int n)) variableIndex[name] = n = variableIndex.Count; return n; }
        var talkOf = new Dictionary<int, int>();
        var design = _systemSettings.Ultima6.Hud;
        var portraitPiece = design.First(HudPieceKind.Portrait) ?? design.First(HudPieceKind.Panel);
        var portraitRect = portraitPiece is null ? (X: 0, Y: 0, Width: 0, Height: 0)
            : (X: portraitPiece.X & ~1, Y: portraitPiece.Y, Width: Math.Clamp(portraitPiece.Width / 4 * 4, 4, 320), Height: Math.Clamp(portraitPiece.Height, 1, 200));
        var portraitOf = new Dictionary<int, int>();
        foreach (var placed in start.Characters.Where(c => !c.Dead))
        {
            var who = _characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId);
            if (who is null || who.IsTestPlayer) continue;
            if (placed.X < 0 || placed.Y < 0 || placed.X >= start.Width || placed.Y >= start.Height) continue;
            var files = LookFiles(who);
            int first = spritePixels.Count;
            foreach (var f in files) { if (f is not null && File.Exists(f)) spritePixels.Add(FitSprite(f)); else spritePixels.Add((new uint[16 * 16], 16, 16)); }
            int facing = placed.Facing switch { SpriteDirection.North => 1, SpriteDirection.East => 2, SpriteDirection.West => 3, _ => 0 };
            if (!talkOf.TryGetValue(who.Id, out int talk))
            {
                talk = -1;
                if (who.TalkCommands.Count > 0)
                {
                    AppleIIgsTalk.Compile(who.TalkCommands, VariableOf, out var code);
                    talk = pack.Talks.Count;
                    pack.Talks.Add((who.Name, code));
                }
                talkOf[who.Id] = talk;
            }
            if (!portraitOf.TryGetValue(who.Id, out int portrait))
            {
                portrait = -1;
                var face = who.FaceSpriteName.Length == 0 ? null : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == who.FaceSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == who.FaceSpriteName)
                    ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == who.FaceSpriteName);
                if (portraitRect.Width > 0 && face?.Frames.FirstOrDefault()?.FilePath is { } faceFile && File.Exists(faceFile))
                {
                    portrait = spritePixels.Count;
                    spritePixels.Add((FitPicture(faceFile, portraitRect.Width, portraitRect.Height), portraitRect.Width, portraitRect.Height));
                }
                portraitOf[who.Id] = portrait;
            }
            characters.Add((placed.X, placed.Y, first, 1, facing, talk < 0 ? 0xFFFF : talk, portrait < 0 ? 0xFFFF : portrait));
        }
        if (characters.Count > AppleIIgsLimits.MaxCharactersPerMap) characters.RemoveRange(AppleIIgsLimits.MaxCharactersPerMap, characters.Count - AppleIIgsLimits.MaxCharactersPerMap);
        var hud = ComposeHud(pack);
        pack.Palette = SuperHiRes.MakePalette(tilePixels.SelectMany(p => p).Concat(avatar.SelectMany(p => p)).Concat(spritePixels.SelectMany(p => p.Px)).Concat(hud ?? Array.Empty<uint>()));
        if (hud is not null) pack.HudBackground = SuperHiRes.Pack(hud, 320, 200, pack.Palette, spriteZeroIsClear: false);
        for (int i = 0; i < pack.Labels.Count; i++)
            if (pack.Labels[i].Color < 0) pack.Labels[i] = pack.Labels[i] with { Color = SuperHiRes.Nearest(pack.Palette, (uint)(-pack.Labels[i].Color - 1) | 0xFF000000u, false) };
        foreach (var px in tilePixels) pack.Tiles.Add(SuperHiRes.Pack(px, 16, 16, pack.Palette, spriteZeroIsClear: false));
        pack.TileFlags.AddRange(flags);
        foreach (var px in avatar) pack.AvatarFrames.Add(SuperHiRes.Pack(px, 16, 16, pack.Palette, spriteZeroIsClear: true));
        foreach (var (px, w, h) in spritePixels) pack.Sprites.Add((w, h, SuperHiRes.Pack(px, w, h, pack.Palette, spriteZeroIsClear: true)));
        pack.Objects.AddRange(objects);
        pack.Characters.AddRange(characters);
        pack.PortraitRect = portraitRect;
        if (design.First(HudPieceKind.MemberName) is { } namePiece) pack.NameRect = (namePiece.X & ~1, namePiece.Y, Math.Min(320 - (namePiece.X & ~1), (namePiece.Width + 3) / 4 * 4), namePiece.Height);
        for (int i = 0; i < pack.Words.Count; i++)
        {
            string id = new[] { "YouSay", "Bye", "NoHelp", "FunnyNoResponse", "Talk", "Nothing", "TalkSelf" }[i];
            pack.Words[i] = U6Words.Of(_systemSettings.Ultima6, id);
        }
        int Colour(string text, int fallback)
        {
            try { if (text.Length > 0 && ColorConverter.ConvertFromString(text) is Color c) return SuperHiRes.Nearest(pack.Palette, 0xFF000000u | (uint)(c.R << 16 | c.G << 8 | c.B), false); } catch { }
            return fallback;
        }
        pack.TextColor = design.First(HudPieceKind.TextArea) is { TextColor.Length: > 0 } area ? Colour(area.TextColor, 0xFFFF) : 0xFFFF;
        pack.KeywordColor = Colour(_systemSettings.Ultima6.KeywordColor, 0xFFFF);
        foreach (var name in variableIndex.OrderBy(kv => kv.Value).Select(kv => kv.Key))
        {
            var v = _variableDatabase.Variables.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            pack.Variables.Add(0);
        }
        pack.MapWidth = start.Width; pack.MapHeight = start.Height; pack.Map = map;
        pack.StartX = start.StartX >= 0 && start.StartX < start.Width ? start.StartX : start.Width / 2;
        pack.StartY = start.StartY >= 0 && start.StartY < start.Height ? start.StartY : start.Height / 2;
        foreach (var r in _resourceDatabase.Entries.Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music).Take(AppleIIgsLimits.MaxSounds))
        {
            if (!IIgsSoundOk(r).Ok && !File.Exists(r.FilePath)) continue;
            try
            {
                using var reader = new NAudio.Wave.WaveFileReader(r.FilePath);
                var bytes = new byte[Math.Min(reader.Length, AppleIIgsLimits.MaxSoundBytes)];
                int read = reader.Read(bytes, 0, bytes.Length);
                pack.Sounds.Add((r.Name, reader.WaveFormat.SampleRate, AppleIIgsGamePack.DocSafe(bytes[..read])));
            }
            catch { /* the check already said so */ }
        }
        return pack;
    }

    /// <summary>
    /// The HUD (HUD Editor) as the IIgs shows it at the start: its frame, pictures, buttons, dials and the inventory view's
    /// body slots composed into one 320 x 200 background; where the Game View and the text area are; the member's name as
    /// words. Null: no HUD (no Game View) - the world shows in the middle of the screen. Label colours are -1 - RGB
    /// until the palette is made (then 0-15), or $FFFF for the brightest.
    /// </summary>
    private uint[]? ComposeHud(AppleIIgsGamePack pack)
    {
        var design = _systemSettings.Ultima6.Hud;
        if (design.First(HudPieceKind.GameView) is not { } view) return null;
        var canvas = new uint[320 * 200];
        SpriteEntry? Of(SpriteLink link) => !link.IsSet ? null :
            _spriteDatabase.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName)
            ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == link.SpriteName);
        void Draw(SpriteEntry? sprite, int x, int y, int w, int h, bool fit)
        {
            string? file = sprite?.Frames.FirstOrDefault()?.FilePath ?? sprite?.FilePath;
            if (string.IsNullOrEmpty(file) || !File.Exists(file) || w <= 0 || h <= 0) return;
            var img = new BitmapImage();
            img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(file)); img.EndInit();
            var src = Pixels(img, out int sw, out int sh);
            int dw = w, dh = h, ox = 0, oy = 0;
            if (fit) { double k = Math.Min((double)w / sw, (double)h / sh); dw = Math.Max(1, (int)(sw * k)); dh = Math.Max(1, (int)(sh * k)); ox = (w - dw) / 2; oy = (h - dh) / 2; }
            for (int yy = 0; yy < dh; yy++)
                for (int xx = 0; xx < dw; xx++)
                {
                    int px = x + ox + xx, py = y + oy + yy;
                    if (px < 0 || py < 0 || px >= 320 || py >= 200) continue;
                    uint c = src[Math.Min(sh - 1, yy * sh / dh) * sw + Math.Min(sw - 1, xx * sw / dw)];
                    if ((c >> 24) >= 128) canvas[py * 320 + px] = c | 0xFF000000u;
                }
        }
        Draw(Of(design.Frame), 0, 0, 320, 200, fit: false);
        var player = PlayerCharacter();
        var front = new List<HudPiece>();
        foreach (var piece in design.Pieces)
            switch (piece.Kind)
            {
                case HudPieceKind.Picture or HudPieceKind.Panel:
                    if (piece.Kind == HudPieceKind.Picture && piece.InFront) front.Add(piece);
                    else Draw(Of(piece.Sprite), piece.X, piece.Y, piece.Width, piece.Height, fit: false);
                    break;
                case HudPieceKind.CommandButton or HudPieceKind.CustomButton or HudPieceKind.Compass or HudPieceKind.SunMoon:
                    Draw(Of(piece.Sprite), piece.X, piece.Y, piece.Width, piece.Height, fit: false);
                    break;
                case HudPieceKind.BodySlot: // the inventory view: what the Avatar wears there, or the slot's own picture
                {
                    var worn = player is not null && player.EquippedSlots.TryGetValue(piece.Slot, out int id) ? player.Inventory.FirstOrDefault(i => i.InstanceId == id) : null;
                    var sprite = worn is not null ? DollLook.ItemSprite(worn.Category, worn.ItemId, _objectDatabase, _bookDatabase, _containerDatabase, _spriteDatabase) : Of(piece.Sprite);
                    Draw(sprite, piece.X, piece.Y, piece.Width, piece.Height, fit: true);
                    break;
                }
                case HudPieceKind.MemberName when player is not null:
                    pack.Labels.Add((piece.X, piece.Y, LabelColor(piece.TextColor), player.Name));
                    break;
            }
        foreach (var piece in front) Draw(Of(piece.Sprite), piece.X, piece.Y, piece.Width, piece.Height, fit: false);
        int columns = Math.Clamp(view.Width / AppleIIgsLimits.TileSize, 1, 20), rows = Math.Clamp(view.Height / AppleIIgsLimits.TileSize, 1, 12);
        pack.View = (Math.Clamp(view.X & ~1, 0, 320 - columns * 16), Math.Clamp(view.Y, 0, 200 - rows * 16), columns, rows);
        if (design.First(HudPieceKind.TextArea) is { } text) pack.TextArea = (text.X, text.Y, text.Width, text.Height);
        return canvas;

        static int LabelColor(string color)
        {
            try { if (color.Length > 0 && ColorConverter.ConvertFromString(color) is Color c) return -1 - (c.R << 16 | c.G << 8 | c.B); } catch { }
            return 0xFFFF;
        }
    }

    /// <summary>The game's disk: the player (S16), GAME.DAT and SOUNDS.DAT on a ProDOS volume, as a .2mg (or plain blocks).</summary>
    private byte[] MakeAppleIIgsDisk(bool twoMg, out string volumeName)
    {
        string player = Path.Combine(IIgsRuntimeFolder, IIgsPlayerName);
        if (!File.Exists(player)) throw new FileNotFoundException("The Apple IIgs player isn't installed with the editor (Runtimes\\AppleIIgs\\SANDCASTLE).");
        var pack = MakeAppleIIgsPack();
        var volume = new ProDosVolume(_currentProjectName ?? "GAME");
        volumeName = volume.VolumeName;
        volume.Add(IIgsPlayerName, 0xB3, 0xDB03, File.ReadAllBytes(player)); // S16: a GS/OS application (aux $DB03: desktop, GS/OS aware)
        volume.Add("GAME.DAT", 0x06, 0x0000, pack.Write());
        volume.Add("SOUNDS.DAT", 0x06, 0x0000, pack.WriteSounds());
        var blocks = volume.Build();
        return twoMg ? ProDosVolume.Wrap2mg(blocks) : blocks;
    }

    private void BuildAppleIIgsGame()
    {
        if (!ShowAppleIIgsCheck(onlyIfProblems: true)) return;
        if (MessageBox.Show(this, "Building saves the project first, so the game is exactly what you have now. Continue?", "Build", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject()) return;
        string gameName = ProDosVolume.CleanName(_currentProjectName ?? "Game", "GAME");
        var pick = new SaveFileDialog
        {
            Title = "Where should the Apple IIgs disk be built?",
            Filter = "Apple IIgs hard disk image (*.2mg)|*.2mg|ProDOS-order image (*.po)|*.po|Hard disk image (*.hdv)|*.hdv",
            FileName = gameName + ".2mg",
            OverwritePrompt = true
        };
        if (pick.ShowDialog(this) != true) return;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var disk = MakeAppleIIgsDisk(Path.GetExtension(pick.FileName).Equals(".2mg", StringComparison.OrdinalIgnoreCase), out string volumeName);
            File.WriteAllBytes(pick.FileName, disk);
            StatusText.Text = $"Apple IIgs disk built: {pick.FileName}";
            MessageBox.Show(this, $"Your game's disk was built:\n{pick.FileName}\n\nOn the Apple IIgs (GS/OS 6, an accelerator), or in KEGS as a second hard disk: open the disk \"{volumeName}\" on the desktop and double-click {IIgsPlayerName}.", "Build", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, $"The disk could not be built: {ex.Message}", "Build", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Mouse.OverrideCursor = null; }
    }

    // ---- Test the Engine: the emulator -----------------------------------------------------------------------------

    private void LaunchAppleIIgsTest()
    {
        var settings = AppleIIgsEmulatorSettings.Load();
        if (!File.Exists(settings.EmulatorPath) || !File.Exists(settings.BootImagePath))
        {
            MessageBox.Show(this, "First tell the editor which Apple IIgs emulator to use and the GS/OS disk it starts from (Game > Apple IIgs Emulator...).", "Test the Engine", MessageBoxButton.OK, MessageBoxImage.Information);
            if (new AppleIIgsEmulatorWindow { Owner = this }.ShowDialog() != true) return;
            settings = AppleIIgsEmulatorSettings.Load();
            if (!File.Exists(settings.EmulatorPath) || !File.Exists(settings.BootImagePath)) return;
        }
        var (accelerated, emulatorName) = AppleIIgsEmulatorSettings.Accelerator(settings.EmulatorPath);
        if (!accelerated)
        {
            MessageBox.Show(this, $"This emulator ({Path.GetFileName(settings.EmulatorPath)}) has no accelerator - the game needs one (a TransWarp GS or a ZipGS).\n\nPick an emulator that emulates an accelerator, such as KEGS (it emulates a ZipGS: kegs.sourceforge.net), in Game > Apple IIgs Emulator...", "Test the Engine", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (!ShowAppleIIgsCheck(onlyIfProblems: true)) return;
        string folder = Path.Combine(Path.GetTempPath(), "SandcastleRpgMaker", "AppleIIgsTest");
        Directory.CreateDirectory(folder);
        string disk = Path.Combine(folder, "test.2mg"), volumeName;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            File.WriteAllBytes(disk, MakeAppleIIgsDisk(twoMg: true, out volumeName));
        }
        catch (Exception ex) { MessageBox.Show(this, $"The test disk could not be made: {ex.Message}", "Test the Engine", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        finally { Mouse.OverrideCursor = null; }

        System.Diagnostics.Process process;
        try { process = AppleIIgsEmulatorSettings.Start(settings, disk, folder); }
        catch (Exception ex) { MessageBox.Show(this, $"The emulator could not be started: {ex.Message}", "Test the Engine", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        // The editor waits, locked, until the emulator is closed.
        new AppleIIgsRunningWindow(process, emulatorName, volumeName, IIgsPlayerName) { Owner = this }.ShowDialog();
    }
}

/// <summary>Game > Apple IIgs Emulator...: kept for this Windows user (not in the project) - every Apple IIgs project uses it.</summary>
public sealed class AppleIIgsEmulatorSettings
{
    public string EmulatorPath { get; set; } = "";
    /// <summary>The disk GS/OS starts from (.2mg, .hdv, .po): the emulator's first hard disk (slot 7, drive 1).</summary>
    public string BootImagePath { get; set; } = "";
    /// <summary>The IIgs ROM file (ROM 01 or ROM 03); empty: the emulator's own setting.</summary>
    public string RomPath { get; set; } = "";
    /// <summary>The accelerator's speed in MHz (KEGS's ZipGS: 8, 16, 32, 64 or 128).</summary>
    public int AcceleratorMHz { get; set; } = 8;

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SandcastleRpgMaker", "appleiigs-emulator.json");

    public static AppleIIgsEmulatorSettings Load()
    {
        try { if (File.Exists(FilePath)) return System.Text.Json.JsonSerializer.Deserialize<AppleIIgsEmulatorSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Whether an emulator emulates an accelerator, read from its program: KEGS (0.84 and later) and its family (GSplus,
    /// GSport) emulate a ZipGS; an emulator with no ZipGS or TransWarp GS in it can't run the game.
    /// </summary>
    public static (bool Accelerated, string Name) Accelerator(string exe)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(exe); } catch { return (false, Path.GetFileNameWithoutExtension(exe)); }
        bool Has(string s) => Find(bytes, System.Text.Encoding.ASCII.GetBytes(s)) || Find(bytes, System.Text.Encoding.Unicode.GetBytes(s));
        string name = Has("KEGS") ? "KEGS" : Has("GSplus") ? "GSplus" : Has("GSport") ? "GSport" : Path.GetFileNameWithoutExtension(exe);
        bool accelerated = Has("ZipGS") || Has("Zip GS") || Has("TransWarp") || Has("8.0MHz (Zip)");
        return (accelerated, name);
    }

    private static bool Find(byte[] hay, byte[] needle)
    {
        for (int i = 0; i <= hay.Length - needle.Length; i++)
        {
            if (hay[i] != needle[0]) continue;
            int j = 1;
            while (j < needle.Length && hay[i + j] == needle[j]) j++;
            if (j == needle.Length) return true;
        }
        return false;
    }

    /// <summary>
    /// A config.kegs line whose path is relative (a disk "s7d3 = ../Games.po", the ROM "g_cfg_rom_path = ../ROM") made
    /// whole from the emulator's folder: the copy is read from the test folder, where the relative path would be wrong.
    /// </summary>
    private static string Absolute(string line, string emuFolder)
    {
        int eq = line.IndexOf('=');
        if (eq < 0 || line.TrimStart().StartsWith("#")) return line;
        string key = line[..eq].Trim();
        bool isPath = key == "g_cfg_rom_path" || key == "g_cfg_charrom_path" || System.Text.RegularExpressions.Regex.IsMatch(key, @"^s\d+d\d+$");
        string value = line[(eq + 1)..].Trim();
        if (!isPath || value.Length == 0 || value.StartsWith("#") || Path.IsPathRooted(value)) return line;
        try { return $"{key} = {Path.GetFullPath(Path.Combine(emuFolder, value))}"; } catch { return line; }
    }

    /// <summary>
    /// Starts the emulator: GS/OS's disk as the first hard disk (s7d1, it boots), the game's disk as the second (s7d2), the
    /// accelerator on. KEGS gets its own config.kegs in the test folder (a copy of the one beside it, if any, so its ROM and
    /// settings stay), started with -cfg.
    /// </summary>
    public static System.Diagnostics.Process Start(AppleIIgsEmulatorSettings s, string gameDisk, string folder)
    {
        string emuFolder = Path.GetDirectoryName(Path.GetFullPath(s.EmulatorPath))!;
        string cfg = Path.Combine(folder, "config.kegs");
        var lines = new List<string>();
        string own = Path.Combine(emuFolder, "config.kegs");
        if (File.Exists(own)) lines.AddRange(File.ReadAllLines(own).Select(l => Absolute(l, emuFolder)));
        else lines.Add("# KEGS configuration file version 1.07");
        void Set(string key, string value)
        {
            int at = lines.FindIndex(l => l.TrimStart().StartsWith(key + " ", StringComparison.Ordinal) || l.TrimStart().StartsWith(key + "=", StringComparison.Ordinal));
            string line = $"{key} = {value}";
            if (at >= 0) lines[at] = line; else lines.Add(line);
        }
        Set("s7d1", Path.GetFullPath(s.BootImagePath));
        Set("s7d2", Path.GetFullPath(gameDisk));
        Set("g_limit_speed", "3"); // 8.0MHz (Zip) - the ZipGS on
        Set("g_zip_speed_mhz", (s.AcceleratorMHz is 8 or 16 or 32 or 64 or 128 ? s.AcceleratorMHz : 8).ToString());
        if (File.Exists(s.RomPath)) Set("g_cfg_rom_path", Path.GetFullPath(s.RomPath));
        else if (!lines.Any(l => l.TrimStart().StartsWith("g_cfg_rom_path")))
            foreach (string rom in new[] { "ROM.03", "ROM.01", "ROM" })
                if (File.Exists(Path.Combine(emuFolder, rom))) { Set("g_cfg_rom_path", Path.Combine(emuFolder, rom)); break; }
        File.WriteAllLines(cfg, lines);
        var start = new System.Diagnostics.ProcessStartInfo(s.EmulatorPath) { WorkingDirectory = emuFolder, UseShellExecute = false };
        start.ArgumentList.Add("-cfg");
        start.ArgumentList.Add(cfg);
        return System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("it didn't start");
    }
}

/// <summary>Game > Apple IIgs Emulator...: the emulator, the GS/OS disk it starts from, the ROM, the accelerator's speed.</summary>
internal sealed class AppleIIgsEmulatorWindow : Window
{
    public AppleIIgsEmulatorWindow()
    {
        var s = AppleIIgsEmulatorSettings.Load();
        Title = "Apple IIgs Emulator";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var rows = new StackPanel { Margin = new Thickness(14) };
        rows.Children.Add(new TextBlock { Text = "Test the Engine runs your Ultima 6 Apple IIgs game in this emulator: GS/OS starts from its first hard disk, your game is the second. Open the game's disk on the desktop and double-click SANDCASTLE. The editor stays locked until you close the emulator.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        TextBox Row(string label, string value, string filter, string tip)
        {
            rows.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            var line = new DockPanel();
            var browse = new Button { Content = "Browse...", Width = 80, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right);
            var box = new TextBox { Text = value, ToolTip = tip };
            browse.Click += (_, _) => { var pick = new OpenFileDialog { Filter = filter }; if (pick.ShowDialog(this) == true) box.Text = pick.FileName; };
            line.Children.Add(browse);
            line.Children.Add(box);
            rows.Children.Add(line);
            return box;
        }
        var emulator = Row("Emulator (KEGS: kegswin.exe):", s.EmulatorPath, "Programs (*.exe)|*.exe|All files (*.*)|*.*", "The Apple IIgs emulator. It must emulate an accelerator - KEGS does (a ZipGS).");
        var boot = Row("GS/OS disk it starts from (first hard disk):", s.BootImagePath, "Disk images (*.2mg;*.hdv;*.po;*.img)|*.2mg;*.hdv;*.po;*.img|All files (*.*)|*.*", "A hard disk image with GS/OS 6.0.1 or later installed: it is the emulator's slot 7 drive 1, and it boots.");
        var rom = Row("Apple IIgs ROM (optional - ROM 01 or ROM 03):", s.RomPath, "ROM files (ROM*;*.rom)|ROM*;*.rom|All files (*.*)|*.*", "Empty: the emulator's own (KEGS looks for ROM.01 or ROM.03 beside it).");
        rows.Children.Add(new TextBlock { Text = "Accelerator speed:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        var speed = new ComboBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (int mhz in new[] { 8, 16, 32, 64, 128 }) speed.Items.Add(new ComboBoxItem { Content = $"{mhz} MHz", Tag = mhz, IsSelected = mhz == s.AcceleratorMHz });
        if (speed.SelectedIndex < 0) speed.SelectedIndex = 0;
        rows.Children.Add(speed);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        rows.Children.Add(status);
        void Recheck()
        {
            if (!File.Exists(emulator.Text)) { status.Text = ""; return; }
            var (ok, name) = AppleIIgsEmulatorSettings.Accelerator(emulator.Text);
            status.Text = ok ? $"{name}: it emulates an accelerator - the game can run in it." : $"{name}: no accelerator found in this emulator - the game can't run in it. Pick another (KEGS emulates a ZipGS).";
            status.Foreground = ok ? Brushes.DarkGreen : Brushes.DarkRed;
        }
        emulator.TextChanged += (_, _) => Recheck();
        Recheck();
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (emulator.Text.Length > 0 && File.Exists(emulator.Text) && !AppleIIgsEmulatorSettings.Accelerator(emulator.Text).Accelerated)
            {
                MessageBox.Show(this, "This emulator has no accelerator, and the game needs one. Pick an emulator that emulates a TransWarp GS or a ZipGS - KEGS does.", Title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            new AppleIIgsEmulatorSettings { EmulatorPath = emulator.Text.Trim(), BootImagePath = boot.Text.Trim(), RomPath = rom.Text.Trim(), AcceleratorMHz = (int)((ComboBoxItem)speed.SelectedItem).Tag }.Save();
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        Content = rows;
    }
}

/// <summary>Shown (modal: the editor is locked) while the emulator runs; it closes itself when the emulator is closed.</summary>
internal sealed class AppleIIgsRunningWindow : Window
{
    public AppleIIgsRunningWindow(System.Diagnostics.Process process, string emulatorName, string volumeName, string playerName)
    {
        Title = "Apple IIgs - testing";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var rows = new StackPanel { Margin = new Thickness(14) };
        rows.Children.Add(new TextBlock { Text = $"Your game is running in {emulatorName}.", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        rows.Children.Add(new TextBlock { Text = $"When GS/OS has started, double-click the disk \"{volumeName}\" on the desktop, then {playerName}.\n\nThe editor is locked until you close the emulator.", TextWrapping = TextWrapping.Wrap });
        var stop = new Button { Content = "Close the emulator", Width = 150, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        stop.Click += (_, _) => { try { if (!process.HasExited) process.Kill(); } catch { } };
        rows.Children.Add(stop);
        Content = rows;
        bool exited = false;
        Closing += (_, e) => { if (!exited) e.Cancel = true; }; // only the emulator closing ends it
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Dispatcher.Invoke(() => { exited = true; Close(); });
        if (process.HasExited) { exited = true; Loaded += (_, _) => Close(); }
    }
}
