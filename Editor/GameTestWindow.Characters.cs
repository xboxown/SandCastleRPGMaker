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
        public NpcState Npc = new();
    }

    private readonly List<MapCharacter> _mapCharacters = new();
    private PoseDatabase _poseDatabase = new();

    /// <summary>Called once at startup, after the player is set up (the character list is the game's own copy by then).</summary>
    private void BuildCharacterVisuals()
    {
        if (_characters is null) return;
        foreach (var placement in _map.Characters)
        {
            var character = _characters.Characters.FirstOrDefault(c => c.Id == placement.CharacterId);
            if (character is null || character.IsTestPlayer) continue; // the main player is the one you control, not a placed character
            var mapCharacter = new MapCharacter
            {
                Placement = placement, Character = character, Size = ActorSizing.Resolve(character, _poseDatabase, _sprites),
                PixelX = placement.X * _tileWidth + _tileWidth / 2.0,
                PixelY = placement.Y * _tileHeight + _tileHeight / 2.0,
                Facing = placement.Facing
            };
            _mapCharacters.Add(mapCharacter);
            // Standing on top of whatever solid thing is under it, like the player at the Start Position.
            mapCharacter.Lift = StandingLiftAt(CharacterFootprint(mapCharacter));
            mapCharacter.Visual = BuildCharacterVisual(mapCharacter);
            GameCanvas.Children.Add(mapCharacter.Visual);
            PositionCharacterVisual(mapCharacter);
        }
        StartSchedules(); // Exult: every NPC takes up the schedule for the time of day
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
        mapCharacter.Facing = facing;
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
    private IEnumerable<Body> CharacterBodies() => _mapCharacters.Select(mc =>
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
        var result = VisualTreeHelper.HitTest(GameCanvas, world);
        for (DependencyObject? current = result?.VisualHit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            var match = _mapCharacters.FirstOrDefault(mc => ReferenceEquals(mc.Visual, current));
            if (match is not null) return match.Placement;
        }
        return null;
    }

    private void IdentifyCharacter(PlacedCharacter placement)
    {
        var mapCharacter = _mapCharacters.FirstOrDefault(mc => mc.Placement == placement);
        if (mapCharacter?.Visual is null) return;
        ShowIdentifyLabel(mapCharacter.Character.Name, placement.X, placement.Y, mapCharacter.Visual);
        // Exult's label is the name alone; faction standing and current activity go to the status line so they can be checked while testing.
        StatusText.Text = $"{mapCharacter.Character.Name}: {DescribeFactionStanding(mapCharacter.Character)}; {DescribeActivity(mapCharacter)}";
    }
}
