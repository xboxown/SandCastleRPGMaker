namespace SandcastleRpgMaker.Engine;

/// <summary>A map's weather (Map Editor > Map > Weather), as Exult's weather eggs: clear, rain, snow, hail, or a storm (rain, lightning and thunder).</summary>
public enum WeatherKind { Clear, Rain, Snow, Hail, Storm }

/// <summary>One map's weather.</summary>
public class WeatherSettings
{
    public WeatherKind Kind { get; set; } = WeatherKind.Clear;
    /// <summary>How heavy it is, 1-100.</summary>
    public int Intensity { get; set; } = 50;
    /// <summary>Cloud shadows drifting over the land, and how many.</summary>
    public bool Clouds { get; set; }
    public int CloudCover { get; set; } = 40;
    /// <summary>The wind: clouds drift (and rain slants) this way.</summary>
    public SpriteDirection Wind { get; set; } = SpriteDirection.East;
    /// <summary>It comes and goes by itself: every game hour, this % chance it turns (clear / its own kind).</summary>
    public bool Changes { get; set; }
    public int ChangeChancePercent { get; set; } = 25;
    /// <summary>Played on a loop while it rains or storms; thunder after each lightning flash.</summary>
    public int RainSoundResourceId { get; set; } = -1;
    public int ThunderSoundResourceId { get; set; } = -1;
}

/// <summary>
/// A map's ambient sounds (Edit > Map Ambient Sounds): one sound looping for
/// each part of the day - birds chirping at dawn, wind by day, crickets at
/// night. The parts of the day are System Settings > Day & Night's hours.
/// A part with no sound is quiet. The whole game is one map - one world.
/// </summary>
public class AmbientSounds
{
    public int DawnSoundId { get; set; } = -1;
    public int DaySoundId { get; set; } = -1;
    public int DuskSoundId { get; set; } = -1;
    public int NightSoundId { get; set; } = -1;
}

/// <summary>
/// The day and night cycle (System Settings > Day & Night), Exult's
/// time-of-day palettes: night, the dawn and dusk colour on either side of
/// it, and day. The colours start as the ones taken from Ultima VII's own
/// PALETTES.FLX (night 155752, dusk/dawn 9C680C).
/// </summary>
public class DayNightSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Night lifts from DawnStartHour (through the dawn colour) to full day at DayStartHour; day turns at DuskStartHour (through the dusk colour) to full night at NightStartHour.</summary>
    public int DawnStartHour { get; set; } = 5;
    public int DayStartHour { get; set; } = 7;
    public int DuskStartHour { get; set; } = 19;
    public int NightStartHour { get; set; } = 21;
    public string NightColor { get; set; } = "#155752";
    public double NightStrength { get; set; } = 0.55;
    public string DuskColor { get; set; } = "#9C680C";
    public double DuskStrength { get; set; } = 0.42;
    /// <summary>How much darker a cloudy, rainy or stormy day is (Exult's overcast palette), at full weather.</summary>
    public double OvercastStrength { get; set; } = 0.25;
}

/// <summary>What a lit light does in the dark (Object Editor > Light Source).</summary>
public enum LightGives
{
    /// <summary>Exult's light levels: a candle's glow, a single light, many lights - the night lifts by how much light there is.</summary>
    ExultLevels,
    /// <summary>In its reach the night looks like dusk (the sunrise / sunset colour).</summary>
    DuskLook,
    /// <summary>In its reach it's as light as day.</summary>
    FullDaylight
}
