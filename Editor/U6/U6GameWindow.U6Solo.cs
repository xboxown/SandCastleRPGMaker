// Ultima 6 engine (U6GameWindow): solo mode - the member picked is the one the player moves - Ultima 6 projects only.
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6's solo mode (Player::set_actor, Party::set_in_combat... - Nuvie): 1-9 puts that member under the player's keys; the
/// avatar and everyone else stay where they are (no one follows). 0 (party mode) gives the avatar back and the party follows it
/// again. The controlled member walks, gets, uses, fights and casts; only the avatar talks ("Not in solo mode."). If the one
/// controlled dies, the avatar is taken back first. Saving ends solo mode first. Done as the Ultima 8 engine's control of
/// another character: the two swap places on the map - the one left becomes a character standing there, the one taken is moved
/// with the keys.
/// </summary>
public partial class U6GameWindow
{
    /// <summary>The avatar while another member is controlled (null: the avatar is the one moved).</summary>
    private CharacterDefinition? _avatar;
    private bool _avatarWasInParty;
    private readonly Dictionary<CharacterDefinition, PlacedCharacter> _soloPlacements = new(ReferenceEqualityComparer.Instance);

    /// <summary>The avatar, whoever is being moved.</summary>
    private CharacterDefinition? Avatar => _avatar ?? _player;
    private bool ControllingMember => _avatar is not null && _player != _avatar;

    /// <summary>The one moved becomes <paramref name="target"/> (a member standing on the map), the one moved before stays where it stood.</summary>
    private bool SoloSwapTo(CharacterDefinition target)
    {
        if (_player is null) return false;
        if (target == _player) return true;
        if (MapCharacterOf(target) is not { } mc) return false;
        _avatar ??= _player;
        var avatar = _avatar;
        var old = _player;

        // What the one moved was doing stops.
        if (PlayerSeated) StandUp(old);
        if (_seats.ContainsKey(target)) StandUp(target);
        CancelPlayerWalk();
        CancelPendingSit();
        _heldDirectionKeys.Clear();
        _playerTarget = null;
        _playerTargetThing = null;
        StopWalking(mc);
        _followers.Remove(mc);
        _scheduleUntil.Remove(mc);

        // The one left: onto the map where it stood.
        double px = _playerPixelX, py = _playerPixelY;
        int pz = _playerZ;
        var facing = _playerFacing;
        if (old == avatar) { _avatarWasInParty = avatar.IsInParty; avatar.IsInParty = true; }
        var place = _soloPlacements.Remove(old, out var kept) ? kept : new PlacedCharacter { Id = NextSoloPlacementId(), CharacterId = old.Id };
        place.X = _playerX;
        place.Y = _playerY;
        place.Floor = MapFloors.FloorOfLift(Math.Max(0, pz));
        place.Dead = false;
        place.SavedCharacterIndex = SavedIndexOf(old);
        _map.Characters.Add(place);
        var left = BringOntoMap(old, place);
        left.PixelX = px;
        left.PixelY = py;
        left.Lift = pz;
        left.ShownSprite = null;
        SetCharacterPose(left, _combatMode ? "Combat Stand" : "Idle", facing);
        PositionCharacterVisual(left);

        // The one taken: off the map, into the player's place.
        if (mc.Visual is { } visual) { UnregisterWorldAnimator(visual); GameCanvas.Children.Remove(visual); }
        _mapCharacters.Remove(mc);
        _map.Characters.Remove(mc.Placement);
        _soloPlacements[target] = mc.Placement;
        if (target == avatar) { avatar.IsInParty = _avatarWasInParty; _avatar = null; }
        _player = target;
        _playerSize = mc.Size;
        PlacePlayer((mc.Placement.X, mc.Placement.Y), mc.Lift);
        _playerPixelX = mc.PixelX;
        _playerPixelY = mc.PixelY;
        _playerFacing = mc.Facing;
        _playerImage.Visibility = Visibility.Visible;
        ApplyPoseSprite(_idlePose);
        SnapPlayerPosition();
        UpdateCamera();
        SoakLog($"solo: {target.Name} moved by the player");
        return true;
    }

    private int NextSoloPlacementId()
    {
        int most = _map.Characters.Count == 0 ? 0 : _map.Characters.Max(p => p.Id);
        if (_soloPlacements.Count > 0) most = Math.Max(most, _soloPlacements.Values.Max(p => p.Id));
        return most + 1;
    }

    /// <summary>The avatar moved again (party mode, a save, the controlled member's death).</summary>
    private void SoloBackToAvatar()
    {
        if (ControllingMember && _avatar is { } avatar) SoloSwapTo(avatar);
    }
}
