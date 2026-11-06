// Ultima 6 engine (U6GameWindow) - copied from the Ultima 7 engine (GameTestWindow, build 523) and changed for Ultima 6 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The map's ambient sounds in play (Edit > Map Ambient Sounds): the sound
/// of the part of the day it is now loops, and gives way to the next part's
/// when the hour comes (System Settings > Day & Night's hours). Rain plays
/// over it; closing the game stops it with every other sound.
/// </summary>
public partial class U6GameWindow
{
    private readonly object _ambientSoundOwner = new();
    private int _ambientPlaying = -2; // -2: nothing decided yet

    private void UpdateAmbient()
    {
        if (InTitle) { SilenceAmbient(); return; } // the Title Screen isn't a map: only its own music - the map's ambient starts when the game does
        if (AmbientSilencedByCombat() || _talkMusicOn) { SilenceAmbient(); return; } // combat music, a battle, or talking with its own music (U6GameWindow.U6Talk.cs) - weather plays on
        var a = _map.Ambient;
        var d = DayNight;
        double hour = _clock.Hour + _clock.Minute / 60.0;
        int sound = hour >= d.DawnStartHour && hour < d.DayStartHour ? a.DawnSoundId
            : hour >= d.DayStartHour && hour < d.DuskStartHour ? a.DaySoundId
            : hour >= d.DuskStartHour && hour < d.NightStartHour ? a.DuskSoundId
            : a.NightSoundId;
        // In a Cave (or a zone with a sound of its own): its ambient instead of the world's.
        if (Zones.AmbientZoneAt(_map.Zones, _playerX, _playerY, MapFloors.FloorOfLift(_playerZ)) is { } zone) sound = zone.AmbientSoundId;
        // After a talk: a random song of the map's in place of this one, until the hour's own changes (Nuvie's random group).
        if (_ambientHold is { } hold)
        {
            if (hold.Due < 0) _ambientHold = hold = (sound, hold.Random);
            if (sound == hold.Due) sound = hold.Random;
            else _ambientHold = null;
        }
        if (sound == _ambientPlaying) return;
        _ambientPlaying = sound;
        SoundPlayback.StopFor(_ambientSoundOwner);
        if (sound >= 0) SoundPlayback.TryPlayFor(_ambientSoundOwner, _resources, sound, loop: true);
    }

    /// <summary>Stops the ambient sound; it starts again the next time it's allowed.</summary>
    private void SilenceAmbient()
    {
        if (_ambientPlaying == -2) return;
        SoundPlayback.StopFor(_ambientSoundOwner);
        _ambientPlaying = -2;
    }
}
