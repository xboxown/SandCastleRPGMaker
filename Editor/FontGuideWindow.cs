using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A floating, non-modal guide shown while editing InGameFont or DialogFont:
/// the font sheet laid out as 18 columns starting at the space character,
/// each cell showing the character that frame position draws and the glyph
/// drawn there so far. The selected frame is highlighted; clicking a cell
/// selects that frame in the Sprite editor. A sample line shows the font in
/// use. It can be moved anywhere while frames keep being added.
/// </summary>
public class FontGuideWindow : Window
{
    private readonly SpriteEntry _font;
    private readonly UniformGrid _cells = new() { Columns = SpriteDatabase.FontSheetColumns };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Image _sample = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
    private int _current = -1;

    /// <summary>Raised when a cell is clicked: the frame index to select.</summary>
    public event Action<int>? FrameChosen;

    public FontGuideWindow(SpriteEntry font)
    {
        _font = font;
        Title = $"Font Guide - {font.Name}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        RenderOptions.SetBitmapScalingMode(_sample, BitmapScalingMode.NearestNeighbor);

        var root = new StackPanel { Margin = new Thickness(10) };
        root.Children.Add(new TextBlock
        {
            Text = "Frame 1 is the space, then ! \" # ... in this order, 18 per row - each frame's position is the character it draws. " +
                   "Import a whole font sheet with File > Import Strip Image (95 frames, 18 per row are filled in for you). Click a cell to select that frame.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 18 * 40, Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new Border { Child = _cells, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left });
        root.Children.Add(_status);
        root.Children.Add(new TextBlock { Text = "Sample (2x):", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), Padding = new Thickness(6), Child = _sample, MinHeight = 24 });
        Content = root;
        Refresh(0);
    }

    /// <summary>Redraws every cell from the sprite's current frames and highlights the selected one.</summary>
    public void Refresh(int currentFrameIndex)
    {
        _current = currentFrameIndex;
        _cells.Children.Clear();
        for (int i = 0; i < SpriteDatabase.FontGlyphCount; i++)
            _cells.Children.Add(BuildCell(i));

        int extra = _font.Frames.Count - SpriteDatabase.FontGlyphCount;
        int missing = Math.Max(0, SpriteDatabase.FontGlyphCount - _font.Frames.Count);
        _status.Text = $"{Math.Min(_font.Frames.Count, SpriteDatabase.FontGlyphCount)} of {SpriteDatabase.FontGlyphCount} characters drawn" +
                       (missing > 0 ? $" - next frame is '{SpriteDatabase.FontCharForFrame(_font.Frames.Count)}'" : "") +
                       (extra > 0 ? $". {extra} frame{(extra == 1 ? "" : "s")} past '~' {(extra == 1 ? "isn't" : "aren't")} used." : ".");
        _status.Foreground = extra > 0 ? Brushes.DarkOrange : Brushes.Black;

        var font = SpriteFont.FromEntry(_font);
        _sample.Source = font?.RenderText("The quick brown fox jumps over the lazy dog! 0123456789", 2, null, 18 * 40);
        if (_sample.Source is BitmapSource sample) { _sample.Width = sample.PixelWidth; _sample.Height = sample.PixelHeight; }
    }

    private FrameworkElement BuildCell(int index)
    {
        char ch = SpriteDatabase.FontCharForFrame(index) ?? '?';
        bool drawn = index < _font.Frames.Count;
        var stack = new StackPanel();

        var glyphHost = new Grid { Height = 26 };
        if (drawn && ProjectSession.FirstFrame(new SpriteEntry { FilePath = _font.Frames[index].FilePath }) is { } bitmap)
        {
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 26, MaxWidth = 34, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            glyphHost.Children.Add(new Border { Background = Brushes.DimGray, Child = image, HorizontalAlignment = HorizontalAlignment.Center });
        }
        stack.Children.Add(glyphHost);
        stack.Children.Add(new TextBlock
        {
            Text = ch == ' ' ? "spc" : ch.ToString(),
            FontFamily = new FontFamily("Consolas"), FontSize = ch == ' ' ? 10 : 14, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = drawn ? Brushes.Black : Brushes.Gray
        });
        stack.Children.Add(new TextBlock { Text = $"#{index + 1}", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.6 });

        var cell = new Border
        {
            Width = 40, Height = 58, Child = stack,
            Background = index == _current ? new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x80)) : drawn ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
            BorderBrush = index == _current ? Brushes.OrangeRed : Brushes.LightGray,
            BorderThickness = new Thickness(index == _current ? 2 : 0.5),
            Cursor = drawn ? System.Windows.Input.Cursors.Hand : null,
            ToolTip = $"Frame {index + 1}: '{ch}' (character {(int)ch})" + (drawn ? "" : " - not drawn yet")
        };
        if (drawn) cell.MouseLeftButtonDown += (_, _) => FrameChosen?.Invoke(index);
        return cell;
    }
}
