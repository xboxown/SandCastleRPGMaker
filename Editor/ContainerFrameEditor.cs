using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A container's item frame, drawn over its open-view paperdoll - the same
/// tool, coordinates and rules as the Container Editor's Item Frame: a
/// 320x240 view (the paperdoll fitted inside it, as the game's container
/// gump shows it), Rectangle / Ellipse drawn corner to corner, Pencil drawn
/// freehand and closing itself once the line meets its start, the shape's
/// body dragged to move it and its corner handle to size it.
/// </summary>
public class ContainerFrameEditor : StackPanel
{
    public const double ViewWidth = 320, ViewHeight = 240; // the game's container gump body (ContainerBodyWidth/Height)
    private const double FreehandCloseThreshold = 12;
    private const int FreehandMinPointsBeforeClose = 10;

    private readonly ContainerData _container;
    private readonly SpriteDatabase _sprites;
    private readonly Canvas _canvas = new() { Width = ViewWidth, Height = ViewHeight, Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), ClipToBounds = true };
    private readonly ToggleButton _rect, _ellipse, _pencil;
    private readonly List<SpriteAnimator> _animators = new();
    private FrameShapeType _tool = FrameShapeType.Rectangle;

    private bool _drawingNew;
    private Point _newStart;
    private readonly List<Point> _freehand = new();
    private (bool Resizing, Point StartMouse, List<BookPoint> StartPoints)? _drag;

    public ContainerFrameEditor(ContainerData container, SpriteDatabase sprites)
    {
        _container = container;
        _sprites = sprites;
        if (_container.Frame.Points.Count == 0) _container.Frame.Points = DefaultPoints();

        Children.Add(new TextBlock
        {
            Text = "Item Frame - where its belongings may lie when it's opened. Rectangle/Ellipse: drag corner to corner. Pencil: draw freehand - it closes " +
                   "by itself once your line comes back to its starting point. Drag the shape to move it, or its corner handle to size it.",
            Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left
        });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _rect = new ToggleButton { Content = "Rectangle", Width = 80, Margin = new Thickness(0, 0, 4, 0), IsChecked = true };
        _ellipse = new ToggleButton { Content = "Ellipse", Width = 80, Margin = new Thickness(0, 0, 4, 0) };
        _pencil = new ToggleButton { Content = "Pencil", Width = 80, Margin = new Thickness(0, 0, 4, 0) };
        foreach (var button in new[] { _rect, _ellipse, _pencil })
        {
            button.Click += (_, _) => ChooseTool(button);
            tools.Children.Add(button);
        }
        Children.Add(tools);
        Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = _canvas });

        _canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
        Unloaded += (_, _) => StopAnimators();
        Rebuild();
    }

    private static List<BookPoint> DefaultPoints() => new() { new(60, 45), new(260, 45), new(260, 195), new(60, 195) };

    /// <summary>Call after the paperdoll sprite changes.</summary>
    public void Refresh() => Rebuild();

    private void ChooseTool(ToggleButton chosen)
    {
        _rect.IsChecked = chosen == _rect;
        _ellipse.IsChecked = chosen == _ellipse;
        _pencil.IsChecked = chosen == _pencil;
        _tool = chosen == _ellipse ? FrameShapeType.Ellipse : chosen == _pencil ? FrameShapeType.Freehand : FrameShapeType.Rectangle;
    }

    private void StopAnimators()
    {
        foreach (var animator in _animators) animator.Stop();
        _animators.Clear();
    }

    /// <summary>The paperdoll, fitted into the 320x240 view the way the game's container gump fits it.</summary>
    private void DrawBackground()
    {
        StopAnimators();
        _canvas.Children.Clear();
        var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == _container.PaperDollSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == _container.PaperDollSpriteName)
                     ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == _container.PaperDollSpriteName);
        if (sprite is null || sprite.Frames.Count == 0) return;
        var image = new Image { Width = ViewWidth, Height = ViewHeight, Stretch = Stretch.Uniform };
        _animators.Add(new SpriteAnimator(sprite, image, null));
        _canvas.Children.Add(image);
    }

    private void Rebuild()
    {
        if (_container.Frame.Points.Count == 0) _container.Frame.Points = DefaultPoints();
        DrawBackground();
        var frame = _container.Frame;
        var b = _container.GetFrameBounds();
        Shape shape = frame.ShapeType == FrameShapeType.Ellipse
            ? new Ellipse { Width = b.Width, Height = b.Height }
            : new Polygon { Points = new PointCollection(frame.Points.Select(p => new Point(p.X - b.X, p.Y - b.Y))) };
        shape.Stroke = Brushes.LimeGreen;
        shape.StrokeThickness = 2;
        shape.Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0));
        shape.Cursor = Cursors.SizeAll;
        Canvas.SetLeft(shape, b.X);
        Canvas.SetTop(shape, b.Y);
        shape.MouseLeftButtonDown += (_, e) => StartDrag(resizing: false, e);
        _canvas.Children.Add(shape);

        var handle = new Border { Width = 14, Height = 14, Background = Brushes.LimeGreen, Cursor = Cursors.SizeNWSE };
        Canvas.SetLeft(handle, b.X + b.Width - 14);
        Canvas.SetTop(handle, b.Y + b.Height - 14);
        handle.MouseLeftButtonDown += (_, e) => StartDrag(resizing: true, e);
        _canvas.Children.Add(handle);
    }

    private void StartDrag(bool resizing, MouseButtonEventArgs e)
    {
        if (_container.Frame.Points.Count == 0) _container.Frame.Points = DefaultPoints();
        _drag = (resizing, e.GetPosition(_canvas), _container.Frame.Points.ToList());
        _canvas.CaptureMouse(); // the canvas itself, never rebuilt - so the drag survives every redraw
        e.Handled = true;
    }

    /// <summary>A press on empty canvas starts a brand new shape with the current tool.</summary>
    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _newStart = e.GetPosition(_canvas);
        _drawingNew = true;
        if (_tool == FrameShapeType.Freehand)
        {
            _freehand.Clear();
            _freehand.Add(_newStart);
        }
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(_canvas);
        if (_drag is { } drag)
        {
            double dx = current.X - drag.StartMouse.X, dy = current.Y - drag.StartMouse.Y;
            if (drag.Resizing)
            {
                double minX = drag.StartPoints.Min(p => p.X), minY = drag.StartPoints.Min(p => p.Y);
                double w = Math.Max(1, drag.StartPoints.Max(p => p.X) - minX), h = Math.Max(1, drag.StartPoints.Max(p => p.Y) - minY);
                double sx = Math.Max(20, w + dx) / w, sy = Math.Max(20, h + dy) / h;
                _container.Frame.Points = drag.StartPoints.Select(p => new BookPoint(minX + (p.X - minX) * sx, minY + (p.Y - minY) * sy)).ToList();
            }
            else
            {
                _container.Frame.Points = drag.StartPoints.Select(p => new BookPoint(p.X + dx, p.Y + dy)).ToList();
            }
            Rebuild();
            return;
        }
        if (!_drawingNew) return;

        if (_tool == FrameShapeType.Freehand)
        {
            var last = _freehand[^1];
            if (Math.Abs(current.X - last.X) < 3 && Math.Abs(current.Y - last.Y) < 3) return;
            _freehand.Add(current);
            var first = _freehand[0];
            if (_freehand.Count >= FreehandMinPointsBeforeClose && Math.Abs(current.X - first.X) < FreehandCloseThreshold && Math.Abs(current.Y - first.Y) < FreehandCloseThreshold)
            {
                // Back to where it started: the loop closes itself.
                _freehand[^1] = first;
                _container.Frame.ShapeType = FrameShapeType.Freehand;
                _container.Frame.Points = _freehand.Select(p => new BookPoint(p.X, p.Y)).ToList();
                _drawingNew = false;
                _freehand.Clear();
                _canvas.ReleaseMouseCapture();
                Rebuild();
                return;
            }
            DrawBackground();
            _canvas.Children.Add(new Polyline { Points = new PointCollection(_freehand), Stroke = Brushes.Yellow, StrokeThickness = 2 });
            return;
        }

        DrawBackground();
        double left = Math.Min(_newStart.X, current.X), top = Math.Min(_newStart.Y, current.Y);
        double width = Math.Abs(current.X - _newStart.X), height = Math.Abs(current.Y - _newStart.Y);
        Shape preview = _tool == FrameShapeType.Ellipse ? new Ellipse { Width = width, Height = height } : new Rectangle { Width = width, Height = height };
        preview.Stroke = Brushes.Yellow;
        preview.StrokeThickness = 2;
        Canvas.SetLeft(preview, left);
        Canvas.SetTop(preview, top);
        _canvas.Children.Add(preview);
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not null)
        {
            _drag = null;
            _canvas.ReleaseMouseCapture();
            return;
        }
        if (!_drawingNew) return;
        _drawingNew = false;
        _canvas.ReleaseMouseCapture();
        if (_tool == FrameShapeType.Freehand)
        {
            _freehand.Clear(); // let go before the line met its start: nothing is kept, the old frame stays
        }
        else
        {
            var current = e.GetPosition(_canvas);
            double left = Math.Min(_newStart.X, current.X), top = Math.Min(_newStart.Y, current.Y);
            double w = Math.Max(20, Math.Abs(current.X - _newStart.X)), h = Math.Max(20, Math.Abs(current.Y - _newStart.Y));
            _container.Frame.ShapeType = _tool;
            _container.Frame.Points = new List<BookPoint> { new(left, top), new(left + w, top), new(left + w, top + h), new(left, top + h) };
        }
        Rebuild();
    }
}
