// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The map's ambient sounds in play (Edit > Map Ambient Sounds): the sound
/// of the part of the day it is now loops, and gives way to the next part's
/// when the hour comes (System Settings > Day & Night's hours). Rain plays
/// over it; closing the game stops it with every other sound.
/// </summary>
public partial class U8GameWindow
{
    private readonly object _ambientSoundOwner = new();
    private int _ambientPlaying = -2; // -2: nothing decided yet

    private void UpdateAmbient()
    {
        if (InTitle) { SilenceAmbient(); return; } // the Title Screen isn't a map: only its own music - the map's ambient starts when the game does
        if (AmbientSilencedByCombat()) { SilenceAmbient(); return; } // combat music, or a battle (U8GameWindow.CombatMusic.cs) - weather plays on
        var a = _map.Ambient;
        var d = DayNight;
        double hour = _clock.Hour + _clock.Minute / 60.0;
        int sound = hour >= d.DawnStartHour && hour < d.DayStartHour ? a.DawnSoundId
            : hour >= d.DayStartHour && hour < d.DuskStartHour ? a.DaySoundId
            : hour >= d.DuskStartHour && hour < d.NightStartHour ? a.DuskSoundId
            : a.NightSoundId;
        // In a Cave (or a zone with a sound of its own): its ambient instead of the world's.
        if (Zones.AmbientZoneAt(_map.Zones, _playerX, _playerY, MapFloors.FloorOfLift(_playerZ)) is { } zone) sound = zone.AmbientSoundId;
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
