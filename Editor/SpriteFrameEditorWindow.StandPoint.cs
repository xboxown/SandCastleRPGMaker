using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8 projects only: each frame's stand point (SpriteFrame.AnchorX / AnchorY; StandPoints) - where the frame stands
/// on the character's spot, so a walk made of frames cut to what's drawn in each doesn't sway. Set stand point (click the
/// picture), Bottom-middle (back to none), Auto-align (every frame, keeping the head still), Preview (the frame over the one
/// before it - drag it into place), How to (the steps). The mark: a cyan cross; a dashed one where a frame stands without one.
/// </summary>
public partial class SpriteFrameEditorWindow
{
    private ToggleButton? _handToggle, _standToggle;
    private bool _placingStand;
    private TextBlock? _standText;
    private StandPointPreviewWindow? _standPreview;
    private CheckBox? _holdStillCheck;
    private bool _holdStillSuppress;

    private void SetUpStandPoints(Panel panel)
    {
        var row = new WrapPanel { Margin = new Thickness(6, 2, 0, 0) };
        row.Children.Add(new TextBlock { Text = "Ultima 8 - stand point:", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 6, 0) });
        var place = new ToggleButton { Content = "Set stand point", ToolTip = "Then click the picture: the point on the feet this frame stands on - the game keeps it on the character's spot", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 0, 6, 0) };
        _standToggle = place;
        place.Checked += (_, _) => { _placingStand = true; if (_handToggle is { IsChecked: true } hand) hand.IsChecked = false; OverlayCanvas.Background = Brushes.Transparent; OverlayCanvas.Cursor = Cursors.Cross; };
        place.Unchecked += (_, _) => { _placingStand = false; if (!_placingHand) { OverlayCanvas.Background = null; OverlayCanvas.Cursor = null; } };
        row.Children.Add(place);
        row.Children.Add(StandButton("Bottom-middle", "This frame stands on its bottom-middle again (no stand point)", () =>
        {
            if (CurrentFrame() is { } f) { (f.AnchorX, f.AnchorY) = (-1, -1); RedrawOverlay(); }
        }));
        row.Children.Add(StandButton("Auto-align all frames", "Every frame's stand point from the pictures: each stands so the head keeps still. Fine-tune any frame after (Preview)", () =>
        {
            if (!StandPoints.AutoAlign(_sprite)) { MessageBox.Show(this, "These frames have no pictures to read.", "Auto-align", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            RedrawOverlay();
        }));
        row.Children.Add(StandButton("Preview...", "This frame over the one before it, standing on their stand points - drag it (or the arrow keys) until they line up; Play shows the walk", ShowStandPreview));
        row.Children.Add(StandButton("How to?", "The steps", ShowStandHowTo));
        _holdStillCheck = new CheckBox { Content = "Holds still (a step doesn't move in it)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0),
            ToolTip = "A step's frame with both feet planted: the Avatar doesn't move in it. None ticked: the first frame holds still" };
        _holdStillCheck.Checked += (_, _) => { if (!_holdStillSuppress && CurrentFrame() is { } f) f.HoldsStill = true; };
        _holdStillCheck.Unchecked += (_, _) => { if (!_holdStillSuppress && CurrentFrame() is { } f) f.HoldsStill = false; };
        row.Children.Add(_holdStillCheck);
        _standText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75, Margin = new Thickness(4, 0, 0, 0) };
        row.Children.Add(_standText);
        panel.Children.Add(row);

        OverlayCanvas.MouseLeftButtonDown += (_, e) =>
        {
            if (!_placingStand || !TryPixelUnderMouse(e, out int x, out int y)) return;
            var frame = _sprite.Frames[_currentFrameIndex];
            (frame.AnchorX, frame.AnchorY) = (x, y);
            RedrawOverlay();
            e.Handled = true;
        };
    }

    private static Button StandButton(string text, string tip, Action click)
    {
        var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 0, 6, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The frame's stand point: a cyan cross (dashed where it stands on its bottom-middle, having none).</summary>
    private void DrawStandMarker(Rect rendered, BitmapSource bmp)
    {
        if (_standText is null || CurrentFrame() is not { } frame) return;
        _standPreview?.ShowFrame(_currentFrameIndex); // the preview follows the frame and its stand point
        var (sx, sy) = StandPoints.Of(frame, bmp);
        _standText.Text = frame.HasAnchor ? $"Stands at {frame.AnchorX}, {frame.AnchorY}." : "Stands on its bottom-middle.";
        if (_holdStillCheck is not null) { _holdStillSuppress = true; _holdStillCheck.IsChecked = frame.HoldsStill; _holdStillSuppress = false; }
        var p = PixelToDisplay(rendered, bmp, sx, sy);
        var dash = frame.HasAnchor ? null : new DoubleCollection { 2, 2 };
        OverlayCanvas.Children.Add(new Line { X1 = p.X - 9, Y1 = p.Y, X2 = p.X + 9, Y2 = p.Y, Stroke = Brushes.Cyan, StrokeThickness = 2, StrokeDashArray = dash, IsHitTestVisible = false });
        OverlayCanvas.Children.Add(new Line { X1 = p.X, Y1 = p.Y - 9, X2 = p.X, Y2 = p.Y + 9, Stroke = Brushes.Cyan, StrokeThickness = 2, StrokeDashArray = dash, IsHitTestVisible = false });
    }

    private void ShowStandPreview()
    {
        if (_standPreview is not null) { _standPreview.Activate(); return; }
        _standPreview = new StandPointPreviewWindow(_sprite) { Owner = this };
        _standPreview.Closed += (_, _) => _standPreview = null;
        _standPreview.StandMoved += RedrawOverlay; // dragged there in the preview: the cross follows
        _standPreview.Show();
        _standPreview.ShowFrame(_currentFrameIndex);
    }

    private void ShowStandHowTo()
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14),
            Text =
                "Stand points (Ultima 8 projects)\n\n" +
                "Ultima 8 stored, for every frame of a character, where it stands - so the body stays still while the legs move. " +
                "Frames cut out of the game (or drawn) are trimmed to what's in each, so that's lost: a walk sways from side to side, or bobs.\n\n" +
                "1. Auto-align all frames: every frame stands so the head keeps still. For most walks and runs that's all it takes.\n" +
                "2. Preview...: the frame over the one before it (see-through), both standing on their stand points. Drag the frame, or use the " +
                "arrow keys, until the body lines up with the one before. Play shows the whole animation in place.\n" +
                "3. Or by hand: Set stand point, then click the picture where this frame's feet stand on the ground.\n" +
                "4. Bottom-middle: a frame stands on its bottom-middle again.\n" +
                "5. Holds still (Step sprites): tick the frames where both feet are planted - the Avatar doesn't move in them, " +
                "only in the frames where his legs move (none ticked: the first frame holds still).\n\n" +
                "The cyan cross is this frame's stand point (dashed: it stands on its bottom-middle). In the game the cross of every frame " +
                "sits on the character's spot. Do it for each direction's sprite (walking, running, standing...). Ultima 7 projects don't use stand points."
        };
        new Window { Title = "How to: stand points", Content = new ScrollViewer { Content = text }, Width = 520, Height = 430, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false }.Show();
    }
}
