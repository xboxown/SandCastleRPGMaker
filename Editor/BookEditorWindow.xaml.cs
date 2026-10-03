using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class BookEditorWindow : Window
{
    public BookDatabase Database { get; private set; } = new();

    private readonly SpriteDatabase _sprites;
    private readonly FactionDatabase _factions;
    private readonly ResourceDatabase _resources;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly VariableDatabase _variables;
    private readonly StatDatabase _stats;

    /// <summary>Sentinel entries prepended to Faction/sound dropdowns so there's an explicit, clickable way to clear the selection back to "none" - never added to the real database, just displayed as an extra choice.</summary>
    private static readonly Faction NoneFaction = new() { Id = -1, Name = "(None)" };
    private static readonly ResourceEntry NoneSound = new() { Id = -1, Name = "(None)" };

    private BookData? _current;
    private int _currentPageIndex;
    private bool _suppress;

    private enum Tool { Select, Rectangle, Ellipse, Polygon, Freehand }
    private Tool _currentTool = Tool.Select;

    private readonly List<Point> _drawingPoints = new();
    private Point? _dragStartPoint;
    private bool _isDrawingShape;
    private Shape? _tempShape;

    private BookTextFrame? _selectedFrame;
    private Point? _dragStartMouse;
    private List<BookPoint>? _dragOriginalPoints;

    // Resize handles for the selected frame.
    private readonly Dictionary<string, Rectangle> _resizeHandles = new();
    private string? _resizingCorner;
    private Point? _resizeStartMouse;
    private List<BookPoint>? _resizeOriginalPoints;
    private Rect _resizeOriginalBbox;

    private const double CanvasWidth = 700, CanvasHeight = 400;

    private readonly Dictionary<BookTextFrame, (Shape Outline, Canvas Overlay)> _frameVisuals = new();

    private BookPageTextPopup? _activePopup;
    private BookTextFrame? _activePopupFrame;

    // Bump this on every new build shipped, so the diagnostic output can
    // confirm which version is actually running - rules out "stale build"
    // as a variable when debugging.
    private const string DiagnosticBuildNotes = "(Found what is very likely the real root cause of the whole bug cascade from the latest report - a chest that vanishes, reappears inside another chest, replaces an unrelated drop, and a real couple-second delay on Escape. BeginDrag, the one real shared entry point for dragging a map item, a paperdoll slot, or an item out of a container alike, always subscribed its two real handlers without ever first unsubscribing. If any previous drag genuinely skipped its own cleanup for any real reason - a double-clicks own first click starting one whose matching release never got the chance to resolve before the second clicks own open-the-gump branch ran, say - the next real drag stacked a second subscription on top of the first, so one real mouse event fired the handler twice, using whatever item/visual happened to be set most recently for both, silently finalizing a stale one instead of, or alongside, the one actually intended. This also plausibly explains the real delay - every stacked, redundant handler call is real, genuine extra work on the very same thread Escape has to wait its turn on. Unsubscribing first makes double-subscription structurally impossible now, and a real safety net rescues any already-dangling drag visual back onto the map at its own current real position rather than leaving it a genuinely invisible, orphaned stray.)";
    private static string DiagnosticBuildVersion => $"{BuildInfo.CurrentBuild} {DiagnosticBuildNotes}";

    private Dictionary<(int PageIndex, int FrameId), BookPaginationEngine.SliceLocation> _sliceMap = new();

    // Standard swatch palette for frame outlines (color is purely visual now,
    // decoupled from rank - rank is simply _current.Frames list order).
    // Black excluded per request (blends into the mostly-black canvas background).
    private static readonly Color[] StandardFrameSwatches =
    {
        Colors.DimGray, Colors.Gray, Colors.DarkGray, Colors.Silver, Colors.White,
        Colors.DarkRed, Colors.Red, Colors.OrangeRed, Colors.Orange, Colors.Gold, Colors.Yellow,
        Colors.DarkGreen, Colors.Green, Colors.SeaGreen, Colors.LimeGreen, Colors.YellowGreen, Colors.PaleGreen,
        Colors.Navy, Colors.DarkBlue, Colors.Blue, Colors.RoyalBlue, Colors.SkyBlue, Colors.LightBlue,
        Colors.Indigo, Colors.Purple, Colors.MediumPurple, Colors.Violet, Colors.Orchid, Colors.Plum,
        Colors.SaddleBrown, Colors.Brown, Colors.Sienna, Colors.Peru, Colors.Tan, Colors.Wheat
    };

    private Color _currentFrameColor = Colors.Red;

    // Frame copy/paste clipboard - shape + background color, NOT outline
    // color (that must stay unique per frame, so paste auto-picks an unused one).
    private (FrameShapeType ShapeType, List<BookPoint> Points, string EditorBackgroundColorHex)? _frameClipboard;

    // "Filter By Group" state for each of the 4 sprite fields - session-only,
    // not persisted. Defaults to whatever group the currently-saved sprite
    // belongs to when a book is loaded (see BookListBox_SelectionChanged).
    private string? _paperDollFilter;
    private string? _spriteFilter;
    private string? _chatteredFilter;
    private string? _destroyedFilter;

    private EventEditorControl? _eventEditorControl;

    /// <summary>Live sprite animators currently attached to the main canvas overlay - stopped and rebuilt fresh every RefreshFrameTextsForCurrentPage, and stopped for good when this window closes.</summary>
    private readonly List<SpriteAnimator> _overlaySpriteAnimators = new();

    public BookEditorWindow(BookDatabase? existing, SpriteDatabase sprites, FactionDatabase factions, ResourceDatabase resources, ObjectDatabase objects, VariableDatabase variables, StatDatabase stats, ContainerDatabase containers)
    {
        InitializeComponent();
        SolidSize.SizeEdited += (_, _) =>
        {
            if (_current is null) return;
            _current.SizeX = SolidSize.SizeX;
            _current.SizeY = SolidSize.SizeY;
            _current.SizeZ = SolidSize.SizeZ;
            _current.SolidOffsetX = SolidSize.OffsetX;
            _current.SolidOffsetY = SolidSize.OffsetY;
        };
        Title += $" [{BuildInfo.CurrentBuild}]";
        if (existing is not null) Database = existing;
        _sprites = sprites;
        _factions = factions;
        _resources = resources;
        _objects = objects;
        _containers = containers;
        _variables = variables;
        _stats = stats;

        BookConditionsControl.SetDatabases(Database, _objects, _containers, _sprites, _variables, _stats);

        BuildFrameSwatchGrid();
        UpdateFrameColorPreview();

        PreviewCanvas.Focusable = true;
        PreviewCanvas.KeyDown += PreviewCanvas_KeyDown;

        Closing += (_, _) =>
        {
            foreach (var animator in _overlaySpriteAnimators) animator.Stop();
        };

        RefreshList();
    }

    private void BuildFrameSwatchGrid()
    {
        FrameSwatchGrid.Children.Clear();
        foreach (var color in StandardFrameSwatches)
        {
            var swatch = new Rectangle
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(2),
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.Gray,
                StrokeThickness = 0.5,
                Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                _currentFrameColor = color;
                UpdateFrameColorPreview();
                FrameColorPopup.IsOpen = false;
            };
            FrameSwatchGrid.Children.Add(swatch);
        }
    }

    private void UpdateFrameColorPreview()
    {
        FrameColorPreview.Fill = new SolidColorBrush(_currentFrameColor);
    }

    private void FrameColorButton_Click(object sender, RoutedEventArgs e)
    {
        FrameColorPopup.IsOpen = !FrameColorPopup.IsOpen;
    }

    private void FrameCustomColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AllowFullOpen = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            _currentFrameColor = Color.FromArgb(c.A, c.R, c.G, c.B);
            UpdateFrameColorPreview();
        }
        FrameColorPopup.IsOpen = false;
    }

    private void RefreshList()
    {
        BookListBox.ItemsSource = null;
        BookListBox.ItemsSource = Database.Books;
    }

    private void NewBook_Click(object sender, RoutedEventArgs e)
    {
        var book = Database.CreateNew();
        RefreshList();
        BookListBox.SelectedItem = book;
    }

    private void DeleteBook_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        Database.Books.Remove(_current);
        _current = null;
        RefreshList();
        EditorPanel.IsEnabled = false;
    }

    private void BookListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClosePopup();
        _current = BookListBox.SelectedItem as BookData;
        if (_current is null) { EditorPanel.IsEnabled = false; return; }

        // A different book has a different event, so the lazily-attached
        // shared engine (if one was ever built) no longer applies - drop it
        // and let it re-attach fresh next time the Event tab is clicked,
        // per the design: it's fine for this to visibly reload every time.
        _eventEditorControl = null;
        EventEnginePlaceholder.Content = null;
        MainTabControl.SelectedIndex = 0;
        BookConditionsControl.CurrentBookId = _current.Id;
        BookConditionsControl.CurrentBookLocalVariables = _current.LocalVariables;

        _suppress = true;

        NameBox.Text = _current.Name;
        WeightBox.Text = _current.Weight.ToString();
        VolumeBox.Text = _current.Volume.ToString();
        PriceBox.Text = _current.Price.ToString();
        DestructibleCheck.IsChecked = _current.Destructible;
        CanBeSoldCheck.IsChecked = _current.CanBeSold;
        CanBeTradedCheck.IsChecked = _current.CanBeTraded;
        CanBeDroppedCheck.IsChecked = _current.CanBeDropped;
        IsStackableCheck.IsChecked = _current.IsStackable;
        AllowElevationCheck.IsChecked = _current.AllowElevation;
        OccludesCheck.IsChecked = _current.Occludes;
        IsSolidCheck.IsChecked = _current.IsSolid;
        IsTwoHandedCheck.IsChecked = _current.IsTwoHanded;
        SolidSize.Load(SolidSizeEditor.LoadSpriteFrame(_sprites, _current.SpriteGroup, _current.SpriteName), _current.SizeX, _current.SizeY, _current.SizeZ, _current.SolidOffsetX, _current.SolidOffsetY, _current.IsSolid);

        _paperDollFilter = string.IsNullOrEmpty(_current.PaperDollSpriteGroup) ? null : _current.PaperDollSpriteGroup;
        PaperDollFilterCheck.IsChecked = _paperDollFilter is not null;
        PopulateSpriteChoices(PaperDollNameCombo, _paperDollFilter, _current.PaperDollSpriteGroup, _current.PaperDollSpriteName);

        _spriteFilter = string.IsNullOrEmpty(_current.SpriteGroup) ? null : _current.SpriteGroup;
        SpriteFilterCheck.IsChecked = _spriteFilter is not null;
        PopulateSpriteChoices(SpriteNameCombo, _spriteFilter, _current.SpriteGroup, _current.SpriteName);

        _chatteredFilter = string.IsNullOrEmpty(_current.ChatteredSpriteGroup) ? null : _current.ChatteredSpriteGroup;
        ChatteredFilterCheck.IsChecked = _chatteredFilter is not null;
        PopulateSpriteChoices(ChatteredSpriteNameCombo, _chatteredFilter, _current.ChatteredSpriteGroup, _current.ChatteredSpriteName);

        _destroyedFilter = string.IsNullOrEmpty(_current.DestroyedSpriteGroup) ? null : _current.DestroyedSpriteGroup;
        DestroyedFilterCheck.IsChecked = _destroyedFilter is not null;
        PopulateSpriteChoices(DestroyedSpriteNameCombo, _destroyedFilter, _current.DestroyedSpriteGroup, _current.DestroyedSpriteName);

        BelongToCombo.ItemsSource = new List<string> { "(None)" };
        BelongToCombo.SelectedIndex = 0;

        // (None) is prepended so there's an explicit, clickable way to clear
        // a Faction/sound back to "none" - without it, once you'd picked
        // something there was no way back except editing the file by hand.
        var factionChoices = new List<Faction> { NoneFaction };
        factionChoices.AddRange(_factions.Factions);
        FactionCombo.ItemsSource = factionChoices;
        FactionCombo.SelectedItem = factionChoices.FirstOrDefault(f => f.Id == _current.FactionId) ?? NoneFaction;

        // Text Font: the system font as before, or any font from the Sprite Editor's Fonts group.
        var fontChoices = new List<string> { DefaultTextFont };
        fontChoices.AddRange(_sprites.FontNames());
        TextFontCombo.ItemsSource = fontChoices;
        TextFontCombo.SelectedItem = fontChoices.Contains(_current.TextFontSpriteName) ? _current.TextFontSpriteName : DefaultTextFont;

        var soundChoices = new List<ResourceEntry> { NoneSound };
        soundChoices.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        OpenSoundCombo.ItemsSource = new List<ResourceEntry>(soundChoices); // own list: ComboBoxes sharing one list silently change each other's selection in WPF
        CloseSoundCombo.ItemsSource = new List<ResourceEntry>(soundChoices); // own list: ComboBoxes sharing one list silently change each other's selection in WPF
        FlipSoundCombo.ItemsSource = new List<ResourceEntry>(soundChoices); // own list: ComboBoxes sharing one list silently change each other's selection in WPF
        DestructibleSoundCombo.ItemsSource = new List<ResourceEntry>(soundChoices); // own list: ComboBoxes sharing one list silently change each other's selection in WPF
        OpenSoundCombo.SelectedItem = soundChoices.FirstOrDefault(s => s.Id == _current.OpenSoundResourceId) ?? NoneSound;
        CloseSoundCombo.SelectedItem = soundChoices.FirstOrDefault(s => s.Id == _current.CloseSoundResourceId) ?? NoneSound;
        FlipSoundCombo.SelectedItem = soundChoices.FirstOrDefault(s => s.Id == _current.PageFlipSoundResourceId) ?? NoneSound;
        DestructibleSoundCombo.SelectedItem = soundChoices.FirstOrDefault(s => s.Id == _current.DestructibleSoundResourceId) ?? NoneSound;

        _suppress = false;
        EditorPanel.IsEnabled = true;

        UpdatePaperDollImage();
        _currentPageIndex = 0;
        RebuildFrameLayout();
    }

    /// <summary>One sprite as it appears in a Sprite ComboBox - carries its real Group so selecting it always tells us exactly which sprite was picked, whether the list is filtered or not.</summary>
    private sealed class SpriteChoice
    {
        public string Group { get; }
        public string Name { get; }
        private readonly string _display;
        public SpriteChoice(string group, string name, bool showGroup)
        {
            Group = group;
            Name = name;
            _display = showGroup ? $"{group}: {name}" : name;
        }
        public override string ToString() => _display;
    }

    /// <summary>
    /// Fills a Sprite ComboBox. If filterGroup is set, shows only that
    /// group's sprites (plain names). If null (unfiltered), shows every
    /// sprite across every group, labeled "Group: Name" so they stay
    /// distinguishable. Re-selects whichever sprite matches
    /// selectedGroup/selectedName if it's present in the resulting list.
    /// </summary>
    private void PopulateSpriteChoices(ComboBox combo, string? filterGroup, string? selectedGroup, string? selectedName)
    {
        List<SpriteChoice> choices;
        if (!string.IsNullOrEmpty(filterGroup))
        {
            var group = _sprites.Groups.FirstOrDefault(g => g.Name == filterGroup);
            choices = group?.Sprites.Select(s => new SpriteChoice(filterGroup, s.Name, showGroup: false)).ToList() ?? new List<SpriteChoice>();
        }
        else
        {
            choices = _sprites.Groups.SelectMany(g => g.Sprites.Select(s => new SpriteChoice(g.Name, s.Name, showGroup: true))).ToList();
        }
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(c => c.Group == selectedGroup && c.Name == selectedName);
    }

    private void UpdatePaperDollImage()
    {
        if (_current is null) return;
        var group = _sprites.Groups.FirstOrDefault(g => g.Name == _current.PaperDollSpriteGroup);
        var sprite = group?.Sprites.FirstOrDefault(s => s.Name == _current.PaperDollSpriteName);

        if (sprite is null || !File.Exists(sprite.FilePath)) { PaperDollImage.Source = null; return; }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(sprite.FilePath, UriKind.Absolute);
            bmp.EndInit();
            PaperDollImage.Source = bmp;
        }
        catch { PaperDollImage.Source = null; }
    }

    private void PaperDollNameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Field_Changed_Combo(sender, e);
        UpdatePaperDollImage();
    }

    /// <summary>
    /// Shared "Filter By Group" handling for all four sprite fields: opens
    /// the group-picker popup, and if a group is chosen, filters that
    /// field's Sprite ComboBox down to just that group. Cancelling (or
    /// picking nothing) reverts the checkbox to unchecked, since there's no
    /// filter to apply.
    /// </summary>
    private void HandleFilterChecked(CheckBox checkBox, ComboBox combo, ref string? filterField, string currentGroup, string currentName)
    {
        if (_suppress || _current is null) return;
        var groupNames = _sprites.Groups.Select(g => g.Name).ToList();
        string? chosen = SelectGroupWindow.Show(this, groupNames, filterField);
        if (chosen is null)
        {
            _suppress = true;
            checkBox.IsChecked = false;
            _suppress = false;
            return;
        }
        filterField = chosen;
        PopulateSpriteChoices(combo, chosen, currentGroup, currentName);
        Field_Changed_Combo(checkBox, null!);
    }

    private void HandleFilterUnchecked(ComboBox combo, ref string? filterField, string currentGroup, string currentName)
    {
        if (_suppress || _current is null) return;
        filterField = null;
        PopulateSpriteChoices(combo, null, currentGroup, currentName);
    }

    private void PaperDollFilterCheck_Checked(object sender, RoutedEventArgs e) =>
        HandleFilterChecked(PaperDollFilterCheck, PaperDollNameCombo, ref _paperDollFilter, _current?.PaperDollSpriteGroup ?? "", _current?.PaperDollSpriteName ?? "");
    private void PaperDollFilterCheck_Unchecked(object sender, RoutedEventArgs e) =>
        HandleFilterUnchecked(PaperDollNameCombo, ref _paperDollFilter, _current?.PaperDollSpriteGroup ?? "", _current?.PaperDollSpriteName ?? "");

    private void SpriteFilterCheck_Checked(object sender, RoutedEventArgs e) =>
        HandleFilterChecked(SpriteFilterCheck, SpriteNameCombo, ref _spriteFilter, _current?.SpriteGroup ?? "", _current?.SpriteName ?? "");
    private void SpriteFilterCheck_Unchecked(object sender, RoutedEventArgs e) =>
        HandleFilterUnchecked(SpriteNameCombo, ref _spriteFilter, _current?.SpriteGroup ?? "", _current?.SpriteName ?? "");

    private void ChatteredFilterCheck_Checked(object sender, RoutedEventArgs e) =>
        HandleFilterChecked(ChatteredFilterCheck, ChatteredSpriteNameCombo, ref _chatteredFilter, _current?.ChatteredSpriteGroup ?? "", _current?.ChatteredSpriteName ?? "");
    private void ChatteredFilterCheck_Unchecked(object sender, RoutedEventArgs e) =>
        HandleFilterUnchecked(ChatteredSpriteNameCombo, ref _chatteredFilter, _current?.ChatteredSpriteGroup ?? "", _current?.ChatteredSpriteName ?? "");

    private void DestroyedFilterCheck_Checked(object sender, RoutedEventArgs e) =>
        HandleFilterChecked(DestroyedFilterCheck, DestroyedSpriteNameCombo, ref _destroyedFilter, _current?.DestroyedSpriteGroup ?? "", _current?.DestroyedSpriteName ?? "");
    private void DestroyedFilterCheck_Unchecked(object sender, RoutedEventArgs e) =>
        HandleFilterUnchecked(DestroyedSpriteNameCombo, ref _destroyedFilter, _current?.DestroyedSpriteGroup ?? "", _current?.DestroyedSpriteName ?? "");

    /// <summary>Restricts a TextBox to decimal numbers only (digits + at most one '.') - used for Weight/Volume.</summary>
    private void DecimalOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        string proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !(proposed.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(proposed, @"^\d*\.?\d*$"));
    }

    /// <summary>Restricts a TextBox to whole numbers only (digits) - used for Price.</summary>
    private void IntegerOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        string proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !(proposed.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(proposed, @"^\d*$"));
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || _current is null) return;
        _current.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _current.Name : NameBox.Text;
        float.TryParse(WeightBox.Text, out float w); _current.Weight = w;
        float.TryParse(VolumeBox.Text, out float v); _current.Volume = v;
        int.TryParse(PriceBox.Text, out int p); _current.Price = p;
        _current.Destructible = DestructibleCheck.IsChecked == true;
        _current.CanBeSold = CanBeSoldCheck.IsChecked == true;
        _current.CanBeTraded = CanBeTradedCheck.IsChecked == true;
        _current.CanBeDropped = CanBeDroppedCheck.IsChecked == true;
        _current.IsStackable = IsStackableCheck.IsChecked == true;
        _current.AllowElevation = AllowElevationCheck.IsChecked == true;
        _current.Occludes = OccludesCheck.IsChecked == true;
        _current.IsSolid = IsSolidCheck.IsChecked == true;
        _current.IsTwoHanded = IsTwoHandedCheck.IsChecked == true;
        SolidSize.SetSolid(_current.IsSolid);
        // Just refresh the list's displayed text (in case Name changed) -
        // NOT ItemsSource/SelectedItem, which was the actual cause of the
        // "locks every keystroke" bug: reassigning ItemsSource briefly
        // clears the selection, which disables the whole editor panel, then
        // immediately re-selecting re-enables it - a visible flicker/lock on
        // every single character typed.
        BookListBox.Items.Refresh();
    }

    private const string DefaultTextFont = "(Default - system font)";

    private void Field_Changed_Combo(object sender, SelectionChangedEventArgs? e)
    {
        if (_suppress || _current is null) return;
        if (PaperDollNameCombo.SelectedItem is SpriteChoice pd) { _current.PaperDollSpriteGroup = pd.Group; _current.PaperDollSpriteName = pd.Name; }
        if (SpriteNameCombo.SelectedItem is SpriteChoice sp) { _current.SpriteGroup = sp.Group; _current.SpriteName = sp.Name; SolidSize.SetSprite(SolidSizeEditor.LoadSpriteFrame(_sprites, sp.Group, sp.Name)); }
        if (ChatteredSpriteNameCombo.SelectedItem is SpriteChoice ch) { _current.ChatteredSpriteGroup = ch.Group; _current.ChatteredSpriteName = ch.Name; }
        if (DestroyedSpriteNameCombo.SelectedItem is SpriteChoice de) { _current.DestroyedSpriteGroup = de.Group; _current.DestroyedSpriteName = de.Name; }
        _current.FactionId = (FactionCombo.SelectedItem as Faction)?.Id ?? -1;
        string chosenFont = TextFontCombo.SelectedItem is string font && font != DefaultTextFont ? font : string.Empty;
        if (chosenFont != _current.TextFontSpriteName)
        {
            _current.TextFontSpriteName = chosenFont;
            RebuildFrameLayout(); // a different font takes different room: re-flow the pages and redraw them
        }
        _current.OpenSoundResourceId = (OpenSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
        _current.CloseSoundResourceId = (CloseSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
        _current.PageFlipSoundResourceId = (FlipSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
        _current.DestructibleSoundResourceId = (DestructibleSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
        _current.BelongToId = -1; // no NPC Editor yet
    }

    // ---------- Tools ----------

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        // Switching tools while a shape is half-drawn discards it - warn first if there's real progress.
        if (_drawingPoints.Count > 0 && !ConfirmDiscardInProgressShape()) return;

        SelectToolButton.IsChecked = sender == SelectToolButton;
        RectangleToolButton.IsChecked = sender == RectangleToolButton;
        EllipseToolButton.IsChecked = sender == EllipseToolButton;
        LineToolButton.IsChecked = sender == LineToolButton;
        FreehandToolButton.IsChecked = sender == FreehandToolButton;

        _currentTool = sender switch
        {
            _ when sender == RectangleToolButton => Tool.Rectangle,
            _ when sender == EllipseToolButton => Tool.Ellipse,
            _ when sender == LineToolButton => Tool.Polygon,
            _ when sender == FreehandToolButton => Tool.Freehand,
            _ => Tool.Select
        };

        CancelInProgressDrawing();
        DeselectFrame();
        FinishShapeButton.IsEnabled = _currentTool == Tool.Polygon;

        // Frames only intercept clicks in Select mode, so drawing tools can click "through" them onto the canvas.
        foreach (var (outline, _) in _frameVisuals.Values)
            outline.IsHitTestVisible = _currentTool == Tool.Select;

        PreviewCanvas.Focus();
    }

    /// <summary>Returns true if it's OK to proceed (no in-progress shape, or user confirmed discarding it).</summary>
    private bool ConfirmDiscardInProgressShape()
    {
        if (_drawingPoints.Count == 0) return true;

        var result = MessageBox.Show(
            "You have an unfinished line/shape in progress. Discard it and continue?",
            "Unfinished shape", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void CancelInProgressDrawing()
    {
        _isDrawingShape = false;
        _dragStartPoint = null;
        _drawingPoints.Clear();
        if (_tempShape is not null) { PreviewCanvas.Children.Remove(_tempShape); _tempShape = null; }
        if (Mouse.Captured == PreviewCanvas) Mouse.Capture(null);
    }

    private void PreviewCanvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drawingPoints.Count > 0)
        {
            CancelInProgressDrawing();
            e.Handled = true;
        }
    }

    // ---------- Pages ----------
    // Frames (shape/position) are shared across every page and drawn once.
    // Pages themselves are now fully COMPUTED: BookData.FullContentXaml is the
    // one continuous text stream, and Repaginate() below distributes it across
    // frames/pages automatically - there is no manual "add/remove page" anymore.

    private void Repaginate()
    {
        if (_current is null) { _sliceMap.Clear(); return; }
        _sliceMap = BookPaginationEngine.Repaginate(_current);
        if (_currentPageIndex >= _current.Pages.Count) _currentPageIndex = _current.Pages.Count - 1;
        if (_currentPageIndex < 0) _currentPageIndex = 0;
    }

    private void RebuildFrameLayout()
    {
        Repaginate();
        ClearAllFrameVisuals();
        if (_current is null) return;
        foreach (var frame in _current.Frames)
            RenderFrame(frame);
        RefreshFrameTextsForCurrentPage();
    }

    private void RefreshFrameTextsForCurrentPage()
    {
        if (_current is null || _current.Pages.Count == 0) return;
        foreach (var animator in _overlaySpriteAnimators) animator.Stop();
        _overlaySpriteAnimators.Clear();

        var page = _current.Pages[_currentPageIndex];
        foreach (var (frame, visuals) in _frameVisuals)
        {
            visuals.Overlay.Children.Clear();
            string xaml = page.FrameTexts.TryGetValue(frame.Id, out var t) ? t : string.Empty;
            var result = FormattedContentRenderer.Render(xaml, visuals.Overlay.Width, visuals.Overlay.Height, _sprites, _current.TextFontSpriteName);

            if (result.TextBitmap is not null)
            {
                var textImage = new Image { Source = result.TextBitmap, Width = visuals.Overlay.Width, Height = visuals.Overlay.Height, Stretch = Stretch.None };
                visuals.Overlay.Children.Add(textImage);
            }

            foreach (var spriteBox in result.Sprites)
            {
                var spriteImage = new Image
                {
                    Width = spriteBox.Bounds.Width,
                    Height = spriteBox.Bounds.Height,
                    Stretch = Stretch.Uniform
                };
                Canvas.SetLeft(spriteImage, spriteBox.Bounds.X);
                Canvas.SetTop(spriteImage, spriteBox.Bounds.Y);
                visuals.Overlay.Children.Add(spriteImage);

                var animator = new SpriteAnimator(spriteBox.Sprite, spriteImage, _resources);
                animator.Ticked += OnAnyOverlaySpriteTicked;
                _overlaySpriteAnimators.Add(animator);
            }
        }
        RefreshPageIndicator();
    }

    /// <summary>
    /// Temporary diagnostic (DEBUG-BUILD-46): the main canvas overlay also
    /// has IsHitTestVisible=false (so clicking through it still reaches
    /// frame selection/resizing underneath), which blocks tooltips here
    /// exactly like the Preview window's reading view - so the same
    /// non-hover diagnostic surface is needed. Piggybacks on the always-
    /// visible page indicator rather than adding yet another label.
    /// </summary>
    private int _diagnosticTickTotal;

    private void OnAnyOverlaySpriteTicked(int _)
    {
        _diagnosticTickTotal++;
        PageIndicatorText.Text = $"Page {_currentPageIndex + 1} of {_current?.Pages.Count ?? 1} - sprite ticks: {_diagnosticTickTotal}";
    }

    private void RefreshPageIndicator()
    {
        if (_current is null || _current.Pages.Count == 0) return;
        PageIndicatorText.Text = $"Page {_currentPageIndex + 1} of {_current.Pages.Count}";
    }

    private void ClearAllFrameVisuals()
    {
        ClosePopup();
        DeselectFrame();
        CancelInProgressDrawing();
        foreach (var (outline, overlay) in _frameVisuals.Values)
        {
            PreviewCanvas.Children.Remove(outline);
            PreviewCanvas.Children.Remove(overlay);
        }
        _frameVisuals.Clear();
    }

    private bool GuardPageNavigation()
    {
        if (_current is null) return false;
        if (!ConfirmDiscardInProgressShape()) return false;
        CancelInProgressDrawing();
        ClosePopup();
        return true;
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _currentPageIndex <= 0) return;
        if (!GuardPageNavigation()) return;
        _currentPageIndex--;
        RefreshFrameTextsForCurrentPage();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _currentPageIndex >= _current.Pages.Count - 1) return;
        if (!GuardPageNavigation()) return;
        _currentPageIndex++;
        RefreshFrameTextsForCurrentPage();
    }

    // ---------- Drawing ----------

    private void PreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_current is null) return;
        PreviewCanvas.Focus();
        var pos = e.GetPosition(PreviewCanvas);

        // Defensive: if a previous drag ended outside the canvas (mouse released
        // off-canvas, so MouseUp never fired here) an orphaned dashed preview
        // shape could still be sitting on the canvas. Clear it before starting anew.
        if (_tempShape is not null && _currentTool != Tool.Polygon)
            CancelInProgressDrawing();

        switch (_currentTool)
        {
            case Tool.Rectangle:
            case Tool.Ellipse:
                _dragStartPoint = pos;
                _isDrawingShape = true;
                _tempShape = _currentTool == Tool.Rectangle
                    ? new Rectangle { Stroke = CurrentFrameBrush(), StrokeThickness = 2, Fill = Brushes.Transparent, StrokeDashArray = { 4, 2 } }
                    : new Ellipse { Stroke = CurrentFrameBrush(), StrokeThickness = 2, Fill = Brushes.Transparent, StrokeDashArray = { 4, 2 } };
                Canvas.SetLeft(_tempShape, pos.X);
                Canvas.SetTop(_tempShape, pos.Y);
                PreviewCanvas.Children.Add(_tempShape);
                Mouse.Capture(PreviewCanvas); // keep tracking the drag even if the mouse leaves the canvas
                break;

            case Tool.Freehand:
                _isDrawingShape = true;
                _drawingPoints.Clear();
                _drawingPoints.Add(pos);
                _tempShape = new Polyline { Stroke = CurrentFrameBrush(), StrokeThickness = 2 };
                PreviewCanvas.Children.Add(_tempShape);
                Mouse.Capture(PreviewCanvas);
                break;

            case Tool.Polygon:
                // Double-click finishes the shape immediately, same as clicking "Finish Shape".
                if (e.ClickCount == 2 && _drawingPoints.Count >= 2)
                {
                    FinishPolygon();
                    return;
                }
                _drawingPoints.Add(pos);
                if (_tempShape is null)
                {
                    _tempShape = new Polyline { Stroke = CurrentFrameBrush(), StrokeThickness = 2, StrokeDashArray = { 4, 2 } };
                    PreviewCanvas.Children.Add(_tempShape);
                }
                ((Polyline)_tempShape).Points = new PointCollection(_drawingPoints);
                break;

            case Tool.Select:
                DeselectFrame();
                break;
        }
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(PreviewCanvas);

        // Resizing via a corner handle takes priority.
        if (_resizingCorner is not null && _selectedFrame is not null && _resizeStartMouse is { } rStart
            && _resizeOriginalPoints is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            ApplyResize(_selectedFrame, pos);
            return;
        }

        if (_currentTool == Tool.Select && _selectedFrame is not null && _dragStartMouse is { } start
            && _dragOriginalPoints is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            // Always compute from the original points + total delta since drag
            // started - never from the last frame position - so clamping can
            // never desync the mouse from the frame (the earlier bug: once
            // clamped, the frame would stop responding to further movement).
            double dx = pos.X - start.X, dy = pos.Y - start.Y;
            var frame = _selectedFrame;

            var moved = _dragOriginalPoints.Select(p => new BookPoint(p.X + dx, p.Y + dy)).ToList();
            var bbox = BoundingBoxOf(moved);

            double clampDx = 0, clampDy = 0;
            if (bbox.X < 0) clampDx = -bbox.X;
            if (bbox.Y < 0) clampDy = -bbox.Y;
            if (bbox.X + bbox.Width > CanvasWidth) clampDx = CanvasWidth - (bbox.X + bbox.Width);
            if (bbox.Y + bbox.Height > CanvasHeight) clampDy = CanvasHeight - (bbox.Y + bbox.Height);

            frame.Points = moved.Select(p => new BookPoint(p.X + clampDx, p.Y + clampDy)).ToList();
            UpdateFrameVisual(frame, selected: true);
            return;
        }

        if (_isDrawingShape && _dragStartPoint is { } shapeStart && _tempShape is not null &&
            (_currentTool == Tool.Rectangle || _currentTool == Tool.Ellipse))
        {
            var clamped = ClampToCanvas(pos);
            double x = Math.Min(shapeStart.X, clamped.X), y = Math.Min(shapeStart.Y, clamped.Y);
            double w = Math.Abs(clamped.X - shapeStart.X), h = Math.Abs(clamped.Y - shapeStart.Y);
            Canvas.SetLeft(_tempShape, x);
            Canvas.SetTop(_tempShape, y);
            _tempShape.Width = w;
            _tempShape.Height = h;
        }
        else if (_isDrawingShape && _currentTool == Tool.Freehand && _tempShape is Polyline pl)
        {
            _drawingPoints.Add(ClampToCanvas(pos));
            pl.Points = new PointCollection(_drawingPoints);
        }
        else if (_currentTool == Tool.Polygon && _tempShape is Polyline poly && _drawingPoints.Count > 0)
        {
            var preview = new List<Point>(_drawingPoints) { ClampToCanvas(pos) };
            poly.Points = new PointCollection(preview);
        }
    }

    private static Point ClampToCanvas(Point p) =>
        new(Math.Max(0, Math.Min(CanvasWidth, p.X)), Math.Max(0, Math.Min(CanvasHeight, p.Y)));

    private static Rect BoundingBoxOf(List<BookPoint> points)
    {
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private void PreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_current is null) return;

        if (_currentTool == Tool.Select)
        {
            bool wasResizing = _resizingCorner is not null;

            _dragStartMouse = null;
            _dragOriginalPoints = null;
            _resizingCorner = null;
            _resizeStartMouse = null;
            _resizeOriginalPoints = null;
            if (Mouse.Captured == PreviewCanvas) Mouse.Capture(null);

            // Resizing changes a frame's capacity - reflow the whole book once,
            // now that the drag is finished (never mid-drag, for performance).
            if (wasResizing) Repaginate();
            RefreshFrameTextsForCurrentPage();
            return;
        }

        if (!_isDrawingShape) return;

        if ((_currentTool == Tool.Rectangle || _currentTool == Tool.Ellipse) && _dragStartPoint is { } start)
        {
            var end = ClampToCanvas(e.GetPosition(PreviewCanvas));
            if (Math.Abs(end.X - start.X) < 4 || Math.Abs(end.Y - start.Y) < 4)
            {
                CancelInProgressDrawing();
                return;
            }

            if (!CheckColorAvailable()) { CancelInProgressDrawing(); return; }

            var frame = new BookTextFrame
            {
                Id = _current.GetNextFrameId(),
                ShapeType = _currentTool == Tool.Rectangle ? FrameShapeType.Rectangle : FrameShapeType.Ellipse,
                Points = new List<BookPoint> { new(start.X, start.Y), new(end.X, end.Y) },
                OutlineColorHex = CurrentFrameColorHex()
            };
            _current.Frames.Add(frame);
            CancelInProgressDrawing();
            Repaginate();
            RenderFrame(frame);
            RefreshFrameTextsForCurrentPage();
            SetTool(SelectToolButton);
        }
        else if (_currentTool == Tool.Freehand)
        {
            if (_drawingPoints.Count >= 3)
            {
                if (!CheckColorAvailable()) { CancelInProgressDrawing(); return; }

                var frame = new BookTextFrame
                {
                    Id = _current.GetNextFrameId(),
                    ShapeType = FrameShapeType.Freehand,
                    Points = _drawingPoints.Select(p => new BookPoint(p.X, p.Y)).ToList(),
                    OutlineColorHex = CurrentFrameColorHex()
                };
                _current.Frames.Add(frame);
                CancelInProgressDrawing();
                Repaginate();
                RenderFrame(frame);
                RefreshFrameTextsForCurrentPage();
                SetTool(SelectToolButton);
            }
            else
            {
                CancelInProgressDrawing();
            }
        }
        // Polygon finalizes via the "Finish Shape" button or a double-click, not mouse-up.
    }

    private void FinishShape_Click(object sender, RoutedEventArgs e) => FinishPolygon();

    /// <summary>Color is purely decorative but must still be unique per frame - checks and shows an error if the current color is already used.</summary>
    private bool CheckColorAvailable()
    {
        if (_current is null) return false;
        bool alreadyUsed = _current.Frames.Any(f =>
            string.Equals(f.OutlineColorHex, CurrentFrameColorHex(), StringComparison.OrdinalIgnoreCase));

        if (alreadyUsed)
        {
            MessageBox.Show("You cannot add another frame with the same color.",
                "Duplicate frame color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private void FinishPolygon()
    {
        if (_current is null || _currentTool != Tool.Polygon) return;

        if (_drawingPoints.Count < 3)
        {
            MessageBox.Show("A line/polygon frame needs at least 3 points before it can be finished.",
                "Not enough points", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!CheckColorAvailable()) return;

        var frame = new BookTextFrame
        {
            Id = _current.GetNextFrameId(),
            ShapeType = FrameShapeType.Polygon,
            Points = _drawingPoints.Select(p => new BookPoint(p.X, p.Y)).ToList(),
            OutlineColorHex = CurrentFrameColorHex()
        };
        _current.Frames.Add(frame);
        CancelInProgressDrawing();
        Repaginate();
        RenderFrame(frame);
        RefreshFrameTextsForCurrentPage();
        SetTool(SelectToolButton);
    }

    private void SetTool(ToggleButton button) => Tool_Click(button, new RoutedEventArgs());

    private Brush CurrentFrameBrush() => new SolidColorBrush(_currentFrameColor);

    private string CurrentFrameColorHex() => _currentFrameColor.ToString();

    // ---------- Frame rendering ----------

    private static Rect BoundingBox(BookTextFrame frame) => BoundingBoxOf(frame.Points);

    private void RenderFrame(BookTextFrame frame)
    {
        var bbox = BoundingBox(frame);
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(frame.OutlineColorHex));

        Shape outline = frame.ShapeType switch
        {
            FrameShapeType.Rectangle => new Rectangle(),
            FrameShapeType.Ellipse => new Ellipse(),
            _ => new Polygon { Points = new PointCollection(frame.Points.Select(p => new Point(p.X, p.Y))) }
        };
        outline.Stroke = brush;
        outline.StrokeThickness = 2;
        outline.Fill = Brushes.Transparent;
        outline.Cursor = Cursors.Hand;
        outline.IsHitTestVisible = _currentTool == Tool.Select;

        if (outline is Rectangle or Ellipse)
        {
            Canvas.SetLeft(outline, bbox.X);
            Canvas.SetTop(outline, bbox.Y);
            outline.Width = bbox.Width;
            outline.Height = bbox.Height;
        }

        outline.MouseLeftButtonDown += (s, e) => OutlineFrame_MouseLeftButtonDown(frame, e);

        var overlay = new Canvas
        {
            IsHitTestVisible = false
        };
        Canvas.SetLeft(overlay, bbox.X);
        Canvas.SetTop(overlay, bbox.Y);
        overlay.Width = bbox.Width;
        overlay.Height = bbox.Height;
        overlay.Clip = BuildRelativeClipGeometry(frame, bbox);

        // No initial content is loaded here - every call site that creates
        // a frame immediately calls RefreshFrameTextsForCurrentPage right
        // after, which populates this overlay for real.
        PreviewCanvas.Children.Add(overlay);
        PreviewCanvas.Children.Add(outline);
        _frameVisuals[frame] = (outline, overlay);
    }

    private static Geometry BuildRelativeClipGeometry(BookTextFrame frame, Rect bbox)
    {
        return frame.ShapeType switch
        {
            FrameShapeType.Rectangle => new RectangleGeometry(new Rect(0, 0, bbox.Width, bbox.Height)),
            FrameShapeType.Ellipse => new EllipseGeometry(new Point(bbox.Width / 2, bbox.Height / 2), bbox.Width / 2, bbox.Height / 2),
            _ => new PathGeometry(new[]
            {
                new PathFigure(
                    new Point(frame.Points[0].X - bbox.X, frame.Points[0].Y - bbox.Y),
                    frame.Points.Skip(1).Select(p => new LineSegment(new Point(p.X - bbox.X, p.Y - bbox.Y), true)),
                    true)
            })
        };
    }

    private void UpdateFrameVisual(BookTextFrame frame, bool selected)
    {
        if (!_frameVisuals.TryGetValue(frame, out var visuals)) return;
        var bbox = BoundingBox(frame);

        visuals.Outline.StrokeThickness = selected ? 3 : 2;

        if (visuals.Outline is Rectangle or Ellipse)
        {
            Canvas.SetLeft(visuals.Outline, bbox.X);
            Canvas.SetTop(visuals.Outline, bbox.Y);
            visuals.Outline.Width = bbox.Width;
            visuals.Outline.Height = bbox.Height;
        }
        else if (visuals.Outline is Polygon polygon)
        {
            polygon.Points = new PointCollection(frame.Points.Select(p => new Point(p.X, p.Y)));
        }

        Canvas.SetLeft(visuals.Overlay, bbox.X);
        Canvas.SetTop(visuals.Overlay, bbox.Y);
        visuals.Overlay.Width = bbox.Width;
        visuals.Overlay.Height = bbox.Height;
        visuals.Overlay.Clip = BuildRelativeClipGeometry(frame, bbox);

        if (selected) PositionResizeHandles(bbox);
    }

    private void OutlineFrame_MouseLeftButtonDown(BookTextFrame frame, MouseButtonEventArgs e)
    {
        if (_currentTool != Tool.Select) return;
        e.Handled = true;
        PreviewCanvas.Focus();

        if (e.ClickCount == 2)
        {
            OpenFramePopup(frame);
            return;
        }

        DeselectFrame();
        _selectedFrame = frame;
        UpdateFrameVisual(frame, selected: true);
        ShowResizeHandles(frame);
        _dragStartMouse = e.GetPosition(PreviewCanvas);
        _dragOriginalPoints = new List<BookPoint>(frame.Points);
        Mouse.Capture(PreviewCanvas);
    }

    private void DeselectFrame()
    {
        HideResizeHandles();
        if (_selectedFrame is null) return;
        var frame = _selectedFrame;
        _selectedFrame = null;
        _dragStartMouse = null;
        _dragOriginalPoints = null;
        UpdateFrameVisual(frame, selected: false);
    }

    // ---------- Resize handles (corner-drag, works for any shape by proportional scaling) ----------

    private void ShowResizeHandles(BookTextFrame frame)
    {
        HideResizeHandles();
        var bbox = BoundingBox(frame);

        foreach (var corner in new[] { "TL", "TR", "BL", "BR" })
        {
            var handle = new Rectangle
            {
                Width = 9,
                Height = 9,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Cursor = corner is "TL" or "BR" ? Cursors.SizeNWSE : Cursors.SizeNESW
            };
            handle.MouseLeftButtonDown += (s, e) => ResizeHandle_MouseLeftButtonDown(corner, e);
            _resizeHandles[corner] = handle;
            PreviewCanvas.Children.Add(handle);
        }
        PositionResizeHandles(bbox);
    }

    private void HideResizeHandles()
    {
        foreach (var handle in _resizeHandles.Values)
            PreviewCanvas.Children.Remove(handle);
        _resizeHandles.Clear();
    }

    private void PositionResizeHandles(Rect bbox)
    {
        if (_resizeHandles.Count == 0) return;
        const double half = 4.5;
        SetHandlePos("TL", bbox.Left - half, bbox.Top - half);
        SetHandlePos("TR", bbox.Right - half, bbox.Top - half);
        SetHandlePos("BL", bbox.Left - half, bbox.Bottom - half);
        SetHandlePos("BR", bbox.Right - half, bbox.Bottom - half);

        void SetHandlePos(string corner, double x, double y)
        {
            if (!_resizeHandles.TryGetValue(corner, out var h)) return;
            Canvas.SetLeft(h, x);
            Canvas.SetTop(h, y);
        }
    }

    private void ResizeHandle_MouseLeftButtonDown(string corner, MouseButtonEventArgs e)
    {
        if (_selectedFrame is null) return;
        e.Handled = true;
        _resizingCorner = corner;
        _resizeStartMouse = e.GetPosition(PreviewCanvas);
        _resizeOriginalPoints = new List<BookPoint>(_selectedFrame.Points);
        _resizeOriginalBbox = BoundingBox(_selectedFrame);
        Mouse.Capture(PreviewCanvas);
    }

    private void ApplyResize(BookTextFrame frame, Point currentMouse)
    {
        if (_resizingCorner is null || _resizeOriginalPoints is null) return;
        var orig = _resizeOriginalBbox;

        // The corner opposite the one being dragged stays fixed (the anchor);
        // the dragged corner follows the mouse, clamped to the canvas.
        var mouse = ClampToCanvas(currentMouse);

        double left = orig.Left, top = orig.Top, right = orig.Right, bottom = orig.Bottom;
        switch (_resizingCorner)
        {
            case "TL": left = mouse.X; top = mouse.Y; break;
            case "TR": right = mouse.X; top = mouse.Y; break;
            case "BL": left = mouse.X; bottom = mouse.Y; break;
            case "BR": right = mouse.X; bottom = mouse.Y; break;
        }

        double newWidth = right - left, newHeight = bottom - top;
        if (Math.Abs(newWidth) < 10 || Math.Abs(newHeight) < 10) return; // avoid collapsing to nothing

        double scaleX = newWidth / orig.Width;
        double scaleY = newHeight / orig.Height;

        frame.Points = _resizeOriginalPoints
            .Select(p => new BookPoint(left + (p.X - orig.X) * scaleX, top + (p.Y - orig.Y) * scaleY))
            .ToList();

        UpdateFrameVisual(frame, selected: true);
    }

    private void DeleteFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _selectedFrame is null) return;
        var frame = _selectedFrame;

        _current.Frames.Remove(frame);
        // Note: FullContentXaml is untouched - nothing is lost. Repaginate()
        // below redistributes all the book's text across whatever frames
        // remain, exactly like the spec: deleting a frame reflows everything.

        _selectedFrame = null;
        HideResizeHandles();
        ClosePopup();
        RebuildFrameLayout();
    }

    private void CopyFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrame is null)
        {
            MessageBox.Show("Select a frame first.", "Copy Frame", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _frameClipboard = (_selectedFrame.ShapeType, new List<BookPoint>(_selectedFrame.Points), _selectedFrame.EditorBackgroundColorHex);
    }

    private void PasteFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_frameClipboard is null)
        {
            MessageBox.Show("Copy a frame first.", "Paste Frame", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (shapeType, points, backgroundHex) = _frameClipboard.Value;

        // Offset slightly so the pasted frame doesn't sit exactly on top of
        // the original - clamp so it never lands outside the canvas.
        var bbox = BoundingBoxOf(points);
        double offsetX = 20, offsetY = 20;
        if (bbox.X + bbox.Width + offsetX > CanvasWidth) offsetX = Math.Max(0, CanvasWidth - (bbox.X + bbox.Width));
        if (bbox.Y + bbox.Height + offsetY > CanvasHeight) offsetY = Math.Max(0, CanvasHeight - (bbox.Y + bbox.Height));
        var newPoints = points.Select(p => new BookPoint(p.X + offsetX, p.Y + offsetY)).ToList();

        // Outline color must stay unique per frame - auto-pick the first
        // unused standard color rather than copying the source frame's color
        // and immediately conflicting with it.
        string newColorHex = StandardFrameSwatches
            .Select(c => c.ToString())
            .FirstOrDefault(hex => !_current.Frames.Any(f => string.Equals(f.OutlineColorHex, hex, StringComparison.OrdinalIgnoreCase)))
            ?? CurrentFrameColorHex();

        var newFrame = new BookTextFrame
        {
            Id = _current.GetNextFrameId(),
            ShapeType = shapeType,
            Points = newPoints,
            OutlineColorHex = newColorHex,
            EditorBackgroundColorHex = backgroundHex
        };
        _current.Frames.Add(newFrame);

        RebuildFrameLayout();

        _selectedFrame = newFrame;
        UpdateFrameVisual(newFrame, selected: true);
        ShowResizeHandles(newFrame);
    }

    // ---------- Event tab: lazy-attach the shared engine, sync with book-specific conditions ----------

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_current is null) return;
        if (MainTabControl.SelectedItem is not TabItem item || item.Header as string != "Event") return;
        if (_eventEditorControl is not null) return; // already attached for this book

        // Never headless - an event with zero pages/conditions is
        // impossible by design, so guarantee at least one page exists.
        if (_current.EventPages.Count == 0)
            _current.EventPages.Add(new BookEventPage());

        var enginePages = new System.Collections.ObjectModel.ObservableCollection<EventCommandPage>(
            _current.EventPages.Select((p, i) => new EventCommandPage { Label = (i + 1).ToString(), Commands = p.Commands }));

        _eventEditorControl = new EventEditorControl();
        _eventEditorControl.ResourceDatabase = _resources;
        _eventEditorControl.Variables = _variables;
        _eventEditorControl.CurrentBookLocalVariables = _current.LocalVariables;
        _eventEditorControl.Books = Database;
        _eventEditorControl.Objects = _objects;
        _eventEditorControl.Sprites = _sprites;
        _eventEditorControl.CurrentBook = _current;
        _eventEditorControl.PageSelected += OnEventPageSelected;
        _eventEditorControl.PageInserted += OnEventPageInserted;
        _eventEditorControl.PageRemoved += OnEventPageRemoved;
        _eventEditorControl.ContentChanged += OnEventContentChanged;
        _eventEditorControl.Pages = enginePages;
        EventEnginePlaceholder.Content = _eventEditorControl;

        BookConditionsControl.Conditions = _current.EventPages[0].Conditions;
    }

    private void OnEventPageSelected(int index)
    {
        if (_current is null) return;
        BookConditionsControl.Conditions = index >= 0 && index < _current.EventPages.Count
            ? _current.EventPages[index].Conditions
            : null;
    }

    private void OnEventPageInserted(int index)
    {
        if (_current is null) return;
        if (index >= 0 && index <= _current.EventPages.Count)
            _current.EventPages.Insert(index, new BookEventPage());
    }

    private void OnEventPageRemoved(int index)
    {
        if (_current is null) return;
        if (index >= 0 && index < _current.EventPages.Count)
            _current.EventPages.RemoveAt(index);
    }

    private void EventDebugButton_Click(object sender, RoutedEventArgs e)
    {
        if (BookConditionsControl.Conditions is not { } conditions)
        {
            MessageBox.Show("Select an event page first.", "Debug Event", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var commands = _eventEditorControl?.CurrentEditingPage?.Commands ?? new List<EventCommand>();

        var dialog = new BookEventDebugWindow(conditions, commands, _resources, _stats, _current.Id) { Owner = this };
        dialog.Show(); // non-modal - keep editing conditions/commands, tweak, and debug again without closing it each time
    }

    private void OnEventContentChanged()
    {
        // Some engine operations (Compile, Paste Event Page) replace a
        // page's Commands list wholesale rather than mutating it in place,
        // which would otherwise silently break the shared reference back to
        // the book's own data. Re-sync every page's Commands after any
        // change instead of trying to keep every mutation path perfectly
        // reference-aligned - simpler and can't miss a case.
        if (_current is null || _eventEditorControl is null) return;
        var enginePages = _eventEditorControl.Pages;
        for (int i = 0; i < enginePages.Count && i < _current.EventPages.Count; i++)
            _current.EventPages[i].Commands = enginePages[i].Commands;
    }

    // ---------- Preview: simulate reading the book like real Ultima 7 ----------

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        if (string.IsNullOrEmpty(_current.PaperDollSpriteGroup) || string.IsNullOrEmpty(_current.PaperDollSpriteName))
        {
            MessageBox.Show("Paper doll is required.", "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(_current.SpriteGroup) || string.IsNullOrEmpty(_current.SpriteName))
        {
            MessageBox.Show("Sprite is required.", "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var group = _sprites.Groups.FirstOrDefault(g => g.Name == _current.SpriteGroup);
        var spriteEntry = group?.Sprites.FirstOrDefault(s => s.Name == _current.SpriteName);
        if (spriteEntry is null || !File.Exists(spriteEntry.FilePath))
        {
            MessageBox.Show("Sprite is required.", "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var preview = new BookPreviewWindow(_current, spriteEntry, _resources, _sprites,
            (grpName, sprName) => _sprites.Groups.FirstOrDefault(g => g.Name == grpName)?.Sprites.FirstOrDefault(s => s.Name == sprName))
        { Owner = this };
        preview.ShowDialog();
    }

    /// <summary>
    /// Temporary diagnostic: reports whether an image is present at each
    /// stage of the pipeline (canonical source -> per-frame paginated slice
    /// -> what the on-screen overlay actually has loaded), so we can see
    /// exactly where it's being lost instead of guessing blind.
    /// </summary>
    /// <summary>
    /// Walks the LIVE overlay control's document tree to find the actual
    /// Image element inside a Floater and reports whether its bitmap Source
    /// is valid - structural presence (a Floater exists) is not the same as
    /// having a real, renderable picture inside it.
    /// </summary>
    /// <summary>Much simpler now that the overlay is a plain Canvas instead of a RichTextBox - no more Floater/BlockUIContainer/Grid nesting to walk (that whole structure only ever existed to make an image survive inside RichTextBox's document model, which no longer applies here at all).</summary>
    private static string DescribeOverlayImageStatus(Canvas overlay)
    {
        var spriteImages = overlay.Children.OfType<Image>().Skip(1).ToList(); // child 0 is always the text bitmap, if present
        if (spriteImages.Count == 0) return "No sprite Image found in this overlay's Canvas.";

        var lines = spriteImages.Select((img, i) => img.Source is BitmapSource bmp
            ? $"Sprite image [{i}]: Source VALID: {bmp.PixelWidth}x{bmp.PixelHeight}px. Visibility={img.Visibility}, Opacity={img.Opacity}, ActualWidth={img.ActualWidth:0.0}, ActualHeight={img.ActualHeight:0.0}"
            : $"Sprite image [{i}]: Source is {(img.Source is null ? "NULL" : img.Source.GetType().Name)} - not a usable bitmap!");
        return string.Join("\n", lines);
    }

    private void DiagnosticCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            MessageBox.Show($"[{DiagnosticBuildVersion}]\n\nNo book selected.", "Diagnostic", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool inFullContent = BookPaginationEngine.HasEmbeddedImage(_current.FullContentXaml);

        var page = _current.Pages.Count > _currentPageIndex ? _current.Pages[_currentPageIndex] : null;
        var frameResults = new List<string>();
        foreach (var frame in _current.Frames)
        {
            bool inSlice = page is not null && page.FrameTexts.TryGetValue(frame.Id, out var sliceXaml)
                && BookPaginationEngine.HasEmbeddedImage(sliceXaml);

            bool inOverlay = _frameVisuals.TryGetValue(frame, out var visuals) && visuals.Overlay.Children.Count > 1;

            frameResults.Add($"Frame {frame.Id}: paginated slice = {(inSlice ? "HAS image" : "no image")}, on-screen overlay = {(inOverlay ? "HAS image" : "no image")}");
        }

        string message = $"[{DiagnosticBuildVersion}]\n\n" +
            $"Canonical FullContentXaml: {(inFullContent ? "HAS image" : "NO image")}\n\n" + string.Join("\n", frameResults);

        foreach (var frame in _current.Frames)
        {
            if (_frameVisuals.TryGetValue(frame, out var visuals))
                message += $"\n\n--- Frame {frame.Id} overlay image inspection ---\n" + DescribeOverlayImageStatus(visuals.Overlay);
        }

        message += "\n\n--- Canonical content structure ---\n" + BookPaginationEngine.DescribeContent(_current.FullContentXaml);

        foreach (var frame in _current.Frames)
        {
            string sliceXaml = page is not null && page.FrameTexts.TryGetValue(frame.Id, out var s) ? s : string.Empty;
            message += $"\n\n--- Frame {frame.Id} paginated slice structure ---\n" + BookPaginationEngine.DescribeContent(sliceXaml);
        }

        foreach (var frame in _current.Frames)
        {
            string sliceXaml = page is not null && page.FrameTexts.TryGetValue(frame.Id, out var s2) ? s2 : string.Empty;
            message += $"\n\n--- Frame {frame.Id} sprite detection (what the new renderer actually sees) ---\n" + FormattedContentRenderer.DiagnoseSpriteDetection(sliceXaml, _sprites);
        }

        MessageBox.Show(message, "Image Pipeline Diagnostic", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DiagnosticPaginationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _selectedFrame is null)
        {
            MessageBox.Show("Select a frame first (click its border in the canvas), then try again.", "Pagination Diagnostic", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var page = _current.Pages.Count > _currentPageIndex ? _current.Pages[_currentPageIndex] : null;
        string xaml = page is not null && page.FrameTexts.TryGetValue(_selectedFrame.Id, out var sliceXaml) ? sliceXaml : string.Empty;
        var bbox = FrameGeometry.BoundingBox(_selectedFrame);
        double targetWidth = Math.Max(20, bbox.Width);
        double targetHeight = Math.Max(10, bbox.Height);

        string diag = BookPaginationEngine.DiagnosePagination(xaml, targetWidth, targetHeight);

        double realOverlayHeight = _frameVisuals.TryGetValue(_selectedFrame, out var visuals) ? visuals.Overlay.ActualHeight : -1;

        string message = $"[{DiagnosticBuildVersion}]\n\n" +
            $"Frame {_selectedFrame.Id}, bbox = {bbox.Width:0.#} x {bbox.Height:0.#} (target used for splitting: {targetWidth:0.#} x {targetHeight:0.#})\n\n" +
            diag +
            $"\nReal on-screen overlay ActualHeight for this frame: {realOverlayHeight:0.#}\n" +
            "(If the probe's running total stays under the target height while the real overlay's ActualHeight is clearly taller, the probe is under-measuring relative to how this content actually renders - that's the concrete number needed to fix this correctly instead of guessing.)";

        MessageBox.Show(message, "Pagination Diagnostic", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- Text popup ----------

    private void OpenFramePopup(BookTextFrame frame)
    {
        if (_current is null) return;
        ClosePopup();
        _activePopupFrame = frame;

        // Find the DISPLAY slice (word-boundary-limited) for this frame/page,
        // then snap it out to whole paragraph boundaries for EDITING - editing
        // must never touch a mid-paragraph fragment directly (see
        // SnapToWholeParagraphs for why: WPF can't preserve "this continues
        // the previous paragraph" across a save/reload, so splicing against a
        // word-split fragment was re-fragmenting the paragraph a little more
        // on every keystroke, which is what made typing look like each new
        // letter replaced the last one).
        var key = (_currentPageIndex, frame.Id);
        int rawStart, rawEnd;
        if (_sliceMap.TryGetValue(key, out var loc))
        {
            rawStart = loc.StartOffset;
            rawEnd = loc.EndOffset;
        }
        else
        {
            int end = BookPaginationEngine.GetTotalLength(_current.FullContentXaml);
            rawStart = end;
            rawEnd = end; // nothing here yet - appending at the end of the stream
        }

        var (editStart, editEnd) = BookPaginationEngine.SnapToWholeParagraphs(_current.FullContentXaml, rawStart, rawEnd);
        int editLength = editEnd - editStart;
        string xaml = BookPaginationEngine.ExtractRange(_current.FullContentXaml, editStart, editEnd);

        var frameBbox = BoundingBox(frame);
        _activePopup = new BookPageTextPopup(
            $"Frame Text (Page {_currentPageIndex + 1})",
            frame.EditorBackgroundColorHex,
            newHex => frame.EditorBackgroundColorHex = newHex,
            frameBbox.Width,
            frameBbox.Height,
            _sprites,
            _resources)
        { Owner = this };
        _activePopup.LoadXaml(xaml);

        // The popup itself now debounces the expensive Save()/serialize step
        // (fixed at the real root cause), so by the time XamlChanged actually
        // fires here it's already throttled - no need to debounce again on
        // this side, which would just add a second, redundant delay.
        void ApplyEdit(string newXaml)
        {
            if (_current is null) return;

            _current.FullContentXaml = BookPaginationEngine.Splice(_current.FullContentXaml, editStart, editStart + editLength, newXaml);
            editLength = BookPaginationEngine.GetTotalLength(newXaml); // what WE now occupy, for the next replace range
            Repaginate();

            // Auto-follow the view to wherever what was just typed now lands -
            // if it overflowed onto a new page, jump there automatically
            // instead of leaving the person on a page that no longer shows it.
            int cursorOffset = Math.Max(editStart, editStart + editLength - 1);
            var landedOn = _sliceMap
                .Where(kv => kv.Value.StartOffset <= cursorOffset && cursorOffset < kv.Value.EndOffset)
                .Select(kv => (int?)kv.Key.PageIndex)
                .DefaultIfEmpty(null)
                .Max();
            if (landedOn is int newPageIndex && newPageIndex != _currentPageIndex)
                _currentPageIndex = newPageIndex;

            RefreshFrameTextsForCurrentPage();
        }

        _activePopup.XamlChanged += ApplyEdit;
        _activePopup.Closed += (_, _) =>
        {
            _activePopup = null;
            _activePopupFrame = null;
        };
        _activePopup.Show();
    }

    private void ClosePopup()
    {
        _activePopup?.Close();
        _activePopup = null;
        _activePopupFrame = null;
    }

    // ---------- Save / Load ----------

    private void SaveDb_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Book DB JSON (*.bookdb.json)|*.bookdb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        BookDatabaseSerializer.Save(Database, dialog.FileName);
    }

    private void LoadDb_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Book DB JSON (*.bookdb.json)|*.bookdb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        ClosePopup();
        Database = BookDatabaseSerializer.Load(dialog.FileName);
        _current = null;
        EditorPanel.IsEnabled = false;
        RefreshList();
    }
}
