using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Locks, keys and lockpicks, ported from Exult:
///  - A container's lock belongs to the container itself (in Exult a locked
///    chest is its own shape), so it stays locked wherever it goes: on the
///    map, in a hand or paperdoll slot, in the backpack list, or inside
///    another container, at any depth. Picking it up or putting it down
///    keeps its state.
///  - A locked container takes nothing in (contain.cc add:
///    is_container_locked), and double-clicking it doesn't open it: it says
///    "Locked" (Ultima VII's chest usecode).
///  - Choosing a target (exult.cc Get_click, via UI_click_on_item): the
///    pointer changes to Green Select, the game pauses (the time queue is
///    paused), the next left click picks whatever is under it - on the map
///    or in an open gump - and Escape cancels.
///  - Keys (content/bgkeyring/src/items/key.uc and keyring.uc KeyInternal /
///    UseKeyOnChest): double-click a key, click a container. Not a lockable
///    container: nothing happens. The key fits (it's the container's Required
///    Key): the key sound plays; locked -> unlocked, "Unlocked" over it;
///    unlocked -> its gumps (and those of containers inside it) close, it
///    locks, "Locked" over it - unless the key is inside it ("The key is
///    inside the chest!"). The key doesn't fit: the blocked pointer flashes
///    and a party member says "The key doesn't fit." or "Maybe it is another
///    key.".
///  - Lockpicks: the original's lockpick usecode isn't in Exult's source,
///    so its exact chance and messages couldn't be verified. Known from
///    play: you use the pick on a locked chest or door, and a failed try
///    breaks the pick. The chance here is a labelled approximation:
///    Dexterity vs the container's Pick-Lock Difficulty, 50% at equal,
///    +-5% a point, 5%..95%.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Waiting for the player to click a target (Get_click); what to do with what they click (null = nothing there).</summary>
    private Action<InteractionTarget?>? _choosingTarget;

    private bool IsChoosingTarget => _choosingTarget is not null;

    private static readonly string[] KeyDoesntFitBarks = { "The key doesn't fit.", "Maybe it is another key." };

    /// <summary>Whether this container is locked right now, wherever it is - its own state once a key or lockpick changed it, otherwise as its Container Editor entry starts.</summary>
    private static bool IsLockedNow(IHasContainerContents container, ContainerData data) => container switch
    {
        PlacedItemInstance placed => placed.IsLockedRuntime ?? data.IsLocked,
        InventoryItemInstance carried => carried.IsLockedRuntime ?? data.IsLocked,
        _ => data.IsLocked
    };

    private void SetLocked(IHasContainerContents container, bool locked)
    {
        if (container is PlacedItemInstance placed) placed.IsLockedRuntime = locked;
        else if (container is InventoryItemInstance carried) carried.IsLockedRuntime = locked;
        RefreshContainerLook(container);
    }

    /// <summary>
    /// The sprite a container shows right now: its Locked Sprite while it's
    /// locked (Ultima VII's locked chest is its own shape), otherwise - or
    /// with no Locked Sprite - its World Sprite. <paramref name="instance"/>
    /// null = how it starts (a fresh one).
    /// </summary>
    private SpriteEntry? ContainerSprite(ContainerData data, IHasContainerContents? instance)
    {
        bool locked = instance is null ? data.IsLocked : IsLockedNow(instance, data);
        if (locked && !string.IsNullOrEmpty(data.LockedSpriteName)
            && _sprites.Groups.FirstOrDefault(g => g.Name == data.LockedSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == data.LockedSpriteName) is { } lockedSprite)
            return lockedSprite;
        return _sprites.Groups.FirstOrDefault(g => g.Name == data.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == data.SpriteName);
    }

    /// <summary>After its lock changes: the container on the map swaps sprite, and every open gump redraws (slots, container windows).</summary>
    private void RefreshContainerLook(IHasContainerContents container)
    {
        if (container is PlacedItemInstance placed && _itemVisuals.GetValueOrDefault(placed) is Image image
            && _containers.Containers.FirstOrDefault(c => c.Id == placed.ItemId) is { } data
            && ContainerSprite(data, placed) is { Frames.Count: > 0 } sprite)
        {
            var frame = LoadStaticFrame(sprite.Frames[0].FilePath);
            var (width, height) = frame is not null ? SpriteSizing.Quantize(frame.PixelWidth, frame.PixelHeight) : ((int)image.Width, (int)image.Height);
            image.Width = width;
            image.Height = height;
            UnregisterWorldAnimator(image);
            RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources));
        }
        RefreshAfterMove();
    }

    // ---------- Choosing a target (Get_click) ----------

    private void BeginChoosingTarget(string prompt, Action<InteractionTarget?> onChosen)
    {
        CancelPendingIdentify();
        _choosingTarget = onChosen;
        ShowPointer(RestingPointer); // Green Select while choosing
        StatusText.Text = prompt + " (Escape to cancel)";
    }

    /// <summary>The next left press while choosing picks the target. True = it was consumed.</summary>
    private bool TryChooseTarget(MouseButtonEventArgs e)
    {
        if (_choosingTarget is not { } onChosen) return false;
        _choosingTarget = null;
        _lastTargetClickWorld = e.GetPosition(GameCanvas); // Get_click's x, y - where on the floor, for a spill
        var target = FindInteractionTarget(e.GetPosition(PaperdollGumpsCanvas));
        ShowPointer(RestingPointer);
        e.Handled = true;
        onChosen(target);
        return true;
    }

    /// <summary>Escape while choosing (Get_click returns false): nothing happens.</summary>
    private bool TryCancelChoosingTarget()
    {
        if (_choosingTarget is null) return false;
        _choosingTarget = null;
        ShowPointer(RestingPointer);
        StatusText.Text = string.Empty;
        return true;
    }

    /// <summary>The container a click picked - on the map, in an open container, in a paperdoll slot or the backpack list - or null if it isn't one (bodies don't count).</summary>
    private (IHasContainerContents Container, ContainerData Data)? ContainerTargeted(InteractionTarget? target)
    {
        IHasContainerContents? container = target switch
        {
            MapEntityTarget { Entity: PlacedItemInstance { Category: ReferencedItemCategory.Container } placed } when _map.Items.Contains(placed) => placed,
            ContainerItemTarget { Item: { Category: ReferencedItemCategory.Container } inside } => inside,
            BackpackRowTarget { Item: { Category: ReferencedItemCategory.Container } row } => row,
            SlotTarget slot when TryGetEquippedInstanceId(slot.Gump.Character, slot.SlotKey, out int id)
                && slot.Gump.Character.Inventory.FirstOrDefault(i => i.InstanceId == id) is { Category: ReferencedItemCategory.Container } equipped => equipped,
            _ => null
        };
        int? itemId = container switch { PlacedItemInstance p => p.ItemId, InventoryItemInstance i => i.ItemId, _ => null };
        if (container is null || itemId is null || _containers.Containers.FirstOrDefault(c => c.Id == itemId) is not { IsBodyContainer: false } data) return null;
        return (container, data);
    }

    // ---------- Using a key or lockpick ----------

    /// <summary>Double-clicking an object: a key or lockpick asks for a target. True = handled.</summary>
    private bool TryUseLockTool(WorldObject? tool, DragOrigin source)
    {
        if (tool is null || tool.Type is not (ObjectType.Key or ObjectType.Lockpick)) return false;
        BeginChoosingTarget(tool.Type == ObjectType.Key ? $"Use \"{tool.Name}\" on what?" : $"Pick which lock with \"{tool.Name}\"?",
            target =>
            {
                // The tool may have gone while the player chose (it can't, the game is paused - but stay safe).
                if (!ToolStillThere(source)) return;
                // A door (keyring.uc KeyInternal: doors and chests alike).
                if (target is MapEntityTarget { Entity: ObjectInstance door } && _map.Objects.Contains(door) && DefinitionOf(door) is { Type: ObjectType.Door } doorObj)
                {
                    if (tool.Type == ObjectType.Key) UseKeyOnDoor(tool, door, doorObj);
                    else UseLockpickOnDoor(tool, source, door, doorObj);
                    return;
                }
                if (ContainerTargeted(target) is not { } hit)
                {
                    StatusText.Text = string.Empty; // KeyInternal: "The target cannot be unlocked" - nothing happens
                    return;
                }
                var (container, data) = hit;
                if (tool.Type == ObjectType.Key) UseKeyOn(tool, source, container, data);
                else UseLockpickOn(tool, source, container, data);
            });
        return true;
    }

    /// <summary>keyring.uc KeyInternal + UseKeyOnChest.</summary>
    private void UseKeyOn(WorldObject key, DragOrigin source, IHasContainerContents container, ContainerData data)
    {
        bool fits = data.RequiredKeyCategory == ReferencedItemCategory.Object && data.RequiredKeyItemId == key.Id;
        if (!fits)
        {
            // flashBlocked: the blocked pointer and its sound - or the container's own Wrong Key Sound instead.
            StatusText.Text = $"\"{key.Name}\" doesn't fit \"{data.Name}\".";
            FlashPointer(MousePointerKind.Blocked, playSound: data.WrongKeySoundResourceId < 0);
            PlaySound(data.WrongKeySoundResourceId);
            PartyBark(VerboseKind.WrongKey);
            return;
        }

        if (IsLockedNow(container, data))
        {
            PlaySound(UnlockSound(data));
            SetLocked(container, false);
            SayOverContainer(container, "Unlocked");
            StatusText.Text = $"You unlock \"{data.Name}\".";
            return;
        }

        if (ItemWeights.Holds(container.Contents, PayloadObject(source)))
        {
            if (_player is not null) ShowFloatingText(_player, $"The key is inside the {data.Name}!", 3000);
            return;
        }

        // Its gumps close, and those of every container inside it.
        foreach (var gump in _openContainers.Where(g => ReferenceEquals(g.PlacedItem, container) || ItemWeights.Holds(container.Contents, g.PlacedItem)).ToList())
            CloseContainerGump(gump);
        PlaySound(LockSound(data));
        SetLocked(container, true);
        SayOverContainer(container, "Locked");
        StatusText.Text = $"You lock \"{data.Name}\".";
    }

    /// <summary>A lockpick on a locked container: Dexterity vs Pick-Lock Difficulty (approximation, see the class summary); a failure breaks the pick.</summary>
    private void UseLockpickOn(WorldObject lockpick, DragOrigin source, IHasContainerContents container, ContainerData data)
    {
        if (!IsLockedNow(container, data))
        {
            StatusText.Text = $"\"{data.Name}\" isn't locked.";
            return;
        }

        int dexterity = _player is null ? 0 : Stat(_player, BuiltInStatKind.Dexterity, 10);
        int chance = Math.Clamp((dexterity - data.PickLockDifficulty) * 5 + 50, 5, 95);
        if (Rng.Next(100) < chance)
        {
            SetLocked(container, false);
            PlaySound(UnlockSound(data));
            SayOverContainer(container, "Unlocked");
            StatusText.Text = $"You pick the lock on \"{data.Name}\". ({chance}% chance)";
            return;
        }

        UseUpOne(source);
        PlaySound(data.LockpickBrokeSoundResourceId);
        if (_player is not null) Bark(_player, VerboseKind.LockpickFailed);
        StatusText.Text = $"You fail to pick the lock on \"{data.Name}\", and \"{lockpick.Name}\" breaks. ({chance}% chance)";
    }

    // ---------- Locked when opened ----------

    /// <summary>Double-clicking a carried container (slot, backpack list, inside another container): a locked one says "Locked" instead of opening. True = it was locked.</summary>
    private bool RefuseIfLocked(InventoryItemInstance carried, ContainerData data)
    {
        if (!IsLockedNow(carried, data)) return false;
        PlaySound(data.LockedRattleSoundResourceId);
        SayOverContainer(carried, "Locked");
        StatusText.Text = $"\"{data.Name}\" is locked.";
        return true;
    }

    /// <summary>item_say: words over the container - on the map, or over its picture in whichever open gump shows it.</summary>
    private void SayOverContainer(IHasContainerContents container, string text)
    {
        if (container is PlacedItemInstance placed) { ShowFloatingTextOver(VisualOf(placed), text); return; }
        if (container is InventoryItemInstance carried && GumpVisualOf(carried) is { Parent: Canvas canvas } visual)
            ShowGumpLabel(canvas, text, Canvas.GetLeft(visual), Canvas.GetTop(visual));
    }

    /// <summary>The picture of a carried item in an open gump (a paperdoll slot or a container window), or null if none shows it.</summary>
    private FrameworkElement? GumpVisualOf(InventoryItemInstance item)
    {
        FrameworkElement? found = null;
        void Walk(DependencyObject node)
        {
            if (found is not null) return;
            if (node is FrameworkElement { Tag: InteractionTarget tag } element && element.Parent is Canvas)
            {
                bool match = tag switch
                {
                    ContainerItemTarget ci => ReferenceEquals(ci.Item, item),
                    SlotTarget slot => TryGetEquippedInstanceId(slot.Gump.Character, slot.SlotKey, out int id) && id == item.InstanceId
                                       && slot.Gump.Character.Inventory.Contains(item),
                    _ => false
                };
                if (match) { found = element; return; }
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(PaperdollGumpsCanvas);
        return found;
    }

    // ---------- Helpers ----------

    private void PlaySound(int resourceId)
    {
        if (resourceId >= 0) SoundPlayback.TryPlay(_resources, resourceId);
    }

    /// <summary>Unlocking: the Unlock Sound, or the Lock Sound if there's none (Exult's one key sound).</summary>
    private static int UnlockSound(ContainerData data) => data.UnlockSoundResourceId >= 0 ? data.UnlockSoundResourceId : data.LockSoundResourceId;
    /// <summary>Locking: the Lock Sound, or the Unlock Sound if there's none.</summary>
    private static int LockSound(ContainerData data) => data.LockSoundResourceId >= 0 ? data.LockSoundResourceId : data.UnlockSoundResourceId;

    /// <summary>Exult's randomPartyBark: a random member of the party who can speak (the player included) says one of their own lines.</summary>
    private void PartyBark(VerboseKind kind)
    {
        var party = new List<CharacterDefinition>();
        if (_player is not null && !IsDead(_player)) party.Add(_player);
        party.AddRange(_mapCharacters.Select(mc => mc.Character).Where(c => c.IsInParty && c != _player && !IsDead(c)));
        party.RemoveAll(c => !c.IsHuman);
        if (party.Count > 0) Bark(party[Rng.Next(party.Count)], kind);
    }

    private bool ToolStillThere(DragOrigin source) => source switch
    {
        MapObjectOrigin o => _map.Objects.Contains(o.Obj),
        SlotOrigin s => s.Character.Inventory.Contains(s.Item),
        BackpackOrigin b => b.Character.Inventory.Contains(b.Item),
        ContainerOrigin c => c.Container.Contents.Contains(c.Item),
        _ => false
    };

    /// <summary>One of a stack is used up; the last one is removed from wherever it is.</summary>
    private void UseUpOne(DragOrigin source)
    {
        switch (source)
        {
            case MapObjectOrigin { Obj.Quantity: > 1 } o: o.Obj.Quantity--; break;
            case SlotOrigin { Item.Quantity: > 1 } s: s.Item.Quantity--; break;
            case BackpackOrigin { Item.Quantity: > 1 } b: b.Item.Quantity--; break;
            case ContainerOrigin { Item.Quantity: > 1 } c: c.Item.Quantity--; break;
            default: RemoveFromSource(source); break;
        }
        RefreshAfterMove();
    }
}
