// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Beds, bedrolls and their covers, and a few carried-item rules:
///  - A bedroll is a bed you carry: in a pack it can't be slept on - put it
///    down, double-click it and it's laid out; the player walks to it and
///    lies down on its sleeping place (Object Editor: its looks and
///    sleeping places, as a bed's), with the same hours slider.
///  - The hours slider starts at 0: 0 means "don't sleep" - the sleeper
///    says so (its Cancel line) and gets up.
///  - A bed's cover lies over the bed: double-clicking it lies down on the
///    bed (unless the cover has events of its own); and the Sleep On Bed
///    event command does the same from any event.
///  - The keyring takes a key clicked with it (Keyring_data).
///  - A light can't be lit inside a container or the pack (only in a hand,
///    or set down); one put away in a container goes out.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>What an event's commands are running for right now (the object, book or part whose event it is).</summary>
    private object? _firingOwner;

    // ================= Sleeping =================

    /// <summary>A bedroll set to sleep without asking: its own hours, or until dawn.</summary>
    private int BedrollHours(WorldObject bedroll)
    {
        int hours = bedroll.Bedroll.SleepHours;
        if (bedroll.Bedroll.WakeAtDawn) hours = (6 - _clock.Hour + 24) % 24 is int untilDawn && untilDawn > 0 ? untilDawn : 24;
        return Math.Clamp(hours, 1, 24);
    }

    /// <summary>0 hours on the slider: no sleep - the sleeper says so and gets up.</summary>
    private void CancelSleep(WorldObject bed)
    {
        string line = bed.Furniture.CancelSleepLine.Trim();
        if (line.Length > 0) AvatarBark(line);
        if (_player is not null && PlayerSeated) StandUp(_player);
        StatusText.Text = $"You decide not to sleep in \"{bed.Name}\".";
    }

    /// <summary>The bed a cover lies on (the cover's own instance), or null.</summary>
    private ObjectInstance? BedUnderCover(ObjectInstance cover) =>
        _map.Objects.FirstOrDefault(b => !ReferenceEquals(b, cover) && DefinitionOf(b) is { Type: ObjectType.Bed } bed
            && bed.Furniture.CoverObjectId == cover.ObjectId && ReferenceEquals(CoverOf(b, bed), cover));

    /// <summary>Lies down on a bed or a bedroll on the map, as double-clicking it does.</summary>
    private void LieDownOn(ObjectInstance bed)
    {
        if (DefinitionOf(bed) is not { } obj) return;
        if (obj.Type == ObjectType.Bedroll) UseBedroll(new Used(obj, new MapObjectOrigin(bed)));
        else LieOn(bed, obj);
    }

    /// <summary>Sleep On Bed (event command): "Under" = the bed the running event's object lies on (a cover), else the nearest.</summary>
    private bool SleepOnBedCommand(string which)
    {
        if (_player is null) return false;
        ObjectInstance? bed = null;
        if (which != "Nearest" && _firingOwner is ObjectInstance self)
        {
            bed = DefinitionOf(self) is { Type: ObjectType.Bed or ObjectType.Bedroll } ? self : BedUnderCover(self);
            bed ??= _map.Objects.FirstOrDefault(b => !ReferenceEquals(b, self) && b.X == self.X && b.Y == self.Y && DefinitionOf(b)?.Type is ObjectType.Bed or ObjectType.Bedroll);
        }
        bed ??= _map.Objects
            .Where(b => b.ContainerInstanceId < 0 && DefinitionOf(b)?.Type is ObjectType.Bed or ObjectType.Bedroll)
            .OrderBy(b => Math.Abs(b.X - _playerX) + Math.Abs(b.Y - _playerY))
            .FirstOrDefault();
        if (bed is null) { StatusText.Text = "Sleep On Bed: there's no bed."; return false; }
        LieDownOn(bed);
        return true;
    }

    // ================= Keys and the keyring =================

    /// <summary>A key clicked as a target - on the map, in a slot, the pack, a container window or a paperdoll - and where it is.</summary>
    private (DragOrigin Origin, WorldObject Key)? KeyItemTargeted(InteractionTarget? target)
    {
        DragOrigin? origin = target switch
        {
            MapEntityTarget { Entity: ObjectInstance o } when _map.Objects.Contains(o) => new MapObjectOrigin(o),
            BackpackRowTarget b when b.Gump.Character.Inventory.Contains(b.Item) => new BackpackOrigin(b.Gump.Character, b.Item),
            ContainerItemTarget c when c.Gump.PlacedItem.Contents.Contains(c.Item) => new ContainerOrigin(c.Gump.PlacedItem, c.Gump.Data, c.Item),
            DollItemTarget d when DollItemStillThere(new DollItemOrigin(d.Gump.State, d.PartId, d.Item)) => new DollItemOrigin(d.Gump.State, d.PartId, d.Item),
            SlotTarget s when TryGetEquippedInstanceId(s.Gump.Character, s.SlotKey, out int id) && s.Gump.Character.Inventory.FirstOrDefault(i => i.InstanceId == id) is { } equipped
                => new SlotOrigin(s.Gump.Character, s.SlotKey, equipped),
            _ => null
        };
        if (origin is null) return null;
        var definition = PayloadObject(origin) switch
        {
            ObjectInstance o => DefinitionOf(o),
            InventoryItemInstance { Category: ReferencedItemCategory.Object } i => DefinitionOf(i),
            _ => null
        };
        return definition is { Type: ObjectType.Key } ? (origin, definition) : null;
    }

    // ================= Carried things =================

    /// <summary>A carried object's name as shown: "Bucket of water" for a full liquid container, as on the map.</summary>
    private string CarriedObjectName(WorldObject obj, InventoryItemInstance item)
    {
        string name = ObjectDisplayName(obj, item.IsSpentRuntime, item.Quantity);
        return obj.Type == ObjectType.LiquidContainer && LiquidFood(item.LiquidIdRuntime ?? obj.Liquid.StartsFullOfId) is { } liquid
            ? $"{name} of {liquid.Name.ToLowerInvariant()}" : name;
    }

    /// <summary>A lit light put away in a container goes out.</summary>
    private void PutOutIfLit(InventoryItemInstance item)
    {
        if (item.IsLitRuntime != true || DefinitionOf(item) is not { Type: ObjectType.LightSource } light) return;
        item.IsLitRuntime = false;
        PlaySound(light.Light.OffSoundResourceId);
    }
}
