// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Character events and dead bodies.
///  - A character's own events (Character Editor > Event) fire with that
///    character as Self: By Equip / By UnEquip (something goes into or out
///    of one of its slots), By Hit (a blow lands), By Ailment Effect (it
///    catches an ailment), By Death.
///  - Exult's Actor::die: the character is taken out of the world and a body
///    is left where it fell, holding everything it carried. The body is a
///    real container object - its own weight and volume, picked up, carried,
///    put in a bag like anything else, unless marked Too Heavy - drawn as the
///    character's Body pose and opened as System Settings' Body Container.
///    Things come out of a body; nothing goes back in (CanContainerAccept).
///  - The main player's death isn't Exult's: the player lies in the Body
///    pose, its By Death events run, and the game ends.
/// </summary>
public partial class U8GameWindow
{
    private ContainerData _bodyContainerTemplate = new() { Name = "Body", IsBodyContainer = true, MaxVolume = 9999 };

    /// <summary>Runs a character's own event pages for something that just happened to it.</summary>
    private void FireCharacterEvent(CharacterDefinition character, BookTriggerConditionBased trigger)
    {
        if (character.EventPages.Count == 0) return;
        var (previous, previousStep) = (_activator, _activatedByCharacterStep);
        _activator = MapCharacterOf(character); // Self = this character (null = the player)
        _activatedByCharacterStep = false;
        try { TryFireEventPages(character, character.EventPages, trigger, selfBookId: -1, $"{character.Name}: {trigger} event fired."); }
        finally
        {
            _activator = previous;
            _activatedByCharacterStep = previousStep;
        }
    }

    /// <summary>Actor::die's body: a container holding everything the character carried, where it fell; the character itself leaves the world.</summary>
    private void LeaveBody(MapCharacter mapCharacter)
    {
        var character = mapCharacter.Character;
        var (cellX, cellY, lift) = (mapCharacter.Placement.X, mapCharacter.Placement.Y, IsFlier(mapCharacter) ? GroundBelow(mapCharacter) : mapCharacter.Lift); // a flier falls
        if (character.Body.NoBody) { LeaveNoBody(mapCharacter, lift); return; }

        // The body's container: the Body Container template, looking like this character's Body pose, weighing what its settings say.
        string json = System.Text.Json.JsonSerializer.Serialize(_bodyContainerTemplate);
        var data = System.Text.Json.JsonSerializer.Deserialize<ContainerData>(json) ?? new ContainerData();
        data.Id = _containers.Containers.Count == 0 ? 1 : _containers.Containers.Max(c => c.Id) + 1;
        data.Name = $"body of {character.Name}";
        data.IsBodyContainer = true;
        data.IsLocked = false;
        data.StartingContents = new List<InventoryItemInstance>();
        data.EventPages = new List<BookEventPage>();
        data.Weight = character.Body.Weight;
        data.Volume = character.Body.Volume;
        data.CanBePicked = !character.Body.TooHeavy;
        data.IsSolid = false; // walked over, like Exult's bodies
        data.AllowElevation = false;
        data.IsStackable = false;
        data.BelongToId = -1; // taking from the dead isn't theft
        data.FactionId = -1;
        ResolveCharacterSprite(character, PoseByName("Body"), mapCharacter.Facing, out _, out string? spriteKey);
        if (spriteKey?.Split('/', 2) is [var group, var name]) { data.SpriteGroup = group; data.SpriteName = name; }
        if (data.MaxVolume <= 0) data.MaxVolume = 9999;
        _containers.Containers.Add(data);

        // Everything it carried - worn or in the pack - goes into the body, laid out in its frame.
        var belongings = character.Inventory.ToList();
        character.Inventory.Clear();
        character.EquippedSlots.Clear();
        character.CustomEquippedSlots.Clear();
        if (!character.IsHuman) // not human: what may be found in its body (Character Editor > When it dies), each by its chance
            foreach (var drop in character.DeathDrops)
                if (ObjectDef(drop.ObjectId) is not null && _random.Next(100) < drop.Percent) belongings.AddRange(MakeGifts(ReferencedItemCategory.Object, drop.ObjectId, Math.Max(1, drop.Quantity)));
        foreach (var item in belongings) PlaceWithinFrame(item, data, null);

        var body = new PlacedItemInstance
        {
            Category = ReferencedItemCategory.Container, ItemId = data.Id, X = cellX, Y = cellY,
            InstanceId = _nextContainerRuntimeInstanceId++, // already "opened" - never seeded with starting contents
            Contents = belongings
        };

        // Out of the world: the character's own figure goes, the body stays.
        if (mapCharacter.Visual is { } visual)
        {
            UnregisterWorldAnimator(visual);
            GameCanvas.Children.Remove(visual);
        }
        _mapCharacters.Remove(mapCharacter);
        _bodies[character] = (body, mapCharacter.Placement); // Resurrect brings it back here
        mapCharacter.Placement.Dead = true; // a save (or a world left and come back to) keeps it dead, its body where it fell
        mapCharacter.Placement.BodyInstanceId = body.InstanceId;

        AddMapEntity(body, null);
        SetMapPosition(body, null, cellX, cellY, 0, 0);
        SetZ(body, lift);
        ForgetHomeFloor(body);
        SettleWorld();
        RefreshAfterMove();
    }

    /// <summary>
    /// Exult's has_no_body: no body is left. What it carried falls on the ground, each thing on the nearest free spot within 5 tiles of
    /// where it died (Actor::die's find_spot); nothing but the things stays.
    /// </summary>
    private void LeaveNoBody(MapCharacter mapCharacter, int lift)
    {
        var character = mapCharacter.Character;
        var (homeX, homeY) = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
        var belongings = character.Inventory.ToList();
        character.Inventory.Clear();
        character.EquippedSlots.Clear();
        character.CustomEquippedSlots.Clear();
        if (!character.IsHuman)
            foreach (var drop in character.DeathDrops)
                if (ObjectDef(drop.ObjectId) is not null && _random.Next(100) < drop.Percent) belongings.AddRange(MakeGifts(ReferencedItemCategory.Object, drop.ObjectId, Math.Max(1, drop.Quantity)));

        int radius = CellsForTiles(5), floor = MapFloors.FloorOfLift(lift);
        var spots = new List<(int X, int Y)>();
        var world = WorldNow();
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var cell = (X: homeX + dx, Y: homeY + dy);
                if (InsideMap(cell.X, cell.Y) && CanStandAt(mapCharacter, cell, lift, world, out int at) && MapFloors.FloorOfLift(at) == floor) spots.Add(cell);
            }
        SoakLog($"no body: {character.Name} drops {belongings.Count} things, {spots.Count} spots");
        var order = spots.OrderBy(c => Math.Max(Math.Abs(c.X - homeX), Math.Abs(c.Y - homeY))).ThenBy(_ => _random.Next()).ToList();
        for (int i = 0; i < belongings.Count; i++)
        {
            var (x, y) = order.Count > 0 ? order[i % order.Count] : (homeX, homeY); // more things than spots: they pile up
            PlaceNewThing(belongings[i], x, y, floor);
        }

        if (mapCharacter.Visual is { } visual)
        {
            UnregisterWorldAnimator(visual);
            GameCanvas.Children.Remove(visual);
        }
        _mapCharacters.Remove(mapCharacter);
        mapCharacter.Placement.Dead = true;
        mapCharacter.Placement.BodyInstanceId = -1;
        SettleWorld();
        RefreshAfterMove();
    }

    /// <summary>A container marked Too Heavy (a wardrobe; a body so marked) can't be lifted at all (Exult's "too heavy" pointer) - only opened where it stands.</summary>
    private bool IsTooHeavyToLift(PlacedItemInstance item) =>
        item.Category == ReferencedItemCategory.Container
        && ContainerDef(item.ItemId) is { CanBePicked: false };
}
