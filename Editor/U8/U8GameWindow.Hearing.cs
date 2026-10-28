// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Sounds come from where they are, as in Exult (audio/Audio.cc get_2d_position_for_tile,
/// AudioChannel::calculate2DVolume):
///  - heard from the middle of the view, the distance measured on the screen in 8px tiles
///    (Tile_coord::square_distance_screen_space: a lift up is half a tile up and left);
///  - further than 24 tiles (MAX_SOUND_FALLOFF): not heard at all - a clock ticking or a fight
///    across the map makes no sound here;
///  - nearer: quieter the further it is, distance = squared tiles * 256 / 24^2, and the view
///    being up high (the camera's character's lift) makes everything a little quieter
///    (SOUND_HEIGHT_FALLOFF 8 a lift); volume (256 - distance) / 256;
///  - to the left or right: balance (2 dx - lifts) * 32 / 5, the other side turned down.
/// Things on the map and characters (their animations' frame sounds, doors, traps, blows, work)
/// are heard this way; the main character's own sounds, the windows', music and the events' Play
/// Sound are heard as they are.
/// </summary>
public partial class U8GameWindow
{
    private const int MaxSoundFalloff = 24, SoundHeightFalloff = 8;

    /// <summary>How a sound at this drawn spot (world pixels, already up-left by its lift) is heard: its volume (0-1) and balance (-1 left .. 1 right), or null when too far.</summary>
    private (double Volume, double Balance)? HearingAt(double drawnX, double drawnY)
    {
        double listenX = _cameraX + ViewportWidth / 2.0, listenY = _cameraY + ViewportHeight / 2.0;
        var (dx, dy) = WrappedDelta(drawnX - listenX, drawnY - listenY); // the short way round a map that wraps around
        double halfX = dx / (SpriteSizing.BaseUnit / 2.0), halfY = dy / (SpriteSizing.BaseUnit / 2.0); // 2 dt - dz, in half tiles
        double squared = (halfX * halfX + halfY * halfY) / 4;
        if (squared > MaxSoundFalloff * MaxSoundFalloff) return null;
        double distance = squared * 256 / (MaxSoundFalloff * MaxSoundFalloff);
        int lift = _cameraFollow is { } followed && MapCharacterOf(followed) is { } on ? on.Lift : _playerZ;
        if (lift > 0) distance = Math.Min(256, distance + lift * SoundHeightFalloff);
        double balance = Math.Clamp(halfX * 32 / 5 / 256, -1, 1);
        return ((256 - distance) / 256, balance);
    }

    /// <summary>A picture on the map (a thing's, a character's): heard from its middle; null when off the map or too far.</summary>
    private (double Volume, double Balance)? HearingOf(UIElement? visual)
    {
        if (visual is not FrameworkElement element || !GameCanvas.IsAncestorOf(element)) return null;
        try
        {
            var middle = element.TranslatePoint(new Point(element.Width / 2, element.Height / 2), GameCanvas);
            return HearingAt(middle.X, middle.Y);
        }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>A sound from a character: where they're drawn (up and left by their lift); the main character's own is heard as it is.</summary>
    private void PlaySoundFrom(int resourceId, CharacterDefinition? who)
    {
        if (resourceId < 0) return;
        if (who is null || who == _player) { PlaySound(resourceId); return; }
        var (x, y) = PixelOf(who);
        if (double.IsNaN(x)) return; // not on this map
        int lift = LiftOf(who);
        if (HearingAt(x - lift * LiftPixelShift, y - lift * LiftPixelShift) is { } heard) SoundPlayback.TryPlayAt(_resources, resourceId, heard.Volume, heard.Balance);
    }

    /// <summary>A sound from a thing on the map (a door, a trap, a lever, a flying shot).</summary>
    private void PlaySoundAt(int resourceId, ObjectInstance? thing)
    {
        if (resourceId < 0) return;
        if (thing is null) { PlaySound(resourceId); return; }
        PlaySoundAt(resourceId, VisualOf(thing));
    }

    private void PlaySoundAt(int resourceId, UIElement? visual)
    {
        if (resourceId < 0) return;
        if (visual is null) { PlaySound(resourceId); return; }
        if (HearingOf(visual) is { } heard) SoundPlayback.TryPlayAt(_resources, resourceId, heard.Volume, heard.Balance);
    }

    /// <summary>Its sounds are heard only near the main character (within Exult's 24 tiles).</summary>
    private bool HeardFrom(CharacterDefinition who)
    {
        if (who == _player) return true;
        var (x, y) = PixelOf(who);
        if (double.IsNaN(x)) return false;
        int lift = LiftOf(who);
        return HearingAt(x - lift * LiftPixelShift, y - lift * LiftPixelShift) is not null;
    }
}
