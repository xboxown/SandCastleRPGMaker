using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A real color picker with hue/saturation/value plus an alpha slider -
/// directly replacing the system ColorDialog for cases that need
/// transparency (e.g. drawing translucent glass), since the system dialog
/// has no alpha channel UI at all.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private double _hue; // 0-360
    private double _saturation = 1; // 0-1
    private double _value = 1; // 0-1
    private int _alpha = 255; // 0-255
    private bool _suppress;
    private bool _draggingSv;
    private bool _draggingHue;

    public Color Result { get; private set; }

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();

        (_hue, _saturation, _value) = RgbToHsv(initial.R, initial.G, initial.B);
        _alpha = initial.A;

        BuildCheckerPattern(AlphaCheckerBackground);
        BuildCheckerPattern(PreviewCheckerBackground);

        _suppress = true;
        AlphaSlider.Value = _alpha;
        _suppress = false;

        UpdateAll();
    }

    private static Brush BuildCheckerBrush()
    {
        const int tile = 6;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, tile * 2, tile * 2));
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, tile, tile));
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(tile, tile, tile, tile));
        }
        return new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, tile * 2, tile * 2), ViewportUnits = BrushMappingMode.Absolute };
    }

    private static void BuildCheckerPattern(Border target) => target.Background = BuildCheckerBrush();

    // ---------- HSV <-> RGB ----------

    private static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta > 0.0001)
        {
            if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / delta) + 2);
            else h = 60 * (((rd - gd) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max <= 0.0001 ? 0 : delta / max;
        double v = max;
        return (h, s, v);
    }

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private Color CurrentColor()
    {
        var (r, g, b) = HsvToRgb(_hue, _saturation, _value);
        return Color.FromArgb((byte)_alpha, r, g, b);
    }

    // ---------- Redraw everything from current H/S/V/A ----------

    private void UpdateAll()
    {
        // AlphaSlider's default Value in XAML fires ValueChanged during
        // InitializeComponent(), before PreviewSwatch (declared later in
        // the same window) has been assigned to its field yet - this gets
        // called again properly once the constructor finishes.
        if (PreviewSwatch is null) return;

        var (baseR, baseG, baseB) = HsvToRgb(_hue, 1, 1);
        HueBackground.Background = new SolidColorBrush(Color.FromRgb(baseR, baseG, baseB));

        double svW = SvCanvas.ActualWidth > 0 ? SvCanvas.ActualWidth : 190;
        double svH = SvCanvas.ActualHeight > 0 ? SvCanvas.ActualHeight : 220;
        Canvas.SetLeft(SvCursor, _saturation * svW - SvCursor.Width / 2);
        Canvas.SetTop(SvCursor, (1 - _value) * svH - SvCursor.Height / 2);

        double hueH = HueCanvas.ActualHeight > 0 ? HueCanvas.ActualHeight : 220;
        Canvas.SetTop(HueCursor, _hue / 360.0 * hueH - HueCursor.Height / 2);
        Canvas.SetLeft(HueCursor, 0);

        var current = CurrentColor();
        var opaque = Color.FromRgb(current.R, current.G, current.B);
        AlphaGradientOverlay.Background = new LinearGradientBrush(Colors.Transparent, opaque, new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        PreviewSwatch.Background = new SolidColorBrush(current);

        if (!_suppress)
        {
            _suppress = true;
            HexBox.Text = $"{current.R:X2}{current.G:X2}{current.B:X2}{current.A:X2}";
            _suppress = false;
        }
    }

    // ---------- SV square ----------

    private void SvCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = true;
        SvCanvas.CaptureMouse();
        UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingSv) UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = false;
        SvCanvas.ReleaseMouseCapture();
    }

    private void UpdateSvFromMouse(System.Windows.Point p)
    {
        double w = SvCanvas.ActualWidth, h = SvCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _saturation = Math.Clamp(p.X / w, 0, 1);
        _value = 1 - Math.Clamp(p.Y / h, 0, 1);
        UpdateAll();
    }

    // ---------- Hue slider ----------

    private void HueCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueCanvas.CaptureMouse();
        UpdateHueFromMouse(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingHue) UpdateHueFromMouse(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = false;
        HueCanvas.ReleaseMouseCapture();
    }

    private void UpdateHueFromMouse(System.Windows.Point p)
    {
        double h = HueCanvas.ActualHeight;
        if (h <= 0) return;
        _hue = Math.Clamp(p.Y / h, 0, 1) * 360;
        UpdateAll();
    }

    // ---------- Alpha + hex ----------

    private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        _alpha = (int)AlphaSlider.Value;
        UpdateAll();
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        string text = HexBox.Text.Trim().TrimStart('#');
        if (text.Length != 8 || !int.TryParse(text[..2], System.Globalization.NumberStyles.HexNumber, null, out int r)
            || !int.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out int g)
            || !int.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out int b)
            || !int.TryParse(text[6..8], System.Globalization.NumberStyles.HexNumber, null, out int a))
            return;

        (_hue, _saturation, _value) = RgbToHsv((byte)r, (byte)g, (byte)b);
        _alpha = a;
        _suppress = true;
        AlphaSlider.Value = _alpha;
        _suppress = false;
        UpdateAll();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = CurrentColor();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
