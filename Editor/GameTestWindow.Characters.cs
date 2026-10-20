using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Characters placed on the map in the Map Editor (Mode > Characters),
/// brought into the game: each stands on its cell in its chosen pose and
/// facing (animated, frozen with everything else while a gump is open), is
/// a solid body with its own Exult-style size (Character Editor SizeX/Y/Z)
/// - so, as Exult actors do, it blocks walking and nothing can be dropped
/// inside it - and can be identified with a single click. Characters with
/// a schedule walk about following it (GameTestWindow.Schedules.cs); the
/// position here is continuous, like the player's, so they walk smoothly.
/// The map is the game's own copy, so moving them never touches the project.
/// </summary>
public partial class GameTestWindow
{
    private sealed class MapCharacter
    {
        public required PlacedCharacter Placement;
        public required CharacterDefinition Character;
        public required (int X, int Y, int Z) Size;
        public int Lift;
        public FrameworkElement? Visual;
        /// <summary>Feet position in world pixels - the same anchor the player uses (cell centre horizontally, cell centre + half a cell = the feet line).</summary>
        public double PixelX, PixelY;
        public SpriteDirection Facing;
        /// <summary>The sprite now showing ("group/name"), so a pose that resolves to the same sprite doesn't restart its animation.</summary>
        public string? ShownSprite;
        /// <summary>The pose it's in now ("Sleeping", "Sit"...) - Exult's frame check (asleep in bed: the sleep frame).</summary>
        public string PoseName = "Idle";
        public NpcState Npc = new();
    }

    private readonly List<MapCharacter> _mapCharacters = new();
    private PoseDatabase _poseDatabase = new();

    /// <summary>Called once at startup, after the player is set up (the character list is the game's own copy by then).</summary>
    private void BuildCharacterVisuals()
    {
        if (_characters is null) return;
        // Bodies left by a save keep their numbers: new ones are numbered after them.
        if (_map.Items.Count > 0) _nextContainerRuntimeInstanceId = Math.Max(_nextContainerRuntimeInstanceId, _map.Items.Max(i => i.InstanceId) + 1);
        var placed = new HashSet<CharacterDefinition>(ReferenceEqualityComparer.Instance);
        var deadOnes = new List<(PlacedCharacter Placement, CharacterDefinition Character)>();
        foreach (var placement in _map.Characters)
        {
            // From a save: the very character this placement was (each copy its own).
            int saved = placement.SavedCharacterIndex;
            var character = saved >= 0 && saved < _characters.Characters.Count && _characters.Characters[saved].Id == placement.CharacterId ? _characters.Characters[saved] : null;
            if (character is not null) placed.Add(character);
            else
            {
                character = _characters.Characters.FirstOrDefault(c => c.Id == placement.CharacterId);
                if (character is null || character.IsTestPlayer) continue; // the main player is the one you control, not a placed character
                // The same character placed more than once: every copy after the first is its own instance of it (a prefab),
                // with its own health, inventory, ailments and fights - killing one never kills the others.
                if (!placed.Add(character)) character = CopyOfCharacter(character);
            }
            if (character.IsTestPlayer) continue;
            if (placement.Dead) { deadOnes.Add((placement, character)); continue; } // died before (a save, a world come back to): only its body is here
            var mapCharacter = new MapCharacter
            {
                Placement = placement, Character = character, Size = ActorSizing.Resolve(character, _poseDatabase, _sprites),
                PixelX = placement.X * _tileWidth + _tileWidth / 2.0,
                PixelY = placement.Y * _tileHeight + _tileHeight / 2.0,
                Facing = placement.Facing
            };
            _mapCharacters.Add(mapCharacter);
            // Standing on top of whatever solid thing is under it, like the player at the Start Position.
            mapCharacter.Lift = StandingLiftOnFloor(CharacterFootprint(mapCharacter), placement.Floor, placement); // on its own floor, not counting its own body (it's already in the list)
            if (character.Movement == MovementKind.Air) mapCharacter.Lift = CruiseLift(mapCharacter); // a flier is up in the air
            mapCharacter.Visual = BuildCharacterVisual(mapCharacter);
            GameCanvas.Children.Add(mapCharacter.Visual);
            PositionCharacterVisual(mapCharacter);
        }
        InitializeVitals(); // health, food, ailments for the player and every character
        foreach (var (placement, character) in deadOnes)
        {
            _vitals[character] = new Vitals { Dead = true };
            if (_map.Items.FirstOrDefault(i => i.InstanceId == placement.BodyInstanceId && i.Category == ReferencedItemCategory.Container) is { } body)
                _bodies[character] = (body, placement); // Resurrect still brings it back from its body
        }
        StartSchedules(); // Exult: every NPC takes up the schedule for the time of day
        StartWeather(); // the map's weather (GameTestWindow.Weather.cs)
    }

    /// <summary>A separate copy of a character for another placement of it - a deep copy, so nothing it does in play touches the others.</summary>
    private CharacterDefinition CopyOfCharacter(CharacterDefinition source)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<CharacterDefinition>(System.Text.Json.JsonSerializer.Serialize(source))
            ?? throw new InvalidOperationException($"Couldn't copy \"{source.Name}\" for another placement.");
        copy.SyncNextInventoryInstanceIdAfterLoad();
        copy.Paperdoll.SyncNextCustomSlotIdAfterLoad();
        _characters!.Characters.Add(copy); // the game's own list (never the project's): same Id, so lookups by Id still find the first
        return copy;
    }

    private PoseDefinition? PoseById(int poseId) => _poseDatabase.Poses.FirstOrDefault(p => p.Id == poseId);
    private PoseDefinition? PoseByName(string name) => _poseDatabase.Poses.FirstOrDefault(p => p.Name == name);

    private SpriteEntry? ResolveCharacterSprite(CharacterDefinition character, PoseDefinition? pose, SpriteDirection facing, out double volume, out string? key)
    {
        volume = 0;
        key = null;
        if (pose is null || !character.PoseSprites.TryGetValue(pose.Id, out var assignment) || assignment.Resolve(facing) is not { } link) return null;
        volume = assignment.Muted ? 0 : assignment.Volume / 100.0;
        key = link.SpriteGroup + "/" + link.SpriteName;
        return _sprites.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName);
    }

    private FrameworkElement BuildCharacterVisual(MapCharacter mapCharacter)
    {
        var placement = mapCharacter.Placement;
        var pose = placement.PoseId >= 0 ? PoseById(placement.PoseId) : PoseByName("Idle");
        var sprite = ResolveCharacterSprite(mapCharacter.Character, pose, placement.Facing, out double volume, out string? key);
        if (sprite is not null && (sprite.Frames.Count > 0 || !string.IsNullOrEmpty(sprite.FilePath)))
        {
            var (width, height) = QuantizedSpriteSize(sprite);
            var image = new Image { Width = width, Height = height, IsHitTestVisible = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources, volume));
            mapCharacter.ShownSprite = key;
            return image;
        }
        // No art for that pose/facing yet: an honest labelled marker, the same way items without art show.
        return new Border
        {
            Width = _tileWidth, Height = _tileHeight, Background = new SolidColorBrush(Color.FromArgb(200, 160, 60, 160)), IsHitTestVisible = true,
            Child = new TextBlock { Text = "C", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    /// <summary>
    /// Shows a character in a pose (by name: Idle, Walking, Sleeping, Dancing...)
    /// facing a direction. A pose the character has no sprite for falls back to
    /// its Idle sprite; the same sprite already showing is left running.
    /// </summary>
    private void SetCharacterPose(MapCharacter mapCharacter, string poseName, SpriteDirection facing)
    {
        if (mapCharacter.ShownSprite is not null && mapCharacter.PoseName == poseName && mapCharacter.Facing == facing) return; // as it is already (every walking step asks)
        mapCharacter.Facing = facing;
        mapCharacter.PoseName = poseName;
        if (mapCharacter.Visual is not Image image) return; // the no-art marker has no poses to show
        var sprite = ResolveCharacterSprite(mapCharacter.Character, PoseByName(poseName), facing, out double volume, out string? key)
                     ?? ResolveCharacterSprite(mapCharacter.Character, PoseByName("Idle"), facing, out volume, out key);
        if (sprite is null || key == mapCharacter.ShownSprite) return;
        var (width, height) = QuantizedSpriteSize(sprite);
        image.Width = width;
        image.Height = height;
        UnregisterWorldAnimator(image);
        RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources, volume));
        mapCharacter.ShownSprite = key;
        PositionCharacterVisual(mapCharacter);
    }

    /// <summary>Drawn the way the player is: bottom-centre on its feet, shifted up-left 4px per lift, ordered by position and lift.</summary>
    private void PositionCharacterVisual(MapCharacter mapCharacter)
    {
        if (mapCharacter.Visual is not { } visual) return;
        Canvas.SetLeft(visual, mapCharacter.PixelX - visual.Width / 2 - mapCharacter.Lift * LiftPixelShift);
        Canvas.SetTop(visual, mapCharacter.PixelY + _tileHeight / 2.0 - visual.Height - mapCharacter.Lift * LiftPixelShift);
        Panel.SetZIndex(visual, ComputeDepthZIndex(mapCharacter.PixelX / _tileWidth, mapCharacter.PixelY / _tileHeight, mapCharacter.Lift));
        OrderCharacterAmongWalls(mapCharacter); // in front of or behind walls, by Exult's compare
        if (SeatedZIndex(mapCharacter.Character, Panel.GetZIndex(visual)) is int seated) Panel.SetZIndex(visual, seated); // on a chair or in bed: over it
    }

    /// <summary>An actor's footprint at a feet position: centred under its feet, its own size - the same rule as the player's.</summary>
    private UnitRect ActorFootprintAt(double pixelX, double pixelY, (int X, int Y, int Z) size)
    {
        double feetX = pixelX;
        double feetY = pixelY + _tileHeight / 2.0;
        int left = (int)Math.Round((feetX - size.X * SpriteSizing.BaseUnit / 2.0) / SpriteSizing.BaseUnit);
        int top = (int)Math.Round((feetY - size.Y * SpriteSizing.BaseUnit) / SpriteSizing.BaseUnit);
        return new UnitRect(left, top, size.X, size.Y);
    }

    private UnitRect CharacterFootprint(MapCharacter mapCharacter) => ActorFootprintAt(mapCharacter.PixelX, mapCharacter.PixelY, mapCharacter.Size);

    /// <summary>Every placed character as a physics body: solid (Exult actors block), never holding things up, never stacked on.</summary>
    private IEnumerable<Body> CharacterBodies() => _mapCharacters.Where(mc => !IsDead(mc.Character)).Select(mc => // a body on the ground isn't in anyone's way
    {
        var foot = CharacterFootprint(mc);
        return new Body(mc.Placement, foot.Left, foot.Top, foot.Width, foot.Depth, mc.Lift, mc.Size.Z, Solid: true, Elevates: false, Stackable: false);
    });

    /// <summary>The player as a body, for characters walking about: they can't walk through you any more than you can walk through them.</summary>
    private Body? PlayerBody()
    {
        if (_player is null) return null;
        var foot = PlayerFootprint();
        return new Body(_player, foot.Left, foot.Top, foot.Width, foot.Depth, _playerZ, _playerSize.Z, Solid: true, Elevates: false, Stackable: false);
    }

    /// <summary>The placed character whose visual is under this world point, if any.</summary>
    private PlacedCharacter? HitTestCharacter(Point world)
    {
        for (DependencyObject? current = TopHitInWorld(world); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var match = _mapCharacters.FirstOrDefault(mc => ReferenceEquals(mc.Visual, current));
            if (match is not null) return match.Placement;
        }
        return null;
    }

    /// <summary>
    /// The topmost thing drawn under a point of the world - skipping what isn't
    /// meant to be clicked (a status or hit outline, floating words) and what's
    /// hidden (a roof while you're under it). VisualTreeHelper.HitTest on its own
    /// doesn't skip them: an outline drawn over a poisoned character used to
    /// catch the click, so a second potion found nobody to drink it.
    /// </summary>
    private DependencyObject? TopHitInWorld(Point world)
    {
        DependencyObject? top = null;
        VisualTreeHelper.HitTest(GameCanvas,
            candidate => candidate is UIElement { IsHitTestVisible: false } or UIElement { Visibility: not Visibility.Visible }
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren
                : HitTestFilterBehavior.Continue,
            result => { top = result.VisualHit; return HitTestResultBehavior.Stop; },
            new PointHitTestParameters(world));
        return top;
    }

    private void IdentifyCharacter(PlacedCharacter placement)
    {
        var mapCharacter = _mapCharacters.FirstOrDefault(mc => mc.Placement == placement);
        if (mapCharacter?.Visual is null) return;
        ShowIdentifyLabel(mapCharacter.Character.Name, placement.X, placement.Y, mapCharacter.Visual);
        // Exult's label is the name alone; faction standing and current activity go to the status line so they can be checked while testing.
        StatusText.Text = $"{mapCharacter.Character.Name}: {DescribeFactionStanding(mapCharacter.Character)}; {DescribeActivity(mapCharacter)}; {DescribeVitals(mapCharacter.Character)}";
    }
}
