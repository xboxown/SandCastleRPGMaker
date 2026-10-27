using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Character Editor > Inventory / Paperdoll > Expand...: the paperdoll big,
/// as the game draws it - the body, every slot with what the character
/// starts with in it (at its real in-game size), and the weight readouts.
/// Drag anything to place it. Built-in slots can be turned off here (and
/// back on); the weight numbers and gauge are set up here.
/// </summary>
public sealed class PaperdollLayoutWindow : Window
{
    private const double LayoutWidth = 320, LayoutHeight = 400;
    /// <summary>What the game scales item pictures by inside the paperdoll's 320x400 layout (GameTestWindow.RefreshPaperdollGump).</summary>
    private const double ItemScale = 2;

    private readonly CharacterDefinition _character;
    private readonly SpriteDatabase _sprites = ProjectSession.Sprites;
    private readonly ObjectDatabase _objects = ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase();
    private readonly ContainerDatabase _containers = ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase();
    private readonly BookDatabase _books = ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase();
    private readonly Canvas _canvas = new() { Width = LayoutWidth, Height = LayoutHeight, Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)), ClipToBounds = true };
    private readonly ScaleTransform _zoom = new(2, 2);
    private readonly StackPanel _settings = new() { Margin = new Thickness(8) };
    private readonly VariableDatabase _variables = ProjectSession.VariablesProvider?.Invoke() ?? new VariableDatabase();
    private object? _dragging, _selected;
    private Point _dragStart, _dragFrom;

    /// <summary>The corner grip of the picked label: dragging it changes its size (Size x), since a sprite font only draws at whole sizes.</summary>
    private sealed record ResizeGrip(PaperdollLabel Label, double HeightPerSize);
    private int _resizeFromScale;

    public PaperdollLayoutWindow(CharacterDefinition character)
    {
        _character = character;
        Title = $"Paperdoll layout - {character.Name}";
        Width = Math.Min(SystemParameters.WorkArea.Width, 1280);
        Height = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _canvas.LayoutTransform = _zoom;
        _canvas.MouseLeftButtonDown += Canvas_Down;
        _canvas.MouseMove += Canvas_Move;
        _canvas.MouseLeftButtonUp += (_, _) => { if (_dragging is ResizeGrip) BuildSettings(); _dragging = null; _canvas.ReleaseMouseCapture(); };
        PreviewKeyDown += (_, e) =>
        {
            // Delete: the picked label goes (not while typing in a box).
            if (e.Key != Key.Delete || _selected is not PaperdollLabel label || Keyboard.FocusedElement is TextBox) return;
            _character.Paperdoll.Labels.Remove(label);
            _selected = null;
            BuildSettings();
            Redraw();
            e.Handled = true;
        };

        var root = new DockPanel();
        var left = new ScrollViewer { Width = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _settings };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            Content = new Border { Margin = new Thickness(12), BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = _canvas }
        });
        Content = root;
        Loaded += (_, _) => { FitZoom(); BuildSettings(); Redraw(); };
    }

    private void FitZoom()
    {
        double fit = Math.Max(1, Math.Floor((ActualHeight - 110) / LayoutHeight * 4) / 4);
        _zoom.ScaleX = _zoom.ScaleY = fit;
    }

    // ================= Settings =================

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) };
    private static TextBlock Note(string text) => new() { Text = text, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 10, 4) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11 });
        panel.Children.Add(control);
        return panel;
    }

    private FrameworkElement NumberBox(string label, Func<double> get, Action<double> set)
    {
        var box = new TextBox { Width = 60, Text = get().ToString("0.##") };
        void Commit() { if (double.TryParse(box.Text, out double v)) { set(v); Redraw(); } box.Text = get().ToString("0.##"); }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return Labeled(label, box);
    }

    private FrameworkElement TextBoxField(string label, double width, Func<string> get, Action<string> set)
    {
        var box = new TextBox { Width = width, Text = get() };
        box.LostFocus += (_, _) => { set(box.Text.Trim()); Redraw(); };
        return Labeled(label, box);
    }

    private CheckBox Check(string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 2, 10, 2) };
        box.Checked += (_, _) => { set(true); Redraw(); };
        box.Unchecked += (_, _) => { set(false); Redraw(); };
        return box;
    }

    private FrameworkElement FontPicker(PaperdollReadout r)
    {
        var fonts = new List<string> { "(in-game font)" };
        fonts.AddRange(_sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.FontsGroupName)?.Sprites.Select(s => s.Name) ?? Enumerable.Empty<string>());
        var combo = new ComboBox { Width = 160, ItemsSource = fonts, SelectedIndex = Math.Max(0, fonts.IndexOf(r.FontName)) };
        combo.SelectionChanged += (_, _) => { r.FontName = combo.SelectedIndex <= 0 ? "" : (string)combo.SelectedItem; Redraw(); };
        return Labeled("Font (Sprite Editor > Fonts)", combo);
    }

    private FrameworkElement EnumCombo<T>(string label, Func<T> get, Action<T> set, double width = 110) where T : struct, Enum
    {
        var values = Enum.GetValues<T>().ToList();
        var combo = new ComboBox { Width = width, ItemsSource = values, SelectedItem = get() };
        combo.SelectionChanged += (_, _) => { set((T)combo.SelectedItem); BuildSettings(); Redraw(); };
        return Labeled(label, combo);
    }

    private void ReadoutSettings(string title, PaperdollReadout r)
    {
        _settings.Children.Add(Check(title, () => r.Enabled, v => r.Enabled = v));
        var row = new WrapPanel { Margin = new Thickness(18, 0, 0, 4) };
        row.Children.Add(FontPicker(r));
        row.Children.Add(NumberBox("Size x", () => r.Scale, v => r.Scale = (int)Math.Clamp(v, 1, 8)));
        row.Children.Add(TextBoxField("Tint (#RRGGBB)", 80, () => r.Tint, v => r.Tint = v));
        row.Children.Add(EnumCombo("Lined up by its", () => r.Align, v => r.Align = v, 80));
        _settings.Children.Add(row);
    }

    private void LabelSettings(PaperdollLabel label, int number)
    {
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var delete = new Button { Content = "Delete", Padding = new Thickness(6, 0, 6, 0), FontSize = 11 };
        delete.Click += (_, _) => { _character.Paperdoll.Labels.Remove(label); if (Equals(_selected, label)) _selected = null; BuildSettings(); Redraw(); };
        DockPanel.SetDock(delete, Dock.Right);
        header.Children.Add(delete);
        header.Children.Add(new TextBlock { Text = $"{(label.Kind == PaperdollLabelKind.Text ? "Text" : "Number")} {number}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(header);
        if (label.Kind == PaperdollLabelKind.Text)
        {
            var box = new TextBox { Width = 300, Text = label.Text, HorizontalAlignment = HorizontalAlignment.Left };
            box.TextChanged += (_, _) => { label.Text = box.Text; Redraw(); };
            panel.Children.Add(Labeled("Text", box));
        }
        else
        {
            var names = _variables.Variables.Select(v => v.Name).ToList();
            var combo = new ComboBox { Width = 220, ItemsSource = names, SelectedIndex = _variables.Variables.FindIndex(v => v.Id == label.VariableId), HorizontalAlignment = HorizontalAlignment.Left };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { label.VariableId = _variables.Variables[combo.SelectedIndex].Id; Redraw(); } };
            panel.Children.Add(Labeled(names.Count == 0 ? "Global variable (none yet - add them in the Variable Editor)" : "Global variable", combo));
        }
        var row = new WrapPanel();
        row.Children.Add(FontPicker(label));
        row.Children.Add(NumberBox("Size x", () => label.Scale, v => label.Scale = (int)Math.Clamp(v, 1, 8)));
        row.Children.Add(TextBoxField("Tint (#RRGGBB)", 80, () => label.Tint, v => label.Tint = v));
        row.Children.Add(EnumCombo("Lined up by its", () => label.Align, v => label.Align = v, 80));
        panel.Children.Add(row);
        _settings.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(6), Margin = new Thickness(0, 2, 0, 4), Child = panel });
    }

    private FrameworkElement SpritePicker(string label, Func<SpriteLink> get, Action<SpriteLink> set)
    {
        var sprites = _sprites.Groups.Where(x => x.Name != SpriteDatabase.TilesetGroupName && x.Name != SpriteDatabase.FontsGroupName)
            .SelectMany(x => x.Sprites.Select(s => (Group: x.Name, s.Name))).ToList();
        var labels = new List<string> { "(none)" };
        labels.AddRange(sprites.Select(s => $"{s.Name}   ({s.Group})"));
        var current = get();
        var combo = new ComboBox { Width = 220, ItemsSource = labels, SelectedIndex = Math.Max(0, sprites.FindIndex(s => s.Group == current.SpriteGroup && s.Name == current.SpriteName) + 1) };
        combo.SelectionChanged += (_, _) =>
        {
            set(combo.SelectedIndex <= 0 ? new SpriteLink() : new SpriteLink { SpriteGroup = sprites[combo.SelectedIndex - 1].Group, SpriteName = sprites[combo.SelectedIndex - 1].Name });
            Redraw();
        };
        return Labeled(label, combo);
    }

    private void ButtonSettings(string title, PaperdollButton button)
    {
        _settings.Children.Add(Check(title, () => button.Enabled, v => button.Enabled = v));
        var row = new WrapPanel { Margin = new Thickness(18, 0, 0, 4) };
        row.Children.Add(SpritePicker("Sprite", () => button.Sprite, v => button.Sprite = v));
        _settings.Children.Add(row);
    }

    private IEnumerable<(PaperdollButton Button, string Name)> Buttons()
    {
        var pd = _character.Paperdoll;
        yield return (pd.CombatButton, "combat");
        yield return (pd.CombatModeButton, "combat behavior");
        yield return (pd.StatsButton, "heart (stats)");
        yield return (pd.DiskButton, "disk (save / load)");
        yield return (pd.HaloButton, "halo (protect)");
    }

    private void BuildSettings()
    {
        _settings.Children.Clear();
        var zoom = new ComboBox { Width = 120, ItemsSource = new[] { "1x", "1.5x", "2x", "2.5x", "3x", "4x" } };
        zoom.SelectedItem = $"{_zoom.ScaleX:0.#}x";
        zoom.SelectionChanged += (_, _) => { if (zoom.SelectedItem is string z && double.TryParse(z.TrimEnd('x'), out double v)) _zoom.ScaleX = _zoom.ScaleY = v; };
        _settings.Children.Add(Labeled("Zoom", zoom));
        _settings.Children.Add(Note("Drag any slot, number or gauge to place it. Items show what this character starts with, at their size in the game, so you can see they sit right on the body."));

        var pd = _character.Paperdoll;
        _settings.Children.Add(Heading("Slots on this paperdoll"));
        _settings.Children.Add(Note("Untick a slot this paperdoll doesn't have (one ring, no pouch): it isn't drawn and nothing goes in it. Built-in slots can't be deleted - custom slots are added, renamed and deleted in the Character Editor."));
        var slots = new WrapPanel();
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var s = slot;
            slots.Children.Add(new CheckBox { Content = s.ToString(), IsChecked = pd.IsSlotShown(s), Width = 100, Margin = new Thickness(0, 2, 0, 2) }
                .Also(c => { c.Checked += (_, _) => { pd.DisabledSlots.Remove(s); Redraw(); }; c.Unchecked += (_, _) => { if (!pd.DisabledSlots.Contains(s)) pd.DisabledSlots.Add(s); Redraw(); }; }));
        }
        _settings.Children.Add(slots);

        _settings.Children.Add(Heading("Weight (stones)"));
        _settings.Children.Add(Note("What the character carries and the most they can (2 x Strength, as Exult), as numbers in a font, and/or a gauge."));
        ReadoutSettings("Show what they carry", pd.WeightCurrent);
        ReadoutSettings("Show the most they can carry", pd.WeightMax);
        var g = pd.WeightGauge;
        _settings.Children.Add(Check("Show how full as a gauge", () => g.Enabled, v => g.Enabled = v));
        var gauge = new WrapPanel { Margin = new Thickness(18, 0, 0, 4) };
        gauge.Children.Add(EnumCombo("Drawn as", () => g.Kind, v => g.Kind = v, 120));
        if (g.Kind == GaugeKind.SpriteFrames)
        {
            var sprites = _sprites.Groups.Where(x => x.Name != SpriteDatabase.TilesetGroupName && x.Name != SpriteDatabase.FontsGroupName)
                .SelectMany(x => x.Sprites.Select(s => (Group: x.Name, s.Name))).ToList();
            var labels = new List<string> { "(none)" };
            labels.AddRange(sprites.Select(s => $"{s.Name}   ({s.Group})"));
            var combo = new ComboBox { Width = 220, ItemsSource = labels };
            combo.SelectedIndex = Math.Max(0, sprites.FindIndex(s => s.Group == g.Sprite.SpriteGroup && s.Name == g.Sprite.SpriteName) + 1);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex <= 0) g.Sprite = new SpriteLink();
                else { var s = sprites[combo.SelectedIndex - 1]; g.Sprite = new SpriteLink { SpriteGroup = s.Group, SpriteName = s.Name }; }
                Redraw();
            };
            gauge.Children.Add(Labeled("Sprite (first frame empty ... last frame full)", combo));
        }
        else
        {
            gauge.Children.Add(NumberBox("Width", () => g.Width, v => g.Width = Math.Clamp(v, 2, 320)));
            gauge.Children.Add(NumberBox("Height", () => g.Height, v => g.Height = Math.Clamp(v, 2, 400)));
            gauge.Children.Add(TextBoxField("Full colour (#RRGGBB)", 80, () => g.FillColor, v => g.FillColor = v));
            gauge.Children.Add(TextBoxField("Empty colour", 80, () => g.BackColor, v => g.BackColor = v));
        }
        _settings.Children.Add(gauge);

        _settings.Children.Add(Heading("Text and numbers"));
        _settings.Children.Add(Note("Put any text on the paperdoll, or a number that shows a global variable's value (kept up to date in the game). Drag one to move it, drag its yellow corner to resize it, Delete removes the picked one."));
        var addRow = new WrapPanel();
        void AddLabel(PaperdollLabelKind kind)
        {
            var label = new PaperdollLabel { Enabled = true, Kind = kind, X = 140, Y = 200, VariableId = kind == PaperdollLabelKind.Number ? _variables.Variables.FirstOrDefault()?.Id ?? -1 : -1 };
            pd.Labels.Add(label);
            _selected = label;
            BuildSettings();
            Redraw();
        }
        addRow.Children.Add(new Button { Content = "+ Text", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 6, 4) }.Also(b => b.Click += (_, _) => AddLabel(PaperdollLabelKind.Text)));
        addRow.Children.Add(new Button { Content = "+ Number", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 6, 4) }.Also(b => b.Click += (_, _) => AddLabel(PaperdollLabelKind.Number)));
        _settings.Children.Add(addRow);
        for (int i = 0; i < pd.Labels.Count; i++) LabelSettings(pd.Labels[i], i + 1);

        _settings.Children.Add(Heading("Buttons (Exult's paperdoll)"));
        _settings.Children.Add(Note("Each is a sprite whose frames are its states. Combat and the disk show on the player's paperdoll only, as in Exult."));
        ButtonSettings("Combat on / off - frames: peace, war", pd.CombatButton);
        ButtonSettings("Combat behavior - 10 frames: Nearest, Weakest, Strongest, Berserk, Protect, Defend, Flank, Flee, Random, Manual (the player's only)", pd.CombatModeButton);
        ButtonSettings("Heart - opens the stats paperdoll - frames: up, pressed", pd.StatsButton);
        ButtonSettings("Disk - save / load - frames: up, pressed", pd.DiskButton);
        ButtonSettings("Halo - the party protects this one - frames: off, on", pd.HaloButton);
        var stats = new Button { Content = "Stats paperdoll...", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        stats.Click += (_, _) => new StatsDollEditorWindow(_character) { Owner = this }.ShowDialog();
        _settings.Children.Add(stats);
        var painter = new Button { Content = "Format Painter...", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "This inventory paperdoll - its picture, slots and where they are, its checks, buttons and readouts - pasted onto the characters you pick." };
        painter.Click += (_, _) => FormatPainterWindow.Paint(this, _character, ProjectSession.Characters.Characters, c => c.Name, "Inventory paperdoll",
            only: new[] { "Paperdoll" }, unticked: new[] { "Paperdoll.StatsDoll" });
        _settings.Children.Add(painter);
    }

    // ================= Drawing =================

    private static readonly Dictionary<string, BitmapSource> Cache = new();

    private static BitmapSource? Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            Cache[path] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    private static BitmapSource? FirstFrame(SpriteEntry? sprite) =>
        sprite is null ? null : Load(sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath);

    /// <summary>The picture of something carried: an object's look, a book's or a container's sprite.</summary>
    private BitmapSource? PictureOf(InventoryItemInstance item) => item.Category switch
    {
        ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId) is { } obj ? FirstFrame(ObjectLooks.SpriteFor(obj, ObjectLooks.DefaultState(obj), _sprites)) : null,
        ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == item.ItemId) is { } book ? FirstFrame(ObjectLooks.Find(_sprites, book.SpriteGroup, book.SpriteName)) : null,
        ReferencedItemCategory.Container => _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId) is { } c ? FirstFrame(ObjectLooks.Find(_sprites, c.SpriteGroup, c.SpriteName)) : null,
        _ => null
    };

    private InventoryItemInstance? ItemIn(object slotKey)
    {
        int id = 0;
        bool has = slotKey switch
        {
            EquipmentSlot s => _character.EquippedSlots.TryGetValue(s, out id),
            CustomEquipmentSlot c => _character.CustomEquippedSlots.TryGetValue(c.Id, out id),
            _ => false
        };
        return has ? _character.Inventory.FirstOrDefault(i => i.InstanceId == id) : null;
    }

    private (double Carried, double Max) Weights()
    {
        double carried = 0;
        try { carried = ItemWeights.Carried(_character, _containers, _books, _objects); } catch { }
        int strength = 10;
        var stats = ProjectSession.StatsProvider?.Invoke();
        if (stats?.Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.Strength) is { } str && _character.StatValues.TryGetValue(str.Id, out var text) && int.TryParse(text, out int v)) strength = v;
        return (carried, ItemWeights.MaxCarry(strength));
    }

    /// <summary>A draggable handle over something drawn: its bounds, outlined, tagged with what it moves.</summary>
    private void AddHandle(object tag, double x, double y, double w, double h, Brush outline, string? label)
    {
        bool picked = Equals(tag, _selected);
        var handle = new Rectangle
        {
            Width = Math.Max(4, w), Height = Math.Max(4, h), Fill = Brushes.Transparent, Tag = tag, Cursor = Cursors.SizeAll,
            Stroke = picked ? Brushes.Yellow : outline, StrokeThickness = picked ? 1.5 : 0.75, StrokeDashArray = new DoubleCollection { 3, 2 }
        };
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
        Panel.SetZIndex(handle, 50);
        _canvas.Children.Add(handle);
        if (label is null) return;
        var text = new TextBlock { Text = label, FontSize = 6, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };
        Canvas.SetLeft(text, x);
        Canvas.SetTop(text, y - 8);
        Panel.SetZIndex(text, 51);
        _canvas.Children.Add(text);
    }

    private static (double W, double H) SizeOf(FrameworkElement element)
    {
        if (!double.IsNaN(element.Width) && !double.IsNaN(element.Height)) return (element.Width, element.Height);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (element.DesiredSize.Width, element.DesiredSize.Height);
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        var pd = _character.Paperdoll;
        var body = ObjectLooks.Find(_sprites, pd.BodySpriteGroup, pd.BodySpriteName) ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == pd.BodySpriteName);
        if (FirstFrame(body) is { } bodyBitmap)
        {
            var image = new Image { Source = bodyBitmap, Width = LayoutWidth, Height = LayoutHeight, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            _canvas.Children.Add(image);
        }

        void Slot(object key, string name, double x, double y, bool custom)
        {
            var outline = custom ? Brushes.LimeGreen : Brushes.DeepSkyBlue;
            if (ItemIn(key) is { } item && PictureOf(item) is { } picture)
            {
                var (w, h) = SpriteSizing.Quantize(picture.PixelWidth, picture.PixelHeight);
                var image = new Image { Source = picture, Width = w * ItemScale, Height = h * ItemScale, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(image, x);
                Canvas.SetTop(image, y);
                Panel.SetZIndex(image, 10);
                _canvas.Children.Add(image);
                AddHandle(key, x, y, w * ItemScale, h * ItemScale, outline, name);
                return;
            }
            // Empty: the area the game takes a drop on (a built-in slot's 40x40 around its spot, a custom slot's 60x22).
            var area = new Rectangle { Width = custom ? 60 : 40, Height = custom ? 22 : 40, Fill = new SolidColorBrush(custom ? Color.FromArgb(70, 60, 170, 60) : Color.FromArgb(70, 70, 130, 200)), IsHitTestVisible = false };
            double ax = custom ? x : x - 4, ay = custom ? y : y - 4;
            Canvas.SetLeft(area, ax);
            Canvas.SetTop(area, ay);
            Panel.SetZIndex(area, 10);
            _canvas.Children.Add(area);
            AddHandle(key, ax, ay, area.Width, area.Height, outline, name);
        }
        foreach (var slot in Enum.GetValues<EquipmentSlot>().Where(pd.IsSlotShown))
        {
            if (!pd.SlotPositions.TryGetValue(slot, out var pos)) pd.SlotPositions[slot] = pos = new PaperdollSlotPosition { X = 140, Y = 10 };
            Slot(slot, slot.ToString(), pos.X, pos.Y, custom: false);
        }
        foreach (var custom in pd.CustomSlots) Slot(custom, custom.Name, custom.X, custom.Y, custom: true);

        var (carried, max) = Weights();
        void Readout(string tag, PaperdollReadout r, string text, string label)
        {
            if (!r.Enabled) return;
            var element = PaperdollReadouts.Readout(r, text, _sprites, ItemScale);
            Panel.SetZIndex(element, 20);
            _canvas.Children.Add(element);
            var (w, h) = SizeOf(element);
            AddHandle(tag, Canvas.GetLeft(element), Canvas.GetTop(element), w, h, Brushes.Orange, label);
        }
        foreach (var (button, name) in Buttons())
        {
            if (!button.Enabled) continue;
            var sprite = ObjectLooks.Find(_sprites, button.Sprite.SpriteGroup, button.Sprite.SpriteName);
            if (FirstFrame(sprite) is { } picture)
            {
                var image = new Image { Source = picture, Width = picture.PixelWidth * ItemScale, Height = picture.PixelHeight * ItemScale, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(image, button.X);
                Canvas.SetTop(image, button.Y);
                Panel.SetZIndex(image, 20);
                _canvas.Children.Add(image);
                AddHandle(button, button.X, button.Y, image.Width, image.Height, Brushes.Violet, name);
            }
            else AddHandle(button, button.X, button.Y, 24, 24, Brushes.Violet, name + " (no sprite)");
        }
        foreach (var label in pd.Labels)
        {
            // In the editor a number shows 0 (its value comes from play); the handle names its variable.
            string text = PaperdollReadouts.LabelText(label, _variables, _ => 0);
            var element = PaperdollReadouts.Readout(label, text, _sprites, ItemScale);
            Panel.SetZIndex(element, 20);
            _canvas.Children.Add(element);
            var (w, h) = SizeOf(element);
            double x = Canvas.GetLeft(element), y = Canvas.GetTop(element);
            string name = label.Kind == PaperdollLabelKind.Text ? "text" : _variables.Variables.FirstOrDefault(v => v.Id == label.VariableId)?.Name ?? "(no variable)";
            AddHandle(label, x, y, w, h, Brushes.Gold, name);
            if (!Equals(_selected, label)) continue;
            var grip = new Rectangle { Width = 5, Height = 5, Fill = Brushes.Yellow, Stroke = Brushes.Black, StrokeThickness = 0.5, Cursor = Cursors.SizeNWSE, Tag = new ResizeGrip(label, Math.Max(1, h / Math.Max(1, label.Scale))) };
            Canvas.SetLeft(grip, x + w - 2.5);
            Canvas.SetTop(grip, y + h - 2.5);
            Panel.SetZIndex(grip, 60);
            _canvas.Children.Add(grip);
        }
        Readout("WeightCurrent", pd.WeightCurrent, $"{Math.Round(carried):0}", "carried");
        Readout("WeightMax", pd.WeightMax, $"{Math.Round(max):0}", "max");
        if (pd.WeightGauge.Enabled && PaperdollReadouts.Gauge(pd.WeightGauge, max <= 0 ? 0 : carried / max, _sprites, ItemScale) is { } gauge)
        {
            Panel.SetZIndex(gauge, 20);
            _canvas.Children.Add(gauge);
            var (w, h) = SizeOf(gauge);
            AddHandle("WeightGauge", pd.WeightGauge.X, pd.WeightGauge.Y, w, h, Brushes.Orange, "weight gauge");
        }
    }

    // ================= Dragging =================

    private Point PositionOf(object tag)
    {
        var pd = _character.Paperdoll;
        return tag switch
        {
            EquipmentSlot s => new Point(pd.SlotPositions[s].X, pd.SlotPositions[s].Y),
            CustomEquipmentSlot c => new Point(c.X, c.Y),
            "WeightCurrent" => new Point(pd.WeightCurrent.X, pd.WeightCurrent.Y),
            "WeightMax" => new Point(pd.WeightMax.X, pd.WeightMax.Y),
            "WeightGauge" => new Point(pd.WeightGauge.X, pd.WeightGauge.Y),
            PaperdollButton b => new Point(b.X, b.Y),
            PaperdollLabel l => new Point(l.X, l.Y),
            _ => default
        };
    }

    private void MoveTo(object tag, double x, double y)
    {
        var pd = _character.Paperdoll;
        x = Math.Round(Math.Clamp(x, 0, LayoutWidth));
        y = Math.Round(Math.Clamp(y, 0, LayoutHeight));
        switch (tag)
        {
            case EquipmentSlot s: pd.SlotPositions[s].X = x; pd.SlotPositions[s].Y = y; break;
            case CustomEquipmentSlot c: c.X = x; c.Y = y; break;
            case "WeightCurrent": pd.WeightCurrent.X = x; pd.WeightCurrent.Y = y; break;
            case "WeightMax": pd.WeightMax.X = x; pd.WeightMax.Y = y; break;
            case "WeightGauge": pd.WeightGauge.X = x; pd.WeightGauge.Y = y; break;
            case PaperdollButton b: b.X = x; b.Y = y; break;
            case PaperdollLabel l: l.X = x; l.Y = y; break;
        }
    }

    private void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        var hit = _canvas.InputHitTest(e.GetPosition(_canvas)) as FrameworkElement;
        if (hit?.Tag is ResizeGrip grip)
        {
            _dragging = grip;
            _dragStart = e.GetPosition(_canvas);
            _resizeFromScale = Math.Max(1, grip.Label.Scale);
            _canvas.CaptureMouse();
            return;
        }
        _selected = hit?.Tag;
        if (_selected is null) { Redraw(); return; }
        _dragging = _selected;
        _dragStart = e.GetPosition(_canvas);
        _dragFrom = PositionOf(_dragging);
        _canvas.CaptureMouse();
        Redraw();
    }

    private void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_canvas);
        if (_dragging is ResizeGrip grip)
        {
            // Taller by one size's height = one size up; a sprite font only draws at whole sizes.
            int scale = (int)Math.Clamp(Math.Round(_resizeFromScale + (p.Y - _dragStart.Y) / grip.HeightPerSize), 1, 8);
            if (scale != grip.Label.Scale) { grip.Label.Scale = scale; Redraw(); }
            return;
        }
        MoveTo(_dragging, _dragFrom.X + p.X - _dragStart.X, _dragFrom.Y + p.Y - _dragStart.Y);
        Redraw();
    }
}

internal static class FrameworkElementExtensions
{
    public static T Also<T>(this T element, Action<T> act) { act(element); return element; }
}