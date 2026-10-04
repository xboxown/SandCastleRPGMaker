using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How a hit shows, ported from Exult (actors.cc Actor::reduce_health,
/// Actor::figure_hit_points, Actor::paint, Clear_hit; combat.cc show_hits):
///  - Whoever is hit gets a momentary red outline around its sprite
///    (paint_outline(HIT_PIXEL)), cleared 200 ms later.
///  - The main character instead flashes the whole screen red when badly
///    hurt: a blow of a third of its maximum health or more, or while it's
///    below a quarter (Palette::flash_red).
///  - The hit numbers ("-4", "miss") only show with System Settings >
///    Combat > Show hit numbers - Exult's show_hits, off by default.
/// </summary>
public partial class GameTestWindow
{
    private const int HitOutlineMs = 200;
    private static readonly Color HitColor = Color.FromRgb(0xE0, 0x10, 0x10);

    private bool ShowHitNumbers => _systemSettings.Combat.ShowHitNumbers;

    /// <summary>reduce_health's flash for <paramref name="amount"/> damage to a character that had <paramref name="oldHp"/>.</summary>
    private void FlashHit(CharacterDefinition character, int amount, int oldHp)
    {
        int maxHp = MaxHp(character);
        if (character == _player && maxHp > 0 && (amount >= maxHp / 3 || oldHp < maxHp / 4)) FlashScreenRed();
        else ShowHitOutline(character);
    }

    /// <summary>A red outline drawn round the character's current frame for 200 ms.</summary>
    private void ShowHitOutline(CharacterDefinition character)
    {
        var visual = character == _player ? _playerImage : MapCharacterOf(character)?.Visual as Image;
        if (visual?.Source is not BitmapSource frame || visual.Width <= 0 || frame.PixelWidth <= 0) return;
        var outline = OutlineOf(frame);
        double scale = visual.Width / frame.PixelWidth;
        var image = new Image
        {
            Source = outline, Width = outline.PixelWidth * scale, Height = outline.PixelHeight * scale,
            Stretch = Stretch.Fill, IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, Canvas.GetLeft(visual) - scale);
        Canvas.SetTop(image, Canvas.GetTop(visual) - scale);
        Panel.SetZIndex(image, Panel.GetZIndex(visual) + 1);
        GameCanvas.Children.Add(image);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HitOutlineMs) };
        timer.Tick += (_, _) => { timer.Stop(); GameCanvas.Children.Remove(image); };
        timer.Start();
    }

    /// <summary>The 1-pixel outline of a frame's shape: every see-through pixel touching a solid one, in the hit colour, one pixel bigger all round.</summary>
    private static BitmapSource OutlineOf(BitmapSource frame)
    {
        var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = source.PixelWidth, h = source.PixelHeight;
        var pixels = new byte[w * h * 4];
        source.CopyPixels(pixels, w * 4, 0);
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && pixels[(y * w + x) * 4 + 3] > 127;

        int ow = w + 2, oh = h + 2;
        var outline = new byte[ow * oh * 4];
        for (int y = -1; y <= h; y++)
            for (int x = -1; x <= w; x++)
            {
                if (Solid(x, y) || !(Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1))) continue;
                int i = ((y + 1) * ow + (x + 1)) * 4;
                outline[i] = HitColor.B; outline[i + 1] = HitColor.G; outline[i + 2] = HitColor.R; outline[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(ow, oh, 96, 96, PixelFormats.Bgra32, null, outline, ow * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Palette::flash_red: the whole view goes red for a moment.</summary>
    private void FlashScreenRed()
    {
        if (GameCanvas.Parent is not Panel viewport) return;
        var flash = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(150, 0xE0, 0, 0)), IsHitTestVisible = false };
        Panel.SetZIndex(flash, 35); // over the world and its lighting, under dialog boxes
        viewport.Children.Add(flash);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); viewport.Children.Remove(flash); };
        timer.Start();
    }
}
