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
/// The Container Editor - same real parent relationship, tab shape, and
/// general editing pattern as BookEditorWindow, per spec. The Properties and
/// Starting Contents tabs are fully real and working; the Events tab (same
/// real BookEventPage data underneath, already wired on ContainerData) is
/// flagged honestly as the next piece of this system to build, rather than
/// rushed into an incomplete state.
/// </summary>
public partial class ContainerEditorWindow : Window
{
    public ContainerDatabase Database { get; }
    private readonly SpriteDatabase _sprites;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ResourceDatabase _resources;
    private readonly VariableDatabase _variables;
    private readonly StatDatabase _stats;
    private readonly CharacterDatabase _characters = new();
    private bool _suppress;
    private string? _worldSpriteFilterGroup;
    private string? _paperDollFilterGroup;

    /// <summary>Same real principle as BookEditorWindow's own _current - tracked separately from ContainerListBox.SelectedItem so the Event tab's lazy-attach logic (MainTabControl_SelectionChanged) and its callbacks can reach the right container without re-querying the list every time.</summary>
    private ContainerData? _current;
    private EventEditorControl? _eventEditorControl;

    private static readonly ResourceEntry NoneSound = new() { Id = -1, Name = "(None)" };
    /// <summary>Real, confirmed extension - CustomSlotName added so this one choice list can represent a real custom slot too (by its own real name, since custom slots are per-character); Slot and CustomSlotName are mutually exclusive, and both null together means "no preference".</summary>
    private sealed record LocationChoice(EquipmentSlot? Slot, string? CustomSlotName, string Label);
    /// <summary>Real, confirmed fix, same real bug class found in the runtime's own container gump - BuildFramePreview/BuildContentsPreview both rebuild their whole canvas on every single MouseMove during a drag, and every discarded SpriteAnimator's own DispatcherTimer would otherwise just keep running forever, orphaned, never garbage-collected just because nothing references it anymore.</summary>
    private readonly List<SpriteAnimator> _framePreviewAnimators = new();
    private readonly List<SpriteAnimator> _contentsPreviewAnimators = new();

    public ContainerEditorWindow(ContainerDatabase containers, SpriteDatabase sprites, BookDatabase books, ObjectDatabase objects, ResourceDatabase resources, VariableDatabase variables, StatDatabase stats, CharacterDatabase characters)
    {
        InitializeComponent();
        SolidSize.SizeEdited += (_, _) =>
        {
            if (ContainerListBox.SelectedItem is not ContainerData container) return;
            container.SizeX = SolidSize.SizeX;
            container.SizeY = SolidSize.SizeY;
            container.SizeZ = SolidSize.SizeZ;
            container.SolidOffsetX = SolidSize.OffsetX;
            container.SolidOffsetY = SolidSize.OffsetY;
        };
        Title += $" [{BuildInfo.CurrentBuild}]";
        Database = containers;
        _sprites = sprites;
        _books = books;
        _objects = objects;
        _resources = resources;
        _variables = variables;
        _stats = stats;
        // Real, confirmed new dependency, per spec ("make sure it is a functional mechanic for ANY custom slots specified") - needed so the Location dropdown can genuinely list every real custom slot name defined across every real character, not just the two hardcoded container slots.
        _characters = characters;
        RefreshContainerList();
    }

    private void RefreshContainerList()
    {
        ContainerListBox.ItemsSource = null;
        ContainerListBox.ItemsSource = Database.Containers;
    }

    private void NewContainer_Click(object sender, RoutedEventArgs e)
    {
        var container = Database.CreateNew();
        RefreshContainerList();
        ContainerListBox.SelectedItem = container;
    }

    private void DeleteContainer_Click(object sender, RoutedEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        if (MessageBox.Show($"Delete \"{container.Name}\"?", "Container Editor", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Database.Containers.Remove(container);
        RefreshContainerList();
    }

    private void ContainerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container)
        {
            EditorPanel.IsEnabled = false;
            _current = null;
            return;
        }
        EditorPanel.IsEnabled = true;
        _suppress = true;
        _current = container;
        // Real per-container re-attach, same principle as BookEditorWindow's own tab-switch logic - switching to a different container means the event engine needs to detach and lazily re-attach fresh for THIS one, next time the Event tab is actually viewed, rather than keep showing the previous container's own pages.
        _eventEditorControl = null;
        EventEnginePlaceholder.Content = null;

        NameBox.Text = container.Name;
        WeightBox.Text = container.Weight.ToString();
        VolumeBox.Text = container.Volume.ToString();
        PriceBox.Text = container.Price.ToString();

        IsBodyContainerCheck.IsChecked = container.IsBodyContainer;
        // Real, confirmed new field, per spec (issue #3) - only the two real container-holding slots that actually exist are offered; custom slots are a real, separate, larger piece of work not built yet.
        // Real, confirmed fix, per spec ("implement the mechanics of either arm or left arm on ANY... slots I specify... a functional mechanic for ANY custom slots specified, don't just hard code it to backbag or pouch") - Right Hand and Left Hand are real, genuine choices now too, not just an automatic fallback the cascade tries on its own; and every distinct real custom slot name defined across every real character in the project is gathered here and offered too, never hardcoded to a fixed list.
        var locationChoices = new List<LocationChoice>
        {
            new(null, null, "No preference"),
            new(EquipmentSlot.RightHand, null, "Right Hand"),
            new(EquipmentSlot.LeftHand, null, "Left Hand"),
            new(EquipmentSlot.Backpack, null, "Backpack"),
            new(EquipmentSlot.Pouch, null, "Pouch")
        };
        var customSlotNames = _characters.Characters
            .SelectMany(c => c.Paperdoll.CustomSlots)
            .Select(s => s.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var name in customSlotNames)
            locationChoices.Add(new(null, name, $"{name} (custom)"));
        LocationCombo.ItemsSource = locationChoices;
        LocationCombo.DisplayMemberPath = nameof(LocationChoice.Label);
        LocationCombo.SelectedItem = locationChoices.FirstOrDefault(c =>
            container.PreferredCustomSlotName is not null
                ? c.CustomSlotName == container.PreferredCustomSlotName
                : c.Slot == container.PreferredEquipSlot)
            ?? locationChoices[0];
        MaxVolumeBox.Text = container.MaxVolume.ToString();
        IsLockedCheck.IsChecked = container.IsLocked;
        PickLockDifficultyBox.Text = container.PickLockDifficulty.ToString();
        RequiredKeyCategoryCombo.ItemsSource = new[] { ReferencedItemCategory.Book, ReferencedItemCategory.Object };
        RequiredKeyCategoryCombo.SelectedItem = container.RequiredKeyCategory;
        PopulateRequiredKeyItemChoices(container);
        RequiredKeyItemCombo.SelectedItem = container.RequiredKeyCategory switch
        {
            ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == container.RequiredKeyItemId),
            ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == container.RequiredKeyItemId),
            _ => null
        };

        DestructibleCheck.IsChecked = container.Destructible;
        CanBeSoldCheck.IsChecked = container.CanBeSold;
        CanBeTradedCheck.IsChecked = container.CanBeTraded;
        CanBeDroppedCheck.IsChecked = container.CanBeDropped;
        IsStackableCheck.IsChecked = container.IsStackable;
        AllowElevationCheck.IsChecked = container.AllowElevation;
        OccludesCheck.IsChecked = container.Occludes;
        IsSolidCheck.IsChecked = container.IsSolid;
        SolidSize.Load(SolidSizeEditor.LoadSpriteFrame(_sprites, container.SpriteGroup, container.SpriteName), container.SizeX, container.SizeY, container.SizeZ, container.SolidOffsetX, container.SolidOffsetY, container.IsSolid);

        _worldSpriteFilterGroup = string.IsNullOrEmpty(container.SpriteGroup) ? null : container.SpriteGroup;
        WorldSpriteFilterCheck.IsChecked = _worldSpriteFilterGroup is not null;
        PopulateSpriteChoices(WorldSpriteCombo, container.SpriteGroup, container.SpriteName);

        _paperDollFilterGroup = string.IsNullOrEmpty(container.PaperDollSpriteGroup) ? null : container.PaperDollSpriteGroup;
        PaperDollFilterCheck.IsChecked = _paperDollFilterGroup is not null;
        PopulateSpriteChoices(PaperDollCombo, container.PaperDollSpriteGroup, container.PaperDollSpriteName);

        // Real sound pickers, per spec - a flat list, same honest pattern Book's own sound pickers already use (ResourceDatabase.Entries has no real group concept at all, unlike sprites, so there is genuinely nothing to filter by here).
        var soundChoices = new List<ResourceEntry> { NoneSound };
        soundChoices.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        OpenSoundCombo.ItemsSource = new List<ResourceEntry>(soundChoices); // own list: ComboBoxes sharing one list silently change each other's selection in WPF
        CloseSoundCombo.ItemsSource = new List<ResourceEntry>(soundChoices); // own list: ComboBoxes sharing one list silently change each other's selection in WPF
        NavigateSoundCombo.ItemsSource = new List<ResourceEntry>(soundChoices); // own list: ComboBoxes sharing one list silently change each other's selection in WPF
        OpenSoundCombo.SelectedItem = soundChoices.FirstOrDefault(s => s.Id == container.OpenSoundResourceId) ?? NoneSound;
        CloseSoundCombo.SelectedItem = soundChoices.FirstOrDefault(s => s.Id == container.CloseSoundResourceId) ?? NoneSound;
        NavigateSoundCombo.SelectedItem = soundChoices.FirstOrDefault(s => s.Id == container.NavigateSoundResourceId) ?? NoneSound;

        BuildFramePreview(container);
        RefreshContentsList(container);
        PopulateContentsCategoryChoices();

        _suppress = false;
    }

    private void Field_Changed(object sender, RoutedEventArgs e) => SaveFields();
    private void Field_Changed(object sender, TextChangedEventArgs e) => SaveFields();

    private void SaveFields()
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;

        container.Name = NameBox.Text;
        if (float.TryParse(WeightBox.Text, out float w)) container.Weight = w;
        if (float.TryParse(VolumeBox.Text, out float vol)) container.Volume = vol;
        if (int.TryParse(PriceBox.Text, out int price)) container.Price = price;

        container.IsBodyContainer = IsBodyContainerCheck.IsChecked == true;
        var locationChoice = LocationCombo.SelectedItem as LocationChoice;
        container.PreferredEquipSlot = locationChoice?.Slot;
        container.PreferredCustomSlotName = locationChoice?.CustomSlotName;
        if (double.TryParse(MaxVolumeBox.Text, out double maxVol))
        {
            container.MaxVolume = maxVol;
            // Real, confirmed fix, per spec ("the container volume is 128 where as the max volume is 64, yet it somehow managed to put a 128 into a 64 max volume") - AddContents_Click's own real check only ever ran the moment an item was actually added; lowering Max Volume afterward, below what was already authored, was never caught at all, and the real runtime seeding step (OpenContainer, the first time a placed container is ever opened) copies Starting Contents in directly with no check of its own either, faithfully reproducing whatever over-capacity state got authored here. A clear, visible warning now, the moment Max Volume itself is what makes the existing contents invalid, so this can never happen silently again.
            double currentContentsVolume = container.StartingContents.Sum(ResolveItemVolume);
            if (currentContentsVolume > container.MaxVolume)
                MessageBox.Show($"Starting Contents already totals {currentContentsVolume:0.##}, which is now more than this new Max Volume of {container.MaxVolume:0.##} - remove some items or raise Max Volume back up, or this container will start the game already over its own real capacity.", "Container Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshCurrentVolumeDisplay(container);
        }
        container.IsLocked = IsLockedCheck.IsChecked == true;
        if (int.TryParse(PickLockDifficultyBox.Text, out int diff)) container.PickLockDifficulty = diff;
        container.RequiredKeyItemId = RequiredKeyItemCombo.SelectedItem switch
        {
            BookData book => book.Id,
            WorldObject obj => obj.Id,
            _ => -1
        };

        container.Destructible = DestructibleCheck.IsChecked == true;
        container.CanBeSold = CanBeSoldCheck.IsChecked == true;
        container.CanBeTraded = CanBeTradedCheck.IsChecked == true;
        container.CanBeDropped = CanBeDroppedCheck.IsChecked == true;
        container.IsStackable = IsStackableCheck.IsChecked == true;
        container.AllowElevation = AllowElevationCheck.IsChecked == true;
        container.Occludes = OccludesCheck.IsChecked == true;
        container.IsSolid = IsSolidCheck.IsChecked == true;
        SolidSize.SetSolid(container.IsSolid);

        container.OpenSoundResourceId = (OpenSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
        container.CloseSoundResourceId = (CloseSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
        container.NavigateSoundResourceId = (NavigateSoundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;

        ContainerListBox.Items.Refresh();
        RefreshCurrentVolumeDisplay(container);
    }

    private void RequiredKeyCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;
        if (RequiredKeyCategoryCombo.SelectedItem is not ReferencedItemCategory category) return;
        container.RequiredKeyCategory = category;
        PopulateRequiredKeyItemChoices(container);
        SaveFields();
    }

    private void PopulateRequiredKeyItemChoices(ContainerData container)
    {
        RequiredKeyItemCombo.ItemsSource = container.RequiredKeyCategory switch
        {
            ReferencedItemCategory.Book => _books.Books.Cast<object>().ToList(),
            ReferencedItemCategory.Object => _objects.Objects.Cast<object>().ToList(),
            _ => new List<object>()
        };
    }

    private void RefreshCurrentVolumeDisplay(ContainerData container)
    {
        double current = container.StartingContents.Sum(i => ResolveItemVolume(i));
        CurrentVolumeText.Text = $"Current volume (starting contents): {current:0.##} / {container.MaxVolume:0.##}";
    }

    private double ResolveItemVolume(InventoryItemInstance item) => item.Category switch
    {
        ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == item.ItemId)?.Volume ?? 0,
        ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId)?.Volume ?? 0, // Real, confirmed fix - closes the earlier honest gap; WorldObject now has its own real Volume field, same as Book/Container.
        ReferencedItemCategory.Container => Database.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.Volume ?? 0,
        _ => 0
    } * item.Quantity;

    // ---------- Sprites ----------

    private void PopulateSpriteChoices(ComboBox combo, string? filterGroup, string? selectedName)
    {
        List<SpriteEntry> choices;
        if (!string.IsNullOrEmpty(filterGroup))
        {
            var group = _sprites.Groups.FirstOrDefault(g => g.Name == filterGroup);
            choices = group?.Sprites.ToList() ?? new List<SpriteEntry>();
        }
        else
        {
            choices = _sprites.Groups.SelectMany(g => g.Sprites).ToList();
        }
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(s => s.Name == selectedName);
    }

    private void WorldSpriteFilterCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;
        string? chosen = SelectGroupWindow.Show(this, _sprites.Groups.Select(g => g.Name).ToList(), _worldSpriteFilterGroup);
        if (chosen is null) { _suppress = true; WorldSpriteFilterCheck.IsChecked = false; _suppress = false; return; }
        _worldSpriteFilterGroup = chosen;
        PopulateSpriteChoices(WorldSpriteCombo, chosen, container.SpriteName);
    }

    private void WorldSpriteFilterCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;
        _worldSpriteFilterGroup = null;
        PopulateSpriteChoices(WorldSpriteCombo, null, container.SpriteName);
    }

    private void WorldSprite_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;
        if (WorldSpriteCombo.SelectedItem is SpriteEntry sprite)
        {
            container.SpriteName = sprite.Name;
            container.SpriteGroup = _worldSpriteFilterGroup ?? _sprites.Groups.FirstOrDefault(g => g.Sprites.Contains(sprite))?.Name ?? string.Empty;
            SolidSize.SetSprite(SolidSizeEditor.LoadSpriteFrame(_sprites, container.SpriteGroup, container.SpriteName));
        }
    }

    private void PaperDollFilterCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;
        string? chosen = SelectGroupWindow.Show(this, _sprites.Groups.Select(g => g.Name).ToList(), _paperDollFilterGroup);
        if (chosen is null) { _suppress = true; PaperDollFilterCheck.IsChecked = false; _suppress = false; return; }
        _paperDollFilterGroup = chosen;
        PopulateSpriteChoices(PaperDollCombo, chosen, container.PaperDollSpriteName);
    }

    private void PaperDollFilterCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;
        _paperDollFilterGroup = null;
        PopulateSpriteChoices(PaperDollCombo, null, container.PaperDollSpriteName);
    }

    private void PaperDollSprite_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ContainerListBox.SelectedItem is not ContainerData container) return;
        if (PaperDollCombo.SelectedItem is SpriteEntry sprite)
        {
            container.PaperDollSpriteName = sprite.Name;
            container.PaperDollSpriteGroup = _paperDollFilterGroup ?? _sprites.Groups.FirstOrDefault(g => g.Sprites.Contains(sprite))?.Name ?? string.Empty;
        }
        BuildFramePreview(container);
    }

    // ---------- Frame (item placement boundary) ----------

    // ---------- Item Frame: real multi-shape tool (Rectangle/Ellipse/Pencil), reusing BookTextFrame's own real shape system ----------

    private FrameShapeType _currentFrameTool = FrameShapeType.Rectangle;
    private bool _isDrawingNewShape;
    private Point _newShapeStartPoint;
    private readonly List<Point> _freehandPoints = new();
    private const double FreehandCloseThreshold = 12;
    private const int FreehandMinPointsBeforeClose = 10;

    private class ShapeDragState
    {
        public required bool Resizing;
        public required Point StartMouse;
        public required List<BookPoint> StartPoints;
    }
    private ShapeDragState? _shapeDragState;

    private void FrameTool_Click(object sender, RoutedEventArgs e)
    {
        RectToolButton.IsChecked = sender == RectToolButton;
        EllipseToolButton.IsChecked = sender == EllipseToolButton;
        PencilToolButton.IsChecked = sender == PencilToolButton;
        _currentFrameTool = sender switch
        {
            _ when sender == EllipseToolButton => FrameShapeType.Ellipse,
            _ when sender == PencilToolButton => FrameShapeType.Freehand,
            _ => FrameShapeType.Rectangle
        };
    }

    private void BuildFramePreview(ContainerData container)
    {
        // Real, confirmed bug fix - a container saved before Frame became a real BookTextFrame (or any other path that ends up with a genuinely empty Points list) has nothing for Min()/Max() to work with, which is exactly what threw "Sequence contains no elements" the moment a resize or move touched it. A real default rectangle is filled in right here, defensively, the instant this is ever found empty - before the shape is even drawn, so neither building the preview nor starting a later drag from it can ever hit an empty list again.
        if (container.Frame.Points.Count == 0)
            container.Frame.Points = new List<BookPoint> { new(60, 45), new(260, 45), new(260, 195), new(60, 195) };

        foreach (var oldAnimator in _framePreviewAnimators) oldAnimator.Stop();
        _framePreviewAnimators.Clear();
        FramePreviewCanvas.Children.Clear();

        var group = _sprites.Groups.FirstOrDefault(g => g.Name == container.PaperDollSpriteGroup);
        var sprite = group?.Sprites.FirstOrDefault(s => s.Name == container.PaperDollSpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == container.PaperDollSpriteName);
        if (sprite is not null && sprite.Frames.Count > 0)
        {
            var img = new Image { Width = 320, Height = 240, Stretch = Stretch.Uniform };
            _framePreviewAnimators.Add(new SpriteAnimator(sprite, img, null));
            Panel.SetZIndex(img, 0);
            FramePreviewCanvas.Children.Add(img);
        }

        var frame = container.Frame;
        var bounds = container.GetFrameBounds();

        Shape shapeVisual = frame.ShapeType == FrameShapeType.Ellipse
            ? new Ellipse { Width = bounds.Width, Height = bounds.Height }
            : new Polygon { Points = new PointCollection(frame.Points.Select(p => new Point(p.X - bounds.X, p.Y - bounds.Y))) };
        shapeVisual.Stroke = Brushes.LimeGreen;
        shapeVisual.StrokeThickness = 2;
        shapeVisual.Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0));
        shapeVisual.Cursor = Cursors.SizeAll;
        Canvas.SetLeft(shapeVisual, bounds.X);
        Canvas.SetTop(shapeVisual, bounds.Y);
        Panel.SetZIndex(shapeVisual, 5);
        shapeVisual.MouseLeftButtonDown += (_, e) => ShapeMoveOrResize_MouseLeftButtonDown(container, resizing: false, e);
        FramePreviewCanvas.Children.Add(shapeVisual);

        var resizeHandle = new Border { Width = 14, Height = 14, Background = Brushes.LimeGreen, Cursor = Cursors.SizeNWSE };
        Canvas.SetLeft(resizeHandle, bounds.X + bounds.Width - 14);
        Canvas.SetTop(resizeHandle, bounds.Y + bounds.Height - 14);
        Panel.SetZIndex(resizeHandle, 10);
        resizeHandle.MouseLeftButtonDown += (_, e) => ShapeMoveOrResize_MouseLeftButtonDown(container, resizing: true, e);
        FramePreviewCanvas.Children.Add(resizeHandle);

        FramePreviewCanvas.MouseLeftButtonDown -= FramePreviewCanvas_MouseLeftButtonDown;
        FramePreviewCanvas.MouseLeftButtonDown += FramePreviewCanvas_MouseLeftButtonDown;
        FramePreviewCanvas.MouseMove -= FramePreviewCanvas_MouseMove;
        FramePreviewCanvas.MouseMove += FramePreviewCanvas_MouseMove;
        FramePreviewCanvas.MouseLeftButtonUp -= FramePreviewCanvas_MouseLeftButtonUp;
        FramePreviewCanvas.MouseLeftButtonUp += FramePreviewCanvas_MouseLeftButtonUp;
    }

    /// <summary>Starts drawing a real, brand NEW shape, replacing the existing frame - only reached for a click that lands on empty canvas, since the existing shape/handle both mark their own clicks Handled first (see ShapeMoveOrResize_MouseLeftButtonDown) and would route here otherwise.</summary>
    private void FramePreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        _newShapeStartPoint = e.GetPosition(FramePreviewCanvas);
        _isDrawingNewShape = true;
        if (_currentFrameTool == FrameShapeType.Freehand)
        {
            _freehandPoints.Clear();
            _freehandPoints.Add(_newShapeStartPoint);
        }
        FramePreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ShapeMoveOrResize_MouseLeftButtonDown(ContainerData container, bool resizing, MouseButtonEventArgs e)
    {
        // Real, independent second safeguard against the exact same "Sequence contains no elements" crash - BuildFramePreview already guarantees this can't be empty before it's ever displayed, but a drag reading straight from real, current data rather than trusting that guarantee held is worth the one extra line for a crash this severe.
        if (container.Frame.Points.Count == 0)
            container.Frame.Points = new List<BookPoint> { new(60, 45), new(260, 45), new(260, 195), new(60, 195) };

        _shapeDragState = new ShapeDragState
        {
            Resizing = resizing,
            StartMouse = e.GetPosition(FramePreviewCanvas),
            StartPoints = container.Frame.Points.ToList()
        };
        // Real, confirmed bug fix - capturing on the shape/handle itself was the actual cause of the drag getting permanently stuck. BuildFramePreview clears and rebuilds the WHOLE canvas on every real MouseMove during the drag, so the specific element that originally called CaptureMouse() is destroyed mid-drag, which leaves WPF's mouse capture in a broken state - subsequent events stop routing correctly, so the release on mouse-up never actually lands. FramePreviewCanvas itself is never destroyed (only its children are, and are rebuilt), so capturing there instead keeps the whole drag correctly tracked from start to finish, however many times the visuals underneath get rebuilt.
        FramePreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void FramePreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        var current = e.GetPosition(FramePreviewCanvas);

        if (_shapeDragState is { } drag)
        {
            double dx = current.X - drag.StartMouse.X, dy = current.Y - drag.StartMouse.Y;
            if (drag.Resizing)
            {
                double startMinX = drag.StartPoints.Min(p => p.X), startMinY = drag.StartPoints.Min(p => p.Y);
                double startW = Math.Max(1, drag.StartPoints.Max(p => p.X) - startMinX);
                double startH = Math.Max(1, drag.StartPoints.Max(p => p.Y) - startMinY);
                double newW = Math.Max(20, startW + dx), newH = Math.Max(20, startH + dy);
                double scaleX = newW / startW, scaleY = newH / startH;
                container.Frame.Points = drag.StartPoints.Select(p => new BookPoint(startMinX + (p.X - startMinX) * scaleX, startMinY + (p.Y - startMinY) * scaleY)).ToList();
            }
            else
            {
                container.Frame.Points = drag.StartPoints.Select(p => new BookPoint(p.X + dx, p.Y + dy)).ToList();
            }
            BuildFramePreview(container);
            return;
        }

        if (!_isDrawingNewShape) return;

        if (_currentFrameTool == FrameShapeType.Freehand)
        {
            var last = _freehandPoints[^1];
            if (Math.Abs(current.X - last.X) < 3 && Math.Abs(current.Y - last.Y) < 3) return; // real point-density limit - don't record every single pixel of jitter
            _freehandPoints.Add(current);

            // Real auto-close, per spec ("will not let go until point A and point B connect together") - once enough of the path has actually been drawn, the moment it comes back around near its own starting point, the shape finishes right here, live, mid-drag - no separate confirm step.
            if (_freehandPoints.Count >= FreehandMinPointsBeforeClose)
            {
                var first = _freehandPoints[0];
                if (Math.Abs(current.X - first.X) < FreehandCloseThreshold && Math.Abs(current.Y - first.Y) < FreehandCloseThreshold)
                {
                    _freehandPoints[^1] = first; // snap the closing point exactly onto the start, a real closed loop
                    container.Frame.ShapeType = FrameShapeType.Freehand;
                    container.Frame.Points = _freehandPoints.Select(p => new BookPoint(p.X, p.Y)).ToList();
                    _isDrawingNewShape = false;
                    _freehandPoints.Clear();
                    FramePreviewCanvas.ReleaseMouseCapture();
                    BuildFramePreview(container);
                    return;
                }
            }

            // Live preview while still drawing - a real, growing polyline, not yet a saved frame.
            foreach (var a in _framePreviewAnimators) a.Stop();
            _framePreviewAnimators.Clear();
            FramePreviewCanvas.Children.Clear();
            var group = _sprites.Groups.FirstOrDefault(g => g.Name == container.PaperDollSpriteGroup);
            var sprite = group?.Sprites.FirstOrDefault(s => s.Name == container.PaperDollSpriteName) ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == container.PaperDollSpriteName);
            if (sprite is not null && sprite.Frames.Count > 0)
            {
                var img = new Image { Width = 320, Height = 240, Stretch = Stretch.Uniform };
                _framePreviewAnimators.Add(new SpriteAnimator(sprite, img, null));
                FramePreviewCanvas.Children.Add(img);
            }
            var livePreview = new Polyline { Points = new PointCollection(_freehandPoints), Stroke = Brushes.Yellow, StrokeThickness = 2 };
            FramePreviewCanvas.Children.Add(livePreview);
            return;
        }

        // Rectangle/Ellipse - a plain live drag-rect preview between the start point and wherever the mouse is now.
        foreach (var a in _framePreviewAnimators) a.Stop();
        _framePreviewAnimators.Clear();
        FramePreviewCanvas.Children.Clear();
        var g2 = _sprites.Groups.FirstOrDefault(g => g.Name == container.PaperDollSpriteGroup);
        var s2 = g2?.Sprites.FirstOrDefault(s => s.Name == container.PaperDollSpriteName) ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == container.PaperDollSpriteName);
        if (s2 is not null && s2.Frames.Count > 0)
        {
            var img = new Image { Width = 320, Height = 240, Stretch = Stretch.Uniform };
            _framePreviewAnimators.Add(new SpriteAnimator(s2, img, null));
            FramePreviewCanvas.Children.Add(img);
        }
        double left = Math.Min(_newShapeStartPoint.X, current.X), top = Math.Min(_newShapeStartPoint.Y, current.Y);
        double w = Math.Abs(current.X - _newShapeStartPoint.X), h = Math.Abs(current.Y - _newShapeStartPoint.Y);
        Shape preview = _currentFrameTool == FrameShapeType.Ellipse ? new Ellipse { Width = w, Height = h } : new Rectangle { Width = w, Height = h };
        preview.Stroke = Brushes.Yellow;
        preview.StrokeThickness = 2;
        Canvas.SetLeft(preview, left);
        Canvas.SetTop(preview, top);
        FramePreviewCanvas.Children.Add(preview);
    }

    private void FramePreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) { _shapeDragState = null; _isDrawingNewShape = false; Mouse.Capture(null); return; }

        if (_shapeDragState is not null)
        {
            _shapeDragState = null;
            Mouse.Capture(null);
            return;
        }

        if (_isDrawingNewShape)
        {
            _isDrawingNewShape = false;
            Mouse.Capture(null);
            if (_currentFrameTool == FrameShapeType.Freehand)
            {
                // Released before the path ever connected back to its own start - per spec, the pencil tool never commits a shape until that happens, so this attempt is silently discarded and the previous frame stays exactly as it was.
                _freehandPoints.Clear();
            }
            else
            {
                var current = e.GetPosition(FramePreviewCanvas);
                double left = Math.Min(_newShapeStartPoint.X, current.X), top = Math.Min(_newShapeStartPoint.Y, current.Y);
                double w = Math.Max(20, Math.Abs(current.X - _newShapeStartPoint.X)), h = Math.Max(20, Math.Abs(current.Y - _newShapeStartPoint.Y));
                container.Frame.ShapeType = _currentFrameTool;
                container.Frame.Points = new List<BookPoint> { new(left, top), new(left + w, top), new(left + w, top + h), new(left, top + h) };
            }
            BuildFramePreview(container);
        }
    }

    // ---------- Starting Contents ----------

    private class ContentsRow
    {
        public InventoryItemInstance Instance { get; }
        private readonly BookDatabase _books;
        private readonly ObjectDatabase _objects;
        private readonly ContainerDatabase _containers;
        public ContentsRow(InventoryItemInstance instance, BookDatabase books, ObjectDatabase objects, ContainerDatabase containers)
        {
            Instance = instance;
            _books = books;
            _objects = objects;
            _containers = containers;
        }
        public string DisplayText
        {
            get
            {
                string name = Instance.Category switch
                {
                    ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == Instance.ItemId)?.Name ?? "(missing book)",
                    ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == Instance.ItemId)?.Name ?? "(missing object)",
                    ReferencedItemCategory.Container => _containers.Containers.FirstOrDefault(c => c.Id == Instance.ItemId)?.Name ?? "(missing container)",
                    _ => $"(no {Instance.Category} database yet)"
                };
                return Instance.Quantity > 1 ? $"{name} x{Instance.Quantity}" : name;
            }
        }
    }

    private void RefreshContentsList(ContainerData container)
    {
        int selectedIndex = ContentsListBox.SelectedIndex;
        ContentsListBox.ItemsSource = container.StartingContents.Select(i => new ContentsRow(i, _books, _objects, Database)).ToList();
        if (selectedIndex >= 0 && selectedIndex < ContentsListBox.Items.Count) ContentsListBox.SelectedIndex = selectedIndex;
        RefreshCurrentVolumeDisplay(container);
        BuildContentsPreview(container);
    }

    private InventoryItemInstance? _selectedContentsItem;
    private InventoryItemInstance? _pendingContentsDragItem;
    private Point _pendingContentsDragStart;

    private void ContentsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedContentsItem = (ContentsListBox.SelectedItem as ContentsRow)?.Instance;
        if (ContainerListBox.SelectedItem is ContainerData container) BuildContentsPreview(container);
    }

    private void MoveContentsUp_Click(object sender, RoutedEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        if (ContentsListBox.SelectedItem is not ContentsRow row) return;
        int index = container.StartingContents.IndexOf(row.Instance);
        if (index <= 0) return;
        (container.StartingContents[index - 1], container.StartingContents[index]) = (container.StartingContents[index], container.StartingContents[index - 1]);
        int newIndex = index - 1;
        ContentsListBox.ItemsSource = container.StartingContents.Select(i => new ContentsRow(i, _books, _objects, Database)).ToList();
        ContentsListBox.SelectedIndex = newIndex;
        BuildContentsPreview(container);
    }

    private void MoveContentsDown_Click(object sender, RoutedEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        if (ContentsListBox.SelectedItem is not ContentsRow row) return;
        int index = container.StartingContents.IndexOf(row.Instance);
        if (index < 0 || index >= container.StartingContents.Count - 1) return;
        (container.StartingContents[index + 1], container.StartingContents[index]) = (container.StartingContents[index], container.StartingContents[index + 1]);
        int newIndex = index + 1;
        ContentsListBox.ItemsSource = container.StartingContents.Select(i => new ContentsRow(i, _books, _objects, Database)).ToList();
        ContentsListBox.SelectedIndex = newIndex;
        BuildContentsPreview(container);
    }

    /// <summary>
    /// The real, played-in-engine look, per spec ("not the editor mode
    /// were I can see the frame... I want it the mode as if it is
    /// played in the runtime mode") - the exact same fixed 320x240 body
    /// display GameTestWindow's own RefreshContainerGump uses, with each
    /// starting-contents item's own real visual drawn directly at its
    /// own stored ContainerX/Y, in List order (first = bottom of the
    /// stack, matching the runtime's own real stacking rule exactly).
    /// Dragging one here moves it for real, live, within the frame's own
    /// real boundary, saved directly onto this container's own
    /// StartingContents the moment it's dropped - so what the author
    /// arranges here is genuinely what a fresh placement of this
    /// container looks like at runtime, not a separate preview of it.
    /// </summary>
    private void BuildContentsPreview(ContainerData container)
    {
        // Real, confirmed fix, same real bug class as the runtime's own container gump - this rebuilds its whole canvas on every single MouseMove while dragging an item within the preview, so every discarded animator must be stopped first or its own DispatcherTimer just keeps running forever, orphaned.
        foreach (var oldAnimator in _contentsPreviewAnimators) oldAnimator.Stop();
        _contentsPreviewAnimators.Clear();
        ContentsPreviewCanvas.Children.Clear();

        var bodyGroup = _sprites.Groups.FirstOrDefault(g => g.Name == container.PaperDollSpriteGroup);
        var bodySprite = bodyGroup?.Sprites.FirstOrDefault(s => s.Name == container.PaperDollSpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == container.PaperDollSpriteName);
        if (bodySprite is not null && bodySprite.Frames.Count > 0)
        {
            var bg = new Image { Width = 320, Height = 240, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            _contentsPreviewAnimators.Add(new SpriteAnimator(bodySprite, bg, _resources));
            Panel.SetZIndex(bg, 0);
            ContentsPreviewCanvas.Children.Add(bg);
        }

        int z = 5;
        foreach (var item in container.StartingContents)
        {
            FrameworkElement? visual = null;
            if (item.Category == ReferencedItemCategory.Book)
            {
                var book = _books.Books.FirstOrDefault(b => b.Id == item.ItemId);
                var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == book?.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book?.SpriteName);
                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var img = new Image { Width = 32, Height = 32, Stretch = Stretch.Uniform };
                    _contentsPreviewAnimators.Add(new SpriteAnimator(sprite, img, _resources));
                    visual = img;
                }
            }
            else if (item.Category == ReferencedItemCategory.Object)
            {
                var obj = _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId);
                if (!string.IsNullOrEmpty(obj?.IconPath) && File.Exists(obj.IconPath))
                    visual = new Image { Source = LoadBitmapForPreview(obj.IconPath), Width = 32, Height = 32, Stretch = Stretch.Uniform };
            }
            else if (item.Category == ReferencedItemCategory.Container)
            {
                var inner = Database.Containers.FirstOrDefault(c => c.Id == item.ItemId);
                var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == inner?.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == inner?.SpriteName);
                if (sprite is not null && sprite.Frames.Count > 0)
                {
                    var img = new Image { Width = 32, Height = 32, Stretch = Stretch.Uniform };
                    _contentsPreviewAnimators.Add(new SpriteAnimator(sprite, img, _resources));
                    visual = img;
                }
            }
            visual ??= new Border { Width = 32, Height = 32, Background = Brushes.Gray };

            // Real selection square, per spec - the selected sprite's own size plus 16 in each dimension, any real color but never black.
            if (item == _selectedContentsItem)
            {
                var selectionSquare = new Border
                {
                    Width = visual.Width + 16,
                    Height = visual.Height + 16,
                    BorderBrush = Brushes.Yellow,
                    BorderThickness = new Thickness(2)
                };
                Canvas.SetLeft(selectionSquare, item.ContainerX - 8);
                Canvas.SetTop(selectionSquare, item.ContainerY - 8);
                Panel.SetZIndex(selectionSquare, z + 1000); // always visible on top of every real item, selected or not
                ContentsPreviewCanvas.Children.Add(selectionSquare);
            }

            Canvas.SetLeft(visual, item.ContainerX);
            Canvas.SetTop(visual, item.ContainerY);
            Panel.SetZIndex(visual, z++);
            visual.Cursor = Cursors.Hand;
            var capturedItem = item;
            visual.MouseLeftButtonDown += (_, e) =>
            {
                ContentsListBox.SelectedItem = ContentsListBox.Items.Cast<ContentsRow>().FirstOrDefault(r => r.Instance == capturedItem);
                _pendingContentsDragItem = capturedItem;
                _pendingContentsDragStart = e.GetPosition(ContentsPreviewCanvas);
                ContentsPreviewCanvas.CaptureMouse();
                e.Handled = true;
            };
            ContentsPreviewCanvas.Children.Add(visual);
        }

        ContentsPreviewCanvas.MouseMove -= ContentsPreviewCanvas_MouseMove;
        ContentsPreviewCanvas.MouseMove += ContentsPreviewCanvas_MouseMove;
        ContentsPreviewCanvas.MouseLeftButtonUp -= ContentsPreviewCanvas_MouseLeftButtonUp;
        ContentsPreviewCanvas.MouseLeftButtonUp += ContentsPreviewCanvas_MouseLeftButtonUp;
    }

    private void ContentsPreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pendingContentsDragItem is not { } item || ContainerListBox.SelectedItem is not ContainerData container || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(ContentsPreviewCanvas);
        if (Math.Abs(current.X - _pendingContentsDragStart.X) < DragThresholdContents && Math.Abs(current.Y - _pendingContentsDragStart.Y) < DragThresholdContents) return;

        var bounds = container.GetFrameBounds();
        item.ContainerX = Math.Clamp(current.X - 16, bounds.X, bounds.X + bounds.Width - 32);
        item.ContainerY = Math.Clamp(current.Y - 16, bounds.Y, bounds.Y + bounds.Height - 32);
        BuildContentsPreview(container);
    }

    private void ContentsPreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pendingContentsDragItem = null;
        ContentsPreviewCanvas.ReleaseMouseCapture();
    }

    private const double DragThresholdContents = 3;

    private static BitmapImage? LoadBitmapForPreview(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }

    private void PopulateContentsCategoryChoices()
    {
        ContentsCategoryCombo.ItemsSource = new[] { ReferencedItemCategory.Book, ReferencedItemCategory.Object, ReferencedItemCategory.Container };
        ContentsCategoryCombo.SelectionChanged -= ContentsCategoryCombo_SelectionChanged;
        ContentsCategoryCombo.SelectionChanged += ContentsCategoryCombo_SelectionChanged;
        ContentsCategoryCombo.SelectedIndex = 0;
    }

    private void ContentsCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContentsCategoryCombo.SelectedItem is not ReferencedItemCategory category) return;
        ContentsItemCombo.ItemsSource = category switch
        {
            ReferencedItemCategory.Book => _books.Books.Cast<object>().ToList(),
            ReferencedItemCategory.Object => _objects.Objects.Cast<object>().ToList(),
            ReferencedItemCategory.Container => Database.Containers.Cast<object>().ToList(),
            _ => new List<object>()
        };
    }

    private void AddContents_Click(object sender, RoutedEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        if (ContentsCategoryCombo.SelectedItem is not ReferencedItemCategory category) return;
        int itemId = ContentsItemCombo.SelectedItem switch
        {
            BookData book => book.Id,
            WorldObject obj => obj.Id,
            ContainerData innerContainer => innerContainer.Id,
            _ => -1
        };
        if (itemId < 0)
        {
            MessageBox.Show("Pick an item first.", "Container Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (category == ReferencedItemCategory.Container && itemId == container.Id)
        {
            MessageBox.Show("A container can't hold itself.", "Container Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Real, confirmed fix, per spec ("Prevent all illegal recursive containment: a container cannot contain itself, cannot be placed inside any of its own descendants, and cannot create a circular container relationship") - a full recursive walk of the CANDIDATE container's own real descendant tree (through nested containers' own real StartingContents), not just a direct one-level check.
        if (category == ReferencedItemCategory.Container && ContainerContainsRecursively(Database, itemId, container.Id))
        {
            MessageBox.Show("That would create a circular container relationship - it already contains this container somewhere inside it.", "Container Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Real, confirmed bug fix, per spec (issue #5 - "all objects are at the top left corner... they look like one") - a brand new item defaulted to (0,0), stacking every new addition on the exact same spot. A reasonable starting position (the frame's own real center) is filled in here instead - the author can still drag it anywhere they like afterward in the live preview below.
        var newItemBounds = container.GetFrameBounds();
        var newItem = new InventoryItemInstance
        {
            InstanceId = container.GetNextContentsInstanceId(),
            Category = category,
            ItemId = itemId,
            ContainerX = Math.Clamp(newItemBounds.X + newItemBounds.Width / 2 - 16, 0, 320 - 32),
            ContainerY = Math.Clamp(newItemBounds.Y + newItemBounds.Height / 2 - 16, 0, 240 - 32)
        };
        double newVolume = container.StartingContents.Sum(ResolveItemVolume) + ResolveItemVolume(newItem);
        if (newVolume > container.MaxVolume)
        {
            MessageBox.Show($"This won't fit - starting contents would total {newVolume:0.##}, but Max Volume is {container.MaxVolume:0.##}.", "Container Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        container.StartingContents.Add(newItem);
        RefreshContentsList(container);
    }

    /// <summary>Real, full recursive descendant check, per spec - true if searchForContainerId appears anywhere within startFromContainerId's own real StartingContents tree, however deeply nested. visited guards against an already-broken cycle in existing data causing infinite recursion here.</summary>
    private static bool ContainerContainsRecursively(ContainerDatabase db, int startFromContainerId, int searchForContainerId, HashSet<int>? visited = null)
    {
        visited ??= new HashSet<int>();
        if (!visited.Add(startFromContainerId)) return false; // already walked this one on this path - broken data, not a real answer either way
        var startFrom = db.Containers.FirstOrDefault(c => c.Id == startFromContainerId);
        if (startFrom is null) return false;
        foreach (var content in startFrom.StartingContents)
        {
            if (content.Category != ReferencedItemCategory.Container) continue;
            if (content.ItemId == searchForContainerId) return true;
            if (ContainerContainsRecursively(db, content.ItemId, searchForContainerId, visited)) return true;
        }
        return false;
    }

    private void RemoveContents_Click(object sender, RoutedEventArgs e)
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        if (ContentsListBox.SelectedItem is not ContentsRow row) return;
        container.StartingContents.Remove(row.Instance);
        RefreshContentsList(container);
    }

    // ---------- Event tab: lazy-attach the shared engine, same real pattern as BookEditorWindow ----------

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_current is null) return;
        if (MainTabControl.SelectedItem is not TabItem item || item.Header as string != "Event") return;
        if (_eventEditorControl is not null) return; // already attached for this container

        // Never headless - same real guarantee as Book's own event, an event with zero pages/conditions is impossible by design.
        if (_current.EventPages.Count == 0)
            _current.EventPages.Add(new BookEventPage());

        var enginePages = new System.Collections.ObjectModel.ObservableCollection<EventCommandPage>(
            _current.EventPages.Select((p, i) => new EventCommandPage { Label = (i + 1).ToString(), Commands = p.Commands }));

        _eventEditorControl = new EventEditorControl();
        _eventEditorControl.ResourceDatabase = _resources;
        _eventEditorControl.Variables = _variables;
        _eventEditorControl.CurrentBookLocalVariables = _current.LocalVariables;
        _eventEditorControl.Books = _books;
        _eventEditorControl.Objects = _objects;
        _eventEditorControl.Sprites = _sprites;
        // Real, honest gap - CurrentBook is only ever used for the "Change Book Content" command, which genuinely has no meaning for a container (a container has no text content to change), so it stays null here on purpose; every other command type works identically to Book's own event editor.
        _eventEditorControl.CurrentBook = null;
        _eventEditorControl.PageSelected += OnEventPageSelected;
        _eventEditorControl.PageInserted += OnEventPageInserted;
        _eventEditorControl.PageRemoved += OnEventPageRemoved;
        _eventEditorControl.ContentChanged += OnEventContentChanged;
        _eventEditorControl.Pages = enginePages;
        EventEnginePlaceholder.Content = _eventEditorControl;

        ContainerConditionsControl.CurrentBookId = _current.Id;
        ContainerConditionsControl.CurrentBookLocalVariables = _current.LocalVariables;
        ContainerConditionsControl.SetDatabases(_books, _objects, Database, _sprites, _variables, _stats);
        ContainerConditionsControl.ApplyContainerTriggerLabels();
        ContainerConditionsControl.Conditions = _current.EventPages[0].Conditions;
    }

    private void OnEventPageSelected(int index)
    {
        if (_current is null) return;
        ContainerConditionsControl.Conditions = index >= 0 && index < _current.EventPages.Count
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

    private void OnEventContentChanged()
    {
        // Same real reason as Book's own version - some engine operations replace a page's Commands list wholesale rather than mutating it in place, which would otherwise silently break the shared reference back to the container's own data.
        if (_current is null || _eventEditorControl is null) return;
        var enginePages = _eventEditorControl.Pages;
        for (int i = 0; i < enginePages.Count && i < _current.EventPages.Count; i++)
            _current.EventPages[i].Commands = enginePages[i].Commands;
    }

    private void EventDebugButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (ContainerConditionsControl.Conditions is not { } conditions)
        {
            MessageBox.Show("Select an event page first.", "Debug Event", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var commands = _eventEditorControl?.CurrentEditingPage?.Commands ?? new List<EventCommand>();

        var dialog = new BookEventDebugWindow(conditions, commands, _resources, _stats, _current.Id) { Owner = this };
        dialog.Show();
    }

    // ---------- Shared input validation, same real pattern as BookEditorWindow ----------

    private void DecimalOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        string proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !(proposed.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(proposed, @"^\d*\.?\d*$"));
    }

    private void IntegerOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        string proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !(proposed.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(proposed, @"^\d*$"));
    }
}
