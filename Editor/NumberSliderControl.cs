using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima VII's number slider, as Exult draws it (gumps/Slider_gump.cc,
/// Slider_widget.cc): a background; at the slider position a left arrow, a
/// track of TrackWidth pixels with a diamond on it, and a right arrow; the
/// number, right-aligned; and a check mark. The arrows step by Step, the
/// diamond can be dragged or the track clicked (snapping to Step), Left and
/// Right keys step, Enter accepts, Escape cancels. Drawn at its own pixel
/// size - the game scales it like the rest of the screen. Its look is System
/// Settings > Slider; without sprites it draws a plain stand-in.
/// </summary>
public class NumberSliderControl : Canvas
{
    private readonly SliderSettings _settings;
    private readonly int _min, _max, _step;
    private readonly Func<string, FrameworkElement> _numberLabel;
    private readonly ResourceDatabase? _resources;
    private int _value;
    private double _xmin, _xmax;
    private FrameworkElement _diamond = null!;
    private FrameworkElement? _number;
    private bool _dragging;

    public int Value => _value;
    public event Action<int>? ValueChanged;
    public event Action<int>? Accepted;
    public event Action? Cancelled;

    /// <param name="numberLabel">Draws the number (the game's own font in play; plain text in the editor).</param>
    public NumberSliderControl(SliderSettings settings, SpriteDatabase sprites, ResourceDatabase? resources, int min, int max, int step, int value,
        Func<string, FrameworkElement>? numberLabel = null)
    {
        _settings = settings;
        _resources = resources;
        _min = Math.Clamp(Math.Min(min, max), 0, SliderSettings.MaxValue);
        _max = Math.Clamp(Math.Max(min, max), 0, SliderSettings.MaxValue);
        _step = Math.Max(1, step);
        _value = Math.Clamp(value, _min, _max);
        _numberLabel = numberLabel ?? (text => new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 9 });
        ClipToBounds = false;
        Focusable = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        // ---------- Background ----------
        var background = Load(sprites, settings.Background);
        double width = background?.PixelWidth ?? Math.Max(settings.NumberX + 8, settings.SliderX + settings.TrackWidth + 30);
        double height = background?.PixelHeight ?? Math.Max(settings.CheckY + 12, settings.SliderY + 20);
        Width = width;
        Height = height;
        if (background is not null) Add(new Image { Source = background, Width = width, Height = height }, 0, 0);
        else Add(new Rectangle { Width = width, Height = height, Fill = new SolidColorBrush(Color.FromRgb(0x6B, 0x4A, 0x2B)), Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x1E, 0x10)), StrokeThickness = 1 }, 0, 0);

        // ---------- Left arrow, track, right arrow (Slider_widget's layout) ----------
        var leftArt = Load(sprites, settings.LeftArrow);
        var rightArt = Load(sprites, settings.RightArrow);
        var diamondArt = Load(sprites, settings.Diamond);
        double leftW = leftArt?.PixelWidth ?? 7, leftH = leftArt?.PixelHeight ?? 9;
        double diamondW = diamondArt?.PixelWidth ?? 7, diamondH = diamondArt?.PixelHeight ?? 7;
        double leftX = settings.SliderX, y = settings.SliderY;
        _xmin = leftX + leftW + 1;
        _xmax = _xmin + settings.TrackWidth - diamondW;
        double rightX = leftX + leftW + settings.TrackWidth + 1;

        if (leftArt is null && rightArt is null)
            Add(new Rectangle { Width = settings.TrackWidth, Height = 1, Fill = Brushes.Black }, _xmin, y + leftH / 2); // the stand-in's track line
        var left = leftArt is not null ? (FrameworkElement)new Image { Source = leftArt, Width = leftW, Height = leftH } : Arrow(pointsLeft: true, leftW, leftH);
        var right = rightArt is not null ? (FrameworkElement)new Image { Source = rightArt, Width = rightArt.PixelWidth, Height = rightArt.PixelHeight } : Arrow(pointsLeft: false, leftW, leftH);
        left.Cursor = right.Cursor = Cursors.Hand;
        left.MouseLeftButtonDown += (_, e) => { Step(-1); e.Handled = true; };
        right.MouseLeftButtonDown += (_, e) => { Step(+1); e.Handled = true; };
        Add(left, leftX, y);
        Add(right, rightX, y);

        // The track: clicking it puts the diamond there.
        var track = new Rectangle { Width = Math.Max(1, settings.TrackWidth), Height = Math.Max(leftH, diamondH), Fill = Brushes.Transparent, Cursor = Cursors.Hand };
        track.MouseLeftButtonDown += (_, e) => { SetFromX(e.GetPosition(this).X - diamondW / 2); e.Handled = true; };
        Add(track, _xmin, y);

        _diamond = diamondArt is not null ? new Image { Source = diamondArt, Width = diamondW, Height = diamondH } : DiamondShape(diamondW, diamondH);
        _diamond.Cursor = Cursors.SizeWE;
        _diamond.MouseLeftButtonDown += (_, e) => { _dragging = true; CaptureMouse(); e.Handled = true; };
        Add(_diamond, _xmin, y + (leftH - diamondH) / 2); // centred between the buttons' height (Exult)
        MouseMove += (_, e) => { if (_dragging) SetFromX(e.GetPosition(this).X - diamondW / 2); };
        MouseLeftButtonUp += (_, _) => { _dragging = false; ReleaseMouseCapture(); };

        // ---------- The check mark ----------
        var checkArt = Load(sprites, settings.CheckMark);
        var check = checkArt is not null ? (FrameworkElement)new Image { Source = checkArt, Width = checkArt.PixelWidth, Height = checkArt.PixelHeight }
            : new TextBlock { Text = "✔", Foreground = Brushes.LimeGreen, FontSize = 9, FontWeight = FontWeights.Bold };
        check.Cursor = Cursors.Hand;
        check.ToolTip = "Accept (Enter)";
        check.MouseLeftButtonDown += (_, e) => { Accepted?.Invoke(_value); e.Handled = true; };
        Add(check, settings.CheckX, settings.CheckY);

        ShowValue(playSound: false);
    }

    /// <summary>Left / Right step, Enter accepts, Escape cancels (Slider_gump::key_down). True if it was one of them.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Left: Step(-1); return true;
            case Key.Right: Step(+1); return true;
            case Key.Enter: Accepted?.Invoke(_value); return true;
            case Key.Escape: Cancelled?.Invoke(); return true;
        }
        return false;
    }

    private void Step(int direction) => SetValue(_value + direction * _step);

    /// <summary>The diamond dragged (or the track clicked) to x: the value there, snapped to the step.</summary>
    private void SetFromX(double x)
    {
        double t = _xmax <= _xmin ? 0 : Math.Clamp((x - _xmin) / (_xmax - _xmin), 0, 1);
        int raw = _min + (int)Math.Round(t * (_max - _min));
        SetValue(_min + (int)Math.Round((raw - _min) / (double)_step) * _step);
    }

    private void SetValue(int value)
    {
        value = Math.Clamp(value, _min, _max);
        if (value == _value) return;
        _value = value;
        ShowValue(playSound: true);
        ValueChanged?.Invoke(_value);
    }

    private void ShowValue(bool playSound)
    {
        double t = _max == _min ? 0 : (_value - _min) / (double)(_max - _min);
        SetLeft(_diamond, _xmin + t * (_xmax - _xmin));
        if (_number is not null) Children.Remove(_number);
        _number = _numberLabel(_value.ToString());
        _number.IsHitTestVisible = false;
        _number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Add(_number, _settings.NumberX - _number.DesiredSize.Width, _settings.NumberY); // right-aligned, ending at NumberX
        if (playSound && _settings.ClickSoundResourceId >= 0 && _resources is not null) SoundPlayback.TryPlay(_resources, _settings.ClickSoundResourceId);
    }

    private void Add(FrameworkElement element, double x, double y)
    {
        if (element is Image image) RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        SetLeft(element, x);
        SetTop(element, y);
        Children.Add(element);
    }

    private static BitmapSource? Load(SpriteDatabase sprites, SpriteLink link) =>
        link.IsSet ? SolidSizeEditor.LoadSpriteFrame(sprites, link.SpriteGroup, link.SpriteName) : null;

    private static FrameworkElement Arrow(bool pointsLeft, double w, double h)
    {
        var shape = new Polygon { Fill = new SolidColorBrush(Color.FromRgb(0xE8, 0xD8, 0xA8)), Stroke = Brushes.Black, StrokeThickness = 0.5, Width = w, Height = h };
        shape.Points = pointsLeft
            ? new PointCollection { new(w, 0), new(0, h / 2), new(w, h) }
            : new PointCollection { new(0, 0), new(w, h / 2), new(0, h) };
        return shape;
    }

    private static FrameworkElement DiamondShape(double w, double h) => new Polygon
    {
        Fill = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x30)), Stroke = Brushes.Black, StrokeThickness = 0.5, Width = w, Height = h,
        Points = new PointCollection { new(w / 2, 0), new(w, h / 2), new(w / 2, h), new(0, h / 2) }
    };
}
