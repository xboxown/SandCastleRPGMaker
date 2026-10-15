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
        if (fireUseEvent()) return true;
        OpenDoll(owner, obj, design);
        return true;
    }

    private void OpenDoll(IHasDollState owner, WorldObject obj, DollDesign design)
    {
        // Made from the design the first time this copy is opened; kept from then on (a design swapped in the Object Editor starts fresh).
        if (owner.DollRuntime is not { } state || state.DesignId != design.Id) owner.DollRuntime = state = DollState.StartOf(design);
        var existing = _openDolls.FirstOrDefault(g => ReferenceEquals(g.State, state));
        if (existing is not null) { BringDollToFront(existing); return; }

        PlayOpenSound(design.OpenSoundResourceId, $"\"{obj.Name}\"");
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
        BringDollToFront(gump);
        RefreshDollGump(gump);
        StatusText.Text = $"You open \"{obj.Name}\".";
    }

    private void BringDollToFront(OpenDollGump gump)
    {
        Panel.SetZIndex(gump.GumpBorder, 200);
        TrackGumpFocus(gump);
    }

    private void CloseDollGump(OpenDollGump gump)
    {
        if (gump.Design.CloseSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, gump.Design.CloseSoundResourceId);
        foreach (var animator in gump.Animators) animator.Stop();
        gump.Animators.Clear();
        PaperdollGumpsCanvas.Children.Remove(gump.GumpBorder);
        _openDolls.Remove(gump);
        UntrackGumpFocus(gump);
        if (_openPaperdolls.Count == 0 && _openContainers.Count == 0 && _openDolls.Count == 0) GameCanvas.Focus();
    }

    // ================= Drawing =================

    private static bool IsClickable(DollPart part) =>
        part.Kind is DollPartKind.Button or DollPartKind.Switch or DollPartKind.Lid or DollPartKind.Slot or DollPartKind.ItemArea
            or DollPartKind.Slider or DollPartKind.Hotspot or DollPartKind.CloseButton
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
        canvas.LayoutTransform = new ScaleTransform(DollGumpScale, DollGumpScale);

        int z = 0;
        foreach (var part in design.Parts)
        {
            z += 10;
            var runtime = gump.State.Of(part);
            if (!runtime.Visible) continue;
            var lid = DollLook.ClosedLidOver(part, design, p => DollStateOf(gump, p));
            if (lid is { HidesWhatItCovers: true }) continue;
            bool reachable = lid is null;
            var visual = BuildDollPartVisual(gump, part, runtime);
            Canvas.SetLeft(visual, part.X);
            Canvas.SetTop(visual, part.Y);
            Panel.SetZIndex(visual, z);
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

    /// <summary>Runs a part's own event pages for a trigger - counted per copy of the object and per part.</summary>
    private bool FireDollPartEvent(DollState state, DollPart part, BookTriggerConditionBased trigger, string what)
    {
        if (part.EventPages.Count == 0) return false;
        return TryFireEventPages((state, part.Id), part.EventPages, trigger, -1, what);
    }

    private void FireDollPartEvent(DollState state, int partId, BookTriggerConditionBased trigger)
    {
        if (_objects.DollDesigns.FirstOrDefault(d => d.Id == state.DesignId)?.Part(partId) is { } part)
            FireDollPartEvent(state, part, trigger, $"\"{part.Name}\" fired ({trigger})");
    }

    // ================= Clicks =================

    /// <summary>Parts that act on a single click, at once (Exult's gump buttons): buttons, switches, sliders, close buttons. True = handled.</summary>
    private bool DollPartClicked(DollPartTarget target)
    {
        if (!_openDolls.Contains(target.Gump) || target.Gump.Design.Part(target.PartId) is not { } part) return false;
        var gump = target.Gump;
        var runtime = gump.State.Of(part);
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
        ReferencedItemCategory.Object when DefinitionOf(item) is { } obj => ObjectDisplayName(obj, item.IsSpentRuntime, item.Quantity),
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

    // ================= Dropping in =================

    private bool DropOnDoll(DragOrigin origin, PointerSession session, OpenDollGump gump, InteractionTarget? hitTarget, Point screen)
    {
        if (gump.Owner is ObjectInstance placed && _map.Objects.Contains(placed) && !IsReachable(placed)) { Refuse(MousePointerKind.OutOfRange, "That's out of reach."); return false; }
        var design = gump.Design;
        var point = PaperdollGumpsCanvas.TranslatePoint(screen, gump.PartsCanvas);
        var topLeft = PaperdollGumpsCanvas.TranslatePoint(new Point(screen.X - session.GrabOffset.X, screen.Y - session.GrabOffset.Y), gump.PartsCanvas);
        var part = hitTarget switch
        {
            DollPartTarget t when ReferenceEquals(t.Gump, gump) => design.Part(t.PartId),
            DollItemTarget t when ReferenceEquals(t.Gump, gump) => design.Part(t.PartId),
            _ => null
        };
        if (part is not { Kind: DollPartKind.Slot or DollPartKind.ItemArea })
        {
            // Dropped near rather than exactly on it: the front-most open slot or area under the point.
            part = design.Parts.AsEnumerable().Reverse().FirstOrDefault(p =>
                p.Kind is DollPartKind.Slot or DollPartKind.ItemArea && gump.State.Of(p).Visible && !IsUnderClosedLid(gump, p)
                && new Rect(p.X, p.Y, Math.Max(4, p.Width), Math.Max(4, p.Height)).Contains(point));
        }
        string name = PayloadName(origin);
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
        var (category, itemId) = PayloadIdentity(origin);
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
        PlayDropSound();
        StatusText.Text = $"You put \"{name}\" into \"{part.Name}\".";
        RefreshAfterMove();
        if (_openDolls.Contains(gump)) FireDollPartEvent(gump.State, part, BookTriggerConditionBased.ByDropItem, $"\"{part.Name}\": \"{name}\" put in.");
        return true;
    }
}
