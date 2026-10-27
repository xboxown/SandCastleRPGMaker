using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Project-wide System Settings. Mouse Pointers: the engine's own pointer
/// graphics (Exult's set, minus the hourglass), each linked to a Sprite
/// Editor sprite. All pointers share one pixel size - choose it at the top,
/// and every pointer's list then offers only sprites of exactly that size.
/// Each pointer has a small black preview that plays the sprite exactly as
/// the game will: animated, with any per-frame sound.
/// </summary>
public partial class SystemSettingsWindow : Window
{
    private sealed record PointerChoice(string Group, string Name, string Display);
    private sealed record SizeChoice(int Width, int Height, string Display);

    private static readonly PointerChoice NoSprite = new("", "", "(none - shows a black square)");

    private readonly SystemSettings _settings;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private readonly ComboBox _sizeCombo = new() { Width = 220, DisplayMemberPath = nameof(SizeChoice.Display) };
    private readonly Dictionary<MousePointerKind, (ComboBox Combo, Image Preview)> _rows = new();
    private readonly Dictionary<MousePointerKind, (TextBlock Info, Button Edit)> _spriteDetails = new();
    /// <summary>Where each pointer's play/stop button goes, under its preview.</summary>
    private readonly Dictionary<MousePointerKind, ContentControl> _playHolders = new();
    /// <summary>Every sprite with its frame size, measured once when the window opens.</summary>
    private readonly List<(string Group, SpriteEntry Sprite, int Width, int Height)> _measured = new();
    private bool _suppress;

    public SystemSettingsWindow(SystemSettings settings, SpriteDatabase sprites, ResourceDatabase resources, string? startTab = null)
    {
        _settings = settings;
        _sprites = sprites;
        _resources = resources;
        Title = $"System Settings [{BuildInfo.CurrentBuild}]";
        Width = 700;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var group in _sprites.Groups)
            foreach (var sprite in group.Sprites)
                if (MousePointerCatalog.PixelSize(sprite) is { } size)
                    _measured.Add((group.Name, sprite, size.Width, size.Height));

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Mouse Pointers", Content = BuildMousePointersTab() });
        tabs.Items.Add(new TabItem { Header = "Game Clock", Content = BuildClockTab() });
        tabs.Items.Add(new TabItem { Header = "Day & Night", Content = BuildDayNightTab() });
        tabs.Items.Add(new TabItem { Header = "Body Container", Content = BuildBodyContainerTab() });
        tabs.Items.Add(new TabItem { Header = "Combat", Content = BuildCombatTab() });
        tabs.Items.Add(new TabItem { Header = "Conversations", Content = BuildConversationTab() });
        tabs.Items.Add(new TabItem { Header = "Sounds", Content = BuildSoundsTab() });
        tabs.Items.Add(new TabItem { Header = "Save / Load", Content = BuildSaveLoadTab() });
        tabs.Items.Add(new TabItem { Header = "Errors", Content = BuildErrorsTab() });
        tabs.Items.Add(new TabItem { Header = "Title Screen", Content = BuildTitleTab() });
        tabs.Items.Add(new TabItem { Header = "Game Window", Content = BuildGameWindowTab() });
        tabs.Items.Add(new TabItem { Header = "Autosave", Content = BuildAutoSaveTab() });
        tabs.Items.Add(new TabItem { Header = "Languages", Content = BuildLanguagesTab() });
        _sliderTabHost = new ContentControl { Content = BuildSliderStyleChooser() }; // Classic (Ultima VII) or Custom
        tabs.Items.Add(new TabItem { Header = "Slider", Content = _sliderTabHost });

        if (startTab is not null) tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => (t.Header as string) == startTab);
        var close = new Button { Content = "Close", Width = 80, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(tabs);
        Content = root;

        Closed += (_, _) => { AnimationPreviewButton.StopAll(); SoundPlayback.StopFor(this); }; // a combat music preview ends with the window
        LoadSizes();
    }

    private sealed record DollChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>Which PaperDoll Editor paperdoll is the in-game Save/Load screen.</summary>
    private UIElement BuildSaveLoadTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        var designs = (ProjectSession.ObjectsProvider?.Invoke()?.DollDesigns ?? new List<DollDesign>()).Select(d => new DollChoice(d.Id, d.Name)).ToList();
        void Row(string label, string none, Func<int> get, Action<int> set)
        {
            var choices = new List<DollChoice> { new(-1, none) };
            choices.AddRange(designs);
            var combo = new ComboBox { Width = 260, ItemsSource = choices };
            combo.SelectedItem = choices.FirstOrDefault(c => c.Id == get()) ?? choices[0];
            combo.SelectionChanged += (_, _) => set((combo.SelectedItem as DollChoice)?.Id ?? -1);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(combo);
            panel.Children.Add(row);
        }
        Row("Save/Load screen:", "(None - the game can't be saved)", () => _settings.SaveLoad.DollDesignId, v => _settings.SaveLoad.DollDesignId = v);
        Row("\"Are you sure?\" prompt:", "(None - press the button again)", () => _settings.SaveLoad.ConfirmDollDesignId, v => _settings.SaveLoad.ConfirmDollDesignId = v);
        Row("\"Game saved\" message:", "(None - a \"Game saved\" label)", () => _settings.SaveLoad.SavedDollDesignId, v => _settings.SaveLoad.SavedDollDesignId = v);
        Row("Quit prompt:", "(None - Ctrl+Q / Quit quit at once)", () => _settings.Quit.DollDesignId, v => _settings.Quit.DollDesignId = v);
        panel.Children.Add(new TextBlock
        {
            Text = "The paperdoll the player saves and loads the game with. Make it in the PaperDoll Editor like any paperdoll - its look is all yours - with these parts from its toolbox:\n" +
                   "  Save slot - one saved game each, as Exult's slots: add as many as you like (click one, type its name, Save)\n" +
                   "  Save list - or all the saved games in one scrolling list (click picks, double-click loads, wheel scrolls)\n" +
                   "  Save screenshot, Save details - the picked save's picture and day / time\n" +
                   "  Save button, Load button, Delete save button - and a Close button.\n\n" +
                   "In the game the S key opens it (as in Exult). It opens with the paperdoll opening sound and the game is paused while it's open. " +
                   "Saves are kept per game on the player's PC, as many as they like.\n\n" +
                   "The \"Are you sure?\" prompt opens over the Save/Load screen before a save is written over or deleted: make it like any paperdoll - a Text part (\"Are you sure?\"), a Yes / OK button (it goes ahead) and a No / Cancel button (it closes the prompt, back to the Save/Load screen). " +
                   "The \"Game saved\" message opens once the game is saved: your words, and a No / Cancel button captioned OK to close it. Both are modal - nothing else can be clicked till they close.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        });
        return panel;
    }

    /// <summary>The game's errors: the paperdoll they show in, and their words by number (editable; numbers of your own for Show Error).</summary>
    private UIElement BuildErrorsTab()
    {
        var errors = _settings.Errors;
        // Every engine error is listed, so its words can be changed; the maker's own stay as they are.
        foreach (var engine in GameErrors.Defaults)
            if (errors.Messages.All(m => m.Number != engine.Number))
                errors.Messages.Add(new GameErrorMessage { Number = engine.Number, Description = engine.Description });
        errors.Messages.Sort((a, b) => a.Number.CompareTo(b.Number));

        var panel = new DockPanel { Margin = new Thickness(10) };
        var designs = (ProjectSession.ObjectsProvider?.Invoke()?.DollDesigns ?? new List<DollDesign>()).Select(d => new DollChoice(d.Id, d.Name)).ToList();
        var choices = new List<DollChoice> { new(-1, "(None - the message shows where it happened)") };
        choices.AddRange(designs);
        var doll = new ComboBox { Width = 280, ItemsSource = choices };
        doll.SelectedItem = choices.FirstOrDefault(c => c.Id == errors.DollDesignId) ?? choices[0];
        doll.SelectionChanged += (_, _) => errors.DollDesignId = (doll.SelectedItem as DollChoice)?.Id ?? -1;
        var top = LabeledRow("Error paperdoll:", doll);
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var note = new TextBlock
        {
            Text = "Every error the game can show has a number and its words. Make the error paperdoll in the PaperDoll Editor like any paperdoll: a Text part with your words, and a No / Cancel button captioned OK to close it. " +
                   "In its text and captions, %errnum is the error's number and %errdescrp its words below - or write your own message around them (\"Oops! Error %errnum - %errdescrp\"). " +
                   "It opens modal, over everything, with the paperdoll opening sound.\n\n" +
                   "In the words, %errdetail is what the engine knows of it (what's going on, the file, Windows' own reason). An error whose number isn't listed shows %errdetail as its words. " +
                   "Add numbers of your own (1000 and up, say) and show them with the Show Error event command. Errors before the game can draw anything (its data files missing) show the same number and words in a plain box.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 8)
        };
        DockPanel.SetDock(note, Dock.Top);
        panel.Children.Add(note);

        var grid = new DataGrid
        {
            ItemsSource = errors.Messages, AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 200
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Error #", Binding = new System.Windows.Data.Binding(nameof(GameErrorMessage.Number)), Width = 70 });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Description (%errdetail = the engine's detail)", Binding = new System.Windows.Data.Binding(nameof(GameErrorMessage.Description)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        var reset = new Button { Content = "Engine's words back for the selected error", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            if (grid.SelectedItem is not GameErrorMessage picked || GameErrors.Defaults.FirstOrDefault(d => d.Number == picked.Number) is not { } engine) return;
            picked.Description = engine.Description;
            grid.Items.Refresh();
        };
        DockPanel.SetDock(reset, Dock.Bottom);
        panel.Children.Add(reset);
        panel.Children.Add(grid);
        return panel;
    }

    /// <summary>The Title Screen: a PaperDoll Editor paperdoll shown before the game, its music, an intro video.</summary>
    private UIElement BuildTitleTab()
    {
        var title = _settings.Title;
        var panel = new StackPanel { Margin = new Thickness(10) };
        var designs = (ProjectSession.ObjectsProvider?.Invoke()?.DollDesigns ?? new List<DollDesign>()).Select(d => new DollChoice(d.Id, d.Name)).ToList();
        var dolls = new List<DollChoice> { new(-1, "(None - the game starts at once)") };
        dolls.AddRange(designs);
        var doll = new ComboBox { Width = 280, ItemsSource = dolls };
        doll.SelectedItem = dolls.FirstOrDefault(d => d.Id == title.DollDesignId) ?? dolls[0];
        doll.SelectionChanged += (_, _) => title.DollDesignId = (doll.SelectedItem as DollChoice)?.Id ?? -1;
        panel.Children.Add(LabeledRow("Title paperdoll:", doll));

        var none = new ResourceEntry { Id = -1, Name = "(None)" };
        var tunes = new List<ResourceEntry> { none };
        tunes.AddRange(_resources.Entries.Where(r => r.Kind is ResourceKind.Music or ResourceKind.Sound));
        var music = new ComboBox { Width = 220, ItemsSource = tunes, DisplayMemberPath = "Name" };
        music.SelectedItem = tunes.FirstOrDefault(t => t.Id == title.MusicId) ?? none;
        music.SelectionChanged += (_, _) => title.MusicId = (music.SelectedItem as ResourceEntry)?.Id ?? -1;
        var play = new Button { Content = "\u25B6", Width = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Hear it" };
        play.Click += (_, _) => { SoundPlayback.StopFor(this); if (title.MusicId >= 0) SoundPlayback.TryPlayFor(this, _resources, title.MusicId, false); };
        var stop = new Button { Content = "\u25A0", Width = 28, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Stop" };
        stop.Click += (_, _) => SoundPlayback.StopFor(this);
        var loops = new CheckBox { Content = "Loop", IsChecked = title.MusicLoops, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        loops.Checked += (_, _) => title.MusicLoops = true;
        loops.Unchecked += (_, _) => title.MusicLoops = false;
        var musicRow = new StackPanel { Orientation = Orientation.Horizontal };
        musicRow.Children.Add(music);
        musicRow.Children.Add(play);
        musicRow.Children.Add(stop);
        musicRow.Children.Add(loops);
        panel.Children.Add(LabeledRow("Title music:", musicRow));

        var videoList = new List<KeyValuePair<int, string>> { new(-1, "(None)") };
        videoList.AddRange((ProjectSession.VideosProvider?.Invoke()?.Videos ?? new List<VideoEntry>()).Select(v => new KeyValuePair<int, string>(v.Id, v.Name)));
        var video = new ComboBox { Width = 280, ItemsSource = videoList, DisplayMemberPath = "Value" };
        video.SelectedItem = videoList.FirstOrDefault(v => v.Key == title.IntroVideoId);
        if (video.SelectedItem is null) video.SelectedIndex = 0;
        video.SelectionChanged += (_, _) => title.IntroVideoId = video.SelectedItem is KeyValuePair<int, string> v ? v.Key : -1;
        panel.Children.Add(LabeledRow("Intro video:", video));

        var inTest = new CheckBox { Content = "Game Test (F5) starts at the Title Screen too", IsChecked = title.InGameTest, Margin = new Thickness(0, 8, 0, 0) };
        inTest.Checked += (_, _) => title.InGameTest = true;
        inTest.Unchecked += (_, _) => title.InGameTest = false;
        panel.Children.Add(inTest);

        panel.Children.Add(new TextBlock
        {
            Text = "Make the Title Screen in the PaperDoll Editor like any paperdoll - its size is the game's view (320 x 200, or bigger and it's scaled to fit):\n" +
                   "  a Picture for the background (an animated sprite works), Text for the game's name,\n" +
                   "  and from the toolbox's Title / Quit row: New Game, Continue (loads the newest save; greyed when there's none), Load Game (the Save/Load screen, load only) and Quit.\n" +
                   "  Any other Button runs its own events (Events..., By Use) - credits, an options paperdoll (Open Paperdoll), a sound.\n\n" +
                   "A built game always starts here (after the intro video, if any); Game > Test the Title Screen tries it. Escape doesn't close it; closing it any other way starts the game. " +
                   "The Return To Title command comes back here with a fresh game. Ctrl+Q asks to quit at any time (the Quit prompt, Save / Load tab).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 12, 0, 0)
        });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>How a built game opens: in a window, or 100% full screen.</summary>
    private UIElement BuildAutoSaveTab()
    {
        var auto = _settings.AutoSave;
        var panel = new StackPanel { Margin = new Thickness(10) };
        var on = new CheckBox { Content = "Save the project by itself", IsChecked = auto.Enabled };
        on.Checked += (_, _) => auto.Enabled = true;
        on.Unchecked += (_, _) => auto.Enabled = false;
        panel.Children.Add(on);
        var minutes = new TextBox { Width = 60, Text = auto.Minutes.ToString() };
        minutes.LostFocus += (_, _) => { if (int.TryParse(minutes.Text.Trim(), out int m)) auto.Minutes = Math.Clamp(m, 1, 600); minutes.Text = auto.Minutes.ToString(); };
        var row = LabeledRow("Every (real minutes):", minutes);
        ((FrameworkElement)row).Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = "While the editor is open, the project (.msrp and its .sdbx files) is saved this often, as Project > Save does - so a power cut or a crash loses no more than these minutes of work. " +
                   "It isn't saved while Game Test is running or a window is open over the editor; it waits till they're closed.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 12, 0, 0)
        });
        return panel;
    }

    /// <summary>
    /// The game's own languages (as Serpent Isle's Ophidian): a name and the font its words show in while the party
    /// doesn't understand them. Show Text and books pick one; Learn Language and the Translate spell make them readable.
    /// </summary>
    private UIElement BuildLanguagesTab()
    {
        var languages = _settings.Languages;
        var panel = new StackPanel { Margin = new Thickness(10) };
        var rows = new StackPanel();
        var fonts = new List<string> { "(the ordinary font)" };
        fonts.AddRange(_sprites.FontNames());
        void Show()
        {
            rows.Children.Clear();
            foreach (var language in languages.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var name = new TextBox { Width = 180, Text = language.Name };
                name.LostFocus += (_, _) => language.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : language.Name;
                row.Children.Add(name);
                row.Children.Add(new TextBlock { Text = "written in", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
                var font = new ComboBox { Width = 180, ItemsSource = fonts, SelectedItem = fonts.Contains(language.FontSpriteName) ? language.FontSpriteName : fonts[0],
                    ToolTip = "The font from the Sprite Editor's Fonts group its words show in while the party doesn't understand them - runes, glyphs." };
                font.SelectionChanged += (_, _) => language.FontSpriteName = font.SelectedIndex > 0 ? font.SelectedItem as string ?? "" : "";
                row.Children.Add(font);
                var remove = new Button { Content = "✕", Width = 22, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove this language" };
                remove.Click += (_, _) =>
                {
                    if (MessageBox.Show($"Remove the language \"{language.Name}\"? Show Texts and books written in it read plainly again.", "Languages", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    languages.Remove(language);
                    Show();
                };
                row.Children.Add(remove);
                rows.Children.Add(row);
            }
        }
        Show();
        panel.Children.Add(new TextBlock { Text = "The game's own languages", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(rows);
        var add = new Button { Content = "Add language", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 0, 0) };
        add.Click += (_, _) => { languages.Add(new LanguageDefinition { Id = languages.Count == 0 ? 1 : languages.Max(l => l.Id) + 1 }); Show(); };
        panel.Children.Add(add);
        panel.Children.Add(new TextBlock
        {
            Text = "As Serpent Isle's Ophidian runes: a Show Text (its Language box) or a book (Book Editor > Language) written in a language shows in its font till the party understands it - " +
                   "someone in the party knows it (Learn / Forget Language, or Character Editor), or a Translate spell is on.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 12, 0, 0)
        });
        return panel;
    }

    private UIElement BuildGameWindowTab()
    {
        var display = _settings.Display;
        var panel = new StackPanel { Margin = new Thickness(10) };
        var window = new RadioButton { Content = "In a window", GroupName = "screen", IsChecked = display.Mode == GameScreenMode.Window };
        var full = new RadioButton { Content = "Full screen (the whole screen, no frame - Alt+F4 or Ctrl+Q quits)", GroupName = "screen", IsChecked = display.Mode == GameScreenMode.FullScreen, Margin = new Thickness(0, 6, 0, 0) };
        window.Checked += (_, _) => display.Mode = GameScreenMode.Window;
        full.Checked += (_, _) => display.Mode = GameScreenMode.FullScreen;
        panel.Children.Add(new TextBlock { Text = "A built game (Game > Build...) opens:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(window);
        panel.Children.Add(full);
        var scales = Enumerable.Range(2, 5).Select(n => $"{n} x  ({320 * n} x {200 * n})").ToList();
        var scale = new ComboBox { Width = 180, ItemsSource = scales, SelectedIndex = Math.Clamp(display.WindowScale, 2, 6) - 2 };
        scale.SelectionChanged += (_, _) => display.WindowScale = scale.SelectedIndex + 2;
        var row = LabeledRow("Window size:", scale);
        ((FrameworkElement)row).Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = "Full screen fills the screen completely - no title bar, no taskbar. The game's view keeps Ultima VII's 320 x 200 shape, scaled up as big as it fits (black bars at the sides on a wide screen). " +
                   "A window can still be resized or maximized by the player.\n\n" +
                   "Game Test in the editor always opens in a window, with its testing bar at the bottom (messages, the clock, F6 to skip time, F9 for the script data); a built game has none of these.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 12, 0, 0)
        });
        return panel;
    }

    private static FrameworkElement LabeledRow(string label, UIElement content)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(content);
        return row;
    }

    /// <summary>How combat shows: Exult's "show hits" option.</summary>
    private UIElement BuildCombatTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        var showHits = new CheckBox { Content = "Show hit numbers", IsChecked = _settings.Combat.ShowHitNumbers, Margin = new Thickness(0, 0, 0, 6) };
        showHits.Checked += (_, _) => _settings.Combat.ShowHitNumbers = true;
        showHits.Unchecked += (_, _) => _settings.Combat.ShowHitNumbers = false;
        panel.Children.Add(showHits);
        panel.Children.Add(new TextBlock
        {
            Text = "Checked: the damage and misses show over whoever is hit (\"-4\", \"miss\") - Exult's \"show hits\" option.\n" +
                   "Unchecked (Exult's default): no numbers. Either way, as in Exult, whoever is hit flashes a red outline for a moment, " +
                   "and a big hit on the main character (a third of its health or more, or while below a quarter) flashes the whole screen red instead.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        });
        return panel;
    }

    /// <summary>Where a conversation's replies show: in the speaker's box, or (as Exult) in the main character's own box under it.</summary>
    private UIElement BuildConversationTab()
    {
        var conversation = _settings.Conversation;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = "The replies the player picks from show:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var speaker = new RadioButton { Content = "Under the speaker's words, in the same box", GroupName = "replies", IsChecked = !conversation.RepliesUnderMainCharacter };
        var main = new RadioButton { Content = "Under the main character, in a box of their own (as Exult)", GroupName = "replies", IsChecked = conversation.RepliesUnderMainCharacter, Margin = new Thickness(0, 6, 0, 0) };
        speaker.Checked += (_, _) => conversation.RepliesUnderMainCharacter = false;
        main.Checked += (_, _) => conversation.RepliesUnderMainCharacter = true;
        panel.Children.Add(speaker);
        panel.Children.Add(main);
        panel.Children.Add(new TextBlock
        {
            Text = "In Ultima VII (Exult) the main character is the one answering: the person talked to has a box with their face and words, " +
                   "and under it is a second box with the main character's face (Character Editor > Face) and the replies beside it.\n\n" +
                   "The two boxes stay together at the Show Text's Position (Top, Middle or Bottom) and use its frame. " +
                   "A Show Text with no replies is one box either way. The Show Text window's preview and the Conversation Test show it the way picked here.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 12, 0, 0)
        });
        return panel;
    }

    /// <summary>Sounds the engine plays by itself, for every thing in the game - no event needed on each object.</summary>
    private UIElement BuildSoundsTab()
    {
        var sounds = _settings.Sounds;
        var panel = new StackPanel { Margin = new Thickness(10) };
        var none = new ResourceEntry { Id = -1, Name = "(None)" };
        var choices = new List<ResourceEntry> { none };
        choices.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var combo = new ComboBox { Width = 220, ItemsSource = choices, DisplayMemberPath = "Name", VerticalAlignment = VerticalAlignment.Center };
        combo.SelectedItem = choices.FirstOrDefault(s => s.Id == sounds.DropOnMapSoundId) ?? none;
        combo.SelectionChanged += (_, _) => sounds.DropOnMapSoundId = (combo.SelectedItem as ResourceEntry)?.Id ?? -1;
        var play = new Button { Content = "▶", Width = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Hear it" };
        play.Click += (_, _) => { if (sounds.DropOnMapSoundId >= 0) SoundPlayback.TryPlay(_resources, sounds.DropOnMapSoundId); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock { Text = "Drop on map:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(combo);
        row.Children.Add(play);
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock
        {
            Text = "Plays whenever anything is put down on the map - items, objects, containers, stacks of money or ammo, and things taken out of dead bodies. " +
                   "No Play Sound event is needed on each object. An object's own By Drop Item event still runs too, so remove any Play Sound you added there or both will play.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        });

        panel.Children.Add(SoundSettingRow("Drop in a window:", () => sounds.NavigateSoundId, v => sounds.NavigateSoundId = v, out _));
        panel.Children.Add(new TextBlock
        {
            Text = "Plays whenever something is put into a container's window or onto a paperdoll - a character's inventory, a slot, a paperdoll's slot - for every container and paperdoll (it was each one's own Navigate Sound). Different from Drop on map.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        });

        var paperdollRow = SoundSettingRow("Paperdoll open:", () => sounds.PaperdollOpenSoundId, v => sounds.PaperdollOpenSoundId = v, out var paperdollCombo);
        var useIt = new CheckBox { Content = "Use this one opening sound for every paperdoll", IsChecked = sounds.PaperdollOpenSoundEnabled, Margin = new Thickness(0, 12, 0, 4) };
        paperdollCombo.IsEnabled = sounds.PaperdollOpenSoundEnabled;
        useIt.Checked += (_, _) => { sounds.PaperdollOpenSoundEnabled = true; paperdollCombo.IsEnabled = true; };
        useIt.Unchecked += (_, _) => { sounds.PaperdollOpenSoundEnabled = false; paperdollCombo.IsEnabled = false; };
        panel.Children.Add(useIt);
        panel.Children.Add(paperdollRow);
        panel.Children.Add(new TextBlock
        {
            Text = "Ticked: every paperdoll plays this when it opens - a character's inventory, a book, a spellbook, a map, a container's window, and paperdolls made in the PaperDoll Editor - and their own Open Sound pickers are greyed out. " +
                   "Unticked (the default): each uses its own, so a book can flip its pages and a chest can creak.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        });

        // Combat music (GameTestWindow.CombatMusic.cs)
        var music = _settings.Combat.Music;
        panel.Children.Add(new TextBlock { Text = "Combat music", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2) });
        panel.Children.Add(new TextBlock
        {
            Text = "Played by the engine itself, as in Exult. Exult plays each piece once and never loops it (Loop unticked); then the map's own sound comes back. " +
                   "While combat music plays, or while the main character battles in combat mode, the map's ambient sound (birds, crickets) is silent; rain, thunder and other weather sounds play on. (None) = that moment changes nothing.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        });
        panel.Children.Add(CombatMusicRow("Danger:", music.Danger, "An enemy close by is fighting, and you're not in combat mode (Exult checks every second). A blow landing on you or the party switches to Battle."));
        panel.Children.Add(CombatMusicRow("Battle:", music.Battle, "Your battle begins in combat mode - at most once every 30 seconds, so Danger can be heard."));
        panel.Children.Add(CombatMusicRow("Battle (2nd):", music.Battle2, "Optional: Exult picks one of its two battle pieces at random each time."));
        panel.Children.Add(CombatMusicRow("Run away:", music.RunAway, "Combat mode turned off (C) while a foe is still close by."));
        panel.Children.Add(CombatMusicRow("Escaped:", music.Escaped, "Exult's \"hidden danger\": the enemies are out of sight while Danger or Battle plays."));
        panel.Children.Add(CombatMusicRow("Victory:", music.Victory, "You or the party kill an enemy, and no enemy is left close by."));
        panel.Children.Add(CombatMusicRow("Battle over:", music.BattleOver, "Instead of Victory after a long fight (over 15 seconds - the longer, the likelier)."));
        panel.Children.Add(CombatMusicRow("Near death:", music.NearDeath, "Not in Exult: you're below a quarter of your health with an enemy around. Stops when you recover or the danger is gone."));
        panel.Children.Add(CombatMusicRow("Death:", music.Death, "Not in Exult: you died. Plays until the Save/Load screen opens (double-click your body, or S) or a game is loaded."));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>One piece of combat music: the track, hear it, loop it, and when it plays.</summary>
    private FrameworkElement CombatMusicRow(string label, CombatMusicCue cue, string when)
    {
        var none = new ResourceEntry { Id = -1, Name = "(None)" };
        var choices = new List<ResourceEntry> { none };
        choices.AddRange(_resources.Entries.Where(r => r.Kind is ResourceKind.Music or ResourceKind.Sound));
        var box = new ComboBox { Width = 220, ItemsSource = choices, DisplayMemberPath = "Name", VerticalAlignment = VerticalAlignment.Center };
        box.SelectedItem = choices.FirstOrDefault(s => s.Id == cue.MusicId) ?? none;
        box.SelectionChanged += (_, _) => cue.MusicId = (box.SelectedItem as ResourceEntry)?.Id ?? -1;
        var play = new Button { Content = "▶", Width = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Hear it" };
        play.Click += (_, _) => { SoundPlayback.StopFor(this); if (cue.MusicId >= 0) SoundPlayback.TryPlayFor(this, _resources, cue.MusicId, false); };
        var stop = new Button { Content = "■", Width = 28, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Stop" };
        stop.Click += (_, _) => SoundPlayback.StopFor(this);
        var loop = new CheckBox { Content = "Loop", IsChecked = cue.Loop, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        loop.Checked += (_, _) => cue.Loop = true;
        loop.Unchecked += (_, _) => cue.Loop = false;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(box);
        row.Children.Add(play);
        row.Children.Add(stop);
        row.Children.Add(loop);
        var block = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        block.Children.Add(row);
        block.Children.Add(new TextBlock { Text = when, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(120, 2, 0, 0) });
        return block;
    }

    private FrameworkElement SoundSettingRow(string label, Func<int> get, Action<int> set, out ComboBox combo)
    {
        var none = new ResourceEntry { Id = -1, Name = "(None)" };
        var choices = new List<ResourceEntry> { none };
        choices.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var box = new ComboBox { Width = 220, ItemsSource = choices, DisplayMemberPath = "Name", VerticalAlignment = VerticalAlignment.Center };
        box.SelectedItem = choices.FirstOrDefault(s => s.Id == get()) ?? none;
        box.SelectionChanged += (_, _) => set((box.SelectedItem as ResourceEntry)?.Id ?? -1);
        var play = new Button { Content = "\u25B6", Width = 28, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Hear it" };
        play.Click += (_, _) => { if (get() >= 0) SoundPlayback.TryPlay(_resources, get()); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 6) };
        row.Children.Add(new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(box);
        row.Children.Add(play);
        combo = box;
        return row;
    }

    /// <summary>Exult's game clock: when a new game starts, and how fast time runs (Exult's time rate).</summary>
    private UIElement BuildClockTab()
    {
        var clock = _settings.Clock;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "The game clock works as Exult's: a game minute passes every 5 real seconds (25 ticks of 200 ms), so an hour takes 5 minutes " +
                   "and a whole day 2 hours. Time stands still while a paperdoll, container or book is open. Characters change what they're doing " +
                   "at the start of each 3-hour period (Character Editor > Schedule).",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });

        var startRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        startRow.Children.Add(new TextBlock { Text = "A new game starts at:", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        var start = new ComboBox { Width = 110 };
        for (int h = 0; h < 24; h++) start.Items.Add($"{(h % 12 == 0 ? 12 : h % 12)}:00 {(h < 12 ? "AM" : "PM")}");
        start.SelectedIndex = Math.Clamp(clock.StartHour, 0, 23);
        start.SelectionChanged += (_, _) => clock.StartHour = Math.Max(0, start.SelectedIndex);
        startRow.Children.Add(start);
        startRow.Children.Add(new TextBlock { Text = "  (Exult: 6:00 AM)", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(startRow);

        var rateRow = new StackPanel { Orientation = Orientation.Horizontal };
        rateRow.Children.Add(new TextBlock { Text = "Time rate:", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        var rate = new ComboBox { Width = 110 };
        foreach (int r in new[] { 1, 2, 3, 4, 5, 10, 25 }) rate.Items.Add(r);
        rate.SelectedItem = rate.Items.Cast<int>().Contains(clock.TimeRate) ? clock.TimeRate : 1;
        rate.SelectionChanged += (_, _) => clock.TimeRate = rate.SelectedItem is int r ? r : 1;
        rateRow.Children.Add(rate);
        rateRow.Children.Add(new TextBlock { Text = "  x (1 = Exult's normal speed; 25 = a game minute every 200 ms)", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(rateRow);
        return panel;
    }

    private UIElement BuildMousePointersTab()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "The game uses these instead of the Windows mouse pointer - the Windows pointer never shows in the game. A pointer with no sprite shows as a black square. All pointers are one size: pick it below, and each list then shows only sprites of exactly that size.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10)
        });

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        sizeRow.Children.Add(new TextBlock { Text = "Pointer size:", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        sizeRow.Children.Add(_sizeCombo);
        _sizeCombo.SelectionChanged += (_, _) => PointerSizeChanged();
        panel.Children.Add(sizeRow);

        foreach (var (kind, name, when) in MousePointerCatalog.All)
        {
            var combo = new ComboBox { Width = 260, DisplayMemberPath = nameof(PointerChoice.Display), VerticalAlignment = VerticalAlignment.Center };
            var preview = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
            var previewBox = new Border { Width = 56, Height = 56, Background = Brushes.Black, Child = preview, Margin = new Thickness(10, 0, 0, 0), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };

            var text = new StackPanel { Width = 230, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.Bold });
            text.Children.Add(new TextBlock { Text = when, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

            // A pointer's animation and sound are its sprite's own - frames, speed, looping and each frame's sound, all set in the Sprite Editor. Shown here, and editable from here.
            var info = new TextBlock { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Width = 260, Margin = new Thickness(0, 3, 0, 0) };
            var editFrames = new Button { Content = "Frames && Sounds...", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Open this sprite in the Sprite Editor: its frames, speed, looping, and a sound per frame" };
            var pickers = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            pickers.Children.Add(combo);
            pickers.Children.Add(info);
            pickers.Children.Add(editFrames);

            var playHolder = new ContentControl { Margin = new Thickness(10, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var previewColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            previewColumn.Children.Add(previewBox);
            previewColumn.Children.Add(playHolder);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(text);
            row.Children.Add(pickers);
            row.Children.Add(previewColumn);
            panel.Children.Add(row);

            var thisKind = kind;
            combo.SelectionChanged += (_, _) => PointerChanged(thisKind);
            editFrames.Click += (_, _) => EditFrames(thisKind);
            _rows[kind] = (combo, preview);
            _playHolders[kind] = playHolder;
            _spriteDetails[kind] = (info, editFrames);
        }

        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void LoadSizes()
    {
        var pointers = _settings.MousePointers;
        var sizes = _measured
            .GroupBy(m => (m.Width, m.Height))
            .Select(g => new SizeChoice(g.Key.Width, g.Key.Height, $"{g.Key.Width} x {g.Key.Height} px ({g.Count()} sprite{(g.Count() == 1 ? "" : "s")})"))
            .OrderBy(s => s.Width * s.Height).ThenBy(s => s.Width)
            .ToList();
        if (pointers.Width > 0 && !sizes.Any(s => s.Width == pointers.Width && s.Height == pointers.Height))
            sizes.Insert(0, new SizeChoice(pointers.Width, pointers.Height, $"{pointers.Width} x {pointers.Height} px (no sprites of this size)"));

        _suppress = true;
        _sizeCombo.ItemsSource = sizes;
        // The saved size if there is one; otherwise the size of the smallest sprites - pointers are small.
        _sizeCombo.SelectedItem = sizes.FirstOrDefault(s => s.Width == pointers.Width && s.Height == pointers.Height) ?? sizes.FirstOrDefault();
        _suppress = false;
        PointerSizeChanged();
    }

    private void PointerSizeChanged()
    {
        if (_suppress || _sizeCombo.SelectedItem is not SizeChoice size) return;
        var pointers = _settings.MousePointers;
        pointers.Width = size.Width;
        pointers.Height = size.Height;

        // Only sprites of exactly this size can be pointers - any assignment of another size no longer applies.
        foreach (var kind in pointers.Sprites.Keys.ToList())
        {
            var sprite = MousePointerCatalog.Resolve(_sprites, pointers.Sprites[kind]);
            if (sprite is null || MousePointerCatalog.PixelSize(sprite) != (size.Width, size.Height)) pointers.Sprites.Remove(kind);
        }

        var choices = new List<PointerChoice> { NoSprite };
        choices.AddRange(_measured
            .Where(m => m.Width == size.Width && m.Height == size.Height)
            .Select(m => new PointerChoice(m.Group, m.Sprite.Name, $"{m.Sprite.Name}   ({m.Group})")));

        _suppress = true;
        foreach (var (kind, (combo, _)) in _rows)
        {
            combo.ItemsSource = choices;
            var link = pointers.Sprites.GetValueOrDefault(kind);
            combo.SelectedItem = link is null ? NoSprite : choices.FirstOrDefault(c => c.Group == link.SpriteGroup && c.Name == link.SpriteName) ?? NoSprite;
        }
        _suppress = false;
        foreach (var kind in _rows.Keys) ShowPreview(kind);
    }

    private void PointerChanged(MousePointerKind kind)
    {
        if (_suppress) return;
        var pointers = _settings.MousePointers;
        if (_rows[kind].Combo.SelectedItem is PointerChoice { Name.Length: > 0 } choice)
            pointers.Sprites[kind] = new MousePointerSprite { SpriteGroup = choice.Group, SpriteName = choice.Name };
        else
            pointers.Sprites.Remove(kind);
        ShowPreview(kind, playFirstFrameSound: true); // the designer just picked it - let them hear it the way the game will
    }

    /// <summary>Opens the pointer's sprite in the Sprite Editor's own frame window - where its frames and per-frame sounds live - then refreshes the preview.</summary>
    private void EditFrames(MousePointerKind kind)
    {
        var sprite = MousePointerCatalog.Resolve(_sprites, _settings.MousePointers.Sprites.GetValueOrDefault(kind));
        if (sprite is null) return;
        new SpriteFrameEditorWindow(sprite, _resources) { Owner = this }.ShowDialog();
        // Edits can change the frame size - a pointer must stay the chosen size, so re-measure and re-check every assignment.
        _measured.Clear();
        foreach (var group in _sprites.Groups)
            foreach (var each in group.Sprites)
                if (MousePointerCatalog.PixelSize(each) is { } size)
                    _measured.Add((group.Name, each, size.Width, size.Height));
        PointerSizeChanged();
    }

    /// <summary>The pointer's first frame, still and silent, with a play button that runs it exactly as the game will (animated, each frame's sound) - or the black square the game shows when there's no sprite.</summary>
    private void ShowPreview(MousePointerKind kind, bool playFirstFrameSound = false)
    {
        if (_playHolders[kind].Content is AnimationPreviewButton oldButton) oldButton.StopPlaying();
        _playHolders[kind].Content = null;
        var preview = _rows[kind].Preview;
        preview.Source = null;
        var sprite = MousePointerCatalog.Resolve(_sprites, _settings.MousePointers.Sprites.GetValueOrDefault(kind));
        var (info, editFrames) = _spriteDetails[kind];
        info.Text = sprite is null ? "No sprite: the game shows a black square." : MousePointerCatalog.Describe(sprite, _resources);
        editFrames.IsEnabled = sprite is not null;
        if (sprite is null) return;
        _playHolders[kind].Content = new AnimationPreviewButton(sprite, preview, _resources); // stopped until played
        if (playFirstFrameSound) MousePointerCatalog.PlayFirstFrameSound(sprite, _resources); // the designer just picked it
    }
}
