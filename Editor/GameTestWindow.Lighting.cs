using System.Windows;
using System.Windows.Media.Animation;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Light sources lighting the night, as Exult does (gamerend.cc
/// Get_light_strength / paint, gameclk.cc get_final_palette): every lit
/// light on screen, plus every lit light a party member holds in a hand,
/// adds brightness x (75 - 2 x tiles across - 3 x tiles down) from the main
/// character (never below 0). In the dark, that total picks how much the
/// darkness lifts: under 224 a candle's glow, under 640 a single light,
/// more: many lights. Daytime is unaffected.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>How dark the night is (the day/night overlay's own strength), or 0 when it isn't night.</summary>
    private double _nightDarkness;
    private int _lightLevel = -1;
    private double _lightCheckMs;

    /// <summary>Exult's light palettes, as a share of the night's darkness left: none, candle, single light, many lights.</summary>
    private static readonly double[] DarknessLeftAtLevel = { 1.0, 0.75, 0.55, 0.35 };

    private void UpdateLighting(double elapsedMs)
    {
        if ((_lightCheckMs -= elapsedMs) > 0) return;
        _lightCheckMs = 250;
        if (_nightDarkness <= 0 || _player is null) { _lightLevel = -1; return; }
        int light = LightStrength();
        int level = light <= 0 ? 0 : light < 224 ? 1 : light < 640 ? 2 : 3;
        if (level == _lightLevel) return;
        _lightLevel = level;
        LightingOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(_nightDarkness * DarknessLeftAtLevel[level], TimeSpan.FromSeconds(0.4)));
    }

    /// <summary>Get_light_strength summed over the lights on screen and those the party holds.</summary>
    private int LightStrength()
    {
        double ax = _playerPixelX, ay = _playerPixelY;
        int Strength(double x, double y, int brightness)
        {
            int dx = (int)Math.Abs(Math.Round((x - ax) / SpriteSizing.BaseUnit));
            int dy = (int)Math.Abs(Math.Round((y - ay) / SpriteSizing.BaseUnit));
            return Math.Max(0, 75 - 2 * dx - 3 * dy) * brightness;
        }
        int total = 0;
        foreach (var light in _map.Objects)
        {
            if (light.IsLitRuntime != true || light.ContainerInstanceId >= 0 || DefinitionOf(light) is not { Type: ObjectType.LightSource } obj) continue;
            double x = light.X * _tileWidth + _tileWidth / 2.0, y = light.Y * _tileHeight + _tileHeight / 2.0;
            if (!IsOnScreen(x, y)) continue;
            total += Strength(x, y, obj.Light.Brightness);
        }
        foreach (var member in AllLivingCharacters().Where(c => c == _player || c.IsInParty))
        {
            var (x, y) = PixelOf(member);
            foreach (var slot in new[] { EquipmentSlot.RightHand, EquipmentSlot.LeftHand })
            {
                if (!member.EquippedSlots.TryGetValue(slot, out int id)) continue;
                if (member.Inventory.FirstOrDefault(i => i.InstanceId == id) is { IsLitRuntime: true } held && DefinitionOf(held) is { Type: ObjectType.LightSource } obj)
                    total += Strength(x, y, obj.Light.Brightness);
            }
        }
        return total;
    }
}
