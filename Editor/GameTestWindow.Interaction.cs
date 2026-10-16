using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Game Test's interaction subsystem: single click (identify), double
/// click (use), and drag-and-drop between the map, paperdolls, backpack
/// lists and open containers. Modeled directly on Exult's own source:
///
/// - exult.cc (SDL mouse handling): ONE mouse handler for the whole game.
///   A press only becomes a drag once the pointer moves more than 2 game
///   pixels (Dragging_info::start). A release without that motion is a
///   click; the click identifies only after the 500 ms double-click window
///   expires ("show_items_time = curtime + 500"), and a second click inside
///   that window uses the object instead, cancelling the pending identify.
/// - drag.cc: exactly ONE Dragging_info per gesture, holding the object and
///   where it came from. On release, open gumps are hit-tested before the
///   map (find_gump, then drop_on_map); a drop that fails puts the object
///   back where it came from (put_back).
/// - contain.cc (Container_game_object::add): a container never ends up
///   inside itself or its own contents ("watch for snake eating itself" -
///   always checked), a locked container refuses, and an item only fits if
///   used volume + its own volume stays within max volume.
/// - Paperdoll_gump::add: a container shown under the drop point takes the
///   item first; otherwise the empty spot under the drop point readies it;
///   otherwise the actor's own general add rules (Actor::add) run.
///
/// One deliberate difference from Exult: the dragged object is NOT removed
/// from its source when the drag starts. It stays exactly where it is (its
/// visual merely hidden) until a drop genuinely succeeds, so a failed,
/// cancelled, or abandoned drag has nothing at all to undo - that is what
/// Exult's put_back achieves, without the window where the object exists
/// nowhere.
///
/// Every mouse handler here is subscribed exactly once, in
/// InitializeInteraction; nothing subscribes or unsubscribes per drag. All
/// per-gesture state lives in the single _pointer session.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>exult.cc: "Last click within .5 secs?" - the double-click window, and the delay before a single click identifies.</summary>
    private const int DoubleClickMilliseconds = 500;
    /// <summary>drag.cc Dragging_info::start: "if (deltax &lt;= 2 &amp;&amp; deltay &lt;= 2) return false; // Wait for greater motion." - in game pixels, so it scales with the viewport.</summary>
    private const double DragSlopGamePixels = 2;

    // ================= What the mouse is on =================

    /// <summary>The one tagged description of whatever a press landed on. Gump visuals carry these in their Tag; map entities are found via HitTestStack. Records compare by value, so "the same target clicked twice" is a plain Equals.</summary>
    private abstract record InteractionTarget;
    /// <summary>Entity is a PlacedItemInstance or an ObjectInstance.</summary>
    private sealed record MapEntityTarget(object Entity) : InteractionTarget;
    private sealed record PlayerTarget : InteractionTarget;
    /// <summary>SlotKey is an EquipmentSlot (built-in) or an int (a custom slot's Id), as TryGetEquippedInstanceId understands it. Used for both empty and occupied slots.</summary>
    private sealed record SlotTarget(OpenPaperdollGump Gump, object SlotKey) : InteractionTarget;
    private sealed record BackpackRowTarget(OpenPaperdollGump Gump, InventoryItemInstance Item) : InteractionTarget;
    private sealed record ContainerItemTarget(OpenContainerGump Gump, InventoryItemInstance Item) : InteractionTarget;

    // ================= Where a dragged thing came from =================

    /// <summary>
    /// One real origin record per drag - what is being dragged AND where it
    /// lives, in one value, so the two can never disagree. Each case holds
    /// exactly the references needed to remove the item from that origin
    /// once a drop succeeds (and nothing needs restoring if it doesn't).
    /// SlotOrigin is Exult's Dragging_info.readied_index.
    /// </summary>
    private abstract record DragOrigin;
    private sealed record MapItemOrigin(PlacedItemInstance Item) : DragOrigin;
    private sealed record MapObjectOrigin(ObjectInstance Obj) : DragOrigin;
    private sealed record SlotOrigin(CharacterDefinition Character, object SlotKey, InventoryItemInstance Item) : DragOrigin;
    private sealed record BackpackOrigin(CharacterDefinition Character, InventoryItemInstance Item) : DragOrigin;
    private sealed record ContainerOrigin(IHasContainerContents Container, ContainerData Data, InventoryItemInstance Item) : DragOrigin;

    /// <summary>
    /// The single consolidated per-gesture state (Exult's Dragging_info):
    /// created on press, discarded on release/cancel. Origin is null while
    /// it's still only a press; it becomes a drag the moment Origin is set.
    /// </summary>
    private sealed class PointerSession
    {
        public required InteractionTarget Target;
        /// <summary>Press position in PaperdollGumpsCanvas coordinates - the unscaled, window-level overlay space every gump and the drag ghost live in.</summary>
        public required Point PressPoint;
        public DragOrigin? Origin;
        /// <summary>The source's own visual, hidden (never removed) while dragging and shown again when the drag ends, whatever the outcome.</summary>
        public UIElement? HiddenSource;
        /// <summary>Exult paints the dragged object last, on top of everything, gumps included - this is that, a purely visual, non-hit-testable copy on the gump layer. It never carries any game data.</summary>
        public FrameworkElement? Ghost;
        public readonly List<SpriteAnimator> GhostAnimators = new();
        /// <summary>Where inside the ghost (screen pixels) the pointer grabbed it - Exult's paintx/painty offset, so the item doesn't jump under the cursor.</summary>
        public Point GrabOffset;
        /// <summary>The ghost's own unscaled (world-pixel) size - the same size the item has on the map.</summary>
        public double NativeWidth, NativeHeight;
        public bool IsDragging => Origin is not null;
    }

    private PointerSession? _pointer;
    private InteractionTarget? _lastClickTarget;
    private long _lastClickTick;
    private InteractionTarget? _pendingIdentify;
    private DispatcherTimer? _identifyDelayTimer;

    /// <summary>Called once from the constructor. These are the ONLY mouse subscriptions the interaction system ever makes - double subscription is structurally impossible because nothing else ever subscribes.</summary>
    private void InitializeInteraction()
    {
        PreviewMouseLeftButtonDown += Interaction_PreviewMouseLeftButtonDown;
        PreviewMouseMove += Interaction_PreviewMouseMove;
        PreviewMouseLeftButtonUp += Interaction_PreviewMouseLeftButtonUp;
        LostMouseCapture += Interaction_LostMouseCapture;

        _identifyDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DoubleClickMilliseconds) };
        _identifyDelayTimer.Tick += (_, _) =>
        {
            _identifyDelayTimer.Stop();
            var target = _pendingIdentify;
            _pendingIdentify = null;
            if (target is not null) IdentifyTarget(target);
        };
        Closed += (_, _) => { _identifyDelayTimer.Stop(); CancelPointer(); };
    }

    // ================= The one mouse pipeline =================

    private void Interaction_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentMessage is not null)
        {
            MessageClicked(e); // a dialog box is up: a click goes on, or picks the reply under it
            e.Handled = true;
            return;
        }
        if (_isReadingBook)
        {
            // Exult reads a book modally: any click, anywhere, is "next page" - nothing else can be clicked.
            _bookNextPage?.Invoke();
            e.Handled = true;
            return;
        }
        if (_objectOverlayOpen)
        {
            // A map being looked at: any click puts it away (Exult's display_map Get_click). A spellbook: only its own buttons work.
            if (!_objectOverlayTakesClicks) { CloseObjectOverlay(); e.Handled = true; }
            return;
        }
        if (_gameOver) { e.Handled = true; return; } // the main character is dead: nothing can be clicked (only a dialog box, above)
        if (TryChooseTarget(e)) return; // a key or lockpick is waiting for its target (Get_click)
        if (_pointer is { IsDragging: true }) { e.Handled = true; return; }
        _pointer = null;

        var screen = e.GetPosition(PaperdollGumpsCanvas);
        var target = FindInteractionTarget(screen);
        // Nothing interactive here (bare tile, gump body/background, exit button, empty list area): left entirely to those elements' own handlers - gump window dragging, exit buttons and list selection keep working untouched.
        if (target is null) return;

        _pointer = new PointerSession { Target = target, PressPoint = screen };
        // The backpack ListBox still needs to see the press so its row gets selected (the select-then-click-a-slot equip workflow); everything else is fully claimed here so no gump-drag handler underneath also fires.
        if (target is not BackpackRowTarget) e.Handled = true;
    }

    private void Interaction_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_currentMessage is not null) MessageMouseMoved(e); // the reply under the mouse lights up
        if (_pointer is not { } session) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelPointer(); return; } // released somewhere we never heard about - abandon cleanly

        var screen = e.GetPosition(PaperdollGumpsCanvas);
        if (!session.IsDragging)
        {
            double slop = DragSlopGamePixels * ViewportScale.ScaleX;
            if (Math.Abs(screen.X - session.PressPoint.X) <= slop && Math.Abs(screen.Y - session.PressPoint.Y) <= slop) return; // Exult: wait for greater motion
            if (!TryStartDrag(session)) { _pointer = null; return; }
        }
        PositionGhost(session, screen);
        e.Handled = true;
    }

    private void Interaction_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pointer is not { } session) return;
        // Cleared BEFORE releasing capture, so the LostMouseCapture that release raises can never mistake this for an abandoned drag.
        _pointer = null;
        if (IsMouseCaptured) ReleaseMouseCapture();

        if (session.IsDragging)
        {
            CompleteDrop(session, e.GetPosition(PaperdollGumpsCanvas));
            e.Handled = true;
            return;
        }

        HandleClickRelease(session.Target);
        if (session.Target is not BackpackRowTarget) e.Handled = true;
    }

    /// <summary>Capture taken away mid-drag (Alt+Tab, another window, a modal dialog): the drag is abandoned and, because nothing was ever removed from its origin, simply vanishes - the item is still exactly where it started.</summary>
    private void Interaction_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        if (_pointer is { IsDragging: true }) CancelPointer();
    }

    /// <summary>Ends whatever gesture is in progress without doing anything - safe to call at any time, any number of times.</summary>
    private void CancelPointer()
    {
        var session = _pointer;
        _pointer = null;
        if (session is null) return;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (session.IsDragging) EndDragVisuals(session);
    }

    // ================= Hit testing =================

    /// <summary>Exult Dragging_info: open gumps are checked first (gumpman->find_gump); only if no gump is under the point does the map get a look.</summary>
    private InteractionTarget? FindInteractionTarget(Point screen)
    {
        if (HitTestGumpLayer(screen, out _, out var gumpTarget)) return gumpTarget;
        return FindMapTarget(screen);
    }

    /// <summary>
    /// Real visual hit test of the gump layer at a point - the actual
    /// topmost gump wins, whatever its kind. Returns true if any gump is
    /// under the point at all; gump is the OpenContainerGump,
    /// OpenPaperdollGump or OpenBackpackGump it belongs to, and target the
    /// innermost interactive thing (item, slot, list row) under the point,
    /// if any. Hidden (dragged-from) visuals and the ghost are never hit.
    /// </summary>
    private bool HitTestGumpLayer(Point screen, out object? gump, out InteractionTarget? target)
    {
        gump = null;
        target = null;
        if (PaperdollGumpsCanvas.InputHitTest(screen) is not DependencyObject hit) return false;

        ListBoxItem? row = null;
        DependencyObject? current = hit;
        while (current is not null && !ReferenceEquals(current, PaperdollGumpsCanvas))
        {
            if (target is null && current is FrameworkElement { Tag: InteractionTarget tagged }) target = tagged;
            if (row is null && current is ListBoxItem listBoxItem) row = listBoxItem;
            if (target is null && current is ListBox { Tag: OpenPaperdollGump owner } && row?.DataContext is PaperdollBackpackRow backpackRow)
                target = new BackpackRowTarget(owner, backpackRow.Instance);
            if (current is Border border && ReferenceEquals(VisualTreeHelper.GetParent(border), PaperdollGumpsCanvas))
            {
                gump = (object?)_openContainers.FirstOrDefault(g => g.GumpBorder == border)
                    ?? (object?)_openPaperdolls.FirstOrDefault(g => g.GumpBorder == border)
                    ?? (object?)_openPaperdolls.Select(g => g.BackpackGump).FirstOrDefault(b => b?.GumpBorder == border)
                    ?? _openDolls.FirstOrDefault(g => g.GumpBorder == border);
                break;
            }
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        // A press on a paperdoll's visible body art over an EMPTY built-in slot (its 40x40 area) is that slot - empty slots have no hit squares of their own, so nothing invisible is ever clickable.
        if (target is null && gump is OpenPaperdollGump paperdoll)
        {
            var inBody = PaperdollGumpsCanvas.TranslatePoint(screen, paperdoll.BodyCanvas);
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                if (TryGetEquippedInstanceId(paperdoll.Character, slot, out _)) continue;
                if (!paperdoll.Character.Paperdoll.SlotPositions.TryGetValue(slot, out var pos)) pos = new PaperdollSlotPosition { X = 140, Y = 10 };
                if (inBody.X >= pos.X - 4 && inBody.X < pos.X + 36 && inBody.Y >= pos.Y - 4 && inBody.Y < pos.Y + 36)
                {
                    target = new SlotTarget(paperdoll, slot);
                    break;
                }
            }
        }
        return true;
    }

    private InteractionTarget? FindMapTarget(Point screen)
    {
        // Only the visible 320x200 camera window is the map - the letterbox around it is not.
        var inViewport = PaperdollGumpsCanvas.TranslatePoint(screen, ViewportBorder);
        if (inViewport.X < 0 || inViewport.Y < 0 || inViewport.X >= ViewportWidth || inViewport.Y >= ViewportHeight) return null;

        var world = PaperdollGumpsCanvas.TranslatePoint(screen, GameCanvas);
        int cellX = (int)Math.Floor(world.X / _tileWidth);
        int cellY = (int)Math.Floor(world.Y / _tileHeight);
        if (cellX < 0 || cellY < 0 || cellX >= _map.Width || cellY >= _map.Height) return null;

        // A character standing on the map, if it's the topmost thing under the pointer.
        if (HitTestCharacter(world) is { } character) return new MapEntityTarget(character);

        var (hitItem, hitObject) = HitTestStack(world);
        if (hitItem is not null) return new MapEntityTarget(hitItem);
        if (hitObject is not null) return new MapEntityTarget(hitObject);
        // Identifying the player is only ever the fallback when nothing else was found there - a book sharing the player's tile is still clickable.
        if ((cellX == _playerX && cellY == _playerY) || IsPointOverPlayer(world)) return new PlayerTarget();
        return null;
    }

    private static FrameworkElement? FindTaggedVisual(Panel panel, InteractionTarget target) =>
        panel.Children.OfType<FrameworkElement>().FirstOrDefault(c => Equals(c.Tag, target));

    /// <summary>Called by the gump builders for every interactive visual they create: if a rebuild happens mid-drag (a window resize, an event command), the item being dragged stays hidden in its source rather than reappearing there.</summary>
    private void HideIfBeingDragged(FrameworkElement visual)
    {
        if (_pointer is not { IsDragging: true } session || !Equals(visual.Tag, session.Target)) return;
        visual.Visibility = Visibility.Hidden;
        session.HiddenSource = visual;
    }

    // ================= Click vs double click (exult.cc) =================

    private void HandleClickRelease(InteractionTarget target)
    {
        // A paperdoll's buttons, switches, sliders and close buttons act at once, as Exult's gump buttons do.
        if (target is DollPartTarget dollPart && DollPartClicked(dollPart)) { _lastClickTarget = null; CancelPendingIdentify(); return; }
        long now = Environment.TickCount64;
        if (_lastClickTarget is not null && Equals(_lastClickTarget, target) && now - _lastClickTick < DoubleClickMilliseconds)
        {
            // Exult: "Last click within .5 secs?" -> double_clicked(), and the pending identify is dropped.
            _lastClickTarget = null;
            CancelPendingIdentify();
            UseTarget(target);
            return;
        }
        _lastClickTarget = target;
        _lastClickTick = now;
        // Exult: "show_items_time = curtime + 500; show_items_clicked = true;" - identify waits out the double-click window.
        _pendingIdentify = target;
        _identifyDelayTimer?.Stop();
        _identifyDelayTimer?.Start();
    }

    private void CancelPendingIdentify()
    {
        _pendingIdentify = null;
        _identifyDelayTimer?.Stop();
    }

    /// <summary>Single click. Re-validated at fire time, since up to half a second has passed and the target may have moved or its gump closed.</summary>
    private void IdentifyTarget(InteractionTarget target)
    {
        if (_isReadingBook) return;
        switch (target)
        {
            case MapEntityTarget { Entity: PlacedItemInstance item } when _map.Items.Contains(item):
                HandleItemClick(item, isUse: false); // Exult identifies anything you can see - no reach check (show_items)
                break;
            case MapEntityTarget { Entity: ObjectInstance obj } when _map.Objects.Contains(obj):
                HandleObjectClick(obj, isUse: false);
                break;
            case PlayerTarget:
                ShowIdentifyLabel(_player?.Name ?? "Yourself", _playerX, _playerY, _playerImage);
                break;
            case MapEntityTarget { Entity: PlacedCharacter placedCharacter }:
                IdentifyCharacter(placedCharacter);
                break;
            case SlotTarget slot when _openPaperdolls.Contains(slot.Gump):
                // Occupied: identifies it. Empty: equips the item selected in the backpack list, if any.
                PaperdollSlotClicked(slot.Gump, slot.SlotKey, 1);
                break;
            case BackpackRowTarget:
                // Selecting the row already names it in the status bar (PaperdollBackpackList_SelectionChanged) - nothing more to show.
                break;
            case ContainerItemTarget ci when _openContainers.Contains(ci.Gump) && ci.Gump.PlacedItem.Contents.Contains(ci.Item):
                HandleContainerItemClick(ci.Gump, ci.Item, isUse: false);
                break;
            case DollPartTarget dollPart:
                IdentifyDollPart(dollPart);
                break;
            case DollItemTarget dollItem when DollItemStillThere(new DollItemOrigin(dollItem.Gump.State, dollItem.PartId, dollItem.Item)):
                IdentifyDollItem(dollItem);
                break;
        }
    }

    /// <summary>Double click.</summary>
    private void UseTarget(InteractionTarget target)
    {
        if (PlayerKnockedOut) { StatusText.Text = "You are unconscious."; return; } // can identify, but not use or talk
        switch (target)
        {
            case MapEntityTarget { Entity: PlacedItemInstance item } when _map.Items.Contains(item):
                if (!IsReachable(item)) { Refuse(MousePointerKind.Blocked, CantReachMessage); return; } // Game_window::double_clicked: !is_grabable -> flash "blocked"
                if (TryAttackInCombatMode(item)) return; // combat mode: attack it (unless it's an unlocked container or a body)
                HandleItemClick(item, isUse: true);
                break;
            case MapEntityTarget { Entity: ObjectInstance obj } when _map.Objects.Contains(obj):
                if (!IsReachable(obj)) { Refuse(MousePointerKind.Blocked, CantReachMessage); return; }
                if (TryAttackInCombatMode(obj)) return;
                HandleObjectClick(obj, isUse: true);
                break;
            case PlayerTarget:
                // Exult: double-clicking the avatar opens their inventory (paperdoll).
                OpenPaperdoll(_player);
                break;
            case MapEntityTarget { Entity: PlacedCharacter placedCharacter }:
                // Exult: in combat mode, double-clicking someone attacks them; otherwise it talks to them (or, dead, opens the body).
                if (_combatMode && _mapCharacters.FirstOrDefault(mc => mc.Placement == placedCharacter) is { } foe && !IsDead(foe.Character) && !foe.Character.IsInParty) SetPlayerTarget(foe.Character); // never a party member
                else TalkTo(placedCharacter);
                break;
            case SlotTarget slot when _openPaperdolls.Contains(slot.Gump):
                var character = slot.Gump.Character;
                var equipped = TryGetEquippedInstanceId(character, slot.SlotKey, out int equippedId)
                    ? character.Inventory.FirstOrDefault(i => i.InstanceId == equippedId)
                    : null;
                if (equipped is { Category: ReferencedItemCategory.Container }) OpenEquippedContainer(equipped);
                // Exult: double-clicking an EMPTY backpack slot opens the character's own general contents.
                else if (equipped is null && slot.SlotKey is EquipmentSlot.Backpack) OpenBackpackContents(slot.Gump);
                else PaperdollSlotClicked(slot.Gump, slot.SlotKey, 2);
                break;
            case BackpackRowTarget row when _openPaperdolls.Contains(row.Gump) && row.Gump.Character.Inventory.Contains(row.Item):
                HandleInventoryItemClick(row.Gump, row.Item, isUse: true);
                break;
            case ContainerItemTarget ci when _openContainers.Contains(ci.Gump) && ci.Gump.PlacedItem.Contents.Contains(ci.Item):
                HandleContainerItemClick(ci.Gump, ci.Item, isUse: true);
                break;
            case DollPartTarget dollPart:
                UseDollPart(dollPart);
                break;
            case DollItemTarget dollItem when DollItemStillThere(new DollItemOrigin(dollItem.Gump.State, dollItem.PartId, dollItem.Item)):
                UseDollItem(dollItem);
                break;
        }
    }

    // ================= Starting a drag =================

    /// <summary>Exult Dragging_info::start - the first real motion. Builds the origin record and the ghost, hides (never removes) the source visual, and takes mouse capture. False means this press can't be dragged at all.</summary>
    private bool TryStartDrag(PointerSession session)
    {
        if (PlayerKnockedOut) return false; // can't pick anything up while unconscious
        DragOrigin? origin = null;
        UIElement? source = null;
        switch (session.Target)
        {
            case MapEntityTarget { Entity: PlacedItemInstance item } when _map.Items.Contains(item):
                if (IsTooHeavyToLift(item)) { Refuse(MousePointerKind.TooHeavy, "That's too heavy to lift."); return false; } // a body marked Too Heavy
                if (!IsReachable(item)) { Refuse(MousePointerKind.RedX, CantReachMessage); return false; } // Dragging_info::start: !is_grabable -> red X
                origin = new MapItemOrigin(item);
                source = _itemVisuals.GetValueOrDefault(item);
                break;
            case MapEntityTarget { Entity: ObjectInstance obj } when _map.Objects.Contains(obj):
                if (_flying.Any(f => ReferenceEquals(f.Instance, obj))) { Refuse(MousePointerKind.TooHeavy, "It's flying - you can't catch it."); return false; } // an arrow or a thrown weapon in flight
                if (!IsMovable(obj)) { Refuse(MousePointerKind.TooHeavy, "That's too heavy to move."); return false; } // Dragging_info::start: "Don't want to move walls" - !is_dragable -> "too heavy"
                // Not Exult (which lets you): a chair or bed someone is sitting or lying on is too heavy to move.
                if (_seats.Values.Any(s => ReferenceEquals(s.Furniture, obj)) || _headingToSeat.Values.Any(h => ReferenceEquals(h.Furniture, obj)))
                { Refuse(MousePointerKind.TooHeavy, "Someone is on it - it's too heavy to move."); return false; }
                if (!IsReachable(obj)) { Refuse(MousePointerKind.RedX, CantReachMessage); return false; }
                origin = new MapObjectOrigin(obj);
                source = _objectVisuals.GetValueOrDefault(obj);
                break;
            case SlotTarget slot when _openPaperdolls.Contains(slot.Gump):
                var character = slot.Gump.Character;
                if (!TryGetEquippedInstanceId(character, slot.SlotKey, out int equippedId)) return false; // an empty slot has nothing to drag
                var equipped = character.Inventory.FirstOrDefault(i => i.InstanceId == equippedId);
                if (equipped is null) return false;
                origin = new SlotOrigin(character, slot.SlotKey, equipped);
                source = FindTaggedVisual(slot.Gump.BodyCanvas, slot);
                break;
            case BackpackRowTarget row when row.Gump.Character.Inventory.Contains(row.Item):
                origin = new BackpackOrigin(row.Gump.Character, row.Item);
                break;
            case ContainerItemTarget ci when ci.Gump.PlacedItem.Contents.Contains(ci.Item):
                // Dragging_info::start: the gump's outermost owner must be in reach - a chest across the room can't be emptied from here ("out of range").
                if (!IsContainerOwnerReachable(ci.Gump.PlacedItem)) { Refuse(MousePointerKind.OutOfRange, "That's out of reach."); return false; }
                origin = new ContainerOrigin(ci.Gump.PlacedItem, ci.Gump.Data, ci.Item);
                source = FindTaggedVisual(ci.Gump.ItemsCanvas, ci);
                break;
            case DollPartTarget dp when _openDolls.Contains(dp.Gump) && dp.Gump.Design.Part(dp.PartId) is { Draggable: true } draggedPart && !IsUnderClosedLid(dp.Gump, draggedPart):
                return StartDollPartDrag(session, dp, draggedPart); // a key part onto a keyhole: it goes back when let go
            case DollItemTarget di when _openDolls.Contains(di.Gump) && DollItemStillThere(new DollItemOrigin(di.Gump.State, di.PartId, di.Item)):
                if (!CanTakeFromDoll(di.Gump)) return false;
                origin = new DollItemOrigin(di.Gump.State, di.PartId, di.Item);
                source = FindTaggedVisual(di.Gump.PartsCanvas, di);
                break;
        }
        if (origin is null) return false; // the player, or something that vanished since the press

        CancelPendingIdentify();
        _lastClickTarget = null; // a drag is never the first half of a double click

        var (category, itemId) = PayloadIdentity(origin);
        var ghost = BuildItemIcon(category, itemId, session.GhostAnimators, PayloadObject(origin));
        double scale = ViewportScale.ScaleX;
        session.NativeWidth = ghost.Width;
        session.NativeHeight = ghost.Height;
        ghost.IsHitTestVisible = false;
        // The gump layer sits outside the viewport's own scale, so the ghost carries that scale itself - it shows at exactly the size the item has on the map.
        ghost.RenderTransform = new ScaleTransform(scale, scale);
        RenderOptions.SetBitmapScalingMode(ghost, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(ghost, 10000);

        double ghostScreenWidth = ghost.Width * scale, ghostScreenHeight = ghost.Height * scale;
        var grab = new Point(ghostScreenWidth / 2, ghostScreenHeight / 2);
        if (source is not null && source.IsVisible)
        {
            var sourceTopLeft = source.TranslatePoint(new Point(0, 0), PaperdollGumpsCanvas);
            grab = new Point(Math.Clamp(session.PressPoint.X - sourceTopLeft.X, 0, ghostScreenWidth),
                             Math.Clamp(session.PressPoint.Y - sourceTopLeft.Y, 0, ghostScreenHeight));
        }
        session.GrabOffset = grab;

        PaperdollGumpsCanvas.Children.Add(ghost);
        session.Ghost = ghost;
        if (source is not null)
        {
            source.Visibility = Visibility.Hidden;
            session.HiddenSource = source;
        }
        session.Origin = origin;
        CaptureMouse();

        FireDragTrigger(category, itemId, BookTriggerConditionBased.ByPickupItem);
        return true;
    }

    private static void PositionGhost(PointerSession session, Point screen)
    {
        if (session.Ghost is null) return;
        Canvas.SetLeft(session.Ghost, screen.X - session.GrabOffset.X);
        Canvas.SetTop(session.Ghost, screen.Y - session.GrabOffset.Y);
    }

    /// <summary>Removes the ghost and shows the source visual again - run at the end of every drag, success or failure. On success the source visual is either the same map visual (now at its new spot) or a stale gump visual that's already been rebuilt away, so showing it is always correct.</summary>
    private void EndDragVisuals(PointerSession session)
    {
        foreach (var animator in session.GhostAnimators) animator.Stop();
        session.GhostAnimators.Clear();
        if (session.Ghost is not null) PaperdollGumpsCanvas.Children.Remove(session.Ghost);
        session.Ghost = null;
        if (session.HiddenSource is not null) session.HiddenSource.Visibility = Visibility.Visible;
        session.HiddenSource = null;
    }

    // ================= Dropping =================

    private void CompleteDrop(PointerSession session, Point screen)
    {
        bool dropped = false;
        try { dropped = TryDrop(session.Origin!, session, screen); }
        finally { EndDragVisuals(session); }
        if (dropped) CheckTheft(session.Origin!); // drag.cc drop: "Check for theft"
        if (dropped && session.Origin is DollItemOrigin fromDoll) FireDollPartEvent(fromDoll.State, fromDoll.PartId, BookTriggerConditionBased.ByPickupItem, new ItemReference { Category = fromDoll.Item.Category, ItemId = fromDoll.Item.ItemId }); // taken out of a paperdoll's slot
    }

    /// <summary>Exult Dragging_info::drop: a gump under the pointer gets the drop (drop_on_gump); otherwise the map does (drop_on_map). False = rejected; nothing has changed anywhere, so the item is simply still at its origin.</summary>
    private bool TryDrop(DragOrigin origin, PointerSession session, Point screen)
    {
        if (origin is DollPartOrigin partOrigin) return DropDollPart(partOrigin, screen); // a paperdoll part dragged onto another
        if (TryCombineOntoObject(origin, screen)) return false; // dropped onto an object whose By Item Combined To It runs: the item stays where it was unless it used it up
        if (HitTestGumpLayer(screen, out var gump, out var hitTarget))
        {
            return gump switch
            {
                OpenContainerGump containerGump => DropOnContainerGump(origin, session, containerGump, hitTarget, screen),
                OpenDollGump dollGump => DropOnDoll(origin, session, dollGump, hitTarget, screen),
                OpenPaperdollGump paperdollGump => DropOnPaperdoll(origin, paperdollGump, hitTarget, screen),
                // The backpack list is the character's own general inventory - dropping a backpack item back onto it is a no-op.
                OpenBackpackGump backpackGump => origin is BackpackOrigin b && b.Character == backpackGump.Owner.Character
                    ? false
                    : DropOnCharacter(origin, backpackGump.Owner.Character),
                _ => false
            };
        }
        return DropOnMap(origin, session, screen);
    }

    private bool DropOnContainerGump(DragOrigin origin, PointerSession session, OpenContainerGump gump, InteractionTarget? hitTarget, Point screen)
    {
        // drag.cc drop_on_gump: "Check the range" - the gump's outermost owner must be within reach.
        if (!IsContainerOwnerReachable(gump.PlacedItem))
        {
            Refuse(MousePointerKind.OutOfRange, "That's out of reach.");
            return false;
        }
        // Where the ghost was showing, in this gump's own frame coordinates - Exult adds at the painted position (paintx/painty), not the raw mouse point.
        var dropTopLeft = PaperdollGumpsCanvas.TranslatePoint(new Point(screen.X - session.GrabOffset.X, screen.Y - session.GrabOffset.Y), gump.ItemsCanvas);

        // A container icon under the drop point takes it first (Gump::add -> find_object -> cont->add), with no need to open that one.
        if (hitTarget is ContainerItemTarget { Item: { Category: ReferencedItemCategory.Container } nested }
            && !ReferenceEquals(nested, PayloadObject(origin))
            && _containers.Containers.FirstOrDefault(c => c.Id == nested.ItemId) is { } nestedData)
        {
            return TryPutIntoContainer(origin, nestedData, nested, dropTopLeft: null); // dropped ON the container, not into its window: no spot yet (Exult 255,255)
        }

        if (origin is ContainerOrigin sameContainer && ReferenceEquals(sameContainer.Container, gump.PlacedItem))
        {
            // A body: Exult takes the item out and offers it back, which the body refuses - so it stays where it was.
            if (gump.Data.IsBodyContainer) { Refuse(MousePointerKind.WontFit, "Things can be taken out of a body, but not put back in."); return false; }
            // Moving within the same gump: Exult removes the item and adds it back at the new spot, so it ends up on top of the pile.
            PlaceWithinFrame(sameContainer.Item, gump.Data, dropTopLeft);
            gump.PlacedItem.Contents.Remove(sameContainer.Item);
            gump.PlacedItem.Contents.Add(sameContainer.Item);
            PlayNavigateSound(gump.Data.NavigateSoundResourceId);
            RefreshContainerGump(gump);
            return true;
        }

        return TryPutIntoContainer(origin, gump.Data, gump.PlacedItem, dropTopLeft);
    }

    /// <summary>Exult Paperdoll_gump::add, in order: a container under the drop point takes it; else the closest free slot to the drop point readies it (find_closest); else the character's general add rules (DropOnCharacter). A failure at one step falls through to the next, as in Exult.</summary>
    private bool DropOnPaperdoll(DragOrigin origin, OpenPaperdollGump gump, InteractionTarget? hitTarget, Point screen)
    {
        var character = gump.Character;
        if (hitTarget is SlotTarget { SlotKey: var hitSlotKey }
            && TryGetEquippedInstanceId(character, hitSlotKey, out int occupantId)
            && character.Inventory.FirstOrDefault(i => i.InstanceId == occupantId) is { Category: ReferencedItemCategory.Container } occupant
            && !ReferenceEquals(occupant, PayloadObject(origin))
            && _containers.Containers.FirstOrDefault(c => c.Id == occupant.ItemId) is { } occupantData
            && TryPutIntoContainer(origin, occupantData, occupant, dropTopLeft: null))
            return true;

        if (FindClosestFreeSlot(gump, PaperdollGumpsCanvas.TranslatePoint(screen, gump.BodyCanvas), origin) is { } slotKey)
        {
            // Dropped back on the slot it came from - nothing to do; it's still there.
            if (origin is SlotOrigin from && from.Character == character && Equals(from.SlotKey, slotKey)) return false;
            if (TryEquipInSlot(origin, character, slotKey)) return true;
        }
        return DropOnCharacter(origin, character);
    }

    /// <summary>Within this distance (paperdoll authoring units) of a slot's center, a drop counts as aimed at that slot. Exult's find_closest has no limit because its gump is all slots; here the body is also a general "give it to this character" target, so a drop well away from every slot uses the character's general add rules instead.</summary>
    private const double SlotDropRadius = 32;

    /// <summary>Exult Paperdoll_gump::find_closest(mx, my, only_empty): the free slot nearest the drop point. A slot whose item is the one being dragged counts as free - its icon is hidden, and dropping it back there must work.</summary>
    private static object? FindClosestFreeSlot(OpenPaperdollGump gump, Point pointInBody, DragOrigin origin)
    {
        var character = gump.Character;
        object? closest = null;
        double closestDistanceSquared = SlotDropRadius * SlotDropRadius;
        void Consider(object slotKey, double centerX, double centerY)
        {
            if (!IsSlotFree(character, slotKey, origin)) return;
            double dx = pointInBody.X - centerX, dy = pointInBody.Y - centerY;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared < closestDistanceSquared) { closestDistanceSquared = distanceSquared; closest = slotKey; }
        }
        // Centers match the rendered hit regions: a built-in slot's 40x40 region at (X-4, Y-4), a custom slot's 60x22 region at (X, Y).
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (!character.Paperdoll.SlotPositions.TryGetValue(slot, out var pos)) pos = new PaperdollSlotPosition { X = 140, Y = 10 };
            Consider(slot, pos.X + 16, pos.Y + 16);
        }
        foreach (var customSlot in character.Paperdoll.CustomSlots) Consider(customSlot.Id, customSlot.X + 30, customSlot.Y + 11);
        return closest;
    }

    /// <summary>System Settings > Sounds > Drop on map: one sound for everything put down on the map.</summary>
    private void PlayDropSound()
    {
        int id = _systemSettings.Sounds.DropOnMapSoundId;
        if (id >= 0) SoundPlayback.TryPlay(_resources, id);
    }

    private bool DropOnMap(DragOrigin origin, PointerSession session, Point screen)
    {
        // Exult drop_on_map: "Attempting to drop off screen?" -> refused.
        var inViewport = PaperdollGumpsCanvas.TranslatePoint(screen, ViewportBorder);
        if (inViewport.X < 0 || inViewport.Y < 0 || inViewport.X >= ViewportWidth || inViewport.Y >= ViewportHeight)
        {
            Refuse(MousePointerKind.RedX, "You can't drop that there."); // drop_on_map: off screen -> red X
            return false;
        }

        // Exult drop_on_map: whatever is under the pointer gets first say (found->drop) - dropping onto the avatar hands it to them, gump or no gump.
        var mouseInWorld = PaperdollGumpsCanvas.TranslatePoint(screen, GameCanvas);
        if (_player is not null && IsPointOverPlayer(mouseInWorld)) return DropOnCharacter(origin, _player);

        // The ghost's top-left in world pixels - exactly where it was showing, which is where it lands.
        var ghostTopLeft = PaperdollGumpsCanvas.TranslatePoint(new Point(screen.X - session.GrabOffset.X, screen.Y - session.GrabOffset.Y), GameCanvas);
        double visualLeft = Math.Clamp(ghostTopLeft.X, 0, Math.Max(0, GameCanvas.Width - _tileWidth));
        double visualTop = Math.Clamp(ghostTopLeft.Y, 0, Math.Max(0, GameCanvas.Height - _tileHeight));
        double visualWidth = session.NativeWidth, visualHeight = session.NativeHeight;
        var (newCellX, newCellY) = ComputeDropCell(visualLeft, visualTop, visualWidth, visualHeight);

        // Exult Game_object::drop: the same kind of stackable thing dropped on a stack joins it (up to 100).
        if (PayloadIdentity(origin) is (ReferencedItemCategory.Object, var stackId) && CanCombine(_objects.Objects.FirstOrDefault(o => o.Id == stackId)))
        {
            int adding = PayloadQuantity(origin);
            var stack = _map.Objects.LastOrDefault(o => !ReferenceEquals(o, PayloadObject(origin)) && o.ContainerInstanceId < 0 && o.ObjectId == stackId
                && o.X == newCellX && o.Y == newCellY && o.Quantity + adding <= ObjectTypes.MaxQuantity);
            if (stack is not null)
            {
                RemoveFromSource(origin);
                stack.Quantity += adding;
                RefreshObjectLook(stack);
                StatusText.Text = $"Added to the stack - {stack.Quantity} now.";
                PlayDropSound();
                RefreshAfterMove();
                return true;
            }
        }

        // A CLOSED container on the target cell takes the item directly - but only when it doesn't allow elevation; one that does is stacked on like any other stackable thing, and its contents are reached by opening it.
        var payloadObject = PayloadObject(origin);
        var targetCellContainer = _map.Items.FirstOrDefault(i => !ReferenceEquals(i, payloadObject) && i.Category == ReferencedItemCategory.Container && i.X == newCellX && i.Y == newCellY);
        if (targetCellContainer is not null
            && _containers.Containers.FirstOrDefault(c => c.Id == targetCellContainer.ItemId) is { AllowElevation: false } targetContainerData)
        {
            return TryPutIntoContainer(origin, targetContainerData, targetCellContainer, dropTopLeft: null);
        }

        // The entity that will stand on the map: the dragged map entity itself, or a not-yet-placed candidate built from the inventory/container item.
        PlacedItemInstance? item = (origin as MapItemOrigin)?.Item;
        ObjectInstance? obj = (origin as MapObjectOrigin)?.Obj;
        if (item is null && obj is null)
        {
            var candidate = CreateMapEntityFor((InventoryItemInstance)payloadObject);
            item = candidate as PlacedItemInstance;
            obj = candidate as ObjectInstance;
        }

        // drag.cc drop_on_map: "for (int lift = max_lift; lift >= 0; lift--)" - the release point is projected at each lift in turn (a thing at lift z is
        // drawn z*4px up-left), highest first, and the first lift where the thing really comes to rest there wins. So something let go of over a table
        // top lands under the pointer on the top, not one table-height down-right on the floor behind it. Nothing fits at any lift: the ground (lift 0) rules.
        int newOffsetX = 0, newOffsetY = 0, newZ = 0;
        bool placed = false;
        for (int lift = _playerZ + MaxDropAboveActor; lift > 0 && !placed; lift--)
        {
            double liftedLeft = Math.Clamp(visualLeft + lift * LiftPixelShift, 0, Math.Max(0, GameCanvas.Width - _tileWidth));
            double liftedTop = Math.Clamp(visualTop + lift * LiftPixelShift, 0, Math.Max(0, GameCanvas.Height - _tileHeight));
            var (liftCellX, liftCellY) = ComputeDropCell(liftedLeft, liftedTop, visualWidth, visualHeight);
            if (TryResolveMapPlacement(item, obj, liftCellX, liftCellY, liftedLeft, liftedTop, visualWidth, visualHeight, out int ox, out int oy, out int z, out _, out _) && z == lift)
                (newCellX, newCellY, newOffsetX, newOffsetY, newZ, placed) = (liftCellX, liftCellY, ox, oy, z, true);
        }
        if (!placed && !TryResolveMapPlacement(item, obj, newCellX, newCellY, visualLeft, visualTop, visualWidth, visualHeight, out newOffsetX, out newOffsetY, out newZ, out string rejection, out MousePointerKind rejectionPointer))
        {
            Refuse(rejectionPointer, rejection);
            return false;
        }

        if (origin is not (MapItemOrigin or MapObjectOrigin))
        {
            RemoveFromSource(origin);
            AddMapEntity(item, obj);
            if (item is not null) RepointContainerGumps(payloadObject, item);
        }
        SetMapPosition(item, obj, newCellX, newCellY, newOffsetX, newOffsetY);
        SetZ((object?)item ?? obj!, newZ);
        ForgetHomeFloor((object?)item ?? obj!); // put down on the floor the player is on
        if (item is not null) (item.LiesUnder, item.HangsOnWall) = (false, false); // put down by hand: no longer tucked underneath anything, nor hung on the wall
        if (obj is not null) (obj.LiesUnder, obj.HangsOnWall) = (false, false);
        if (obj is not null) NoteGlassFall(obj, _playerZ, newZ); // glass let go of above a lower floor falls - and maybe shatters
        // Gravity everywhere: whatever rested on it where it came from falls, and so does the player if it was under them.
        SettleWorld();

        StatusText.Text = $"Moved to ({newCellX}, {newCellY}), sub-position ({newOffsetX}, {newOffsetY}).";
        PlayDropSound();
        TilesUnderDropped(item, obj); // a tile's By Item Dropped On It
        var (category, itemId) = PayloadIdentity(origin);
        FireDragTrigger(category, itemId, BookTriggerConditionBased.ByDropItem);
        // Only something that genuinely left a character's own inventory is a "no longer having it" transition - a map-to-map move never was "had".
        if (origin is SlotOrigin or BackpackOrigin) FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByNotHavingAnItem);
        RefreshAfterMove();
        return true;
    }

    /// <summary>
    /// The character's own general add rules - Exult Actor::add, adapted to
    /// this project's own established pickup order (unchanged from before):
    /// 0) a container goes to its own preferred slot if that slot is free;
    /// 1) the container in the Backpack slot, then the Pouch slot, if it has room;
    /// 2) the right hand, then the left hand (a two-handed item needs both).
    /// Used for drops on the avatar, on a paperdoll body, and on a backpack list.
    /// </summary>
    private bool DropOnCharacter(DragOrigin origin, CharacterDefinition character)
    {
        if (!CheckWeight(origin, character)) return false; // drag.cc Check_weight
        var (category, itemId) = PayloadIdentity(origin);
        var payloadObject = PayloadObject(origin);
        bool isTwoHanded = PayloadIsTwoHanded(origin);

        // Ammo goes to its own slot (Object Editor > Ammo), joining a stack of the same ammo already there.
        if (category == ReferencedItemCategory.Object && _objects.Objects.FirstOrDefault(o => o.Id == itemId) is { Type: ObjectType.Ammo } ammo
            && AmmoSlotFor(ammo, character) is { } ammoSlot)
        {
            if (TryGetEquippedInstanceId(character, ammoSlot, out int heldId) && character.Inventory.FirstOrDefault(i => i.InstanceId == heldId) is { } held
                && held.Category == category && held.ItemId == itemId && held.Quantity + PayloadQuantity(origin) <= ObjectTypes.MaxQuantity)
            {
                held.Quantity += PayloadQuantity(origin);
                RemoveFromSource(origin);
                FinishCharacterReceive(character, PayloadName(origin));
                return true;
            }
            if (IsSlotFree(character, ammoSlot, origin)) { EquipInSlot(origin, character, ammoSlot); return true; }
        }

        if (category == ReferencedItemCategory.Container)
        {
            // A container only ever goes in its own Location slot (Container Editor), or a hand: Location first, then the right hand, then the left hand - otherwise it can't be picked up. It never goes into another slot, and never into a bag.
            var preferredSlotKey = ContainerLocationSlot(itemId, character);
            if (preferredSlotKey is not null && IsSlotFree(character, preferredSlotKey, origin))
            {
                EquipInSlot(origin, character, preferredSlotKey);
                return true;
            }
            if (IsSlotFree(character, EquipmentSlot.RightHand, origin))
            {
                EquipInSlot(origin, character, EquipmentSlot.RightHand);
                return true;
            }
            if (IsSlotFree(character, EquipmentSlot.LeftHand, origin) && !IsLeftHandLockedIgnoring(character, origin))
            {
                EquipInSlot(origin, character, EquipmentSlot.LeftHand);
                return true;
            }
            Refuse(MousePointerKind.WontFit, "Unable to pick it up - its own slot and both hands are full.");
            return false;
        }

        foreach (var containerSlot in new[] { EquipmentSlot.Backpack, EquipmentSlot.Pouch })
        {
            if (!character.EquippedSlots.TryGetValue(containerSlot, out int slotInstanceId)) continue;
            var slotItem = character.Inventory.FirstOrDefault(i => i.InstanceId == slotInstanceId);
            if (slotItem is not { Category: ReferencedItemCategory.Container } || ReferenceEquals(slotItem, payloadObject)) continue;
            var slotContainerData = _containers.Containers.FirstOrDefault(c => c.Id == slotItem.ItemId);
            if (slotContainerData is null || !CanContainerAccept(slotContainerData, slotItem, origin, out _, out _)) continue;

            string storedName = PayloadName(origin);
            if (TryCombineIntoContents(slotItem.Contents, category, itemId, PayloadQuantity(origin)))
            {
                RemoveFromSource(origin);
                FinishCharacterReceive(character, storedName);
                return true;
            }
            var stored = DetachAsInventoryItem(origin, slotContainerData.GetNextContentsInstanceId);
            PlaceWithinFrame(stored, slotContainerData, topLeft: null);
            slotItem.Contents.Add(stored);
            FinishCharacterReceive(character, storedName);
            return true;
        }

        bool leftHandLocked = IsLeftHandLockedIgnoring(character, origin);
        EquipmentSlot? hand =
            IsSlotFree(character, EquipmentSlot.RightHand, origin) && (!isTwoHanded || IsSlotFree(character, EquipmentSlot.LeftHand, origin)) ? EquipmentSlot.RightHand :
            !isTwoHanded && !leftHandLocked && IsSlotFree(character, EquipmentSlot.LeftHand, origin) ? EquipmentSlot.LeftHand :
            null;
        if (hand is null)
        {
            Refuse(MousePointerKind.WontFit, "Unable to pick up - it doesn't fit in the Backpack or Pouch container, and both hands are full.");
            return false;
        }
        EquipInSlot(origin, character, hand.Value);
        return true;
    }

    /// <summary>Paperdoll_gump::add step 2 (add_readied at the spot under the drop point), with this project's own hand rules: the left hand is unavailable while the right holds a two-handed item, and a two-handed item goes in the right hand with the left hand empty.</summary>
    private bool TryEquipInSlot(DragOrigin origin, CharacterDefinition character, object slotKey)
    {
        if (!CheckWeight(origin, character)) return false; // drag.cc Check_weight
        var (category, itemId) = PayloadIdentity(origin);
        if (category == ReferencedItemCategory.Container && !IsContainerSlotAllowed(itemId, character, slotKey))
        {
            StatusText.Text = "A container only fits in its own slot or a hand.";
            return false;
        }
        // Exult's ready types: a weapon or a piece of armor goes in its own slot - or a hand, which holds anything.
        if (category == ReferencedItemCategory.Object && _objects.Objects.FirstOrDefault(o => o.Id == itemId) is { Type: ObjectType.Weapon or ObjectType.Armor } gear
            && slotKey is not (EquipmentSlot.RightHand or EquipmentSlot.LeftHand) && !Equals(OwnSlotFor(gear, character), slotKey))
        {
            StatusText.Text = $"\"{gear.Name}\" isn't worn there.";
            return false;
        }
        bool isTwoHanded = PayloadIsTwoHanded(origin);
        if (slotKey is EquipmentSlot.LeftHand && IsLeftHandLockedIgnoring(character, origin))
        {
            StatusText.Text = "Left hand is unavailable - the right hand is holding a two-handed weapon.";
            return false;
        }
        if (slotKey is EquipmentSlot.LeftHand && isTwoHanded)
        {
            StatusText.Text = "This needs both hands - put it in the right hand.";
            return false;
        }
        if (slotKey is EquipmentSlot.RightHand && isTwoHanded && !IsSlotFree(character, EquipmentSlot.LeftHand, origin))
        {
            StatusText.Text = "This needs both hands - unequip the left hand first.";
            return false;
        }
        EquipInSlot(origin, character, slotKey);
        return true;
    }

    private void EquipInSlot(DragOrigin origin, CharacterDefinition character, object slotKey)
    {
        string name = PayloadName(origin);
        var item = DetachAsInventoryItem(origin, character.GetNextInventoryInstanceId);
        character.Inventory.Add(item);
        SetEquippedInstanceId(character, slotKey, item.InstanceId);
        FinishCharacterReceive(character, name);
    }

    private void FinishCharacterReceive(CharacterDefinition character, string name)
    {
        // The one place an item lands on a character - fresh pickup or a slot-to-slot move alike - so the paperdoll's own Navigate Sound plays here.
        PlayNavigateSound(character.Paperdoll.NavigateSoundResourceId);
        StatusText.Text = $"Picked up \"{name}\".";
        RefreshAfterMove();
        // Inventory possession just changed, so every map event gets its chance to react.
        FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByHavingAnItem);
    }

    private bool TryPutIntoContainer(DragOrigin origin, ContainerData data, IHasContainerContents container, Point? dropTopLeft)
    {
        if (!CheckWeight(origin, CarrierOf(container))) return false; // drag.cc Check_weight: whoever carries the container (at any depth)
        EnsureContainerSeeded(container, data);
        if (!CanContainerAccept(data, container, origin, out string reason, out var pointer))
        {
            Refuse(pointer, reason);
            return false;
        }
        string name = PayloadName(origin);
        // Exult drop: onto a stack of the same kind inside, it joins it.
        var (combineCategory, combineId) = PayloadIdentity(origin);
        if (TryCombineIntoContents(container.Contents, combineCategory, combineId, PayloadQuantity(origin)))
        {
            RemoveFromSource(origin);
            PlayNavigateSound(data.NavigateSoundResourceId);
            StatusText.Text = $"You put \"{name}\" into \"{data.Name}\".";
            RefreshAfterMove();
            return true;
        }
        var item = DetachAsInventoryItem(origin, data.GetNextContentsInstanceId);
        PutOutIfLit(item); // a torch put away goes out
        PlaceWithinFrame(item, data, dropTopLeft);
        container.Contents.Add(item); // appended = on top of the pile, as Exult's add() appends to the chain
        PlayNavigateSound(data.NavigateSoundResourceId);
        StatusText.Text = $"You put \"{name}\" into \"{data.Name}\".";
        RefreshAfterMove();
        return true;
    }

    // ================= Container rules (contain.cc) =================

    /// <summary>
    /// Every rule for "may this dragged thing go into this container", in
    /// one place, used by every path that puts something into a container
    /// (open gump, nested icon, closed chest on the map, equipped container,
    /// the pickup order's Backpack/Pouch step).
    /// </summary>
    private bool CanContainerAccept(ContainerData data, IHasContainerContents container, DragOrigin origin, out string reason, out MousePointerKind pointer)
    {
        reason = "";
        // drag.cc drop_on_gump: a container refusing the add (body, locked, no room) flashes "won't fit"; putting something into itself is always a red X.
        pointer = MousePointerKind.WontFit;
        string name = PayloadName(origin);
        if (data.IsBodyContainer)
        {
            reason = $"You can't put things into \"{data.Name}\" - it's a body, not a real container.";
            return false;
        }
        // contain.cc add: a locked container refuses - on the map or carried, at any depth.
        if (IsLockedNow(container, data))
        {
            reason = $"\"{data.Name}\" is locked.";
            return false;
        }
        // contain.cc, "Always check this. ALWAYS! ... Watch for snake eating itself." - not just the container into itself, but into anything inside it, at any depth.
        var payloadObject = PayloadObject(origin);
        if (ReferenceEquals(payloadObject, container) || (payloadObject is IHasContainerContents payloadContainer && ContainsRecursively(payloadContainer, container)))
        {
            reason = $"\"{name}\" can't go inside itself.";
            pointer = MousePointerKind.RedX;
            return false;
        }
        double volume = PayloadVolume(origin);
        var alreadyInside = payloadObject as InventoryItemInstance;
        if (!ContainerHasRoomFor(data, container, volume, alreadyInside))
        {
            reason = $"\"{name}\" won't fit in \"{data.Name}\" - {UsedVolume(container, alreadyInside):0.##} + {volume:0.##} would exceed the max volume of {data.MaxVolume:0.##}.";
            return false;
        }
        return true;
    }

    private static bool ContainsRecursively(IHasContainerContents root, object target)
    {
        foreach (var child in root.Contents)
        {
            if (ReferenceEquals(child, target)) return true;
            if (child.Category == ReferencedItemCategory.Container && ContainsRecursively(child, target)) return true;
        }
        return false;
    }

    /// <summary>Total volume a container's contents occupy - every item through the one ResolveContentsVolume. excluding skips an item that's being moved and is already counted inside this same container.</summary>
    private double UsedVolume(IHasContainerContents container, InventoryItemInstance? excluding = null) =>
        container.Contents.Where(i => !ReferenceEquals(i, excluding)).Sum(ResolveContentsVolume);

    /// <summary>The single volume/fit check - every container drop path goes through this, so there is exactly one rule for "does it fit".</summary>
    private bool ContainerHasRoomFor(ContainerData containerData, IHasContainerContents container, double newItemVolume, InventoryItemInstance? excluding = null) =>
        UsedVolume(container, excluding) + newItemVolume <= containerData.MaxVolume;

    /// <summary>
    /// Exult Gump::add: dropped into the open container window, an item goes
    /// exactly where it was released, shifted to stay inside the area. Put
    /// into the container any other way (dropped on the container itself,
    /// on its icon, or stored automatically) it gets no spot yet - Exult's
    /// set_shape_pos(255, 255) - and is laid out the next time the window is
    /// drawn (LayOutUnplacedContents).
    /// </summary>
    private void PlaceWithinFrame(InventoryItemInstance item, ContainerData data, Point? topLeft)
    {
        if (topLeft is not { } point)
        {
            item.ContainerX = UnplacedSpot;
            item.ContainerY = UnplacedSpot;
            return;
        }
        _autoPlaced.Remove(item); // placed by hand - it keeps this spot and leaves the automatic row
        var bounds = data.GetFrameBounds();
        double maxX = bounds.X + Math.Max(0, bounds.Width - 32);
        double maxY = bounds.Y + Math.Max(0, bounds.Height - 32);
        item.ContainerX = Math.Clamp(point.X, bounds.X, maxX);
        item.ContainerY = Math.Clamp(point.Y, bounds.Y, maxY);
    }

    /// <summary>"No spot yet" - Exult's (255, 255) shape position. Runtime only: never part of authored data.</summary>
    private const double UnplacedSpot = -100000;

    private static bool IsUnplaced(InventoryItemInstance item) => item.ContainerX <= UnplacedSpot || item.ContainerY <= UnplacedSpot;

    /// <summary>Items the engine placed automatically (laid out in the row), as opposed to dropped at a chosen spot - runtime only.</summary>
    private readonly HashSet<InventoryItemInstance> _autoPlaced = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Exult Gump::paint, for a container being drawn: first check_elem_positions -
    /// if more than one item and they all share one spot (a fresh container),
    /// all become unplaced - then every unplaced item, in order, is laid out
    /// from the top-left of the item area: its bottom-right at (cursor +
    /// its size, clamped to the area), the cursor moving right by its width
    /// less one ("mostly avoid overlap"); past the right edge the row moves
    /// 8 down and back to the left; past the bottom it starts over 2 lower
    /// each time round. Items that already have a spot keep it.
    ///
    /// One deliberate difference, at the designer's request: Exult restarts
    /// this row at the top-left on every redraw, so items added one at a
    /// time all land on the same top-left spot, on top of each other. Here
    /// the row carries on after the items it already placed automatically,
    /// wrapping to the next row as Exult does within one pass. Items placed
    /// by hand keep their spot and take no part in the row.
    /// sizeOf gives each item's drawn size in this window.
    /// </summary>
    private void LayOutUnplacedContents(IHasContainerContents container, ContainerData data, Func<InventoryItemInstance, (double Width, double Height)> sizeOf)
    {
        var contents = container.Contents;
        if (contents.Count > 1 && contents.All(c => c.ContainerX == contents[0].ContainerX && c.ContainerY == contents[0].ContainerY))
            foreach (var item in contents) { item.ContainerX = UnplacedSpot; item.ContainerY = UnplacedSpot; }

        var area = data.GetFrameBounds();
        double endX = area.Width, endY = area.Height;
        double curX = 0, curY = 0;
        int loop = 0;
        foreach (var item in contents)
        {
            bool unplaced = IsUnplaced(item);
            if (!unplaced && !_autoPlaced.Contains(item)) continue; // placed by hand: keeps its spot, not part of the row
            var (width, height) = sizeOf(item);
            if (unplaced)
            {
                double px = Math.Min(curX + width, endX);
                double py = Math.Min(curY + height, endY);
                item.ContainerX = area.X + px - width;
                item.ContainerY = area.Y + py - height;
                _autoPlaced.Add(item);
            }
            // Already-placed row items keep their spot but still move the cursor on, so the next new item continues the row instead of restarting at the top-left.
            curX += width - 1;
            if (curX >= endX)
            {
                curY += 8;
                curX = 0;
                if (curY >= endY) curY = 2 * (++loop);
            }
        }
    }

    // ================= Character slot helpers =================

    /// <summary>The slot a container's Location (Container Editor) names for this character - a built-in slot, or the character's custom slot of that name - or null for "No preference" / a custom slot this character doesn't have.</summary>
    private object? ContainerLocationSlot(int containerId, CharacterDefinition character)
    {
        var containerData = _containers.Containers.FirstOrDefault(c => c.Id == containerId);
        if (containerData?.PreferredEquipSlot is { } builtin) return builtin;
        if (containerData?.PreferredCustomSlotName is { } customName)
            return character.Paperdoll.CustomSlots.FirstOrDefault(s => s.Name == customName)?.Id;
        return null;
    }

    /// <summary>A container may only be equipped in its own Location slot or in either hand.</summary>
    private bool IsContainerSlotAllowed(int containerId, CharacterDefinition character, object slotKey) =>
        slotKey is EquipmentSlot.RightHand or EquipmentSlot.LeftHand
        || (ContainerLocationSlot(containerId, character) is { } location && Equals(location, slotKey));

    /// <summary>A slot counts as free if it's empty, or if what's in it is the very item being dragged out of it.</summary>
    private static bool IsSlotFree(CharacterDefinition character, object slotKey, DragOrigin origin) =>
        !TryGetEquippedInstanceId(character, slotKey, out _)
        || (origin is SlotOrigin slotOrigin && slotOrigin.Character == character && Equals(slotOrigin.SlotKey, slotKey));

    private bool IsLeftHandLockedIgnoring(CharacterDefinition character, DragOrigin origin) =>
        IsPaperdollLeftHandLocked(character)
        && !(origin is SlotOrigin { SlotKey: EquipmentSlot.RightHand } slotOrigin && slotOrigin.Character == character);

    // ================= Moving the actual data =================

    /// <summary>Removes the dragged thing from wherever it lives. Only ever called once a drop has been accepted.</summary>
    private void RemoveFromSource(DragOrigin origin)
    {
        switch (origin)
        {
            case MapItemOrigin m:
                _map.Items.Remove(m.Item);
                if (_itemVisuals.Remove(m.Item, out var itemVisual)) { GameCanvas.Children.Remove(itemVisual); UnregisterWorldAnimator(itemVisual); }
                SettleWorld(); // whatever rested on it falls
                break;
            case MapObjectOrigin o:
                _map.Objects.Remove(o.Obj);
                if (_objectVisuals.Remove(o.Obj, out var objectVisual)) { GameCanvas.Children.Remove(objectVisual); UnregisterWorldAnimator(objectVisual); }
                SettleWorld(); // whatever rested on it falls
                break;
            case SlotOrigin s:
                RemoveFromCharacter(s.Character, s.Item);
                break;
            case BackpackOrigin b:
                RemoveFromCharacter(b.Character, b.Item);
                break;
            case ContainerOrigin c:
                c.Container.Contents.Remove(c.Item);
                break;
            case DollItemOrigin d:
                if (d.State.Parts.TryGetValue(d.PartId, out var dollPart)) dollPart.Items.Remove(d.Item);
                break;
        }
    }

    private void RemoveFromCharacter(CharacterDefinition character, InventoryItemInstance item)
    {
        character.Inventory.Remove(item);
        bool wasEquipped = false;
        foreach (var slot in character.EquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) { character.EquippedSlots.Remove(slot); wasEquipped = true; }
        foreach (var slot in character.CustomEquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) { character.CustomEquippedSlots.Remove(slot); wasEquipped = true; }
        if (wasEquipped) { RecordPersonItem("unequip", character, item); FireCharacterEvent(character, BookTriggerConditionBased.ByUnEquip); }
        foreach (var gump in _openPaperdolls.Where(g => g.Character == character && g.SelectedInstanceId == item.InstanceId)) gump.SelectedInstanceId = null;
    }

    /// <summary>
    /// Removes the dragged thing from its source and returns it as an
    /// inventory/container item, re-numbered for its destination's own id
    /// space. An item that already was an InventoryItemInstance is the SAME
    /// object afterwards - its Contents and Quantity travel with it intact.
    /// A map item is converted, carrying a container's live Contents (or
    /// seeding them from Starting Contents if it was never opened).
    /// </summary>
    private InventoryItemInstance DetachAsInventoryItem(DragOrigin origin, Func<int> nextInstanceId)
    {
        RemoveFromSource(origin);
        switch (origin)
        {
            case MapItemOrigin m:
                var fromMap = new InventoryItemInstance
                {
                    InstanceId = nextInstanceId(),
                    Category = m.Item.Category,
                    ItemId = m.Item.ItemId,
                    Quantity = 1,
                    IsLockedRuntime = m.Item.IsLockedRuntime, // its lock goes with it
                    // InstanceId 0 = this placed container was never opened, so it still holds its authored Starting Contents; anything else already has live Contents, carried over exactly as they are, empty or not.
                    Contents = m.Item.Category != ReferencedItemCategory.Container ? new List<InventoryItemInstance>()
                        : m.Item.InstanceId != 0 ? m.Item.Contents
                        : SeedContentsOf(m.Item)
                };
                RepointContainerGumps(m.Item, fromMap);
                return fromMap;
            case MapObjectOrigin o:
                // Everything it is now goes with it (Exult: the same object, same frame) - its facing, a torch's flame and burn time, its damage.
                return new InventoryItemInstance
                {
                    InstanceId = nextInstanceId(), Category = ReferencedItemCategory.Object, ItemId = o.Obj.ObjectId, Quantity = Math.Max(1, o.Obj.Quantity),
                    FacingRuntime = o.Obj.Facing, IsLitRuntime = o.Obj.IsLitRuntime, BurnLeftRuntime = o.Obj.BurnLeftRuntime,
                    IsSpentRuntime = o.Obj.IsSpentRuntime, FrameRuntime = o.Obj.FrameRuntime, HitPointsRuntime = o.Obj.HitPointsRuntime,
                    KeyIdsRuntime = o.Obj.KeyIdsRuntime, ChargesRuntime = o.Obj.ChargesRuntime, LiquidIdRuntime = o.Obj.LiquidIdRuntime,
                    DollRuntime = o.Obj.DollRuntime // its paperdoll as it is (the tape still in the radio)
                };
            default:
                var item = (InventoryItemInstance)PayloadObject(origin);
                item.InstanceId = nextInstanceId();
                return item;
        }
    }

    /// <summary>
    /// Seed-once-then-live: a placed container's InstanceId 0 means it has
    /// never been opened, so its real contents are still its authored
    /// Starting Contents. They're materialized the first time anything
    /// touches the contents - opening it (OpenContainer) OR putting
    /// something into it - so an item dropped into a never-opened chest is
    /// never wiped by a later seed, and the volume check counts what is
    /// really inside. True if it seeded just now.
    /// </summary>
    private bool EnsureContainerSeeded(IHasContainerContents container, ContainerData data)
    {
        if (container is not PlacedItemInstance { InstanceId: 0 } placed) return false;
        placed.InstanceId = _nextContainerRuntimeInstanceId++;
        placed.Contents.InsertRange(0, SeedContentsOf(placed));
        return true;
    }

    /// <summary>What a placed container starts with: its own contents if the Map Editor gave it some (Ctrl + double-click), otherwise its Starting Contents.</summary>
    private List<InventoryItemInstance> SeedContentsOf(PlacedItemInstance placed) =>
        placed.OwnContents is { } own ? CopyContents(own) : SeedStartingContents(placed.ItemId);

    /// <summary>Fresh live copies of authored contents - the template itself is never played with.</summary>
    private static List<InventoryItemInstance> CopyContents(IEnumerable<InventoryItemInstance> seeds) =>
        seeds.Select(seed => new InventoryItemInstance { InstanceId = seed.InstanceId, Category = seed.Category, ItemId = seed.ItemId, Quantity = seed.Quantity, ContainerX = seed.ContainerX, ContainerY = seed.ContainerY }).ToList();

    private List<InventoryItemInstance> SeedStartingContents(int containerId) =>
        _containers.Containers.FirstOrDefault(c => c.Id == containerId)?.StartingContents
            .Select(seed => new InventoryItemInstance { InstanceId = seed.InstanceId, Category = seed.Category, ItemId = seed.ItemId, Quantity = seed.Quantity, ContainerX = seed.ContainerX, ContainerY = seed.ContainerY })
            .ToList() ?? new List<InventoryItemInstance>();

    /// <summary>A not-yet-placed map entity for an inventory/container item headed for the map - only added to the map if the placement is accepted. A container keeps its live Contents and gets a real runtime id, so it's never re-seeded.</summary>
    private object CreateMapEntityFor(InventoryItemInstance item)
    {
        if (item.Category == ReferencedItemCategory.Object)
            return new ObjectInstance
            {
                ObjectId = item.ItemId, Quantity = Math.Max(1, item.Quantity),
                Facing = item.FacingRuntime, IsLitRuntime = item.IsLitRuntime, BurnLeftRuntime = item.BurnLeftRuntime,
                IsSpentRuntime = item.IsSpentRuntime, FrameRuntime = item.FrameRuntime, HitPointsRuntime = item.HitPointsRuntime,
                KeyIdsRuntime = item.KeyIdsRuntime, ChargesRuntime = item.ChargesRuntime, LiquidIdRuntime = item.LiquidIdRuntime,
                DollRuntime = item.DollRuntime
            };
        var placed = new PlacedItemInstance { Category = item.Category, ItemId = item.ItemId };
        if (item.Category == ReferencedItemCategory.Container)
        {
            placed.InstanceId = _nextContainerRuntimeInstanceId++;
            placed.Contents = item.Contents;
            placed.IsLockedRuntime = item.IsLockedRuntime; // its lock goes with it
        }
        return placed;
    }

    private static void SetMapPosition(PlacedItemInstance? item, ObjectInstance? obj, int x, int y, int offsetX, int offsetY)
    {
        if (item is not null) { item.X = x; item.Y = y; item.OffsetX = offsetX; item.OffsetY = offsetY; }
        if (obj is not null) { obj.X = x; obj.Y = y; obj.OffsetX = offsetX; obj.OffsetY = offsetY; }
    }

    private void AddMapEntity(PlacedItemInstance? item, ObjectInstance? obj)
    {
        if (item is not null)
        {
            var animators = new List<SpriteAnimator>();
            var visual = BuildItemIcon(item.Category, item.ItemId, animators, item);
            foreach (var animator in animators) RegisterWorldAnimator(visual, animator);
            GameCanvas.Children.Add(visual);
            _map.Items.Add(item);
            _itemVisuals[item] = visual;
        }
        else if (obj is not null)
        {
            var animators = new List<SpriteAnimator>();
            var visual = BuildObjectVisual(obj, animators);
            foreach (var animator in animators) RegisterWorldAnimator(visual, animator);
            GameCanvas.Children.Add(visual);
            _map.Objects.Add(obj);
            _objectVisuals[obj] = visual;
        }
    }

    /// <summary>A container's open gump follows it when it changes form (map item &lt;-&gt; carried item): its Contents list is the same list either way, so the gump just points at the new holder and stays open, as in Exult.</summary>
    private void RepointContainerGumps(object oldHolder, IHasContainerContents newHolder)
    {
        foreach (var gump in _openContainers.Where(g => ReferenceEquals(g.PlacedItem, oldHolder))) gump.PlacedItem = newHolder;
    }

    /// <summary>After any successful move, every view is rebuilt from the data - the map's stacking layout and every open gump - so no view can be left showing a stale copy.</summary>
    private void RefreshAfterMove()
    {
        RefreshStackingVisuals();
        foreach (var gump in _openPaperdolls.ToList()) RefreshPaperdollGump(gump);
        foreach (var gump in _openContainers.ToList()) RefreshContainerGump(gump);
        foreach (var gump in _openDolls.ToList()) RefreshDollGump(gump);
    }

    // ================= Map drop position =================

    /// <summary>Cell computed from the dragged visual's CENTER point, not a simple top-left/tileSize division - visuals are variable-sized and bottom-center anchored rather than always exactly one tile, so top-left no longer maps directly to a cell.</summary>
    private (int CellX, int CellY) ComputeDropCell(double visualLeft, double visualTop, double visualWidth, double visualHeight)
    {
        double centerX = visualLeft + visualWidth / 2;
        double centerY = visualTop + visualHeight / 2;
        int newCellX = (int)Math.Floor(centerX / _tileWidth);
        int newCellY = (int)Math.Floor(centerY / _tileHeight);
        newCellX = Math.Clamp(newCellX, 0, _map.Width - 1);
        newCellY = Math.Clamp(newCellY, 0, _map.Height - 1);
        return (newCellX, newCellY);
    }

    // ================= Payload description =================

    private static object PayloadObject(DragOrigin origin) => origin switch
    {
        MapItemOrigin m => m.Item,
        MapObjectOrigin o => o.Obj,
        SlotOrigin s => s.Item,
        BackpackOrigin b => b.Item,
        ContainerOrigin c => c.Item,
        DollItemOrigin d => d.Item,
        _ => throw new InvalidOperationException($"Unknown drag origin {origin}")
    };

    private static (ReferencedItemCategory Category, int ItemId) PayloadIdentity(DragOrigin origin)
    {
        if (origin is MapItemOrigin m) return (m.Item.Category, m.Item.ItemId);
        if (origin is MapObjectOrigin o) return (ReferencedItemCategory.Object, o.Obj.ObjectId);
        var carried = (InventoryItemInstance)PayloadObject(origin);
        return (carried.Category, carried.ItemId);
    }

    /// <summary>Through the one ResolveContentsVolume, like every other volume in the engine - a carried item counts its own Quantity, a map item counts as one.</summary>
    private double PayloadVolume(DragOrigin origin)
    {
        if (PayloadObject(origin) is InventoryItemInstance carried) return ResolveContentsVolume(carried);
        var (category, itemId) = PayloadIdentity(origin);
        return ResolveContentsVolume(new InventoryItemInstance { Category = category, ItemId = itemId, Quantity = 1 });
    }

    /// <summary>The paperdoll slot an ammo object goes into for this character (a built-in slot, or their custom slot of that name), or null.</summary>
    private static object? AmmoSlotFor(WorldObject ammo, CharacterDefinition character)
    {
        if (ammo.Ammo.PreferredEquipSlot is { } builtin) return builtin;
        if (ammo.Ammo.PreferredCustomSlotName is { } name) return character.Paperdoll.CustomSlots.FirstOrDefault(s => s.Name == name)?.Id;
        return null;
    }

    /// <summary>How many of it are being moved (a stack moves whole).</summary>
    private static int PayloadQuantity(DragOrigin origin) => PayloadObject(origin) switch
    {
        ObjectInstance o => Math.Max(1, o.Quantity),
        InventoryItemInstance i => Math.Max(1, i.Quantity),
        _ => 1
    };

    private bool PayloadIsTwoHanded(DragOrigin origin)
    {
        var (category, itemId) = PayloadIdentity(origin);
        return IsPaperdollItemTwoHanded(new InventoryItemInstance { Category = category, ItemId = itemId });
    }

    private string PayloadName(DragOrigin origin)
    {
        var (category, itemId) = PayloadIdentity(origin);
        return category switch
        {
            ReferencedItemCategory.Book => _books.Books.FirstOrDefault(b => b.Id == itemId) is { } book ? EffectiveBookName(book) : "book",
            ReferencedItemCategory.Container => _containers.Containers.FirstOrDefault(c => c.Id == itemId)?.Name ?? "container",
            ReferencedItemCategory.Object => _objects.Objects.FirstOrDefault(o => o.Id == itemId)?.Name ?? "object",
            _ => category.ToString()
        };
    }

    /// <summary>By Pickup Item / By Drop Item - fired at the same moments Book Preview fires them (drag start past the threshold; a completed drop on the map). Books and Containers are the categories with event pages.</summary>
    private void FireDragTrigger(ReferencedItemCategory category, int itemId, BookTriggerConditionBased trigger)
    {
        if (category == ReferencedItemCategory.Container)
        {
            var containerData = _containers.Containers.FirstOrDefault(c => c.Id == itemId);
            if (containerData is not null) TryFireEventPages(containerData, containerData.EventPages, trigger, containerData.Id, $"\"{containerData.Name}\" fired ({trigger}).");
            return;
        }
        if (category != ReferencedItemCategory.Book) return;
        var book = _books.Books.FirstOrDefault(b => b.Id == itemId);
        if (book is null) return;
        TryFireEventPages(book, book.EventPages, trigger, book.Id, $"\"{EffectiveBookName(book)}\" fired ({trigger}).");
    }

    // ================= Visuals =================

    /// <summary>
    /// The one builder for an item's world-sized visual - the map sprite of
    /// a Book/Container, an Object's icon, or the same honest lettered
    /// marker the map already uses when there's no art. Used for the drag
    /// ghost and for anything newly placed on the map. animators collects
    /// any SpriteAnimator started, so the caller can stop it (the ghost) or
    /// register it as part of the game world, frozen while the game is
    /// paused (map visuals - see GameTestWindow.Pause.cs).
    /// </summary>
    private FrameworkElement BuildItemIcon(ReferencedItemCategory category, int itemId, List<SpriteAnimator>? animators, object? instance = null)
    {
        // An Object Editor object: its look for its state and quantity.
        if (category == ReferencedItemCategory.Object)
        {
            if (instance is ObjectInstance placed) return BuildObjectVisual(placed, animators);
            if (instance is InventoryItemInstance carried && BuildCarriedObjectPicture(carried, 1, animators ?? new List<SpriteAnimator>()) is { } picture) return picture;
            return BuildObjectVisual(new ObjectInstance { ObjectId = itemId }, animators);
        }
        SpriteEntry? sprite = null;
        if (category == ReferencedItemCategory.Book && _books.Books.FirstOrDefault(b => b.Id == itemId) is { } book)
            sprite = _sprites.Groups.FirstOrDefault(g => g.Name == book.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book.SpriteName);
        else if (category == ReferencedItemCategory.Container && _containers.Containers.FirstOrDefault(c => c.Id == itemId) is { } containerData)
            sprite = ContainerSprite(containerData, instance as IHasContainerContents); // its Locked Sprite while locked

        if (sprite is not null && sprite.Frames.Count > 0)
        {
            var (width, height) = QuantizedSpriteSize(sprite);
            var image = new Image { Width = width, Height = height, IsHitTestVisible = true };
            var animator = new SpriteAnimator(sprite, image, _resources);
            animators?.Add(animator);
            return image;
        }

        if (category == ReferencedItemCategory.Object)
        {
            var worldObject = _objects.Objects.FirstOrDefault(o => o.Id == itemId);
            var bitmap = string.IsNullOrEmpty(worldObject?.IconPath) || !File.Exists(worldObject.IconPath) ? null : LoadStaticFrame(worldObject.IconPath);
            if (bitmap is not null)
            {
                var (width, height) = SpriteSizing.Quantize(bitmap.PixelWidth, bitmap.PixelHeight);
                return new Image { Source = bitmap, Width = width, Height = height, IsHitTestVisible = true };
            }
        }

        var (letter, background) = category switch
        {
            ReferencedItemCategory.Book => ("B", Brushes.SaddleBrown),
            ReferencedItemCategory.Container => ("C", Brushes.SaddleBrown),
            ReferencedItemCategory.Object => ("O", Brushes.DarkSlateGray),
            _ => (category.ToString()[0].ToString(), Brushes.DimGray)
        };
        return new Border
        {
            Width = _tileWidth, Height = _tileHeight, Background = background, IsHitTestVisible = true,
            Child = new TextBlock { Text = letter, Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }
}
