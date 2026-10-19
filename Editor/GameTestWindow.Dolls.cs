using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Objects' paperdolls (PaperDoll Editor) in play - Exult's object gumps. A
/// paperdoll opens on its own window on the gump layer, like a container's:
/// dragged by its body, closed with Escape, pausing the game while open.
/// Every placed (or carried) copy keeps its own state (DollState), which
/// travels with it. Built-in behaviour, then the part's own events:
///  - Button: click shows its Pressed look, then By Press.
///  - Switch: click moves it to its next look, then By Switched.
///  - Lid: double-click opens / closes it; closed, what it covers is hidden
///    or out of reach.
///  - Slot: drag an item onto it to put it in (only what it takes, while it
///    has room); it shows in its fitted look; drag it out to take it back.
///  - Item area: holds anything, where it was dropped.
///  - Slider: a click along its track sets its value; Displays follow it.
///  - Close button: closes the paperdoll.
/// A sound started by a part's event (Play Sound) keeps playing after the
/// paperdoll closes; a sprite's own frame sounds stop with its window.
/// </summary>
public partial class GameTestWindow
{
    private class OpenDollGump
    {
        public required DollState State;
        public required DollDesign Design;
        public required WorldObject Obj;
        public required IHasDollState Owner;
        public required Border GumpBorder;
        public required Canvas PartsCanvas;
        public readonly List<SpriteAnimator> Animators = new();
        /// <summary>Modal (above everything, nothing under it reachable): its layer; 0 = not modal.</summary>
        public int ModalLayer;
        /// <summary>A modal paperdoll's cover over the game and every other window.</summary>
        public Border? Blocker;
        /// <summary>Whoever opened it to ask (a save prompt, Open Paperdoll): hears yes (Yes / OK) or no.</summary>
        public Action<bool>? Answered;
        /// <summary>Its own size on screen (the Title Screen fills the game's view); null = a paperdoll's usual size.</summary>
        public double? Scale;
    }

    private readonly List<OpenDollGump> _openDolls = new();

    /// <summary>A part of an open paperdoll (only the parts that do something are clickable; the rest is its body, for dragging the window).</summary>
    private sealed record DollPartTarget(OpenDollGump Gump, int PartId) : InteractionTarget;
    /// <summary>An item inside a paperdoll's slot or item area.</summary>
    private sealed record DollItemTarget(OpenDollGump Gump, int PartId, InventoryItemInstance Item) : InteractionTarget;
    /// <summary>An item being dragged out of a paperdoll's slot or item area.</summary>
    private sealed record DollItemOrigin(DollState State, int PartId, InventoryItemInstance Item) : DragOrigin;

    /// <summary>The same normalized scale as a container's window, so a 320 x 240 paperdoll fills the view the same way.</summary>
    private double DollGumpScale => ViewportScale.ScaleX * (200.0 / ContainerBodyHeight);
    /// <summary>Items drawn in their own look are counter-scaled to the size they have on the map, as in a container.</summary>
    private const double DollItemCounterScale = ContainerBodyHeight / 200.0;

    // ================= Opening and closing =================

    /// <summary>Double-clicking an object that opens a paperdoll: its own By Use event first (as a container's), else the paperdoll opens. True = it has one.</summary>
    private bool TryUseDoll(IHasDollState owner, WorldObject obj, Func<bool> fireUseEvent)
    {
        if (DollLook.DesignOf(obj, _objects) is not { } design) return false;
        // The object's own events can act on its paperdoll (Paperdoll command) - a radio's By Use could start it playing.
        if (WithDoll(new DollContext { State = DollStateOf(owner, design), Design = design, Owner = owner, Obj = obj }, fireUseEvent)) return true;
        OpenDoll(owner, obj, design);
        return true;
    }

    /// <summary>Made from the design the first time this copy is used; kept from then on (a design swapped in the Object Editor starts fresh).</summary>
    private static DollState DollStateOf(IHasDollState owner, DollDesign design)
    {
        if (owner.DollRuntime is not { } state || state.DesignId != design.Id) owner.DollRuntime = state = DollState.StartOf(design);
        return state;
    }

    private void OpenDoll(IHasDollState owner, WorldObject obj, DollDesign design)
    {
        var state = DollStateOf(owner, design);
        var existing = _openDolls.FirstOrDefault(g => ReferenceEquals(g.State, state));
        if (existing is not null) { BringDollToFront(existing); return; }

        // Every PaperDoll Editor paperdoll opens with System Settings > Sounds > Paperdoll open (none set = silent).
        PlaySound(_systemSettings.Sounds.PaperdollOpenSoundId);
        var canvas = new Canvas(); // no background: only its parts are part of it (pixel-precise), as a container's art
        var border = new Border { Child = canvas };
        var gump = new OpenDollGump { State = state, Design = design, Obj = obj, Owner = owner, GumpBorder = border, PartsCanvas = canvas };
        int open = _openContainers.Count + _openDolls.Count;
        Canvas.SetLeft(border, 40 + open * 36);
        Canvas.SetTop(border, 30 + open * 30);
        // Its body (every part that does nothing on a click) drags the window; clickable parts are claimed by the interaction pipeline first.
        canvas.MouseLeftButtonDown += (_, e) => { BringDollToFront(gump); PaperdollTitleBar_MouseLeftButtonDown(gump, border, canvas, e); };
        canvas.MouseMove += (_, e) => PaperdollTitleBar_MouseMove(border, e);
        canvas.MouseLeftButtonUp += (_, _) => PaperdollTitleBar_MouseLeftButtonUp(canvas);
        PaperdollGumpsCanvas.Children.Add(border);
        _openDolls.Add(gump);
        ReadControlVariables(gump); // controls kept in variables show them (GameTestWindow.DollControls.cs)
        BringDollToFront(gump);
        RefreshDollGump(gump);
        StatusText.Text = $"You open \"{obj.Name}\".";
    }

    private void BringDollToFront(OpenDollGump gump)
    {
        Panel.SetZIndex(gump.GumpBorder, gump.ModalLayer > 0 ? gump.ModalLayer + 1 : 200);
        TrackGumpFocus(gump);
    }

    private void CloseDollGump(OpenDollGump gump)
    {
        if (gump.Design.CloseSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, gump.Design.CloseSoundResourceId);
        foreach (var animator in gump.Animators) animator.Stop();
        gump.Animators.Clear();
        PaperdollGumpsCanvas.Children.Remove(gump.GumpBorder);
        _openDolls.Remove(gump);
        SaveScreenClosed(gump);
        UntrackGumpFocus(gump);
        DollControlsClosed(gump); // last: an unanswered question hears no once it's gone
        TitleClosed(gump); // the Title Screen closing starts the game (GameTestWindow.Title.cs)
        if (_openPaperdolls.Count == 0 && _openContainers.Count == 0 && _openDolls.Count == 0) GameCanvas.Focus();
    }

    // ================= Drawing =================

    private static bool IsClickable(DollPart part) =>
        part.Kind is DollPartKind.Button or DollPartKind.Switch or DollPartKind.Lid or DollPartKind.Slot or DollPartKind.ItemArea
            or DollPartKind.Slider or DollPartKind.Hotspot or DollPartKind.CloseButton
            or DollPartKind.SaveList or DollPartKind.SaveName or DollPartKind.SaveButton or DollPartKind.LoadButton or DollPartKind.DeleteSaveButton
            or DollPartKind.ConfirmButton or DollPartKind.CancelButton or DollPartKind.TextBox or DollPartKind.CheckBox or DollPartKind.RadioButton
            or DollPartKind.DropDown or DollPartKind.ListBox || DollPart.IsTitleKind(part.Kind)
        || part.EventPages.Any(p => p.Commands.Count > 0); // a picture, light, text or display with its own events

    private (int Look, bool Visible) DollStateOf(OpenDollGump gump, DollPart part)
    {
        var runtime = gump.State.Of(part);
        return (runtime.Look, runtime.Visible);
    }

    /// <summary>Under a closed lid that only puts it out of reach (a glass door): seen, but not clickable.</summary>
    private bool IsUnderClosedLid(OpenDollGump gump, DollPart part) => DollLook.ClosedLidOver(part, gump.Design, p => DollStateOf(gump, p)) is not null;

    private void RefreshDollGump(OpenDollGump gump)
    {
        // As a container's window: every animator stopped before the rebuild, or its timer would tick on forever.
        foreach (var animator in gump.Animators) animator.Stop();
        gump.Animators.Clear();
        var canvas = gump.PartsCanvas;
        canvas.Children.Clear();
        var design = gump.Design;
        canvas.Width = design.Width;
        canvas.Height = design.Height;
        canvas.LayoutTransform = new ScaleTransform(gump.Scale ?? DollGumpScale, gump.Scale ?? DollGumpScale);

        int z = 0;
        foreach (var part in design.Parts)
        {
            z += 10;
            var runtime = gump.State.Of(part);
            if (!runtime.Visible) continue;
            if ((_gameOver || InTitle) && part.Kind == DollPartKind.SaveButton && IsSaveScreen(gump)) continue; // dead or at the title: load only, no Save button
            var lid = DollLook.ClosedLidOver(part, design, p => DollStateOf(gump, p));
            if (lid is { HidesWhatItCovers: true }) continue;
            bool reachable = lid is null;
            var visual = BuildDollPartVisual(gump, part, runtime);
            Canvas.SetLeft(visual, runtime.X ?? part.X); // Move Part
            Canvas.SetTop(visual, runtime.Y ?? part.Y);
            if (runtime.Disabled) { visual.Opacity = 0.45; reachable = false; } // Enable / Disable Part: greyed, out of reach
            Panel.SetZIndex(visual, IsOpenDropDown(gump, part) ? int.MaxValue / 2 : z); // an open list over everything
            if (reachable && IsClickable(part))
            {
                visual.Tag = new DollPartTarget(gump, part.Id); // tagged, not wired - the one mouse pipeline handles it
                visual.Cursor = Cursors.Hand;
            }
            else visual.IsHitTestVisible = part.Kind is not (DollPartKind.Hotspot or DollPartKind.Slot or DollPartKind.ItemArea); // out of reach: art only
            canvas.Children.Add(visual);
            if (part.Kind is DollPartKind.Slot or DollPartKind.ItemArea) AddDollItems(gump, part, runtime, reachable, z + 5);
        }
    }

    private FrameworkElement AnimatedLook(OpenDollGump gump, SpriteEntry sprite)
    {
        var first = DollLook.Frame(sprite);
        var image = new AlphaHitImage { Width = first?.PixelWidth ?? 32, Height = first?.PixelHeight ?? 32, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        gump.Animators.Add(new SpriteAnimator(sprite, image, _resources)); // its frames and frame sounds, as the Sprite Editor made them
        return image;
    }

    private FrameworkElement BuildDollPartVisual(OpenDollGump gump, DollPart part, DollPartRuntime runtime)
    {
        part = part.WithRuntime(runtime); // its words and choices as events set them
        if (DollPart.IsControlKind(part.Kind)) return DollControlVisual(gump, part, runtime); // buttons with captions, boxes, lists (DollControls)
        var size = DollLook.SizeOf(part, runtime.Look, _sprites);
        switch (part.Kind)
        {
            case DollPartKind.Hotspot:
                return new Border { Width = size.Width, Height = size.Height, Background = Brushes.Transparent };
            case DollPartKind.Text:
            {
                if (DollLook.TextPicture(part, _sprites) is { } words)
                {
                    var image = new Image { Source = words, Width = words.PixelWidth, Height = words.PixelHeight, Stretch = Stretch.None };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                    return new Border { Background = Brushes.Transparent, Child = image };
                }
                return new TextBlock { Text = part.Text, Foreground = Brushes.White, FontSize = 8 * Math.Max(1, part.TextScale), TextWrapping = TextWrapping.Wrap, Width = size.Width, Background = Brushes.Transparent };
            }
            case DollPartKind.Slider:
            {
                var holder = new Canvas { Width = size.Width, Height = size.Height, Background = Brushes.Transparent };
                if (DollLook.LookSprite(part, 0, _sprites) is { } track) holder.Children.Add(AnimatedLook(gump, track));
                var knobSprite = DollLook.LookSprite(part, 1, _sprites);
                FrameworkElement knob = knobSprite is not null ? AnimatedLook(gump, knobSprite)
                    : new Border { Width = Math.Min(8, size.Width), Height = size.Height, Background = Brushes.Silver };
                var at = DollLook.KnobAt(part, runtime.Value, size, new Size(knob.Width, knob.Height));
                Canvas.SetLeft(knob, at.X);
                Canvas.SetTop(knob, at.Y);
                knob.IsHitTestVisible = false;
                holder.Children.Add(knob);
                return holder;
            }
            case DollPartKind.Display:
            {
                int value = DollLook.DisplayValue(part, gump.Design, p => gump.State.Of(p).Value);
                int min = part.ShowsPartId >= 0 && gump.Design.Part(part.ShowsPartId) is { } source ? source.Min : part.Min;
                if (DollLook.Frame(DollLook.LookSprite(part, 0, _sprites), value - min) is { } frame)
                {
                    var image = new AlphaHitImage { Source = frame, Width = frame.PixelWidth, Height = frame.PixelHeight, Stretch = Stretch.Fill };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                    return image;
                }
                return new TextBlock { Text = value.ToString(), Foreground = Brushes.White, FontSize = 10 };
            }
            case DollPartKind.SaveList or DollPartKind.SaveName or DollPartKind.SaveScreenshot or DollPartKind.SaveDetails:
                return SaveScreenPartVisual(gump, part, size); // GameTestWindow.SaveLoad.cs
            case DollPartKind.Slot:
            case DollPartKind.ItemArea:
            {
                // Its hole catches drops and clicks even where its picture is transparent.
                var holder = new Canvas { Width = size.Width, Height = size.Height, Background = Brushes.Transparent };
                if (DollLook.LookSprite(part, 0, _sprites) is { } behind) holder.Children.Add(AnimatedLook(gump, behind));
                return holder;
            }
            default:
            {
                // A button's Pressed look falls back to its normal one when it has none.
                var sprite = DollLook.LookSprite(part, runtime.Look, _sprites) ?? DollLook.LookSprite(part, 0, _sprites);
                if (sprite is not null) return AnimatedLook(gump, sprite);
                // No sprite: a clickable part is still there to click (its own size), a picture is nothing.
                return part.Kind is DollPartKind.Picture or DollPartKind.Light
                    ? new Border { Width = 0, Height = 0 }
                    : new Border { Width = size.Width, Height = size.Height, Background = Brushes.Transparent };
            }
        }
    }

    private void AddDollItems(OpenDollGump gump, DollPart part, DollPartRuntime runtime, bool reachable, int z)
    {
        double rowX = 0;
        foreach (var item in runtime.Items)
        {
            var fit = DollLook.FitFor(part, item.Category, item.ItemId);
            FrameworkElement visual;
            double left, top;
            if (fit is { FittedSprite.IsSet: true } && DollLook.SpriteOf(fit.FittedSprite, _sprites) is { } fitted)
            {
                // In the slot it shows its fitted look (a cassette in the tray), at its fitted spot.
                visual = AnimatedLook(gump, fitted);
                left = part.X + fit.OffsetX;
                top = part.Y + fit.OffsetY;
            }
            else
            {
                visual = item.Category == ReferencedItemCategory.Object ? BuildCarriedObjectPicture(item, DollItemCounterScale, gump.Animators) ?? ScaledItemIcon(gump, item) : ScaledItemIcon(gump, item);
                double width = visual.Width * (visual.LayoutTransform is ScaleTransform s ? s.ScaleX : 1), height = visual.Height * (visual.LayoutTransform is ScaleTransform t ? t.ScaleY : 1);
                if (part.Kind == DollPartKind.Slot)
                {
                    left = part.X + (part.Width - width) / 2;
                    top = part.Y + (part.Height - height) / 2;
                }
                else
                {
                    if (item.ContainerX <= UnplacedSpot || item.ContainerY <= UnplacedSpot) { item.ContainerX = rowX; item.ContainerY = 0; }
                    rowX = Math.Max(rowX, item.ContainerX + width + 2);
                    left = part.X + item.ContainerX;
                    top = part.Y + item.ContainerY;
                }
            }
            Canvas.SetLeft(visual, left);
            Canvas.SetTop(visual, top);
            Panel.SetZIndex(visual, z++);
            if (reachable)
            {
                visual.Tag = new DollItemTarget(gump, part.Id, item);
                visual.Cursor = Cursors.Hand;
            }
            else visual.IsHitTestVisible = false;
            HideIfBeingDragged(visual);
            gump.PartsCanvas.Children.Add(visual);
        }
    }

    private FrameworkElement ScaledItemIcon(OpenDollGump gump, InventoryItemInstance item)
    {
        var icon = BuildItemIcon(item.Category, item.ItemId, gump.Animators, item);
        icon.LayoutTransform = new ScaleTransform(DollItemCounterScale, DollItemCounterScale);
        return icon;
    }

    private FrameworkElement? DollPartVisual(OpenDollGump gump, int partId) =>
        gump.PartsCanvas.Children.OfType<FrameworkElement>().FirstOrDefault(c => c.Tag is DollPartTarget t && t.PartId == partId);

    // ================= Events =================

    /// <summary>The paperdoll a Paperdoll command acts on: the one whose part's event is running, or the paperdoll of the object whose own event is running.</summary>
    private sealed class DollContext
    {
        public required DollState State;
        public required DollDesign Design;
        public IHasDollState? Owner;
        public WorldObject? Obj;
        /// <summary>By Combined With An Item: the item dropped on the part (still where it came from).</summary>
        public DragOrigin? Combined;
    }

    private DollContext? _dollContext;
    /// <summary>The last one set: commands after a Show Text run once its box is read, when the event that started them has returned.</summary>
    private DollContext? _lastDollContext;

    private T WithDoll<T>(DollContext context, Func<T> run)
    {
        var previous = _dollContext;
        _dollContext = _lastDollContext = context;
        try { return run(); }
        finally { _dollContext = previous; }
    }

    /// <summary>
    /// Runs a part's own event pages for a trigger - counted per copy of the
    /// object and per part. A page set to an item (Put In, Taken Out,
    /// Combined With An Item) runs only for that item; one set to a part
    /// (Combined With A Part) only for that part.
    /// </summary>
    private bool FireDollPartEvent(DollState state, DollPart part, BookTriggerConditionBased trigger, string what,
        ItemReference? item = null, int combinedPartId = -1, DragOrigin? combined = null)
    {
        if (part.EventPages.Count == 0) return false;
        var pages = part.EventPages.Where(page =>
        {
            var conditions = page.Conditions;
            if (conditions.TriggerConditionBased != trigger) return true; // other pages decide by their own conditions
            if (trigger is BookTriggerConditionBased.ByDropItem or BookTriggerConditionBased.ByPickupItem or BookTriggerConditionBased.ByCombineItem
                && conditions.TriggerItemReference is { ItemId: >= 0 } wanted && item is not null)
                return wanted.Category == item.Category && wanted.ItemId == item.ItemId;
            if (trigger == BookTriggerConditionBased.ByCombinePart && conditions.CombinedPartId >= 0) return conditions.CombinedPartId == combinedPartId;
            return true;
        }).ToList();
        var gump = _openDolls.FirstOrDefault(g => ReferenceEquals(g.State, state));
        var design = gump?.Design ?? _objects.DollDesigns.FirstOrDefault(d => d.Id == state.DesignId);
        if (design is null) return false;
        var context = new DollContext { State = state, Design = design, Owner = HolderOf(state, gump?.Owner), Obj = gump?.Obj, Combined = combined };
        return WithDoll(context, () => TryFireEventPages((state, part.Id), pages, trigger, -1, what));
    }

    private bool HasPages(DollPart part, BookTriggerConditionBased trigger) =>
        part.EventPages.Any(p => p.Conditions.TriggerConditionBased == trigger && p.Commands.Count > 0);

    /// <summary>
    /// Whatever holds this paperdoll now - it travels with its object, which
    /// may have been picked up, put in a pack or a chest, or set down again
    /// since its window opened.
    /// </summary>
    private IHasDollState? HolderOf(DollState state, IHasDollState? lastKnown)
    {
        if (lastKnown is not null && ReferenceEquals(lastKnown.DollRuntime, state) && (lastKnown is not ObjectInstance placed || _map.Objects.Contains(placed))) return lastKnown;
        if (_map.Objects.FirstOrDefault(o => ReferenceEquals(o.DollRuntime, state)) is { } onMap) return onMap;
        IEnumerable<InventoryItemInstance> Within(IEnumerable<InventoryItemInstance> items) =>
            items.SelectMany(i => new[] { i }.Concat(Within(i.Contents)).Concat(i.DollRuntime is { } inner ? Within(inner.Parts.Values.SelectMany(p => p.Items)) : Enumerable.Empty<InventoryItemInstance>()));
        var carried = AllLivingCharacters(includeDead: true).SelectMany(c => c.Inventory)
            .Concat(_map.Items.SelectMany(i => i.Contents))
            .Concat(_map.Objects.Where(o => o.DollRuntime is not null).SelectMany(o => o.DollRuntime!.Parts.Values.SelectMany(p => p.Items)));
        return Within(carried).FirstOrDefault(i => ReferenceEquals(i.DollRuntime, state));
    }

    private void FireDollPartEvent(DollState state, int partId, BookTriggerConditionBased trigger, ItemReference? item)
    {
        if (_objects.DollDesigns.FirstOrDefault(d => d.Id == state.DesignId)?.Part(partId) is { } part)
            FireDollPartEvent(state, part, trigger, $"\"{part.Name}\" fired ({trigger})", item);
    }

    // ================= Clicks =================

    /// <summary>Parts that act on a single click, at once (Exult's gump buttons): buttons, switches, sliders, close buttons. True = handled.</summary>
    private bool DollPartClicked(DollPartTarget target)
    {
        if (!_openDolls.Contains(target.Gump) || target.Gump.Design.Part(target.PartId) is not { } part) return false;
        var gump = target.Gump;
        var runtime = gump.State.Of(part);
        PlayControlClick(part); // every click: System Settings > Sounds > Drop in a window
        if (DollControlClicked(gump, part, runtime)) return true; // form controls (GameTestWindow.DollControls.cs)
        if (SaveScreenPartClicked(gump, part, runtime)) return true; // the Save/Load screen's own parts
        switch (part.Kind)
        {
            case DollPartKind.Button:
            {
                runtime.Look = 1;
                RefreshDollGump(gump);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    runtime.Look = 0;
                    if (_openDolls.Contains(gump)) RefreshDollGump(gump);
                };
                timer.Start();
                if (!FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" pressed.")) StatusText.Text = $"You press \"{part.Name}\".";
                return true;
            }
            case DollPartKind.Switch:
                if (part.Looks.Count > 0) runtime.Look = (runtime.Look + 1) % part.Looks.Count;
                RefreshDollGump(gump);
                if (!FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" switched."))
                    StatusText.Text = $"\"{part.Name}\": {(runtime.Look < part.Looks.Count ? part.Looks[runtime.Look].Name : "")}";
                return true;
            case DollPartKind.Slider:
            {
                if (DollPartVisual(gump, part.Id) is not { } visual) return true;
                var size = DollLook.SizeOf(part, 0, _sprites);
                int value = DollLook.ValueAt(part, Mouse.GetPosition(visual), size);
                if (value == runtime.Value) return true;
                runtime.Value = value;
                WriteControlVariable(part, runtime);
                RefreshDollGump(gump);
                if (!FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" set to {value}.")) StatusText.Text = $"\"{part.Name}\": {value}";
                return true;
            }
            case DollPartKind.CloseButton:
                FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" fired.");
                if (_openDolls.Contains(gump)) CloseDollGump(gump);
                return true;
        }
        return false;
    }

    /// <summary>Single click on a part that isn't acted on at once: its name, as Exult identifies.</summary>
    private void IdentifyDollPart(DollPartTarget target)
    {
        if (!_openDolls.Contains(target.Gump) || target.Gump.Design.Part(target.PartId) is not { } part || string.IsNullOrWhiteSpace(part.Name)) return;
        ShowGumpLabel(target.Gump.PartsCanvas, part.Name, part.X, part.Y);
        StatusText.Text = part.Name;
    }

    /// <summary>Double click on a part: a lid opens or shuts; anything else runs its By Use event.</summary>
    private void UseDollPart(DollPartTarget target)
    {
        if (!_openDolls.Contains(target.Gump) || target.Gump.Design.Part(target.PartId) is not { } part) return;
        var gump = target.Gump;
        var runtime = gump.State.Of(part);
        if (SaveScreenPartUsed(gump, part)) return; // double-clicking a save loads it
        if (part.Kind == DollPartKind.Lid)
        {
            runtime.Look = runtime.Look == 0 ? 1 : 0;
            RefreshDollGump(gump);
            if (!FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" {(runtime.Look == 1 ? "opened" : "closed")}."))
                StatusText.Text = $"You {(runtime.Look == 1 ? "open" : "close")} \"{part.Name}\".";
            return;
        }
        if (!FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByUseItem, $"\"{part.Name}\" used."))
            StatusText.Text = $"\"{part.Name}\" - nothing happens.";
    }

    private string DollItemName(InventoryItemInstance item) => item.Category switch
    {
        ReferencedItemCategory.Object when DefinitionOf(item) is { } obj => CarriedObjectName(obj, item),
        ReferencedItemCategory.Book when _books.Books.FirstOrDefault(b => b.Id == item.ItemId) is { } book => EffectiveBookName(book),
        _ => DollLook.ItemName(new ItemReference { Category = item.Category, ItemId = item.ItemId }, _objects, _books, _containers)
    };

    private void IdentifyDollItem(DollItemTarget target)
    {
        if (!_openDolls.Contains(target.Gump) || target.Gump.Design.Part(target.PartId) is not { } part) return;
        string name = DollItemName(target.Item);
        ShowGumpLabel(target.Gump.PartsCanvas, name, part.X, part.Y);
        StatusText.Text = name;
    }

    /// <summary>Double click on an item inside: used as any carried object is (a key, a light, its own paperdoll...).</summary>
    private void UseDollItem(DollItemTarget target)
    {
        if (!_openDolls.Contains(target.Gump)) return;
        if (target.Item.Category == ReferencedItemCategory.Object) UseCarriedObject(target.Item, new DollItemOrigin(target.Gump.State, target.PartId, target.Item));
        else StatusText.Text = $"\"{DollItemName(target.Item)}\" - nothing happens.";
    }

    private bool DollItemStillThere(DollItemOrigin origin) =>
        origin.State.Parts.TryGetValue(origin.PartId, out var runtime) && runtime.Items.Contains(origin.Item);

    /// <summary>The window a paperdoll item is shown in, for a drag starting there.</summary>
    private bool CanTakeFromDoll(OpenDollGump gump)
    {
        if (gump.Owner is ObjectInstance placed && _map.Objects.Contains(placed) && !IsReachable(placed)) { Refuse(MousePointerKind.OutOfRange, "That's out of reach."); return false; }
        return true;
    }

    // ================= Dropping in, combining =================

    /// <summary>The front-most part under a point of the paperdoll that's shown and in reach (optionally only some kinds, never one part).</summary>
    private DollPart? DollPartAt(OpenDollGump gump, Point point, Func<DollPart, bool>? only = null, int excludingId = -1) =>
        gump.Design.Parts.AsEnumerable().Reverse().FirstOrDefault(p =>
            p.Id != excludingId && (only?.Invoke(p) ?? true) && gump.State.Of(p).Visible && !IsUnderClosedLid(gump, p)
            && new Rect(new Point(p.X, p.Y), DollLook.SizeOf(p, gump.State.Of(p).Look, _sprites)).Contains(point));

    private bool DropOnDoll(DragOrigin origin, PointerSession session, OpenDollGump gump, InteractionTarget? hitTarget, Point screen)
    {
        if (gump.Owner is ObjectInstance placed && _map.Objects.Contains(placed) && !IsReachable(placed)) { Refuse(MousePointerKind.OutOfRange, "That's out of reach."); return false; }
        var design = gump.Design;
        var point = PaperdollGumpsCanvas.TranslatePoint(screen, gump.PartsCanvas);
        var topLeft = PaperdollGumpsCanvas.TranslatePoint(new Point(screen.X - session.GrabOffset.X, screen.Y - session.GrabOffset.Y), gump.PartsCanvas);
        var hitPart = hitTarget switch
        {
            DollPartTarget t when ReferenceEquals(t.Gump, gump) => design.Part(t.PartId),
            DollItemTarget t when ReferenceEquals(t.Gump, gump) => design.Part(t.PartId),
            _ => null
        };
        string name = PayloadName(origin);
        var (category, itemId) = PayloadIdentity(origin);
        var identity = new ItemReference { Category = category, ItemId = itemId };

        // Combining first: a part programmed for "By Combined With An Item" (this item, or any) takes it - a key on a keyhole, a battery on the robot.
        var combineOn = hitPart ?? DollPartAt(gump, point, p => HasPages(p, BookTriggerConditionBased.ByCombineItem));
        bool fromThatPart = origin is DollItemOrigin own && ReferenceEquals(own.State, gump.State) && own.PartId == combineOn?.Id;
        if (combineOn is not null && !fromThatPart && HasPages(combineOn, BookTriggerConditionBased.ByCombineItem)
            && FireDollPartEvent(gump.State, combineOn, BookTriggerConditionBased.ByCombineItem, $"\"{combineOn.Name}\": \"{name}\" combined.", identity, combined: origin))
        {
            // The item stays where it came from unless the event used it up (Paperdoll > Use up the item just combined).
            RefreshAfterMove();
            return false;
        }

        var part = hitPart is { Kind: DollPartKind.Slot or DollPartKind.ItemArea } ? hitPart
            : DollPartAt(gump, point, p => p.Kind is DollPartKind.Slot or DollPartKind.ItemArea); // dropped near rather than exactly on it
        if (part is null) { Refuse(MousePointerKind.WontFit, $"\"{name}\" doesn't go there."); return false; }
        var runtime = gump.State.Of(part);

        if (origin is DollItemOrigin same && ReferenceEquals(same.State, gump.State) && same.PartId == part.Id)
        {
            if (part.Kind != DollPartKind.ItemArea) return false; // back into the slot it came from: still there
            same.Item.ContainerX = topLeft.X - part.X;
            same.Item.ContainerY = topLeft.Y - part.Y;
            runtime.Items.Remove(same.Item);
            runtime.Items.Add(same.Item); // on top of the pile, as in a container
            RefreshDollGump(gump);
            return true;
        }
        // "Watch for snake eating itself" (contain.cc): the object can't go into its own paperdoll.
        if (PayloadObject(origin) is IHasDollState payload && ReferenceEquals(payload.DollRuntime, gump.State))
        {
            Refuse(MousePointerKind.RedX, $"\"{name}\" can't go inside itself.");
            return false;
        }
        if (part.Kind == DollPartKind.Slot && DollLook.FitFor(part, category, itemId) is null)
        {
            Refuse(MousePointerKind.WontFit, $"\"{name}\" doesn't fit in \"{part.Name}\".");
            return false;
        }
        if (runtime.Items.Count >= Math.Max(1, part.Capacity))
        {
            Refuse(MousePointerKind.WontFit, $"\"{part.Name}\" is full.");
            return false;
        }
        var item = DetachAsInventoryItem(origin, () => gump.State.NextItemInstanceId++);
        if (part.Kind == DollPartKind.ItemArea)
        {
            item.ContainerX = Math.Clamp(topLeft.X - part.X, 0, Math.Max(0, part.Width - 8));
            item.ContainerY = Math.Clamp(topLeft.Y - part.Y, 0, Math.Max(0, part.Height - 8));
        }
        runtime.Items.Add(item);
        PlayNavigateSound(); // put into a paperdoll's slot
        StatusText.Text = $"You put \"{name}\" into \"{part.Name}\".";
        RefreshAfterMove();
        if (_openDolls.Contains(gump)) FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByDropItem, $"\"{part.Name}\": \"{name}\" put in.", identity);
        return true;
    }

    // ================= Dragging a part onto another =================

    /// <summary>A part being dragged onto another (PaperDoll Editor: "Can be dragged onto other parts"). It never leaves its place - it goes back when let go.</summary>
    private sealed record DollPartOrigin(OpenDollGump Gump, int PartId) : DragOrigin;

    private bool StartDollPartDrag(PointerSession session, DollPartTarget target, DollPart part)
    {
        var gump = target.Gump;
        var runtime = gump.State.Of(part);
        var sprite = DollLook.LookSprite(part, runtime.Look, _sprites) ?? DollLook.LookSprite(part, 0, _sprites);
        if (sprite is null) return false; // nothing to see being dragged
        CancelPendingIdentify();
        _lastClickTarget = null;
        var first = DollLook.Frame(sprite);
        var ghost = new AlphaHitImage { Width = first?.PixelWidth ?? 32, Height = first?.PixelHeight ?? 32, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(ghost, BitmapScalingMode.NearestNeighbor);
        session.GhostAnimators.Add(new SpriteAnimator(sprite, ghost, _resources));
        double scale = DollGumpScale;
        ghost.RenderTransform = new ScaleTransform(scale, scale);
        Panel.SetZIndex(ghost, 10000);
        session.NativeWidth = ghost.Width;
        session.NativeHeight = ghost.Height;
        var source = DollPartVisual(gump, part.Id);
        var grab = new Point(ghost.Width * scale / 2, ghost.Height * scale / 2);
        if (source is not null && source.IsVisible)
        {
            var sourceTopLeft = source.TranslatePoint(new Point(0, 0), PaperdollGumpsCanvas);
            grab = new Point(Math.Clamp(session.PressPoint.X - sourceTopLeft.X, 0, ghost.Width * scale), Math.Clamp(session.PressPoint.Y - sourceTopLeft.Y, 0, ghost.Height * scale));
        }
        session.GrabOffset = grab;
        PaperdollGumpsCanvas.Children.Add(ghost);
        session.Ghost = ghost;
        if (source is not null) { source.Visibility = Visibility.Hidden; session.HiddenSource = source; }
        session.Origin = new DollPartOrigin(gump, part.Id);
        CaptureMouse();
        return true;
    }

    /// <summary>A part let go over another part of the same paperdoll: that part's By Combined With A Part runs. Always false - the dragged part goes back to its place.</summary>
    private bool DropDollPart(DollPartOrigin origin, Point screen)
    {
        var gump = origin.Gump;
        if (!_openDolls.Contains(gump) || gump.Design.Part(origin.PartId) is not { } dragged) return false;
        var point = PaperdollGumpsCanvas.TranslatePoint(screen, gump.PartsCanvas);
        if (HitTestGumpLayer(screen, out var hitGump, out _) && !ReferenceEquals(hitGump, gump)) return false; // over another window
        var target = DollPartAt(gump, point, excludingId: dragged.Id);
        if (target is null) return false;
        if (!FireDollPartEvent(gump.State, target, BookTriggerConditionBased.ByCombinePart, $"\"{dragged.Name}\" combined with \"{target.Name}\".", combinedPartId: dragged.Id))
            StatusText.Text = $"\"{dragged.Name}\" and \"{target.Name}\" - nothing happens.";
        if (_openDolls.Contains(gump)) RefreshDollGump(gump);
        return false;
    }

    // ================= The Paperdoll command =================

    /// <summary>Paperdoll (event command): acts on the paperdoll of the event that's running (DollCommandWindow lists the actions).</summary>
    private bool PaperdollCommand(EventCommand command)
    {
        // Use up the item just combined - also for an object's own By Item Combined To It.
        if (command.Get("Action") == "UseUpCombined" && _dollContext is null && _combinedOrigin is { } combinedOnObject)
        {
            if (!CombinedStillThere(combinedOnObject)) return false;
            UseUpOne(combinedOnObject);
            return true;
        }
        var context = _dollContext ?? _lastDollContext;
        if (context is null) { StatusText.Text = "Paperdoll command: no paperdoll here (it runs from a paperdoll part's events, or an object's that opens one)."; return false; }
        var state = context.State;
        var design = context.Design;
        var part = int.TryParse(command.Get("PartId"), out int partId) ? design.Part(partId) : null;
        var runtime = part is null ? null : state.Of(part);
        string action = command.Get("Action");
        bool needsPart = action is not ("UseUpCombined" or "PlaySound" or "StopSounds" or "Close" or "TurnInto");
        if (needsPart && (part is null || runtime is null)) { StatusText.Text = $"Paperdoll command: \"{design.Name}\" has no part \"{command.Get("PartName")}\"."; return false; }
        int.TryParse(command.Get("Value"), out int value);
        switch (action)
        {
            case "SetLook":
                runtime!.Look = Math.Clamp(int.TryParse(command.Get("Look"), out int look) ? look : 0, 0, Math.Max(0, part!.Looks.Count - 1));
                break;
            case "NextLook":
                if (part!.Looks.Count > 0) runtime!.Look = (runtime.Look + 1) % part.Looks.Count;
                break;
            case "Show": runtime!.Visible = true; break;
            case "Hide": runtime!.Visible = false; break;
            case "SetValue": runtime!.Value = Math.Clamp(value, part!.Min, Math.Max(part.Min, part.Max)); break;
            case "AddValue": runtime!.Value = Math.Clamp(runtime.Value + value, part!.Min, Math.Max(part.Min, part.Max)); break;
            case "OpenLid": runtime!.Look = 1; break;
            case "CloseLid": runtime!.Look = 0; break;
            case "PutItem":
            {
                if (!int.TryParse(command.Get("ItemId"), out int itemId) || !Enum.TryParse<ReferencedItemCategory>(command.Get("ItemCategory"), out var category)) return false;
                if (runtime!.Items.Count >= Math.Max(1, part!.Capacity)) { StatusText.Text = $"\"{part.Name}\" is full."; return false; }
                runtime.Items.Add(new InventoryItemInstance { InstanceId = state.NextItemInstanceId++, Category = category, ItemId = itemId, Quantity = 1, ContainerX = UnplacedSpot, ContainerY = UnplacedSpot });
                break;
            }
            case "UseUpItems": runtime!.Items.Clear(); break;
            case "UseUpCombined":
            {
                if (context.Combined is not { } combined || !CombinedStillThere(combined)) return false;
                UseUpOne(combined); // one of a stack; the last one is gone
                break;
            }
            case "ToVariable":
            {
                string name = command.Get("VariableName");
                if (string.IsNullOrEmpty(name)) return false;
                double result = command.Get("State") switch
                {
                    "Look" => runtime!.Look + 1,
                    "Value" => runtime!.Value,
                    "Items" => runtime!.Items.Count,
                    _ => runtime!.Visible ? 1 : 0
                };
                _execContext.SetVariable(name, result);
                break;
            }
            case "PlaySound":
                // Owned by this copy's paperdoll, not its window: it plays on after the paperdoll closes, until a Stop.
                if (!int.TryParse(command.Get("SoundResourceId"), out int soundId)) return false;
                SoundPlayback.TryPlayFor(state, _resources, soundId, command.Get("Loop") == "True");
                break;
            case "StopSounds":
                SoundPlayback.StopFor(state);
                break;
            case "Close":
                foreach (var open in _openDolls.Where(g => ReferenceEquals(g.State, state)).ToList()) CloseDollGump(open);
                return true;
            case "TurnInto":
                return TurnDollOwnerInto(context, command);
            default:
                return false;
        }
        if (part is not null && runtime is not null) WriteControlVariable(part, runtime); // a control kept in a variable
        foreach (var open in _openDolls.Where(g => ReferenceEquals(g.State, state)).ToList()) RefreshDollGump(open);
        return true;
    }

    private bool CombinedStillThere(DragOrigin origin) => origin switch
    {
        MapItemOrigin m => _map.Items.Contains(m.Item),
        DollItemOrigin d => DollItemStillThere(d),
        _ => ToolStillThere(origin)
    };

    /// <summary>Turn the object into another object: this copy becomes it where it is (on the map or carried) - the machine with every part fitted becomes the finished machine. Its paperdoll closes; the new object starts with its own.</summary>
    private bool TurnDollOwnerInto(DollContext context, EventCommand command)
    {
        if (!int.TryParse(command.Get("ObjectId"), out int objectId) || _objects.Objects.FirstOrDefault(o => o.Id == objectId) is not { } becomes) return false;
        var holder = HolderOf(context.State, context.Owner);
        foreach (var open in _openDolls.Where(g => ReferenceEquals(g.State, context.State)).ToList()) CloseDollGump(open);
        SoundPlayback.StopFor(context.State);
        string was = context.Obj?.Name ?? "It";
        switch (holder)
        {
            case ObjectInstance placed:
                placed.ObjectId = becomes.Id;
                placed.DollRuntime = null;
                RefreshObjectLook(placed);
                break;
            case InventoryItemInstance carried:
                carried.ItemId = becomes.Id;
                carried.DollRuntime = null;
                RefreshAfterMove();
                break;
            default:
                return false;
        }
        StatusText.Text = $"\"{was}\" becomes \"{becomes.Name}\".";
        return true;
    }
}
