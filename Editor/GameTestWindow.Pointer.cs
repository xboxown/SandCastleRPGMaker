using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The engine's own mouse pointer, Exult-style (mouse.h / mouse.cc): the
/// operating system's pointer is switched off for the whole game window
/// (ForceCursor, so no gump or control can bring it back) and a sprite
/// pointer is drawn on top of everything instead, at the game's own scale,
/// its hot spot at the sprite's Origin. The resting pointer is the hand;
/// refusals flash another pointer for 600 ms (Mouse::flash_shape) and then
/// the hand returns. A pointer with no sprite assigned shows as a black
/// square of the pointer size. Animated pointer sprites animate, with any
/// per-frame sound, through the same SpriteAnimator everything else uses.
/// </summary>
public partial class GameTestWindow
{
    private SystemSettings _systemSettings = new();
    private readonly Border _pointerHost = new() { IsHitTestVisible = false };
    private readonly Image _pointerImage = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
    private SpriteAnimator? _pointerAnimator;
    private MousePointerKind? _shownPointer;
    private DispatcherTimer? _pointerFlashTimer;
    /// <summary>The hot spot - the exact pixel that "points" - in unscaled sprite pixels.</summary>
    private Point _pointerHotspot;
    private double _pointerScale = -1;
    private Point _lastPointerPosition;

    /// <summary>Called once from the constructor.</summary>
    private void InitializePointer(SystemSettings settings)
    {
        _systemSettings = settings;
        Cursor = Cursors.None;
        ForceCursor = true; // overrides every child's own Cursor (gump hand cursors, buttons, ...) - the Windows pointer never shows in the game
        RenderOptions.SetBitmapScalingMode(_pointerImage, BitmapScalingMode.NearestNeighbor);
        _pointerHost.Child = _pointerImage;
        PointerCanvas.Children.Add(_pointerHost);

        // handledEventsToo: the drag pipeline marks every move during a drag as handled, which would otherwise skip this and freeze the pointer while the dragged item moves.
        AddHandler(PreviewMouseMoveEvent, new MouseEventHandler((_, e) => MovePointer(e.GetPosition(PointerCanvas))), handledEventsToo: true);
        MouseLeave += (_, _) => _pointerHost.Visibility = Visibility.Hidden;
        MouseEnter += (_, e) =>
        {
            _pointerHost.Visibility = Visibility.Visible;
            MovePointer(e.GetPosition(PointerCanvas));
        };

        _pointerFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MousePointerCatalog.FlashMilliseconds) };
        _pointerFlashTimer.Tick += (_, _) =>
        {
            _pointerFlashTimer.Stop();
            ShowPointer(RestingPointer);
        };
        Closed += (_, _) => { _pointerFlashTimer.Stop(); _pointerAnimator?.Stop(); };

        _pointerHost.Visibility = IsMouseOver ? Visibility.Visible : Visibility.Hidden;
        ShowPointer(RestingPointer);
    }

    /// <summary>Exult Mouse::flash_shape: show this pointer for 600 ms, then return to the hand.</summary>
    private void FlashPointer(MousePointerKind kind)
    {
        ShowPointer(kind);
        // Exult plays a sound together with the flash (drag.cc: flash_shape(Mouse::redx); play_sound_effect(76)) - here that sound is the pointer sprite's own, set per frame in the Sprite Editor: its first frame's sound plays the moment it flashes, the rest as the animation reaches them.
        if (MousePointerCatalog.Resolve(_sprites, _systemSettings.MousePointers.Sprites.GetValueOrDefault(kind)) is { } sprite)
            MousePointerCatalog.PlayFirstFrameSound(sprite, _resources);
        _pointerFlashTimer?.Stop();
        _pointerFlashTimer?.Start();
    }

    /// <summary>A refusal the player should see and feel: the message in the status bar, and the matching Exult pointer flashed.</summary>
    private void Refuse(MousePointerKind pointer, string message)
    {
        StatusText.Text = message;
        FlashPointer(pointer);
    }

    private void ShowPointer(MousePointerKind kind)
    {
        if (_shownPointer == kind) return;
        _shownPointer = kind;
        _pointerAnimator?.Stop();
        _pointerAnimator = null;
        _pointerImage.Source = null;

        var sprite = MousePointerCatalog.Resolve(_sprites, _systemSettings.MousePointers.Sprites.GetValueOrDefault(kind));
        if (sprite is not null)
        {
            _pointerHost.Background = null;
            _pointerHotspot = new Point(sprite.OriginX, sprite.OriginY);
            _pointerAnimator = new SpriteAnimator(sprite, _pointerImage, _resources);
        }
        else
        {
            // No sprite: a black square, per spec - never the Windows pointer.
            _pointerHost.Background = Brushes.Black;
            _pointerHotspot = new Point(0, 0);
        }
        _pointerScale = -1; // re-apply size for the new pointer
        MovePointer(_lastPointerPosition);
    }

    /// <summary>All pointers share one size (System Settings); 16x16 until one is chosen.</summary>
    private (int Width, int Height) PointerSize()
    {
        var pointers = _systemSettings.MousePointers;
        return pointers.Width > 0 && pointers.Height > 0 ? (pointers.Width, pointers.Height) : (16, 16);
    }

    private void MovePointer(Point position)
    {
        _lastPointerPosition = position;
        // Same scale as the game world (Exult scales its pointer with the whole screen) - kept in step if the window is resized.
        double scale = ViewportScale.ScaleX;
        if (scale != _pointerScale)
        {
            _pointerScale = scale;
            var (width, height) = PointerSize();
            _pointerHost.Width = width * scale;
            _pointerHost.Height = height * scale;
        }
        Canvas.SetLeft(_pointerHost, position.X - _pointerHotspot.X * scale);
        Canvas.SetTop(_pointerHost, position.Y - _pointerHotspot.Y * scale);
    }
}
