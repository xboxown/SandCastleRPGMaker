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
/// inside it - and can be identified with a single click. AI, schedules,
/// talking and combat come later; for now they stand where they were put.
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
            var mapCharacter = new MapCharacter { Placement = placement, Character = character, Size = ActorSizing.Resolve(character, _poseDatabase, _sprites) };
            _mapCharacters.Add(mapCharacter);
            // Standing on top of whatever solid thing is under it, like the player at the Start Position.
            mapCharacter.Lift = StandingLiftAt(CharacterFootprint(mapCharacter));
            mapCharacter.Visual = BuildCharacterVisual(mapCharacter);
            GameCanvas.Children.Add(mapCharacter.Visual);
            PositionCharacterVisual(mapCharacter);
        }
    }

    private FrameworkElement BuildCharacterVisual(MapCharacter mapCharacter)
    {
        var placement = mapCharacter.Placement;
        var pose = placement.PoseId >= 0 ? _poseDatabase.Poses.FirstOrDefault(p => p.Id == placement.PoseId) : _poseDatabase.Poses.FirstOrDefault(p => p.Name == "Idle");
        SpriteEntry? sprite = null;
        double volume = 0;
        if (pose is not null && mapCharacter.Character.PoseSprites.TryGetValue(pose.Id, out var assignment) && assignment.Resolve(placement.Facing) is { } link)
        {
            sprite = _sprites.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName);
            volume = assignment.Muted ? 0 : assignment.Volume / 100.0;
        }
        if (sprite is not null && (sprite.Frames.Count > 0 || !string.IsNullOrEmpty(sprite.FilePath)))
        {
            var (width, height) = QuantizedSpriteSize(sprite);
            var image = new Image { Width = width, Height = height, IsHitTestVisible = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources, volume));
            return image;
        }
        // No art for that pose/facing yet: an honest labelled marker, the same way items without art show.
        return new Border
        {
            Width = _tileWidth, Height = _tileHeight, Background = new SolidColorBrush(Color.FromArgb(200, 160, 60, 160)), IsHitTestVisible = true,
            Child = new TextBlock { Text = "C", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    /// <summary>Drawn the way the player is: bottom-center on its cell, shifted up-left 4px per lift, ordered by position and lift.</summary>
    private void PositionCharacterVisual(MapCharacter mapCharacter)
    {
        if (mapCharacter.Visual is not { } visual) return;
        var placement = mapCharacter.Placement;
        PositionQuantizedVisual(visual, placement.X, placement.Y, visual.Width, visual.Height);
        Canvas.SetLeft(visual, Canvas.GetLeft(visual) - mapCharacter.Lift * LiftPixelShift);
        Canvas.SetTop(visual, Canvas.GetTop(visual) - mapCharacter.Lift * LiftPixelShift);
        Panel.SetZIndex(visual, ComputeDepthZIndex(placement.X + 0.5, placement.Y + 0.5, mapCharacter.Lift));
    }

    /// <summary>An actor's footprint: centered under its feet on its cell, its own size - the same rule as the player's.</summary>
    private UnitRect CharacterFootprint(MapCharacter mapCharacter)
    {
        double feetX = mapCharacter.Placement.X * _tileWidth + _tileWidth / 2.0;
        double feetY = (mapCharacter.Placement.Y + 1) * _tileHeight;
        int left = (int)Math.Round((feetX - mapCharacter.Size.X * SpriteSizing.BaseUnit / 2.0) / SpriteSizing.BaseUnit);
        int top = (int)Math.Round((feetY - mapCharacter.Size.Y * SpriteSizing.BaseUnit) / SpriteSizing.BaseUnit);
        return new UnitRect(left, top, mapCharacter.Size.X, mapCharacter.Size.Y);
    }

    /// <summary>Every placed character as a physics body: solid (Exult actors block), never holding things up, never stacked on.</summary>
    private IEnumerable<Body> CharacterBodies() => _mapCharacters.Select(mc =>
    {
        var foot = CharacterFootprint(mc);
        return new Body(mc.Placement, foot.Left, foot.Top, foot.Width, foot.Depth, mc.Lift, mc.Size.Z, Solid: true, Elevates: false, Stackable: false);
    });

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
    }
}
