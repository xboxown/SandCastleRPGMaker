using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Combat mode against things, ported from Exult (Game_window::double_clicked,
/// Game_object::attacked / reduce_health, Container_game_object::remove_this):
///  - In combat mode a double-click attacks what it's on - except a party
///    member, a body, or an unlocked container, which are used as normal
///    (Exult opens unlocked doors and containers even in combat). A locked
///    container is attacked. What can't be reached flashes "blocked".
///  - Objects are always hit (Game_object::try_to_hit). Damage: 1..Strength/3
///    plus the weapon's points. Exult needs a weapon to hurt objects; there
///    are no weapons yet, so bare hands count as 1 point for now. Defense
///    (not in Exult) takes 1..Defense off each blow.
///  - A container that can be destroyed loses hit points; at 0 it breaks
///    (its destroyed sound plays) and everything in it falls out where it
///    stood (extract_contents). Books, objects and containers marked "Cannot
///    be destroyed" take no damage.
///  - Hitting or breaking something owned (Faction / Belong To) while a
///    sympathetic witness sees it is treated like theft: warnings, then
///    they come after you.
/// </summary>
public partial class GameTestWindow
{
    private object? _playerTargetThing; // a PlacedItemInstance or ObjectInstance being attacked
    private readonly Dictionary<PlacedItemInstance, int> _containerHp = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PlacedItemInstance> _struckOwnedContainers = new(ReferenceEqualityComparer.Instance);

    /// <summary>The pointer at rest: Green Select while choosing a target, the attack pointer in combat mode, the hand otherwise.</summary>
    private MousePointerKind RestingPointer => IsChoosingTarget ? MousePointerKind.GreenSelect : _combatMode ? MousePointerKind.Attack : MousePointerKind.Hand;

    /// <summary>Game_window::double_clicked in combat mode, for things on the map. True = attacked (handled); false = use it as normal.</summary>
    private bool TryAttackInCombatMode(object entity)
    {
        if (!_combatMode) return false;
        if (entity is PlacedItemInstance { Category: ReferencedItemCategory.Container } container
            && _containers.Containers.FirstOrDefault(c => c.Id == container.ItemId) is { } data
            && (data.IsBodyContainer || !IsLockedNow(container, data)))
            return false; // bodies and unlocked containers open, combat or not
        _playerTarget = null;
        _playerTargetThing = entity;
        StatusText.Text = $"Attacking {NameOfEntity(entity)}.";
        return true;
    }

    /// <summary>Within a blow's reach: next to the player (neighbouring cells) and within a few lifts.</summary>
    private bool ThingInReach(object entity)
    {
        var (x, y, z) = entity switch
        {
            PlacedItemInstance item => (item.X, item.Y, item.Z),
            ObjectInstance obj => (obj.X, obj.Y, obj.Z),
            _ => (-999, -999, 0)
        };
        return Math.Max(Math.Abs(x - _playerX), Math.Abs(y - _playerY)) <= 1 && Math.Abs(z - _playerZ) <= 4;
    }

    private bool ThingStillThere(object entity) => entity switch
    {
        PlacedItemInstance item => _map.Items.Contains(item),
        ObjectInstance obj => _map.Objects.Contains(obj),
        _ => false
    };

    /// <summary>One blow at a thing (Game_object::attacked): always hits.</summary>
    private void StrikeThing(object entity)
    {
        if (_player is null) return;
        var visual = VisualOf(entity);
        if (entity is ObjectInstance placedObject && DefinitionOf(placedObject) is { } objectDefinition)
        {
            int str3 = Stat(_player, BuiltInStatKind.Strength, 10) / 3;
            StrikeObject(placedObject, objectDefinition, (str3 > 0 ? 1 + Rng.Next(str3) : 0) + 1); // doors, junk, chairs...
            return;
        }
        // Exult effective_hps: a locked chest with a key of its own can't be broken (only quality 0 has hit points).
        if (entity is not PlacedItemInstance { Category: ReferencedItemCategory.Container } container
            || _containers.Containers.FirstOrDefault(c => c.Id == container.ItemId) is not { } data
            || !data.Destructible || data.IsBodyContainer || data.RequiredKeyItemId >= 0)
        {
            ShowFloatingTextOver(visual, "no effect"); // indestructible: Exult's reduce_health returns 0
            return;
        }

        int str = Stat(_player, BuiltInStatKind.Strength, 10) / 3;
        int damage = (str > 0 ? 1 + Rng.Next(str) : 0) + 1; // + 1: bare hands, until weapons exist
        if (data.Defense > 0) damage -= 1 + Rng.Next(data.Defense);
        if (damage <= 0)
        {
            ShowFloatingTextOver(visual, "no damage");
            return;
        }

        if (!_containerHp.TryGetValue(container, out int hp)) hp = Math.Max(1, data.HitPoints);
        hp -= damage;
        _containerHp[container] = hp;
        if (ShowHitNumbers) ShowFloatingTextOver(visual, $"-{damage}"); // "no effect" always shows; the numbers only with Show hit numbers

        // Someone's property: a crime if anyone sympathetic is watching (once per container, and again when it breaks).
        var owner = OwnerOf(ReferencedItemCategory.Container, container.ItemId);
        if (owner is { } owned && _struckOwnedContainers.Add(container)) Theft(owned.Owner, owned.FactionId);

        if (hp > 0) return;
        DestroyContainer(container, data);
        if (owner is { } brokenOwned) Theft(brokenOwned.Owner, brokenOwned.FactionId);
    }

    /// <summary>The container breaks: its sound, everything inside falls out where it stood, and it's gone.</summary>
    private void DestroyContainer(PlacedItemInstance container, ContainerData data)
    {
        if (data.DestructibleSoundResourceId >= 0) SoundPlayback.TryPlay(_resources, data.DestructibleSoundResourceId);
        foreach (var gump in _openContainers.Where(g => ReferenceEquals(g.PlacedItem, container)).ToList()) CloseContainerGump(gump);

        EnsureContainerSeeded(container, data); // never opened? its starting contents are in it all the same
        var spilled = container.Contents.ToList();
        container.Contents.Clear();

        if (_itemVisuals.Remove(container, out var visual))
        {
            UnregisterWorldAnimator(visual);
            GameCanvas.Children.Remove(visual);
        }
        _map.Items.Remove(container);
        _containerHp.Remove(container);

        foreach (var item in spilled)
        {
            var entity = CreateMapEntityFor(item);
            var placedItem = entity as PlacedItemInstance;
            var placedObject = entity as ObjectInstance;
            AddMapEntity(placedItem, placedObject);
            SetMapPosition(placedItem, placedObject, container.X, container.Y, 0, 0);
            SetZ(entity, container.Z);
        }
        SettleWorld();
        RefreshAfterMove();
        _playerTargetThing = null;
        StatusText.Text = $"\"{data.Name}\" breaks apart" + (spilled.Count > 0 ? $" - {spilled.Count} thing{(spilled.Count == 1 ? "" : "s")} fall out." : ".");
    }

    private void ShowFloatingTextOver(UIElement? visual, string text, int milliseconds = 1000)
    {
        if (visual is not FrameworkElement element) return;
        var label = GameTextLabel(text);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, Canvas.GetLeft(element) + element.Width / 2 - label.DesiredSize.Width / 2);
        Canvas.SetTop(label, Canvas.GetTop(element) - label.DesiredSize.Height - 2);
        Panel.SetZIndex(label, 1_000_000);
        GameCanvas.Children.Add(label);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); GameCanvas.Children.Remove(label); };
        timer.Start();
    }
}
