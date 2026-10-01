using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class SpriteFrameEditorWindow : Window
{
    private readonly SpriteEntry _sprite;
    private readonly ResourceDatabase _resources;
    /// <summary>True when this sprite belongs to SpriteDatabase.TilesetGroupName - the sound icon is hidden for tileset sprites specifically, since a tileset's Frames are individual tile cells, not an animation with per-frame audio cues.</summary>
    private readonly bool _isTilesetGroup;
    private bool _suppress;

    private int _currentFrameIndex;
    private bool _isPlaying;
    private readonly DispatcherTimer _playbackTimer = new();

    private static readonly string[] AnchorOptions =
    {
        "Top Left", "Top Center", "Top Right",
        "Middle Left", "Middle Center", "Middle Right",
        "Bottom Left", "Bottom Center", "Bottom Right",
        "Custom"
    };

    public SpriteFrameEditorWindow(SpriteEntry sprite, ResourceDatabase resources, bool isTilesetGroup = false)
    {
        InitializeComponent();
        _sprite = sprite;
        _resources = resources;
        _isTilesetGroup = isTilesetGroup;
        PreviewKeyDown += SpriteFrameEditorWindow_PreviewKeyDown;

        OriginAnchorCombo.ItemsSource = AnchorOptions;

        _suppress = true;
        NameBox.Text = _sprite.Name;
        FpsBox.Text = _sprite.Fps.ToString();
        LoopCheck.IsChecked = _sprite.Loop;
        OriginXBox.Text = _sprite.OriginX.ToString();
        OriginYBox.Text = _sprite.OriginY.ToString();
        OriginAnchorCombo.SelectedItem = AnchorOptions.Contains(_sprite.OriginAnchor) ? _sprite.OriginAnchor : "Custom";

        InitMaskUi(_sprite.CollisionMask, CollisionModeCombo, CollisionTypeCombo, CollisionTypeDescText, ToleranceSlider, ToleranceValueText, MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);
        InitMaskUi(_sprite.LegsMask, LegsCollisionModeCombo, LegsCollisionTypeCombo, LegsCollisionTypeDescText, LegsToleranceSlider, LegsToleranceValueText, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);
        SplitLegsBodyCheck.IsChecked = _sprite.SplitLegsBodyMask;
        UpdateSplitLegsBodyVisibility();
        UpdateCollisionBoxEnabledState();
        UpdateLegsCollisionBoxEnabledState();

        NineSliceActiveCheck.IsChecked = _sprite.NineSlice.Enabled;
        SliceLeftBox.Text = _sprite.NineSlice.Left.ToString();
        SliceRightBox.Text = _sprite.NineSlice.Right.ToString();
        SliceTopBox.Text = _sprite.NineSlice.Top.ToString();
        SliceBottomBox.Text = _sprite.NineSlice.Bottom.ToString();
        _suppress = false;

        Title = $"Sprite: {_sprite.Name}";

        _playbackTimer.Tick += (_, _) => AdvanceFrame();

        RebuildFrameStrip();
        UpdatePreview();

        // A freshly-created sprite (or one whose art changed since it was
        // last saved) shouldn't sit with a degenerate zero-size Automatic
        // box until the user happens to touch a combo - recompute once up
        // front, same as any other Automatic-mode refresh.
        if (_sprite.CollisionMask.Mode == CollisionMaskMode.Automatic && _sprite.CollisionMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.CollisionMask, MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);
        if (_sprite.SplitLegsBodyMask && _sprite.LegsMask.Mode == CollisionMaskMode.Automatic && _sprite.LegsMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.LegsMask, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);

        // Layers panel now lives inside SpriteDrawingWindow itself (opened when
        // a frame is double-clicked) rather than here in the frame editor.

        // Belt-and-suspenders for the very first paint: PreviewImage_SizeChanged
        // (which keeps OverlayCanvas's size locked to PreviewImage's) only fires
        // once layout has actually happened, which can land after the RedrawOverlay
        // calls already run above during construction. Loaded guarantees a final
        // sync + redraw once real layout numbers exist, closing that gap.
        Loaded += (_, _) =>
        {
            OverlayCanvas.Width = PreviewImage.ActualWidth;
            OverlayCanvas.Height = PreviewImage.ActualHeight;
            RedrawOverlay();
        };

        Closing += (_, _) => _playbackTimer.Stop();
    }

    private static void InitMaskUi(CollisionMaskSettings mask, ComboBox modeCombo, ComboBox typeCombo, TextBlock descText,
        Slider toleranceSlider, TextBlock toleranceText, TextBox leftBox, TextBox rightBox, TextBox topBox, TextBox bottomBox)
    {
        modeCombo.ItemsSource = Enum.GetValues<CollisionMaskMode>();
        typeCombo.ItemsSource = Enum.GetValues<CollisionMaskType>();
        modeCombo.SelectedItem = mask.Mode;
        typeCombo.SelectedItem = mask.Type;
        descText.Text = GetCollisionTypeDescription(mask.Type);
        toleranceSlider.Value = mask.Tolerance;
        toleranceText.Text = mask.Tolerance.ToString();
        leftBox.Text = mask.Left.ToString();
        rightBox.Text = mask.Right.ToString();
        topBox.Text = mask.Top.ToString();
        bottomBox.Text = mask.Bottom.ToString();
    }

    private static BitmapImage? LoadBitmap(string path) => LoadBitmap(path, out _);

    private static BitmapImage? LoadBitmap(string path, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(path)) { error = "No file path given."; return null; }
        if (!File.Exists(path)) { error = $"File does not exist:\n{path}"; return null; }
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
        catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; return null; }
    }

    /// <summary>Keeps FilePath synced to the first frame, so every existing consumer (Book Editor, item picker, etc.) that only knows about a static FilePath still shows something sensible.</summary>
    private void SyncLegacyFilePath()
    {
        _sprite.FilePath = _sprite.Frames.Count > 0 ? _sprite.Frames[0].FilePath : string.Empty;
    }

    // ---------- Metadata ----------

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        _sprite.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _sprite.Name : NameBox.Text;
        Title = $"Sprite: {_sprite.Name}";
    }

    private void FpsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (int.TryParse(FpsBox.Text, out int fps) && fps > 0)
        {
            _sprite.Fps = fps;
            if (_isPlaying) RestartPlaybackTimer();
        }
    }

    private void LoopCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _sprite.Loop = LoopCheck.IsChecked == true;
    }

    private void OriginBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (int.TryParse(OriginXBox.Text, out int x)) _sprite.OriginX = x;
        if (int.TryParse(OriginYBox.Text, out int y)) _sprite.OriginY = y;
        UpdateAnchorToMatchCurrentOrigin();
        RedrawOverlay();
    }

    private void OriginAnchorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        string anchor = OriginAnchorCombo.SelectedItem as string ?? "Top Left";
        _sprite.OriginAnchor = anchor;

        var (frameW, frameH) = GetCurrentFrameSize();
        var computed = ComputeAnchorOrigin(anchor, frameW, frameH);
        if (computed is { } origin)
        {
            _sprite.OriginX = origin.X;
            _sprite.OriginY = origin.Y;
            _suppress = true;
            OriginXBox.Text = origin.X.ToString();
            OriginYBox.Text = origin.Y.ToString();
            _suppress = false;
        }
        RedrawOverlay();
    }

    /// <summary>Selects "Custom" in the anchor dropdown when the current X/Y don't match any preset for the frame's size - mirrors GameMaker's own behavior.</summary>
    private void UpdateAnchorToMatchCurrentOrigin()
    {
        var (frameW, frameH) = GetCurrentFrameSize();
        string matched = "Custom";
        foreach (var option in AnchorOptions)
        {
            if (option == "Custom") continue;
            var computed = ComputeAnchorOrigin(option, frameW, frameH);
            if (computed is { } o && o.X == _sprite.OriginX && o.Y == _sprite.OriginY) { matched = option; break; }
        }
        _sprite.OriginAnchor = matched;
        _suppress = true;
        OriginAnchorCombo.SelectedItem = matched;
        _suppress = false;
    }

    private static (int X, int Y)? ComputeAnchorOrigin(string anchor, int width, int height) => anchor switch
    {
        "Top Left" => (0, 0),
        "Top Center" => (width / 2, 0),
        "Top Right" => (width, 0),
        "Middle Left" => (0, height / 2),
        "Middle Center" => (width / 2, height / 2),
        "Middle Right" => (width, height / 2),
        "Bottom Left" => (0, height),
        "Bottom Center" => (width / 2, height),
        "Bottom Right" => (width, height),
        _ => null
    };

    private (int Width, int Height) GetCurrentFrameSize()
    {
        if (PreviewImage.Source is BitmapSource bmp) return (bmp.PixelWidth, bmp.PixelHeight);
        return (0, 0);
    }

    // ---------- Playback ----------

    private void RestartPlaybackTimer()
    {
        _playbackTimer.Stop();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _sprite.Fps));
        _playbackTimer.Start();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPlaying = !_isPlaying;
        PlayPauseButton.Content = _isPlaying ? "\u275A\u275A" : "\u25B6";
        if (_isPlaying) RestartPlaybackTimer(); else _playbackTimer.Stop();
    }

    private void AdvanceFrame()
    {
        if (_sprite.Frames.Count == 0) return;
        _currentFrameIndex++;
        if (_currentFrameIndex >= _sprite.Frames.Count)
        {
            if (_sprite.Loop) _currentFrameIndex = 0;
            else { _currentFrameIndex = _sprite.Frames.Count - 1; _isPlaying = false; _playbackTimer.Stop(); PlayPauseButton.Content = "\u25B6"; }
        }
        UpdatePreview();
        HighlightSelectedThumbnail();

        var frame = _sprite.Frames[_currentFrameIndex];
        if (frame.SoundResourceId >= 0) SoundPlayback.TryPlay(_resources, frame.SoundResourceId);
    }

    private void PrevFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sprite.Frames.Count == 0) return;
        _currentFrameIndex = (_currentFrameIndex - 1 + _sprite.Frames.Count) % _sprite.Frames.Count;
        UpdatePreview();
        HighlightSelectedThumbnail();
    }

    private void NextFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sprite.Frames.Count == 0) return;
        _currentFrameIndex = (_currentFrameIndex + 1) % _sprite.Frames.Count;
        UpdatePreview();
        HighlightSelectedThumbnail();
    }

    private void UpdatePreview()
    {
        string? loadError = null;
        if (_sprite.Frames.Count > 0 && _currentFrameIndex < _sprite.Frames.Count)
        {
            PreviewImage.Source = LoadBitmap(_sprite.Frames[_currentFrameIndex].FilePath, out loadError);
        }
        else
        {
            PreviewImage.Source = null;
        }

        FrameIndexText.Text = _sprite.Frames.Count > 0 ? $"Frame {_currentFrameIndex + 1} / {_sprite.Frames.Count}" : "No frames";
        FrameIndexText.Text += loadError is not null ? $"  \u26A0 Could not load image: {loadError}" : "";
        FrameCountText.Text = _sprite.Frames.Count.ToString();
        RedrawOverlay();
    }

    /// <summary>
    /// OverlayCanvas is supposed to be exactly the same size as PreviewImage
    /// (they're siblings in the same Grid cell, both defaulting to Stretch)
    /// - but rather than trust that implicit layout agreement to always hold,
    /// force it explicitly here. Every mask/origin/nine-slice coordinate is
    /// computed relative to PreviewImage's own ActualWidth/ActualHeight; if
    /// OverlayCanvas ever ends up a different actual size than that, every
    /// one of those coordinates is drawn in the wrong place relative to what
    /// you actually see - which is exactly the "two different-sized frames"
    /// mismatch spotted directly in the running app.
    /// </summary>
    private void PreviewImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        OverlayCanvas.Width = PreviewImage.ActualWidth;
        OverlayCanvas.Height = PreviewImage.ActualHeight;
        RedrawOverlay();
    }

    // ---------- Frame strip ----------

    private readonly List<Border> _thumbnailBorders = new();

    private void RebuildFrameStrip()
    {
        FrameStripPanel.Children.Clear();
        _thumbnailBorders.Clear();

        for (int i = 0; i < _sprite.Frames.Count; i++)
            FrameStripPanel.Children.Add(BuildFrameThumbnail(_sprite.Frames[i], i));

        FrameStripPanel.Children.Add(BuildAddFrameTile());

        HighlightSelectedThumbnail();
        FrameCountText.Text = _sprite.Frames.Count.ToString();
    }

    private UIElement BuildFrameThumbnail(SpriteFrame frame, int index)
    {
        var border = new Border
        {
            Width = 80,
            Height = 90,
            Margin = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.DarkGray
        };
        _thumbnailBorders.Add(border);

        var grid = new Grid();
        var image = new Image { Source = LoadBitmap(frame.FilePath), Stretch = Stretch.Uniform, Margin = new Thickness(2) };
        grid.Children.Add(image);

        // Volume icon - filled/outlined depending on whether a sound is attached.
        // Hidden entirely for the Tileset group - each frame there is a
        // distinct tile cell, not an animation frame, so per-frame sound
        // doesn't apply to it the way it does everywhere else.
        if (!_isTilesetGroup)
        {
            var volumeIcon = new TextBlock
            {
                Text = frame.SoundResourceId >= 0 ? "\U0001F50A" : "\U0001F507",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 2),
                ToolTip = frame.SoundResourceId >= 0 ? "Click to change or remove sound" : "Click to add a sound"
            };
            volumeIcon.MouseLeftButtonDown += (_, args) =>
            {
                ShowFrameSoundMenu(frame, volumeIcon);
                args.Handled = true;
            };
            grid.Children.Add(volumeIcon);
        }

        var indexLabel = new TextBlock
        {
            Text = (index + 1).ToString(),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        grid.Children.Add(indexLabel);

        // Delete X - top-left corner, matching the graphical-delete pattern used elsewhere in this project.
        var deleteButton = new Button
        {
            Content = "X",
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = Brushes.Firebrick,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1),
            ToolTip = "Delete this frame"
        };
        deleteButton.Click += (_, args) =>
        {
            DeleteFrame(frame);
            args.Handled = true;
        };
        grid.Children.Add(deleteButton);

        border.Child = grid;
        border.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount == 2)
            {
                _currentFrameIndex = index;
                OpenDrawingWindowForFrame(frame);
                args.Handled = true;
            }
            else
            {
                _currentFrameIndex = index;
                UpdatePreview();
                HighlightSelectedThumbnail();
            }
        };

        return border;
    }

    private void DeleteFrame(SpriteFrame frame)
    {
        int index = _sprite.Frames.IndexOf(frame);
        if (index < 0) return;
        PushUndoSnapshot();
        _sprite.Frames.RemoveAt(index);
        SyncLegacyFilePath();
        _currentFrameIndex = Math.Max(0, Math.Min(_currentFrameIndex, _sprite.Frames.Count - 1));
        RebuildFrameStrip();
        UpdatePreview();
    }

    private void OpenDrawingWindowForFrame(SpriteFrame frame)
    {
        var drawing = new SpriteDrawingWindow(_sprite, frame) { Owner = this };
        if (drawing.ShowDialog() != true || !drawing.Result) return;
        SyncLegacyFilePath();
        RebuildFrameStrip();
        UpdatePreview();
        RefreshDerivedGeometryForCurrentFrame();
    }

    /// <summary>
    /// An anchored Origin (everything except "Custom") and an Automatic
    /// collision mask are both computed FROM the frame's pixel size at the
    /// moment you set them - not kept as a live formula. If the art later
    /// changes shape (canvas resized, a bigger layer added, redrawn taller,
    /// etc.) those stored values go stale: Top Right origin stays wherever
    /// the old width put it, and an Automatic mask stays sized to the old
    /// bounds, leaving part of the new art uncovered. Re-derive both here,
    /// right after any edit that could have changed the current frame's
    /// size, so they track the art instead of freezing at whatever it
    /// looked like when they were last touched.
    /// </summary>
    private void RefreshDerivedGeometryForCurrentFrame()
    {
        var (frameW, frameH) = GetCurrentFrameSize();
        if (frameW == 0 || frameH == 0) return;

        if (_sprite.OriginAnchor != "Custom" && ComputeAnchorOrigin(_sprite.OriginAnchor, frameW, frameH) is { } origin)
        {
            _sprite.OriginX = origin.X;
            _sprite.OriginY = origin.Y;
            _suppress = true;
            OriginXBox.Text = origin.X.ToString();
            OriginYBox.Text = origin.Y.ToString();
            _suppress = false;
        }

        if (_sprite.CollisionMask.Mode == CollisionMaskMode.Automatic && _sprite.CollisionMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.CollisionMask, MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);
        if (_sprite.SplitLegsBodyMask && _sprite.LegsMask.Mode == CollisionMaskMode.Automatic && _sprite.LegsMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.LegsMask, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);

        RedrawOverlay();
    }

    /// <summary>The "+" tile at the end of the frame strip - Aseprite-style way to start a brand-new blank frame, replacing the old standalone buttons.</summary>
    private UIElement BuildAddFrameTile()
    {
        var border = new Border
        {
            Width = 80,
            Height = 90,
            Margin = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(2, 2, 2, 2),
            BorderBrush = Brushes.Gray,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Draw a new blank frame"
        };
        border.Child = new TextBlock
        {
            Text = "+",
            FontSize = 32,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.MouseLeftButtonDown += (_, _) => DrawNewFrame();
        return border;
    }

    private void DrawNewFrame()
    {
        var sizeInput = SimpleInputWindow.Show(this, "Canvas size (width x height, e.g. 32x32):", "New Drawn Frame", "32x32");
        if (string.IsNullOrWhiteSpace(sizeInput)) return;

        var parts = sizeInput.Split('x', 'X');
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out int w) || !int.TryParse(parts[1].Trim(), out int h) || w <= 0 || h <= 0)
        {
            MessageBox.Show("Enter a size like 32x32.", "New Drawn Frame", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newFrame = new SpriteFrame { DurationMs = (int)(1000.0 / Math.Max(1, _sprite.Fps)) };
        var drawing = new SpriteDrawingWindow(_sprite, newFrame, w, h) { Owner = this };
        if (drawing.ShowDialog() != true || !drawing.Result) return; // cancelled - don't add an empty frame
        PushUndoSnapshot();
        _sprite.Frames.Add(newFrame);

        SyncLegacyFilePath();
        RebuildFrameStrip();
        _currentFrameIndex = _sprite.Frames.Count - 1;
        UpdatePreview();
        RefreshDerivedGeometryForCurrentFrame();
        HighlightSelectedThumbnail();
    }

    private void HighlightSelectedThumbnail()
    {
        for (int i = 0; i < _thumbnailBorders.Count; i++)
            _thumbnailBorders[i].BorderBrush = i == _currentFrameIndex ? Brushes.OrangeRed : Brushes.Transparent;
    }

    /// <summary>
    /// Click the volume icon -> a small popup right at the icon (not a
    /// centered modal) offering Add Sound / Remove Sound, matching how
    /// every other "linked to another editor's database" picker in this
    /// project works (Insert Sprite's own picker, the Add Command menu,
    /// etc.) - a quick menu first, then a focused dialog only for the
    /// action that actually needs one.
    /// </summary>
    private void ShowFrameSoundMenu(SpriteFrame frame, UIElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };

        var addItem = new MenuItem { Header = "Add Sound..." };
        addItem.Click += (_, _) =>
        {
            if (OpenAddSoundDialog(frame)) RebuildFrameStrip();
        };
        menu.Items.Add(addItem);

        var removeItem = new MenuItem { Header = "Remove Sound", IsEnabled = frame.SoundResourceId >= 0 };
        removeItem.Click += (_, _) =>
        {
            frame.SoundResourceId = -1;
            RebuildFrameStrip();
        };
        menu.Items.Add(removeItem);

        menu.IsOpen = true;
    }

    /// <summary>The Sprite Editor is the mothership for every sprite-driven feature in this project - per-frame sound is no exception, so this reads straight from the Sound Editor's own Resource database (Kind == Sound) exactly like every other "linked" picker does, not a hardcoded list.</summary>
    private bool OpenAddSoundDialog(SpriteFrame frame)
    {
        var sounds = _resources.Entries.Where(r => r.Kind == ResourceKind.Sound).ToList();
        if (sounds.Count == 0)
        {
            MessageBox.Show("No sounds available yet - add one in the Sound Editor first.", "Add Sound", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var window = new Window
        {
            Title = "Add Sound",
            Width = 280,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var combo = new ComboBox { ItemsSource = sounds, DisplayMemberPath = "Name", Margin = new Thickness(10, 10, 10, 6) };
        var current = sounds.FirstOrDefault(s => s.Id == frame.SoundResourceId);
        combo.SelectedItem = current ?? sounds[0];

        var okButton = new Button { Content = "OK", Width = 70, Margin = new Thickness(4), IsDefault = true };
        var cancelButton = new Button { Content = "Cancel", Width = 70, Margin = new Thickness(4), IsCancel = true };

        bool confirmed = false;
        okButton.Click += (_, _) =>
        {
            if (combo.SelectedItem is ResourceEntry chosen) frame.SoundResourceId = chosen.Id;
            confirmed = true;
            window.DialogResult = true;
        };
        cancelButton.Click += (_, _) => window.DialogResult = false;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 10, 10) };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var root = new DockPanel();
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(combo);
        window.Content = root;

        window.ShowDialog();
        return confirmed;
    }

    // ---------- Add / Delete frames ----------

    private SpriteFrame? _frameClipboard;

    /// <summary>
    /// Simple frame-list-level undo - snapshots the whole Frames list
    /// (deep-cloned) before any Add/Delete/Cut/Paste, and Ctrl+Z restores
    /// the most recent one. Scoped to frame-list operations specifically;
    /// pixel-level edits inside SpriteDrawingWindow have their own
    /// separate concern and aren't covered by this stack.
    /// </summary>
    private readonly Stack<List<SpriteFrame>> _undoStack = new();

    private static SpriteFrame CloneFrame(SpriteFrame f) => new() { FilePath = f.FilePath, DurationMs = f.DurationMs, SoundResourceId = f.SoundResourceId };

    private void PushUndoSnapshot() => _undoStack.Push(_sprite.Frames.Select(CloneFrame).ToList());

    private void Undo()
    {
        if (_undoStack.Count == 0) return;
        var previous = _undoStack.Pop();
        _sprite.Frames.Clear();
        _sprite.Frames.AddRange(previous);
        SyncLegacyFilePath();
        _currentFrameIndex = Math.Max(0, Math.Min(_currentFrameIndex, _sprite.Frames.Count - 1));
        RebuildFrameStrip();
        UpdatePreview();
        HighlightSelectedThumbnail();
    }

    private void UndoMenuItem_Click(object sender, RoutedEventArgs e) => Undo();

    /// <summary>Standard Windows shortcuts, per spec - Ctrl+C/V/X/Z alongside whatever buttons already exist for the same actions.</summary>
    private void SpriteFrameEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

        switch (e.Key)
        {
            case Key.C:
                CopyFrame_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.V:
                PasteFrame_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.X:
                CutFrame_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Z:
                Undo();
                e.Handled = true;
                break;
        }
    }

    private void CopyFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_sprite.Frames.Count == 0 || _currentFrameIndex >= _sprite.Frames.Count) return;
        var source = _sprite.Frames[_currentFrameIndex];
        _frameClipboard = CloneFrame(source);

        // Also copy the real image to the actual Windows clipboard, not just this app's own internal one - lets the same frame be pasted into (or from) any other program too.
        if (File.Exists(source.FilePath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(source.FilePath, UriKind.Absolute);
                bmp.EndInit();
                Clipboard.SetImage(bmp);
            }
            catch { /* internal clipboard already set above - system clipboard is a bonus, not required */ }
        }
    }

    private void CutFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_sprite.Frames.Count == 0 || _currentFrameIndex >= _sprite.Frames.Count) return;
        CopyFrame_Click(sender, e);
        DeleteFrame(_sprite.Frames[_currentFrameIndex]);
    }

    /// <summary>
    /// Real external-clipboard paste, per spec - an image copied from
    /// Paint, Word, a Snipping Tool, or any other Windows program now
    /// works here, not just copy/paste between frames within this same
    /// app. The real system clipboard is checked FIRST (an external image
    /// is almost always the most recently copied thing); this app's own
    /// internal frame clipboard is the fallback when the system clipboard
    /// has no image at all.
    /// </summary>
    private void PasteFrame_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsImage())
        {
            BitmapSource? clipboardImage = null;
            try { clipboardImage = Clipboard.GetImage(); } catch { /* fall through to internal clipboard below */ }

            if (clipboardImage is not null)
            {
                string dir = _sprite.Frames.Count > 0 && !string.IsNullOrEmpty(_sprite.Frames[0].FilePath)
                    ? (System.IO.Path.GetDirectoryName(_sprite.Frames[0].FilePath) ?? System.IO.Path.GetTempPath())
                    : System.IO.Path.GetTempPath();

                string newPath;
                int counter = 1;
                do { newPath = System.IO.Path.Combine(dir, $"pasted_{_sprite.Name}_{counter}.png"); counter++; }
                while (File.Exists(newPath));

                try
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(clipboardImage));
                    using var stream = File.Create(newPath);
                    encoder.Save(stream);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not save the pasted image:\n\n{ex.Message}", "Paste Frame", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PushUndoSnapshot();
                var pastedFromClipboard = new SpriteFrame { FilePath = newPath, DurationMs = 100, SoundResourceId = -1 };
                int insertIndex = Math.Min(_currentFrameIndex + 1, _sprite.Frames.Count);
                _sprite.Frames.Insert(insertIndex, pastedFromClipboard);

                SyncLegacyFilePath();
                RebuildFrameStrip();
                _currentFrameIndex = insertIndex;
                UpdatePreview();
                HighlightSelectedThumbnail();
                return;
            }
        }

        if (_frameClipboard is null) return;
        PushUndoSnapshot();

        string internalNewPath = _frameClipboard.FilePath;
        if (File.Exists(_frameClipboard.FilePath))
        {
            string internalDir = System.IO.Path.GetDirectoryName(_frameClipboard.FilePath) ?? System.IO.Path.GetTempPath();
            string baseName = System.IO.Path.GetFileNameWithoutExtension(_frameClipboard.FilePath);
            string ext = System.IO.Path.GetExtension(_frameClipboard.FilePath);
            int internalCounter = 1;
            do { internalNewPath = System.IO.Path.Combine(internalDir, $"{baseName}_copy{internalCounter}{ext}"); internalCounter++; }
            while (File.Exists(internalNewPath));

            try { File.Copy(_frameClipboard.FilePath, internalNewPath); }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy the frame image:\n\n{ex.Message}", "Paste Frame", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        var pasted = new SpriteFrame { FilePath = internalNewPath, DurationMs = _frameClipboard.DurationMs, SoundResourceId = _frameClipboard.SoundResourceId };
        int insertAt = Math.Min(_currentFrameIndex + 1, _sprite.Frames.Count);
        _sprite.Frames.Insert(insertAt, pasted);

        SyncLegacyFilePath();
        RebuildFrameStrip();
        _currentFrameIndex = insertAt;
        UpdatePreview();
        HighlightSelectedThumbnail();
    }

    private void ImportStripImage_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (openDialog.ShowDialog() != true) return;

        var converter = new ConvertToFramesWindow(openDialog.FileName) { Owner = this };
        if (converter.ShowDialog() != true || converter.Result.Count == 0) return;

        int defaultDuration = (int)(1000.0 / Math.Max(1, _sprite.Fps));
        PushUndoSnapshot();
        foreach (var frame in converter.Result)
        {
            frame.DurationMs = defaultDuration;
            _sprite.Frames.Add(frame);
        }

        SyncLegacyFilePath();
        RebuildFrameStrip();
        _currentFrameIndex = Math.Max(0, _sprite.Frames.Count - 1);
        UpdatePreview();
        HighlightSelectedThumbnail();
    }

    private void AddFramesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = true };
        if (dialog.ShowDialog() != true) return;

        int defaultDuration = (int)(1000.0 / Math.Max(1, _sprite.Fps));
        var failures = new List<string>();
        PushUndoSnapshot();
        foreach (var file in dialog.FileNames)
        {
            _sprite.Frames.Add(new SpriteFrame { FilePath = file, DurationMs = defaultDuration });

            // Verify it actually loads right away, so a bad file shows a real
            // error now instead of a silent empty/gray frame later.
            LoadBitmap(file, out string? error);
            if (error is not null) failures.Add($"{System.IO.Path.GetFileName(file)}:\n{error}");
        }

        SyncLegacyFilePath();
        RebuildFrameStrip();
        _currentFrameIndex = Math.Max(0, _sprite.Frames.Count - 1);
        UpdatePreview();
        HighlightSelectedThumbnail();

        if (failures.Count > 0)
        {
            MessageBox.Show(
                "One or more images could not be loaded and will show as empty:\n\n" + string.Join("\n\n", failures),
                "Image Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Overlay: origin marker + nine-slice guides, mapped onto the actual rendered (letterboxed) image rect ----------

    private void Expander_Toggled(object sender, RoutedEventArgs e) { }

    /// <summary>Where the image is actually drawn within its container, accounting for Stretch=Uniform letterboxing.</summary>
    private Rect GetRenderedImageRect()
    {
        if (PreviewImage.Source is not BitmapSource bmp) return Rect.Empty;
        double containerW = PreviewImage.ActualWidth, containerH = PreviewImage.ActualHeight;
        double imgW = bmp.PixelWidth, imgH = bmp.PixelHeight;
        if (containerW <= 0 || containerH <= 0 || imgW <= 0 || imgH <= 0) return Rect.Empty;

        double scale = Math.Min(containerW / imgW, containerH / imgH);
        double renderedW = imgW * scale, renderedH = imgH * scale;
        return new Rect((containerW - renderedW) / 2, (containerH - renderedH) / 2, renderedW, renderedH);
    }

    private static Point PixelToDisplay(Rect rendered, BitmapSource bmp, double px, double py) =>
        new(rendered.X + px * (rendered.Width / bmp.PixelWidth), rendered.Y + py * (rendered.Height / bmp.PixelHeight));

    private static Point DisplayToPixel(Rect rendered, BitmapSource bmp, double dx, double dy) =>
        new((dx - rendered.X) / (rendered.Width / bmp.PixelWidth), (dy - rendered.Y) / (rendered.Height / bmp.PixelHeight));

    private const double OriginMarkerRadius = 6;

    private static Polygon BuildDiamondMarker(Point center)
    {
        var diamond = new Polygon { Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
        SetDiamondPoints(diamond, center);
        return diamond;
    }

    private static void RepositionDiamond(Polygon diamond, Point center) => SetDiamondPoints(diamond, center);

    private static void SetDiamondPoints(Polygon diamond, Point center)
    {
        const double r = OriginMarkerRadius;
        diamond.Points = new PointCollection
        {
            new(center.X, center.Y - r), new(center.X + r, center.Y),
            new(center.X, center.Y + r), new(center.X - r, center.Y)
        };
    }

    private bool _draggingOrigin;

    private void RedrawOverlay()
    {
        OverlayCanvas.Children.Clear();
        if (PreviewImage.Source is not BitmapSource bmp)
        {
            OverlayDebugText.Text = "[diag] PreviewImage.Source is null - nothing to overlay";
            return;
        }
        var rendered = GetRenderedImageRect();
        OverlayDebugText.Text = $"[diag] container={PreviewImage.ActualWidth:0.#}x{PreviewImage.ActualHeight:0.#} " +
            $"img={bmp.PixelWidth}x{bmp.PixelHeight} " +
            $"rendered=X:{rendered.X:0.#} Y:{rendered.Y:0.#} W:{rendered.Width:0.#} H:{rendered.Height:0.#} " +
            $"mask={_sprite.CollisionMask.Left}/{_sprite.CollisionMask.Right}/{_sprite.CollisionMask.Top}/{_sprite.CollisionMask.Bottom}";
        if (rendered.IsEmpty) return;

        // Origin marker - a small diamond, matching the GameMaker reference.
        // Draggable: press and hold to move it, which updates Origin X/Y live
        // instead of typing numbers - more direct than the text fields alone.
        var originPoint = PixelToDisplay(rendered, bmp, _sprite.OriginX, _sprite.OriginY);
        var diamond = BuildDiamondMarker(originPoint);
        diamond.Cursor = Cursors.SizeAll;
        diamond.ToolTip = "Drag to move the origin";
        diamond.MouseLeftButtonDown += (s, args) =>
        {
            _draggingOrigin = true;
            diamond.CaptureMouse();
            args.Handled = true;
        };
        diamond.MouseMove += (s, args) =>
        {
            if (!_draggingOrigin || PreviewImage.Source is not BitmapSource currentBmp) return;
            var currentRendered = GetRenderedImageRect();
            if (currentRendered.IsEmpty) return;

            var pos = args.GetPosition(OverlayCanvas);
            var pixelPos = DisplayToPixel(currentRendered, currentBmp, pos.X, pos.Y);
            int newX = (int)Math.Round(Math.Clamp(pixelPos.X, 0, currentBmp.PixelWidth));
            int newY = (int)Math.Round(Math.Clamp(pixelPos.Y, 0, currentBmp.PixelHeight));

            _sprite.OriginX = newX;
            _sprite.OriginY = newY;
            _suppress = true;
            OriginXBox.Text = newX.ToString();
            OriginYBox.Text = newY.ToString();
            _suppress = false;

            // Reposition the existing marker in place rather than calling
            // RedrawOverlay() (which would rebuild it as a brand-new element
            // and break the active mouse capture mid-drag).
            var newDisplayPos = PixelToDisplay(currentRendered, currentBmp, newX, newY);
            RepositionDiamond(diamond, newDisplayPos);
        };
        diamond.MouseLeftButtonUp += (s, args) =>
        {
            if (!_draggingOrigin) return;
            _draggingOrigin = false;
            diamond.ReleaseMouseCapture();
            UpdateAnchorToMatchCurrentOrigin(); // may flip to "Custom" now that the drag is done
            RedrawOverlay();
        };
        OverlayCanvas.Children.Add(diamond);

        // Nine slice guides.
        if (_sprite.NineSlice.Enabled)
        {
            var slice = _sprite.NineSlice;
            AddOverlayLine(PixelToDisplay(rendered, bmp, slice.Left, 0), PixelToDisplay(rendered, bmp, slice.Left, bmp.PixelHeight), Brushes.Magenta);
            AddOverlayLine(PixelToDisplay(rendered, bmp, bmp.PixelWidth - slice.Right, 0), PixelToDisplay(rendered, bmp, bmp.PixelWidth - slice.Right, bmp.PixelHeight), Brushes.Magenta);
            AddOverlayLine(PixelToDisplay(rendered, bmp, 0, slice.Top), PixelToDisplay(rendered, bmp, bmp.PixelWidth, slice.Top), Brushes.Magenta);
            AddOverlayLine(PixelToDisplay(rendered, bmp, 0, bmp.PixelHeight - slice.Bottom), PixelToDisplay(rendered, bmp, bmp.PixelWidth, bmp.PixelHeight - slice.Bottom), Brushes.Magenta);
        }

        // Collision mask(s) - darkened excluded area (matching the GameMaker reference).
        // The shape drawn/darkened now actually follows Type (Rectangle/Ellipse/
        // Diamond/Precise) instead of always being a rectangle - that mismatch,
        // plus CollisionTypeCombo never triggering a redraw at all, was the bug.
        // Body always draws; Legs only draws when the sprite has Split enabled -
        // same drawing code, different tint so the two are easy to tell apart.
        DrawMaskOverlay(_sprite.CollisionMask, rendered, bmp, Color.FromArgb(120, 0, 0, 0), Brushes.LimeGreen,
            MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);

        if (_sprite.SplitLegsBodyMask)
        {
            DrawMaskOverlay(_sprite.LegsMask, rendered, bmp, Color.FromArgb(90, 0, 60, 160), Brushes.Cyan,
                LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);
        }
    }

    /// <summary>
    /// Draws one collision mask (Body or Legs - whichever is passed in) onto
    /// the overlay: the darkened excluded area, the dashed outline, and (in
    /// Manual mode) the four draggable corner handles that resize it. Both
    /// masks share this exact code path - only the mask instance, dim/outline
    /// colors, and which set of textboxes to sync are different.
    /// </summary>
    private void DrawMaskOverlay(CollisionMaskSettings mask, Rect rendered, BitmapSource bmp, Color dimColor, Brush outlineBrush,
        TextBox leftBox, TextBox rightBox, TextBox topBox, TextBox bottomBox)
    {
        var dim = new SolidColorBrush(dimColor);

        if (mask.Type == CollisionMaskType.Precise)
        {
            // No box, no handles - the mask is every non-transparent pixel,
            // rebuilt pixel-for-pixel from the sprite's own alpha channel.
            var preciseOverlay = BuildPreciseMaskOverlay(bmp, mask.Tolerance, dimColor);
            if (preciseOverlay is not null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = preciseOverlay,
                    Width = rendered.Width,
                    Height = rendered.Height,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(img, rendered.X);
                Canvas.SetTop(img, rendered.Y);
                OverlayCanvas.Children.Add(img);
            }
            return;
        }

        var topLeft = PixelToDisplay(rendered, bmp, mask.Left, mask.Top);
        var bottomRight = PixelToDisplay(rendered, bmp, mask.Right, mask.Bottom);
        double boxLeft = topLeft.X, boxTop = topLeft.Y;
        double boxWidth = Math.Max(0, bottomRight.X - topLeft.X), boxHeight = Math.Max(0, bottomRight.Y - topLeft.Y);
        var box = new Rect(boxLeft, boxTop, boxWidth, boxHeight);

        var shapeGeometry = BuildMaskShapeGeometry(mask.Type, box);
        var darkenPath = new System.Windows.Shapes.Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(rendered), shapeGeometry),
            Fill = dim
        };
        OverlayCanvas.Children.Add(darkenPath);

        var outlinePath = new System.Windows.Shapes.Path
        {
            Data = shapeGeometry,
            Stroke = outlineBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 }
        };
        OverlayCanvas.Children.Add(outlinePath);

        if (mask.Mode == CollisionMaskMode.Manual)
        {
            void OnBoxChanged(Rect newBox)
            {
                var newShape = BuildMaskShapeGeometry(mask.Type, newBox);
                outlinePath.Data = newShape;
                darkenPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(rendered), newShape);
            }

            AddCollisionHandle(mask, box, isLeft: true, isTop: true, OnBoxChanged, leftBox, rightBox, topBox, bottomBox);
            AddCollisionHandle(mask, box, isLeft: false, isTop: true, OnBoxChanged, leftBox, rightBox, topBox, bottomBox);
            AddCollisionHandle(mask, box, isLeft: true, isTop: false, OnBoxChanged, leftBox, rightBox, topBox, bottomBox);
            AddCollisionHandle(mask, box, isLeft: false, isTop: false, OnBoxChanged, leftBox, rightBox, topBox, bottomBox);
        }
    }

    /// <summary>Vector outline for the box-based mask types - used both as the visible dashed outline and (via CombinedGeometry.Exclude against the full image rect) as the darkened "outside the mask" area.</summary>
    private static Geometry BuildMaskShapeGeometry(CollisionMaskType type, Rect box)
    {
        switch (type)
        {
            case CollisionMaskType.Ellipse:
                return new EllipseGeometry(new Point(box.Left + box.Width / 2, box.Top + box.Height / 2), box.Width / 2, box.Height / 2);

            case CollisionMaskType.Diamond:
                var figure = new PathFigure { StartPoint = new Point(box.Left + box.Width / 2, box.Top), IsClosed = true };
                figure.Segments.Add(new LineSegment(new Point(box.Right, box.Top + box.Height / 2), true));
                figure.Segments.Add(new LineSegment(new Point(box.Left + box.Width / 2, box.Bottom), true));
                figure.Segments.Add(new LineSegment(new Point(box.Left, box.Top + box.Height / 2), true));
                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                return geometry;

            default: // Rectangle
                return new RectangleGeometry(box);
        }
    }

    /// <summary>Builds a same-size overlay bitmap for Type=Precise: every pixel at or below the alpha tolerance is painted with the dim color, every pixel above it is left fully transparent - so what's left uncovered is exactly the sprite's own non-transparent silhouette. More CPU than the box-based types since it walks every pixel on every redraw, which is exactly the trade-off the "Precise (more CPU)" label warns about.</summary>
    private static BitmapSource? BuildPreciseMaskOverlay(BitmapSource bmp, int tolerance, Color dimColor)
    {
        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight;
        if (width == 0 || height == 0) return null;
        int stride = width * 4;
        byte[] src = new byte[height * stride];
        converted.CopyPixels(src, stride, 0);

        byte[] outPixels = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                if (src[i + 3] <= tolerance)
                {
                    outPixels[i + 0] = dimColor.B;
                    outPixels[i + 1] = dimColor.G;
                    outPixels[i + 2] = dimColor.R;
                    outPixels[i + 3] = dimColor.A;
                }
            }
        }

        var wb = new WriteableBitmap(width, height, bmp.DpiX > 0 ? bmp.DpiX : 96, bmp.DpiY > 0 ? bmp.DpiY : 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, width, height), outPixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    private bool _draggingCollisionHandle;

    /// <summary>
    /// One draggable corner handle for the Manual collision box - shared by
    /// all box-based mask types (Rectangle/Ellipse/Diamond all resize the
    /// same underlying Left/Right/Top/Bottom box; only how that box is
    /// drawn differs, via onBoxChanged). During the drag this only
    /// repositions the handle itself and calls onBoxChanged to update the
    /// existing outline/darken shapes in place - calling the full
    /// RedrawOverlay() mid-drag would rebuild every overlay element
    /// (including this handle) as brand-new objects and break the active
    /// mouse capture, exactly like the origin marker bug fixed earlier. A
    /// full redraw (updating the dark strips and the other three handles
    /// too) happens once on release.
    /// </summary>
    private void AddCollisionHandle(CollisionMaskSettings mask, Rect initialBox, bool isLeft, bool isTop, Action<Rect> onBoxChanged,
        TextBox leftBox, TextBox rightBox, TextBox topBox, TextBox bottomBox)
    {
        const double size = 8;
        double displayX = isLeft ? initialBox.Left : initialBox.Right;
        double displayY = isTop ? initialBox.Top : initialBox.Bottom;

        var handle = new System.Windows.Shapes.Rectangle
        {
            Width = size,
            Height = size,
            Fill = Brushes.LimeGreen,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            Cursor = Cursors.SizeNWSE
        };
        Canvas.SetLeft(handle, displayX - size / 2);
        Canvas.SetTop(handle, displayY - size / 2);

        handle.MouseLeftButtonDown += (_, args) =>
        {
            _draggingCollisionHandle = true;
            handle.CaptureMouse();
            args.Handled = true;
        };
        handle.MouseMove += (_, args) =>
        {
            if (!_draggingCollisionHandle || PreviewImage.Source is not BitmapSource currentBmp) return;
            var rendered = GetRenderedImageRect();
            if (rendered.IsEmpty) return;

            var pos = args.GetPosition(OverlayCanvas);
            var pixelPos = DisplayToPixel(rendered, currentBmp, pos.X, pos.Y);
            int px = (int)Math.Round(Math.Clamp(pixelPos.X, 0, currentBmp.PixelWidth));
            int py = (int)Math.Round(Math.Clamp(pixelPos.Y, 0, currentBmp.PixelHeight));

            if (isLeft) mask.Left = Math.Min(px, mask.Right);
            else mask.Right = Math.Max(px, mask.Left);
            if (isTop) mask.Top = Math.Min(py, mask.Bottom);
            else mask.Bottom = Math.Max(py, mask.Top);

            _suppress = true;
            leftBox.Text = mask.Left.ToString();
            rightBox.Text = mask.Right.ToString();
            topBox.Text = mask.Top.ToString();
            bottomBox.Text = mask.Bottom.ToString();
            _suppress = false;

            // In-place update only - the new corner position, the shared shape (rectangle/ellipse/diamond outline + darken fill), and this handle.
            var newTopLeft = PixelToDisplay(rendered, currentBmp, mask.Left, mask.Top);
            var newBottomRight = PixelToDisplay(rendered, currentBmp, mask.Right, mask.Bottom);
            var newBox = new Rect(newTopLeft, newBottomRight);
            onBoxChanged(newBox);
            Canvas.SetLeft(handle, (isLeft ? newTopLeft.X : newBottomRight.X) - size / 2);
            Canvas.SetTop(handle, (isTop ? newTopLeft.Y : newBottomRight.Y) - size / 2);
        };
        handle.MouseLeftButtonUp += (_, _) =>
        {
            _draggingCollisionHandle = false;
            handle.ReleaseMouseCapture();
            RedrawOverlay(); // now safe - full rebuild once the drag has ended
        };

        OverlayCanvas.Children.Add(handle);
    }

    private void AddOverlayLine(Point a, Point b, Brush brush)
    {
        OverlayCanvas.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = brush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 2 } });
    }

    // ---------- Collision Mask ----------

    private static void UpdateMaskBoxEnabledState(CollisionMaskSettings mask, ComboBox modeCombo, TextBox leftBox, TextBox rightBox, TextBox topBox, TextBox bottomBox)
    {
        // Precise has no Left/Right/Top/Bottom box at all, and Mode
        // (Automatic vs Manual) doesn't apply to it either - it's derived
        // fresh from the sprite's own alpha channel every redraw, always.
        // Leaving Mode selectable while Precise is picked was the bug:
        // it looked like switching to Manual should let you drag
        // something, but there was never anything for it to control.
        bool isPrecise = mask.Type == CollisionMaskType.Precise;
        modeCombo.IsEnabled = !isPrecise;
        bool manual = mask.Mode == CollisionMaskMode.Manual && !isPrecise;
        leftBox.IsEnabled = manual;
        rightBox.IsEnabled = manual;
        topBox.IsEnabled = manual;
        bottomBox.IsEnabled = manual;
    }

    private void UpdateCollisionBoxEnabledState() =>
        UpdateMaskBoxEnabledState(_sprite.CollisionMask, CollisionModeCombo, MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);

    private void UpdateLegsCollisionBoxEnabledState() =>
        UpdateMaskBoxEnabledState(_sprite.LegsMask, LegsCollisionModeCombo, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);

    private static string GetCollisionTypeDescription(CollisionMaskType type) => type switch
    {
        CollisionMaskType.Rectangle => "An axis-aligned box - cheapest, good default for most sprites.",
        CollisionMaskType.Ellipse => "An oval inscribed in the box - fits round objects more tightly.",
        CollisionMaskType.Diamond => "A rotated square inscribed in the box - fits top-down diagonal shapes.",
        CollisionMaskType.Precise => "Precise (more CPU): covers all the non-transparent part of the sprite. Mode and the box below don't apply - there's nothing to drag, it's always exactly the sprite's own pixels.",
        _ => string.Empty
    };


    // ---- Body mask (the original single mask - always present) ----

    private void CollisionModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CollisionModeCombo.SelectedItem is not CollisionMaskMode mode) return;
        _sprite.CollisionMask.Mode = mode;
        UpdateCollisionBoxEnabledState();

        if (mode == CollisionMaskMode.Automatic && _sprite.CollisionMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.CollisionMask, MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);
        RedrawOverlay();
    }

    private void CollisionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CollisionTypeCombo.SelectedItem is not CollisionMaskType type) return;
        _sprite.CollisionMask.Type = type;
        CollisionTypeDescText.Text = GetCollisionTypeDescription(type);
        UpdateCollisionBoxEnabledState();

        // This call was missing entirely before - selecting a new Type
        // updated the stored value but the preview never redrew, so the
        // mask always kept rendering as whatever shape happened to be on
        // screen already (always a plain rectangle, since that was the
        // only shape RedrawOverlay knew how to draw).
        if (type != CollisionMaskType.Precise && _sprite.CollisionMask.Mode == CollisionMaskMode.Automatic)
            RecomputeAutomaticBounds(_sprite.CollisionMask, MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);
        RedrawOverlay();
    }

    private void ToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        _sprite.CollisionMask.Tolerance = (int)ToleranceSlider.Value;
        ToleranceValueText.Text = _sprite.CollisionMask.Tolerance.ToString();
        if (_sprite.CollisionMask.Mode == CollisionMaskMode.Automatic && _sprite.CollisionMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.CollisionMask, MaskLeftBox, MaskRightBox, MaskTopBox, MaskBottomBox);
        RedrawOverlay();
    }

    private void MaskBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (int.TryParse(MaskLeftBox.Text, out int l)) _sprite.CollisionMask.Left = l;
        if (int.TryParse(MaskRightBox.Text, out int r)) _sprite.CollisionMask.Right = r;
        if (int.TryParse(MaskTopBox.Text, out int t)) _sprite.CollisionMask.Top = t;
        if (int.TryParse(MaskBottomBox.Text, out int b)) _sprite.CollisionMask.Bottom = b;
        RedrawOverlay();
    }

    // ---- Legs mask (movement-only blocking; only visible/used when SplitLegsBodyMask is on) ----

    private void LegsCollisionModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || LegsCollisionModeCombo.SelectedItem is not CollisionMaskMode mode) return;
        _sprite.LegsMask.Mode = mode;
        UpdateLegsCollisionBoxEnabledState();

        if (mode == CollisionMaskMode.Automatic && _sprite.LegsMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.LegsMask, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);
        RedrawOverlay();
    }

    private void LegsCollisionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || LegsCollisionTypeCombo.SelectedItem is not CollisionMaskType type) return;
        _sprite.LegsMask.Type = type;
        LegsCollisionTypeDescText.Text = GetCollisionTypeDescription(type);
        UpdateLegsCollisionBoxEnabledState();

        if (type != CollisionMaskType.Precise && _sprite.LegsMask.Mode == CollisionMaskMode.Automatic)
            RecomputeAutomaticBounds(_sprite.LegsMask, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);
        RedrawOverlay();
    }

    private void LegsToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        _sprite.LegsMask.Tolerance = (int)LegsToleranceSlider.Value;
        LegsToleranceValueText.Text = _sprite.LegsMask.Tolerance.ToString();
        if (_sprite.LegsMask.Mode == CollisionMaskMode.Automatic && _sprite.LegsMask.Type != CollisionMaskType.Precise)
            RecomputeAutomaticBounds(_sprite.LegsMask, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);
        RedrawOverlay();
    }

    private void LegsMaskBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (int.TryParse(LegsMaskLeftBox.Text, out int l)) _sprite.LegsMask.Left = l;
        if (int.TryParse(LegsMaskRightBox.Text, out int r)) _sprite.LegsMask.Right = r;
        if (int.TryParse(LegsMaskTopBox.Text, out int t)) _sprite.LegsMask.Top = t;
        if (int.TryParse(LegsMaskBottomBox.Text, out int b)) _sprite.LegsMask.Bottom = b;
        RedrawOverlay();
    }

    // ---- Split toggle ----

    private void SplitLegsBodyCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _sprite.SplitLegsBodyMask = SplitLegsBodyCheck.IsChecked == true;

        if (_sprite.SplitLegsBodyMask && _sprite.LegsMask.Right == 0 && _sprite.LegsMask.Bottom == 0)
        {
            // First time Split is turned on for this sprite - the Legs mask
            // has never been touched, so seed it the same way Body starts
            // out (Automatic, full canvas) rather than showing a zero-size
            // box. The user then shrinks it down to the feet region.
            RecomputeAutomaticBounds(_sprite.LegsMask, LegsMaskLeftBox, LegsMaskRightBox, LegsMaskTopBox, LegsMaskBottomBox);
        }

        UpdateSplitLegsBodyVisibility();
        RedrawOverlay();
    }

    private void UpdateSplitLegsBodyVisibility()
    {
        bool split = _sprite.SplitLegsBodyMask;
        LegsMaskPanel.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        BodyMaskLabel.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Left/Right/Top/Bottom here are the ABSOLUTE pixel coordinates where the
    /// collision box starts/ends - distinct from Nine Slice below, whose
    /// same-named fields are margin DISTANCES inward from each edge.
    /// Computed from the tightest box around non-transparent pixels, since
    /// that's what "Automatic" means in GameMaker - but falls back to the
    /// full canvas when the frame has no non-transparent pixels at all (a
    /// brand-new, not-yet-drawn sprite), instead of leaving the box
    /// collapsed at its zero-size default. Shared by both Body and Legs.
    /// </summary>
    private void RecomputeAutomaticBounds(CollisionMaskSettings mask, TextBox leftBox, TextBox rightBox, TextBox topBox, TextBox bottomBox)
    {
        if (PreviewImage.Source is not BitmapSource bmp) return;
        var bounds = ComputeAlphaBoundingBox(bmp, mask.Tolerance);
        var (left, right, top, bottom) = bounds ?? (0, bmp.PixelWidth, 0, bmp.PixelHeight);

        mask.Left = left;
        mask.Right = right;
        mask.Top = top;
        mask.Bottom = bottom;

        _suppress = true;
        leftBox.Text = left.ToString();
        rightBox.Text = right.ToString();
        topBox.Text = top.ToString();
        bottomBox.Text = bottom.ToString();
        _suppress = false;
    }

    private static (int Left, int Right, int Top, int Bottom)? ComputeAlphaBoundingBox(BitmapSource bmp, int tolerance)
    {
        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight;
        if (width == 0 || height == 0) return null;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        int minX = width, maxX = -1, minY = height, maxY = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte alpha = pixels[y * stride + x * 4 + 3];
                if (alpha > tolerance)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }
        // maxX/maxY above are the last INCLUSIVE pixel with content, but
        // Right/Bottom are used everywhere else in this file as EXCLUSIVE
        // bounds - width = Right - Left, and dragging a handle lets Right
        // go all the way up to PixelWidth itself (the canvas's true edge),
        // not "the last pixel index". Returning maxX/maxY directly made the
        // computed box exactly one pixel short on the right and bottom
        // every time, letting the true last row/column of art poke out
        // past the mask. +1 converts to the same exclusive convention the
        // rest of the box already uses.
        return maxX < 0 ? null : (minX, maxX + 1, minY, maxY + 1);
    }

    // ---------- Nine Slice ----------

    private void NineSliceActiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _sprite.NineSlice.Enabled = NineSliceActiveCheck.IsChecked == true;
        RedrawOverlay();
    }

    private void SliceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (int.TryParse(SliceLeftBox.Text, out int l)) _sprite.NineSlice.Left = l;
        if (int.TryParse(SliceRightBox.Text, out int r)) _sprite.NineSlice.Right = r;
        if (int.TryParse(SliceTopBox.Text, out int t)) _sprite.NineSlice.Top = t;
        if (int.TryParse(SliceBottomBox.Text, out int b)) _sprite.NineSlice.Bottom = b;
        RedrawOverlay();
    }
}
