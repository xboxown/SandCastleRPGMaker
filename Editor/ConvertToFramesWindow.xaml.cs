using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Slices a sprite-sheet/strip image into individual frame files, laid out
/// on a grid: Frame Width/Height defines each cell's size, Cell Offset
/// skips whole cells before starting, Pixel Offset is a fine adjustment on
/// top of that (draggable live on the preview), and Separation is the gap
/// between adjacent cells. Number of Frames x Frames per Row determines how
/// many cells get cut and how they wrap into rows.
/// </summary>
public partial class ConvertToFramesWindow : Window
{
    private readonly string _sourcePath;
    private readonly BitmapSource _sourceImage;
    private double _zoom = 1;

    private int _numberOfFrames = 1;
    private int _framesPerRow = 1;
    private int _frameWidth = 32;
    private int _frameHeight = 32;
    private int _hCellOffset;
    private int _vCellOffset;
    private int _hPixelOffset;
    private int _vPixelOffset;
    private int _hSeparation;
    private int _vSeparation;

    private TextBox _hPixelOffsetBox = null!;
    private TextBox _vPixelOffsetBox = null!;

    /// <summary>The frames created by Convert - empty until the user clicks it.</summary>
    public List<SpriteFrame> Result { get; } = new();

    public ConvertToFramesWindow(string sourceImagePath)
    {
        InitializeComponent();
        _sourcePath = sourceImagePath;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(sourceImagePath, UriKind.Absolute);
        bmp.EndInit();
        _sourceImage = bmp;

        SourceImage.Source = _sourceImage;
        ApplyZoom();

        BuildFields();
        RedrawSelectionOverlay();
    }

    // ---------- Numeric fields (built programmatically to avoid repetitive XAML) ----------

    private void BuildFields()
    {
        AddSpinner("Number of Frames", _numberOfFrames, 1, v => { _numberOfFrames = v; RedrawSelectionOverlay(); });
        AddSpinner("Frames per Row", _framesPerRow, 1, v => { _framesPerRow = v; RedrawSelectionOverlay(); });
        AddSpinner("Frame Width", _frameWidth, 1, v => { _frameWidth = v; RedrawSelectionOverlay(); });
        AddSpinner("Frame Height", _frameHeight, 1, v => { _frameHeight = v; RedrawSelectionOverlay(); });
        AddSpinner("Horizontal Cell Offset", _hCellOffset, 0, v => { _hCellOffset = v; RedrawSelectionOverlay(); });
        AddSpinner("Vertical Cell Offset", _vCellOffset, 0, v => { _vCellOffset = v; RedrawSelectionOverlay(); });
        _hPixelOffsetBox = AddSpinner("Horizontal Pixel Offset", _hPixelOffset, 0, v => { _hPixelOffset = v; RedrawSelectionOverlay(); });
        _vPixelOffsetBox = AddSpinner("Vertical Pixel Offset", _vPixelOffset, 0, v => { _vPixelOffset = v; RedrawSelectionOverlay(); });
        AddSpinner("Horizontal Separation", _hSeparation, 0, v => { _hSeparation = v; RedrawSelectionOverlay(); });
        AddSpinner("Vertical Separation", _vSeparation, 0, v => { _vSeparation = v; RedrawSelectionOverlay(); });
    }

    private TextBox AddSpinner(string label, int defaultValue, int min, Action<int> onChanged)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new TextBlock { Text = label });

        var inner = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var textBox = new TextBox { Width = 60, Text = defaultValue.ToString() };
        var upButton = new Button { Content = "\u25B2", Width = 20, Height = 18, Padding = new Thickness(0), FontSize = 8, Margin = new Thickness(4, 0, 0, 0) };
        var downButton = new Button { Content = "\u25BC", Width = 20, Height = 18, Padding = new Thickness(0), FontSize = 8, Margin = new Thickness(2, 0, 0, 0) };

        void Commit()
        {
            if (!int.TryParse(textBox.Text, out int v)) v = min;
            v = Math.Max(min, v);
            textBox.Text = v.ToString();
            onChanged(v);
        }

        textBox.LostFocus += (_, _) => Commit();
        textBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        upButton.Click += (_, _) => { textBox.Text = (int.Parse(textBox.Text) + 1).ToString(); Commit(); };
        downButton.Click += (_, _) => { textBox.Text = Math.Max(min, int.Parse(textBox.Text) - 1).ToString(); Commit(); };

        inner.Children.Add(textBox);
        inner.Children.Add(upButton);
        inner.Children.Add(downButton);
        row.Children.Add(inner);
        FieldsPanel.Children.Add(row);
        return textBox;
    }

    // ---------- Zoom ----------

    private void ApplyZoom()
    {
        SourceImage.Width = _sourceImage.PixelWidth * _zoom;
        SourceImage.Height = _sourceImage.PixelHeight * _zoom;
        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoom = Math.Min(8, _zoom * 1.5); ApplyZoom(); RedrawSelectionOverlay(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoom = Math.Max(0.1, _zoom / 1.5); ApplyZoom(); RedrawSelectionOverlay(); }

    // ---------- Grid math ----------

    private (int X, int Y) CellOrigin() =>
        (_hCellOffset * (_frameWidth + _hSeparation) + _hPixelOffset,
         _vCellOffset * (_frameHeight + _vSeparation) + _vPixelOffset);

    private (int X, int Y) FramePosition(int frameIndex)
    {
        int col = frameIndex % Math.Max(1, _framesPerRow);
        int row = frameIndex / Math.Max(1, _framesPerRow);
        var origin = CellOrigin();
        return (origin.X + col * (_frameWidth + _hSeparation), origin.Y + row * (_frameHeight + _vSeparation));
    }

    // ---------- Preview overlay: one rectangle per frame, first one draggable ----------

    private bool _dragging;
    private System.Windows.Point _dragStartMouse;
    private int _dragStartOffsetX, _dragStartOffsetY;

    private void RedrawSelectionOverlay()
    {
        SelectionOverlay.Children.Clear();

        for (int i = 0; i < _numberOfFrames; i++)
        {
            var pos = FramePosition(i);
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = _frameWidth * _zoom,
                Height = _frameHeight * _zoom,
                Stroke = i == 0 ? Brushes.Yellow : Brushes.LimeGreen,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(rect, pos.X * _zoom);
            Canvas.SetTop(rect, pos.Y * _zoom);

            if (i == 0)
            {
                rect.Cursor = Cursors.SizeAll;
                rect.ToolTip = "Drag to reposition the grid";
                rect.MouseLeftButtonDown += (_, args) =>
                {
                    _dragging = true;
                    _dragStartMouse = args.GetPosition(SelectionOverlay);
                    _dragStartOffsetX = _hPixelOffset;
                    _dragStartOffsetY = _vPixelOffset;
                    rect.CaptureMouse();
                };
                rect.MouseMove += (_, args) =>
                {
                    if (!_dragging) return;
                    var current = args.GetPosition(SelectionOverlay);
                    int dx = (int)((current.X - _dragStartMouse.X) / _zoom);
                    int dy = (int)((current.Y - _dragStartMouse.Y) / _zoom);
                    _hPixelOffset = Math.Max(0, _dragStartOffsetX + dx);
                    _vPixelOffset = Math.Max(0, _dragStartOffsetY + dy);
                    _hPixelOffsetBox.Text = _hPixelOffset.ToString();
                    _vPixelOffsetBox.Text = _vPixelOffset.ToString();
                    RedrawSelectionOverlay();
                };
                rect.MouseLeftButtonUp += (_, _) => { _dragging = false; rect.ReleaseMouseCapture(); };
            }

            SelectionOverlay.Children.Add(rect);
        }
    }

    // ---------- Convert ----------

    private void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        Result.Clear();
        string dir = Path.GetDirectoryName(_sourcePath) ?? Path.GetTempPath();
        string baseName = Path.GetFileNameWithoutExtension(_sourcePath);
        var skippedDetails = new List<string>();

        for (int i = 0; i < _numberOfFrames; i++)
        {
            var pos = FramePosition(i);
            if (pos.X < 0 || pos.Y < 0 || pos.X + _frameWidth > _sourceImage.PixelWidth || pos.Y + _frameHeight > _sourceImage.PixelHeight)
            {
                // Diagnostic detail per skipped frame - exactly what was computed vs. the real image bounds, so a mismatch is visible immediately instead of just "it failed."
                skippedDetails.Add($"Frame {i + 1}: needs ({pos.X},{pos.Y}) to ({pos.X + _frameWidth},{pos.Y + _frameHeight}), image is {_sourceImage.PixelWidth}x{_sourceImage.PixelHeight}");
                continue; // out of bounds - skip rather than crash on a bad grid setting
            }

            var cropped = new CroppedBitmap(_sourceImage, new Int32Rect(pos.X, pos.Y, _frameWidth, _frameHeight));

            string outPath = Path.Combine(dir, $"{baseName}_frame{i + 1:00}.png");
            try
            {
                using var stream = new FileStream(outPath, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(cropped));
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save frame {i + 1}:\n\n{ex.Message}", "Convert to Frames", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Result.Add(new SpriteFrame { FilePath = outPath });
        }

        if (Result.Count < _numberOfFrames)
        {
            string suggestion = string.Empty;
            if (_frameWidth > _sourceImage.PixelWidth || _frameHeight > _sourceImage.PixelHeight)
            {
                // The most common real-world cause: the source file itself is a pixel or few off from what was intended (an easy mistake when exporting a sprite sheet), not a grid-configuration error. Name the likely fix directly rather than leaving the person to do this same subtraction themselves.
                suggestion = $"\n\nThis usually means the source image itself is smaller than expected - it's {_sourceImage.PixelWidth}x{_sourceImage.PixelHeight}px. " +
                             $"Try Frame Width {Math.Min(_frameWidth, _sourceImage.PixelWidth)} / Frame Height {Math.Min(_frameHeight, _sourceImage.PixelHeight)} to match it, " +
                             "or re-export the source image at the size you actually intended.";
            }

            MessageBox.Show(
                $"{_numberOfFrames - Result.Count} frame(s) fell outside the image bounds and were skipped. Adjust the offsets and try again if needed.{suggestion}\n\n" +
                string.Join("\n", skippedDetails),
                "Convert to Frames", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DialogResult = Result.Count > 0;
    }
}
