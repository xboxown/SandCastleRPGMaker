using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8: a sprite's frames standing on their stand points (StandPoints) as the game stands them - the Sprite Editor's
/// frame over the one before it (see-through), on one spot (the cyan cross). Drag the frame (or the arrow keys) until its
/// body lines up with the one before: that sets its stand point. Play shows the whole animation in place.
/// </summary>
public class StandPointPreviewWindow : Window
{
    private readonly SpriteEntry _sprite;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
    private readonly CheckBox _ghostCheck = new() { Content = "Show the frame before (see-through)", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly Button _playButton = new() { Content = "Play", Padding = new Thickness(10, 2, 10, 2) };
    private readonly TextBlock _info = new() { Margin = new Thickness(8, 4, 8, 6), TextWrapping = TextWrapping.Wrap };
    private int _frameIndex;
    private int? _playing;           // the frame shown while playing
    private DispatcherTimer? _timer;
    private double _spotX, _spotY;   // the stand spot on the canvas
    private Point? _dragFrom;

    /// <summary>A frame's stand point was moved here: the Sprite Editor shows it.</summary>
    public event Action? StandMoved;

    public StandPointPreviewWindow(SpriteEntry sprite)
    {
        _sprite = sprite;
        Title = $"Stand point preview: {sprite.Name}";
        Width = 420;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var top = new WrapPanel { Margin = new Thickness(8, 8, 8, 4) };
        top.Children.Add(_playButton);
        top.Children.Add(_ghostCheck);
        var hint = new TextBlock { Text = "Drag the frame (or the arrow keys) until it lines up with the one before: that's its stand point.", Margin = new Thickness(8, 0, 8, 4), Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var view = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas, Margin = new Thickness(8) };
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.NearestNeighbor);
        var stage = new Border { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), Margin = new Thickness(8, 0, 8, 0), Child = view };
        var root = new DockPanel();
        foreach (var part in new FrameworkElement[] { top, hint }) { DockPanel.SetDock(part, Dock.Top); root.Children.Add(part); }
        DockPanel.SetDock(_info, Dock.Bottom);
        root.Children.Add(_info);
        root.Children.Add(stage);
        Content = root;

        _playButton.Click += (_, _) => { if (_timer is null) StartPlaying(); else StopPlaying(); };
        _ghostCheck.Checked += (_, _) => Redraw();
        _ghostCheck.Unchecked += (_, _) => Redraw();
        _canvas.MouseLeftButtonDown += (_, e) => { if (_playing is not null) return; _dragFrom = e.GetPosition(_canvas); _canvas.CaptureMouse(); e.Handled = true; };
        _canvas.MouseMove += (_, e) =>
        {
            if (_dragFrom is not { } from) return;
            var at = e.GetPosition(_canvas);
            int dx = (int)Math.Round(at.X - from.X), dy = (int)Math.Round(at.Y - from.Y);
            if (dx == 0 && dy == 0) return;
            _dragFrom = new Point(from.X + dx, from.Y + dy);
            MoveFrame(dx, dy);
        };
        _canvas.MouseLeftButtonUp += (_, _) => { _dragFrom = null; _canvas.ReleaseMouseCapture(); };
        PreviewKeyDown += (_, e) =>
        {
            var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
            if ((dx, dy) == (0, 0) || _playing is not null) return;
            MoveFrame(dx, dy);
            e.Handled = true;
        };
        Closed += (_, _) => StopPlaying();
    }

    /// <summary>The editor's frame now (or its stand point changed): drawn again.</summary>
    public void ShowFrame(int index)
    {
        _frameIndex = index;
        if (_playing is null) Redraw();
    }

    /// <summary>The frame's picture moved by (dx, dy): its stand point the other way, so it now stands there.</summary>
    private void MoveFrame(int dx, int dy)
    {
        if (_frameIndex < 0 || _frameIndex >= _sprite.Frames.Count) return;
        var frame = _sprite.Frames[_frameIndex];
        if (FrameCache.Get(frame.FilePath) is not { } picture) return;
        var (sx, sy) = StandPoints.Of(frame, picture);
        frame.AnchorX = (int)Math.Round(sx - dx);
        frame.AnchorY = (int)Math.Round(sy - dy);
        Redraw();
        StandMoved?.Invoke();
    }

    private void StartPlaying()
    {
        if (_sprite.Frames.Count < 2) return;
        _playing = 0;
        _playButton.Content = "Stop";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _sprite.Fps)) };
        _timer.Tick += (_, _) => { _playing = (_playing + 1) % _sprite.Frames.Count; Redraw(); };
        _timer.Start();
        Redraw();
    }

    private void StopPlaying()
    {
        _timer?.Stop();
        _timer = null;
        _playing = null;
        _playButton.Content = "Play";
        Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        int count = _sprite.Frames.Count;
        if (count == 0) { _info.Text = "No frames."; return; }
        // One stage for all the frames: room round the spot for the widest and tallest of them, so it doesn't jump about.
        double left = 8, right = 8, up = 8, down = 8;
        foreach (var f in _sprite.Frames)
            if (FrameCache.Get(f.FilePath) is { } p)
            {
                var (sx, sy) = StandPoints.Of(f, p);
                left = Math.Max(left, sx + 8); right = Math.Max(right, p.PixelWidth - sx + 8);
                up = Math.Max(up, sy + 8); down = Math.Max(down, p.PixelHeight - sy + 8);
            }
        double half = Math.Max(left, right);
        _canvas.Width = half * 2;
        _canvas.Height = up + down;
        (_spotX, _spotY) = (half, up);
        _canvas.Children.Add(new Line { X1 = 0, X2 = _canvas.Width, Y1 = _spotY, Y2 = _spotY, Stroke = Brushes.Gray, StrokeThickness = 0.3, StrokeDashArray = new DoubleCollection { 4, 4 } }); // the ground

        int shown = Math.Clamp(_playing ?? _frameIndex, 0, count - 1);
        if (_playing is null && _ghostCheck.IsChecked == true && count > 1) AddFrame((shown - 1 + count) % count, 0.35);
        AddFrame(shown, 1);
        _canvas.Children.Add(new Line { X1 = _spotX - 4, X2 = _spotX + 4, Y1 = _spotY, Y2 = _spotY, Stroke = Brushes.Cyan, StrokeThickness = 0.6 });
        _canvas.Children.Add(new Line { X1 = _spotX, X2 = _spotX, Y1 = _spotY - 4, Y2 = _spotY + 4, Stroke = Brushes.Cyan, StrokeThickness = 0.6 });

        var frame = _sprite.Frames[shown];
        _info.Text = _playing is not null ? $"Playing - frame {shown + 1} / {count}."
            : $"Frame {shown + 1} / {count}: " + (frame.HasAnchor ? $"stands at {frame.AnchorX}, {frame.AnchorY}." : "stands on its bottom-middle (no stand point yet).")
              + (count > 1 && _ghostCheck.IsChecked == true ? $" See-through: frame {(shown - 1 + count) % count + 1}." : "");
    }

    /// <summary>A frame standing on the spot (its stand point on the cyan cross).</summary>
    private void AddFrame(int index, double opacity)
    {
        var frame = _sprite.Frames[index];
        if (FrameCache.Get(frame.FilePath) is not { } picture) return;
        var (sx, sy) = StandPoints.Of(frame, picture);
        var image = new Image { Source = picture, Width = picture.PixelWidth, Height = picture.PixelHeight, Stretch = Stretch.Fill, Opacity = opacity, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, _spotX - sx);
        Canvas.SetTop(image, _spotY - sy);
        _canvas.Children.Add(image);
    }
}
