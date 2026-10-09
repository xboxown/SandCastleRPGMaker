using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>The dialog box's measurements on the 320x200 game screen.</summary>
internal static class DialogBoxMetrics
{
    public const double ScreenWidth = 320, ScreenHeight = 200;
    /// <summary>Gap between the box and the screen's edge.</summary>
    public const double Margin = 4;
    /// <summary>Gap between the box's edge and the text when the frame has no Nine Slice borders.</summary>
    public const double Padding = 4;
    public const double MinHeight = 32;
    public const double FaceMax = 48;
    public const double Width = ScreenWidth - 2 * Margin;

    /// <summary>How far in from each edge the text starts: the frame's Nine Slice borders (at least Padding).</summary>
    public static Thickness Inset(SpriteEntry? frame) =>
        frame is { NineSlice.Enabled: true } f
            ? new Thickness(Math.Max(Padding, f.NineSlice.Left), Math.Max(Padding, f.NineSlice.Top), Math.Max(Padding, f.NineSlice.Right), Math.Max(Padding, f.NineSlice.Bottom))
            : new Thickness(Padding);
}

/// <summary>
/// A dialog frame sprite (the DialogFrame group) drawn at any size: with
/// Nine Slice on, the corners stay exactly as drawn and the edges and middle
/// stretch; without it, the whole picture stretches. It runs through
/// SpriteAnimator like every other sprite, so its frames animate and play
/// their sounds. Call Stop when the box goes away.
/// </summary>
internal sealed class DialogFrameVisual : Grid
{
    private readonly SpriteEntry _sprite;
    private readonly Image[] _pieces = new Image[9];
    private readonly SpriteAnimator _animator;
    private readonly Image _probe = new();

    public DialogFrameVisual(SpriteEntry sprite, double width, double height, ResourceDatabase? resources)
    {
        _sprite = sprite;
        Width = width;
        Height = height;
        IsHitTestVisible = false;
        var slice = sprite.NineSlice;
        bool nine = slice.Enabled;
        // Borders can't be wider than half the box.
        double left = nine ? Math.Min(slice.Left, width / 2) : 0, right = nine ? Math.Min(slice.Right, width / 2) : 0;
        double top = nine ? Math.Min(slice.Top, height / 2) : 0, bottom = nine ? Math.Min(slice.Bottom, height / 2) : 0;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(left) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(right) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(top) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(bottom) });
        for (int i = 0; i < 9; i++)
        {
            var piece = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(piece, BitmapScalingMode.NearestNeighbor);
            SetRow(piece, i / 3);
            SetColumn(piece, i % 3);
            _pieces[i] = piece;
            Children.Add(piece);
        }
        _animator = new SpriteAnimator(sprite, _probe, resources);
        ShowFrame();
        _animator.Ticked += _ => ShowFrame();
    }

    public void Stop() => _animator.Stop();

    private void ShowFrame()
    {
        if (_probe.Source is not BitmapSource picture)
        {
            foreach (var piece in _pieces) piece.Source = null;
            return;
        }
        int w = picture.PixelWidth, h = picture.PixelHeight;
        var slice = _sprite.NineSlice;
        if (!slice.Enabled)
        {
            for (int i = 0; i < 9; i++) _pieces[i].Source = i == 4 ? picture : null;
            return;
        }
        int l = Math.Clamp(slice.Left, 0, w), r = Math.Clamp(slice.Right, 0, w - l), t = Math.Clamp(slice.Top, 0, h), b = Math.Clamp(slice.Bottom, 0, h - t);
        int[] xs = { 0, l, w - r }, ws = { l, w - l - r, r };
        int[] ys = { 0, t, h - b }, hs = { t, h - t - b, b };
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                _pieces[row * 3 + col].Source = ws[col] > 0 && hs[row] > 0 ? new CroppedBitmap(picture, new Int32Rect(xs[col], ys[row], ws[col], hs[row])) : null;
    }
}

/// <summary>How dialog frames work - shown when a sprite is added to the DialogFrame group, and from Show Text's "?" button.</summary>
public class DialogFrameGuideWindow : Window
{
    public DialogFrameGuideWindow()
    {
        Title = "Dialog Frame Guide";
        SizeToContent = SizeToContent.Height;
        Width = 600;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(14) };
        void Heading(string text) => root.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 6, 0, 4) });
        void Para(string text) => root.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        root.Children.Add(new TextBlock { Text = "How the DialogFrame group works", FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 8) });
        Para("Every sprite in the DialogFrame group is one look for the Show Text dialog box. In Show Text you pick one for each line, leave it on Default (the first sprite in this group), or pick None for no box at all - just the text over the game. If this group is empty there's no frame anywhere: the text is drawn straight over the game.");

        Heading("The size of the box");
        Para($"The game screen is {DialogBoxMetrics.ScreenWidth:0} x {DialogBoxMetrics.ScreenHeight:0} pixels. The box is always {DialogBoxMetrics.Width:0} pixels wide ({DialogBoxMetrics.Margin:0} pixels in from each side), " +
             $"and as tall as its text, face and replies need - at least {DialogBoxMetrics.MinHeight:0} pixels, at most the whole screen height. So your picture gets stretched to fit.");

        Heading("Nine Slice: corners that don't stretch");
        Para("Open the sprite in the frame editor and turn on Nine Slice. Set Left, Right, Top and Bottom to how thick your border is. The picture is cut 3 x 3 along those lines: " +
             "the four corners are drawn exactly as you drew them, the four edges stretch along their length, and the middle stretches to fill. " +
             "The text, the face and the replies start inside those borders, so a thick border pushes the text in.");
        Para("A good start: a 48 x 48 picture with an 8-pixel border (Nine Slice 8, 8, 8, 8). Any size works - small pictures stretch better if the middle is a plain colour or a simple pattern.");
        Para("Without Nine Slice the whole picture is stretched to the box (fine for a plain colour or a soft blur, but corners and borders get squashed). The text then starts 4 pixels in.");

        Heading("See-through, animation and sound");
        Para("Transparent and half-transparent pixels show the game behind - that's how you make a see-through box (a dark blue at about 85% opacity with a light grey 1-pixel border gives the old look)." +
             "It's an ordinary sprite: give it several frames and it animates while the box is up, and a sound on any frame plays when that frame comes round (Fps and Loop as set in the frame editor).");

        Heading("The text");
        Para("The words are written in the DialogFont (Fonts group), so choose a frame that colour shows up on. Pick \"Dim the screen\" in Show Text to darken the game behind the box too.");

        var ok = new Button { Content = "Got it", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true, Margin = new Thickness(0, 6, 0, 0) };
        ok.Click += (_, _) => Close();
        root.Children.Add(ok);
        Content = root;
    }
}
