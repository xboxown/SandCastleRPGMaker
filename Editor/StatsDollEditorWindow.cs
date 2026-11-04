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
/// Character Editor > Stats paperdoll...: the character's stats window
/// (Exult's Stats_gump - Z in the game, or the heart on the inventory
/// paperdoll), laid out freely: a background and close button from the
/// Sprite Editor, and any number of elements placed anywhere - labels, the
/// name, any stat (built-in or custom) as a number, bar, pie or sprite
/// frames, ailment pictures shown while they last, and hunger. Drawn at
/// the background's own pixel size, as the game draws it.
/// </summary>
public sealed class StatsDollEditorWindow : Window
{
    private readonly CharacterDefinition _character;
    private StatsDollDefinition Doll => _character.Paperdoll.StatsDoll;
    private readonly SpriteDatabase _sprites = ProjectSession.Sprites;
    private readonly StatDatabase _stats = ProjectSession.StatsProvider?.Invoke() ?? new StatDatabase();
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)), ClipToBounds = true };
    private readonly ScaleTransform _zoom = new(4, 4);
    private readonly StackPanel _settings = new() { Margin = new Thickness(8) };
    private readonly StackPanel _elementProps = new();
    private readonly ListBox _list = new() { Height = 160 };
    private StatsDollElement? _picked;
    private object? _dragging;
    private Point _dragStart, _dragFrom;

    public StatsDollEditorWindow(CharacterDefinition character)
    {
        _character = character;
        Title = $"Stats paperdoll - {character.Name}";
        Width = Math.Min(SystemParameters.WorkArea.Width, 1280);
        Height = Math.Min(SystemParameters.WorkArea.Height, 900);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _canvas.LayoutTransform = _zoom;
        _canvas.MouseLeftButtonDown += Canvas_Down;
        _canvas.MouseMove += Canvas_Move;
        _canvas.MouseLeftButtonUp += (_, _) => { _dragging = null; _canvas.ReleaseMouseCapture(); };
        _list.SelectionChanged += (_, _) => { _picked = _list.SelectedItem is ListBoxItem { Tag: StatsDollElement e } ? e : null; ShowElement(); Redraw(); };
        var root = new DockPanel();
        var left = new ScrollViewer { Width = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _settings };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
            Content = new Border { Margin = new Thickness(12), BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = _canvas }
        });
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Delete && _picked is not null && Keyboard.FocusedElement is not TextBox) { Doll.Elements.Remove(_picked); _picked = null; RefreshList(); ShowElement(); Redraw(); } };
        Loaded += (_, _) => { BuildSettings(); RefreshList(); ShowElement(); Redraw(); };
    }

    // ================= Little builders =================

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

    private BitmapSource? Frame(SpriteLink link, int frame = 0)
    {
        var sprite = ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName);
        if (sprite is null) return null;
        return Load(sprite.Frames.Count > 0 ? sprite.Frames[Math.Clamp(frame, 0, sprite.Frames.Count - 1)].FilePath : sprite.FilePath);
    }

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
        void Commit() { if (double.TryParse(box.Text, out double v)) { set(v); Redraw(); RefreshList(); } box.Text = get().ToString("0.##"); }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return Labeled(label, box);
    }

    private FrameworkElement TextField(string label, double width, Func<string> get, Action<string> set)
    {
        var box = new TextBox { Width = width, Text = get() };
        box.TextChanged += (_, _) => { set(box.Text); Redraw(); };
        box.LostFocus += (_, _) => RefreshList();
        return Labeled(label, box);
    }

    private FrameworkElement Combo<T>(string label, IList<T> values, Func<T, string> name, Func<T> get, Action<T> set, double width = 150)
    {
        var combo = new ComboBox { Width = width, ItemsSource = values.Select(name).ToList() };
        combo.SelectedIndex = Math.Max(0, values.IndexOf(get()));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(values[combo.SelectedIndex]); ShowElement(); RefreshList(); Redraw(); } };
        return Labeled(label, combo);
    }

    private FrameworkElement SpritePicker(string label, Func<SpriteLink> get, Action<SpriteLink> set)
    {
        var sprites = _sprites.Groups.Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName)
            .SelectMany(g => g.Sprites.Select(s => (Group: g.Name, s.Name))).ToList();
        var labels = new List<string> { "(none)" };
        labels.AddRange(sprites.Select(s => $"{s.Name}   ({s.Group})"));
        var current = get();
        var combo = new ComboBox { Width = 240, ItemsSource = labels, SelectedIndex = Math.Max(0, sprites.FindIndex(s => s.Group == current.SpriteGroup && s.Name == current.SpriteName) + 1) };
        combo.SelectionChanged += (_, _) =>
        {
            set(combo.SelectedIndex <= 0 ? new SpriteLink() : new SpriteLink { SpriteGroup = sprites[combo.SelectedIndex - 1].Group, SpriteName = sprites[combo.SelectedIndex - 1].Name });
            Redraw();
        };
        return Labeled(label, combo);
    }

    private FrameworkElement FontPicker(PaperdollReadout r)
    {
        var fonts = new List<string> { "(in-game font)" };
        fonts.AddRange(_sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.FontsGroupName)?.Sprites.Select(s => s.Name) ?? Enumerable.Empty<string>());
        var combo = new ComboBox { Width = 160, ItemsSource = fonts, SelectedIndex = Math.Max(0, fonts.IndexOf(r.FontName)) };
        combo.SelectionChanged += (_, _) => { r.FontName = combo.SelectedIndex <= 0 ? "" : (string)combo.SelectedItem; Redraw(); };
        return Labeled("Font (Sprite Editor > Fonts)", combo);
    }

    private Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 6, 2) };
        button.Click += (_, _) => click();
        return button;
    }

    // ================= Settings =================

    private string StatName(int id) => _stats.Stats.FirstOrDefault(s => s.Id == id)?.Name ?? "(no stat)";

    private string Describe(StatsDollElement e) => e.Kind switch
    {
        StatsElementKind.Text => $"Text \"{e.Text}\"",
        StatsElementKind.Name => "Name",
        StatsElementKind.Stat => $"{StatName(e.StatId)} ({e.ShowAs})",
        StatsElementKind.Ailment => $"While {Ailments.Describe(e.Ailment).ToLowerInvariant()}",
        _ => e.OnlyWhenHungry ? "Hunger (while hungry)" : "Hunger (how full)"
    };

    private void RefreshList()
    {
        var picked = _picked;
        _list.Items.Clear();
        foreach (var e in Doll.Elements) _list.Items.Add(new ListBoxItem { Content = Describe(e), Tag = e });
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag == picked);
    }

    private void Add(StatsDollElement element)
    {
        element.Id = Doll.NextElementId();
        element.Style.X = 10; element.Style.Y = 10 + Doll.Elements.Count * 9 % 100;
        Doll.Elements.Add(element);
        _picked = element;
        RefreshList();
        ShowElement();
        Redraw();
    }

    /// <summary>This character's controls: its own paperdoll in the PaperDoll Editor, as big as the window (made the first time).</summary>
    private void EditControls()
    {
        if (ProjectSession.ObjectsProvider?.Invoke() is not { } objects || ProjectSession.ResourcesProvider?.Invoke() is not { } resources
            || ProjectSession.BooksProvider?.Invoke() is not { } books || ProjectSession.ContainersProvider?.Invoke() is not { } containers) return;
        if (objects.DollDesigns.FirstOrDefault(d => d.Id == Doll.ControlsDesignId) is null)
        {
            var background = ObjectLooks.Find(_sprites, Doll.Background.SpriteGroup, Doll.Background.SpriteName)?.Frames.FirstOrDefault() is { } frame && File.Exists(frame.FilePath)
                ? BitmapFrame.Create(new Uri(frame.FilePath), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None) : null;
            var design = new DollDesign { Id = objects.NextDollDesignId(), Name = $"Stats controls: {_character.Name}", Width = background?.PixelWidth ?? 160, Height = background?.PixelHeight ?? 120 };
            objects.DollDesigns.Add(design);
            Doll.ControlsDesignId = design.Id;
        }
        new PaperDollEditorWindow(objects, _sprites, resources, books, containers, ProjectSession.VariablesProvider?.Invoke(), _stats, Doll.ControlsDesignId) { Owner = this }.ShowDialog();
    }

    private void BuildSettings()
    {
        _settings.Children.Clear();
        var zoom = new ComboBox { Width = 100, ItemsSource = new[] { "2x", "3x", "4x", "5x", "6x" }, SelectedItem = $"{_zoom.ScaleX:0}x" };
        zoom.SelectionChanged += (_, _) => { if (zoom.SelectedItem is string z && double.TryParse(z.TrimEnd('x'), out double v)) _zoom.ScaleX = _zoom.ScaleY = v; };
        _settings.Children.Add(Labeled("Zoom", zoom));
        _settings.Children.Add(Note("Ultima VII's stats window: Z opens the player's, the heart on a paperdoll opens that character's. Drag anything on the right to place it; Delete removes the picked element."));
        _settings.Children.Add(SpritePicker("Background (its size is the window's size)", () => Doll.Background, v => Doll.Background = v));
        var close = new WrapPanel();
        close.Children.Add(SpritePicker("Close button (2 frames: up, pressed)", () => Doll.CloseButton, v => Doll.CloseButton = v));
        _settings.Children.Add(close);
        var others = ProjectSession.Characters.Characters.Where(c => c != _character).ToList();
        if (others.Count > 0)
        {
            var copy = new ComboBox { Width = 200, ItemsSource = others.Select(c => c.Name).ToList() };
            var row = new WrapPanel();
            row.Children.Add(Labeled("Copy the layout of", copy));
            row.Children.Add(MakeButton("Copy", () =>
            {
                if (copy.SelectedIndex < 0) return;
                var json = System.Text.Json.JsonSerializer.Serialize(others[copy.SelectedIndex].Paperdoll.StatsDoll);
                _character.Paperdoll.StatsDoll = System.Text.Json.JsonSerializer.Deserialize<StatsDollDefinition>(json) ?? new StatsDollDefinition();
                if (ProjectSession.ObjectsProvider?.Invoke() is { } objects && objects.DollDesigns.FirstOrDefault(d => d.Id == Doll.ControlsDesignId) is { } theirs) // its own copy of the controls, to change for this character alone
                {
                    var mine = System.Text.Json.JsonSerializer.Deserialize<DollDesign>(System.Text.Json.JsonSerializer.Serialize(theirs))!;
                    mine.Id = objects.NextDollDesignId();
                    mine.Name = $"Stats controls: {_character.Name}";
                    objects.DollDesigns.Add(mine);
                    Doll.ControlsDesignId = mine.Id;
                }
                _picked = null; BuildSettings(); RefreshList(); ShowElement(); Redraw();
            }));
            _settings.Children.Add(row);
            _settings.Children.Add(MakeButton("Format Painter... (this layout onto others)", () =>
                FormatPainterWindow.Paint(this, _character, ProjectSession.Characters.Characters, c => c.Name, "Stats paperdoll", only: new[] { "Paperdoll.StatsDoll" })));
        }
        _settings.Children.Add(Heading("Controls and parts"));
        _settings.Children.Add(Note("Yes / OK and No / Cancel buttons, text boxes, check boxes, radio buttons, drop-down lists, list boxes, progress bars, and every other part: this character's own, laid over the window. They open in the PaperDoll Editor, with all their properties and events."));
        _settings.Children.Add(MakeButton("Controls... (PaperDoll Editor)", EditControls));
        _settings.Children.Add(Heading("Elements"));
        var adds = new WrapPanel();
        adds.Children.Add(MakeButton("+ Text", () => Add(new StatsDollElement { Kind = StatsElementKind.Text, Text = "Strength:" })));
        adds.Children.Add(MakeButton("+ Name", () => Add(new StatsDollElement { Kind = StatsElementKind.Name })));
        adds.Children.Add(MakeButton("+ Stat", () => Add(new StatsDollElement { Kind = StatsElementKind.Stat, StatId = _stats.Stats.FirstOrDefault()?.Id ?? -1, Style = new PaperdollReadout { Enabled = true, Align = ReadoutAlign.Right } })));
        adds.Children.Add(MakeButton("+ Ailment picture", () => Add(new StatsDollElement { Kind = StatsElementKind.Ailment })));
        adds.Children.Add(MakeButton("+ Hunger", () => Add(new StatsDollElement { Kind = StatsElementKind.Hunger })));
        adds.Children.Add(MakeButton("+ Every stat", () =>
        {
            // The Ultima VII layout to start from: a label on the left and its value on the right, a line each.
            double y = 14;
            foreach (var stat in _stats.Stats)
            {
                Add(new StatsDollElement { Kind = StatsElementKind.Text, Text = stat.Name + ":", Style = new PaperdollReadout { Enabled = true, X = 8, Y = y } });
                Add(new StatsDollElement { Kind = StatsElementKind.Stat, StatId = stat.Id, Style = new PaperdollReadout { Enabled = true, X = 150, Y = y, Align = ReadoutAlign.Right } });
                y += 9;
            }
        }));
        _settings.Children.Add(adds);
        _settings.Children.Add(_list);
        _settings.Children.Add(_elementProps);
    }

    private void ShowElement()
    {
        _elementProps.Children.Clear();
        if (_picked is not { } e) { _elementProps.Children.Add(Note("Pick an element in the list (or click it on the right).")); return; }
        _elementProps.Children.Add(Heading(Describe(e)));
        bool text = e.Kind is StatsElementKind.Text or StatsElementKind.Name || (e.Kind == StatsElementKind.Stat && e.ShowAs == StatShowAs.Number);
        if (e.Kind == StatsElementKind.Text) _elementProps.Children.Add(TextField("Says", 240, () => e.Text, v => e.Text = v));
        if (e.Kind == StatsElementKind.Stat)
        {
            var row = new WrapPanel();
            row.Children.Add(Combo("Stat", _stats.Stats.Select(s => s.Id).ToList(), StatName, () => e.StatId, v => e.StatId = v, 160));
            row.Children.Add(Combo("Shown as", Enum.GetValues<StatShowAs>().ToList(), v => v == StatShowAs.SpriteFrames ? "Sprite frames" : v.ToString(), () => e.ShowAs, v => e.ShowAs = v, 120));
            _elementProps.Children.Add(row);
            if (e.ShowAs != StatShowAs.Number)
            {
                var range = new WrapPanel();
                range.Children.Add(NumberBox("From", () => e.Min, v => e.Min = (int)v));
                range.Children.Add(NumberBox("To", () => e.Max, v => e.Max = (int)v));
                var ids = new List<int> { -1 };
                ids.AddRange(_stats.Stats.Select(s => s.Id));
                range.Children.Add(Combo("...or to this stat's value", ids, id => id < 0 ? "(the number)" : StatName(id), () => e.MaxStatId, v => e.MaxStatId = v, 150));
                _elementProps.Children.Add(range);
            }
        }
        if (e.Kind == StatsElementKind.Ailment)
        {
            _elementProps.Children.Add(Combo("While they are", Ailments.All.Concat(new[] { Ailment.Invisible, Ailment.Protected }).ToList(), Ailments.Describe, () => e.Ailment, v => e.Ailment = v, 150));
            _elementProps.Children.Add(SpritePicker("Picture (shown while it lasts)", () => e.Gauge.Sprite, v => e.Gauge.Sprite = v));
        }
        if (e.Kind == StatsElementKind.Hunger)
        {
            var row = new WrapPanel();
            var only = new CheckBox { Content = "Only while hungry (a picture); else always, as how full they are", IsChecked = e.OnlyWhenHungry, Margin = new Thickness(0, 4, 0, 4) };
            only.Checked += (_, _) => { e.OnlyWhenHungry = true; ShowElement(); RefreshList(); Redraw(); };
            only.Unchecked += (_, _) => { e.OnlyWhenHungry = false; ShowElement(); RefreshList(); Redraw(); };
            row.Children.Add(only);
            _elementProps.Children.Add(row);
            if (e.OnlyWhenHungry) _elementProps.Children.Add(SpritePicker("Picture", () => e.Gauge.Sprite, v => e.Gauge.Sprite = v));
            else _elementProps.Children.Add(Combo("Shown as", new List<StatShowAs> { StatShowAs.Bar, StatShowAs.Pie, StatShowAs.SpriteFrames }, v => v == StatShowAs.SpriteFrames ? "Sprite frames" : v.ToString(), () => e.ShowAs == StatShowAs.Number ? StatShowAs.Bar : e.ShowAs, v => e.ShowAs = v, 120));
        }
        bool gauge = (e.Kind == StatsElementKind.Stat && e.ShowAs != StatShowAs.Number) || (e.Kind == StatsElementKind.Hunger && !e.OnlyWhenHungry);
        if (gauge)
        {
            if (e.ShowAs == StatShowAs.SpriteFrames) _elementProps.Children.Add(SpritePicker("Sprite (first frame empty ... last frame full)", () => e.Gauge.Sprite, v => e.Gauge.Sprite = v));
            else if (e.ShowAs == StatShowAs.Icons)
            {
                _elementProps.Children.Add(SpritePicker("Icon (frames: full ... empty - 2: full, empty; 3: full, half, empty)", () => e.Gauge.Sprite, v => e.Gauge.Sprite = v));
                var icons = new WrapPanel();
                icons.Children.Add(NumberBox("One is worth", () => e.Gauge.IconValue, v => e.Gauge.IconValue = Math.Max(1, (int)v)));
                icons.Children.Add(NumberBox("To a row", () => e.Gauge.IconsPerRow, v => e.Gauge.IconsPerRow = Math.Max(1, (int)v)));
                icons.Children.Add(NumberBox("Gap", () => e.Gauge.IconGap, v => e.Gauge.IconGap = Math.Max(0, (int)v)));
                _elementProps.Children.Add(icons);
            }
            else
            {
                var g = new WrapPanel();
                g.Children.Add(NumberBox("Width", () => e.Gauge.Width, v => e.Gauge.Width = Math.Clamp(v, 2, 400)));
                g.Children.Add(NumberBox("Height", () => e.Gauge.Height, v => e.Gauge.Height = Math.Clamp(v, 2, 400)));
                g.Children.Add(TextField("Full colour", 80, () => e.Gauge.FillColor, v => e.Gauge.FillColor = v));
                g.Children.Add(TextField("Empty colour", 80, () => e.Gauge.BackColor, v => e.Gauge.BackColor = v));
                _elementProps.Children.Add(g);
            }
        }
        if (text)
        {
            var style = new WrapPanel();
            style.Children.Add(FontPicker(e.Style));
            style.Children.Add(NumberBox("Size x", () => e.Style.Scale, v => e.Style.Scale = (int)Math.Clamp(v, 1, 8)));
            style.Children.Add(TextField("Tint (#RRGGBB)", 80, () => e.Style.Tint, v => e.Style.Tint = v));
            style.Children.Add(Combo("Lined up by its", Enum.GetValues<ReadoutAlign>().ToList(), v => v == ReadoutAlign.Left ? "Left edge" : v == ReadoutAlign.Right ? "Right edge" : "Centre", () => e.Style.Align, v => e.Style.Align = v, 100));
            _elementProps.Children.Add(style);
        }
        var place = new WrapPanel();
        place.Children.Add(NumberBox("X", () => e.Style.X, v => e.Style.X = v));
        place.Children.Add(NumberBox("Y", () => e.Style.Y, v => e.Style.Y = v));
        place.Children.Add(MakeButton("Delete", () => { Doll.Elements.Remove(e); _picked = null; RefreshList(); ShowElement(); Redraw(); }));
        _elementProps.Children.Add(place);
    }

    // ================= Drawing =================

    private string SampleValue(StatsDollElement e) =>
        _stats.Stats.FirstOrDefault(s => s.Id == e.StatId) is { } stat && _character.StatValues.TryGetValue(stat.Id, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "18";

    private double Number(int statId, double fallback) =>
        _character.StatValues.TryGetValue(statId, out var v) && double.TryParse(v, out double d) ? d : fallback;

    private void Redraw()
    {
        _canvas.Children.Clear();
        var background = Frame(Doll.Background);
        _canvas.Width = background?.PixelWidth ?? 160;
        _canvas.Height = background?.PixelHeight ?? 120;
        if (background is not null)
        {
            var image = new Image { Source = background, Width = background.PixelWidth, Height = background.PixelHeight, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            _canvas.Children.Add(image);
        }
        foreach (var e in Doll.Elements)
        {
            var visual = StatsDollLook.Element(e, _sprites, _character.Name, SampleValue(e), id => Number(id, 18), fraction: 0.6, ailmentActive: true, hungry: true);
            if (visual is null)
            {
                visual = new Rectangle { Width = 12, Height = 8, Fill = new SolidColorBrush(Color.FromArgb(90, 200, 120, 60)) };
                Canvas.SetLeft(visual, e.Style.X);
                Canvas.SetTop(visual, e.Style.Y);
            }
            visual.IsHitTestVisible = false;
            _canvas.Children.Add(visual);
            var (w, h) = SizeOf(visual);
            AddHandle(e, Canvas.GetLeft(visual), Canvas.GetTop(visual), w, h);
        }
        if (Frame(Doll.CloseButton) is { } closeBitmap)
        {
            var close = new Image { Source = closeBitmap, Width = closeBitmap.PixelWidth, Height = closeBitmap.PixelHeight, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(close, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(close, Doll.CloseX);
            Canvas.SetTop(close, Doll.CloseY);
            _canvas.Children.Add(close);
            AddHandle("Close", Doll.CloseX, Doll.CloseY, closeBitmap.PixelWidth, closeBitmap.PixelHeight);
        }
    }

    private static (double W, double H) SizeOf(FrameworkElement element)
    {
        if (!double.IsNaN(element.Width) && !double.IsNaN(element.Height)) return (element.Width, element.Height);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (element.DesiredSize.Width, element.DesiredSize.Height);
    }

    private void AddHandle(object tag, double x, double y, double w, double h)
    {
        bool picked = ReferenceEquals(tag, _picked);
        var handle = new Rectangle
        {
            Width = Math.Max(2, w), Height = Math.Max(2, h), Fill = Brushes.Transparent, Tag = tag, Cursor = Cursors.SizeAll,
            Stroke = picked ? Brushes.Yellow : new SolidColorBrush(Color.FromArgb(120, 255, 165, 0)), StrokeThickness = picked ? 0.5 : 0.25
        };
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
        Panel.SetZIndex(handle, 50);
        _canvas.Children.Add(handle);
    }

    private void Canvas_Down(object sender, MouseButtonEventArgs e)
    {
        var hit = (_canvas.InputHitTest(e.GetPosition(_canvas)) as FrameworkElement)?.Tag;
        if (hit is StatsDollElement element) { _picked = element; RefreshList(); ShowElement(); }
        if (hit is null) return;
        _dragging = hit;
        _dragStart = e.GetPosition(_canvas);
        _dragFrom = hit is StatsDollElement el ? new Point(el.Style.X, el.Style.Y) : new Point(Doll.CloseX, Doll.CloseY);
        _canvas.CaptureMouse();
        Redraw();
    }

    private void Canvas_Move(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_canvas);
        double x = Math.Round(_dragFrom.X + p.X - _dragStart.X), y = Math.Round(_dragFrom.Y + p.Y - _dragStart.Y);
        if (_dragging is StatsDollElement el)
        {
            // A right- or centre-aligned text keeps its anchor: move the anchor by the drag.
            el.Style.X = x; el.Style.Y = y;
        }
        else { Doll.CloseX = x; Doll.CloseY = y; }
        Redraw();
    }
}

/// <summary>How a stats paperdoll element looks - one rule for the designer and the game (at the background's own pixels).</summary>
internal static class StatsDollLook
{
    public static FrameworkElement? Element(StatsDollElement e, SpriteDatabase sprites, string name, string statText, Func<int, double> statValue, double? fraction, bool ailmentActive, bool hungry)
    {
        switch (e.Kind)
        {
            case StatsElementKind.Text: return PaperdollReadouts.Readout(e.Style, e.Text, sprites, 1);
            case StatsElementKind.Name: return PaperdollReadouts.Readout(e.Style, name, sprites, 1);
            case StatsElementKind.Stat:
                if (e.ShowAs == StatShowAs.Number) return PaperdollReadouts.Readout(e.Style, statText, sprites, 1);
                double value = statValue(e.StatId), max = e.MaxStatId >= 0 ? statValue(e.MaxStatId) : e.Max;
                double full = fraction ?? (max - e.Min <= 0 ? 0 : (value - e.Min) / (max - e.Min));
                return Gauge(e, full, sprites, value: fraction is { } f ? f * (max - e.Min) : value - e.Min, range: max - e.Min);
            case StatsElementKind.Ailment:
                return ailmentActive ? Gauge(e, 0, sprites, picture: true) : null;
            default:
                if (e.OnlyWhenHungry) return hungry ? Gauge(e, 0, sprites, picture: true) : null;
                return Gauge(e, fraction ?? 0, sprites);
        }
    }

    private static FrameworkElement? Gauge(StatsDollElement e, double fraction, SpriteDatabase sprites, bool picture = false, double value = 0, double range = 0)
    {
        var g = e.Gauge;
        g.X = e.Style.X;
        g.Y = e.Style.Y;
        g.Kind = picture ? GaugeKind.SpriteFrames : PaperdollReadouts.KindOf(e.ShowAs);
        return PaperdollReadouts.Gauge(g, picture ? 0 : fraction, sprites, 1, value, range);
    }
}