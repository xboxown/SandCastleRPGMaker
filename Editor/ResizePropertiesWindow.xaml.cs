using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Replaces the old "type WxH into a text box" Canvas Size prompt. Two
/// distinct operations live here, matching the GameMaker reference:
/// "Scale Image" resamples the existing pixels to a new size (the art
/// stretches/shrinks); "Resize Canvas" leaves the existing pixels at their
/// original size and grows or crops the canvas around them, anchored to
/// whichever of the 9 grid cells is selected. The two are genuinely
/// different operations, not two labels for the same thing, so both get
/// their own math, their own preview, and their own origin-adjustment rule.
/// </summary>
public partial class ResizePropertiesWindow : Window
{
    private readonly BitmapSource _source;
    private readonly int _oldW, _oldH;
    private bool _suppress;
    private bool _scaleUnitIsPercent;
    private bool _canvasUnitIsPercent;
    private string _anchor = "MiddleCenter";
    private readonly List<Button> _anchorButtons = new();
    private bool _initialized;

    private readonly Dictionary<string, BitmapScalingMode> _interpolationModes = new()
    {
        ["None"] = BitmapScalingMode.NearestNeighbor,
        ["Linear"] = BitmapScalingMode.Linear,
        ["High Quality"] = BitmapScalingMode.HighQuality
    };

    public bool Result { get; private set; }
    public WriteableBitmap? ResultBitmap { get; private set; }
    public bool ApplyToOrigin { get; private set; }

    /// <summary>Additive origin shift - only meaningful for Resize Canvas (the anchor can push existing content, and the origin should move with it).</summary>
    public int OriginDeltaX { get; private set; }
    public int OriginDeltaY { get; private set; }

    /// <summary>Multiplicative origin scale - only meaningful for Scale Image (the whole picture, and therefore the origin's position on it, grows/shrinks proportionally).</summary>
    public double OriginScaleX { get; private set; } = 1.0;
    public double OriginScaleY { get; private set; } = 1.0;

    public ResizePropertiesWindow(BitmapSource source, string spriteName)
    {
        // ResizeCanvasRadio has IsChecked="True" in XAML, which fires its
        // Checked event (ModeRadio_Checked) synchronously right here, inside
        // InitializeComponent - before controls declared later in the same
        // XAML file (CanvasAspectCheck, CanvasUnitCombo, etc.) have actually
        // been connected to their fields yet. _initialized (false until just
        // after this call returns) makes that premature firing a harmless
        // no-op instead of a NullReferenceException.
        InitializeComponent();
        _initialized = true;

        _source = source;
        _oldW = source.PixelWidth;
        _oldH = source.PixelHeight;

        TitleText.Text = $"Resize Properties: {spriteName}";
        CurrentThumb.Source = source;
        CurrentWidthText.Text = _oldW.ToString();
        CurrentHeightText.Text = _oldH.ToString();

        ScaleUnitCombo.ItemsSource = new[] { "Pixels", "Percent" };
        ScaleUnitCombo.SelectedIndex = 0;
        CanvasUnitCombo.ItemsSource = new[] { "Pixels", "Percent" };
        CanvasUnitCombo.SelectedIndex = 0;
        InterpolationCombo.ItemsSource = _interpolationModes.Keys.ToList();
        InterpolationCombo.SelectedIndex = 0;

        _suppress = true;
        ScaleWidthBox.Text = _oldW.ToString();
        ScaleHeightBox.Text = _oldH.ToString();
        CanvasWidthBox.Text = _oldW.ToString();
        CanvasHeightBox.Text = _oldH.ToString();
        _suppress = false;

        BuildAnchorGrid();
        ModeRadio_Checked(this, new RoutedEventArgs());
    }

    private void BuildAnchorGrid()
    {
        string[] anchors = { "TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight" };
        string[] glyphs = { "\u2196", "\u2191", "\u2197", "\u2190", "\u25CF", "\u2192", "\u2199", "\u2193", "\u2198" };

        for (int i = 0; i < anchors.Length; i++)
        {
            string anchor = anchors[i];
            var btn = new Button
            {
                Content = glyphs[i],
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                Background = anchor == _anchor ? Brushes.SteelBlue : Brushes.Transparent,
                ToolTip = anchor
            };
            btn.Click += (_, _) =>
            {
                _anchor = anchor;
                foreach (var b in _anchorButtons) b.Background = Brushes.Transparent;
                btn.Background = Brushes.SteelBlue;
                UpdatePreview();
            };
            _anchorButtons.Add(btn);
            AnchorGrid.Children.Add(btn);
        }
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        bool scaleMode = ScaleImageRadio.IsChecked == true;
        ScaleWidthBox.IsEnabled = ScaleHeightBox.IsEnabled = ScaleAspectCheck.IsEnabled = ScaleUnitCombo.IsEnabled = InterpolationCombo.IsEnabled = scaleMode;
        CanvasWidthBox.IsEnabled = CanvasHeightBox.IsEnabled = CanvasAspectCheck.IsEnabled = CanvasUnitCombo.IsEnabled = !scaleMode;
        foreach (var b in _anchorButtons) b.IsEnabled = !scaleMode;
        UpdatePreview();
    }

    private void AnyOption_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    // ---------- Scale Image side ----------

    private void ScaleWidthBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (ScaleAspectCheck.IsChecked == true && double.TryParse(ScaleWidthBox.Text, out double w))
        {
            _suppress = true;
            ScaleHeightBox.Text = _scaleUnitIsPercent
                ? ScaleWidthBox.Text
                : Math.Max(1, (int)Math.Round(w * _oldH / (double)_oldW)).ToString();
            _suppress = false;
        }
        UpdatePreview();
    }

    private void ScaleHeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (ScaleAspectCheck.IsChecked == true && double.TryParse(ScaleHeightBox.Text, out double h))
        {
            _suppress = true;
            ScaleWidthBox.Text = _scaleUnitIsPercent
                ? ScaleHeightBox.Text
                : Math.Max(1, (int)Math.Round(h * _oldW / (double)_oldH)).ToString();
            _suppress = false;
        }
        UpdatePreview();
    }

    private void ScaleUnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool nowPercent = ScaleUnitCombo.SelectedIndex == 1;
        if (nowPercent != _scaleUnitIsPercent && double.TryParse(ScaleWidthBox.Text, out double w) && double.TryParse(ScaleHeightBox.Text, out double h))
        {
            _suppress = true;
            if (nowPercent)
            {
                ScaleWidthBox.Text = (w / _oldW * 100).ToString("0.##");
                ScaleHeightBox.Text = (h / _oldH * 100).ToString("0.##");
            }
            else
            {
                ScaleWidthBox.Text = Math.Max(1, (int)Math.Round(w / 100.0 * _oldW)).ToString();
                ScaleHeightBox.Text = Math.Max(1, (int)Math.Round(h / 100.0 * _oldH)).ToString();
            }
            _suppress = false;
        }
        _scaleUnitIsPercent = nowPercent;
        UpdatePreview();
    }

    private bool TryGetScaleSize(out int w, out int h)
    {
        w = h = 0;
        if (!double.TryParse(ScaleWidthBox.Text, out double wv) || !double.TryParse(ScaleHeightBox.Text, out double hv)) return false;
        w = _scaleUnitIsPercent ? Math.Max(1, (int)Math.Round(wv / 100.0 * _oldW)) : Math.Max(1, (int)Math.Round(wv));
        h = _scaleUnitIsPercent ? Math.Max(1, (int)Math.Round(hv / 100.0 * _oldH)) : Math.Max(1, (int)Math.Round(hv));
        return true;
    }

    // ---------- Resize Canvas side ----------

    private void CanvasWidthBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (CanvasAspectCheck.IsChecked == true && double.TryParse(CanvasWidthBox.Text, out double w))
        {
            _suppress = true;
            CanvasHeightBox.Text = _canvasUnitIsPercent
                ? CanvasWidthBox.Text
                : Math.Max(1, (int)Math.Round(w * _oldH / (double)_oldW)).ToString();
            _suppress = false;
        }
        UpdatePreview();
    }

    private void CanvasHeightBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (CanvasAspectCheck.IsChecked == true && double.TryParse(CanvasHeightBox.Text, out double h))
        {
            _suppress = true;
            CanvasWidthBox.Text = _canvasUnitIsPercent
                ? CanvasHeightBox.Text
                : Math.Max(1, (int)Math.Round(h * _oldW / (double)_oldH)).ToString();
            _suppress = false;
        }
        UpdatePreview();
    }

    private void CanvasUnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool nowPercent = CanvasUnitCombo.SelectedIndex == 1;
        if (nowPercent != _canvasUnitIsPercent && double.TryParse(CanvasWidthBox.Text, out double w) && double.TryParse(CanvasHeightBox.Text, out double h))
        {
            _suppress = true;
            if (nowPercent)
            {
                CanvasWidthBox.Text = (w / _oldW * 100).ToString("0.##");
                CanvasHeightBox.Text = (h / _oldH * 100).ToString("0.##");
            }
            else
            {
                CanvasWidthBox.Text = Math.Max(1, (int)Math.Round(w / 100.0 * _oldW)).ToString();
                CanvasHeightBox.Text = Math.Max(1, (int)Math.Round(h / 100.0 * _oldH)).ToString();
            }
            _suppress = false;
        }
        _canvasUnitIsPercent = nowPercent;
        UpdatePreview();
    }

    private bool TryGetCanvasSize(out int w, out int h)
    {
        w = h = 0;
        if (!double.TryParse(CanvasWidthBox.Text, out double wv) || !double.TryParse(CanvasHeightBox.Text, out double hv)) return false;
        w = _canvasUnitIsPercent ? Math.Max(1, (int)Math.Round(wv / 100.0 * _oldW)) : Math.Max(1, (int)Math.Round(wv));
        h = _canvasUnitIsPercent ? Math.Max(1, (int)Math.Round(hv / 100.0 * _oldH)) : Math.Max(1, (int)Math.Round(hv));
        return true;
    }

    // ---------- Preview / Apply ----------

    private void UpdatePreview()
    {
        if (_suppress) return;
        try
        {
            WriteableBitmap? preview = null;
            if (ScaleImageRadio.IsChecked == true && TryGetScaleSize(out int sw, out int sh))
            {
                var mode = _interpolationModes[(string)InterpolationCombo.SelectedItem];
                preview = ScaleImageBitmap(_source, sw, sh, mode);
            }
            else if (ResizeCanvasRadio.IsChecked == true && TryGetCanvasSize(out int cw, out int ch))
            {
                preview = ResizeCanvasBitmap(_source, cw, ch, _anchor);
            }

            if (preview is not null)
            {
                PreviewThumb.Source = preview;
                PreviewWidthText.Text = preview.PixelWidth.ToString();
                PreviewHeightText.Text = preview.PixelHeight.ToString();
            }
        }
        catch
        {
            // Transient invalid input mid-typing (blank box, stray characters) - just skip this preview tick.
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ScaleImageRadio.IsChecked == true)
            {
                if (!TryGetScaleSize(out int w, out int h))
                {
                    MessageBox.Show("Enter a valid width and height.", "Resize Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var mode = _interpolationModes[(string)InterpolationCombo.SelectedItem];
                ResultBitmap = ScaleImageBitmap(_source, w, h, mode);
                OriginScaleX = w / (double)_oldW;
                OriginScaleY = h / (double)_oldH;
                OriginDeltaX = 0;
                OriginDeltaY = 0;
            }
            else
            {
                if (!TryGetCanvasSize(out int w, out int h))
                {
                    MessageBox.Show("Enter a valid width and height.", "Resize Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ResultBitmap = ResizeCanvasBitmap(_source, w, h, _anchor);
                var (dx, dy) = AnchorOffset(_anchor, _oldW, _oldH, w, h);
                OriginDeltaX = dx;
                OriginDeltaY = dy;
                OriginScaleX = 1.0;
                OriginScaleY = 1.0;
            }

            ApplyToOrigin = ApplyToOriginCheck.IsChecked == true;
            Result = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not resize: {ex.Message}", "Resize Properties", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---------- Pixel math ----------

    /// <summary>How far the old content's top-left corner lands inside the new canvas, given the selected anchor. Negative values mean that side gets cropped away; positive values mean that side gets padded with transparency.</summary>
    private static (int dx, int dy) AnchorOffset(string anchor, int oldW, int oldH, int newW, int newH)
    {
        int dx = anchor switch
        {
            "TopLeft" or "MiddleLeft" or "BottomLeft" => 0,
            "TopCenter" or "MiddleCenter" or "BottomCenter" => (newW - oldW) / 2,
            _ => newW - oldW
        };
        int dy = anchor switch
        {
            "TopLeft" or "TopCenter" or "TopRight" => 0,
            "MiddleLeft" or "MiddleCenter" or "MiddleRight" => (newH - oldH) / 2,
            _ => newH - oldH
        };
        return (dx, dy);
    }

    /// <summary>Resize Canvas: the existing pixels keep their original size exactly - this only changes how much canvas surrounds them (cropping or padding with transparency), anchored per AnchorOffset.</summary>
    private static WriteableBitmap ResizeCanvasBitmap(BitmapSource source, int newW, int newH, string anchor)
    {
        int oldW = source.PixelWidth, oldH = source.PixelHeight;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int oldStride = oldW * 4;
        var oldPixels = new byte[oldH * oldStride];
        converted.CopyPixels(oldPixels, oldStride, 0);

        newW = Math.Max(1, newW);
        newH = Math.Max(1, newH);
        int newStride = newW * 4;
        var newPixels = new byte[newH * newStride];

        var (dx, dy) = AnchorOffset(anchor, oldW, oldH, newW, newH);

        int copyStartX = Math.Max(0, -dx);
        int copyEndX = Math.Min(oldW, newW - dx);
        if (copyEndX > copyStartX)
        {
            int copyLen = (copyEndX - copyStartX) * 4;
            for (int y = 0; y < oldH; y++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= newH) continue;
                int srcOffset = y * oldStride + copyStartX * 4;
                int dstOffset = ny * newStride + (copyStartX + dx) * 4;
                Array.Copy(oldPixels, srcOffset, newPixels, dstOffset, copyLen);
            }
        }

        var wb = new WriteableBitmap(newW, newH, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, newW, newH), newPixels, newStride, 0);
        return wb;
    }

    /// <summary>Scale Image: resamples the actual pixel content to the new size using the chosen interpolation - unlike Resize Canvas, this genuinely stretches/shrinks the art.</summary>
    private static WriteableBitmap ScaleImageBitmap(BitmapSource source, int newW, int newH, BitmapScalingMode mode)
    {
        newW = Math.Max(1, newW);
        newH = Math.Max(1, newH);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, mode);
            dc.DrawImage(source, new Rect(0, 0, newW, newH));
        }
        var rtb = new RenderTargetBitmap(newW, newH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        int stride = newW * 4;
        var pixels = new byte[newH * stride];
        converted.CopyPixels(pixels, stride, 0);

        var wb = new WriteableBitmap(newW, newH, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, newW, newH), pixels, stride, 0);
        return wb;
    }
}
