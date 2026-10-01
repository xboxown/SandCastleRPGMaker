using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Phase 2 pixel editor, expanded toward a fuller Aseprite/GameMaker
/// hybrid: pencil, eraser, eyedropper, bucket fill, line/rectangle/ellipse
/// shapes, rectangle select with copy/cut/paste, a deep undo/redo stack,
/// an optional pixel grid, canvas resize/flip/rotate/clear, and export to
/// PNG/JPG/BMP. Deliberately deferred to a follow-up: a custom color wheel
/// (the system color dialog remains the "custom color" option), saved/
/// exportable palettes, and lasso/magic-wand/stamp/text tools.
/// </summary>
public partial class SpriteDrawingWindow : Window
{
    private enum Tool { Pencil, Eraser, Eyedropper, Bucket, TransparencyFill, Line, Rectangle, Ellipse, Select, ColorRemove, ColorReplace, Polygon, Arc, Text, MagicWand, Pan }

    private readonly SpriteEntry? _sprite;
    private readonly SpriteFrame _frame;
    private int _activeLayerId = -1;
    private WriteableBitmap _bitmap = null!;
    private double _zoom = 8;
    private Tool _tool = Tool.Pencil;
    private bool _isDrawing;
    private System.Windows.Point? _lastPixelPos;
    private Color _currentColor = Colors.Black;
    private int _brushSize = 1;
    private bool _showGrid;

    // Shape tool drag state
    private System.Windows.Point? _shapeStartPixel;

    // Selection state
    private Int32Rect? _selection;
    private System.Windows.Point? _selectionStartPixel;
    private (int Width, int Height, byte[] Pixels)? _clipboard;

    // Move-selection (floating drag) state
    private bool _movingSelection;
    private (int Width, int Height, byte[] Pixels)? _floatingPixels;
    private System.Windows.Point _moveStartPixel;
    private Int32Rect _moveOriginalRect;
    private Image? _floatingImage;

    // Polygon tool: accumulated vertices, closes on double-click or clicking near the start
    private readonly List<System.Windows.Point> _polygonPoints = new();

    // Arc tool: stage 0 = dragging the chord, stage 1 = dragging the bulge
    private int _arcStage;
    private System.Windows.Point _arcStart, _arcEnd, _arcBulgeRef;

    // Text tool
    private System.Windows.Point? _textBoxStart;
    private TextBox? _activeTextBox;
    private Int32Rect _textBoxPixelRect;

    // Magic Wand - an arbitrary-shaped mask, distinct from the simple rectangular _selection
    private bool[,]? _selectionMask;

    // Mirror Brush - live symmetry across the vertical center axis
    private bool _mirrorMode;

    // Pan tool
    private bool _panning;
    private System.Windows.Point _panStartMouse;
    private double _panStartH, _panStartV;

    // Undo/redo - full-bitmap snapshots. Frames are small, so this is cheap
    // enough to keep a genuinely deep history ("undo all the way back to
    // ground zero") without any diffing complexity.
    private sealed record UndoState(int Width, int Height, byte[] Pixels);
    private readonly List<UndoState> _undoStack = new();
    private readonly List<UndoState> _redoStack = new();
    private const int MaxUndoDepth = 200;

    private static readonly Color[] StandardSwatches =
    {
        Colors.Black, Colors.White, Colors.Gray, Colors.DarkGray, Colors.Silver, Colors.DimGray, Colors.Transparent, Colors.Brown,
        Colors.Red, Colors.OrangeRed, Colors.Orange, Colors.Gold, Colors.Yellow, Colors.YellowGreen, Colors.Green, Colors.SeaGreen,
        Colors.Cyan, Colors.SkyBlue, Colors.Blue, Colors.RoyalBlue, Colors.Navy, Colors.Purple, Colors.Magenta, Colors.Pink,
        Colors.SaddleBrown, Colors.Sienna, Colors.Tan, Colors.Wheat, Colors.Khaki, Colors.Beige, Colors.Ivory, Colors.LightGray
    };

    /// <summary>True if Save was clicked and the frame's image was updated.</summary>
    public bool Result { get; private set; }

    /// <summary>
    /// sprite is null only for a truly standalone edit with no owning
    /// SpriteEntry (kept for flexibility - every current caller passes a
    /// real sprite). When non-null, the Layers panel lives in this window
    /// (matching GameMaker's actual layout) instead of the old design where
    /// each layer opened as its own blind popup with no view of the others.
    /// </summary>
    public SpriteDrawingWindow(SpriteEntry? sprite, SpriteFrame frame, int? newWidth = null, int? newHeight = null)
    {
        InitializeComponent();
        _sprite = sprite;
        _frame = frame;

        BuildCheckerBackground();
        BuildColorPalette();
        UpdateCurrentColorSwatch();

        if (_sprite is not null)
        {
            LayersPanelRoot.Visibility = Visibility.Visible;
            _activeLayerId = _sprite.Layers.FirstOrDefault(l => !l.IsGroup)?.Id ?? -1;
            RebuildLayersPanel();

            if (_activeLayerId >= 0) LoadActiveLayerIntoBitmap(newWidth, newHeight);
            else InitLegacyBitmap(newWidth, newHeight); // sprite has no layers yet - edit the flat frame file directly, same as before layers existed
        }
        else
        {
            LayersPanelRoot.Visibility = Visibility.Collapsed;
            InitLegacyBitmap(newWidth, newHeight);
        }

        UpdateZoomText();
        PushUndoState(); // baseline state, so Undo can always return to how it started

        PreviewKeyBindings();
    }

    private void InitLegacyBitmap(int? newWidth, int? newHeight)
    {
        int width = newWidth ?? 32, height = newHeight ?? 32;
        BitmapSource? existing = null;
        if (File.Exists(_frame.FilePath))
        {
            existing = LoadExisting(_frame.FilePath);
            if (existing is not null) { width = existing.PixelWidth; height = existing.PixelHeight; }
        }
        InitBitmap(width, height, existing);
        RebuildLayerContextImages();
    }

    private void LoadActiveLayerIntoBitmap(int? newWidth, int? newHeight)
    {
        string existingPath = _activeLayerId >= 0 ? _frame.LayerFilePaths.GetValueOrDefault(_activeLayerId, string.Empty) : string.Empty;
        int width = newWidth ?? 32, height = newHeight ?? 32;
        BitmapSource? existing = null;
        if (File.Exists(existingPath))
        {
            existing = LoadExisting(existingPath);
            if (existing is not null) { width = existing.PixelWidth; height = existing.PixelHeight; }
        }
        InitBitmap(width, height, existing);
        RebuildLayerContextImages();
    }

    private void PreviewKeyBindings()
    {
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CancelCurrentAction();
                e.Handled = true;
                return;
            }

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (!ctrl) return;
            switch (e.Key)
            {
                case Key.Z: Undo(); e.Handled = true; break;
                case Key.Y: Redo(); e.Handled = true; break;
                case Key.C: CopySelection(); e.Handled = true; break;
                case Key.X: CutSelection(); e.Handled = true; break;
                case Key.V: PasteClipboard(); e.Handled = true; break;
            }
        };
    }

    /// <summary>Cancels whatever multi-step gesture is in progress (polygon vertices, arc staging, an open text box, an active selection drag) and reverts to Pencil.</summary>
    private void CancelCurrentAction()
    {
        CancelActiveTextBox();
        _polygonPoints.Clear();
        _arcStage = 0;
        _shapeStartPixel = null;
        _selectionStartPixel = null;
        _isDrawing = false;
        _lastPixelPos = null;
        _panning = false;
        if (_movingSelection && _floatingPixels is { } floating)
        {
            // Drop the floating piece back where it was lifted from, rather than losing it.
            _bitmap.WritePixels(_moveOriginalRect, floating.Pixels, floating.Width * 4, 0);
        }
        _movingSelection = false;
        _floatingPixels = null;
        _floatingImage = null;
        ShapeOverlay.Children.Clear();
        CanvasImage.ReleaseMouseCapture();
        SetTool(Tool.Pencil);
    }

    private static BitmapSource? LoadExisting(string path)
    {
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

    /// <summary>Aseprite-style checkered transparency background, tiled behind the canvas.</summary>
    private void BuildCheckerBackground()
    {
        const int tile = 8;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, tile * 2, tile * 2));
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, tile, tile));
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(tile, tile, tile, tile));
        }
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile * 2, tile * 2),
            ViewportUnits = BrushMappingMode.Absolute
        };
        CheckerBackground.Background = brush;
    }

    private void BuildColorPalette()
    {
        ColorPaletteGrid.Children.Clear();
        foreach (var color in StandardSwatches)
        {
            var swatch = new Rectangle
            {
                Width = 18, Height = 18, Margin = new Thickness(2),
                Fill = new SolidColorBrush(color), Stroke = Brushes.Gray, StrokeThickness = 0.5,
                Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (_, _) => { _currentColor = color; UpdateCurrentColorSwatch(); };
            ColorPaletteGrid.Children.Add(swatch);
        }
    }

    private void UpdateCurrentColorSwatch() => CurrentColorSwatch.Background = new SolidColorBrush(_currentColor);

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow(_currentColor) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _currentColor = picker.Result;
            UpdateCurrentColorSwatch();
        }
    }

    // ---------- Bitmap setup ----------

    private void InitBitmap(int width, int height, BitmapSource? existing)
    {
        _bitmap = new WriteableBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Bgra32, null);

        if (existing is not null)
        {
            var converted = new FormatConvertedBitmap(existing, PixelFormats.Bgra32, null, 0);
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            converted.CopyPixels(pixels, stride, 0);
            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        }

        CanvasImage.Source = _bitmap;
        ApplyZoomToCanvasSize();
        RedrawGrid();
    }

    private void ApplyZoomToCanvasSize()
    {
        CanvasImage.Width = _bitmap.PixelWidth * _zoom;
        CanvasImage.Height = _bitmap.PixelHeight * _zoom;
        CanvasRoot.Width = CanvasImage.Width;
        CanvasRoot.Height = CanvasImage.Height;
    }

    private byte[] ReadAllPixels(out int stride)
    {
        stride = _bitmap.PixelWidth * 4;
        var pixels = new byte[_bitmap.PixelHeight * stride];
        _bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private void ReplaceBitmap(int width, int height, byte[] pixels, int stride)
    {
        _bitmap = new WriteableBitmap(Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Bgra32, null);
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        CanvasImage.Source = _bitmap;
        ApplyZoomToCanvasSize();
        RedrawGrid();
    }

    // ---------- Undo / Redo ----------

    private void PushUndoState()
    {
        _undoStack.Add(new UndoState(_bitmap.PixelWidth, _bitmap.PixelHeight, ReadAllPixels(out _)));
        if (_undoStack.Count > MaxUndoDepth) _undoStack.RemoveAt(0);
        _redoStack.Clear();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_undoStack.Count <= 1) return; // keep the baseline state, don't undo past the start
        var current = new UndoState(_bitmap.PixelWidth, _bitmap.PixelHeight, ReadAllPixels(out _));
        _redoStack.Add(current);
        _undoStack.RemoveAt(_undoStack.Count - 1);
        var target = _undoStack[^1];
        ReplaceBitmap(target.Width, target.Height, target.Pixels, target.Width * 4);
    }

    private void Redo()
    {
        if (_redoStack.Count == 0) return;
        var current = new UndoState(_bitmap.PixelWidth, _bitmap.PixelHeight, ReadAllPixels(out _));
        _undoStack.Add(current);
        var target = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        ReplaceBitmap(target.Width, target.Height, target.Pixels, target.Width * 4);
    }

    // ---------- Grid ----------

    private void ShowGrid_Click(object sender, RoutedEventArgs e)
    {
        _showGrid = ShowGridMenuItem.IsChecked;
        RedrawGrid();
    }

    private void RedrawGrid()
    {
        GridOverlay.Children.Clear();
        if (!_showGrid) return;

        int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;
        var brush = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));
        for (int x = 0; x <= w; x++)
            GridOverlay.Children.Add(new Line { X1 = x * _zoom, Y1 = 0, X2 = x * _zoom, Y2 = h * _zoom, Stroke = brush, StrokeThickness = 1 });
        for (int y = 0; y <= h; y++)
            GridOverlay.Children.Add(new Line { X1 = 0, Y1 = y * _zoom, X2 = w * _zoom, Y2 = y * _zoom, Stroke = brush, StrokeThickness = 1 });
    }

    // ---------- Tools ----------

    private void SetTool(Tool tool)
    {
        _tool = tool;
        PencilToolButton.IsChecked = tool == Tool.Pencil;
        EraserToolButton.IsChecked = tool == Tool.Eraser;
        BucketToolButton.IsChecked = tool == Tool.Bucket;
        TransparencyFillToolButton.IsChecked = tool == Tool.TransparencyFill;
        LineToolButton.IsChecked = tool == Tool.Line;
        RectangleToolButton.IsChecked = tool == Tool.Rectangle;
        EllipseToolButton.IsChecked = tool == Tool.Ellipse;
        SelectToolButton.IsChecked = tool == Tool.Select;
        ColorRemoveToolButton.IsChecked = tool == Tool.ColorRemove;
        ColorReplaceToolButton.IsChecked = tool == Tool.ColorReplace;
        PolygonToolButton.IsChecked = tool == Tool.Polygon;
        ArcToolButton.IsChecked = tool == Tool.Arc;
        TextToolButton.IsChecked = tool == Tool.Text;
        MagicWandToolButton.IsChecked = tool == Tool.MagicWand;
        PanToolButton.IsChecked = tool == Tool.Pan;
        CommitActiveTextBox();
        ShapeOverlay.Children.Clear();
        _polygonPoints.Clear();
        _arcStage = 0;
    }

    private void PencilToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Pencil);
    private void EraserToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Eraser);
    private void EyedropperToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Eyedropper);
    private void BucketToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Bucket);
    private void TransparencyFillToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.TransparencyFill);
    private void LineToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Line);
    private void RectangleToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Rectangle);
    private void EllipseToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Ellipse);
    private void SelectToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Select);
    private void ColorRemoveToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.ColorRemove);
    private void ColorReplaceToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.ColorReplace);
    private void PolygonToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Polygon);
    private void ArcToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Arc);
    private void TextToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Text);
    private void MagicWandToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.MagicWand);
    private void PanToolButton_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Pan);

    private void MirrorToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _mirrorMode = MirrorToggleButton.IsChecked == true;
    }

    private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // The slider's initial Value in XAML fires this event during
        // InitializeComponent(), before BrushSizeText (declared later in
        // the same window) has been assigned to its field yet.
        if (BrushSizeText is null) return;
        _brushSize = (int)BrushSizeSlider.Value;
        BrushSizeText.Text = _brushSize.ToString();
    }

    // ---------- Mouse dispatch ----------

    private System.Windows.Point ToPixelSpace(System.Windows.Point displayPos) => new(displayPos.X / _zoom, displayPos.Y / _zoom);

    private void CanvasImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = ToPixelSpace(e.GetPosition(CanvasImage));
        int px = (int)p.X, py = (int)p.Y;

        switch (_tool)
        {
            case Tool.Eyedropper:
                PickColorAt(px, py);
                return;

            case Tool.Bucket:
                PushUndoState();
                FloodFill(px, py, _currentColor);
                return;

            case Tool.TransparencyFill:
                // Same connected flood fill as the bucket, filling with fully transparent pixels (0,0,0,0 - the same "transparent" Magic Transparency and the Eraser leave behind).
                PushUndoState();
                FloodFill(px, py, Color.FromArgb(0, 0, 0, 0));
                return;

            case Tool.Line:
            case Tool.Rectangle:
            case Tool.Ellipse:
                _shapeStartPixel = p;
                CanvasImage.CaptureMouse();
                return;

            case Tool.Select:
                if (_selection is { } existingSel && existingSel.Width > 0 && existingSel.Height > 0 &&
                    px >= existingSel.X && px < existingSel.X + existingSel.Width &&
                    py >= existingSel.Y && py < existingSel.Y + existingSel.Height)
                {
                    StartMovingSelection(existingSel, p);
                }
                else
                {
                    _selectionStartPixel = p;
                    _selection = new Int32Rect(px, py, 0, 0);
                    _selectionMask = null;
                    CanvasImage.CaptureMouse();
                }
                return;

            case Tool.ColorRemove:
                PushUndoState();
                RemoveAllOfColor(SampleColorAt(px, py));
                return;

            case Tool.ColorReplace:
                PushUndoState();
                ReplaceAllOfColor(SampleColorAt(px, py), _currentColor);
                return;

            case Tool.Polygon:
                HandlePolygonClick(p, e.ClickCount == 2);
                return;

            case Tool.Arc:
                HandleArcClick(p);
                return;

            case Tool.Text:
                _textBoxStart = p;
                CanvasImage.CaptureMouse();
                return;

            case Tool.MagicWand:
                SelectConnectedRegion(px, py);
                return;

            case Tool.Pan:
                _panning = true;
                _panStartMouse = e.GetPosition(CanvasScrollViewer);
                _panStartH = CanvasScrollViewer.HorizontalOffset;
                _panStartV = CanvasScrollViewer.VerticalOffset;
                CanvasImage.CaptureMouse();
                return;

            default: // Pencil, Eraser
                PushUndoState();
                _isDrawing = true;
                CanvasImage.CaptureMouse();
                PaintAt(px, py);
                _lastPixelPos = p;
                return;
        }
    }

    private void CanvasImage_MouseMove(object sender, MouseEventArgs e)
    {
        var p = ToPixelSpace(e.GetPosition(CanvasImage));

        if (_isDrawing && (_tool == Tool.Pencil || _tool == Tool.Eraser))
        {
            if (_lastPixelPos is { } last)
                DrawLine((int)last.X, (int)last.Y, (int)p.X, (int)p.Y);
            else
                PaintAt((int)p.X, (int)p.Y);
            _lastPixelPos = p;
            return;
        }

        if (_shapeStartPixel is { } start && (_tool == Tool.Line || _tool == Tool.Rectangle || _tool == Tool.Ellipse))
        {
            DrawShapePreview(start, p);
            return;
        }

        if (_selectionStartPixel is { } selStart && _tool == Tool.Select)
        {
            UpdateSelectionRect(selStart, p);
            return;
        }

        if (_textBoxStart is { } textStart && _tool == Tool.Text && _activeTextBox is null)
        {
            DrawTextBoxSizingPreview(textStart, p);
            return;
        }

        if (_tool == Tool.Arc && _arcStage == 1)
        {
            _arcBulgeRef = p;
            DrawArcPreview();
            return;
        }

        if (_panning)
        {
            var current = e.GetPosition(CanvasScrollViewer);
            CanvasScrollViewer.ScrollToHorizontalOffset(_panStartH - (current.X - _panStartMouse.X));
            CanvasScrollViewer.ScrollToVerticalOffset(_panStartV - (current.Y - _panStartMouse.Y));
            return;
        }

        if (_movingSelection && _floatingImage is not null)
        {
            int dx = (int)Math.Round(p.X - _moveStartPixel.X);
            int dy = (int)Math.Round(p.Y - _moveStartPixel.Y);
            Canvas.SetLeft(_floatingImage, (_moveOriginalRect.X + dx) * _zoom);
            Canvas.SetTop(_floatingImage, (_moveOriginalRect.Y + dy) * _zoom);
        }
    }

    private void CanvasImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var p = ToPixelSpace(e.GetPosition(CanvasImage));

        if (_isDrawing)
        {
            _isDrawing = false;
            _lastPixelPos = null;
        }
        else if (_shapeStartPixel is { } start)
        {
            if (_tool == Tool.Arc)
            {
                _arcEnd = p;
                _arcBulgeRef = p;
                _arcStage = 1;
                _shapeStartPixel = null;
                ShapeOverlay.Children.Clear();
            }
            else
            {
                PushUndoState();
                CommitShape(start, p);
                _shapeStartPixel = null;
                ShapeOverlay.Children.Clear();
            }
        }
        else if (_selectionStartPixel is not null)
        {
            _selectionStartPixel = null;
        }
        else if (_movingSelection)
        {
            CommitMovingSelection(p);
        }
        else if (_textBoxStart is { } textStart && _tool == Tool.Text && _activeTextBox is null)
        {
            int x0 = (int)textStart.X, y0 = (int)textStart.Y, x1 = (int)p.X, y1 = (int)p.Y;
            var rect = ClampToCanvas(new Int32Rect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
            ShapeOverlay.Children.Clear();
            if (rect.Width > 2 && rect.Height > 2) ShowTextBoxEditor(rect);
            else _textBoxStart = null;
        }
        else if (_panning)
        {
            _panning = false;
        }

        CanvasImage.ReleaseMouseCapture();
    }

    /// <summary>Right-click is always a quick eyedropper, regardless of the active tool.</summary>
    private void CanvasImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = ToPixelSpace(e.GetPosition(CanvasImage));
        PickColorAt((int)p.X, (int)p.Y);
    }

    private void PickColorAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _bitmap.PixelWidth || y >= _bitmap.PixelHeight) return;
        var pixels = new byte[4];
        _bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        _currentColor = Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
        UpdateCurrentColorSwatch();
    }

    // ---------- Pencil / Eraser ----------

    private void PaintAt(int px, int py)
    {
        int half = _brushSize / 2;
        for (int dx = -half; dx < _brushSize - half; dx++)
            for (int dy = -half; dy < _brushSize - half; dy++)
                SetPixel(px + dx, py + dy, _tool == Tool.Eraser ? Colors.Transparent : _currentColor);
    }

    /// <summary>Bresenham's line algorithm - interpolates between the last and current pixel so fast mouse movement doesn't leave gaps in a freehand stroke.</summary>
    private void DrawLine(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            PaintAt(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void SetPixel(int x, int y, Color c)
    {
        SetPixelRaw(x, y, c);
        if (_mirrorMode)
        {
            int mirroredX = _bitmap.PixelWidth - 1 - x;
            if (mirroredX != x) SetPixelRaw(mirroredX, y, c);
        }
    }

    private void SetPixelRaw(int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= _bitmap.PixelWidth || y >= _bitmap.PixelHeight) return;
        _bitmap.Lock();
        IntPtr address = _bitmap.BackBuffer + y * _bitmap.BackBufferStride + x * 4;
        int colorData = (c.A << 24) | (c.R << 16) | (c.G << 8) | c.B;
        Marshal.WriteInt32(address, colorData);
        _bitmap.AddDirtyRect(new Int32Rect(x, y, 1, 1));
        _bitmap.Unlock();
    }

    // ---------- Bucket fill (iterative flood fill - avoids recursion stack overflow on large regions) ----------

    private void FloodFill(int startX, int startY, Color fillColor)
    {
        if (startX < 0 || startY < 0 || startX >= _bitmap.PixelWidth || startY >= _bitmap.PixelHeight) return;

        var pixels = ReadAllPixels(out int stride);
        int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;

        int startIndex = startY * stride + startX * 4;
        byte targetB = pixels[startIndex], targetG = pixels[startIndex + 1], targetR = pixels[startIndex + 2], targetA = pixels[startIndex + 3];
        int newColorData = (fillColor.A << 24) | (fillColor.R << 16) | (fillColor.G << 8) | fillColor.B;

        bool Matches(int x, int y)
        {
            int i = y * stride + x * 4;
            return pixels[i] == targetB && pixels[i + 1] == targetG && pixels[i + 2] == targetR && pixels[i + 3] == targetA;
        }

        if (targetB == fillColor.B && targetG == fillColor.G && targetR == fillColor.R && targetA == fillColor.A) return; // already that color

        var queue = new Queue<(int X, int Y)>();
        var visited = new bool[w * h];
        queue.Enqueue((startX, startY));
        visited[startY * w + startX] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            int i = y * stride + x * 4;
            pixels[i] = (byte)(newColorData & 0xFF);
            pixels[i + 1] = (byte)((newColorData >> 8) & 0xFF);
            pixels[i + 2] = (byte)((newColorData >> 16) & 0xFF);
            pixels[i + 3] = (byte)((newColorData >> 24) & 0xFF);

            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || visited[ny * w + nx]) continue;
                if (!Matches(nx, ny)) continue;
                visited[ny * w + nx] = true;
                queue.Enqueue((nx, ny));
            }
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
    }

    // ---------- Shape tools: line, rectangle, ellipse ----------

    private void DrawShapePreview(System.Windows.Point start, System.Windows.Point end)
    {
        ShapeOverlay.Children.Clear();
        var brush = new SolidColorBrush(_currentColor);
        double x0 = start.X * _zoom, y0 = start.Y * _zoom, x1 = end.X * _zoom, y1 = end.Y * _zoom;

        UIElement shape = _tool switch
        {
            Tool.Line => new Line { X1 = x0, Y1 = y0, X2 = x1, Y2 = y1, Stroke = brush, StrokeThickness = Math.Max(1, _brushSize * _zoom) },
            Tool.Rectangle => BuildPreviewRect(x0, y0, x1, y1, brush),
            Tool.Ellipse => BuildPreviewEllipse(x0, y0, x1, y1, brush),
            _ => new Line()
        };
        ShapeOverlay.Children.Add(shape);
    }

    private static UIElement BuildPreviewRect(double x0, double y0, double x1, double y1, Brush brush)
    {
        var rect = new Rectangle { Stroke = brush, StrokeThickness = 2, Width = Math.Abs(x1 - x0), Height = Math.Abs(y1 - y0) };
        Canvas.SetLeft(rect, Math.Min(x0, x1));
        Canvas.SetTop(rect, Math.Min(y0, y1));
        return rect;
    }

    private static UIElement BuildPreviewEllipse(double x0, double y0, double x1, double y1, Brush brush)
    {
        var ellipse = new Ellipse { Stroke = brush, StrokeThickness = 2, Width = Math.Abs(x1 - x0), Height = Math.Abs(y1 - y0) };
        Canvas.SetLeft(ellipse, Math.Min(x0, x1));
        Canvas.SetTop(ellipse, Math.Min(y0, y1));
        return ellipse;
    }

    private void CommitShape(System.Windows.Point start, System.Windows.Point end)
    {
        int x0 = (int)start.X, y0 = (int)start.Y, x1 = (int)end.X, y1 = (int)end.Y;

        switch (_tool)
        {
            case Tool.Line:
                DrawLine(x0, y0, x1, y1);
                break;

            case Tool.Rectangle:
            {
                int left = Math.Min(x0, x1), right = Math.Max(x0, x1), top = Math.Min(y0, y1), bottom = Math.Max(y0, y1);
                DrawLine(left, top, right, top);
                DrawLine(left, bottom, right, bottom);
                DrawLine(left, top, left, bottom);
                DrawLine(right, top, right, bottom);
                break;
            }

            case Tool.Ellipse:
            {
                int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
                double rx = Math.Abs(x1 - x0) / 2.0, ry = Math.Abs(y1 - y0) / 2.0;
                int steps = Math.Max(36, (int)((rx + ry) * 4));
                int? prevX = null, prevY = null;
                for (int i = 0; i <= steps; i++)
                {
                    double angle = 2 * Math.PI * i / steps;
                    int px = cx + (int)(rx * Math.Cos(angle));
                    int py = cy + (int)(ry * Math.Sin(angle));
                    if (prevX is int lx && prevY is int ly) DrawLine(lx, ly, px, py);
                    prevX = px; prevY = py;
                }
                break;
            }
        }
    }

    // ---------- Color Remove / Color Replace (global, not just under the cursor) ----------

    private Color SampleColorAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _bitmap.PixelWidth || y >= _bitmap.PixelHeight) return Colors.Transparent;
        var pixels = new byte[4];
        _bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return Color.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    /// <summary>Magic Transparency: turns every pixel matching the given color transparent, anywhere in the image - distinct from the Eraser, which only affects pixels under the cursor, and from Transparency Fill, which only affects the connected area clicked.</summary>
    private void RemoveAllOfColor(Color target)
    {
        var pixels = ReadAllPixels(out int stride);
        int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                if (pixels[i] == target.B && pixels[i + 1] == target.G && pixels[i + 2] == target.R && pixels[i + 3] == target.A)
                {
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                }
            }
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
    }

    /// <summary>Replaces every pixel matching the given color with a new one, anywhere in the image.</summary>
    private void ReplaceAllOfColor(Color target, Color replacement)
    {
        var pixels = ReadAllPixels(out int stride);
        int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                if (pixels[i] == target.B && pixels[i + 1] == target.G && pixels[i + 2] == target.R && pixels[i + 3] == target.A)
                {
                    pixels[i] = replacement.B; pixels[i + 1] = replacement.G; pixels[i + 2] = replacement.R; pixels[i + 3] = replacement.A;
                }
            }
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
    }

    // ---------- Polygon tool ----------

    private void HandlePolygonClick(System.Windows.Point p, bool isDoubleClick)
    {
        if (isDoubleClick || (_polygonPoints.Count > 2 && IsNearFirstVertex(p)))
        {
            if (_polygonPoints.Count >= 2)
            {
                PushUndoState();
                CommitPolygon();
            }
            _polygonPoints.Clear();
            ShapeOverlay.Children.Clear();
            return;
        }

        _polygonPoints.Add(p);
        DrawPolygonPreview();
    }

    private bool IsNearFirstVertex(System.Windows.Point p)
    {
        if (_polygonPoints.Count == 0) return false;
        var first = _polygonPoints[0];
        double dx = (p.X - first.X) * _zoom, dy = (p.Y - first.Y) * _zoom;
        return Math.Sqrt(dx * dx + dy * dy) < 8;
    }

    private void DrawPolygonPreview()
    {
        ShapeOverlay.Children.Clear();
        if (_polygonPoints.Count == 0) return;
        var polyline = new Polyline { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = 2 };
        foreach (var pt in _polygonPoints) polyline.Points.Add(new System.Windows.Point(pt.X * _zoom, pt.Y * _zoom));
        ShapeOverlay.Children.Add(polyline);
        foreach (var pt in _polygonPoints)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.Yellow };
            Canvas.SetLeft(dot, pt.X * _zoom - 3);
            Canvas.SetTop(dot, pt.Y * _zoom - 3);
            ShapeOverlay.Children.Add(dot);
        }
    }

    private void CommitPolygon()
    {
        for (int i = 0; i < _polygonPoints.Count; i++)
        {
            var a = _polygonPoints[i];
            var b = _polygonPoints[(i + 1) % _polygonPoints.Count];
            DrawLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y);
        }
    }

    // ---------- Arc tool: drag a chord, then drag to bulge it, click to commit ----------

    private void HandleArcClick(System.Windows.Point p)
    {
        if (_arcStage == 0)
        {
            _shapeStartPixel = p;
            _arcStart = p;
            CanvasImage.CaptureMouse();
        }
        else
        {
            PushUndoState();
            CommitArc();
            _arcStage = 0;
            ShapeOverlay.Children.Clear();
        }
    }

    private void DrawArcPreview()
    {
        ShapeOverlay.Children.Clear();
        var points = ComputeArcPoints(_arcStart, _arcEnd, _arcBulgeRef);
        var polyline = new Polyline { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = 2 };
        foreach (var pt in points) polyline.Points.Add(new System.Windows.Point(pt.X * _zoom, pt.Y * _zoom));
        ShapeOverlay.Children.Add(polyline);
    }

    private void CommitArc()
    {
        var points = ComputeArcPoints(_arcStart, _arcEnd, _arcBulgeRef);
        for (int i = 0; i < points.Count - 1; i++)
            DrawLine((int)points[i].X, (int)points[i].Y, (int)points[i + 1].X, (int)points[i + 1].Y);
    }

    /// <summary>Quadratic-Bezier approximation of "chord + bulge amount" - simpler than a true 3-point circular arc and visually equivalent for this purpose.</summary>
    private static List<System.Windows.Point> ComputeArcPoints(System.Windows.Point start, System.Windows.Point end, System.Windows.Point bulgeRef)
    {
        var mid = new System.Windows.Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        var result = new List<System.Windows.Point>();
        if (len < 0.001) { result.Add(start); result.Add(end); return result; }

        double nx = -dy / len, ny = dx / len;
        double bulge = (bulgeRef.X - mid.X) * nx + (bulgeRef.Y - mid.Y) * ny;
        var control = new System.Windows.Point(mid.X + nx * bulge * 2, mid.Y + ny * bulge * 2);

        int steps = Math.Max(12, (int)(len / 2));
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double x = (1 - t) * (1 - t) * start.X + 2 * (1 - t) * t * control.X + t * t * end.X;
            double y = (1 - t) * (1 - t) * start.Y + 2 * (1 - t) * t * control.Y + t * t * end.Y;
            result.Add(new System.Windows.Point(x, y));
        }
        return result;
    }

    // ---------- Text tool: draw a box, type, Enter rasterizes it into the bitmap ----------

    /// <summary>High-contrast yellow dashed box while sizing the text area - a plain shape-preview in the current drawing color was easy to lose against a dark canvas.</summary>
    private void DrawTextBoxSizingPreview(System.Windows.Point start, System.Windows.Point end)
    {
        ShapeOverlay.Children.Clear();
        double x0 = start.X * _zoom, y0 = start.Y * _zoom, x1 = end.X * _zoom, y1 = end.Y * _zoom;
        var rect = new Rectangle
        {
            Width = Math.Abs(x1 - x0),
            Height = Math.Abs(y1 - y0),
            Stroke = Brushes.Yellow,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        Canvas.SetLeft(rect, Math.Min(x0, x1));
        Canvas.SetTop(rect, Math.Min(y0, y1));
        ShapeOverlay.Children.Add(rect);
    }

    private FontFamily _textFontFamily = new("Consolas");
    private double _textFontSize = 12;
    private bool _textBold;
    private bool _textItalic;
    private StackPanel? _textToolbar;

    private void ShowTextBoxEditor(Int32Rect pixelRect)
    {
        _textBoxPixelRect = pixelRect;

        _activeTextBox = new TextBox
        {
            AcceptsReturn = false, // Enter always commits; long text wraps automatically via TextWrapping instead of manual newlines
            TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            Foreground = new SolidColorBrush(_currentColor),
            BorderBrush = Brushes.Yellow,
            BorderThickness = new Thickness(1),
            FontFamily = _textFontFamily,
            FontSize = _textFontSize,
            FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = _textItalic ? FontStyles.Italic : FontStyles.Normal
        };
        Canvas.SetLeft(_activeTextBox, pixelRect.X * _zoom);
        Canvas.SetTop(_activeTextBox, pixelRect.Y * _zoom);
        _activeTextBox.Width = pixelRect.Width * _zoom;
        _activeTextBox.Height = pixelRect.Height * _zoom;
        _activeTextBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitActiveTextBox();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelActiveTextBox();
                e.Handled = true;
            }
        };

        ShapeOverlay.Children.Add(_activeTextBox);
        BuildFloatingTextToolbar(pixelRect);
        _activeTextBox.Focus();
    }

    /// <summary>Small floating toolbar (font family, size, bold, italic) positioned just above the text box - the earlier version had no way to change these at all.</summary>
    private void BuildFloatingTextToolbar(Int32Rect pixelRect)
    {
        var fontNames = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n).ToList();

        var familyCombo = new ComboBox { Width = 130, ItemsSource = fontNames, SelectedItem = fontNames.Contains(_textFontFamily.Source) ? _textFontFamily.Source : fontNames.FirstOrDefault() };
        var sizeBox = new TextBox { Width = 40, Text = _textFontSize.ToString("0") };
        var boldButton = new ToggleButton { Content = "B", Width = 24, FontWeight = FontWeights.Bold, IsChecked = _textBold };
        var italicButton = new ToggleButton { Content = "I", Width = 24, FontStyle = FontStyles.Italic, IsChecked = _textItalic };

        void ApplyToActiveTextBox()
        {
            if (_activeTextBox is null) return;
            if (familyCombo.SelectedItem is string fam) { _textFontFamily = new FontFamily(fam); _activeTextBox.FontFamily = _textFontFamily; }
            if (double.TryParse(sizeBox.Text, out double sz) && sz > 0) { _textFontSize = sz; _activeTextBox.FontSize = sz; }
            _textBold = boldButton.IsChecked == true; _activeTextBox.FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal;
            _textItalic = italicButton.IsChecked == true; _activeTextBox.FontStyle = _textItalic ? FontStyles.Italic : FontStyles.Normal;
        }

        familyCombo.SelectionChanged += (_, _) => ApplyToActiveTextBox();
        sizeBox.LostFocus += (_, _) => ApplyToActiveTextBox();
        boldButton.Click += (_, _) => ApplyToActiveTextBox();
        italicButton.Click += (_, _) => ApplyToActiveTextBox();

        _textToolbar = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Black };
        _textToolbar.Children.Add(familyCombo);
        _textToolbar.Children.Add(sizeBox);
        _textToolbar.Children.Add(boldButton);
        _textToolbar.Children.Add(italicButton);

        Canvas.SetLeft(_textToolbar, pixelRect.X * _zoom);
        Canvas.SetTop(_textToolbar, Math.Max(0, pixelRect.Y * _zoom - 28));
        ShapeOverlay.Children.Add(_textToolbar);
    }

    private void RemoveFloatingTextToolbar()
    {
        if (_textToolbar is not null) ShapeOverlay.Children.Remove(_textToolbar);
        _textToolbar = null;
    }

    private void CommitActiveTextBox()
    {
        if (_activeTextBox is null) return;
        string text = _activeTextBox.Text;
        var rect = _textBoxPixelRect;
        ShapeOverlay.Children.Remove(_activeTextBox);
        _activeTextBox = null;
        _textBoxStart = null;
        RemoveFloatingTextToolbar();

        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0) return;
        PushUndoState();
        RasterizeText(text, rect);
    }

    private void CancelActiveTextBox()
    {
        if (_activeTextBox is null) return;
        ShapeOverlay.Children.Remove(_activeTextBox);
        _activeTextBox = null;
        _textBoxStart = null;
        RemoveFloatingTextToolbar();
    }

    private void RasterizeText(string text, Int32Rect rect)
    {
        rect = ClampToCanvas(rect);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var typeface = new Typeface(_textFontFamily, _textItalic ? FontStyles.Italic : FontStyles.Normal,
            _textBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            _textFontSize,
            new SolidColorBrush(_currentColor),
            1.0)
        {
            MaxTextWidth = rect.Width,
            MaxTextHeight = rect.Height
        };

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawText(formatted, new System.Windows.Point(0, 0));

        var rtb = new RenderTargetBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
        int stride = rect.Width * 4;
        var textPixels = new byte[rect.Height * stride];
        converted.CopyPixels(textPixels, stride, 0);

        var existing = new byte[rect.Height * stride];
        _bitmap.CopyPixels(rect, existing, stride, 0);

        for (int i = 0; i < textPixels.Length; i += 4)
        {
            if (textPixels[i + 3] == 0) continue; // transparent text pixel - keep whatever's already there
            existing[i] = textPixels[i];
            existing[i + 1] = textPixels[i + 1];
            existing[i + 2] = textPixels[i + 2];
            existing[i + 3] = textPixels[i + 3];
        }

        _bitmap.WritePixels(rect, existing, stride, 0);
    }

    // ---------- Magic Wand: selects an arbitrarily-shaped connected region, not just a rectangle ----------

    private void SelectConnectedRegion(int startX, int startY)
    {
        if (startX < 0 || startY < 0 || startX >= _bitmap.PixelWidth || startY >= _bitmap.PixelHeight) return;

        var pixels = ReadAllPixels(out int stride);
        int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;
        int startIndex = startY * stride + startX * 4;
        byte tb = pixels[startIndex], tg = pixels[startIndex + 1], tr = pixels[startIndex + 2], ta = pixels[startIndex + 3];

        bool Matches(int x, int y)
        {
            int i = y * stride + x * 4;
            return pixels[i] == tb && pixels[i + 1] == tg && pixels[i + 2] == tr && pixels[i + 3] == ta;
        }

        var mask = new bool[w, h];
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((startX, startY));
        mask[startX, startY] = true;
        int minX = startX, maxX = startX, minY = startY, maxY = startY;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);

            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || mask[nx, ny]) continue;
                if (!Matches(nx, ny)) continue;
                mask[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        _selectionMask = mask;
        _selection = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        DrawSelectionOverlay();
    }

    // ---------- Rotate / Flip acting on the selection (or whole canvas if none) ----------

    private void RotateSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_selection is not { } sel || sel.Width <= 0 || sel.Height <= 0) { RotateClockwise_Click(sender, e); return; }
        TransformRegion(sel, (w, h, src) =>
        {
            var dst = new byte[src.Length];
            int stride = w * 4;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int nx = h - 1 - y, ny = x; // only meaningful for square regions; non-square rotates into a differently-shaped area, so this keeps the same bounding box and best-effort maps what fits
                    if (nx < w && ny < h)
                        Array.Copy(src, y * stride + x * 4, dst, ny * stride + nx * 4, 4);
                }
            return dst;
        });
    }

    private void FlipSelection_Click(object sender, RoutedEventArgs e)
    {
        if (_selection is not { } sel || sel.Width <= 0 || sel.Height <= 0) { FlipHorizontal_Click(sender, e); return; }
        TransformRegion(sel, (w, h, src) =>
        {
            var dst = new byte[src.Length];
            int stride = w * 4;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Array.Copy(src, y * stride + x * 4, dst, y * stride + (w - 1 - x) * 4, 4);
            return dst;
        });
    }

    private void TransformRegion(Int32Rect sel, Func<int, int, byte[], byte[]> transform)
    {
        var clipped = ClampToCanvas(sel);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        PushUndoState();
        int stride = clipped.Width * 4;
        var pixels = new byte[clipped.Height * stride];
        _bitmap.CopyPixels(clipped, pixels, stride, 0);
        var transformed = transform(clipped.Width, clipped.Height, pixels);
        _bitmap.WritePixels(clipped, transformed, stride, 0);
    }

    // ---------- Pan tool has no dedicated commit step - handled entirely in the mouse dispatch above ----------

    // ---------- Rectangle select + copy/cut/paste ----------

    private void UpdateSelectionRect(System.Windows.Point start, System.Windows.Point end)
    {
        int x0 = (int)start.X, y0 = (int)start.Y, x1 = (int)end.X, y1 = (int)end.Y;
        int left = Math.Min(x0, x1), top = Math.Min(y0, y1);
        int w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
        _selection = new Int32Rect(left, top, w, h);
        DrawSelectionOverlay();
    }

    private void DrawSelectionOverlay()
    {
        ShapeOverlay.Children.Clear();
        if (_selection is not { } sel || sel.Width <= 0 || sel.Height <= 0) return;

        if (_selectionMask is { } mask)
        {
            DrawMaskOutline(mask, sel);
            return;
        }

        AddMarchingAntsRect(sel.X * _zoom, sel.Y * _zoom, sel.Width * _zoom, sel.Height * _zoom);
    }

    /// <summary>Two overlaid dashed rectangles (black + white, offset) so the selection reads clearly against any background color - a plain single-color dashed line was getting lost against light or checkered areas.</summary>
    private void AddMarchingAntsRect(double x, double y, double w, double h)
    {
        var black = new Rectangle { Width = w, Height = h, Stroke = Brushes.Black, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 4 } };
        var white = new Rectangle { Width = w, Height = h, Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 4 }, StrokeDashOffset = 4 };
        Canvas.SetLeft(black, x); Canvas.SetTop(black, y);
        Canvas.SetLeft(white, x); Canvas.SetTop(white, y);
        ShapeOverlay.Children.Add(black);
        ShapeOverlay.Children.Add(white);
    }

    /// <summary>Draws a short black+white dashed edge on every boundary side of an arbitrary-shaped mask (from Magic Wand), rather than just its bounding box.</summary>
    private void DrawMaskOutline(bool[,] mask, Int32Rect bounds)
    {
        for (int y = bounds.Y; y < bounds.Y + bounds.Height; y++)
        {
            for (int x = bounds.X; x < bounds.X + bounds.Width; x++)
            {
                if (!mask[x, y]) continue;
                bool left = x == 0 || !mask[x - 1, y];
                bool right = x == mask.GetLength(0) - 1 || !mask[x + 1, y];
                bool top = y == 0 || !mask[x, y - 1];
                bool bottom = y == mask.GetLength(1) - 1 || !mask[x, y + 1];

                if (left) AddMaskEdge(x * _zoom, y * _zoom, x * _zoom, (y + 1) * _zoom);
                if (right) AddMaskEdge((x + 1) * _zoom, y * _zoom, (x + 1) * _zoom, (y + 1) * _zoom);
                if (top) AddMaskEdge(x * _zoom, y * _zoom, (x + 1) * _zoom, y * _zoom);
                if (bottom) AddMaskEdge(x * _zoom, (y + 1) * _zoom, (x + 1) * _zoom, (y + 1) * _zoom);
            }
        }
    }

    private void AddMaskEdge(double x1, double y1, double x2, double y2)
    {
        ShapeOverlay.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.Black, StrokeThickness = 2 });
        ShapeOverlay.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.White, StrokeThickness = 1 });
    }

    /// <summary>
    /// Clicking inside an existing selection (instead of drawing a new one)
    /// lifts those pixels into a floating overlay you can drag anywhere
    /// before it's committed back into the bitmap - directly addressing
    /// "I could not copy/cut/paste a portion and move it around".
    /// </summary>
    private void StartMovingSelection(Int32Rect sel, System.Windows.Point mouseStart)
    {
        var clipped = ClampToCanvas(sel);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        PushUndoState();

        int stride = clipped.Width * 4;
        var pixels = new byte[clipped.Height * stride];
        _bitmap.CopyPixels(clipped, pixels, stride, 0);
        _floatingPixels = (clipped.Width, clipped.Height, pixels);
        _moveOriginalRect = clipped;
        _moveStartPixel = mouseStart;

        // Clear the original spot immediately so it doesn't appear duplicated while dragging.
        for (int y = clipped.Y; y < clipped.Y + clipped.Height; y++)
            for (int x = clipped.X; x < clipped.X + clipped.Width; x++)
                SetPixel(x, y, Colors.Transparent);

        var floatBitmap = new WriteableBitmap(clipped.Width, clipped.Height, 96, 96, PixelFormats.Bgra32, null);
        floatBitmap.WritePixels(new Int32Rect(0, 0, clipped.Width, clipped.Height), pixels, stride, 0);

        var floatImage = new Image { Source = floatBitmap, Width = clipped.Width * _zoom, Height = clipped.Height * _zoom, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(floatImage, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(floatImage, clipped.X * _zoom);
        Canvas.SetTop(floatImage, clipped.Y * _zoom);

        ShapeOverlay.Children.Clear();
        ShapeOverlay.Children.Add(floatImage);
        _floatingImage = floatImage;

        _movingSelection = true;
        CanvasImage.CaptureMouse();
    }

    private void CommitMovingSelection(System.Windows.Point mouseEnd)
    {
        if (_floatingPixels is not { } floating)
        {
            _movingSelection = false;
            return;
        }

        int dx = (int)Math.Round(mouseEnd.X - _moveStartPixel.X);
        int dy = (int)Math.Round(mouseEnd.Y - _moveStartPixel.Y);
        int destX = Math.Max(0, _moveOriginalRect.X + dx);
        int destY = Math.Max(0, _moveOriginalRect.Y + dy);
        int destW = Math.Min(floating.Width, _bitmap.PixelWidth - destX);
        int destH = Math.Min(floating.Height, _bitmap.PixelHeight - destY);

        if (destW > 0 && destH > 0)
        {
            _bitmap.WritePixels(new Int32Rect(destX, destY, destW, destH), floating.Pixels, floating.Width * 4, 0);
            _selection = new Int32Rect(destX, destY, destW, destH);
        }

        _movingSelection = false;
        _floatingPixels = null;
        _floatingImage = null;
        DrawSelectionOverlay();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelection();
    private void Cut_Click(object sender, RoutedEventArgs e) => CutSelection();
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteClipboard();

    private void CopySelection()
    {
        if (_selection is not { } sel || sel.Width <= 0 || sel.Height <= 0) return;
        var clipped = ClampToCanvas(sel);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        int stride = clipped.Width * 4;
        var pixels = new byte[clipped.Height * stride];
        _bitmap.CopyPixels(clipped, pixels, stride, 0);
        _clipboard = (clipped.Width, clipped.Height, pixels);

        // Also push the real Windows clipboard, not just this app's own internal one - so a selection copied here can be pasted into Paint, Word, anywhere else too.
        try
        {
            var bitmapSource = BitmapSource.Create(clipped.Width, clipped.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            Clipboard.SetImage(bitmapSource);
        }
        catch { /* internal clipboard already set above - system clipboard is a bonus, not required */ }
    }

    private void CutSelection()
    {
        if (_selection is not { } sel || sel.Width <= 0 || sel.Height <= 0) return;
        CopySelection();
        PushUndoState();
        var clipped = ClampToCanvas(sel);
        for (int y = clipped.Y; y < clipped.Y + clipped.Height; y++)
            for (int x = clipped.X; x < clipped.X + clipped.Width; x++)
                SetPixel(x, y, Colors.Transparent);
    }

    /// <summary>
    /// Real external-clipboard paste, per spec - an image copied from
    /// Paint, a Snipping Tool capture, or any other Windows program now
    /// pastes directly onto the drawing canvas, not just a selection
    /// copied earlier within this same drawing session. The real system
    /// clipboard is checked FIRST; this window's own internal selection
    /// clipboard is the fallback when the system clipboard has no image.
    /// </summary>
    private void PasteClipboard()
    {
        if (Clipboard.ContainsImage())
        {
            BitmapSource? clipboardImage = null;
            try { clipboardImage = Clipboard.GetImage(); } catch { /* fall through to internal clipboard below */ }

            if (clipboardImage is not null)
            {
                var converted = new FormatConvertedBitmap(clipboardImage, PixelFormats.Bgra32, null, 0);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                var pixels = new byte[height * stride];
                converted.CopyPixels(pixels, stride, 0);

                PasteIntoCanvas(width, height, pixels);
                return;
            }
        }

        if (_clipboard is not { } clip) return;
        PasteIntoCanvas(clip.Width, clip.Height, clip.Pixels);
    }

    private void PasteIntoCanvas(int width, int height, byte[] pixels)
    {
        PushUndoState();

        // Pastes at the top-left of the current selection if one exists, otherwise (0,0).
        // Clamped so a paste near the canvas edge trims to fit instead of throwing.
        int destX = Math.Max(0, _selection?.X ?? 0);
        int destY = Math.Max(0, _selection?.Y ?? 0);
        int destW = Math.Min(width, _bitmap.PixelWidth - destX);
        int destH = Math.Min(height, _bitmap.PixelHeight - destY);
        if (destW <= 0 || destH <= 0) return;

        _bitmap.WritePixels(new Int32Rect(destX, destY, destW, destH), pixels, width * 4, 0);
    }

    private Int32Rect ClampToCanvas(Int32Rect rect)
    {
        int x = Math.Max(0, rect.X), y = Math.Max(0, rect.Y);
        int right = Math.Min(_bitmap.PixelWidth, rect.X + rect.Width);
        int bottom = Math.Min(_bitmap.PixelHeight, rect.Y + rect.Height);
        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    // ---------- Image menu: canvas size, flip, rotate, clear ----------

    private void CanvasSize_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ResizePropertiesWindow(_bitmap, _sprite?.Name ?? "Sprite") { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Result || dialog.ResultBitmap is null) return;

        PushUndoState();
        var pixels = new byte[dialog.ResultBitmap.PixelHeight * dialog.ResultBitmap.PixelWidth * 4];
        dialog.ResultBitmap.CopyPixels(pixels, dialog.ResultBitmap.PixelWidth * 4, 0);
        ReplaceBitmap(dialog.ResultBitmap.PixelWidth, dialog.ResultBitmap.PixelHeight, pixels, dialog.ResultBitmap.PixelWidth * 4);

        // Keep the Origin pinned to the same spot on the actual artwork through
        // the resize - a proportional scale for Scale Image, an additive shift
        // (matching the anchor) for Resize Canvas. Origin itself lives on the
        // owning SpriteEntry, not the frame, so this only applies with one.
        if (dialog.ApplyToOrigin && _sprite is not null)
        {
            _sprite.OriginX = (int)Math.Round(_sprite.OriginX * dialog.OriginScaleX) + dialog.OriginDeltaX;
            _sprite.OriginY = (int)Math.Round(_sprite.OriginY * dialog.OriginScaleY) + dialog.OriginDeltaY;
        }
    }

    private void FlipHorizontal_Click(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        var pixels = ReadAllPixels(out int stride);
        int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;
        var result = new byte[pixels.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Array.Copy(pixels, y * stride + x * 4, result, y * stride + (w - 1 - x) * 4, 4);
        ReplaceBitmap(w, h, result, stride);
    }

    private void FlipVertical_Click(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        var pixels = ReadAllPixels(out int stride);
        int h = _bitmap.PixelHeight;
        var result = new byte[pixels.Length];
        for (int y = 0; y < h; y++)
            Array.Copy(pixels, y * stride, result, (h - 1 - y) * stride, stride);
        ReplaceBitmap(_bitmap.PixelWidth, h, result, stride);
    }

    private void RotateClockwise_Click(object sender, RoutedEventArgs e) => Rotate(clockwise: true);
    private void RotateCounterClockwise_Click(object sender, RoutedEventArgs e) => Rotate(clockwise: false);

    private void Rotate(bool clockwise)
    {
        PushUndoState();
        var pixels = ReadAllPixels(out int stride);
        int w = _bitmap.PixelWidth, h = _bitmap.PixelHeight;
        int newW = h, newH = w, newStride = newW * 4;
        var result = new byte[newH * newStride];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int nx = clockwise ? (h - 1 - y) : y;
                int ny = clockwise ? x : (w - 1 - x);
                Array.Copy(pixels, y * stride + x * 4, result, ny * newStride + nx * 4, 4);
            }
        }
        ReplaceBitmap(newW, newH, result, newStride);
    }

    private void ClearCanvas_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear the entire canvas to transparent?", "Clear Canvas", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        PushUndoState();
        var empty = new byte[_bitmap.PixelHeight * _bitmap.PixelWidth * 4];
        ReplaceBitmap(_bitmap.PixelWidth, _bitmap.PixelHeight, empty, _bitmap.PixelWidth * 4);
    }

    // ---------- Zoom ----------

    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoom = Math.Min(32, _zoom * 2); ApplyZoomToCanvasSize(); RedrawGrid(); UpdateZoomText(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoom = Math.Max(1, _zoom / 2); ApplyZoomToCanvasSize(); RedrawGrid(); UpdateZoomText(); }
    private void UpdateZoomText() => ZoomText.Text = $"{_zoom * 100:0}%";

    // ---------- Layers (embedded here, not in the outer frame editor - matching GameMaker's actual layout) ----------

    private static string GenerateTempImagePath() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sandcastle_layer_{Guid.NewGuid():N}.png");

    private string EnsureFrameFilePath()
    {
        if (string.IsNullOrEmpty(_frame.FilePath))
            _frame.FilePath = GenerateTempImagePath();
        return _frame.FilePath;
    }

    /// <summary>
    /// Persists whatever is currently on screen for the active layer (or, in
    /// legacy no-layers mode, the frame's own flat file) before swapping the
    /// canvas to a different layer - otherwise switching layers would
    /// silently discard in-progress edits. One consequence: edits are no
    /// longer fully "cancel-able" once you've switched layers at least once
    /// in a session, the same tradeoff GameMaker's own live layer editing
    /// makes.
    /// </summary>
    private void FlushActiveBitmapToDisk()
    {
        if (_sprite is not null && _activeLayerId >= 0)
        {
            string path = _frame.LayerFilePaths.GetValueOrDefault(_activeLayerId, string.Empty);
            if (string.IsNullOrEmpty(path))
            {
                path = GenerateTempImagePath();
                _frame.LayerFilePaths[_activeLayerId] = path;
            }
            SaveBitmapToFile(path, new PngBitmapEncoder());
            SpriteCompositor.Composite(_sprite, _frame);
        }
        else
        {
            SaveBitmapToFile(EnsureFrameFilePath(), new PngBitmapEncoder());
        }
    }

    private void RecompositeAndRefreshContext()
    {
        if (_sprite is null) return;
        SpriteCompositor.Composite(_sprite, _frame);
        RebuildLayerContextImages();
    }

    /// <summary>
    /// The composited "everything except the active layer" images shown
    /// behind/in front of the editable canvas - live context while you
    /// draw, so drawing the eyes with the hat layer visible above them
    /// actually works, instead of each layer being a blind popup.
    /// </summary>
    private void RebuildLayerContextImages()
    {
        if (_sprite is null || _activeLayerId < 0)
        {
            BelowLayersImage.Source = null;
            AboveLayersImage.Source = null;
            return;
        }

        int activeIndex = _sprite.Layers.FindIndex(l => l.Id == _activeLayerId);
        BelowLayersImage.Source = SpriteCompositor.BuildComposite(_sprite, _frame, l => _sprite.Layers.IndexOf(l) > activeIndex);
        AboveLayersImage.Source = SpriteCompositor.BuildComposite(_sprite, _frame, l => _sprite.Layers.IndexOf(l) < activeIndex);
    }

    private void SwitchActiveLayer(int layerId)
    {
        if (layerId == _activeLayerId) return;
        FlushActiveBitmapToDisk();
        _activeLayerId = layerId;
        _undoStack.Clear();
        _redoStack.Clear();
        LoadActiveLayerIntoBitmap(_bitmap.PixelWidth, _bitmap.PixelHeight);
        PushUndoState();
        RebuildLayersPanel();
    }

    /// <summary>The first time a layer is added to a sprite that predates layers, its existing frame art becomes that first layer's content - nothing is lost or hidden.</summary>
    private void EnsureMigratedToLayers()
    {
        if (_sprite is null || _sprite.Layers.Count > 0) return;
        FlushActiveBitmapToDisk(); // make sure what's on screen right now is what gets migrated, not a stale on-disk copy
        var layer = new SpriteLayer { Id = _sprite.GetNextLayerId(), Name = "Layer 1" };
        _sprite.Layers.Add(layer);
        foreach (var f in _sprite.Frames)
            if (!string.IsNullOrEmpty(f.FilePath))
                f.LayerFilePaths[layer.Id] = f.FilePath;
        _activeLayerId = layer.Id;
    }

    private void AddLayer_Click(object sender, RoutedEventArgs e)
    {
        if (_sprite is null) return;

        if (_sprite.Layers.Count == 0)
        {
            EnsureMigratedToLayers();
        }
        else
        {
            FlushActiveBitmapToDisk();
            int number = _sprite.Layers.Count(l => !l.IsGroup) + 1;
            var newLayer = new SpriteLayer { Id = _sprite.GetNextLayerId(), Name = $"Layer {number}" };
            _sprite.Layers.Insert(0, newLayer); // new layers go on top, matching Aseprite/GameMaker
            _activeLayerId = newLayer.Id;
        }

        _undoStack.Clear();
        _redoStack.Clear();
        LoadActiveLayerIntoBitmap(_bitmap.PixelWidth, _bitmap.PixelHeight); // keep the new layer the same size as the frame's existing canvas
        PushUndoState();
        RebuildLayersPanel();
    }

    private void AddLayerGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_sprite is null) return;

        if (_sprite.Layers.Count == 0)
        {
            EnsureMigratedToLayers();
            _undoStack.Clear();
            _redoStack.Clear();
            LoadActiveLayerIntoBitmap(_bitmap.PixelWidth, _bitmap.PixelHeight);
            PushUndoState();
        }

        var group = new SpriteLayer { Id = _sprite.GetNextLayerId(), Name = "Group", IsGroup = true };
        _sprite.Layers.Insert(0, group);
        RebuildLayersPanel();
    }

    private void DeleteLayer(SpriteLayer layer)
    {
        if (_sprite is null) return;
        if (_sprite.Layers.Count(l => !l.IsGroup) <= 1 && !layer.IsGroup)
        {
            MessageBox.Show("A sprite needs at least one layer.", "Delete Layer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Deleting a group also ungroups (not deletes) its children, rather than silently destroying their art.
        foreach (var child in _sprite.Layers.Where(l => l.ParentGroupId == layer.Id))
            child.ParentGroupId = -1;

        _sprite.Layers.Remove(layer);
        foreach (var f in _sprite.Frames)
            f.LayerFilePaths.Remove(layer.Id);

        if (_activeLayerId == layer.Id)
        {
            _activeLayerId = _sprite.Layers.FirstOrDefault(l => !l.IsGroup)?.Id ?? -1;
            _undoStack.Clear();
            _redoStack.Clear();
            LoadActiveLayerIntoBitmap(_bitmap.PixelWidth, _bitmap.PixelHeight);
            PushUndoState();
        }
        else
        {
            RecompositeAndRefreshContext();
        }

        RebuildLayersPanel();
    }

    private void RebuildLayersPanel()
    {
        if (_sprite is null) return;
        LayersListPanel.Children.Clear();
        foreach (var layer in _sprite.Layers)
            LayersListPanel.Children.Add(BuildLayerRow(layer));
    }

    private UIElement BuildLayerRow(SpriteLayer layer)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(layer.ParentGroupId >= 0 ? 16 : 0, 0, 0, 2) };

        var deleteButton = new Button
        {
            Content = "X", Width = 18, Height = 18, Padding = new Thickness(0), FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Background = Brushes.Firebrick, Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Delete this layer"
        };
        deleteButton.Click += (_, _) => DeleteLayer(layer);

        var visibleCheck = new CheckBox { IsChecked = layer.Visible, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Visible" };
        visibleCheck.Checked += (_, _) => { layer.Visible = true; RecompositeAndRefreshContext(); };
        visibleCheck.Unchecked += (_, _) => { layer.Visible = false; RecompositeAndRefreshContext(); };

        var lockCheck = new CheckBox { IsChecked = layer.Locked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Locked (prevents editing - enforced when picking which layer to draw on)" };
        lockCheck.Checked += (_, _) => layer.Locked = true;
        lockCheck.Unchecked += (_, _) => layer.Locked = false;

        var nameBox = new TextBox { Width = 68, Text = layer.Name, FontWeight = layer.IsGroup ? FontWeights.Bold : FontWeights.Normal };
        nameBox.TextChanged += (_, _) => layer.Name = nameBox.Text;

        row.Children.Add(deleteButton);
        row.Children.Add(visibleCheck);
        row.Children.Add(lockCheck);
        row.Children.Add(nameBox);

        var border = new Border
        {
            BorderBrush = !layer.IsGroup && layer.Id == _activeLayerId ? Brushes.Yellow : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 2),
            Child = row
        };
        if (!layer.IsGroup)
        {
            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonDown += (_, args) =>
            {
                if (layer.Locked)
                {
                    MessageBox.Show("This layer is locked.", "Layer Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                SwitchActiveLayer(layer.Id);
                args.Handled = true;
            };
        }
        return border;
    }

    // ---------- Save / Export ----------

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sprite is not null && _activeLayerId >= 0)
        {
            string layerPath = _frame.LayerFilePaths.GetValueOrDefault(_activeLayerId, string.Empty);
            if (string.IsNullOrEmpty(layerPath))
            {
                layerPath = GenerateTempImagePath();
                _frame.LayerFilePaths[_activeLayerId] = layerPath;
            }
            if (!SaveBitmapToFile(layerPath, new PngBitmapEncoder())) return;

            SpriteCompositor.Composite(_sprite, _frame);
            Result = true;
            DialogResult = true;
            return;
        }

        string path = _frame.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "NewFrame.png" };
            if (dialog.ShowDialog() != true) return;
            path = dialog.FileName;
        }

        if (!SaveBitmapToFile(path, MakeEncoderForPath(path)))
            return;

        _frame.FilePath = path;
        Result = true;
        DialogResult = true;
    }

    private void ExportAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap image (*.bmp)|*.bmp",
            FileName = string.IsNullOrEmpty(_frame.FilePath) ? "sprite.png" : System.IO.Path.GetFileNameWithoutExtension(_frame.FilePath) + ".png"
        };
        if (dialog.ShowDialog() != true) return;

        if (_sprite is not null && _activeLayerId >= 0)
        {
            // Export means the full flattened picture (every visible layer), not just whichever one happens to be active.
            FlushActiveBitmapToDisk(); // so the export reflects what's on screen right now
            var composite = SpriteCompositor.BuildComposite(_sprite, _frame, _ => true);
            if (composite is not null)
            {
                using var stream = new FileStream(dialog.FileName, FileMode.Create);
                var encoder = MakeEncoderForPath(dialog.FileName);
                encoder.Frames.Add(BitmapFrame.Create(composite));
                encoder.Save(stream);
            }
            return;
        }

        SaveBitmapToFile(dialog.FileName, MakeEncoderForPath(dialog.FileName));
    }

    private static BitmapEncoder MakeEncoderForPath(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
        ".bmp" => new BmpBitmapEncoder(),
        _ => new PngBitmapEncoder()
    };

    private bool SaveBitmapToFile(string path, BitmapEncoder encoder)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Create);
            encoder.Frames.Add(BitmapFrame.Create(_bitmap));
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the image:\n\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
