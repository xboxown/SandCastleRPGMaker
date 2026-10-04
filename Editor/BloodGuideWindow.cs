using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A floating, non-modal guide shown while editing a blood type (a sprite in
/// the Blood group): what each of the four frames is - the puddle sizes,
/// small to large, as Exult's blood shape - with what's drawn there so far,
/// and how bleeding works in the game. Clicking a size selects that frame.
/// </summary>
public class BloodGuideWindow : Window
{
    private static readonly string[] SizeNames = { "Smallest drop", "Small puddle", "Puddle", "Largest pool" };

    private readonly SpriteEntry _blood;
    private readonly StackPanel _cells = new() { Orientation = Orientation.Horizontal };
    private int _current = -1;

    /// <summary>Raised when a size is clicked: the frame index to select.</summary>
    public event Action<int>? FrameChosen;

    public BloodGuideWindow(SpriteEntry blood)
    {
        _blood = blood;
        Title = $"Blood Guide - {blood.Name}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var root = new StackPanel { Margin = new Thickness(10), MaxWidth = 470 };
        root.Children.Add(new TextBlock
        {
            Text = $"This sprite is one blood type. Its frames are puddle sizes, not an animation - frame 1 the smallest, each next one bigger, " +
                   $"up to {SpriteDatabase.BloodSizes} (Exult's blood frames 0-3). No sound, no animation. Click a size to select that frame.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new Border { Child = _cells, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left });
        root.Children.Add(new TextBlock { Text = "How bleeding works (as in Exult)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) });
        root.Children.Add(new TextBlock
        {
            Text =
                "- Give a character this blood type in the Character Editor (Health tab). A character with no blood type never bleeds (a robot, a ghost).\n" +
                "- A blow that lands may start it bleeding: the more damage, the likelier (10 or more always does), unless there are already 2 or more puddles right around it.\n" +
                "- While bleeding, it drips about every 0.5-1 seconds for 1-3 minutes, leaving a blood trail as it moves. Healing, or dying, stops it.\n" +
                "- A drip on a spot with no blood leaves a new puddle of size 1 or 2. A drip on a spot that already has a puddle makes that puddle one size bigger instead, up to the largest.\n" +
                "- Each puddle is drawn either way round (mirrored or not) at random, so one set of frames gives more variety.\n" +
                "- Puddles are temporary, like Exult's: they disappear once that part of the map is far off screen.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11
        });
        Content = root;
        Refresh(0);
    }

    /// <summary>Redraws the sizes from the sprite's current frames and highlights the selected one.</summary>
    public void Refresh(int currentFrameIndex)
    {
        _current = currentFrameIndex;
        _cells.Children.Clear();
        for (int i = 0; i < SpriteDatabase.BloodSizes; i++)
        {
            int index = i;
            var picture = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            if (i < _blood.Frames.Count) picture.Source = Load(_blood.Frames[i].FilePath);
            var cell = new StackPanel { Margin = new Thickness(6) };
            cell.Children.Add(new TextBlock { Text = $"Frame {i + 1}", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new Border
            {
                Width = 70, Height = 70, Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), Child = picture,
                BorderBrush = i == _current ? Brushes.Gold : Brushes.Transparent, BorderThickness = new Thickness(2)
            });
            cell.Children.Add(new TextBlock
            {
                Text = i < _blood.Frames.Count ? SizeNames[i] : $"{SizeNames[i]}\n(not drawn yet)",
                FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            });
            var button = new Button { Content = cell, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
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
