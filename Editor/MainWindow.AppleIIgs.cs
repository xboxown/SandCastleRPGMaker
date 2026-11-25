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
    private MenuItem? _convertAudioMenuItem, _iigsCheckMenuItem, _iigsEmulatorMenuItem, _iigsPreviewMenuItem;
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
            _iigsPreviewMenuItem = new MenuItem { Header = "Apple IIgs Colour Preview...", ToolTip = "What the Apple IIgs will really show: every colour in 12 bits and the game's 16-colour palette, beside your 24-bit art" };
            _iigsPreviewMenuItem.Click += (_, _) => ShowAppleIIgsColourPreview();
            _iigsEmulatorMenuItem = new MenuItem { Header = "Apple IIgs Emulator...", ToolTip = "The emulator Test the Engine runs the game in (KEGS), the GS/OS disk it starts from, and the IIgs ROM" };
            _iigsEmulatorMenuItem.Click += (_, _) => new AppleIIgsEmulatorWindow { Owner = this }.ShowDialog();
            GameMenu.Items.Add(_iigsSeparator);
            GameMenu.Items.Add(_iigsCheckMenuItem);
            GameMenu.Items.Add(_iigsPreviewMenuItem);
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
        _convertAudioMenuItem.Visibility = _iigsCheckMenuItem!.Visibility = _iigsEmulatorMenuItem!.Visibility = _iigsPreviewMenuItem!.Visibility = _iigsSeparator!.Visibility = shown;
        if (_convertAudioToolButton is not null) _convertAudioToolButton.Visibility = shown;
        BuildGameMenuItem.ToolTip = iigs ? "Build the game as an Apple IIgs hard disk image (.2mg) with GS/OS's ProDOS inside" : "Build a standalone game: choose the platform and where";
        TestEngineMenuItem.Header = iigs ? "Test the Engine (Apple IIgs emulator)" : "Test the Engine";
        TestTitleScreenMenuItem.Visibility = iigs ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Game > Apple IIgs Colour Preview: the game made as Build makes it, its pictures shown both ways.</summary>
    private void ShowAppleIIgsColourPreview()
    {
        AppleIIgsGamePack pack;
        try { System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait; pack = MakeAppleIIgsPack(); }
        catch (Exception ex) { MessageBox.Show(this, "The game couldn't be made for the preview: " + ex.Message, "Apple IIgs Colour Preview", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        finally { System.Windows.Input.Mouse.OverrideCursor = null; }
        new AppleIIgsColourPreviewWindow(pack, IIgsColourNotes(pack)) { Owner = this }.Show();
    }

    /// <summary>The colour notes for a game made (its pictures against its 16 colours).</summary>
    private static List<string> IIgsColourNotes(AppleIIgsGamePack pack)
    {
        if (pack.Preview is not { } p) return new();
        var pics = p.Tiles.Select(t => (t, 16, 16)).Concat(p.Sprites).Concat(p.Avatar.Select(a => (a, 16, 16)));
        if (p.Hud is not null) pics = pics.Append((p.Hud, 320, 200));
        if (p.Title is not null) pics = pics.Append((p.Title, 320, 200));
        return SuperHiRes.ColourNotes(pack.Palette, pics);
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
        int SoundOk(int id) => _resourceDatabase.Entries.Any(r => r.Id == id && r.Kind is ResourceKind.Sound or ResourceKind.Music) ? 0 : -1;
        var gameMaps = maps.Take(AppleIIgsLimits.MaxMaps).ToList();
        int MapOk(int id) => gameMaps.FindIndex(m => m.Id == id);
        foreach (var who in _characterDatabase.Characters.Where(c => c.TalkCommands.Count > 0))
            foreach (var type in AppleIIgsTalk.Compile(who.TalkCommands, _ => 0, out _, SoundOk, MapOk).Unsupported) leftOut.Add(type);
        const string works = "Show Text with replies, Set / Conditional Branch on variables and switches, Labels, Play Sound, Change Map, Teleport (the Avatar), Join / Leave Party, Give Item (to the Avatar), Remove Item, Change HP, Change Stat (built-in stats), Wait, Place / Remove Character, Kill Character, Change Schedule, Set Time, Set Door State, Show / Erase Picture, Game Over, Quit Game, Save / Load Game, Shop, Change State, Modify Ailments, Change Food Level, Calculate, While, Repeat, Select Case, Break / Next Loop, Wait Until, Call Common Event, Advance IF (variables, items, stats, time), Show Bark, Play / Fade In / Stop Music, Set Volume, Resume Map Music, Change Weather, Drive Vehicle, Stop Driving, Set Vehicle Speed, Travel Vehicle, Rise A Level, Meditate At Shrine, Run Leveling Event, Scene Text and Scene Choice (in the text area, with {formulas}, {LevelWho}, {RestGuard}), Text Operation, Get Game Data, Swap Variables, Input Text, Input Number, every array and grid command, For Each (formulas with texts, arrays, grids and the game's functions) and Stop Conversation work";
        if (leftOut.Count > 0) problems.Add($"Note: in the characters' talk, these commands aren't on the Apple IIgs yet and are left out there: {string.Join(", ", leftOut)}. {works}.");
        // Map events: their pages run on the Apple IIgs by these triggers (Player/iigs/events.s); others are left out.
        var eventsLeftOut = new SortedSet<string>();
        var triggersLeftOut = new SortedSet<string>();
        var iigsTriggers = new HashSet<BookTriggerConditionBased> { BookTriggerConditionBased.ByEnter, BookTriggerConditionBased.ByStay, BookTriggerConditionBased.ByExit,
            BookTriggerConditionBased.Always, BookTriggerConditionBased.ByHavingAnItem, BookTriggerConditionBased.ByNotHavingAnItem, BookTriggerConditionBased.ByDropItem,
            BookTriggerConditionBased.ByPickupItem, BookTriggerConditionBased.ByUseItem };
        foreach (var m in gameMaps)
            foreach (var ev in m.Events)
                foreach (var page in ev.EventPages.Where(pg => pg.Commands.Count > 0))
                {
                    var t = page.Conditions.TriggerConditionBased;
                    if (!iigsTriggers.Contains(t) || (t == BookTriggerConditionBased.ByUseItem && page.Conditions.TriggerItemReference is not null)) { triggersLeftOut.Add(t.ToString()); continue; }
                    foreach (var type in AppleIIgsTalk.Compile(page.Commands, _ => 0, out _, SoundOk, MapOk).Unsupported) eventsLeftOut.Add(type);
                }
        if (eventsLeftOut.Count > 0) problems.Add($"Note: in the map events, these commands aren't on the Apple IIgs yet and are left out there: {string.Join(", ", eventsLeftOut)}. {works}.");
        if (triggersLeftOut.Count > 0) problems.Add($"Note: map event pages started by {string.Join(", ", triggersLeftOut)} don't run on the Apple IIgs yet (By Enter, Stay, Exit, Use (the action button), Always, Having / Not Having an Item, Drop and Pickup Item do).");
        // Spells: the effects the Apple IIgs does.
        var effectsLeftOut = new SortedSet<string>();
        foreach (var book in _objectDatabase.Objects.Where(o => o.Type == ObjectType.Spellbook))
            foreach (var sp in book.Spellbook.Spells)
                foreach (var e in sp.Effects.Where(e => !IIgsEffect(e.Kind)))
                    effectsLeftOut.Add(e.Kind.ToString());
        if (effectsLeftOut.Count > 0) problems.Add($"Note: these spell effects aren't on the Apple IIgs yet and do nothing there: {string.Join(", ", effectsLeftOut)} (every other effect works; traps aren't on the Apple IIgs, so Reveal / Destroy Traps do nothing there).");
        // Vehicles: their dashboards (paperdolls) aren't on the Apple IIgs.
        foreach (var h in ProjectSession.Houses.Where(h => h.Vehicle is { DashboardDollId: >= 0 }))
            problems.Add($"Note: vehicle \"{h.Name}\" has a dashboard paperdoll: the Apple IIgs has no paperdolls - use its controls or driver's seat (and the vehicle commands in events).");
        // The colours as the build reduces them: what is lost (the whole game made as Build makes it).
        try { problems.AddRange(IIgsColourNotes(MakeAppleIIgsPack())); }
        catch { }
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

    /// <summary>A character's picture in a pose, facing south, the first frame (null: it has none).</summary>
    private string? PoseFile(CharacterDefinition who, string poseName)
    {
        var pose = _poseDatabase.Poses.FirstOrDefault(p => p.Name == poseName && who.PoseSprites.ContainsKey(p.Id));
        if (pose is null) return null;
        var link = who.PoseSprites[pose.Id].Resolve(SpriteDirection.South);
        var sprite = link is null ? null : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(x => x.Name == link.SpriteName)
                                         ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(x => x.Name == link.SpriteName);
        return sprite?.Frames.FirstOrDefault()?.FilePath is { } f && File.Exists(f) ? f : null;
    }

    /// <summary>A character's body picture: Ultima 6's sleeping look (else its Body pose, Unconscious, Dead), facing south, the first frame.</summary>
    private string? BodyFile(CharacterDefinition who)
    {
        foreach (var name in new[] { "Sleeping", "Body", "Unconscious", "Dead" })
        {
            var pose = _poseDatabase.Poses.FirstOrDefault(p => p.Name == name && who.PoseSprites.ContainsKey(p.Id));
            if (pose is null) continue;
            var link = who.PoseSprites[pose.Id].Resolve(SpriteDirection.South);
            var sprite = link is null ? null : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(x => x.Name == link.SpriteName)
                                             ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(x => x.Name == link.SpriteName);
            if (sprite?.Frames.FirstOrDefault()?.FilePath is { } f && File.Exists(f)) return f;
        }
        return null;
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
    /// <summary>A point of a picture (a hand, a grip) as it lands in its fitted sprite, from the sprite's standing point (its bottom middle).</summary>
    private static (int X, int Y) FitPoint(string file, int px, int py)
    {
        var img = new BitmapImage();
        img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(file)); img.EndInit();
        int w = img.PixelWidth, h = img.PixelHeight;
        double scale = Math.Min(1.0, Math.Min((double)AppleIIgsLimits.MaxSpriteWidth / w, (double)AppleIIgsLimits.MaxSpriteHeight / h));
        int dw = Math.Max(1, (int)(w * scale)), dh = Math.Max(1, (int)(h * scale)), pw = (dw + 3) / 4 * 4, left = (pw - dw) / 2;
        return ((int)Math.Round(left + px * scale) - pw / 2, (int)Math.Round(py * scale) - dh);
    }

    /// <summary>A sprite fitted and mirrored left to right (a held weapon facing east / west).</summary>
    private static (uint[] Px, int W, int H) FitSpriteMirrored(string file)
    {
        var (px, w, h) = FitSprite(file);
        var m = new uint[px.Length];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = px[y * w + (w - 1 - x)];
        return (m, w, h);
    }

    /// <summary>A character's first frames for south, north, east and west (as LookFiles), for their hand points.</summary>
    private List<SpriteFrame?> LookFrames(CharacterDefinition who)
    {
        var list = new List<SpriteFrame?>();
        var pose = _poseDatabase.Poses.FirstOrDefault(p => p.Name == "Idle" && who.PoseSprites.ContainsKey(p.Id))
                   ?? _poseDatabase.Poses.FirstOrDefault(p => p.Name == "Walking" && who.PoseSprites.ContainsKey(p.Id));
        PoseAssignment? assignment = pose is not null ? who.PoseSprites[pose.Id] : who.PoseSprites.Values.FirstOrDefault();
        foreach (var facing in new[] { SpriteDirection.South, SpriteDirection.North, SpriteDirection.East, SpriteDirection.West })
        {
            var link = assignment?.Resolve(facing);
            var sprite = link is null ? null : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName)
                                             ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == link.SpriteName);
            list.Add(sprite?.Frames.FirstOrDefault());
        }
        return list;
    }

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

    /// <summary>A Book Editor frame's formatted words as plain text.</summary>
    private static string IIgsPlainText(string data)
    {
        if (data.Length == 0) return string.Empty;
        try
        {
            var box = new System.Windows.Controls.RichTextBox();
            RichTextXamlHelper.Load(box, data);
            return new System.Windows.Documents.TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Replace("\r\n", "\n").Trim();
        }
        catch { return System.Text.RegularExpressions.Regex.Replace(data, "<[^>]+>", string.Empty).Trim(); }
    }

    /// <summary>The spell effects the Apple IIgs player does (Player/iigs/magic.s, ailments.s).</summary>
    private static bool IIgsEffect(MagicEffectKind k) => k is not (MagicEffectKind.Custom or MagicEffectKind.Levitate or MagicEffectKind.Translate);

    private AppleIIgsGamePack MakeAppleIIgsPack()
    {
        var pack = new AppleIIgsGamePack { Title = _currentProjectName ?? "Game" };
        var soundIndex = new Dictionary<int, int>(); // Sound Editor id -> its number in GAME.DAT
        // The sounds first: the talks' Play Sound needs their numbers.
        // (filled in below, before the characters' talks are made)
        var first = _worlds.FirstOrDefault() ?? _map ?? throw new InvalidOperationException("There is no map.");
        // The maps (Maps tab, the first one first): map n of the game; Change Map goes by these numbers. Each a copy with its
        // houses and vehicles built in (as the Windows engines do as a game starts: HouseBuilder), the project's maps untouched.
        var worldList = (_worlds.Count > 0 ? _worlds : new List<MapData> { first }).Take(AppleIIgsLimits.MaxMaps)
            .Select(m =>
            {
                if (m.Houses.Count == 0 && m.SpawnPoints.Count == 0) return m;
                var copy = System.Text.Json.JsonSerializer.Deserialize<MapData>(System.Text.Json.JsonSerializer.Serialize(m))!;
                HouseBuilder.BuildInto(copy, ProjectSession.Houses);
                return copy;
            }).ToList();
        var start = worldList[0];
        int WorldIndex(int id) => worldList.FindIndex(m => m.Id == id);
        // Upper floors (Map Editor > floors): each its own map after the worlds; stairs and ladders take the Avatar between them.
        var floorMap = new Dictionary<(int World, int Floor), int>();
        for (int wi = 0; wi < worldList.Count; wi++)
        {
            int top = Math.Max(worldList[wi].Layers.Select(l => l.Floor).DefaultIfEmpty(1).Max(), worldList[wi].Characters.Select(c => c.Floor).DefaultIfEmpty(1).Max());
            for (int f = 2; f <= Math.Min(top, MapFloors.MaxFloors); f++)
                if (worldList.Count + floorMap.Count < AppleIIgsLimits.MaxMaps) floorMap[(wi, f)] = worldList.Count + floorMap.Count;
        }
        int MapOfFloor(MapData world, int floor) => floor <= 1 ? worldList.IndexOf(world) : floorMap.TryGetValue((worldList.IndexOf(world), floor), out int m) ? m : -1;
        // Stairs: stepped on, to the floor above at the cell past the stairs; the floor above's cell over the stairs: down, before them.
        // Ladders: to their destination (its floor by its lift).
        List<(int X, int Y, int Map, int TX, int TY)> FloorLinks(MapData world, int floor)
        {
            var links = new List<(int, int, int, int, int)>();
            foreach (var inst in world.Objects.Where(o => o.ContainerInstanceId < 0))
            {
                var def = _objectDatabase.Objects.FirstOrDefault(o => o.Id == inst.ObjectId);
                if (def is null || def.Type != ObjectType.Stairs) continue;
                int of = MapFloors.FloorOfLift(inst.Z);
                if (def.Stairs.Kind == StairsKind.Ladder)
                {
                    if (of == floor && inst.DestinationX >= 0 && inst.DestinationY >= 0 && MapOfFloor(world, MapFloors.FloorOfLift(inst.DestinationLift)) is int lm and >= 0)
                        links.Add((inst.X, inst.Y, lm, inst.DestinationX, inst.DestinationY));
                    continue;
                }
                var (dx, dy) = def.Stairs.RisesToward switch { SpriteDirection.North => (0, -1), SpriteDirection.South => (0, 1), SpriteDirection.East => (1, 0), _ => (-1, 0) };
                if (of == floor && MapOfFloor(world, floor + 1) is int up and >= 0) links.Add((inst.X, inst.Y, up, inst.X + dx, inst.Y + dy));
                if (of == floor - 1 && MapOfFloor(world, floor - 1) is int down and >= 0) links.Add((inst.X, inst.Y, down, inst.X - dx, inst.Y - dy));
            }
            return links;
        }
        HashSet<(int, int)> StairTops(MapData world, int floor) => FloorLinks(world, floor).Where(l => l.Map == MapOfFloor(world, floor - 1)).Select(l => (l.X, l.Y)).ToHashSet();

        // Every different cell picture, composed from its layers.
        var keys = new Dictionary<string, int>();
        var tilePixels = new List<uint[]>();
        var flags = new List<byte>();
        var tileNames = new List<string>();
        // A map's cells: each different cell picture once, composed from its layers (shared by all the maps).
        ushort[] Grid(MapData world, int floor = 1, HashSet<(int, int)>? stairTops = null)
        {
            var worldLayers = world.Layers.Where(l => l.Floor <= floor).ToList();
            var slabs = world.Layers.Where(l => l.Floor == floor).ToList();
            var grid = new ushort[world.Width * world.Height];
            for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    string key = CellKey(world, worldLayers, x, y);
                    if (floor > 1 && !slabs.Any(l => l.GetTile(x, y, world.Width) >= 0))
                        key += stairTops?.Contains((x, y)) == true ? "|#stair" : "|#air"; // an upper floor: over nothing, it can't be walked (but down the stairs)
                    if (!keys.TryGetValue(key, out int n))
                    {
                        if (keys.Count >= AppleIIgsLimits.MaxTiles) throw new InvalidOperationException($"More than {AppleIIgsLimits.MaxTiles} different tiles.");
                        var px = new uint[256];
                        bool walkable = true, water = false;
                        var animParts = new List<(int Index, string Sprite)>();
                        int partIndex = 0;
                        foreach (var part in key.Length == 0 ? Array.Empty<string>() : key.Split('|'))
                        {
                            if (part == "#air") { walkable = false; continue; }
                            if (part.StartsWith('#')) continue;
                            partIndex++;
                            int colon = part.LastIndexOf(':');
                            string sheet = part[..colon];
                            int local = int.Parse(part[(colon + 1)..]);
                            if (TilePixels(sheet, local) is { } top)
                                for (int i = 0; i < 256; i++) if ((top[i] >> 24) >= 128) px[i] = top[i];
                            if (_tilePropertiesDatabase.ByTileKey.TryGetValue($"{sheet}:{local}", out var props))
                            {
                                walkable &= props.Walkable; water |= props.IsWater;
                                if (props.AnimationSpriteName.Length > 0) animParts.Add((partIndex, props.AnimationSpriteName));
                            }
                        }
                        n = keys[key] = tilePixels.Count;
                        tilePixels.Add(px);
                        // An animated tile: its frames composed the same way, the animated part's frame over the parts under it.
                        if (animParts.Count > 0 && _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(sp => sp.Name == animParts[0].Sprite) is { } anim && anim.Frames.Count > 1)
                        {
                            var frameTiles = new List<int> { n };
                            foreach (var frame in anim.Frames.Skip(1).Take(3))
                            {
                                if (!File.Exists(frame.FilePath)) continue;
                                var fpx = new uint[256];
                                int pi = 0;
                                foreach (var part in key.Split('|'))
                                {
                                    if (part.StartsWith('#')) continue;
                                    pi++;
                                    int colon = part.LastIndexOf(':');
                                    var top = pi == animParts[0].Index ? FitInto16(frame.FilePath) : TilePixels(part[..colon], int.Parse(part[(colon + 1)..]));
                                    if (top is not null) for (int i = 0; i < 256; i++) if ((top[i] >> 24) >= 128) fpx[i] = top[i];
                                }
                                frameTiles.Add(tilePixels.Count);
                                tilePixels.Add(fpx);
                                tileNames.Add("");
                                flags.Add((byte)((walkable ? 1 : 0) | (water ? 2 : 0)));
                            }
                            pack.AnimTiles.Add(frameTiles);
                        }
                        string tileName = "";
                        foreach (var part in key.Length == 0 ? Array.Empty<string>() : key.Split('|'))
                        {
                            if (part.StartsWith('#')) continue;
                            int colon = part.LastIndexOf(':');
                            if (_tilePropertiesDatabase.ByTileKey.TryGetValue($"{part[..colon]}:{part[(colon + 1)..]}", out var named) && named.TerrainLabel.Length > 0) tileName = named.TerrainLabel;
                        }
                        tileNames.Insert(n, tileName);
                        flags.Insert(n, (byte)((walkable ? 1 : 0) | (water ? 2 : 0)));
                    }
                    grid[y * world.Width + x] = (ushort)n;
                }
            // Line of sight (U6GameWindow.U6Sight.cs): walls and what Occludes stop sight; walls, doors and windows always show.
            foreach (var inst in world.Objects.Where(o => o.ContainerInstanceId < 0 && MapFloors.FloorOfLift(o.Z) == floor))
            {
                if (inst.X < 0 || inst.Y < 0 || inst.X >= world.Width || inst.Y >= world.Height) continue;
                if (_objectDatabase.Objects.FirstOrDefault(o => o.Id == inst.ObjectId) is not { } sd) continue;
                int at = inst.Y * world.Width + inst.X;
                bool wallish = sd.Type is ObjectType.Wall or ObjectType.Door or ObjectType.Window;
                bool blocks = sd.Type == ObjectType.Wall || (sd.Occludes && sd.Type is not (ObjectType.Window or ObjectType.Door));
                if (wallish) grid[at] |= 0x4000;
                if (blocks) grid[at] |= 0x8000;
            }
            return grid;
        }
        var map = Grid(start);
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
        foreach (var r in _resourceDatabase.Entries.Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music).Take(AppleIIgsLimits.MaxSounds))
        {
            if (!IIgsSoundOk(r).Ok && !File.Exists(r.FilePath)) continue;
            try
            {
                using var reader = new NAudio.Wave.WaveFileReader(r.FilePath);
                var bytes = new byte[Math.Min(reader.Length, AppleIIgsLimits.MaxSoundBytes)];
                int read = reader.Read(bytes, 0, bytes.Length);
                soundIndex[r.Id] = pack.Sounds.Count;
                pack.Sounds.Add((r.Name, reader.WaveFormat.SampleRate, AppleIIgsGamePack.DocSafe(bytes[..read])));
                pack.SoundVolumes.Add((int)Math.Clamp(r.Volume * 255, 0, 255));
            }
            catch { /* the check already said so */ }
        }
        // The Disk Swap screen (HUD Editor): its picture, its animation's frames, its words and sound.
        var swap = _systemSettings.Ultima6.Hud.DiskSwap;
        pack.SwapMessage = swap.Message;
        pack.SwapAnimation = (swap.AnimationX, swap.AnimationY);
        if (soundIndex.TryGetValue(swap.SoundId, out int swapSound)) pack.SwapSound = swapSound;
        var objects = new List<(int X, int Y, int Sprite, int Flags, int Name, int Other, int Icon, int In)>();
        // The inventory's cells (HUD Editor > Carried Items): every object gets its look fitted into one, once.
        var carriedPiece = _systemSettings.Ultima6.Hud.First(HudPieceKind.CarriedItems);
        int cell = carriedPiece is null ? 0 : Math.Clamp(carriedPiece.CellSize / 4 * 4, 4, 32);
        // Weapons and armour: what they do worn (Engine/AppleIIgs.cs GearCode), in the object's Other.
        int GearOf(WorldObject? def)
        {
            if (def is null) return 0xFFFF;
            if (def.Type == ObjectType.Weapon)
                return AppleIIgsGamePack.GearCode(true, (int)(def.Weapon.EquipSlot ?? EquipmentSlot.RightHand) + 1,
                    def.Weapon.Use == WeaponUse.Melee ? 1 : Math.Max(2, def.Weapon.RangeTiles / 2), def.Weapon.Damage);
            if (def.Type == ObjectType.Armor && def.Armor.EquipSlot is { } slot)
                return AppleIIgsGamePack.GearCode(false, (int)slot + 1, 1, def.Armor.ArmorPoints);
            if (def.Type == ObjectType.Spellbook && !def.Spellbook.U8Focus) return 0xC000 | (((int)EquipmentSlot.RightHand + 1) << 10) | BookOf(def);
            return 0xFFFF;
        }
        // Books and signs (Book Editor): their words read in the text area (Ultima 6's notepad text, else the pages' frames).
        var textOf = new Dictionary<int, int>();
        var textArea = _systemSettings.Ultima6.Hud.First(HudPieceKind.TextArea);
        int TextOf(int bookId)
        {
            if (textOf.TryGetValue(bookId, out int known)) return known;
            var book = _bookDatabase.Books.FirstOrDefault(b => b.Id == bookId);
            string text = book is null ? "" : book.U6Text.Trim().Length > 0 ? book.U6Text
                : string.Join("\n", book.Pages.SelectMany(pg => book.Frames.Select(f => pg.FrameTexts.TryGetValue(f.Id, out var x) ? IIgsPlainText(x) : "")).Where(x => x.Length > 0));
            int chars = Math.Max(10, (textArea?.Width ?? 300) / 6), lines = Math.Max(2, (textArea?.Height ?? 70) / 10 - 1);
            textOf[bookId] = pack.Texts.Count;
            pack.Texts.Add(AppleIIgsTalk.CompileText(text, chars, lines));
            return pack.Texts.Count - 1;
        }
        // A light source's Other: $F000, lit (bit 8), how far it lights (1-7, from its Brightness: a candle 1, a lamp 7).
        // Food: $C400 + how much it feeds (Object Editor > Food: Nutrition).
        int FoodCode(WorldObject def) => 0xC400 | Math.Clamp(def.Food.Nutrition, 1, 255);
        // Plants, tools, crafting stations, musical instruments: Other $D000 | their record's number (made at the end).
        var specialOf = new Dictionary<int, int>();
        var specialDefs = new List<WorldObject>();
        int? SpecialOf(WorldObject def)
        {
            if (def.Type is not (ObjectType.Plant or ObjectType.Tool or ObjectType.CraftingStation or ObjectType.MusicalInstrument or ObjectType.UseableObject or ObjectType.Potion)) return null;
            if (!specialOf.TryGetValue(def.Id, out int i)) { if (specialDefs.Count >= 256) return null; specialOf[def.Id] = i = specialDefs.Count; specialDefs.Add(def); }
            if (def.Type == ObjectType.UseableObject) ItemSpell(def, def.Useable.Effects, def.Useable.AsksForTarget);
            if (def.Type == ObjectType.Potion) ItemSpell(def, def.Potion.Effects, true);
            return 0xD000 | i;
        }
        // Ammo: $C800 + its family's number (arrows, bolts...).
        var families = new List<string>();
        int FamilyOf(string family) { int i = families.FindIndex(f => string.Equals(f, family, StringComparison.OrdinalIgnoreCase)); if (i < 0) { families.Add(family); i = families.Count - 1; } return Math.Min(i, 1023); }
        int AmmoCode(WorldObject def) => 0xC800 | FamilyOf(def.Ammo.Family);
        int LightCode(WorldObject def, bool lit) => 0xF000 | (lit ? 0x100 : 0) | Math.Clamp(1 + def.Light.Brightness / 2, 1, 7);
        var pendingThings = new List<(AppleIIgsGamePack.Spell Spell, int Index, MagicEffect Effect)>();
        // A wand's, a scroll's or a potion's effects: a spell of its own (no words, no circle, no mana).
        var itemSpell = new Dictionary<int, int>();
        int ItemSpell(WorldObject def, List<MagicEffect> effects, bool target)
        {
            if (itemSpell.TryGetValue(def.Id, out int known)) return known;
            var spell = new AppleIIgsGamePack.Spell { Name = pack.NameOf(def.Name), Words = pack.NameOf(def.Name), Circle = 0, Mana = 0, Target = target, Radius = 0, Letters = "" };
            foreach (var e in effects.Where(e => IIgsEffect(e.Kind)).Take(2))
            {
                pendingThings.Add((spell, spell.Effects.Count, e));
                spell.Effects.Add(((int)e.Kind, Math.Max(0, e.Min), Math.Max(e.Min, e.Max), 0xFFFF, (int)Math.Clamp(e.Seconds, 0, 3000)));
            }
            itemSpell[def.Id] = pack.Spells.Count;
            pack.Spells.Add(spell);
            return pack.Spells.Count - 1;
        }
        // Wands, scrolls and potions: known (their spells made) before the spells are finished.
        foreach (var od in _objectDatabase.Objects.Where(o => o.Type is ObjectType.UseableObject or ObjectType.Potion)) SpecialOf(od);
        // Spellbooks: each book's spells into the game's spell list (Engine/AppleIIgs.cs Spells / Books).
        var bookIndex = new Dictionary<int, int>();
        var spellIndex = new Dictionary<SpellDefinition, int>();
        int BookOf(WorldObject book)
        {
            if (bookIndex.TryGetValue(book.Id, out int known)) return known;
            var list = new List<int>();
            foreach (var sp in book.Spellbook.Spells)
            {
                if (!spellIndex.TryGetValue(sp, out int si))
                {
                    var spell = new AppleIIgsGamePack.Spell
                    {
                        Name = pack.NameOf(sp.Name), Words = pack.NameOf(sp.Words.Length > 0 ? sp.Words : sp.Name), Circle = Math.Max(1, sp.Circle), Mana = sp.EffectiveManaCost,
                        Target = sp.NeedsTarget, Radius = Math.Max(0, sp.RadiusTiles / 2),
                        Letters = string.Concat(sp.Words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])))
                    };
                    foreach (var r in sp.AllReagents().Take(4))
                        if (_objectDatabase.Objects.FirstOrDefault(o => o.Id == r.ObjectId) is { } rd) spell.Reagents.Add((pack.NameOf(rd.Name), Math.Max(1, r.Quantity)));
                    foreach (var e in sp.Effects.Where(e => IIgsEffect(e.Kind)).Take(2))
                    {
                        pendingThings.Add((spell, spell.Effects.Count, e)); // its thing: filled in at the end (SpellThing)
                        spell.Effects.Add(((int)e.Kind, Math.Max(0, e.Min), Math.Max(e.Min, e.Max), 0xFFFF, (int)Math.Clamp(e.Seconds, 0, 3000)));
                    }
                    si = spellIndex[sp] = pack.Spells.Count;
                    pack.Spells.Add(spell);
                }
                list.Add(si);
            }
            bookIndex[book.Id] = pack.Books.Count;
            pack.Books.Add(list);
            return pack.Books.Count - 1;
        }
        var iconOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int IconOf(string? file)
        {
            if (cell == 0 || string.IsNullOrEmpty(file) || !File.Exists(file)) return 0xFFFF;
            if (iconOf.TryGetValue(file, out int known)) return known;
            spritePixels.Add((FitPicture(file, cell, cell), cell, cell));
            return iconOf[file] = spritePixels.Count - 1;
        }
        // Containers (chests, bags, barrels...) and what is in them - off the map, inside (256), until Use spills them out
        // (Ultima 6's Search) or a bag is opened in the inventory. Their contents as the Game Test seeds them: the map's own, else the Starting Contents.
        void AddContents(IEnumerable<InventoryItemInstance> things, int container, int depth)
        {
            if (depth > 4) return;
            foreach (var thing in things)
            {
                if (thing.Category is not (ReferencedItemCategory.Object or ReferencedItemCategory.Container)) continue;
                var look = DollLook.ItemSprite(thing.Category, thing.ItemId, _objectDatabase, _bookDatabase, _containerDatabase, _spriteDatabase);
                string? file = look?.Frames.FirstOrDefault()?.FilePath;
                if (thing.Category == ReferencedItemCategory.Object && file is null) file = _objectDatabase.Objects.FirstOrDefault(o => o.Id == thing.ItemId)?.IconPath;
                int n = SpriteOf(file);
                if (n < 0) continue;
                string name = DollLook.ItemName(new ItemReference { Category = thing.Category, ItemId = thing.ItemId }, _objectDatabase, _bookDatabase, _containerDatabase);
                int f = 256 | 2;
                if (thing.Category == ReferencedItemCategory.Container && _containerDatabase.Containers.FirstOrDefault(c => c.Id == thing.ItemId) is { } bag)
                    f |= 128 | (bag.IsSolid ? 1 : 0) | ((thing.IsLockedRuntime ?? bag.IsLocked) ? 8 : 0);
                else if (_objectDatabase.Objects.FirstOrDefault(o => o.Id == thing.ItemId) is { } od)
                {
                    if (od.IsSolid) f |= 1;
                    if (od.Type == ObjectType.Key) f |= 512;
                    if (od.Type == ObjectType.Lockpick) f |= 1024;
                }
                bool isKey = thing.Category == ReferencedItemCategory.Object && _objectDatabase.Objects.FirstOrDefault(o => o.Id == thing.ItemId)?.Type == ObjectType.Key;
                var thingDef = thing.Category == ReferencedItemCategory.Object ? _objectDatabase.Objects.FirstOrDefault(o => o.Id == thing.ItemId) : null;
                objects.Add((0xFFFF, 0xFFFF, n, f, pack.NameOf(name), isKey ? thing.ItemId : thingDef?.Type is ObjectType.Weapon or ObjectType.Armor or ObjectType.Spellbook ? GearOf(thingDef) : thingDef?.Type == ObjectType.LightSource ? LightCode(thingDef, false) : thingDef?.Type == ObjectType.Food ? FoodCode(thingDef) : thingDef?.Type == ObjectType.Ammo ? AmmoCode(thingDef) : 0xFFFF, IconOf(file), container));
                if (thing.Category == ReferencedItemCategory.Container) AddContents(thing.Contents, objects.Count - 1, depth + 1);
            }
        }
        // A map's objects and containers (on the map: their flags' top 4 bits are its number).
        var vehicleIndex = new Dictionary<(int Map, int State), int>();
        void VehiclePiece(MapData world, ObjectInstance instance, int objIndex, int mapNo)
        {
            if (world.VehicleStates.FirstOrDefault(v => v.Id == instance.VehicleStateId) is not { } vs) return;
            if (ProjectSession.Houses.FirstOrDefault(h => h.Id == vs.TemplateId) is not { Vehicle: { } settings }) return;
            if (!vehicleIndex.TryGetValue((mapNo, vs.Id), out int vi))
            {
                int ux = Math.Max(1, world.Tileset.TileWidth / 8), uy = Math.Max(1, world.Tileset.TileHeight / 8);
                int Snd(int id) => id >= 0 && soundIndex.TryGetValue(id, out int sn) ? sn : 0xFFFF;
                string deedName = settings.DeedObjectId >= 0 ? _objectDatabase.Objects.FirstOrDefault(o => o.Id == settings.DeedObjectId)?.Name ?? "" : "";
                vi = vehicleIndex[(mapNo, vs.Id)] = pack.Vehicles.Count;
                pack.Vehicles.Add((mapNo, vs.TemplateId, vs.Left / ux, vs.Top / uy, Math.Max(1, vs.Width / ux), Math.Max(1, vs.Depth / uy),
                    settings.Movement switch { MovementKind.Sea => 1, MovementKind.Air => 2, _ => 0 },
                    (int)Math.Clamp(Math.Round(60 / Math.Max(0.1, settings.CellsPerSecond)), 1, 600),
                    deedName.Length > 0 ? pack.NameOf(deedName) : 0xFFFF, settings.NeedDeedText, Snd(settings.StartSoundResourceId), Snd(settings.MovingSoundResourceId), Snd(settings.StopSoundResourceId)));
            }
            var def = _objectDatabase.Objects.FirstOrDefault(o => o.Id == instance.ObjectId);
            if (def is null) return;
            var looks = new int[4];
            for (int q = 0; q < 4; q++)
            {
                var state = ObjectLooks.StartState(def, instance) with { Facing = (SpriteDirection)(((int)instance.Facing + q * 2) % 8) };
                string? f = ObjectLooks.SpriteFor(def, state, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath;
                looks[q] = SpriteOf(f) is int sn2 and >= 0 ? sn2 : objects[objIndex].Sprite;
            }
            int moving = 0xFFFF;
            if (def.VehicleRole == VehiclePartRole.Wheel && def.MovingLook.IsSet && ObjectLooks.Find(_spriteDatabase, def.MovingLook.SpriteGroup, def.MovingLook.SpriteName)?.Frames.FirstOrDefault()?.FilePath is { } mf)
                moving = SpriteOf(mf) is int ms and >= 0 ? ms : 0xFFFF;
            int role = def.VehicleRole switch { VehiclePartRole.DriverSeat => 1, VehiclePartRole.Controls => 2, VehiclePartRole.Wheel => 3, _ => 0 };
            pack.VehicleParts.Add((vi, objIndex, role, looks, moving));
        }
        // Levers, switches, buttons: their links (Map Editor > Link) become objects' numbers once every map's objects are in (lever.s).
        var instanceAt = new Dictionary<(MapData, int), int>();
        var pendingLevers = new List<(MapData World, ObjectInstance Instance, WorldObject Def, int At, int Other, bool On)>();
        void AddMapObjects(MapData world, int bits, int floor = 1)
        {
            foreach (var instance in world.Objects.Where(o => o.ContainerInstanceId < 0))
            {
              int objAt = objects.Count;
              try
              {
                var def = _objectDatabase.Objects.FirstOrDefault(o => o.Id == instance.ObjectId);
                if (def is null) continue;
                if (def.Type == ObjectType.Roof ? floor != 1 : MapFloors.FloorOfLift(instance.Z) != floor) continue; // this floor's things; roofs over the ground floor
                var state = new ObjectState(instance.IsOpenRuntime ?? false, instance.IsLockedRuntime ?? false, instance.IsLitRuntime ?? false, instance.IsSpentRuntime,
                    Math.Max(1, instance.Quantity), instance.Facing, Stage: instance.FrameRuntime, LiquidId: instance.LiquidIdRuntime ?? -1);
                var sprite = ObjectLooks.SpriteFor(def, state, _spriteDatabase);
                string? file = sprite?.Frames.FirstOrDefault()?.FilePath ?? (string.IsNullOrEmpty(def.IconPath) ? null : def.IconPath);
                int n = SpriteOf(file);
                if (n < 0 || instance.X < 0 || instance.Y < 0 || instance.X >= world.Width || instance.Y >= world.Height) continue;
                int objFlags = (def.IsSolid ? 1 : 0) | (def.IsPortable && def.Type != ObjectType.Door ? 2 : 0), other = 0xFFFF;
                if (def.Type == ObjectType.Door)
                {
                    // A door: its other look (open while closed, closed while open); closed it stops walkers, open it doesn't.
                    bool open = instance.IsOpenRuntime ?? def.Door.StartsOpen, locked = instance.IsLockedRuntime ?? def.Door.StartsLocked;
                    var otherState = state with { Open = !open, Locked = false };
                    string? otherFile = ObjectLooks.SpriteFor(def, otherState, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath;
                    other = SpriteOf(otherFile) is int o and >= 0 ? o : n;
                    objFlags = 4 | (open ? 16 : 1) | (locked ? 8 : 0) | (def.Door.MagicallyLocked ? 2048 : 0);
                    objects.Add((instance.X, instance.Y, n, objFlags | bits, pack.NameOf(def.Name), other, Math.Clamp(def.Door.PickLockDifficulty, 0, 255), def.Door.RequiredKeyItemId >= 0 ? def.Door.RequiredKeyItemId : 0xFFFF));
                    continue;
                }
                if (def.Type == ObjectType.Key) { objFlags |= 512; other = def.Id; }
                if (def.Type == ObjectType.Lockpick) objFlags |= 1024;
                if (def.Type is ObjectType.Weapon or ObjectType.Armor or ObjectType.Spellbook) other = GearOf(def);
                if (def.Type == ObjectType.Roof) { objects.Add((instance.X, instance.Y, n, bits, pack.NameOf(def.Name), 0xFFFD, 0xFFFF, -1)); continue; } // a roof: hidden while the Avatar is under it
                if (def.Type == ObjectType.LightSource) other = LightCode(def, instance.IsLitRuntime ?? def.Light.StartsLit);
                if (def.Type == ObjectType.Food && !def.Food.IsLiquid) other = FoodCode(def);
                if (def.Type == ObjectType.Ammo) other = AmmoCode(def);
                if (def.Type == ObjectType.Money) { objects.Add((instance.X, instance.Y, n, 2 | bits, pack.NameOf(def.Name), 0xFFFE, Math.Clamp(instance.Quantity, 1, 30000), -1)); continue; }
                if (SpecialOf(def) is int spc)
                {
                    other = spc;
                    if (def.Type == ObjectType.Plant) { objects.Add((instance.X, instance.Y, n, objFlags | bits, pack.NameOf(def.Name), other, Math.Clamp(instance.FrameRuntime, 0, 3), -1)); continue; }
                }
                if (def.Type == ObjectType.Lever)
                {
                    // A lever, switch or button: its off look (its own) and on look (Lever > On sprite) - the one not shown kept in its record.
                    bool on = instance.IsOpenRuntime ?? def.Lever.StartsOn;
                    int offLook = SpriteOf(ObjectLooks.SpriteFor(def, state with { Open = false }, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath) is int ol and >= 0 ? ol : n;
                    int onLook = def.Lever.OnSprite.IsSet && ObjectLooks.Find(_spriteDatabase, def.Lever.OnSprite.SpriteGroup, def.Lever.OnSprite.SpriteName)?.Frames.FirstOrDefault()?.FilePath is { } onFile && SpriteOf(onFile) is int nl and >= 0 ? nl : offLook;
                    pendingLevers.Add((world, instance, def, objects.Count, on ? offLook : onLook, on));
                    objects.Add((instance.X, instance.Y, on ? onLook : offLook, objFlags | bits, pack.NameOf(def.Name), 0xFFFB, IconOf(file), -1));
                    continue;
                }
                if (def.Type == ObjectType.Trap)
                {
                    // A trap: its facing in the icon's place (a shooting trap fires that way - floor.s).
                    int facingCode = instance.Facing switch { SpriteDirection.North => 1, SpriteDirection.East => 2, SpriteDirection.West => 3, _ => 0 };
                    objects.Add((instance.X, instance.Y, n, objFlags | bits, pack.NameOf(def.Name), other, facingCode, -1));
                    continue;
                }
                objects.Add((instance.X, instance.Y, n, objFlags | bits, pack.NameOf(def.Name), other, IconOf(file), -1));
              }
              finally
              {
                if (objects.Count > objAt && instance.InstanceId > 0) instanceAt[(world, instance.InstanceId)] = objAt;
                // A piece of a vehicle: the vehicle (its footprint, settings), the piece's role and its turned looks.
                if (instance.VehicleStateId != 0 && objects.Count > objAt) VehiclePiece(world, instance, objAt, bits >> 12);
              }
            }
            foreach (var placed in world.Items.Where(i => i.Category == ReferencedItemCategory.Book && MapFloors.FloorOfLift(i.Z) == floor))
            {
                if (placed.X < 0 || placed.Y < 0 || placed.X >= world.Width || placed.Y >= world.Height) continue;
                string? bookFile = DollLook.ItemSprite(ReferencedItemCategory.Book, placed.ItemId, _objectDatabase, _bookDatabase, _containerDatabase, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath;
                int bn = SpriteOf(bookFile);
                if (bn < 0) continue;
                var bookDef = _bookDatabase.Books.FirstOrDefault(b => b.Id == placed.ItemId);
                objects.Add((placed.X, placed.Y, bn, ((bookDef?.CanBePicked ?? true) ? 2 : 0) | ((bookDef?.IsSolid ?? false) ? 1 : 0) | bits,
                    pack.NameOf(bookDef?.Name ?? "book"), 0xC000 | Math.Min(TextOf(placed.ItemId), 1023), IconOf(bookFile), -1));
            }
            foreach (var placed in world.Items.Where(i => i.Category == ReferencedItemCategory.Container && MapFloors.FloorOfLift(i.Z) == floor))
            {
                if (_containerDatabase.Containers.FirstOrDefault(c => c.Id == placed.ItemId) is not { } data) continue;
                if (placed.X < 0 || placed.Y < 0 || placed.X >= world.Width || placed.Y >= world.Height) continue;
                int n = SpriteOf(DollLook.ItemSprite(ReferencedItemCategory.Container, data.Id, _objectDatabase, _bookDatabase, _containerDatabase, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath);
                if (n < 0) continue;
                int f = 128 | (data.IsSolid ? 1 : 0) | (data.CanBePicked && !data.IsBodyContainer ? 2 : 0) | ((placed.IsLockedRuntime ?? data.IsLocked) ? 8 : 0);
                int keyCode = data.RequiredKeyCategory == ReferencedItemCategory.Object && data.RequiredKeyItemId >= 0 ? data.RequiredKeyItemId : 0xFFFF;
                objects.Add((placed.X, placed.Y, n, f | bits, pack.NameOf(data.Name), keyCode, IconOf(DollLook.ItemSprite(ReferencedItemCategory.Container, data.Id, _objectDatabase, _bookDatabase, _containerDatabase, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath), -1));
                var contents = placed.InstanceId != 0 ? placed.Contents : placed.OwnContents ?? data.StartingContents;
                AddContents(contents, objects.Count - 1, 0);
            }
        }
        AddMapObjects(start, 0);
        // What the Avatar carries (not what it wears - that is in the body slots): objects carried, off the map.
        int avatarGold = 0;
        // Currencies (money.s): the main one (the first Money object's) is the Avatar's gold, the others purses 1-7.
        var moneyDefs = _objectDatabase.Objects.Where(o => o.Type == ObjectType.Money).ToList();
        int mainCurrency = moneyDefs.FirstOrDefault()?.Money.CurrencyId ?? -1;
        var purseOf = new Dictionary<int, int> { [mainCurrency] = 0 };
        foreach (var cur in ProjectSession.Currencies.Currencies)
            if (!purseOf.ContainsKey(cur.Id) && purseOf.Count < 8 && moneyDefs.Any(o => o.Money.CurrencyId == cur.Id)) purseOf[cur.Id] = purseOf.Count;
        foreach (var md in moneyDefs)
            if (purseOf.TryGetValue(md.Money.CurrencyId, out int pi) && pack.MoneyKinds.All(k => k.Name != pack.NameOf(md.Name))) pack.MoneyKinds.Add((pack.NameOf(md.Name), pi));
        // What someone carries and wears (owner: -1 the Avatar, else $8000 + map x 64 + place - a character who may join the party).
        void AddCarried(CharacterDefinition avatarWho, int owner)
        {
            foreach (var item in avatarWho.Inventory.Where(i => i.Category is ReferencedItemCategory.Object or ReferencedItemCategory.Container))
            {
                if (item.Category == ReferencedItemCategory.Container)
                {
                    // a bag carried, with what is in it
                    if (_containerDatabase.Containers.FirstOrDefault(c => c.Id == item.ItemId) is not { } bag) continue;
                    string? bagFile = DollLook.ItemSprite(ReferencedItemCategory.Container, bag.Id, _objectDatabase, _bookDatabase, _containerDatabase, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath;
                    int b = SpriteOf(bagFile);
                    if (b < 0) continue;
                    objects.Add((0xFFFF, 0xFFFF, b, 64 | 128 | 2 | (bag.IsSolid ? 1 : 0) | ((item.IsLockedRuntime ?? bag.IsLocked) ? 8 : 0), pack.NameOf(bag.Name), 0xFFFF, IconOf(bagFile), owner));
                    AddContents(item.Contents.Count > 0 ? item.Contents : bag.StartingContents, objects.Count - 1, 0);
                    continue;
                }
                var def = _objectDatabase.Objects.FirstOrDefault(o => o.Id == item.ItemId);
                if (def is null) continue;
                if (def.Type == ObjectType.Money)
                {
                    if (owner < 0)
                    {
                        if (purseOf.TryGetValue(def.Money.CurrencyId, out int pp) && pp > 0) pack.StartPurses[pp] += Math.Max(1, item.Quantity);
                        else avatarGold += Math.Max(1, item.Quantity);
                    }
                    continue;
                }
                var look = ObjectLooks.SpriteFor(def, new ObjectState(false, false, false, false, Math.Max(1, item.Quantity), SpriteDirection.South), _spriteDatabase);
                string? file = look?.Frames.FirstOrDefault()?.FilePath ?? (string.IsNullOrEmpty(def.IconPath) ? null : def.IconPath);
                int n = SpriteOf(file);
                if (n < 0) continue;
                int wornSlot = avatarWho.EquippedSlots.FirstOrDefault(kv => kv.Value == item.InstanceId) is { Value: > 0 } kv2 && avatarWho.EquippedSlots.ContainsValue(item.InstanceId) ? (int)kv2.Key + 1 : 0;
                if (wornSlot > 15) wornSlot = 0;
                objects.Add((0xFFFF, 0xFFFF, n, 64 | 2 | (def.IsSolid ? 1 : 0) | (def.Type == ObjectType.Key ? 512 : 0) | (def.Type == ObjectType.Lockpick ? 1024 : 0) | (wornSlot << 12),
                    pack.NameOf(def.Name), def.Type == ObjectType.Key ? def.Id : def.Type is ObjectType.Weapon or ObjectType.Armor or ObjectType.Spellbook ? GearOf(def) : def.Type == ObjectType.LightSource ? LightCode(def, item.IsLitRuntime ?? false) : def.Type == ObjectType.Food ? FoodCode(def) : def.Type == ObjectType.Ammo ? AmmoCode(def) : 0xFFFF, IconOf(file), owner));
            }
        }
        if (PlayerCharacter() is { } theAvatar) AddCarried(theAvatar, -1);
        // Combat and stats (as the Windows Ultima 6 engine: U6GameWindow.Combat.cs / Weapons.cs / Vitals.cs).
        U6Leveling.Use(_systemSettings.Ultima6);
        bool u6Levels = !_systemSettings.Ultima6.UseUltima7Leveling;
        var avatarDef = PlayerCharacter();
        int StatOf(CharacterDefinition c, BuiltInStatKind kind, int fallback) =>
            _statDatabase.Stats.FirstOrDefault(st => st.Kind == kind) is { } stat && c.StatValues.TryGetValue(stat.Id, out var text) && int.TryParse(text, out int value) ? value : fallback;
        (int Weapon, int Reach, int Armour) Gear(CharacterDefinition c)
        {
            int weapon = 0, reach = 1, armour = 0;
            foreach (int id in c.EquippedSlots.Values)
            {
                var item = c.Inventory.FirstOrDefault(i => i.InstanceId == id);
                var def = item is null || item.Category != ReferencedItemCategory.Object ? null : _objectDatabase.Objects.FirstOrDefault(o => o.Id == item.ItemId);
                if (def is null) continue;
                if (def.Type == ObjectType.Weapon) { weapon = Math.Max(weapon, def.Weapon.Damage); reach = Math.Max(reach, def.Weapon.Use == WeaponUse.Melee ? 1 : Math.Max(2, def.Weapon.RangeTiles / 2)); }
                else if (def.Type == ObjectType.Armor) armour += Math.Max(0, def.Armor.ArmorPoints);
            }
            return (weapon, reach, armour);
        }
        // What flies when a weapon is used from afar: its projectile, else its family's ammo, else itself (thrown).
        int MissileOf(WorldObject wd)
        {
            string? f = null;
            if (wd.Weapon.ProjectileObjectId >= 0 && _objectDatabase.Objects.FirstOrDefault(o => o.Id == wd.Weapon.ProjectileObjectId) is { } pd) f = ObjectLooks.SpriteFor(pd, new ObjectState(false, false, false, false, 1, SpriteDirection.South), _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath ?? pd.IconPath;
            else if (wd.Weapon.Ammo == WeaponAmmo.Ammo && _objectDatabase.Objects.FirstOrDefault(o => o.Type == ObjectType.Ammo && string.Equals(o.Ammo.Family, wd.Weapon.AmmoFamily, StringComparison.OrdinalIgnoreCase)) is { } ad) f = ObjectLooks.SpriteFor(ad, new ObjectState(false, false, false, false, 1, SpriteDirection.South), _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath ?? ad.IconPath;
            else if (wd.Weapon.Use != WeaponUse.Melee) f = ObjectLooks.SpriteFor(wd, new ObjectState(false, false, false, false, 1, SpriteDirection.South), _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath ?? wd.IconPath;
            int sp = SpriteOf(f);
            return sp < 0 ? 0xFFFF : sp;
        }
        foreach (var wd in _objectDatabase.Objects.Where(o => o.Type == ObjectType.Weapon && o.Weapon.Use != WeaponUse.Melee).Take(64))
            pack.Weapons.Add((pack.NameOf(wd.Name), wd.Weapon.Ammo == WeaponAmmo.Ammo ? 1 : wd.Weapon.Ammo == WeaponAmmo.ThrowsItself ? 2 : wd.Weapon.Ammo == WeaponAmmo.Charges ? 3 : 0,
                wd.Weapon.Ammo == WeaponAmmo.Ammo ? FamilyOf(wd.Weapon.AmmoFamily) : wd.Weapon.Ammo == WeaponAmmo.Charges ? Math.Clamp(wd.Weapon.Charges, 0, 9999) | (wd.Weapon.GoneWhenEmpty ? 0x8000 : 0) : 0, MissileOf(wd)));
        int[] StatsOf(CharacterDefinition c, int homeX, int homeY)
        {
            var (weapon, reach, armour) = Gear(c);
            int level = Math.Max(1, StatOf(c, BuiltInStatKind.Level, 1));
            int str = StatOf(c, BuiltInStatKind.Strength, 10);
            int maxHp = u6Levels ? U6Leveling.MaxHits(level) : Math.Max(1, str);
            int hp = Math.Clamp(StatOf(c, BuiltInStatKind.Hits, maxHp), 1, Math.Max(maxHp, 1));
            var st = new int[AppleIIgsGamePack.StatWords];
            st[AppleIIgsGamePack.Stat.Hp] = hp; st[AppleIIgsGamePack.Stat.MaxHp] = Math.Max(maxHp, hp);
            st[AppleIIgsGamePack.Stat.Str] = str; st[AppleIIgsGamePack.Stat.Dex] = StatOf(c, BuiltInStatKind.Dexterity, 10);
            st[AppleIIgsGamePack.Stat.Int] = StatOf(c, BuiltInStatKind.Intelligence, 10); st[AppleIIgsGamePack.Stat.Combat] = StatOf(c, BuiltInStatKind.Combat, 10);
            st[AppleIIgsGamePack.Stat.Magic] = StatOf(c, BuiltInStatKind.Magic, 0);
            st[AppleIIgsGamePack.Stat.Mana] = StatOf(c, BuiltInStatKind.Mana, st[AppleIIgsGamePack.Stat.Magic]); st[AppleIIgsGamePack.Stat.MaxMana] = Math.Max(st[AppleIIgsGamePack.Stat.Magic], st[AppleIIgsGamePack.Stat.Mana]);
            st[AppleIIgsGamePack.Stat.Level] = level; st[AppleIIgsGamePack.Stat.Exp] = StatOf(c, BuiltInStatKind.Exp, 0);
            st[AppleIIgsGamePack.Stat.Armour] = armour; st[AppleIIgsGamePack.Stat.Weapon] = weapon; st[AppleIIgsGamePack.Stat.Reach] = reach;
            st[AppleIIgsGamePack.Stat.HomeX] = homeX; st[AppleIIgsGamePack.Stat.HomeY] = homeY;
            st[AppleIIgsGamePack.Stat.Food] = c.Survival.CanGetHungry ? Math.Clamp(c.Survival.StartingFood, 0, 31) : 0xFFFF;
            st[20] = c == avatarDef ? 6 : 0xFFFF; // the Avatar: Command (solo.s); anyone else: its schedule's key (schedule.s) - a member's mode comes by name (Modes)
            if (c != avatarDef && AppleIIgsGamePack.CombatModeCode(c.U6CombatMode) is int modeCode and > 0 && pack.Modes.All(m => m.Name != pack.NameOf(c.Name))) pack.Modes.Add((pack.NameOf(c.Name), modeCode));
            st[23] = 0xFFFF; // what flies when it strikes from afar (its equipped ranged weapon's)
            foreach (int eid in c.EquippedSlots.Values)
                if (c.Inventory.FirstOrDefault(i => i.InstanceId == eid) is { Category: ReferencedItemCategory.Object } ei && _objectDatabase.Objects.FirstOrDefault(o => o.Id == ei.ItemId) is { Type: ObjectType.Weapon } ewd && ewd.Weapon.Use != WeaponUse.Melee)
                    st[23] = MissileOf(ewd);
            return st;
        }
        bool Hostile(CharacterDefinition c) => avatarDef is not null && c != avatarDef && _factionDatabase.BehaviorToward(c.FactionId, avatarDef.FactionId) == FactionBehavior.Attack;
        if (avatarDef is not null) { int gold = pack.AvatarStats[23]; pack.AvatarStats = StatsOf(avatarDef, 0, 0); pack.AvatarStats[23] = gold; }
        pack.U6Levels = u6Levels; pack.HitsPerLevel = U6Leveling.HitsPerLevel; pack.MostHits = U6Leveling.Table.MostHits;
        for (int lv = 1; lv < Math.Min(U6Leveling.MaxLevel, 17); lv++) pack.LevelNeeds.Add(U6Leveling.Needs(lv));
        if (avatarGold > 0) pack.AvatarStats[23] = Math.Min(avatarGold, 65535);
        var characters = new List<(int X, int Y, int Sprite, int Flags, int Facing, int Talk, int Portrait, int Name, int[] Stats)>();
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
        // Where each character stands in the game (its map, its place in that map's list) - for Join / Leave Party.
        var whereIs = new Dictionary<string, (int Map, int Index)>();
        for (int wi = 0; wi < worldList.Count; wi++)
        {
            int index = 0;
            foreach (var placed in worldList[wi].Characters.Where(c => !c.Dead && c.Floor == 1))
            {
                var who = _characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId);
                if (who is null || who.IsTestPlayer || placed.X < 0 || placed.Y < 0 || placed.X >= worldList[wi].Width || placed.Y >= worldList[wi].Height) continue;
                if (index >= AppleIIgsLimits.MaxCharactersPerMap) break;
                whereIs.TryAdd(who.Id.ToString(), (wi, index));
                index++;
            }
        }
        (int Map, int Index)? WhereIs(string id) => whereIs.TryGetValue(id, out var at) ? at : null;
        // Who may join the party (a Join Party in any talk or map event): their things come with them.
        var joiners = new HashSet<int>();
        void Joins(IEnumerable<EventCommand> cmds, int self)
        {
            foreach (var c in cmds.Where(c => c.Type == EventCommandType.JoinParty && c.Get("Value") != "Leave the party"))
                if (c.Get("Who") is "" or "Activator") { if (self >= 0) joiners.Add(self); }
                else if (int.TryParse(c.Get("Who"), out int id)) joiners.Add(id);
        }
        foreach (var ch in _characterDatabase.Characters) Joins(ch.TalkCommands, ch.Id);
        foreach (var w in worldList) foreach (var ev in w.Events) foreach (var pg in ev.EventPages) Joins(pg.Commands, -1);
        foreach (int id in joiners)
            if (_characterDatabase.Characters.FirstOrDefault(c => c.Id == id) is { } joiner && !joiner.IsTestPlayer && whereIs.TryGetValue(id.ToString(), out var at))
                AddCarried(joiner, 0x8000 | (at.Map << 6) | at.Index);
        // Give Item / Remove Item / Show Picture / Change Stat: what the commands need (Engine/AppleIIgsTalk.cs Env).
        var pictureOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Arrays and grids: every name the game's commands use, a number each (16 arrays, 8 grids on the Apple IIgs).
        var arrayIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var gridIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void ScanNames(IEnumerable<EventCommand>? commands)
        {
            foreach (var c in commands ?? Enumerable.Empty<EventCommand>())
            {
                void Arr(string n) { n = n.Trim(); if (n.Length > 0 && !arrayIndex.ContainsKey(n) && arrayIndex.Count < 16) arrayIndex[n] = arrayIndex.Count; }
                void Grd(string n) { n = n.Trim(); if (n.Length > 0 && !gridIndex.ContainsKey(n) && gridIndex.Count < 8) gridIndex[n] = gridIndex.Count; }
                switch (c.Type)
                {
                    case EventCommandType.CreateArray or EventCommandType.SetElement or EventCommandType.GetElement or EventCommandType.AddToArray or EventCommandType.InsertAt
                        or EventCommandType.RemoveAt or EventCommandType.ArrayLength or EventCommandType.FindInArray or EventCommandType.ReorderArray or EventCommandType.FillArray
                        or EventCommandType.ClearArray or EventCommandType.ArrayStatistic:
                        Arr(c.Get("Array")); break;
                    case EventCommandType.CopyArray: Arr(c.Get("From")); Arr(c.Get("Array")); break;
                    case EventCommandType.CreateGrid or EventCommandType.SetCell or EventCommandType.GetCell or EventCommandType.FillGrid or EventCommandType.GridSize
                        or EventCommandType.ResizeGrid or EventCommandType.FindInGrid or EventCommandType.GridFromMap or EventCommandType.GridToMap:
                        Grd(c.Get("Grid")); break;
                    case EventCommandType.GridLineToArray or EventCommandType.ArrayToGridLine: Grd(c.Get("Grid")); Arr(c.Get("Array")); break;
                    case EventCommandType.ForEach: if (c.Get("Of") == "Cell of a grid") Grd(c.Get("Array")); else Arr(c.Get("Array")); break;
                }
            }
        }
        foreach (var ce in _systemSettings.CommonEvents) ScanNames(ce.Commands);
        foreach (var ch in _characterDatabase.Characters) { ScanNames(ch.TalkCommands); foreach (var pg in ch.EventPages) ScanNames(pg.Commands); }
        foreach (var od in _objectDatabase.Objects) foreach (var pg in od.EventPages) ScanNames(pg.Commands);
        foreach (var cd in _containerDatabase.Containers) foreach (var pg in cd.EventPages) ScanNames(pg.Commands);
        foreach (var world in worldList) foreach (var ev in world.Events) foreach (var pg in ev.EventPages) ScanNames(pg.Commands);
        foreach (var le in _systemSettings.Ultima6.Active?.Events ?? new List<U6LevelEvent>()) ScanNames(le.Commands);
        int StatWord(BuiltInStatKind? kind) => kind switch
        {
            BuiltInStatKind.Hits => AppleIIgsGamePack.Stat.Hp, BuiltInStatKind.Strength => AppleIIgsGamePack.Stat.Str, BuiltInStatKind.Dexterity => AppleIIgsGamePack.Stat.Dex,
            BuiltInStatKind.Intelligence => AppleIIgsGamePack.Stat.Int, BuiltInStatKind.Combat => AppleIIgsGamePack.Stat.Combat, BuiltInStatKind.Magic => AppleIIgsGamePack.Stat.Magic,
            BuiltInStatKind.Mana => AppleIIgsGamePack.Stat.Mana, BuiltInStatKind.Level => AppleIIgsGamePack.Stat.Level, BuiltInStatKind.Exp => AppleIIgsGamePack.Stat.Exp, _ => -1
        };
        int StatNamed(string name) => StatWord(_statDatabase.Stats.FirstOrDefault(st => string.Equals(st.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))?.Kind);
        var formulaNames = new ScriptExpression.IIgsNames
        {
            Array = n => arrayIndex.TryGetValue(n.Trim(), out int i) ? i : -1,
            Grid = n => gridIndex.TryGetValue(n.Trim(), out int i) ? i : -1,
            Stat = StatNamed,
            Item = n => pack.NameOf(n.Trim()),
            Who = n => _characterDatabase.Characters.FirstOrDefault(c => string.Equals(c.Name, n.Trim(), StringComparison.OrdinalIgnoreCase)) is { } wc ? WhereIs(wc.Id.ToString()) : null
        };
        var talkEnv = new AppleIIgsTalk.Env
        {
            AvatarFaction = avatarDef?.FactionId ?? -1,
            Exchange = (fromId, toId) =>
            {
                if (!purseOf.TryGetValue(fromId, out int pf) || !purseOf.TryGetValue(toId, out int pt)) return null;
                var (rate, cut) = ProjectSession.Currencies.RateOf(fromId, toId);
                return (pf, pt, (int)Math.Clamp(Math.Round(rate * (1 - cut / 100.0) * 256), 0, 65535));
            },
            Names = formulaNames,
            StatByName = StatNamed,
            Item = (category, itemId) =>
            {
                if (!Enum.TryParse<ReferencedItemCategory>(category, out var cat) || !int.TryParse(itemId, out int id)) return null;
                string? file = DollLook.ItemSprite(cat, id, _objectDatabase, _bookDatabase, _containerDatabase, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath;
                if (cat == ReferencedItemCategory.Object && file is null) file = _objectDatabase.Objects.FirstOrDefault(o => o.Id == id)?.IconPath;
                int sprite = SpriteOf(file);
                if (sprite < 0) return null;
                string name = DollLook.ItemName(new ItemReference { Category = cat, ItemId = id }, _objectDatabase, _bookDatabase, _containerDatabase);
                var giveDef = cat == ReferencedItemCategory.Object ? _objectDatabase.Objects.FirstOrDefault(o => o.Id == id) : null;
                int flags = 2 | (cat == ReferencedItemCategory.Container ? 128 : 0) | (giveDef?.Type == ObjectType.Key ? 512 : 0) | (giveDef?.Type == ObjectType.Lockpick ? 1024 : 0);
                int other = giveDef?.Type == ObjectType.Key ? giveDef.Id : giveDef?.Type is ObjectType.Weapon or ObjectType.Armor or ObjectType.Spellbook ? GearOf(giveDef) : giveDef?.Type == ObjectType.LightSource ? LightCode(giveDef, false) : giveDef?.Type == ObjectType.Food ? FoodCode(giveDef) : giveDef?.Type == ObjectType.Ammo ? AmmoCode(giveDef) : 0xFFFF;
                if (giveDef is not null && SpecialOf(giveDef) is int gsc) other = gsc;
                return (pack.NameOf(name), sprite, IconOf(file), flags, other);
            },
            Picture = (group, name) =>
            {
                var sp = _spriteDatabase.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(x => x.Name == name)
                    ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(x => x.Name == name);
                if (sp?.Frames.FirstOrDefault()?.FilePath is not { } f || !File.Exists(f)) return -1;
                if (pictureOf.TryGetValue(f, out int known)) return known;
                var img = new BitmapImage();
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(f)); img.EndInit();
                int pw = Math.Min(320, (img.PixelWidth + 3) / 4 * 4), ph = Math.Min(200, img.PixelHeight);
                spritePixels.Add((FitPicture(f, pw, ph), pw, ph));
                return pictureOf[f] = spritePixels.Count - 1;
            },
            ItemName = (cat, id) => DollLook.ItemName(new ItemReference { Category = cat, ItemId = id }, _objectDatabase, _bookDatabase, _containerDatabase),
            Common = id => _systemSettings.CommonEvents.FirstOrDefault(c => c.Id == id)?.Commands,
            LevelEvent = name => _systemSettings.Ultima6.Active?.Events.FirstOrDefault(e => string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))?.Commands,
            WhoName = who => who is "" or "Activator" or "Player" ? PlayerCharacter()?.Name : int.TryParse(who, out int wid) ? _characterDatabase.Characters.FirstOrDefault(c => c.Id == wid)?.Name : null,
            Stat = id => _statDatabase.Stats.FirstOrDefault(st => st.Id.ToString() == id)?.Kind switch
            {
                BuiltInStatKind.Hits => AppleIIgsGamePack.Stat.Hp, BuiltInStatKind.Strength => AppleIIgsGamePack.Stat.Str, BuiltInStatKind.Dexterity => AppleIIgsGamePack.Stat.Dex,
                BuiltInStatKind.Intelligence => AppleIIgsGamePack.Stat.Int, BuiltInStatKind.Combat => AppleIIgsGamePack.Stat.Combat, BuiltInStatKind.Magic => AppleIIgsGamePack.Stat.Magic,
                BuiltInStatKind.Mana => AppleIIgsGamePack.Stat.Mana, BuiltInStatKind.Level => AppleIIgsGamePack.Stat.Level, BuiltInStatKind.Exp => AppleIIgsGamePack.Stat.Exp, _ => -1
            }
        };

        pack.PartySize = Math.Max(1, _systemSettings.Ultima6.PartySize);
        // Bodies: each placed character's (and the Avatar's) Body pose picture, whether it leaves one, whether it can be lifted.
        {
            var seenBody = new HashSet<int>();
            foreach (var world in worldList)
                foreach (var placed in world.Characters)
                    if (_characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId) is { } bc && seenBody.Add(bc.Id))
                        pack.CharBodies.Add((pack.NameOf(bc.Name), SpriteOf(BodyFile(bc)) is int bs and >= 0 ? bs : 0xFFFF, (bc.Body.NoBody ? 1 : 0) | (bc.Body.TooHeavy ? 2 : 0)));
            if (PlayerCharacter() is { } av && SpriteOf(BodyFile(av)) is int avb and >= 0) pack.AvatarBody = avb;
            pack.BodyName = pack.NameOf(_systemSettings.BodyContainer.Name.Trim().Length > 0 ? _systemSettings.BodyContainer.Name : "Body");
        }
        // Text variables start empty; the maps' names (Get Game Data).
        foreach (var v in _variableDatabase.Variables.Where(v => v.Type == VariableValueType.String)) pack.TextVariables.Add(VariableOf(v.Name));
        foreach (var world in worldList) pack.MapNames.Add(pack.NameOf(world.MapName));
        var dn = _systemSettings.DayNight;
        pack.DayNight = (dn.Enabled, dn.DawnStartHour, dn.DayStartHour, dn.DuskStartHour, dn.NightStartHour);
        // A character's or thing's own event pages as code (in Codes), once each.
        var ownCode = new Dictionary<string, int?>();
        int? OwnEvents(List<BookEventPage> pages, string key)
        {
            if (ownCode.TryGetValue(key, out var known)) return known;
            var ps = IIgsPages(pages, true, out _);
            int? result = null;
            if (ps.Count > 0)
            {
                AppleIIgsTalk.CompileEvent(ps, VariableOf, out var code, out _, id => soundIndex.TryGetValue(id, out int sn) ? sn : -1, WorldIndex, WhereIs, talkEnv);
                result = pack.Codes.Count;
                pack.Codes.Add(code);
            }
            return ownCode[key] = result;
        }
        // Spawn points: each map's eggs (their commands as code), monster eggs' characters as absent copies (MapCharacters).
        var eggPlacements = new Dictionary<(MapData, int), List<PlacedCharacter>>();
        var eggCopy = new Dictionary<PlacedCharacter, (int Egg, int Percent, int X, int Y)>();
        void MapEggs(MapData world, int floor, int mapNo)
        {
            foreach (var egg in world.SpawnPoints.Where(e => e.Floor == floor))
            {
                if (pack.Eggs.Count >= 64) break;
                int eggNo = pack.Eggs.Count;
                int Code(params EventCommand[] cmds)
                {
                    AppleIIgsTalk.CompileEvent(new() { new AppleIIgsTalk.EventPage(18, -1, 2, new(), cmds.ToList()) }, VariableOf, out var c, out _,
                        id => soundIndex.TryGetValue(id, out int sn) ? sn : -1, WorldIndex, WhereIs, talkEnv);
                    pack.Codes.Add(c);
                    return pack.Codes.Count - 1;
                }
                EventCommand Cmd(EventCommandType t, params (string K, string V)[] ps) { var c = new EventCommand { Type = t }; foreach (var (k, v) in ps) c.Parameters[k] = v; return c; }
                int code = 0xFFFF, stop = 0xFFFF;
                switch (egg.Kind)
                {
                    case EggKind.Music:
                        code = Code(Cmd(EventCommandType.PlayMusic, ("MusicResourceId", egg.SoundId.ToString()), ("Loop", egg.Continuous ? "True" : "False")));
                        if (egg.Continuous) stop = Code(Cmd(EventCommandType.StopMusic, ("Seconds", "0")));
                        break;
                    case EggKind.Sound:
                        code = Code(Cmd(EventCommandType.PlaySound, ("SoundResourceId", egg.SoundId.ToString())));
                        break;
                    case EggKind.Usecode when egg.CommonEventId >= 0:
                        code = Code(Cmd(EventCommandType.CallCommonEvent, ("CommonEventId", egg.CommonEventId.ToString())));
                        break;
                    case EggKind.Teleport:
                    {
                        int tw = egg.TeleportWorld, tx = egg.TeleportX, ty = egg.TeleportY;
                        if (egg.TeleportPath >= 0 && world.SpawnPoints.FirstOrDefault(pp => pp.Kind == EggKind.Path && pp.PathNumber == egg.TeleportPath) is { } path) (tw, tx, ty) = (-1, path.X, path.Y);
                        if (tx >= 0 && ty >= 0)
                            code = Code(Cmd(EventCommandType.ChangeMap, ("World", tw > 0 ? tw.ToString() : ""), ("X", tx.ToString()), ("Y", ty.ToString()), ("Floor", egg.TeleportFloor.ToString()), ("Fade", "False")));
                        break;
                    }
                    case EggKind.Weather:
                        code = Code(Cmd(EventCommandType.ChangeWeather, ("Kind", egg.WeatherKind), ("Intensity", egg.WeatherIntensity.ToString()), ("Clouds", "")));
                        break;
                }
                int sprite = 0xFFFF, dmin = 0, dmax = 0, launch = 0xFFFF;
                if (egg.Kind == EggKind.Missile && _objectDatabase.Objects.FirstOrDefault(o => o.Id == egg.ProjectileId && o.Type == ObjectType.Projectile) is { } proj)
                {
                    var pf = ObjectLooks.SpriteFor(proj, ObjectLooks.DefaultState(proj), _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath ?? proj.IconPath;
                    sprite = SpriteOf(pf) is int ps and >= 0 ? ps : 0xFFFF;
                    (dmin, dmax) = (Math.Max(0, proj.Projectile.DamageMin), Math.Max(proj.Projectile.DamageMin, proj.Projectile.DamageMax));
                }
                int dir = egg.Direction < 0 ? 0xFFFF : egg.Direction & 7;
                pack.Eggs.Add(new[] { mapNo, egg.X, egg.Y, (int)egg.Kind, (int)egg.Criteria, Math.Clamp(egg.Distance, 0, 63), Math.Clamp(egg.Probability, 1, 100),
                    (egg.Nocturnal ? 1 : 0) | (egg.Once ? 2 : 0) | (egg.AutoReset ? 4 : 0) | (egg.Continuous ? 8 : 0), code, stop, sprite, dmin, dmax, dir,
                    (int)Math.Clamp(Math.Round(egg.DelaySeconds * 60), 12, 3600), Math.Clamp(egg.RangeTiles, 1, 63), Math.Clamp(egg.WeatherMinutes, 0, 30000), launch });
                if (egg.Kind != EggKind.Monster) continue;
                if (!eggPlacements.TryGetValue((world, floor), out var pl)) eggPlacements[(world, floor)] = pl = new List<PlacedCharacter>();
                foreach (var entry in egg.Entries.Where(en => en.CharacterId >= 0))
                    for (int k = 0; k < Math.Clamp(entry.Count, 1, 8); k++)
                    {
                        var copy = new PlacedCharacter { Id = 900000 + eggNo * 64 + pl.Count, CharacterId = entry.CharacterId, X = egg.X, Y = egg.Y, Floor = floor };
                        pl.Add(copy);
                        eggCopy[copy] = (eggNo, Math.Clamp(entry.Percent, 0, 100), egg.X, egg.Y);
                    }
            }
        }
        // A character's four looks' hand points (from each facing's first frame), for the weapon drawn in its hand (inhand.s).
        // sixteen: the Avatar's looks are fitted into 16 x 16 (FitInto16), not FitSprite.
        var handsDone = new HashSet<int>();
        void HandPoints(CharacterDefinition who, int firstSprite, bool sixteen = false)
        {
            var frames = LookFrames(who);
            for (int k = 0; k < 4 && k < frames.Count; k++)
            {
                if (frames[k] is not { HasHand: true } fr || !File.Exists(fr.FilePath) || !handsDone.Add(firstSprite + k)) continue;
                (int X, int Y) hp;
                if (sixteen)
                {
                    var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(Path.GetFullPath(fr.FilePath)); bi.EndInit();
                    hp = ((int)Math.Round(fr.HandX * 16.0 / Math.Max(1, bi.PixelWidth)) - 8, (int)Math.Round(fr.HandY * 16.0 / Math.Max(1, bi.PixelHeight)) - 16);
                }
                else hp = FitPoint(fr.FilePath, fr.HandX, fr.HandY);
                pack.Hands.Add((firstSprite + k, hp.X, hp.Y, fr.WeaponBehind));
            }
        }
        List<(int X, int Y, int Sprite, int Flags, int Facing, int Talk, int Portrait, int Name, int[] Stats)> MapCharacters(MapData world, List<(int Character, int Period, int Activity, int X, int Y)> schedules, int floor = 1, int mapNo = 0)
        {
            var list = new List<(int X, int Y, int Sprite, int Flags, int Facing, int Talk, int Portrait, int Name, int[] Stats)>();
            foreach (var placed in world.Characters.Where(c => !c.Dead && c.Floor == floor).Concat(eggPlacements.TryGetValue((world, floor), out var eggPl) ? eggPl : new List<PlacedCharacter>()))
            {
                var who = _characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId);
                if (who is null || who.IsTestPlayer) continue;
                if (placed.X < 0 || placed.Y < 0 || placed.X >= world.Width || placed.Y >= world.Height) continue;
                var files = LookFiles(who);
                // Ultima 6: its sleeping and sitting looks after its four facings (+4, +5; flags $4000) - asleep in bed, sitting on a chair.
                string? sleepFile = PoseFile(who, "Sleeping"), sitFile = PoseFile(who, "Sit");
                bool poseLooks = sleepFile is not null || sitFile is not null;
                if (poseLooks) { files.Add(sleepFile ?? files[0]); files.Add(sitFile ?? files[0]); }
                int first = spritePixels.Count;
                foreach (var f in files) { if (f is not null && File.Exists(f)) spritePixels.Add(FitSprite(f)); else spritePixels.Add((new uint[16 * 16], 16, 16)); }
                HandPoints(who, first); // where its weapon is held, each facing (inhand.s)
                int facing = placed.Facing switch { SpriteDirection.North => 1, SpriteDirection.East => 2, SpriteDirection.West => 3, _ => 0 };
                if (!talkOf.TryGetValue(who.Id, out int talk))
                {
                    talk = -1;
                    if (who.TalkCommands.Count > 0)
                    {
                        AppleIIgsTalk.Compile(who.TalkCommands, VariableOf, out var code, id => soundIndex.TryGetValue(id, out int n) ? n : -1, WorldIndex, WhereIs, talkEnv);
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
                var stats = StatsOf(who, placed.X, placed.Y);
            // Its schedule (Exult's 3-hour periods): the activities the Apple IIgs does; work ones loiter at their spot.
            foreach (var entry in who.Schedules.Where(e => e.Period is >= 0 and <= 7))
            {
                int activity = entry.Type switch
                {
                    ScheduleType.Stand or ScheduleType.Wait => 0,
                    ScheduleType.Sit => 0x80, // stands there, drawn sitting (main.s)
                    ScheduleType.Wander => 1,
                    ScheduleType.Sleep => 2,
                    ScheduleType.HorizPace => 3,
                    ScheduleType.VertPace => 4,
                    ScheduleType.Bake or ScheduleType.Sew or ScheduleType.Blacksmith or ScheduleType.Lab => 6,
                    ScheduleType.Farm or ScheduleType.Miner => 7,
                    _ => 5
                };
                int sx = entry.X >= 0 && entry.X < world.Width ? entry.X : placed.X, sy = entry.Y >= 0 && entry.Y < world.Height ? entry.Y : placed.Y;
                schedules.Add((list.Count, entry.Period, activity, sx, sy));
            }
            stats[AppleIIgsGamePack.Stat.Behaviour] = who.Schedules.Count > 0 ? 2 : Hostile(who) ? 1 : 0;
            int moves = who.Movement switch { MovementKind.Sea => 0x1000, MovementKind.Air => 0x2000, _ => 0 }; // sea / air (moves.s)
            if (poseLooks) moves |= 0x4000;
            list.Add((placed.X, placed.Y, first, 1 | (Hostile(who) ? 2 : 0) | moves, facing, talk < 0 ? 0xFFFF : talk, portrait < 0 ? 0xFFFF : portrait, pack.NameOf(who.Name), stats));
                if (OwnEvents(who.EventPages, "c" + who.Id) is int ownCode) pack.CharEvents.Add((mapNo, list.Count - 1, ownCode));
                if (eggCopy.TryGetValue(placed, out var ec))
                {
                    // An egg's monster: not there till the egg hatches; it roams round the egg (free: anywhere).
                    var last = list[^1];
                    last.Stats[AppleIIgsGamePack.Stat.Behaviour] = 1;
                    last.Stats[AppleIIgsGamePack.Stat.HomeX] = ec.X; last.Stats[AppleIIgsGamePack.Stat.HomeY] = ec.Y;
                    list[^1] = last with { Flags = last.Flags | 4 };
                    pack.EggChars.Add((mapNo, list.Count - 1, ec.Egg, ec.Percent));
                }
            }
            // Monsters that split when hit (Joining): 6 spare copies of each such kind on this map, absent till a split (join.s).
            var splitters = list.Where(c => (c.Flags & 4) == 0).Select(c => c).GroupBy(c => c.Name)
                .Where(g => _characterDatabase.Characters.FirstOrDefault(d => pack.NameOf(d.Name) == g.Key) is { Joining.Growth: not JoinGrowth.OurEngine }).ToList();
            foreach (var g in splitters)
                for (int k = 0; k < 6 && list.Count < AppleIIgsLimits.MaxCharactersPerMap; k++)
                {
                    var t = g.First();
                    list.Add((t.X, t.Y, t.Sprite, t.Flags | 4 | 0x8000, t.Facing, t.Talk, t.Portrait, t.Name, (int[])t.Stats.Clone()));
                }
            if (list.Count > AppleIIgsLimits.MaxCharactersPerMap) list.RemoveRange(AppleIIgsLimits.MaxCharactersPerMap, list.Count - AppleIIgsLimits.MaxCharactersPerMap);
            return list;
        }
        MapEggs(start, 1, 0);
        characters.AddRange(MapCharacters(start, pack.Schedules));
        pack.StartHour = _systemSettings.Clock.StartHour;
        // A map's events: stepped on (By Enter), their first such page's commands run - made into the talk code.
        List<(int X, int Y, byte[] Code)> MapEvents(MapData world, List<int> masks, int floor = 1)
        {
            var list = new List<(int X, int Y, byte[] Code)>();
            // Stairs and ladders (Object Editor > Stairs / Ladder): stepped on, to the floor above / below, or the ladder's destination.
            foreach (var (x, y, map, tx, ty) in FloorLinks(world, floor))
            {
                var code = new List<byte> { 9, 1 };
                code.AddRange(new byte[] { 0, 0 });
                code.Add(11);
                code.Add(7); code.AddRange(BitConverter.GetBytes((ushort)map)); code.AddRange(BitConverter.GetBytes((ushort)tx)); code.AddRange(BitConverter.GetBytes((ushort)ty));
                code.Add(0);
                int next = code.Count;
                code[2] = (byte)next; code[3] = (byte)(next >> 8);
                code.Add(0);
                list.Add((x, y, code.ToArray()));
                masks.Add(1 << 1);
            }
            foreach (var ev in world.Events.Where(e => e.Floor == floor && e.X >= 0 && e.Y >= 0 && e.X < world.Width && e.Y < world.Height))
            {
                var pages = IIgsEventPages(ev, out _);
                if (pages.Count == 0) continue;
                AppleIIgsTalk.CompileEvent(pages, VariableOf, out var code, out int mask, id => soundIndex.TryGetValue(id, out int n) ? n : -1, WorldIndex, WhereIs, talkEnv);
                list.Add((ev.X, ev.Y, code));
                masks.Add(mask);
            }
            return list;
        }
        List<AppleIIgsTalk.EventPage> IIgsEventPages(GameEvent ev, out List<string> leftOut) => IIgsPages(ev.EventPages, false, out leftOut);
        // A character's, object's or map event's pages (owner: a character's or a thing's own - hit, killed, Used...).
        List<AppleIIgsTalk.EventPage> IIgsPages(List<BookEventPage> ownPages, bool owner, out List<string> leftOut)
        {
            leftOut = new List<string>();
            var pages = new List<AppleIIgsTalk.EventPage>();
            foreach (var page in ownPages.Where(pg => pg.Commands.Count > 0))
            {
                var c = page.Conditions;
                int item = c.TriggerItemReference is { } r ? pack.NameOf(DollLook.ItemName(r, _objectDatabase, _bookDatabase, _containerDatabase)) : -1;
                (int Trigger, int Kind) t = c.TriggerConditionBased switch
                {
                    BookTriggerConditionBased.ByEnter => (1, 2),
                    BookTriggerConditionBased.ByStay => owner ? (14, 2) : (2, 2),
                    BookTriggerConditionBased.ByExit => (3, 2),
                    BookTriggerConditionBased.ByUseItem when item < 0 => owner ? (12, 2) : (4, 2),
                    BookTriggerConditionBased.ByUseItem => (13, 2),
                    BookTriggerConditionBased.ByHit when owner => (9, 2),
                    BookTriggerConditionBased.ByTradeItem when owner => (15, 2),
                    BookTriggerConditionBased.ByAfterReadItem when owner => (16, 2),
                    BookTriggerConditionBased.ByDeath when owner => (10, 2),
                    BookTriggerConditionBased.ByAilmentEffect when owner => (11, 2),
                    BookTriggerConditionBased.Always => (5, 2),
                    BookTriggerConditionBased.ByHavingAnItem => (6, 0),
                    BookTriggerConditionBased.ByNotHavingAnItem => (6, 1),
                    BookTriggerConditionBased.ByDropItem => (7, 2),
                    BookTriggerConditionBased.ByPickupItem => (8, 2),
                    _ => (0, 0)
                };
                if (t.Trigger == 0) { leftOut.Add(c.TriggerConditionBased == BookTriggerConditionBased.ByUseItem ? "By Use Item (an item used on it)" : c.TriggerConditionBased.ToString()); continue; }
                var conds = new List<(int Variable, int Test, int Value, bool Or)>();
                if (c.ConditionInACondition)
                    for (int i = 0; i < c.VariableConditions.Count; i++)
                    {
                        var e = c.VariableConditions[i];
                        var v = _variableDatabase.Variables.FirstOrDefault(x => x.Id == e.VariableId);
                        if (v is null || e.Mode == VariableConditionMode.String) { leftOut.Add("a text variable condition"); continue; }
                        int test = e.Comparison switch
                        {
                            ConditionComparisonOperator.NotEqual => 1, ConditionComparisonOperator.GreaterThan => 2, ConditionComparisonOperator.LessThan => 3,
                            ConditionComparisonOperator.GreaterOrEqual => 4, ConditionComparisonOperator.LessOrEqual => 5, _ => 0
                        };
                        int value = e.Mode == VariableConditionMode.Boolean ? (bool.TryParse(e.Value, out bool b) && b ? 1 : 0) : int.TryParse(e.Value, out int n) ? n : 0;
                        conds.Add((VariableOf(v.Name), test, value, i > 0 && c.VariableConditions[i - 1].NextOperator == ConditionLogicOperator.Or));
                    }
                if (c.ConditionStatCondition || c.ConditionEventTriggerCompleted || c.ConditionComplex || c.ConditionDistance || c.ConditionJoinedParty)
                    leftOut.Add("conditions other than variables and switches");
                pages.Add(new AppleIIgsTalk.EventPage(t.Trigger, t.Kind == 2 && t.Trigger is 1 or 2 or 3 or 4 or 5 or 9 or 10 or 11 or 12 or 14 or 15 or 16 ? -1 : item, t.Kind, conds, page.Commands));
            }
            return pages;
        }
        pack.Events.AddRange(MapEvents(start, pack.EventMasks));
        // Ultima 6 leveling (Leveling Editor): rising by themselves or only earned; its events (When Run Leveling Event
        // calls one: built into the caller); the variables they read; characters' ids by name.
        if (!_systemSettings.Ultima6.UseUltima7Leveling)
        {
            U6Leveling.Use(_systemSettings.Ultima6);
            var system = _systemSettings.Ultima6.Active;
            pack.RiseByThemselves = system?.RiseByThemselves ?? true;
            pack.StatCap = U6Leveling.StatCap;
            pack.LevelVars = new[] { VariableOf("LevelWhoId"), VariableOf("LevelNow"), VariableOf("LevelEarned"), VariableOf("LevelRose"), VariableOf("RestHours") };
            foreach (var e in system?.Events ?? new List<U6LevelEvent>())
            {
                int trig = e.Trigger switch { U6LevelEvent.WhenEarned => 21, U6LevelEvent.WhenRested => 22, U6LevelEvent.WhenRestedEach => 23, _ => 0 };
                if (trig == 0 || e.Commands.Count == 0) continue;
                AppleIIgsTalk.CompileEvent(new() { new AppleIIgsTalk.EventPage(trig, -1, 2, new(), e.Commands) }, VariableOf, out var lcode, out _,
                    id => soundIndex.TryGetValue(id, out int sn) ? sn : -1, WorldIndex, WhereIs, talkEnv);
                pack.LevelEvents.Add((trig, pack.Codes.Count));
                pack.Codes.Add(lcode);
            }
            pack.AvatarId = PlayerCharacter()?.Id ?? 0;
            foreach (var world in worldList)
                foreach (var placed in world.Characters)
                    if (_characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId) is { } cd && !pack.CharIds.Any(ci => ci.Id == cd.Id))
                        pack.CharIds.Add((pack.NameOf(cd.Name), cd.Id));
        }
        else pack.RiseByThemselves = true;
        // Objects' and containers' own events: by the thing's name.
        foreach (var od in _objectDatabase.Objects.Where(o => o.EventPages.Any(pg => pg.Commands.Count > 0)))
            if (OwnEvents(od.EventPages, "o" + od.Id) is int oc) pack.ObjEvents.Add((pack.NameOf(od.Name), oc));
        foreach (var cd in _containerDatabase.Containers.Where(c => c.EventPages.Any(pg => pg.Commands.Count > 0)))
            if (OwnEvents(cd.EventPages, "k" + cd.Id) is int kc) pack.ObjEvents.Add((pack.NameOf(cd.Name), kc));
        // The other maps: their cells, objects, characters and events (each map its own file, MAP.n).
        for (int mi = 1; mi < worldList.Count; mi++)
        {
            var world = worldList[mi];
            var other = new AppleIIgsMap { Width = world.Width, Height = world.Height, Map = Grid(world),
                StartX = world.StartX >= 0 && world.StartX < world.Width ? world.StartX : world.Width / 2,
                StartY = world.StartY >= 0 && world.StartY < world.Height ? world.StartY : world.Height / 2 };
            AddMapObjects(world, mi << 12);
            MapEggs(world, 1, mi);
            other.Characters.AddRange(MapCharacters(world, other.Schedules, 1, mi));
            other.Events.AddRange(MapEvents(world, other.EventMasks));
            pack.OtherMaps.Add(other);
        }
        foreach (var ((wi, f), mi) in floorMap.OrderBy(kv => kv.Value))
        {
            var world = worldList[wi];
            var other = new AppleIIgsMap { Width = world.Width, Height = world.Height, Map = Grid(world, f, StairTops(world, f)), StartX = world.StartX, StartY = world.StartY };
            AddMapObjects(world, mi << 12, f);
            MapEggs(world, f, mi);
            other.Characters.AddRange(MapCharacters(world, other.Schedules, f, mi));
            other.Events.AddRange(MapEvents(world, other.EventMasks, f));
            pack.OtherMaps.Add(other);
        }
        // Music: each map's ambient sounds by the time of day, its zones' own (a Cave: silence), the combat music, the talk music.
        {
            int Snd(int id) => id >= 0 && soundIndex.TryGetValue(id, out int sn) ? sn : 0xFFFF;
            var ambient = Enumerable.Repeat((0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF), 1 + pack.OtherMaps.Count).ToArray();
            var weather = Enumerable.Range(0, ambient.Length).Select(_ => new[] { 0, 50, 0, 0, 0, 0, 0, 0, 0xFFFF, 0xFFFF }).ToArray();
            void MusicOf(MapData world, int mapNo, int floor)
            {
                if (mapNo >= ambient.Length) return;
                var a = world.Ambient;
                ambient[mapNo] = (Snd(a.DawnSoundId), Snd(a.DaySoundId), Snd(a.DuskSoundId), Snd(a.NightSoundId));
                foreach (var z in world.Zones.Where(z => z.Floor == floor && (SandcastleRpgMaker.Engine.Zones.IsCave(z) || z.AmbientSoundId >= 0))
                             .OrderBy(z => z.AmbientSoundId >= 0 ? 0 : 1).ThenBy(z => z.Width * z.Height))
                    pack.MusicZones.Add((mapNo, z.X, z.Y, z.Width, z.Height, z.AmbientSoundId >= 0 ? Snd(z.AmbientSoundId) : 0xFFFF, SandcastleRpgMaker.Engine.Zones.IsCave(z)));
                var wt = world.Weather;
                int wx = wt.Wind is SpriteDirection.East or SpriteDirection.NorthEast or SpriteDirection.SouthEast ? 1 : wt.Wind is SpriteDirection.West or SpriteDirection.NorthWest or SpriteDirection.SouthWest ? -1 : 0;
                int wy = wt.Wind is SpriteDirection.South or SpriteDirection.SouthEast or SpriteDirection.SouthWest ? 1 : wt.Wind is SpriteDirection.North or SpriteDirection.NorthEast or SpriteDirection.NorthWest ? -1 : 0;
                weather[mapNo] = new[] { (int)wt.Kind, Math.Clamp(wt.Intensity, 1, 100), wt.Clouds ? 1 : 0, Math.Clamp(wt.CloudCover, 0, 100), wx & 0xFFFF, wy & 0xFFFF,
                    wt.Changes ? 1 : 0, Math.Clamp(wt.ChangeChancePercent, 0, 100), Snd(wt.RainSoundResourceId), Snd(wt.ThunderSoundResourceId) };
            }
            for (int wi = 0; wi < worldList.Count; wi++) MusicOf(worldList[wi], wi, 1);
            foreach (var ((wi, f), mi) in floorMap) MusicOf(worldList[wi], mi, f);
            pack.Ambient.AddRange(ambient);
            pack.Weather.AddRange(weather);
            pack.Overcast16 = (int)Math.Round(Math.Clamp(_systemSettings.DayNight.OvercastStrength, 0, 1) * 16);
            var cm = _systemSettings.Combat.Music;
            foreach (var cue in new[] { cm.Danger, cm.Battle, cm.Battle2, cm.RunAway, cm.Escaped, cm.Victory, cm.BattleOver, cm.NearDeath, cm.Death })
                pack.CombatCues.Add((Snd(cue.MusicId), cue.Loop));
            pack.TalkMusic = Snd(_systemSettings.Ultima6.ConversationMusicId);
            pack.AfterTalkRandom = _systemSettings.Ultima6.AfterTalkMusic == Ultima6Settings.AfterTalkRandom;
        }
        // The Disk Swap screen's picture (the whole screen) and its animation's frames (each as a sprite of its own size).
        uint[]? swapPicture = null;
        SpriteEntry? SpriteOfLink(SpriteLink link) => !link.IsSet ? null :
            _spriteDatabase.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(sp => sp.Name == link.SpriteName)
            ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(sp => sp.Name == link.SpriteName);
        if (SpriteOfLink(swap.Picture)?.Frames.FirstOrDefault()?.FilePath is { } swapFile && File.Exists(swapFile)) swapPicture = FitPicture(swapFile, 320, 200);
        foreach (var frame in SpriteOfLink(swap.Animation)?.Frames.Take(16) ?? Enumerable.Empty<SpriteFrame>())
        {
            if (!File.Exists(frame.FilePath)) continue;
            var img = new BitmapImage();
            img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(Path.GetFullPath(frame.FilePath)); img.EndInit();
            int fw = Math.Min(320, (img.PixelWidth + 3) / 4 * 4), fh = Math.Min(200, img.PixelHeight);
            pack.SwapFrames.Add(spritePixels.Count);
            spritePixels.Add((FitPicture(frame.FilePath, fw, fh), fw, fh));
        }
        // The title menu's picture, its Acknowledgments, its Introduction (the intro commands).
        uint[]? titlePicture = null;
        var u6 = _systemSettings.Ultima6;
        if (SpriteOfLink(u6.TitleSprite)?.Frames.FirstOrDefault()?.FilePath is { } titleFile && File.Exists(titleFile)) titlePicture = FitPicture(titleFile, 320, 200);
        pack.Acknowledgments = u6.Acknowledgments;
        pack.LineOfSight = u6.LineOfSight;
        // The HUD's command buttons: a click is its verb's key (Ultima 6's: A C T L G D M U R B).
        foreach (var button in u6.Hud.Pieces.Where(p => p.Kind == HudPieceKind.CommandButton))
            pack.Buttons.Add((button.Verb switch
            {
                HudVerb.Attack => 'a', HudVerb.Cast => 'c', HudVerb.Talk => 't', HudVerb.Look => 'l', HudVerb.Get => 'g', HudVerb.Drop => 'd',
                HudVerb.Move => 'm', HudVerb.Use => 'u', HudVerb.Rest => 'r', _ => 'b'
            }, button.X, button.Y, button.Width, button.Height));
        foreach (var button in u6.Hud.Pieces.Where(p => p.Kind == HudPieceKind.CustomButton && p.EventPages.Count > 0 && p.EventPages[0].Commands.Count > 0))
        {
            AppleIIgsTalk.CompileEvent(new() { new AppleIIgsTalk.EventPage(17, -1, 2, new(), button.EventPages[0].Commands) }, VariableOf, out var bcode, out _,
                id => soundIndex.TryGetValue(id, out int sn) ? sn : -1, WorldIndex, WhereIs, talkEnv);
            pack.Buttons.Add(((char)(0x100 + pack.Codes.Count), button.X, button.Y, button.Width, button.Height));
            pack.Codes.Add(bcode);
        }
        if (u6.Hud.IntroCommands.Count > 0)
        {
            AppleIIgsTalk.Compile(u6.Hud.IntroCommands, VariableOf, out var intro, id => soundIndex.TryGetValue(id, out int sn) ? sn : -1, WorldIndex, WhereIs, talkEnv);
            pack.IntroCode = intro;
        }
        // A spell effect's thing: a summoned kind, a made item, a common event's code (their places in the pack).
        var spellItemOf = new Dictionary<int, int>();
        var summonOf = new Dictionary<int, int>();
        var codeOf = new Dictionary<int, int>();
        int SpellThing(MagicEffect e)
        {
            switch (e.Kind)
            {
                case MagicEffectKind.Summon when _characterDatabase.Characters.FirstOrDefault(c => c.Id == e.ThingId) is { } kind:
                    if (!summonOf.TryGetValue(kind.Id, out int si))
                    {
                        int first = spritePixels.Count;
                        foreach (var f in LookFiles(kind)) { if (f is not null && File.Exists(f)) spritePixels.Add(FitSprite(f)); else spritePixels.Add((new uint[16 * 16], 16, 16)); }
                        var st = StatsOf(kind, 0, 0);
                        st[AppleIIgsGamePack.Stat.Food] = 0xFFFF;
                        summonOf[kind.Id] = si = pack.Summons.Count;
                        pack.Summons.Add((first, pack.NameOf(kind.Name), st));
                    }
                    return si;
                case MagicEffectKind.CreateItem or MagicEffectKind.CreateField or MagicEffectKind.DestroyField when _objectDatabase.Objects.FirstOrDefault(o => o.Id == e.ThingId) is { } od:
                    if (!spellItemOf.TryGetValue(od.Id, out int ii))
                    {
                        string? file = ObjectLooks.SpriteFor(od, new ObjectState(false, false, false, false, 1, SpriteDirection.South), _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath ?? od.IconPath;
                        int sp = SpriteOf(file);
                        int flags = (od.IsPortable ? 2 : 0) | (od.IsSolid ? 1 : 0);
                        int other = od.Type == ObjectType.Food ? FoodCode(od) : od.Type == ObjectType.Ammo ? AmmoCode(od) : od.Type is ObjectType.Weapon or ObjectType.Armor ? GearOf(od) : 0xFFFF;
                        spellItemOf[od.Id] = ii = pack.Items.Count;
                        pack.Items.Add((pack.NameOf(od.Name), sp < 0 ? 0 : sp, IconOf(file), flags, other));
                    }
                    return ii;
                case MagicEffectKind.CommonEvent when _systemSettings.CommonEvents.FirstOrDefault(c => c.Id == e.CommonEventId) is { } ce:
                    if (!codeOf.TryGetValue(ce.Id, out int ci))
                    {
                        AppleIIgsTalk.Compile(ce.Commands, VariableOf, out var code, id => soundIndex.TryGetValue(id, out int sn) ? sn : -1, WorldIndex, WhereIs, talkEnv);
                        codeOf[ce.Id] = ci = pack.Codes.Count;
                        pack.Codes.Add(code);
                    }
                    return ci;
                default:
                    return 0xFFFF;
            }
        }
        foreach (var (spell, index, effect) in pendingThings) spell.Effects[index] = spell.Effects[index] with { Item4 = SpellThing(effect) };
        var hud = ComposeHud(pack, out var portraitPanel);
        foreach (var (dk, dx, dy, dw, dh, dframes) in pack.PendingDials)
        {
            int dfirst = spritePixels.Count;
            foreach (var f in dframes) spritePixels.Add((f, dw, dh));
            pack.Dials.Add((dk, dx, dy, dw, dh, dfirst, dframes.Count));
        }
        pack.Palette = SuperHiRes.MakePalette(tilePixels.SelectMany(p => p).Concat(avatar.SelectMany(p => p)).Concat(spritePixels.SelectMany(p => p.Px)).Concat(hud ?? Array.Empty<uint>()).Concat(portraitPanel ?? Array.Empty<uint>()).Concat(swapPicture ?? Array.Empty<uint>()).Concat(titlePicture ?? Array.Empty<uint>()));
        if (swapPicture is not null) pack.SwapPicture = SuperHiRes.Pack(swapPicture, 320, 200, pack.Palette, spriteZeroIsClear: false);
        if (titlePicture is not null) pack.TitlePicture = SuperHiRes.Pack(titlePicture, 320, 200, pack.Palette, spriteZeroIsClear: false);
        pack.SwapText = (swap.TextX, swap.TextY, swap.TextColor.Length > 0 && ColorConverter.ConvertFromString(swap.TextColor) is Color sc ? SuperHiRes.Nearest(pack.Palette, 0xFF000000u | (uint)(sc.R << 16 | sc.G << 8 | sc.B), false) : 0xFFFF);
        if (hud is not null) pack.HudBackground = SuperHiRes.Pack(hud, 320, 200, pack.Palette, spriteZeroIsClear: false);
        if (portraitPanel is not null) pack.PortraitPanel = SuperHiRes.Pack(portraitPanel, pack.PanelRect.Width, pack.PanelRect.Height, pack.Palette, spriteZeroIsClear: false);
        for (int i = 0; i < pack.PortraitLabels.Count; i++)
            if (pack.PortraitLabels[i].Color < 0) pack.PortraitLabels[i] = pack.PortraitLabels[i] with { Color = SuperHiRes.Nearest(pack.Palette, (uint)(-pack.PortraitLabels[i].Color - 1) | 0xFF000000u, false) };
        for (int i = 0; i < pack.DateTexts.Count; i++)
            if (pack.DateTexts[i].Color < 0) pack.DateTexts[i] = pack.DateTexts[i] with { Color = SuperHiRes.Nearest(pack.Palette, (uint)(-pack.DateTexts[i].Color - 1) | 0xFF000000u, false) };
        for (int i = 0; i < pack.Labels.Count; i++)
            if (pack.Labels[i].Color < 0) pack.Labels[i] = pack.Labels[i] with { Color = SuperHiRes.Nearest(pack.Palette, (uint)(-pack.Labels[i].Color - 1) | 0xFF000000u, false) };
        foreach (var px in tilePixels) pack.Tiles.Add(SuperHiRes.Pack(px, 16, 16, pack.Palette, spriteZeroIsClear: false));
        pack.TileFlags.AddRange(flags);
        pack.TileNames.AddRange(tileNames.Select(pack.NameOf));
        foreach (var px in avatar) pack.AvatarFrames.Add(SuperHiRes.Pack(px, 16, 16, pack.Palette, spriteZeroIsClear: true));
        // Ultima 6's blood under a body: the Blood folder's one sprite, each frame a sprite (floor.s).
        if (_spriteDatabase.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.BloodGroupName)?.Sprites.FirstOrDefault(s => s.Name == SpriteDatabase.U6BloodSpriteName) is { Frames.Count: > 0 } bloodSprite)
        {
            int firstBlood = spritePixels.Count;
            foreach (var frame in bloodSprite.Frames.Take(8)) if (File.Exists(frame.FilePath)) spritePixels.Add(FitSprite(frame.FilePath));
            if (spritePixels.Count > firstBlood) pack.Blood = (firstBlood, spritePixels.Count - firstBlood, pack.NameOf(U6Words.Of(_systemSettings.Ultima6, "Blood")));
        }
        // Doors beyond the swing: their opening / closing frames as sprites, double, by itself, not by hand, sounds (door.s).
        foreach (var dd in _objectDatabase.Objects.Where(o => o.Type == ObjectType.Door))
        {
            var ds = dd.Door;
            bool anim = ds.Opening == DoorOpening.Animation;
            int doorSnd(int id) => id >= 0 && soundIndex.TryGetValue(id, out int s) ? s : 0xFFFF;
            if (!anim && !ds.OpensWithNeighbours && !ds.OpensByItself && !ds.NotByHand && ds.Opening != DoorOpening.Slide && doorSnd(ds.OpenSoundResourceId) == 0xFFFF && doorSnd(ds.CloseSoundResourceId) == 0xFFFF) continue;
            int nameNo = pack.NameOf(dd.Name);
            if (pack.Doors.Any(d => d[0] == nameNo)) continue;
            (int First, int Count) Frames(SpriteLink link)
            {
                if (!anim || !link.IsSet || ObjectLooks.Find(_spriteDatabase, link.SpriteGroup, link.SpriteName) is not { Frames.Count: > 0 } sp) return (0, 0);
                int first = spritePixels.Count;
                foreach (var fr in sp.Frames.Take(8)) if (File.Exists(fr.FilePath)) spritePixels.Add(FitSprite(fr.FilePath));
                return (first, spritePixels.Count - first);
            }
            var opening = Frames(ds.OpeningSprite);
            var closing = Frames(ds.ClosingSprite);
            int frames = Math.Max(1, Math.Max(opening.Count, closing.Count));
            pack.Doors.Add(new[] { nameNo, opening.First, opening.Count, closing.First, closing.Count,
                (ds.OpensWithNeighbours ? 1 : 0) | (ds.OpensByItself ? 2 : 0) | (ds.NotByHand || ds.Opening == DoorOpening.Slide ? 4 : 0),
                Math.Clamp(ds.OpensWithinCells, 1, 8), (int)Math.Ceiling(Math.Max(0, ds.StaysOpenMs) / 500.0),
                doorSnd(ds.OpenSoundResourceId), doorSnd(ds.CloseSoundResourceId), Math.Clamp(ds.MotionMs * 60 / 1000 / frames, 1, 60), 0 });
        }
        // Monsters that join: whether they split, their joined look's 32 frames as sprites (join.s).
        foreach (var jc in _characterDatabase.Characters.Where(c => c.Joining.IsSet))
        {
            int jName = pack.NameOf(jc.Name);
            if (pack.Joiners.Any(j => j.Name == jName)) continue;
            int look = 0xFFFF;
            if (jc.Joining.JoinedSprite.SpriteName.Length > 0 && ObjectLooks.Find(_spriteDatabase, jc.Joining.JoinedSprite.SpriteGroup, jc.Joining.JoinedSprite.SpriteName) is { Frames.Count: > 0 } js)
            {
                look = spritePixels.Count;
                for (int f = 0; f < 32; f++) { string? file = js.Frames[Math.Min(f, js.Frames.Count - 1)].FilePath; spritePixels.Add(file is not null && File.Exists(file) ? FitSprite(file) : (new uint[16 * 16], 16, 16)); }
            }
            pack.Joiners.Add((jName, jc.Joining.Growth != JoinGrowth.OurEngine ? 1 : 0, look));
        }
        pack.AvatarSprite = spritePixels.Count; // the Avatar's looks as sprites too (solo mode)
        foreach (var px in avatar.Take(4)) spritePixels.Add((px, 16, 16));
        if (PlayerCharacter() is { } handAvatar) HandPoints(handAvatar, pack.AvatarSprite, sixteen: true);
        // Weapons' held looks (Sprite Editor > Weapon in hand: frame 1), as is and mirrored, their grips (inhand.s).
        foreach (var wd in _objectDatabase.Objects.Where(o => o.Type == ObjectType.Weapon && o.Weapon.InHandSprite.SpriteName.Length > 0))
        {
            int wn = pack.NameOf(wd.Name);
            if (pack.HeldWeapons.Any(h => h.Name == wn)) continue;
            if (ObjectLooks.Find(_spriteDatabase, wd.Weapon.InHandSprite.SpriteGroup, wd.Weapon.InHandSprite.SpriteName)?.Frames.FirstOrDefault() is not { } held || !File.Exists(held.FilePath)) continue;
            int heldSprite = spritePixels.Count;
            spritePixels.Add(FitSprite(held.FilePath));
            spritePixels.Add(FitSpriteMirrored(held.FilePath));
            var (gw, gh) = (spritePixels[heldSprite].W, spritePixels[heldSprite].H);
            var grip = held.GripX >= 0 && held.GripY >= 0 ? FitPoint(held.FilePath, held.GripX, held.GripY) : (0, -gh / 2);
            pack.HeldWeapons.Add((wn, heldSprite, heldSprite + 1, grip.Item1, grip.Item2));
        }
        foreach (var (px, w, h) in spritePixels) pack.Sprites.Add((w, h, SuperHiRes.Pack(px, w, h, pack.Palette, spriteZeroIsClear: true)));
        // Plants', tools', stations' and instruments' records (Engine/AppleIIgs.cs Specials); carried ones get their code too.
        {
            var toolKinds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int Kind(string k) { k = k.Trim(); if (k.Length == 0) return 0; if (!toolKinds.TryGetValue(k, out int v)) toolKinds[k] = v = Math.Min(255, toolKinds.Count + 1); return v; }
            int Snd(int id) => id >= 0 && soundIndex.TryGetValue(id, out int sn) ? sn : 0xFFFF;
            int LinkSprite(SpriteLink l) => l.IsSet && ObjectLooks.Find(_spriteDatabase, l.SpriteGroup, l.SpriteName)?.Frames.FirstOrDefault()?.FilePath is { } lf && SpriteOf(lf) is int ls and >= 0 ? ls : 0xFFFF;
            void ItemDef(List<byte> b, int objectId)
            {
                var it = objectId >= 0 ? talkEnv.Item("Object", objectId.ToString()) : null;
                foreach (int v in it is { } t ? new[] { t.Name, t.Sprite, t.Icon, t.Flags, t.Other } : new[] { 0xFFFF, 0, 0, 0, 0xFFFF }) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
            }
            void W(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
            for (int si = 0; si < specialDefs.Count; si++)
            {
                var d = specialDefs[si];
                var b = new List<byte>();
                switch (d.Type)
                {
                    case ObjectType.Plant:
                    {
                        b.Add(1);
                        var stages = d.Plant.Stages.Take(4).ToList();
                        b.Add((byte)Math.Max(1, stages.Count));
                        for (int k = 0; k < 4; k++)
                        {
                            var st = ObjectLooks.SpriteFor(d, ObjectLooks.DefaultState(d) with { Stage = Math.Min(k, Math.Max(0, stages.Count - 1)) }, _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath;
                            W(b, SpriteOf(st) is int ss and >= 0 ? ss : 0);
                        }
                        W(b, (int)Math.Clamp(Math.Round(d.Plant.HoursPerStage), 1, 255));
                        ItemDef(b, d.Plant.ProduceObjectId);
                        W(b, Math.Clamp(d.Plant.ProduceQuantity, 1, 20));
                        b.Add((byte)Kind(d.Plant.HarvestToolKind)); b.Add((byte)(d.Plant.OnlyWhenRipe ? 1 : 0));
                        b.Add((byte)Math.Clamp(d.Plant.StageAfterHarvest, 0, 3)); b.Add((byte)(d.Plant.RemovedWhenHarvested ? 1 : 0));
                        W(b, Snd(d.Plant.HarvestSoundResourceId));
                        ItemDef(b, d.Plant.SoilObjectId);
                        // (sowing: the seed's name, the plant's own name)
                        W(b, d.Plant.SeedObjectId >= 0 && _objectDatabase.Objects.FirstOrDefault(o => o.Id == d.Plant.SeedObjectId) is { } seed ? pack.NameOf(seed.Name) : 0xFFFF);
                        W(b, pack.NameOf(d.Name));
                        break;
                    }
                    case ObjectType.Tool:
                        b.Add(2); b.Add((byte)Kind(d.Tool.ToolKind)); W(b, Snd(d.Tool.UseSoundResourceId));
                        break;
                    case ObjectType.CraftingStation:
                    {
                        b.Add(3);
                        W(b, LinkSprite(d.Crafting.WorkingSprite));
                        W(b, d.Crafting.Hint.Trim().Length > 0 ? pack.NameOf(d.Crafting.Hint.Trim()) : 0xFFFF);
                        var recipes = d.Crafting.Recipes.Where(r => r.OutputObjectId >= 0).Take(8).ToList();
                        b.Add((byte)recipes.Count);
                        foreach (var r in recipes)
                        {
                            b.Add((byte)Kind(r.ToolKind));
                            var ins = r.Inputs.Where(i => i.ObjectId >= 0).Take(4).ToList();
                            b.Add((byte)ins.Count);
                            foreach (var i in ins) { W(b, pack.NameOf(_objectDatabase.Objects.FirstOrDefault(o => o.Id == i.ObjectId)?.Name ?? "?")); b.Add((byte)Math.Clamp(i.Quantity, 1, 99)); }
                            ItemDef(b, r.OutputObjectId);
                            b.Add((byte)Math.Clamp(r.OutputQuantity, 1, 20));
                            W(b, (int)Math.Clamp(Math.Round(r.WorkSeconds * 60), 1, 3600));
                            W(b, Snd(r.WorkSoundResourceId));
                        }
                        break;
                    }
                    case ObjectType.UseableObject:
                        b.Add(5); W(b, itemSpell[d.Id]); W(b, Math.Clamp(d.Useable.Charges, 0, 9999)); b.Add((byte)(d.Useable.RemovedWhenUsedUp ? 1 : 0));
                        b.Add((byte)(d.Useable.AsksForTarget ? 1 : 0)); W(b, LinkSprite(d.Useable.UsedUpSprite)); W(b, Snd(d.Useable.UseSoundResourceId));
                        break;
                    case ObjectType.Potion:
                        b.Add(6); W(b, itemSpell[d.Id]); W(b, Snd(d.Potion.DrinkSoundResourceId)); ItemDef(b, d.Potion.EmptyObjectId);
                        break;
                    case ObjectType.MusicalInstrument:
                    {
                        b.Add(4);
                        var songs = d.Instrument.SongSoundIds.Select(Snd).Where(n => n != 0xFFFF).Take(4).ToList();
                        b.Add((byte)songs.Count);
                        foreach (int n in songs) W(b, n);
                        break;
                    }
                }
                pack.Specials.Add(b.ToArray());
            }
            // Carried (or in things) tools and instruments: their code by their name.
            var byName = new Dictionary<int, int>();
            foreach (var d in specialDefs.Where(d => d.Type is ObjectType.Tool or ObjectType.MusicalInstrument or ObjectType.UseableObject or ObjectType.Potion)) byName.TryAdd(pack.NameOf(d.Name), 0xD000 | specialOf[d.Id]);
            for (int oi = 0; oi < objects.Count; oi++)
                if (objects[oi].Other == 0xFFFF && byName.TryGetValue(objects[oi].Name, out int nsc)) objects[oi] = objects[oi] with { Other = nsc };
        }
        foreach (var (lw, li, ld, lat, lother, lon) in pendingLevers)
        {
            int Snd(int id) => id >= 0 && soundIndex.TryGetValue(id, out int s) ? s : 0xFFFF;
            var links = li.LinkedInstanceIds.Select(id => instanceAt.TryGetValue((lw, id), out int at) ? at : -1).Where(at => at >= 0).Take(4).ToArray();
            pack.Levers.Add((lat, (int)ld.Lever.Kind, lother, Snd(ld.Lever.OnSoundResourceId), Snd(ld.Lever.OffSoundResourceId),
                (int)Math.Ceiling(Math.Max(0, ld.Lever.ButtonResetSeconds) * 2), lon, links));
        }
        // Spare objects (gone, no name) that Give Item fills in at play.
        for (int i = 0; i < 48; i++) objects.Add((0xFFFF, 0xFFFF, 0, 32, 0xFFFF, 0xFFFF, 0xFFFF, -1));
        pack.Objects.AddRange(objects);
        pack.Characters.AddRange(characters);
        pack.PortraitRect = portraitRect;
        if (carriedPiece is not null && cell > 0) pack.CarriedGrid = (carriedPiece.X, carriedPiece.Y, Math.Clamp(carriedPiece.Columns, 1, 16), Math.Clamp(carriedPiece.Rows, 1, 12), cell);
        if (design.First(HudPieceKind.MemberName) is { } namePiece) pack.NameRect = (namePiece.X & ~1, namePiece.Y, Math.Min(320 - (namePiece.X & ~1), (namePiece.Width + 3) / 4 * 4), namePiece.Height);
        for (int i = 0; i < AppleIIgsGamePack.WordIds.Length; i++) pack.Words[i] = U6Words.Of(_systemSettings.Ultima6, AppleIIgsGamePack.WordIds[i]);
        pack.Words[^1] = PlayerCharacter()?.Name ?? "Avatar";
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
        // Kept for the Colour Preview and the check (the 24-bit pictures; GAME.DAT gets only the 16-colour ones).
        pack.Preview = new AppleIIgsPreviewData { Hud = hud, Title = titlePicture };
        pack.Preview.Tiles.AddRange(tilePixels);
        pack.Preview.Sprites.AddRange(spritePixels.Select(sp => (sp.Px, sp.W, sp.H)));
        pack.Preview.Avatar.AddRange(avatar);
        pack.Preview.MapNames.AddRange(worldList.Select(m => m.MapName));
        foreach (var ((wi, f), _) in floorMap.OrderBy(kv => kv.Value)) pack.Preview.MapNames.Add($"{worldList[wi].MapName}, floor {f}");
        // Maps that wrap around (their upper floors too) and the things' weights by name (moves.s).
        for (int wi = 0; wi < worldList.Count; wi++)
            if (worldList[wi].WrapsAround)
            {
                pack.WrapMaps.Add(wi);
                foreach (var ((fw, _), fm) in floorMap) if (fw == wi) pack.WrapMaps.Add(fm);
            }
        var weighed = new HashSet<int>();
        void Weigh(string name, ReferencedItemCategory category, int id)
        {
            int n = pack.Names.IndexOf(name);
            if (n < 0 || !weighed.Add(n)) return;
            double kg = ItemWeights.OwnWeight(category, id, _containerDatabase, _bookDatabase, _objectDatabase);
            pack.Weights.Add((n, (int)Math.Round(kg * 10), ItemWeights.WeighsOnlyItself(category, id, _containerDatabase)));
        }
        foreach (var o in _objectDatabase.Objects) Weigh(o.Name, ReferencedItemCategory.Object, o.Id);
        // Cold and hot zones (each map's, its upper floors' too), worn gear's warmth and cooling, the immune (temp.s).
        void TempZonesOf(MapData world, int mapNo, int floor)
        {
            foreach (var z in world.Zones.Where(z => z.Floor == floor))
            {
                int kind = SandcastleRpgMaker.Engine.Zones.IsCold(new[] { z }, z.X, z.Y, floor) ? 1 : SandcastleRpgMaker.Engine.Zones.IsHot(new[] { z }, z.X, z.Y, floor) ? 2 : 0;
                if (kind > 0) pack.TempZones.Add((mapNo, z.X, z.Y, z.Width, z.Height, kind));
            }
        }
        for (int wi = 0; wi < worldList.Count; wi++) TempZonesOf(worldList[wi], wi, 1);
        foreach (var ((wi, f), mi) in floorMap) TempZonesOf(worldList[wi], mi, f);
        foreach (var o in _objectDatabase.Objects.Where(o => o.Type == ObjectType.Armor && (o.Armor.Warmth != 0 || o.Armor.Cooling != 0)))
            if (pack.Names.IndexOf(o.Name) is int gn and >= 0 && pack.Gear.All(g => g.Name != gn)) pack.Gear.Add((gn, o.Armor.Warmth, o.Armor.Cooling));
        foreach (var c in _characterDatabase.Characters.Where(c => c.Survival.ColdImmune || c.Survival.HeatImmune))
            if (pack.Names.IndexOf(c.Name) is int cn and >= 0) pack.TempImmune.Add((cn, (c.Survival.ColdImmune ? 1 : 0) | (c.Survival.HeatImmune ? 2 : 0)));
        // Magic worn or held: reach, defense, light, strength / dexterity / intelligence by name (worn.s).
        foreach (var o in _objectDatabase.Objects.Where(o => o.Type is ObjectType.Weapon or ObjectType.Armor && !o.Magic.IsEmpty))
        {
            int mn = pack.NameOf(o.Name);
            if (pack.WornMagic.Any(m => m[0] == mn)) continue;
            var mg = o.Magic;
            int reach = mg.ReachCells > 1 ? mg.ReachCells : 0, def = mg.DefenseBonus, light = mg.LightBrightness > 0 ? Math.Clamp(1 + mg.LightBrightness / 2, 1, 7) : 0, str = 0, dex = 0, intel = 0;
            foreach (var b in mg.StatBonuses)
                switch (b.Kind)
                {
                    case MagicBonusKind.Defense: def += b.Amount; break;
                    case MagicBonusKind.Reach: reach = Math.Max(reach, b.Amount); break;
                    case MagicBonusKind.Light: light = Math.Max(light, Math.Clamp(1 + b.Amount / 2, 1, 7)); break;
                    case MagicBonusKind.Stat:
                        var kind = _statDatabase.Stats.FirstOrDefault(s => s.Id == b.StatId)?.Kind;
                        if (kind == BuiltInStatKind.Strength) str += b.Amount;
                        else if (kind == BuiltInStatKind.Dexterity) dex += b.Amount;
                        else if (kind == BuiltInStatKind.Intelligence) intel += b.Amount;
                        break;
                }
            if (reach > 0 || def != 0 || light > 0 || str != 0 || dex != 0 || intel != 0)
                pack.WornMagic.Add(new[] { mn, reach, def & 0xFFFF, light, str & 0xFFFF, dex & 0xFFFF, intel & 0xFFFF, 0 });
        }
        // Factions: the Avatar's, the bands, each faction's relationship to the Avatar's, each character's faction (fac.s).
        if (avatarDef is not null && avatarDef.FactionId >= 0)
        {
            pack.AvatarFaction = avatarDef.FactionId;
            int end = 0;
            foreach (var band in _factionDatabase.Bands) { end += band.WidthPercent; pack.FactionBands.Add((end, band.Behavior == FactionBehavior.Attack)); }
            foreach (var f in _factionDatabase.Factions) pack.Factions.Add((f.Id, Math.Clamp(_factionDatabase.GetRelation(f.Id, avatarDef.FactionId), 0, 100)));
            foreach (var c in _characterDatabase.Characters.Where(c => c.FactionId >= 0 && c != avatarDef))
                if (pack.CharFactions.All(x => x.Name != pack.NameOf(c.Name))) pack.CharFactions.Add((pack.NameOf(c.Name), c.FactionId));
        }
        // Damage types: weapons' types, characters' responses, those who get back up (tough.s).
        foreach (var o in _objectDatabase.Objects.Where(o => o.Type == ObjectType.Weapon && o.Weapon.DamageType != DamageType.Normal))
            if (pack.WeaponTypes.All(t => t.Name != pack.NameOf(o.Name))) pack.WeaponTypes.Add((pack.NameOf(o.Name), (int)o.Weapon.DamageType));
        foreach (var c in _characterDatabase.Characters)
        {
            int cn = pack.NameOf(c.Name);
            foreach (var r in c.DamageResponses.Where(r => r.Response != DamageResponseKind.Normal))
                if (pack.Responses.All(x => x.Name != cn || x.Type != (int)r.Type))
                    pack.Responses.Add((cn, (int)r.Type, r.Response switch { DamageResponseKind.Immune => 1, DamageResponseKind.Half => 2, _ => 3 }, r.Amount & 0xFFFF));
            if (c.GetsBackUp && !c.Body.NoBody && pack.Risers.All(x => x.Name != cn))
            {
                int least = Math.Max(1, c.BackUpMinSeconds * 2), most = Math.Max(least, c.BackUpMaxSeconds * 2);
                pack.Risers.Add((cn, least, most, c.BackUpNotFrom.Aggregate(0, (m, t) => m | (1 << (int)t))));
            }
        }
        // Ground traps and lights that burn down or burn, by name (floor.s).
        foreach (var o in _objectDatabase.Objects)
        {
            int n = pack.Names.IndexOf(o.Name);
            if (n < 0) continue;
            if (o.Type == ObjectType.Trap && pack.Traps.All(t => t[0] != n))
            {
                var t = o.Trap;
                int ail = t.Ailment switch { TrapAilment.Poisoned => 1, TrapAilment.Asleep => 2, TrapAilment.Paralyzed => 4, _ => 0 };
                int soundId = t.Kind == TrapKind.Explosive && t.ExplosionSoundResourceId >= 0 ? t.ExplosionSoundResourceId : t.TriggerSoundResourceId;
                int sound = soundId >= 0 && soundIndex.TryGetValue(soundId, out int sn) ? sn : 0xFFFF;
                int kind = t.Kind switch { TrapKind.Explosive => 1, TrapKind.Shooting => 2, _ => 0 };
                int reach = Math.Max(1, (int)Math.Round((t.Kind == TrapKind.Explosive ? t.RadiusTiles : t.RangeTiles) / 2.0)); // 8px tiles -> 16px cells
                int missile = 0xFFFF;
                if (t.Kind == TrapKind.Shooting && t.ProjectileObjectId >= 0 && _objectDatabase.Objects.FirstOrDefault(p => p.Id == t.ProjectileObjectId) is { } projectile)
                    missile = SpriteOf(ObjectLooks.SpriteFor(projectile, new ObjectState(false, false, false, false, 1, SpriteDirection.South), _spriteDatabase)?.Frames.FirstOrDefault()?.FilePath ?? projectile.IconPath) is int ms and >= 0 ? ms : 0xFFFF;
                pack.Traps.Add(new[] { n, Math.Max(0, Math.Min(t.DamageMin, t.DamageMax)), Math.Max(0, Math.Max(t.DamageMin, t.DamageMax)), ail,
                    (t.Hidden ? 1 : 0) | (t.OneShot ? 2 : 0), t.OneShot ? 0 : (int)Math.Ceiling(Math.Max(0, t.RearmSeconds) * 2), sound,
                    kind, reach, missile, t.Kind == TrapKind.Shooting && t.TriggeredBy != TrapTrigger.StepOn ? 1 : 0, t.Kind == TrapKind.Explosive && t.RemovedAfterExplosion ? 1 : 0 });
            }
            if (o.Type == ObjectType.LightSource && (o.Light.BurnMinutes > 0 || o.Light.BurnsWhenLit) && pack.Lights.All(l => l.Name != n))
                pack.Lights.Add((n, Math.Clamp(o.Light.BurnMinutes, 0, 65535), o.Light.BurnsWhenLit ? Math.Max(0, Math.Min(o.Light.BurnDamageMin, o.Light.BurnDamageMax)) : 0,
                    o.Light.BurnsWhenLit ? Math.Max(1, Math.Max(o.Light.BurnDamageMin, o.Light.BurnDamageMax)) : 0));
        }
        foreach (var c in _containerDatabase.Containers) Weigh(c.Name, ReferencedItemCategory.Container, c.Id);
        foreach (var b in _bookDatabase.Books) Weigh(b.Name, ReferencedItemCategory.Book, b.Id);
        pack.MapWidth = start.Width; pack.MapHeight = start.Height; pack.Map = map;
        pack.StartX = start.StartX >= 0 && start.StartX < start.Width ? start.StartX : start.Width / 2;
        pack.StartY = start.StartY >= 0 && start.StartY < start.Height ? start.StartY : start.Height / 2;
        return pack;
    }

    /// <summary>
    /// The HUD (HUD Editor) as the IIgs shows it at the start: its frame, pictures, buttons, dials and the inventory view's
    /// body slots composed into one 320 x 200 background; where the Game View and the text area are; the member's name as
    /// words. Null: no HUD (no Game View) - the world shows in the middle of the screen. Label colours are -1 - RGB
    /// until the palette is made (then 0-15), or $FFFF for the brightest.
    /// </summary>
    private uint[]? ComposeHud(AppleIIgsGamePack pack, out uint[]? portraitPanel)
    {
        portraitPanel = null;
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
        var player = PlayerCharacter();
        // The inventory view (as the game starts), then the portrait view - only its panel is kept.
        Paint(portraitView: false);
        var main = canvas;
        var panelPieces = design.Pieces.Where(p => p.Kind == HudPieceKind.Panel).ToList();
        if (panelPieces.Count == 0) panelPieces = design.Pieces.Where(p => HudPiece.IsPanelContent(p.Kind) || p.Kind == HudPieceKind.MemberName).ToList();
        if (panelPieces.Count > 0)
        {
            int left = panelPieces.Min(p => p.X) & ~1, top = panelPieces.Min(p => p.Y);
            int right = Math.Min(320, panelPieces.Max(p => p.X + p.Width)), bottom = Math.Min(200, panelPieces.Max(p => p.Y + p.Height));
            int width = Math.Max(2, (right - left + 1) & ~1), height = Math.Max(1, bottom - top);
            if (left + width > 320) width = (320 - left) & ~1;
            pack.PanelRect = (left, top, width, height);
            canvas = new uint[320 * 200];
            Paint(portraitView: true);
            portraitPanel = new uint[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    portraitPanel[y * width + x] = canvas[(top + y) * 320 + left + x];
            canvas = main;
        }
        int columns = Math.Clamp(view.Width / AppleIIgsLimits.TileSize, 1, 20), rows = Math.Clamp(view.Height / AppleIIgsLimits.TileSize, 1, 12);
        pack.View = (Math.Clamp(view.X & ~1, 0, 320 - columns * 16), Math.Clamp(view.Y, 0, 200 - rows * 16), columns, rows);
        if (design.First(HudPieceKind.TextArea) is { } text) pack.TextArea = (text.X, text.Y, text.Width, text.Height);
        return canvas;

        void Paint(bool portraitView)
        {
        Draw(Of(design.Frame), 0, 0, 320, 200, fit: false);
        var front = new List<HudPiece>();
        foreach (var piece in design.Pieces)
        {
            var only = HudPiece.ViewOf(piece.Kind);
            if (only != HudPanelView.Always && only != (portraitView ? HudPanelView.Portrait : HudPanelView.Inventory)) continue;
            switch (piece.Kind)
            {
                case HudPieceKind.Picture or HudPieceKind.Panel:
                    if (piece.Kind == HudPieceKind.Picture && piece.InFront) front.Add(piece);
                    else Draw(Of(piece.Sprite), piece.X, piece.Y, piece.Width, piece.Height, fit: false);
                    break;
                case HudPieceKind.CommandButton or HudPieceKind.CustomButton or HudPieceKind.Compass or HudPieceKind.SunMoon:
                    Draw(Of(piece.Sprite), piece.X, piece.Y, piece.Width, piece.Height, fit: false);
                    if (piece.Kind is HudPieceKind.Compass or HudPieceKind.SunMoon && piece.Sprite.IsSet && ObjectLooks.Find(_spriteDatabase, piece.Sprite.SpriteGroup, piece.Sprite.SpriteName) is { Frames.Count: > 1 } dial)
                    {
                        // Its frames, turned by the player: the compass by the wind, the sun and moons by the hour.
                        int dx = piece.X & ~1, dw = Math.Min(320 - dx, (piece.Width + 3) / 4 * 4), dh = Math.Min(200 - piece.Y, piece.Height);
                        var frames = dial.Frames.Take(24).Where(f => File.Exists(f.FilePath)).Select(f => FitPicture(f.FilePath, dw, dh)).ToList();
                        if (frames.Count > 1) pack.PendingDials.Add((piece.Kind == HudPieceKind.Compass ? 0 : 1, dx, piece.Y, dw, dh, frames));
                    }
                    break;
                case HudPieceKind.DateText:
                    pack.DateTexts.Add((piece.X, piece.Y, LabelColor(piece.TextColor)));
                    break;
                case HudPieceKind.BodySlot: // the inventory view: what the Avatar wears there, or the slot's own picture
                {
                    Draw(Of(piece.Sprite), piece.X, piece.Y, piece.Width, piece.Height, fit: true); // empty: the player draws what is worn there
                    pack.BodySlots.Add(((int)piece.Slot + 1, piece.X, piece.Y));
                    break;
                }
                case HudPieceKind.CarriedItems: // its empty cells (the things carried are drawn by the player)
                {
                    int size = Math.Clamp(piece.CellSize / 4 * 4, 4, 32);
                    for (int r = 0; r < piece.Rows; r++)
                        for (int c = 0; c < piece.Columns; c++)
                            Draw(Of(piece.Sprite), (piece.X & ~1) + c * size, piece.Y + r * size, size, size, fit: false);
                    break;
                }
                case HudPieceKind.MemberName when player is not null && !portraitView:
                    pack.Labels.Add((piece.X, piece.Y, LabelColor(piece.TextColor), player.Name));
                    break;
                case HudPieceKind.Portrait when player is not null: // the portrait view: the member's face
                {
                    var face = player.FaceSpriteName.Length == 0 ? null : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == player.FaceSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == player.FaceSpriteName)
                        ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == player.FaceSpriteName);
                    Draw(face, piece.X, piece.Y, piece.Width, piece.Height, fit: true);
                    break;
                }
                case HudPieceKind.Stats when player is not null: // the portrait view: "Name value", a line each
                {
                    int line = 0;
                    foreach (var stat in piece.StatIds.Count > 0 ? _statDatabase.Stats.Where(s => piece.StatIds.Contains(s.Id)) : _statDatabase.Stats)
                    {
                        if ((line + 1) * 10 > Math.Max(10, piece.Height)) break;
                        string value = player.StatValues.TryGetValue(stat.Id, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "0";
                        // The built-in stats change in play: the player writes their value now (~ and the stat's letter).
                        int? live = stat.Kind switch
                        {
                            BuiltInStatKind.Hits => AppleIIgsGamePack.Stat.Hp, BuiltInStatKind.Strength => AppleIIgsGamePack.Stat.Str,
                            BuiltInStatKind.Dexterity => AppleIIgsGamePack.Stat.Dex, BuiltInStatKind.Intelligence => AppleIIgsGamePack.Stat.Int,
                            BuiltInStatKind.Combat => AppleIIgsGamePack.Stat.Combat, BuiltInStatKind.Magic => AppleIIgsGamePack.Stat.Magic,
                            BuiltInStatKind.Mana => AppleIIgsGamePack.Stat.Mana, BuiltInStatKind.Level => AppleIIgsGamePack.Stat.Level,
                            BuiltInStatKind.Exp => AppleIIgsGamePack.Stat.Exp, BuiltInStatKind.NextExp => 25, _ => null
                        };
                        if (live is int code) value = "~" + (char)('A' + code);
                        pack.PortraitLabels.Add((piece.X, piece.Y + line * 10, LabelColor(piece.TextColor), $"{stat.Name} {value}"));
                        line++;
                    }
                    break;
                }
            }
        }
        foreach (var piece in front) Draw(Of(piece.Sprite), piece.X, piece.Y, piece.Width, piece.Height, fit: false);
        }

        static int LabelColor(string color)
        {
            try { if (color.Length > 0 && ColorConverter.ConvertFromString(color) is Color c) return -1 - (c.R << 16 | c.G << 8 | c.B); } catch { }
            return 0xFFFF;
        }
    }

    /// <summary>The player (an S16 GS/OS application: aux $DB03, desktop and GS/OS aware).</summary>
    private static (string Name, byte FileType, ushort AuxType, byte[] Data) IIgsPlayerFile()
    {
        string player = Path.Combine(IIgsRuntimeFolder, IIgsPlayerName);
        if (!File.Exists(player)) throw new FileNotFoundException("The Apple IIgs player isn't installed with the editor (Runtimes\\AppleIIgs\\SANDCASTLE).");
        return (IIgsPlayerName, 0xB3, 0xDB03, File.ReadAllBytes(player));
    }

    /// <summary>The game's disk: the player, GAME.DAT and the sounds (SND.1...) on a ProDOS volume, as a .2mg (or plain blocks).</summary>
    private byte[] MakeAppleIIgsDisk(bool twoMg, out string volumeName) => MakeAppleIIgsDisk(MakeAppleIIgsPack(), twoMg, out volumeName);

    private byte[] MakeAppleIIgsDisk(AppleIIgsGamePack pack, bool twoMg, out string volumeName)
    {
        var volume = new ProDosVolume(_currentProjectName ?? "GAME");
        volumeName = volume.VolumeName;
        foreach (var f in pack.Files().Prepend(IIgsPlayerFile())) volume.Add(f.Name, f.FileType, f.AuxType, f.Data);
        var blocks = volume.Build();
        return twoMg ? ProDosVolume.Wrap2mg(blocks) : blocks;
    }

    private void BuildAppleIIgsGame()
    {
        if (!ShowAppleIIgsCheck(onlyIfProblems: true)) return;
        if (MessageBox.Show(this, "Building saves the project first, so the game is exactly what you have now. Continue?", "Build", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        if (!SaveProject()) return;
        string game = _currentProjectName is { Length: > 0 } n ? n : "Game";
        AppleIIgsGamePack pack;
        List<List<(string Name, byte FileType, ushort AuxType, byte[] Data)>> floppies;
        (string Name, byte FileType, ushort AuxType, byte[] Data) player;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            pack = MakeAppleIIgsPack();
            player = IIgsPlayerFile();
            var files = pack.Files();
            try { floppies = AppleIIgsFloppies.Spread(new() { player, files[0], ("DISKS.DAT", 0x06, 0x0000, new byte[4096]) }, files.Skip(1).ToList()); } // DISKS.DAT: room kept, written once the disks are named
            catch { floppies = new(); }
        }
        catch (Exception ex) { MessageBox.Show(this, $"The game could not be made: {ex.Message}", "Build", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        finally { Mouse.OverrideCursor = null; }
        long bytes = pack.Files().Sum(f => (long)f.Data.Length) + player.Data.Length;
        var swap = _systemSettings.Ultima6.Hud.DiskSwap;
        var ask = new AppleIIgsBuildWindow(bytes, floppies.Count, swap.DiskNames, game) { Owner = this };
        if (ask.ShowDialog() != true) return;
        try
        {
            if (ask.Kind == AppleIIgsDiskKind.Floppies)
            {
                swap.DiskNames = ask.DiskNames.Take(floppies.Count).ToList(); // kept with the project (HUD Editor > Disk Swap screen)
                using var folderPick = new System.Windows.Forms.FolderBrowserDialog { Description = $"Where should the {floppies.Count} floppy images go?" };
                if (folderPick.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                Mouse.OverrideCursor = Cursors.Wait;
                var written = new List<string>();
                var volumeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var diskList = new List<(string Volume, string Shown)>();
                for (int d = 0; d < floppies.Count; d++)
                {
                    string name = swap.NameOf(d + 1, game);
                    string volumeName = ProDosVolume.CleanName(name, $"DISK{d + 1}");
                    for (int k = 2; !volumeNames.Add(volumeName); k++) volumeName = ProDosVolume.CleanName(name, "DISK")[..Math.Min(13, ProDosVolume.CleanName(name, "DISK").Length)] + k;
                    diskList.Add((volumeName, name));
                }
                // Which file is on which disk: DISKS.DAT, on disk 1 in the room kept for it.
                var where = floppies.SelectMany((files, d) => files.Select(f => (File: f.Name, Disk: d + 1))).Where(f => f.File != "DISKS.DAT").ToList();
                var disksDat = AppleIIgsDisks.Write(diskList, where);
                for (int d = 0; d < floppies.Count; d++)
                {
                    string name = diskList[d].Shown, volumeName = diskList[d].Volume;
                    var volume = new ProDosVolume(volumeName);
                    foreach (var f in floppies[d]) volume.Add(f.Name, f.FileType, f.AuxType, f.Name == "DISKS.DAT" ? disksDat : f.Data);
                    string file = Path.Combine(folderPick.SelectedPath, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".2mg");
                    File.WriteAllBytes(file, ProDosVolume.Wrap2mg(volume.Build(AppleIIgsFloppies.Blocks)));
                    written.Add($"{Path.GetFileName(file)}  (disk \"{volumeName}\": {string.Join(", ", floppies[d].Select(f => f.Name))})");
                }
                StatusText.Text = $"Apple IIgs floppies built: {floppies.Count} in {folderPick.SelectedPath}";
                MessageBox.Show(this, $"Your game's {floppies.Count} floppies were built:\n\n{string.Join("\n", written)}\n\nStart GS/OS from its own disk, put in disk 1 and double-click {IIgsPlayerName}. Or copy every disk's files into one folder of a hard disk: the game runs from there and never asks for a disk.", "Build", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool hfs = ask.Kind == AppleIIgsDiskKind.HfsHardDisk;
            var pick = new SaveFileDialog
            {
                Title = "Where should the Apple IIgs disk be built?",
                Filter = hfs ? "Apple IIgs hard disk image, HFS (*.2mg)|*.2mg|Hard disk image (*.hdv)|*.hdv"
                             : "Apple IIgs hard disk image (*.2mg)|*.2mg|ProDOS-order image (*.po)|*.po|Hard disk image (*.hdv)|*.hdv",
                FileName = ProDosVolume.CleanName(game, "GAME") + ".2mg",
                OverwritePrompt = true
            };
            if (pick.ShowDialog(this) != true) return;
            Mouse.OverrideCursor = Cursors.Wait;
            bool twoMg = Path.GetExtension(pick.FileName).Equals(".2mg", StringComparison.OrdinalIgnoreCase);
            string shownName;
            byte[] disk;
            if (hfs)
            {
                var volume = new HfsVolume(game);
                foreach (var f in pack.Files().Prepend(player)) volume.Add(f.Name, f.FileType, f.AuxType, f.Data);
                var blocks = volume.Build();
                disk = twoMg ? ProDosVolume.Wrap2mg(blocks) : blocks;
                shownName = volume.VolumeName;
            }
            else disk = MakeAppleIIgsDisk(pack, twoMg, out shownName);
            File.WriteAllBytes(pick.FileName, disk);
            StatusText.Text = $"Apple IIgs disk built: {pick.FileName}";
            MessageBox.Show(this, $"Your game's disk was built ({(hfs ? "HFS" : "ProDOS")}):\n{pick.FileName}\n\nOn the Apple IIgs (GS/OS 6, an accelerator), or in KEGS as a second hard disk: open the disk \"{shownName}\" on the desktop and double-click {IIgsPlayerName}.", "Build", MessageBoxButton.OK, MessageBoxImage.Information);
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
