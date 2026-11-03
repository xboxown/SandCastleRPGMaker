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
        if (settings.Style == SliderStyle.Custom) { BuildCustom(settings.Custom, sprites); return; } // a slider made from pieces (below); the classic one follows

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
        // Frame 2 of its sprite while pressed; it accepts when let go over it (Exult's Gump_button), not on the press.
        var checkArt = Load(sprites, settings.CheckMark);
        var checkSprite = settings.CheckMark.IsSet ? ObjectLooks.Find(sprites, settings.CheckMark.SpriteGroup, settings.CheckMark.SpriteName) : null;
        var checkPressed = checkSprite is { Frames.Count: > 1 } ? SolidSizeEditor.LoadImageFile(checkSprite.Frames[1].FilePath) : null;
        Image? checkImage = checkArt is not null ? new Image { Source = checkArt, Width = checkArt.PixelWidth, Height = checkArt.PixelHeight } : null;
        FrameworkElement check = checkImage ?? (FrameworkElement)new TextBlock { Text = "✔", Foreground = Brushes.LimeGreen, FontSize = 9, FontWeight = FontWeights.Bold };
        check.Cursor = Cursors.Hand;
        check.ToolTip = "Accept (Enter)";
        bool checkHeld = false;
        check.MouseLeftButtonDown += (_, e) =>
        {
            if (checkImage is not null && checkPressed is not null) checkImage.Source = checkPressed;
            checkHeld = true;
            check.CaptureMouse();
            e.Handled = true;
        };
        check.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!checkHeld) return;
            checkHeld = false;
            check.ReleaseMouseCapture();
            if (checkImage is not null) checkImage.Source = checkArt;
            var at = e.GetPosition(check);
            if (at.X >= 0 && at.Y >= 0 && at.X <= check.ActualWidth && at.Y <= check.ActualHeight) Accepted?.Invoke(_value); // dragged off before letting go: no
        };
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
        if (_custom is not null) { ShowCustomValue(playSound); return; }
        double t = _max == _min ? 0 : (_value - _min) / (double)(_max - _min);
        SetLeft(_diamond, _xmin + t * (_xmax - _xmin));
        if (_number is not null) Children.Remove(_number);
        _number = _numberLabel(_value.ToString());
        _number.IsHitTestVisible = false;
        _number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Add(_number, _settings.NumberX - _number.DesiredSize.Width, _settings.NumberY); // right-aligned, ending at NumberX
        if (playSound && _settings.ClickSoundResourceId >= 0 && _resources is not null) SoundPlayback.TryPlay(_resources, _settings.ClickSoundResourceId);
    }

    // ================= Custom style (System Settings > Slider > Custom) =================

    private CustomSliderDesign? _custom;
    private SpriteDatabase? _sprites;
    private SliderPiece? _numberPiece;
    private readonly List<FrameworkElement> _digitVisuals = new();
    private SliderPiece? _trackPiece;
    private FrameworkElement? _thumb;
    private double _thumbWidth;
    private readonly System.Windows.Threading.DispatcherTimer _repeat = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private int _repeatDirection;

    /// <summary>A slider made from pieces placed on a background: buttons (frame 2 while pressed, a sound each, held down they repeat), the number (a digit sprite or the game font), a track with its thumb, decorations.</summary>
    private void BuildCustom(CustomSliderDesign design, SpriteDatabase sprites)
    {
        _custom = design;
        _sprites = sprites;
        _repeat.Tick += (_, _) => { _repeat.Interval = TimeSpan.FromMilliseconds(110); Step(_repeatDirection); };

        var background = Load(sprites, design.Background);
        double width = background?.PixelWidth ?? 0, height = background?.PixelHeight ?? 0;
        if (background is null)
        {
            // No background: a plain panel round the pieces.
            foreach (var piece in design.Pieces)
            {
                var art = Load(sprites, piece.Sprite);
                double pw = piece.Kind == SliderPieceKind.Track ? piece.Width : art?.PixelWidth ?? 14, ph = art?.PixelHeight ?? 12;
                if (piece.Kind == SliderPieceKind.Number) pw = Math.Max(pw * Math.Max(1, piece.DigitCount), 18);
                width = Math.Max(width, piece.X + pw + 4);
                height = Math.Max(height, piece.Y + ph + 4);
            }
            Add(new Rectangle { Width = Math.Max(20, width), Height = Math.Max(12, height), Fill = new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3A)), Stroke = Brushes.Black, StrokeThickness = 1 }, 0, 0);
        }
        else Add(new Image { Source = background, Width = width, Height = height }, 0, 0);
        Width = Math.Max(20, width);
        Height = Math.Max(12, height);

        foreach (var piece in design.Pieces)
        {
            switch (piece.Kind)
            {
                case SliderPieceKind.Decrease:
                case SliderPieceKind.Increase:
                case SliderPieceKind.Accept:
                case SliderPieceKind.Cancel:
                    Add(CustomButton(piece), piece.X, piece.Y);
                    break;
                case SliderPieceKind.Number:
                    _numberPiece = piece;
                    break;
                case SliderPieceKind.Track:
                {
                    _trackPiece = piece;
                    var art = Load(sprites, piece.Sprite);
                    _thumbWidth = art?.PixelWidth ?? 6;
                    double thumbHeight = art?.PixelHeight ?? 8;
                    var track = new Rectangle { Width = Math.Max(1, piece.Width), Height = thumbHeight, Fill = Brushes.Transparent, Cursor = Cursors.Hand };
                    track.MouseLeftButtonDown += (_, e) => { SetFromTrack(e.GetPosition(this).X - _thumbWidth / 2); e.Handled = true; };
                    Add(track, piece.X, piece.Y);
                    if (art is null) Add(new Rectangle { Width = Math.Max(1, piece.Width), Height = 1, Fill = Brushes.LightGray, IsHitTestVisible = false }, piece.X, piece.Y + thumbHeight / 2);
                    _thumb = art is not null ? new Image { Source = art, Width = _thumbWidth, Height = thumbHeight } : DiamondShape(_thumbWidth, thumbHeight);
                    _thumb.Cursor = Cursors.SizeWE;
                    _thumb.MouseLeftButtonDown += (_, e) => { _dragging = true; CaptureMouse(); e.Handled = true; };
                    Add(_thumb, piece.X, piece.Y);
                    MouseMove += (_, e) => { if (_dragging) SetFromTrack(e.GetPosition(this).X - _thumbWidth / 2); };
                    MouseLeftButtonUp += (_, _) => { _dragging = false; ReleaseMouseCapture(); };
                    break;
                }
                default:
                    if (Load(sprites, piece.Sprite) is { } decoration) Add(new Image { Source = decoration, Width = decoration.PixelWidth, Height = decoration.PixelHeight, IsHitTestVisible = false }, piece.X, piece.Y);
                    break;
            }
        }
        ShowCustomValue(playSound: false);
    }

    /// <summary>A button piece: its picture (frame 2 while pressed), its sound, and what it does; minus and plus repeat while held.</summary>
    private FrameworkElement CustomButton(SliderPiece piece)
    {
        var sprite = piece.Sprite.IsSet && _sprites is not null ? ObjectLooks.Find(_sprites, piece.Sprite.SpriteGroup, piece.Sprite.SpriteName) : null;
        var normal = sprite is { Frames.Count: > 0 } ? SolidSizeEditor.LoadImageFile(sprite.Frames[0].FilePath) : null;
        var pressed = sprite is { Frames.Count: > 1 } ? SolidSizeEditor.LoadImageFile(sprite.Frames[1].FilePath) : null;
        FrameworkElement visual;
        Image? image = null;
        if (normal is not null) visual = image = new Image { Source = normal, Width = normal.PixelWidth, Height = normal.PixelHeight };
        else
        {
            string text = piece.Kind switch { SliderPieceKind.Decrease => "-", SliderPieceKind.Increase => "+", SliderPieceKind.Accept => "✔", _ => "✕" };
            visual = new Border { Width = 12, Height = 11, Background = new SolidColorBrush(Color.FromRgb(0x55, 0x60, 0x6A)), BorderBrush = Brushes.Black, BorderThickness = new Thickness(0.5),
                Child = new TextBlock { Text = text, FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        }
        visual.Cursor = Cursors.Hand;
        visual.ToolTip = piece.Kind switch { SliderPieceKind.Decrease => "Less (Left)", SliderPieceKind.Increase => "More (Right)", SliderPieceKind.Accept => "Accept (Enter)", _ => "Cancel (Escape)" };
        visual.MouseLeftButtonDown += (_, e) =>
        {
            if (image is not null && pressed is not null) image.Source = pressed;
            if (piece.SoundResourceId >= 0 && _resources is not null) SoundPlayback.TryPlay(_resources, piece.SoundResourceId);
            switch (piece.Kind)
            {
                case SliderPieceKind.Decrease: StartRepeat(-1); break;
                case SliderPieceKind.Increase: StartRepeat(+1); break;
                case SliderPieceKind.Accept: Accepted?.Invoke(_value); break;
                default: Cancelled?.Invoke(); break;
            }
            visual.CaptureMouse();
            e.Handled = true;
        };
        visual.MouseLeftButtonUp += (_, _) =>
        {
            if (image is not null && normal is not null) image.Source = normal;
            _repeat.Stop();
            visual.ReleaseMouseCapture();
        };
        return visual;
    }

    private void StartRepeat(int direction)
    {
        Step(direction);
        _repeatDirection = direction;
        _repeat.Interval = TimeSpan.FromMilliseconds(400); // held down: after a moment it keeps going
        _repeat.Start();
    }

    private void SetFromTrack(double x)
    {
        if (_trackPiece is null) return;
        double span = Math.Max(1, _trackPiece.Width - _thumbWidth);
        double t = Math.Clamp((x - _trackPiece.X) / span, 0, 1);
        int raw = _min + (int)Math.Round(t * (_max - _min));
        SetValue(_min + (int)Math.Round((raw - _min) / (double)_step) * _step);
    }

    /// <summary>The number in its digits (or the game font), the thumb on its track, and the digit's (or the change) sound.</summary>
    private void ShowCustomValue(bool playSound)
    {
        foreach (var old in _digitVisuals) Children.Remove(old);
        _digitVisuals.Clear();
        if (_numberPiece is { } piece)
        {
            var sprite = piece.Sprite.IsSet && _sprites is not null ? ObjectLooks.Find(_sprites, piece.Sprite.SpriteGroup, piece.Sprite.SpriteName) : null;
            string digits = _value.ToString();
            if (piece.DigitCount > 0) digits = piece.LeadingZeros ? digits.PadLeft(piece.DigitCount, '0') : digits.PadLeft(piece.DigitCount, ' ');
            if (sprite is { Frames.Count: >= 10 })
            {
                double x = piece.X;
                foreach (char c in digits)
                {
                    int frame = c == ' ' ? 10 : c - '0';
                    var bitmap = frame < sprite.Frames.Count ? SolidSizeEditor.LoadImageFile(sprite.Frames[frame].FilePath) : null;
                    double w = bitmap?.PixelWidth ?? SolidSizeEditor.LoadImageFile(sprite.Frames[0].FilePath)?.PixelWidth ?? 6;
                    if (bitmap is not null)
                    {
                        var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, IsHitTestVisible = false };
                        Add(image, x, piece.Y);
                        _digitVisuals.Add(image);
                    }
                    x += w + piece.DigitSpacing;
                }
            }
            else
            {
                var label = _numberLabel(digits.Trim().Length == 0 ? "0" : digits);
                label.IsHitTestVisible = false;
                Add(label, piece.X, piece.Y);
                _digitVisuals.Add(label);
            }
        }
        if (_thumb is not null && _trackPiece is not null)
        {
            double t = _max == _min ? 0 : (_value - _min) / (double)(_max - _min);
            SetLeft(_thumb, _trackPiece.X + t * Math.Max(0, _trackPiece.Width - _thumbWidth));
        }
        if (!playSound || _custom is null || _resources is null) return;
        int digitSound = _value % 10 < _custom.DigitSoundIds.Count ? _custom.DigitSoundIds[_value % 10] : -1;
        int sound = digitSound >= 0 ? digitSound : _custom.ChangeSoundResourceId;
        if (sound >= 0) SoundPlayback.TryPlay(_resources, sound);
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
