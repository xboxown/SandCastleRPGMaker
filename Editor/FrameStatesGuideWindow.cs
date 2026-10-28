using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A guide for a sprite whose frames are states, not an animation - what
/// each frame stands for, with what's drawn there so far (click one to
/// select that frame) and how the game uses it. Used for keychains (the
/// Keychain group: frame by how many keys) and tilesheets (the Tileset
/// group: one tile a frame).
/// </summary>
public class FrameStatesGuideWindow : Window
{
    /// <summary>What a guide says: its title, the opening words, a name per frame (null = as many frames as the sprite has, numbered), and how it works.</summary>
    public sealed record GuideText(string Title, string Intro, string[]? FrameNames, string HowItWorks);

    public static readonly GuideText Keychain = new(
        "Keychain Guide",
        $"This sprite is one keychain look (Exult's keyring). Its frames show how many keys it holds - not an animation: no sound, no animation, at most {SpriteDatabase.KeychainFrames} frames.",
        new[] { "No keys", "1 key", "2 keys", "3 keys", "4 or more keys" },
        "- In the Object Editor, make an object of type Keychain and pick this sprite for its look.\n" +
        "- In play the game shows the frame for how many keys it holds (Exult's set_keyring_frame: 0, 1, 2, 3, then 4 for four or more).\n" +
        "- Double-click the keychain, then click yourself: every key the party carries goes onto it. Click a door or chest: it opens it with whichever of its keys fits.\n" +
        "- A frame left undrawn shows the last one there is.");

    public static readonly GuideText Tileset = new(
        "Tilesheet Guide",
        "A tilesheet's frames are its tiles, not an animation: frame 1 is the first tile, frame 2 the second, and so on. No sound, no animation.",
        null,
        "- The Map Editor paints the map with these tiles: pick one in the Tiles palette and draw.\n" +
        "- Every frame should be one tile's size (the map's tile width and height). Use Import Sheet to cut a whole tilesheet image into tiles at once.\n" +
        "- Each tile's own properties (walkable or not, its effects) are set in the Map Editor's Tile Properties, by tile number - so keep the tiles in the same order once a map uses them.\n" +
        "- Animated water or lava, and sounds, belong on objects (Object Editor, type Tile), not on the tilesheet.");

    /// <summary>The Weapon in hand group's guide: its frames as System Settings > Weapon in hand sets them.</summary>
    public static GuideText WeaponInHand(WeaponInHandSettings settings) => new(
        "Weapon in Hand Guide",
        settings.Smooth
            ? $"This sprite is one weapon as it is held in the hand - smooth: each of the eight directions its own held frame and a swing of {Math.Clamp(settings.SwingFrames, 1, 8)} frames, {settings.FrameCount} frames in all. States, not an animation: no sound, no animation."
            : "This sprite is one weapon as it is held in the hand, as Exult draws it (Actor::paint_weapon): 4 frames - held, and the swing pointing north, east and south. Characters facing east or west show them mirrored. States, not an animation: no sound, no animation.",
        SpriteDatabase.WeaponInHandFrameNames(settings.Smooth, settings.SwingFrames),
        "- Draw each frame as the weapon looks in a hand, then click the picture to set its grip point (a red cross) - where the hand holds it.\n" +
        "- In the Object Editor, a weapon picks this sprite as its look in the hand.\n" +
        "- In each character's pose sprites (Sprite Editor, any other folder), every frame gets a hand point: Set hand point, then click where the weapon hand is - \"No weapon here\" for a frame without one (lying down, asleep).\n" +
        "- In play, the weapon a character holds is drawn with its grip on the hand of whatever frame they show: held while standing and walking, the swing frames through an attack pose (or the frame a pose frame names). Over the body, as Exult - or behind it, where a frame says so.\n" +
        "- Exult's 4 frames, or smooth ones: System Settings > Weapon in hand (and how many swing frames). Changing it changes what these frames are.");

    private readonly SpriteEntry? _sprite;
    private readonly GuideText _text;
    private readonly WrapPanel _cells = new() { MaxWidth = 560 };
    private int _current = -1;

    /// <summary>Raised when a frame is clicked: its index.</summary>
    public event Action<int>? FrameChosen;

    /// <param name="sprite">The sprite being edited (its frames are shown), or null for the guide alone.</param>
    public FrameStatesGuideWindow(SpriteEntry? sprite, GuideText text)
    {
        _sprite = sprite;
        _text = text;
        Title = sprite is null ? text.Title : $"{text.Title} - {sprite.Name}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = false;
        WindowStartupLocation = sprite is null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.Manual;
        if (sprite is not null) ShowActivated = false;

        var root = new StackPanel { Margin = new Thickness(10), MaxWidth = 580 };
        root.Children.Add(new TextBlock { Text = text.Intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        if (sprite is not null || text.FrameNames is not null)
            root.Children.Add(new Border { Child = _cells, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left });
        root.Children.Add(new TextBlock { Text = "How it works", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) });
        root.Children.Add(new TextBlock { Text = text.HowItWorks, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        if (sprite is null)
        {
            var ok = new Button { Content = "Got it", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true, Margin = new Thickness(0, 10, 0, 0) };
            ok.Click += (_, _) => Close();
            root.Children.Add(ok);
        }
        Content = root;
        Refresh(0);
    }

    /// <summary>Redraws the frames and highlights the selected one.</summary>
    public void Refresh(int currentFrameIndex)
    {
        _current = currentFrameIndex;
        _cells.Children.Clear();
        int count = _text.FrameNames?.Length ?? Math.Min(_sprite?.Frames.Count ?? 0, 48);
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var picture = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            if (_sprite is not null && i < _sprite.Frames.Count) picture.Source = Load(_sprite.Frames[i].FilePath);
            string name = _text.FrameNames is { } names ? names[i] : $"Tile {i + 1}";
            var cell = new StackPanel { Margin = new Thickness(4) };
            cell.Children.Add(new TextBlock { Text = $"Frame {i + 1}", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 });
            cell.Children.Add(new Border
            {
                Width = 54, Height = 54, Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), Child = picture,
                BorderBrush = i == _current ? Brushes.Gold : Brushes.Transparent, BorderThickness = new Thickness(2)
            });
            bool drawn = _sprite is not null && i < _sprite.Frames.Count;
            cell.Children.Add(new TextBlock { Text = drawn || _sprite is null ? name : $"{name}\n(not drawn yet)", FontSize = 10, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Content = cell, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), IsEnabled = drawn };
            button.Click += (_, _) => FrameChosen?.Invoke(index);
            _cells.Children.Add(button);
        }
    }

    private static BitmapImage? Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }
}
