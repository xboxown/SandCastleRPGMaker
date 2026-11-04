using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The sky's light, as Exult's time-of-day palettes (gameclk.cc
/// set_time_palette, get_final_palette): day; at dawn the night lifts
/// through the dawn colour, at dusk it falls through the dusk colour; night.
/// Cloudy, rainy and stormy days are darker (Exult's overcast palette).
/// The hours and colours are System Settings > Day &amp; Night. In the dark,
/// light sources lift it as Exult's do (gamerend.cc Get_light_strength): every
/// lit light on screen, and every lit light a party member holds, adds
/// brightness x (75 - 2 x tiles across - 3 x tiles down) from the main
/// character; under 224 a candle's glow, under 640 a single light, more: many
/// lights - or, set so (Light gives), the dusk colour or full daylight.
/// The Change Day/Night command sets the sky until the next hour.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Exult's light palettes, as a share of the night's darkness left: none, candle, single light, many lights.</summary>
    private static readonly double[] DarknessLeftAtLevel = { 1.0, 0.75, 0.55, 0.35 };

    private double _lightCheckMs;
    private (Color Color, double Opacity)? _skyOverride;
    private int _skyOverrideHour = -1;
    private (Color Color, double Opacity) _skyShown = (Colors.Transparent, -1);

    private DayNightSettings DayNight => _systemSettings.DayNight;

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex); } catch { return fallback; }
    }

    private static (Color Color, double Opacity) Mix((Color Color, double Opacity) a, (Color Color, double Opacity) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        Color color = a.Opacity <= 0 ? b.Color : b.Opacity <= 0 ? a.Color : Color.FromRgb(
            (byte)(a.Color.R + (b.Color.R - a.Color.R) * t), (byte)(a.Color.G + (b.Color.G - a.Color.G) * t), (byte)(a.Color.B + (b.Color.B - a.Color.B) * t));
        return (color, a.Opacity + (b.Opacity - a.Opacity) * t);
    }

    /// <summary>The sky for this time of day, and how much of it is night (0 = none, 1 = full night) - the part light sources lift.</summary>
    private ((Color Color, double Opacity) Sky, double Night) SkyNow()
    {
        var d = DayNight;
        var night = (ParseColor(d.NightColor, Color.FromRgb(0x15, 0x57, 0x52)), Math.Clamp(d.NightStrength, 0, 1));
        var dusk = (ParseColor(d.DuskColor, Color.FromRgb(0x9C, 0x68, 0x0C)), Math.Clamp(d.DuskStrength, 0, 1));
        var day = (dusk.Item1, 0.0);
        if (_skyOverride is { } set && _clock.Hour == _skyOverrideHour) return (set, set.Opacity >= night.Item2 - 0.01 ? 1 : 0);
        _skyOverride = null;
        if (!d.Enabled) return (day, 0);
        double h = _clock.Hour + _clock.Minute / 60.0;
        double dawnStart = d.DawnStartHour, dayStart = Math.Max(d.DayStartHour, d.DawnStartHour + 0.01), duskStart = d.DuskStartHour, nightStart = Math.Max(d.NightStartHour, d.DuskStartHour + 0.01);
        if (h >= dawnStart && h < dayStart)
        {
            double t = (h - dawnStart) / (dayStart - dawnStart);
            return t < 0.5 ? (Mix(night, dusk, t * 2), 1 - t * 2) : (Mix(dusk, day, (t - 0.5) * 2), 0);
        }
        if (h >= duskStart && h < nightStart)
        {
            double t = (h - duskStart) / (nightStart - duskStart);
            return t < 0.5 ? (Mix(day, dusk, t * 2), 0) : (Mix(dusk, night, (t - 0.5) * 2), (t - 0.5) * 2);
        }
        return h >= dayStart && h < duskStart ? (day, 0) : (night, 1);
    }

    /// <summary>A few times a second: the sky, the weather's gloom, and the lights.</summary>
    private void UpdateLighting(double elapsedMs)
    {
        if ((_lightCheckMs -= elapsedMs) > 0) return;
        _lightCheckMs = 250;
        var (sky, nightAmount) = SkyNow();
        bool inCave = PlayerInCave;
        if (inCave) (sky, nightAmount) = ((Colors.Black, 1.0), 1.0); // a Cave: complete darkness, day or night - only a light source lifts it

        // Overcast: weather and clouds darken the day (and the night a little more).
        double gloom = inCave ? 0 : (_weather.Kind == WeatherKind.Clear ? 0 : _weather.Intensity / 100.0) + (_weather.Clouds ? _weather.CloudCover / 200.0 : 0);
        gloom = Math.Clamp(gloom, 0, 1) * Math.Clamp(DayNight.OvercastStrength, 0, 1);
        if (gloom > 0.001)
        {
            var grey = Color.FromRgb(0x20, 0x24, 0x2C);
            var overcast = (Mix((sky.Color, sky.Opacity), (grey, gloom), sky.Opacity <= 0 ? 1 : 0.35).Color, 1 - (1 - sky.Opacity) * (1 - gloom));
            sky = overcast;
        }

        // Light sources, in the dark.
        var (strength, gives) = nightAmount > 0 && _player is not null ? LightAround() : (0, LightGives.ExultLevels);
        if (_specialLightMinutes > 0) strength = Math.Max(strength, 640); // a light spell: as many lights (add_special_light)
        if (nightAmount > 0 && strength > 0)
        {
            var dusk = (ParseColor(DayNight.DuskColor, Color.FromRgb(0x9C, 0x68, 0x0C)), Math.Clamp(DayNight.DuskStrength, 0, 1));
            switch (gives)
            {
                case LightGives.FullDaylight:
                    sky = (sky.Color, sky.Opacity * (1 - nightAmount));
                    break;
                case LightGives.DuskLook:
                    sky = Mix(sky, dusk, nightAmount);
                    break;
                default:
                    int level = strength <= 0 ? 0 : strength < 224 ? 1 : strength < 640 ? 2 : 3;
                    sky = (sky.Color, sky.Opacity * (1 - nightAmount * (1 - DarknessLeftAtLevel[level])));
                    break;
            }
        }
        ShowSky(sky);
    }

    private void ShowSky((Color Color, double Opacity) sky)
    {
        if (_skyShown.Color == sky.Color && Math.Abs(_skyShown.Opacity - sky.Opacity) < 0.005) return;
        bool colorChanged = _skyShown.Color != sky.Color;
        _skyShown = sky;
        if (colorChanged || LightingOverlay.Fill is not SolidColorBrush) LightingOverlay.Fill = new SolidColorBrush(sky.Color);
        LightingOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(Math.Clamp(sky.Opacity, 0, 1), TimeSpan.FromSeconds(0.4)));
    }

    /// <summary>Get_light_strength summed over the lights on screen and those the party holds, and the strongest kind of light among them.</summary>
    private (int Strength, LightGives Gives) LightAround()
    {
        double ax = _playerPixelX, ay = _playerPixelY;
        int Strength(double x, double y, int brightness)
        {
            int dx = (int)Math.Abs(Math.Round((x - ax) / SpriteSizing.BaseUnit));
            int dy = (int)Math.Abs(Math.Round((y - ay) / SpriteSizing.BaseUnit));
            return Math.Max(0, 75 - 2 * dx - 3 * dy) * brightness;
        }
        int total = 0;
        var gives = LightGives.ExultLevels;
        void Add(int strength, WorldObject obj)
        {
            if (strength <= 0) return;
            total += strength;
            if (obj.Light.Gives > gives) gives = obj.Light.Gives; // full daylight wins over the dusk look, which wins over Exult's levels
        }
        foreach (var light in _map.Objects)
        {
            if (light.IsLitRuntime != true || light.ContainerInstanceId >= 0 || DefinitionOf(light) is not { Type: ObjectType.LightSource } obj) continue;
            double x = light.X * _tileWidth + _tileWidth / 2.0, y = light.Y * _tileHeight + _tileHeight / 2.0;
            if (!IsOnScreen(x, y)) continue;
            Add(Strength(x, y, obj.Light.Brightness), obj);
        }
        foreach (var member in AllLivingCharacters().Where(c => c == _player || c.IsInParty))
        {
            var (x, y) = PixelOf(member);
            foreach (var slot in new[] { EquipmentSlot.RightHand, EquipmentSlot.LeftHand })
            {
                if (!member.EquippedSlots.TryGetValue(slot, out int id)) continue;
                if (member.Inventory.FirstOrDefault(i => i.InstanceId == id) is { IsLitRuntime: true } held && DefinitionOf(held) is { Type: ObjectType.LightSource } obj)
                    Add(Strength(x, y, obj.Light.Brightness), obj);
            }
        }
        return (total, gives);
    }

    /// <summary>The Change Day/Night command: this sky until the next hour, then the clock's again.</summary>
    private void SetSkyUntilNextHour(Color color, double opacity)
    {
        _skyOverride = (color, opacity);
        _skyOverrideHour = _clock.Hour;
        _lightCheckMs = 0;
    }
}
