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
public class SystemSettingsWindow : Window
{
    private sealed record PointerChoice(string Group, string Name, string Display);
    private sealed record SizeChoice(int Width, int Height, string Display);

    private static readonly PointerChoice NoSprite = new("", "", "(none - shows a black square)");
    private static readonly ResourceEntry NoSound = new() { Id = -1, Name = "(no sound)" };

    private readonly SystemSettings _settings;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private readonly ComboBox _sizeCombo = new() { Width = 220, DisplayMemberPath = nameof(SizeChoice.Display) };
    private readonly Dictionary<MousePointerKind, (ComboBox Combo, Image Preview)> _rows = new();
    private readonly Dictionary<MousePointerKind, ComboBox> _soundCombos = new();
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

            // The sound the engine plays when it flashes this pointer (Exult plays its error "ding" with the red X) - any sound from the Sound list; picking one plays it once so you can hear it.
            var soundChoices = new List<ResourceEntry> { NoSound };
            soundChoices.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
            var soundCombo = new ComboBox { Width = 260, DisplayMemberPath = nameof(ResourceEntry.Name), ItemsSource = soundChoices, Margin = new Thickness(0, 4, 0, 0), ToolTip = "Sound played when the game flashes this pointer" };
            int savedSound = _settings.MousePointers.SoundResourceIds.GetValueOrDefault(kind, -1);
            soundCombo.SelectedItem = soundChoices.FirstOrDefault(r => r.Id == savedSound) ?? NoSound;

            var pickers = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            pickers.Children.Add(combo);
            var soundRow = new StackPanel { Orientation = Orientation.Horizontal };
            soundRow.Children.Add(new TextBlock { Text = "🔊", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 4, 0) });
            soundRow.Children.Add(soundCombo);
            pickers.Children.Add(soundRow);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(text);
            row.Children.Add(pickers);
            row.Children.Add(previewBox);
            panel.Children.Add(row);

            var thisKind = kind;
            combo.SelectionChanged += (_, _) => PointerChanged(thisKind);
            soundCombo.SelectionChanged += (_, _) => SoundChanged(thisKind);
            _rows[kind] = (combo, preview);
            _soundCombos[kind] = soundCombo;
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
        ShowPreview(kind);
    }

    private void SoundChanged(MousePointerKind kind)
    {
        var sounds = _settings.MousePointers.SoundResourceIds;
        if (_soundCombos[kind].SelectedItem is ResourceEntry { Id: >= 0 } sound)
        {
            sounds[kind] = sound.Id;
            SoundPlayback.TryPlay(_resources, sound.Id); // let the designer hear it
        }
        else
        {
            sounds.Remove(kind);
        }
    }

    /// <summary>Plays the pointer exactly as the game will - animated, with each frame's sound - or shows the black square the game shows when there's no sprite.</summary>
    private void ShowPreview(MousePointerKind kind)
    {
        if (_previewAnimators.Remove(kind, out var old)) old.Stop();
        var preview = _rows[kind].Preview;
        preview.Source = null;
        var sprite = MousePointerCatalog.Resolve(_sprites, _settings.MousePointers.Sprites.GetValueOrDefault(kind));
        if (sprite is not null) _previewAnimators[kind] = new SpriteAnimator(sprite, preview, _resources);
    }
}
