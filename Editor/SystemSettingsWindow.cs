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
    private readonly Dictionary<MousePointerKind, SpriteAnimator> _previewAnimators = new();
    /// <summary>Every sprite with its frame size, measured once when the window opens.</summary>
    private readonly List<(string Group, SpriteEntry Sprite, int Width, int Height)> _measured = new();
    private bool _suppress;

    public SystemSettingsWindow(SystemSettings settings, SpriteDatabase sprites, ResourceDatabase resources)
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
        tabs.Items.Add(new TabItem { Header = "Body Container", Content = BuildBodyContainerTab() });
        tabs.Items.Add(new TabItem { Header = "Combat", Content = BuildCombatTab() });

        var close = new Button { Content = "Close", Width = 80, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(tabs);
        Content = root;

        Closed += (_, _) => { foreach (var animator in _previewAnimators.Values) animator.Stop(); };
        LoadSizes();
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

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(text);
            row.Children.Add(pickers);
            row.Children.Add(previewBox);
            panel.Children.Add(row);

            var thisKind = kind;
            combo.SelectionChanged += (_, _) => PointerChanged(thisKind);
            editFrames.Click += (_, _) => EditFrames(thisKind);
            _rows[kind] = (combo, preview);
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

    /// <summary>Plays the pointer exactly as the game will - animated, with each frame's sound - or shows the black square the game shows when there's no sprite.</summary>
    private void ShowPreview(MousePointerKind kind, bool playFirstFrameSound = false)
    {
        if (_previewAnimators.Remove(kind, out var old)) old.Stop();
        var preview = _rows[kind].Preview;
        preview.Source = null;
        var sprite = MousePointerCatalog.Resolve(_sprites, _settings.MousePointers.Sprites.GetValueOrDefault(kind));
        var (info, editFrames) = _spriteDetails[kind];
        info.Text = sprite is null ? "No sprite: the game shows a black square." : MousePointerCatalog.Describe(sprite, _resources);
        editFrames.IsEnabled = sprite is not null;
        if (sprite is null) return;
        _previewAnimators[kind] = new SpriteAnimator(sprite, preview, _resources);
        if (playFirstFrameSound) MousePointerCatalog.PlayFirstFrameSound(sprite, _resources);
    }
}
