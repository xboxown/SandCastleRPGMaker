// Ultima 8 engine (U8GameWindow) - the weapon in the hand, as the Ultima 7 engine draws it (GameTestWindow.WeaponInHand.cs), on the Ultima 8 view.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A weapon held in the hand, as Exult draws it (Actor::paint_weapon, figure_weapon_pos): every frame, for each character
/// holding a weapon that has a look in the hand (Object Editor; a sprite in the Sprite Editor's Weapon in hand group),
/// the frame they show now gives the hand point and which weapon frame (its own choice, or: held - or, through an attack
/// pose, the swing); the weapon frame is drawn with its grip on the hand, over the body (or behind it where the frame
/// says so). Exult's frames (System Settings > Weapon in hand): held, the swing pointing south, east, north through an
/// attack (Exult's 4, 3, 2), mirrored for a character facing east or west (its reflected frames). Smooth: the direction's
/// own held and swing frames. A frame with no hand point shows no weapon (lying down: Exult's 255).
/// </summary>
public partial class U8GameWindow
{
    private readonly Dictionary<UIElement, Image> _weaponsInHand = new(ReferenceEqualityComparer.Instance);

    private void UpdateWeaponsInHand()
    {
        var shown = new HashSet<UIElement>(ReferenceEqualityComparer.Instance);
        if (_player is not null && _playerImage is { } playerImage) DrawWeaponInHand(_player, playerImage, _playerAnimator, _playerFacing, _currentAppliedPose?.Name, shown);
        foreach (var mapCharacter in _mapCharacters)
            if (mapCharacter.Visual is Image image && _worldAnimators.TryGetValue(image, out var animator))
                DrawWeaponInHand(mapCharacter.Character, image, animator, mapCharacter.Facing, mapCharacter.PoseName, shown);
        foreach (var (owner, weaponImage) in _weaponsInHand.ToList())
            if (!shown.Contains(owner)) { GameCanvas.Children.Remove(weaponImage); _weaponsInHand.Remove(owner); }
    }

    private void DrawWeaponInHand(CharacterDefinition who, Image body, SpriteAnimator? animator, SpriteDirection facing, string? poseName, HashSet<UIElement> shown)
    {
        if (animator is null || body.Visibility != Visibility.Visible || !GameCanvas.Children.Contains(body) || IsDead(who)) return;
        if (WieldedWeapon(who)?.Def is not { } weapon || !weapon.Weapon.InHandSprite.IsSet) return;
        var held = ObjectLooks.Find(_sprites, weapon.Weapon.InHandSprite.SpriteGroup, weapon.Weapon.InHandSprite.SpriteName);
        if (held is null || held.Frames.Count == 0) return;
        var sprite = animator.Sprite;
        int index = animator.FrameIndex;
        if (index < 0 || index >= sprite.Frames.Count) return;
        var frame = sprite.Frames[index];
        if (!frame.HasHand || FrameCache.Get(frame.FilePath) is not { } bodyPicture) return; // no weapon in this frame
        var (weaponFrame, mirrored) = WeaponFrameFor(frame, index, sprite.Frames.Count, facing, IsAttackPose(weapon, poseName));
        var heldFrame = held.Frames[Math.Clamp(weaponFrame, 0, held.Frames.Count - 1)];
        if (FrameCache.Get(heldFrame.FilePath) is not { } weaponPicture) return;

        // Where the body's picture is drawn, and at what size (fitted in its box, as the body's image shows it).
        var drawn = GameCanvas.ScreenRectOf(body); // the Ultima 8 view: where it is drawn on the screen
        double scale = Math.Min(drawn.Width / Math.Max(1, bodyPicture.PixelWidth), drawn.Height / Math.Max(1, bodyPicture.PixelHeight));
        double insetX = (drawn.Width - bodyPicture.PixelWidth * scale) / 2, insetY = (drawn.Height - bodyPicture.PixelHeight * scale) / 2;
        double handX = drawn.X + insetX + frame.HandX * scale, handY = drawn.Y + insetY + frame.HandY * scale;
        double gripX = (heldFrame.GripX >= 0 ? heldFrame.GripX : weaponPicture.PixelWidth / 2.0) * scale;
        double gripY = (heldFrame.GripY >= 0 ? heldFrame.GripY : weaponPicture.PixelHeight / 2.0) * scale;

        if (!_weaponsInHand.TryGetValue(body, out var image))
        {
            image = new Image { IsHitTestVisible = false, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            U8ProjectionCanvas.SetPlacement(image, U8Placement.Screen); // placed on the screen, by the body as drawn
            GameCanvas.Children.Add(image);
            _weaponsInHand[body] = image;
        }
        image.Source = weaponPicture;
        image.Width = weaponPicture.PixelWidth * scale;
        image.Height = weaponPicture.PixelHeight * scale;
        Canvas.SetLeft(image, handX - gripX);
        Canvas.SetTop(image, handY - gripY);
        // Exult's reflected frames swap x and y (about the grip): a character facing east or west.
        image.RenderTransform = mirrored ? WeaponInHandLook.MirrorAboutGrip(gripX, gripY) : Transform.Identity;
        image.Opacity = body.Opacity;
        Panel.SetZIndex(image, Panel.GetZIndex(body) + (frame.WeaponBehind ? -1 : 1));
        shown.Add(body);
    }

    /// <summary>An attack pose (the weapon's own, or the usual ones): the weapon swings through it.</summary>
    private static bool IsAttackPose(WorldObject weapon, string? poseName) =>
        poseName is { Length: > 0 } name && (name == weapon.Weapon.AttackPose || name == weapon.Weapon.ShootPose || name is "Attacking Melee" or "Shooting" or "Attack");

    /// <summary>Which weapon frame a character frame shows, and whether mirrored (WeaponInHandLook - the Sprite Editor's preview shows the same).</summary>
    private (int Frame, bool Mirrored) WeaponFrameFor(SpriteFrame frame, int index, int count, SpriteDirection facing, bool attacking) =>
        WeaponInHandLook.FrameFor(_systemSettings.WeaponInHand, frame, index, count, facing, attacking);
}
