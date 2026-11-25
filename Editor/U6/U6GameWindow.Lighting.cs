// Ultima 6 engine (U6GameWindow) - copied from the Ultima 7 engine (GameTestWindow, build 523) and changed for Ultima 6 here only.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6's darkness (the user, 2026-11-25: Ultima 6 / Savage Empire / Martian Dreams, not Ultima 7's tinted sky):
/// Engine/U6Lighting.cs - Nuvie's original lighting. By day nothing is dark; at night, and in a Cave zone day or night,
/// every cell is dark but for globes of light: the Avatar's (the hour's level - 4 at night, dusk and dawn stepping it;
/// a lit light in the party, the Light spell or worn magic light: 8), each lit light on the map (a candle 2, brighter 3)
/// and each other character holding a lit light (3). Each cell 0-4 is drawn as Ultima 6's shading: lit, a quarter, half,
/// three quarters dark, or black (a 2 x 2 dither) - one bitmap over the view (ShadeImage), redrawn a few times a second
/// and whenever the view crosses a cell. The Apple IIgs player draws the same (look.s DrawDark). Change Day/Night sets
/// the light until the next hour: Morning - day, Afternoon - dusk's middle, Evening - night.
/// </summary>
public partial class U6GameWindow
{
    private double _lightCheckMs;
    /// <summary>Change Day/Night's level (0 day, else the Avatar's globe) and the hour it holds for; -1: none.</summary>
    private int _levelOverride = -1, _levelOverrideHour = -1;
    private WriteableBitmap? _shadeBitmap;
    private TranslateTransform? _shadeShift;
    private System.Windows.Controls.Image? _shadeImage;
    private (int C0, int R0) _shadeOrigin = (int.MinValue, int.MinValue);

    private DayNightSettings DayNight => _systemSettings.DayNight;

    /// <summary>The Avatar's globe for now (0 = day: no darkness).</summary>
    private int AmbientLevelNow(bool inCave)
    {
        if (_levelOverride >= 0 && _clock.Hour == _levelOverrideHour && !inCave) return _levelOverride;
        _levelOverride = -1;
        return U6Lighting.AmbientLevel(_clock.Hour, _clock.Minute, DayNight, inCave);
    }

    /// <summary>A few times a second (and when the view crosses a cell): the darkness over the view.</summary>
    private void UpdateLighting(double elapsedMs)
    {
        if ((_lightCheckMs -= elapsedMs) > 0) return;
        _lightCheckMs = 250;
        LightingOverlay.Opacity = 0; // (Ultima 7's tint: not in Ultima 6)
        var eye = LightEye();
        bool inCave = Zones.IsInCave(_map.Zones, eye.X, eye.Y, eye.Floor);
        int ambient = AmbientLevelNow(inCave);
        var image = ShadeImage();
        bool cutscene = _cameraHold is not null || _cameraFollow is not null; // the camera elsewhere: a cutscene - full light (the user, 2026-11-25)
        if (ambient <= 0 || cutscene || _player is null || _tileWidth <= 0 || _tileHeight <= 0) { image.Visibility = Visibility.Collapsed; return; }

        // The cells over the view, one more each side.
        int c0 = (int)Math.Floor(_cameraX / _tileWidth) - 1, r0 = (int)Math.Floor(_cameraY / _tileHeight) - 1;
        int cols = (int)Math.Ceiling(ViewportWidth / _tileWidth) + 3, rows = (int)Math.Ceiling(ViewportHeight / _tileHeight) + 3;
        var shade = new byte[cols * rows];

        // The Avatar's globe: the hour's, or a light in the party.
        bool partyLight = _specialLightMinutes > 0;
        foreach (var member in AllLivingCharacters())
        {
            bool party = member == _player || member.IsInParty;
            bool holds = HoldsLitLight(member) || MagicTotal(member, MagicBonusKind.Light) > 0;
            if (!holds) continue;
            if (party) { partyLight = true; continue; }
            var (mx, my) = CellOf(member); // anyone else with a light: its own globe
            U6Lighting.AddGlobe(shade, cols, rows, mx - c0, my - r0, 3);
        }
        int avatar = partyLight ? Math.Max(ambient, U6Lighting.PartyLightLevel) : ambient;
        U6Lighting.AddGlobe(shade, cols, rows, eye.X - c0, eye.Y - r0, avatar);
        foreach (var light in _map.Objects)
        {
            if (light.IsLitRuntime != true || light.ContainerInstanceId >= 0 || DefinitionOf(light) is not { Type: ObjectType.LightSource } obj) continue;
            if (light.X < c0 - 6 || light.Y < r0 - 6 || light.X > c0 + cols + 6 || light.Y > r0 + rows + 6) continue;
            U6Lighting.AddGlobe(shade, cols, rows, light.X - c0, light.Y - r0, U6Lighting.ObjectLevel(obj.Light.Brightness));
        }
        DrawShade(shade, cols, rows);
        _shadeOrigin = (c0, r0);
        PlaceShade();
        image.Visibility = Visibility.Visible;
    }

    /// <summary>Whether a character holds a lit light in a hand.</summary>
    private bool HoldsLitLight(CharacterDefinition member)
    {
        foreach (var slot in new[] { EquipmentSlot.RightHand, EquipmentSlot.LeftHand })
            if (member.EquippedSlots.TryGetValue(slot, out int id) && member.Inventory.FirstOrDefault(i => i.InstanceId == id) is { IsLitRuntime: true } held
                && DefinitionOf(held) is { Type: ObjectType.LightSource })
                return true;
        return false;
    }

    /// <summary>The bitmap over the view (made the first time), in the world view's grid over the weather.</summary>
    private System.Windows.Controls.Image ShadeImage()
    {
        if (_shadeImage is not null) return _shadeImage;
        _shadeShift = new TranslateTransform();
        _shadeImage = new System.Windows.Controls.Image
        {
            IsHitTestVisible = false, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _shadeShift, Visibility = Visibility.Collapsed
        };
        RenderOptions.SetBitmapScalingMode(_shadeImage, BitmapScalingMode.NearestNeighbor);
        System.Windows.Controls.Panel.SetZIndex(_shadeImage, 25);
        ((System.Windows.Controls.Grid)LightingOverlay.Parent).Children.Add(_shadeImage);
        return _shadeImage;
    }

    /// <summary>Each cell's light as Ultima 6's shading: its pixels black by the 2 x 2 dither, the rest see-through.</summary>
    private void DrawShade(byte[] shade, int cols, int rows)
    {
        int w = cols * _tileWidth, h = rows * _tileHeight;
        if (_shadeBitmap is null || _shadeBitmap.PixelWidth != w || _shadeBitmap.PixelHeight != h)
        {
            _shadeBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            ShadeImage().Source = _shadeBitmap;
        }
        var pixels = new int[w * h];
        for (int py = 0; py < h; py++)
        {
            int row = py / _tileHeight * cols;
            for (int px = 0; px < w; px++)
                if (U6Lighting.DarkPixel(shade[row + px / _tileWidth], px, py)) pixels[py * w + px] = unchecked((int)0xFF000000);
        }
        _shadeBitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
    }

    /// <summary>The bitmap where its cells are, as the view moves (UpdateCamera); a new cell crossed: drawn again now.</summary>
    private void PlaceShade()
    {
        if (_shadeShift is null || _shadeOrigin.C0 == int.MinValue) return;
        _shadeShift.X = _shadeOrigin.C0 * _tileWidth - _cameraX;
        _shadeShift.Y = _shadeOrigin.R0 * _tileHeight - _cameraY;
        if (_tileWidth > 0 && _tileHeight > 0 && ((int)Math.Floor(_cameraX / _tileWidth) - 1 != _shadeOrigin.C0 || (int)Math.Floor(_cameraY / _tileHeight) - 1 != _shadeOrigin.R0)) _lightCheckMs = 0;
    }

    /// <summary>
    /// Where the light is judged from: whoever the view follows (Camera Follows someone else) - else the player. Without
    /// it, the view following someone away from the player's lights (or out of the player's Cave) went dark.
    /// </summary>
    private (int X, int Y, int Floor, double PixelX, double PixelY) LightEye() =>
        _cameraFollow is { } followed && MapCharacterOf(followed) is { } eye
            ? (eye.Placement.X, eye.Placement.Y, MapFloors.FloorOfLift(eye.Lift), eye.PixelX, eye.PixelY)
            : (_playerX, _playerY, MapFloors.FloorOfLift(_playerZ), _playerPixelX, _playerPixelY);

    /// <summary>The Change Day/Night command: Morning - day, Afternoon - dusk's middle, Evening - night, until the next hour.</summary>
    private void SetLevelUntilNextHour(string period)
    {
        _levelOverride = period switch { "Evening" => U6Lighting.NightLevel, "Afternoon" => 7, _ => 0 };
        _levelOverrideHour = _clock.Hour;
        _lightCheckMs = 0;
    }
}
