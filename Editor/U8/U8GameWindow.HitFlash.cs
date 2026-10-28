// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
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
public partial class U8GameWindow
{
    private const int HitOutlineMs = 200;
    private static readonly Color HitColor = Color.FromRgb(0xE0, 0x10, 0x10);

    private bool ShowHitNumbers => _systemSettings.Combat.ShowHitNumbers;

    /// <summary>Outlines showing right now, each with the character picture it belongs to - moved with it every frame, so it never trails behind someone walking.</summary>
    private readonly List<(Image Outline, Image Visual, double Scale)> _hitOutlines = new();

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
        var outline = OutlineOf(frame, HitColor);
        double scale = visual.Width / frame.PixelWidth;
        var image = new Image
        {
            Source = outline, Width = outline.PixelWidth * scale, Height = outline.PixelHeight * scale,
            Stretch = Stretch.Fill, IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var entry = (image, visual, scale);
        _hitOutlines.Add(entry);
        GameCanvas.Children.Add(image);
        FollowHitOutlines();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HitOutlineMs) };
        timer.Tick += (_, _) => { timer.Stop(); GameCanvas.Children.Remove(image); _hitOutlines.Remove(entry); };
        timer.Start();
    }

    /// <summary>Every frame: each outline sits exactly round its character, wherever it has walked to.</summary>
    private void FollowHitOutlines()
    {
        foreach (var (outline, visual, scale) in _hitOutlines)
        {
            U8ProjectionCanvas.SetPlacement(outline, U8ProjectionCanvas.GetPlacement(visual)); // placed as what it outlines
            Canvas.SetLeft(outline, Canvas.GetLeft(visual) - scale);
            Canvas.SetTop(outline, Canvas.GetTop(visual) - scale);
            Panel.SetZIndex(outline, Panel.GetZIndex(visual) + 1);
        }
    }

    /// <summary>The 1-pixel outline of a frame's shape: every see-through pixel touching a solid one, in the hit colour, one pixel bigger all round.</summary>
    private static BitmapSource OutlineOf(BitmapSource frame, Color color)
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
                outline[i] = color.B; outline[i + 1] = color.G; outline[i + 2] = color.R; outline[i + 3] = 255;
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
