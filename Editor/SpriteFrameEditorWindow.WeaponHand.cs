using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A weapon held in the hand (Exult's Actor::paint_weapon), in the frame editor:
///  - A sprite in the Weapon in hand group: its frames are states named by System Settings > Weapon in hand (Exult's
///    held + three swing frames, or smooth: each direction's held and swing frames); click the picture to set this
///    frame's grip point - where the hand holds it (a red cross).
///  - Any other sprite (a character's pose): each frame's hand point - click "Set hand point", then the picture (a yellow
///    ring); "No weapon here" for a frame without one (lying down); which weapon frame it shows (Automatic: held, or the
///    swing in an attack pose); drawn behind the body (facing away).
/// The game draws the equipped weapon's frame with its grip on the hand.
/// </summary>
public partial class SpriteFrameEditorWindow
{
    private bool _isWeaponInHandSprite;
    private bool _placingHand;
    private ComboBox? _weaponFrameCombo;
    private CheckBox? _weaponBehindCheck;
    private TextBlock? _handText;
    private bool _weaponUiSuppress;
    private WeaponPreviewWindow? _weaponPreview;

    private static WeaponInHandSettings WeaponSettings => ProjectSession.SettingsProvider?.Invoke()?.WeaponInHand ?? new WeaponInHandSettings();

    /// <summary>A weapon-in-hand sprite: its frames as the setting says (states), each with its grip point.</summary>
    private void SetUpWeaponInHandMode()
    {
        _isWeaponInHandSprite = true;
        var settings = WeaponSettings;
        SetUpStatesMode(FrameStatesGuideWindow.WeaponInHand(settings), settings.FrameCount);
        Title = $"Weapon in hand: {_sprite.Name}";
        if (AddFramesButton.Parent is Panel panel)
            panel.Children.Add(new TextBlock { Text = "Click the picture: this frame's grip point (where the hand holds it).", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Foreground = Brushes.DarkRed });
        OverlayCanvas.Background = Brushes.Transparent;
        OverlayCanvas.MouseLeftButtonDown += (_, e) =>
        {
            if (!TryPixelUnderMouse(e, out int x, out int y)) return;
            var frame = _sprite.Frames[_currentFrameIndex];
            (frame.GripX, frame.GripY) = (x, y);
            RedrawOverlay();
            e.Handled = true;
        };
    }

    /// <summary>Any other sprite: a hand point per frame, for a weapon held in it.</summary>
    private void SetUpHandPoints()
    {
        if (AddFramesButton.Parent is not Panel panel) return;
        var settings = WeaponSettings;
        var row = new WrapPanel { Margin = new Thickness(6, 0, 0, 0) };
        var place = new System.Windows.Controls.Primitives.ToggleButton { Content = "Set hand point", ToolTip = "Then click the picture: where this frame's weapon hand is - the weapon held is drawn with its grip there", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 0, 6, 0) };
        _handToggle = place;
        place.Checked += (_, _) => { _placingHand = true; if (_standToggle is { IsChecked: true } stand) stand.IsChecked = false; OverlayCanvas.Background = Brushes.Transparent; OverlayCanvas.Cursor = Cursors.Cross; };
        place.Unchecked += (_, _) => { _placingHand = false; OverlayCanvas.Background = null; OverlayCanvas.Cursor = null; };
        row.Children.Add(place);
        var none = new Button { Content = "No weapon here", ToolTip = "This frame shows no weapon (lying down, asleep) - Exult's 255", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 0, 6, 0) };
        none.Click += (_, _) => { if (CurrentFrame() is { } f) { (f.HandX, f.HandY) = (SpriteFrame.NoHand, SpriteFrame.NoHand); RedrawOverlay(); } };
        row.Children.Add(none);
        var preview = new Button { Content = "Preview weapon...", ToolTip = "A window showing a weapon in this frame's hand, as the game draws it - it follows the frame you're on. Drag the weapon there to move this frame's hand point", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 0, 6, 0) };
        preview.Click += (_, _) => ShowWeaponPreview();
        row.Children.Add(preview);
        var choices = new List<string> { "Automatic" };
        choices.AddRange(SpriteDatabase.WeaponInHandFrameNames(settings.Smooth, settings.SwingFrames));
        _weaponFrameCombo = new ComboBox { ItemsSource = choices, Width = 200, ToolTip = "Which frame of the weapon in hand this frame shows. Automatic: held - or, in an attack pose, the swing frame for this frame of it", Margin = new Thickness(0, 0, 4, 0) };
        _weaponFrameCombo.SelectionChanged += (_, _) => { if (!_weaponUiSuppress && CurrentFrame() is { } f) { f.WeaponFrame = _weaponFrameCombo.SelectedIndex - 1; _weaponPreview?.ShowFrame(_currentFrameIndex); } };
        row.Children.Add(_weaponFrameCombo);
        _weaponBehindCheck = new CheckBox { Content = "Behind the body", VerticalAlignment = VerticalAlignment.Center, ToolTip = "The weapon drawn behind the body in this frame (facing away) - else over it, as Exult", Margin = new Thickness(0, 0, 6, 0) };
        _weaponBehindCheck.Checked += (_, _) => { if (!_weaponUiSuppress && CurrentFrame() is { } f) { f.WeaponBehind = true; _weaponPreview?.ShowFrame(_currentFrameIndex); } };
        _weaponBehindCheck.Unchecked += (_, _) => { if (!_weaponUiSuppress && CurrentFrame() is { } f) { f.WeaponBehind = false; _weaponPreview?.ShowFrame(_currentFrameIndex); } };
        row.Children.Add(_weaponBehindCheck);
        _handText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
        row.Children.Add(_handText);
        panel.Children.Add(row);
        if (ProjectSession.IsUltima8) SetUpStandPoints(panel); // Ultima 8 projects: each frame's stand point (SpriteFrameEditorWindow.StandPoint.cs)
        OverlayCanvas.MouseLeftButtonDown += (_, e) =>
        {
            if (!_placingHand || !TryPixelUnderMouse(e, out int x, out int y)) return;
            var frame = _sprite.Frames[_currentFrameIndex];
            (frame.HandX, frame.HandY) = (x, y);
            RedrawOverlay();
            e.Handled = true;
        };
    }

    /// <summary>The weapon preview window (WeaponPreviewWindow): opened once, it follows this editor's frame.</summary>
    private void ShowWeaponPreview()
    {
        if (_weaponPreview is not null) { _weaponPreview.Activate(); return; }
        _weaponPreview = new WeaponPreviewWindow(_sprite) { Owner = this };
        _weaponPreview.Closed += (_, _) => _weaponPreview = null;
        _weaponPreview.HandMoved += RedrawOverlay; // dragged there in the preview: the ring follows
        _weaponPreview.ChoiceChanged += RedrawOverlay; // another weapon, facing or attacking: the hand point checked again
        _weaponPreview.Show();
        _weaponPreview.ShowFrame(_currentFrameIndex);
    }

    private SpriteFrame? CurrentFrame() => _currentFrameIndex >= 0 && _currentFrameIndex < _sprite.Frames.Count ? _sprite.Frames[_currentFrameIndex] : null;

    private bool TryPixelUnderMouse(MouseButtonEventArgs e, out int x, out int y)
    {
        x = y = 0;
        if (PreviewImage.Source is not BitmapSource bmp || CurrentFrame() is null) return false;
        var rendered = GetRenderedImageRect();
        if (rendered.IsEmpty) return false;
        var p = DisplayToPixel(rendered, bmp, e.GetPosition(OverlayCanvas).X, e.GetPosition(OverlayCanvas).Y);
        if (p.X < 0 || p.Y < 0 || p.X > bmp.PixelWidth || p.Y > bmp.PixelHeight) return false;
        (x, y) = ((int)Math.Round(p.X), (int)Math.Round(p.Y));
        return true;
    }

    /// <summary>The overlay's weapon marks for this frame: a weapon's grip (red cross), or a character frame's hand (yellow ring).</summary>
    private void DrawWeaponMarkers(Rect rendered, BitmapSource bmp)
    {
        if (CurrentFrame() is not { } frame) return;
        if (_isWeaponInHandSprite)
        {
            double gx = frame.GripX >= 0 ? frame.GripX : bmp.PixelWidth / 2.0, gy = frame.GripY >= 0 ? frame.GripY : bmp.PixelHeight / 2.0;
            var g = PixelToDisplay(rendered, bmp, gx, gy);
            OverlayCanvas.Children.Add(new Line { X1 = g.X - 7, Y1 = g.Y, X2 = g.X + 7, Y2 = g.Y, Stroke = Brushes.Red, StrokeThickness = 2, IsHitTestVisible = false });
            OverlayCanvas.Children.Add(new Line { X1 = g.X, Y1 = g.Y - 7, X2 = g.X, Y2 = g.Y + 7, Stroke = Brushes.Red, StrokeThickness = 2, IsHitTestVisible = false });
            return;
        }
        if (_handText is null) return;
        _weaponPreview?.ShowFrame(_currentFrameIndex); // the preview follows the frame and its hand point
        _weaponUiSuppress = true;
        if (_weaponFrameCombo is not null) _weaponFrameCombo.SelectedIndex = Math.Clamp(frame.WeaponFrame + 1, 0, _weaponFrameCombo.Items.Count - 1);
        if (_weaponBehindCheck is not null) _weaponBehindCheck.IsChecked = frame.WeaponBehind;
        _weaponUiSuppress = false;
        if (!frame.HasHand) { _handText.Text = "No weapon in this frame."; return; }
        _handText.Text = $"Hand at {frame.HandX}, {frame.HandY}.";
        var h = PixelToDisplay(rendered, bmp, frame.HandX, frame.HandY);
        var ring = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.Gold, StrokeThickness = 2, IsHitTestVisible = false };
        // The weapon (the preview's, else the last previewed) reaching out of the frame: the ring pulses red.
        var (weapon, facing, attacking) = _weaponPreview?.Choice ?? WeaponPreviewWindow.DefaultChoice(_sprite);
        if (weapon is not null && WeaponInHandLook.WeaponBounds(WeaponSettings, _sprite, _currentFrameIndex, weapon, facing, attacking) is { } reach
            && !new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight).Contains(reach))
        {
            ring.Stroke = Brushes.Red;
            ring.Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0, 0));
            ring.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.2, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            _handText.Text += $" {weapon.Name} reaches out of the frame.";
        }
        Canvas.SetLeft(ring, h.X - 6);
        Canvas.SetTop(ring, h.Y - 6);
        OverlayCanvas.Children.Add(ring);
    }
}
