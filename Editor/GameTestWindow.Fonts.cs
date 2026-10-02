using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// In-game text in the game's own graphical font (Sprite Editor > Fonts >
/// InGameFont), drawn plain over the world the way Exult draws its
/// identify text. Until that font has glyphs, the old system-font labels
/// are used so nothing goes blank.
/// </summary>
public partial class GameTestWindow
{
    private SpriteFont? InGameFont => SpriteFont.For(_sprites, SpriteDatabase.InGameFontName);

    /// <summary>A label element for in-game text: the InGameFont picture, or the old boxed system-font label as a fallback.</summary>
    private FrameworkElement GameTextLabel(string text)
    {
        if (InGameFont?.RenderText(text) is { } bitmap)
        {
            var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.None, IsHitTestVisible = false, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            return image;
        }
        return new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Padding = new Thickness(4, 2, 4, 2),
            IsHitTestVisible = false,
            FontSize = 12
        };
    }

    /// <summary>The world's identify label: the font picture when there is one, otherwise the plain text label. Returns the element now showing.</summary>
    private FrameworkElement SetIdentifyText(string text)
    {
        if (InGameFont?.RenderText(text) is { } bitmap)
        {
            IdentifyFontImage.Source = bitmap;
            IdentifyFontImage.Width = bitmap.PixelWidth;
            IdentifyFontImage.Height = bitmap.PixelHeight;
            IdentifyLabel.Visibility = Visibility.Collapsed;
            return IdentifyFontImage;
        }
        IdentifyLabel.Text = text;
        IdentifyFontImage.Visibility = Visibility.Collapsed;
        return IdentifyLabel;
    }
}
