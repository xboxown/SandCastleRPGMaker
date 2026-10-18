using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The map's ambient sounds in play (Edit > Map Ambient Sounds): the sound
/// of the part of the day it is now loops, and gives way to the next part's
/// when the hour comes (System Settings > Day & Night's hours). Rain plays
/// over it; closing the game stops it with every other sound.
/// </summary>
public partial class GameTestWindow
{
    private readonly object _ambientSoundOwner = new();
    private int _ambientPlaying = -2; // -2: nothing decided yet

    private void UpdateAmbient()
    {
        var a = _map.Ambient;
        var d = DayNight;
        double hour = _clock.Hour + _clock.Minute / 60.0;
        int sound = hour >= d.DawnStartHour && hour < d.DayStartHour ? a.DawnSoundId
            : hour >= d.DayStartHour && hour < d.DuskStartHour ? a.DaySoundId
            : hour >= d.DuskStartHour && hour < d.NightStartHour ? a.DuskSoundId
            : a.NightSoundId;
        if (sound == _ambientPlaying) return;
        _ambientPlaying = sound;
        SoundPlayback.StopFor(_ambientSoundOwner);
        if (sound >= 0) SoundPlayback.TryPlayFor(_ambientSoundOwner, _resources, sound, loop: true);
    }
}
