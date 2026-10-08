using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The object types added in build 248, in play: what using each one does
/// (on the map, or carried in a paperdoll, backpack list or container
/// window), their live state, and the things they all share - a "used
/// thing" that is either a placed object or a carried item, targets chosen
/// with Exult's Get_click (UI_click_on_item), and words said over a thing
/// (item_say). Each type's own behaviour is in the partial files named in
/// its section, ported from Exult's own item usecode where it has one.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>An object being used: placed on the map (a MapObjectOrigin) or carried (a slot, the backpack list, a container window).</summary>
    private sealed record Used(WorldObject Obj, DragOrigin Source)
    {
        public ObjectInstance? Placed => (Source as MapObjectOrigin)?.Obj;
        public InventoryItemInstance? Carried => Source switch
        {
            SlotOrigin s => s.Item,
            BackpackOrigin b => b.Item,
            ContainerOrigin c => c.Item,
            _ => null
        };
        /// <summary>Who holds it, if a character carries it directly (slot or backpack list).</summary>
        public CharacterDefinition? Holder => Source switch
        {
            SlotOrigin s => s.Character,
            BackpackOrigin b => b.Character,
            _ => null
        };
    }

    /// <summary>Where on the map (world pixels) the last target click was - Get_click's x, y, for a spill on the floor.</summary>
    private Point _lastTargetClickWorld;

    private bool IsPartyOrPlayer(CharacterDefinition c) => c == _player || c.IsInParty;

    // ================= Starting state =================

    /// <summary>At the start of play (after InitializeObjectStates): plants at their starting stage, keychains holding their keys, liquid containers filled, roofs up at their height.</summary>
    private void InitializeNewTypeStates()
    {
        foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            if (DefinitionOf(instance) is not { } obj) continue;
            switch (obj.Type)
            {
                case ObjectType.Plant:
                    instance.FrameRuntime = Math.Clamp(instance.StartStage ?? 0, 0, Math.Max(0, obj.Plant.Stages.Count - 1));
                    break;
                case ObjectType.Keychain:
                    instance.KeyIdsRuntime ??= obj.Keychain.StartingKeyIds.ToList();
                    instance.FrameRuntime = Math.Min(instance.KeyIdsRuntime.Count, 4);
                    break;
                case ObjectType.LiquidContainer:
                    instance.LiquidIdRuntime ??= ObjectLooks.StartLiquid(obj, instance);
                    break;
            }
        }
    }

    // ================= Using them =================

    /// <summary>Double-clicking a placed object of a build 248 type. True = handled.</summary>
    private bool UseNewTypeOnMap(ObjectInstance instance, WorldObject obj)
    {
        switch (obj.Type)
        {
            case ObjectType.Window: UseWindow(instance, obj); return true;
            case ObjectType.Plant: UsePlant(instance, obj, toolKind: null); return true;
            case ObjectType.Lever: PullLever(instance, obj); return true;
            case ObjectType.Stairs: UseStairs(instance, obj); return true;
            case ObjectType.CraftingStation: UseStation(instance, obj, toolKind: null); return true;
            case ObjectType.Explosive: if (!FireObjectEvent(instance, obj, BookTriggerConditionBased.ByUseItem)) LightFuse(instance, obj, byPlayer: true, forced: false); return true;
        }
        return UseThing(new Used(obj, new MapObjectOrigin(instance)));
    }

    /// <summary>Double-clicking a carried object of a build 248 type. True = handled.</summary>
    private bool UseNewTypeCarried(InventoryItemInstance item, WorldObject obj, DragOrigin source) => UseThing(new Used(obj, source));

    /// <summary>The types that work the same placed or carried.</summary>
    private bool UseThing(Used u)
    {
        switch (u.Obj.Type)
        {
            case ObjectType.Keychain: UseKeychain(u); return true;
            case ObjectType.Sextant: UseSextant(u); return true;
            case ObjectType.Map: UseMap(u); return true;
            case ObjectType.Food: UseFoodItem(u); return true;
            case ObjectType.Potion: UsePotion(u); return true;
            case ObjectType.Spellbook: UseSpellbook(u); return true;
            case ObjectType.UseableObject: UseUseable(u); return true;
            case ObjectType.Clock: UseClock(u); return true;
            case ObjectType.Tool: UseTool(u); return true;
            case ObjectType.MusicalInstrument: UseInstrument(u); return true;
            case ObjectType.Bedroll: UseBedroll(u); return true;
            case ObjectType.LiquidContainer: UseLiquid(u); return true;
        }
        return false;
    }

    /// <summary>Its By Use event first: a page with commands takes over (as a door's or a light's does). True = it did.</summary>
    private bool FireUseEvent(Used u) => u.Placed is { } placed
        ? FireObjectEvent(placed, u.Obj, BookTriggerConditionBased.ByUseItem)
        : u.Carried is { } carried && TryFireEventPages(carried, u.Obj.EventPages, BookTriggerConditionBased.ByUseItem, -1, $"\"{u.Obj.Name}\" fired");

    // ================= Its state, placed or carried =================

    /// <summary>What a liquid container holds (a liquid Food's id), or -1 if it's empty.</summary>
    private int LiquidOf(Used u) => (u.Placed?.LiquidIdRuntime ?? u.Carried?.LiquidIdRuntime) ?? ObjectLooks.StartLiquid(u.Obj, u.Placed);

    private void SetLiquid(Used u, int liquidId)
    {
        if (u.Placed is { } placed) placed.LiquidIdRuntime = liquidId;
        if (u.Carried is { } carried) carried.LiquidIdRuntime = liquidId;
        RefreshUsed(u);
    }

    private List<int> KeysOf(Used u)
    {
        if (u.Placed is { } placed) return placed.KeyIdsRuntime ??= u.Obj.Keychain.StartingKeyIds.ToList();
        if (u.Carried is { } carried) return carried.KeyIdsRuntime ??= u.Obj.Keychain.StartingKeyIds.ToList();
        return new List<int>();
    }

    private int ChargesOf(Used u)
    {
        int left = u.Placed?.ChargesRuntime ?? u.Carried?.ChargesRuntime ?? -1;
        return left < 0 ? u.Obj.Useable.Charges : left;
    }

    private void SetCharges(Used u, int left)
    {
        if (u.Placed is { } placed) placed.ChargesRuntime = left;
        if (u.Carried is { } carried) carried.ChargesRuntime = left;
    }

    private bool IsSpent(Used u) => u.Placed?.IsSpentRuntime ?? u.Carried?.IsSpentRuntime ?? false;

    private void SetSpent(Used u)
    {
        if (u.Placed is { } placed) placed.IsSpentRuntime = true;
        if (u.Carried is { } carried) carried.IsSpentRuntime = true;
        RefreshUsed(u);
    }

    private void SetFrame(Used u, int frame)
    {
        if (u.Placed is { } placed) placed.FrameRuntime = frame;
        if (u.Carried is { } carried) carried.FrameRuntime = frame;
        RefreshUsed(u);
    }

    /// <summary>Its picture redrawn for its new state - on the map, or in whichever gump shows it.</summary>
    private void RefreshUsed(Used u)
    {
        if (u.Placed is { } placed) RefreshObjectLook(placed);
        else RefreshAfterMove();
    }

    /// <summary>Whoever uses it: the character holding it, else the main character.</summary>
    private CharacterDefinition? UserOf(Used u) => u.Holder is { } holder && !IsDead(holder) ? holder : _player;

    /// <summary>item_say: words over the thing - over it on the map, over its picture in a gump.</summary>
    private void SayOver(Used u, string text, int milliseconds = 2500)
    {
        if (u.Placed is { } placed) { ShowFloatingTextOver(VisualOf(placed), text, milliseconds); return; }
        if (u.Carried is { } carried && GumpVisualOf(carried) is { Parent: Canvas canvas } visual) { ShowGumpLabel(canvas, text, Canvas.GetLeft(visual), Canvas.GetTop(visual)); return; }
        if (_player is not null) ShowFloatingText(_player, text, milliseconds);
    }

    /// <summary>avatarBark: the main character says it.</summary>
    private void AvatarBark(string text)
    {
        if (_player is not null) ShowFloatingText(_player, text, 2500);
    }

    /// <summary>randomPartyBark with the given words: a random party member who can speak says one of them.</summary>
    private void PartySay(params string[] lines)
    {
        if (lines.Length == 0) return;
        var party = AllLivingCharacters().Where(c => IsPartyOrPlayer(c) && c.IsHuman && !IsIncapacitated(c)).ToList();
        if (party.Count > 0) ShowFloatingText(party[Rng.Next(party.Count)], lines[Rng.Next(lines.Length)], 3000);
    }

    // ================= Targets (Get_click) =================

    /// <summary>The character a target click picked: the main character, someone on the map, or the owner of a paperdoll clicked.</summary>
    private CharacterDefinition? CharacterTargeted(InteractionTarget? target) => target switch
    {
        PlayerTarget => _player,
        MapEntityTarget { Entity: PlacedCharacter placed } => _mapCharacters.FirstOrDefault(mc => mc.Placement == placed)?.Character,
        SlotTarget slot => slot.Gump.Character,
        BackpackRowTarget row => row.Gump.Character,
        _ => null
    };

    /// <summary>The placed object a target click picked, with its Object Editor entry.</summary>
    private (ObjectInstance Instance, WorldObject Obj)? ObjectTargeted(InteractionTarget? target) =>
        target is MapEntityTarget { Entity: ObjectInstance instance } && _map.Objects.Contains(instance) && DefinitionOf(instance) is { } obj ? (instance, obj) : null;

    /// <summary>The map cell of the last target click.</summary>
    private (int X, int Y) LastTargetCell() =>
        (Math.Clamp((int)Math.Floor(_lastTargetClickWorld.X / _tileWidth), 0, _map.Width - 1), Math.Clamp((int)Math.Floor(_lastTargetClickWorld.Y / _tileHeight), 0, _map.Height - 1));

    // ================= Things appearing and going =================

    /// <summary>A new object put on the map at a cell (produce, an empty bottle, shards, a spill), resting on whatever is there.</summary>
    private ObjectInstance? SpawnObjectAt(int objectId, int quantity, int cellX, int cellY)
    {
        if (objectId < 0 || _objects.Objects.FirstOrDefault(o => o.Id == objectId) is not { } def) return null;
        // Joins a stack of the same thing already there (Game_object::drop).
        if (CanCombine(def) && _map.Objects.FirstOrDefault(o => o.ObjectId == objectId && o.ContainerInstanceId < 0 && o.X == cellX && o.Y == cellY && o.Quantity + quantity <= ObjectTypes.MaxQuantity) is { } stack)
        {
            stack.Quantity += quantity;
            RefreshObjectLook(stack);
            return stack;
        }
        var instance = new ObjectInstance { ObjectId = objectId, Quantity = Math.Max(1, quantity), X = cellX, Y = cellY, ContainerInstanceId = -1, InstanceId = _map.GetNextInstanceId() };
        AddMapEntity(null, instance);
        SettleWorld();
        RefreshAfterMove();
        return instance;
    }

    /// <summary>A placed object gone from the world.</summary>
    private void RemoveMapObject(ObjectInstance instance)
    {
        if (_objectVisuals.Remove(instance, out var gone)) { UnregisterWorldAnimator(gone); GameCanvas.Children.Remove(gone); }
        _map.Objects.Remove(instance);
        if (ReferenceEquals(_playerTargetThing, instance)) _playerTargetThing = null;
    }

    /// <summary>The cell a used thing is at: its own on the map, else the main character's.</summary>
    private (int X, int Y) CellOfUsed(Used u) => u.Placed is { } placed ? (placed.X, placed.Y) : (_playerX, _playerY);

    // ================= Per frame and per game minute =================

    private double _lastNewTypeMinute = -1;

    /// <summary>Plants growing, fuses burning, buttons springing back, crafting work, the special light, and roofs - each frame of play.</summary>
    private void UpdateNewTypeObjects(double elapsedMs)
    {
        double now = TotalMinutes();
        if (_lastNewTypeMinute < 0) _lastNewTypeMinute = now;
        if (now > _lastNewTypeMinute)
        {
            double minutes = now - _lastNewTypeMinute;
            _lastNewTypeMinute = now;
            GrowPlants(minutes);
            if (_specialLightMinutes > 0) _specialLightMinutes = Math.Max(0, _specialLightMinutes - minutes);
        }
        UpdateFuses(elapsedMs);
        UpdateButtons(elapsedMs);
        UpdateCraftingWork(elapsedMs);
        UpdateRoofs();
    }

    // ================= Invisibility (Exult's invisible flag) =================

    /// <summary>Combat_schedule::stop_attacking_invisible: those who can't see invisible stop fighting them.</summary>
    private void StopAttackingInvisible(CharacterDefinition character)
    {
        foreach (var mapCharacter in _mapCharacters)
        {
            if (mapCharacter.Npc.Target != character || CanSee(mapCharacter.Character, character)) continue;
            mapCharacter.Npc.Target = null;
        }
        if (_playerTarget == character && character != _player && !IsPartyOrPlayer(character)) _playerTarget = null;
    }

    /// <summary>Exult paints the invisible as a shimmer if they're in the party, and not at all otherwise.</summary>
    private void ShowInvisibility(CharacterDefinition character)
    {
        bool invisible = Has(character, Ailment.Invisible);
        if (character == _player) { _playerImage.Opacity = invisible ? 0.4 : 1; return; }
        if (MapCharacterOf(character)?.Visual is { } visual) visual.Opacity = !invisible ? 1 : character.IsInParty ? 0.4 : 0;
    }
}
