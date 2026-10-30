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
    /// <summary>
    /// True for the two locked font sprites (Fonts > InGameFont / DialogFont):
    /// their frames are glyphs, never played - so no per-frame sound, no
    /// playback, no FPS/Loop, the name can't change, and the floating Font
    /// Guide shows which character each frame position is.
    /// </summary>
    private readonly bool _isFontSprite;
    private FontGuideWindow? _fontGuide;
    /// <summary>A blood type (a sprite in the Blood group): frames are puddle sizes, small to large, at most SpriteDatabase.BloodSizes - no sound, no animation - with the Blood Guide alongside.</summary>
    private readonly bool _isBloodSprite;
    private BloodGuideWindow? _bloodGuide;
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

    public SpriteFrameEditorWindow(SpriteEntry sprite, ResourceDatabase resources, bool isTilesetGroup = false, bool isFontSprite = false, bool isBloodSprite = false, bool isKeychainSprite = false, bool isWeaponInHandSprite = false)
    {
        InitializeComponent();
        _sprite = sprite;
        Loaded += (_, _) => TakeIntegratedSnapshot(); // integrated already: as it is once open (after any setting-up of its frames), to compare when it closes (SpriteFrameEditorWindow.Integrated.cs)
        _resources = resources;
        _isTilesetGroup = isTilesetGroup;
        _isFontSprite = isFontSprite;
        _isBloodSprite = isBloodSprite;
        PreviewKeyDown += SpriteFrameEditorWindow_PreviewKeyDown;

        OriginAnchorCombo.ItemsSource = AnchorOptions;

        _suppress = true;
        NameBox.Text = _sprite.Name;
        FpsBox.Text = _sprite.Fps.ToString();
        LoopCheck.IsChecked = _sprite.Loop;
        OriginXBox.Text = _sprite.OriginX.ToString();
        OriginYBox.Text = _sprite.OriginY.ToString();
        OriginAnchorCombo.SelectedItem = AnchorOptions.Contains(_sprite.OriginAnchor) ? _sprite.OriginAnchor : "Custom";

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

        if (_isFontSprite) SetUpFontMode();
        if (_isBloodSprite) SetUpBloodMode();
        if (_isTilesetGroup) SetUpStatesMode(FrameStatesGuideWindow.Tileset, 0); // a tile a frame: no animation, no sound
        if (isKeychainSprite) SetUpStatesMode(FrameStatesGuideWindow.Keychain, SpriteDatabase.KeychainFrames); // frame by how many keys
        if (isWeaponInHandSprite) SetUpWeaponInHandMode(); // a weapon held in the hand: its frames, each with a grip point (SpriteFrameEditorWindow.WeaponHand.cs)
        else if (!isTilesetGroup && !isFontSprite && !isBloodSprite && !isKeychainSprite) SetUpHandPoints(); // a character's frames: where the weapon hand is
    }

    /// <summary>A blood type: puddle-size frames only - nothing that animates or makes sound - and the Blood Guide alongside.</summary>
    private void SetUpBloodMode()
    {
        foreach (var frame in _sprite.Frames) frame.SoundResourceId = -1; // never played, so never heard
        _sprite.Loop = false;
        foreach (UIElement control in new UIElement[] { FpsBox, LoopCheck, PlayPauseButton })
        {
            control.IsEnabled = false;
            ((FrameworkElement)control).ToolTip = "Blood doesn't animate - each frame is one puddle size";
        }
        Title = $"Blood: {_sprite.Name}";
        TrimBloodFrames();

        var guideButton = new Button { Content = "Blood Guide...", Margin = new Thickness(0, 0, 6, 0), ToolTip = "What each frame is, and how bleeding works" };
        guideButton.Click += (_, _) => ShowBloodGuide();
        if (AddFramesButton.Parent is Panel panel) panel.Children.Add(guideButton);

        Loaded += (_, _) => ShowBloodGuide();
    }

    /// <summary>Exult's blood has four sizes: frames past the last size are removed (with a word why).</summary>
    private void TrimBloodFrames()
    {
        if (!_isBloodSprite || _sprite.Frames.Count <= SpriteDatabase.BloodSizes) return;
        int extra = _sprite.Frames.Count - SpriteDatabase.BloodSizes;
        _sprite.Frames.RemoveRange(SpriteDatabase.BloodSizes, extra);
        foreach (var frame in _sprite.Frames) frame.SoundResourceId = -1;
        if (_currentFrameIndex >= _sprite.Frames.Count) _currentFrameIndex = _sprite.Frames.Count - 1;
        MessageBox.Show($"Blood has {SpriteDatabase.BloodSizes} sizes (frames 1-{SpriteDatabase.BloodSizes}, small to large), as in Exult - the {extra} extra frame{(extra == 1 ? " was" : "s were")} removed.",
            "Blood", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowBloodGuide()
    {
        if (_bloodGuide is { IsLoaded: true })
        {
            _bloodGuide.Activate();
            return;
        }
        _bloodGuide = new BloodGuideWindow(_sprite) { Owner = this };
        _bloodGuide.FrameChosen += index =>
        {
            if (index < 0 || index >= _sprite.Frames.Count) return;
            _currentFrameIndex = index;
            UpdatePreview();
            HighlightSelectedThumbnail();
        };
        _bloodGuide.Closed += (_, _) => _bloodGuide = null;
        _bloodGuide.Left = Math.Max(0, Left + ActualWidth - 40);
        _bloodGuide.Top = Math.Max(0, Top + 40);
        _bloodGuide.Show();
        _bloodGuide.Refresh(_currentFrameIndex);
    }

    /// <summary>A font sprite: glyph frames only - nothing that animates or makes sound, a fixed name, and the Font Guide alongside.</summary>
    private void SetUpFontMode()
    {
        foreach (var frame in _sprite.Frames) frame.SoundResourceId = -1; // never played, so never heard
        _sprite.Loop = false;
        if (SpriteDatabase.IsFontSprite(SpriteDatabase.FontsGroupName, _sprite.Name))
        {
            NameBox.IsReadOnly = true;
            NameBox.ToolTip = "The game's two fonts have fixed names";
        }
        foreach (UIElement control in new UIElement[] { FpsBox, LoopCheck, PlayPauseButton })
        {
            control.IsEnabled = false;
            ((FrameworkElement)control).ToolTip = "Fonts don't animate - each frame is one letter";
        }
        Title = $"Font: {_sprite.Name}";

        var guideButton = new Button { Content = "Font Guide...", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Show which character each frame position is" };
        guideButton.Click += (_, _) => ShowFontGuide();
        if (AddFramesButton.Parent is Panel panel) panel.Children.Add(guideButton);

        Loaded += (_, _) => ShowFontGuide();
    }

    private void ShowFontGuide()
    {
        if (_fontGuide is { IsLoaded: true })
        {
            _fontGuide.Activate();
            return;
        }
        _fontGuide = new FontGuideWindow(_sprite) { Owner = this };
        _fontGuide.FrameChosen += index =>
        {
            if (index < 0 || index >= _sprite.Frames.Count) return;
            _currentFrameIndex = index;
            UpdatePreview();
            HighlightSelectedThumbnail();
        };
        _fontGuide.Closed += (_, _) => _fontGuide = null;
        // Beside this window rather than over it, so both stay usable.
        _fontGuide.Left = Math.Max(0, Left + ActualWidth - 40);
        _fontGuide.Top = Math.Max(0, Top + 40);
        _fontGuide.Show();
        _fontGuide.Refresh(_currentFrameIndex);
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
        if (_isFontSprite) return;
        _isPlaying = !_isPlaying;
        PlayPauseButton.Content = _isPlaying ? "\u275A\u275A" : "\u25B6";
        if (_isPlaying) RestartPlaybackTimer(); else _playbackTimer.Stop();
    }

    private void AdvanceFrame()
    {
        if (_sprite.Frames.Count == 0 || _isFontSprite) return;
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
    /// force it explicitly here. Every origin/nine-slice coordinate is
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
        TrimBloodFrames(); // every way of adding frames ends here
        TrimStatesFrames();
        if (IsStatesSprite || _isBloodSprite) foreach (var frame in _sprite.Frames) frame.SoundResourceId = -1;
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
        if (!_isTilesetGroup && !_isFontSprite && !_isBloodSprite && !IsStatesSprite)
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
            Text = (index + 1).ToString() + (_isFontSprite ? FontCharLabel(index) : ""),
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
    /// An anchored Origin (everything except "Custom") is computed FROM
    /// the frame's pixel size at the moment you set it - not kept as a
    /// live formula. If the art later changes shape (canvas resized, a
    /// bigger layer added, redrawn taller, etc.) the stored value goes
    /// stale: Top Right origin stays wherever the old width put it.
    /// Re-derive it here, right after any edit that could have changed
    /// the current frame's size, so it tracks the art instead of
    /// freezing at whatever it looked like when it was last touched.
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
        _fontGuide?.Refresh(_currentFrameIndex);
        _bloodGuide?.Refresh(_currentFrameIndex);
        _statesGuide?.Refresh(_currentFrameIndex);
    }

    /// <summary>The character a font frame draws, for its thumbnail label.</summary>
    private static string FontCharLabel(int index) => SpriteDatabase.FontCharForFrame(index) switch
    {
        ' ' => "  spc",
        char ch => $"  {ch}",
        null => "  (unused)"
    };

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

        var pasted = new SpriteFrame { FilePath = internalNewPath, DurationMs = _frameClipboard.DurationMs, SoundResourceId = _isFontSprite ? -1 : _frameClipboard.SoundResourceId };
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

        // A font sheet: 95 characters, 18 to a row, from the space - the cell size follows from the image.
        var converter = new ConvertToFramesWindow(openDialog.FileName,
            _isFontSprite ? (SpriteDatabase.FontGlyphCount, SpriteDatabase.FontSheetColumns) : null) { Owner = this };
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
        if (PreviewImage.Source is not BitmapSource bmp) return;
        var rendered = GetRenderedImageRect();
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
        DrawWeaponMarkers(rendered, bmp); // a weapon's grip, a character frame's hand (SpriteFrameEditorWindow.WeaponHand.cs)
    }

    private void AddOverlayLine(Point a, Point b, Brush brush)
    {
        OverlayCanvas.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = brush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 2 } });
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
