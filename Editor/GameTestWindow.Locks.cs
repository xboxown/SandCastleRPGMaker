using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Locks, keys and lockpicks, ported from Exult:
///  - Choosing a target (exult.cc Get_click, via UI_click_on_item): the
///    pointer changes to Green Select, the game pauses (the time queue is
///    paused), the next left click picks whatever is under it, and Escape
///    cancels.
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
///    breaks the pick. The chance here is the same labelled approximation
///    as before: Dexterity vs the container's Pick-Lock Difficulty, 50% at
///    equal, +-5% a point, 5%..95%.
///  - Double-clicking a container that's locked doesn't open it: "Locked"
///    over it (Ultima VII's chest usecode).
/// Only containers standing on the map carry lock state in play.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Waiting for the player to click a target (Get_click); what to do with what they click (null = nothing there).</summary>
    private Action<InteractionTarget?>? _choosingTarget;

    private bool IsChoosingTarget => _choosingTarget is not null;

    private static readonly string[] KeyDoesntFitBarks = { "The key doesn't fit.", "Maybe it is another key." };

    /// <summary>Whether this container on the map is locked right now.</summary>
    private static bool IsLockedNow(PlacedItemInstance container, ContainerData data) => container.IsLockedRuntime ?? data.IsLocked;

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

    // ---------- Using a key or lockpick ----------

    /// <summary>Double-clicking an object: a key or lockpick asks for a target. True = handled.</summary>
    private bool TryUseLockTool(WorldObject? tool, DragOrigin source)
    {
        if (tool is null || tool.UseAs == ObjectUse.Nothing) return false;
        BeginChoosingTarget(tool.UseAs == ObjectUse.Key ? $"Use \"{tool.Name}\" on what?" : $"Pick which lock with \"{tool.Name}\"?",
            target =>
            {
                // The tool may have gone while the player chose (it can't, the game is paused - but stay safe).
                if (!ToolStillThere(source)) return;
                if (target is not MapEntityTarget { Entity: PlacedItemInstance { Category: ReferencedItemCategory.Container } container }
                    || !_map.Items.Contains(container)
                    || _containers.Containers.FirstOrDefault(c => c.Id == container.ItemId) is not { IsBodyContainer: false } data)
                {
                    StatusText.Text = string.Empty; // KeyInternal: "The target cannot be unlocked" - nothing happens
                    return;
                }
                if (tool.UseAs == ObjectUse.Key) UseKeyOn(tool, source, container, data);
                else UseLockpickOn(tool, source, container, data);
            });
        return true;
    }

    /// <summary>keyring.uc KeyInternal + UseKeyOnChest.</summary>
    private void UseKeyOn(WorldObject key, DragOrigin source, PlacedItemInstance container, ContainerData data)
    {
        bool fits = data.RequiredKeyCategory == ReferencedItemCategory.Object && data.RequiredKeyItemId == key.Id;
        if (!fits)
        {
            Refuse(MousePointerKind.Blocked, $"\"{key.Name}\" doesn't fit \"{data.Name}\"."); // flashBlocked
            PartyBark(KeyDoesntFitBarks[Rng.Next(KeyDoesntFitBarks.Length)]);
            return;
        }

        PlayLockSound(data);
        var visual = VisualOf(container);
        if (IsLockedNow(container, data))
        {
            container.IsLockedRuntime = false;
            ShowFloatingTextOver(visual, "Unlocked");
            StatusText.Text = $"You unlock \"{data.Name}\".";
            return;
        }

        if (source is ContainerOrigin { Item: var keyItem } && IsInside(container.Contents, keyItem))
        {
            if (_player is not null) ShowFloatingText(_player, $"The key is inside the {data.Name}!", 3000);
            return;
        }

        // Its gumps close, and those of every container inside it.
        foreach (var gump in _openContainers.Where(g => ReferenceEquals(g.PlacedItem, container)
                     || (g.PlacedItem is InventoryItemInstance inner && IsInside(container.Contents, inner))).ToList())
            CloseContainerGump(gump);
        container.IsLockedRuntime = true;
        ShowFloatingTextOver(visual, "Locked");
        StatusText.Text = $"You lock \"{data.Name}\".";
    }

    /// <summary>A lockpick on a locked container: Dexterity vs Pick-Lock Difficulty (approximation, see the class summary); a failure breaks the pick.</summary>
    private void UseLockpickOn(WorldObject lockpick, DragOrigin source, PlacedItemInstance container, ContainerData data)
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
            container.IsLockedRuntime = false;
            PlayLockSound(data);
            ShowFloatingTextOver(VisualOf(container), "Unlocked");
            StatusText.Text = $"You pick the lock on \"{data.Name}\". ({chance}% chance)";
            return;
        }

        UseUpOne(source);
        if (_player is not null) ShowFloatingText(_player, "The lockpick broke!", 2000);
        StatusText.Text = $"You fail to pick the lock on \"{data.Name}\", and \"{lockpick.Name}\" breaks. ({chance}% chance)";
    }

    // ---------- Helpers ----------

    private void PlayLockSound(ContainerData data)
    {
        if (data.LockSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, data.LockSoundResourceId);
    }

    /// <summary>Exult's randomPartyBark: a random member of the party (the player included) says it.</summary>
    private void PartyBark(string text)
    {
        var party = new List<CharacterDefinition>();
        if (_player is not null && !IsDead(_player)) party.Add(_player);
        party.AddRange(_mapCharacters.Select(mc => mc.Character).Where(c => c.IsInParty && c != _player && !IsDead(c)));
        if (party.Count > 0) ShowFloatingText(party[Rng.Next(party.Count)], text, 3000);
    }

    /// <summary>Whether the item is in these contents, at any depth.</summary>
    private static bool IsInside(List<InventoryItemInstance> contents, InventoryItemInstance item) =>
        contents.Any(c => ReferenceEquals(c, item) || IsInside(c.Contents, item));

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
