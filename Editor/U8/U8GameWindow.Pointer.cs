// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
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
public partial class U8GameWindow
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
    private void FlashPointer(MousePointerKind kind, bool playSound = true)
    {
        ShowPointer(kind);
        if (!playSound) { _pointerFlashTimer?.Stop(); _pointerFlashTimer?.Start(); return; } // something else makes the sound
        // Exult plays a sound together with the flash (drag.cc: flash_shape(Mouse::redx); play_sound_effect(76)) - here that sound is the pointer sprite's own, set per frame in the Sprite Editor: its first frame's sound plays the moment it flashes, the rest as the animation reaches them.
        if (PointerSprite(kind) is { } sprite)
            MousePointerCatalog.PlayFirstFrameSound(sprite, _resources);
        _pointerFlashTimer?.Stop();
        _pointerFlashTimer?.Start();
    }

    /// <summary>A refusal the player should see and feel: the message in the status bar, and the matching Exult pointer flashed.</summary>
    private void Refuse(MousePointerKind pointer, string message)
    {
        StatusText.Text = message;
        if (!_quietRefusals) FlashPointer(pointer); // an event's hand-over: nobody dragged anything

    }

    /// <summary>
    /// A pointer's sprite. Ultima 8: a refusal's own pointer (Too Heavy, Out of Range, Out of Ammo, Won't Fit, Blocked) left
    /// without a sprite shows the X - Ultima 8's one refusal pointer (Mouse::flashCrossCursor); with one, its own, as Ultima 7.
    /// </summary>
    private SpriteEntry? PointerSprite(MousePointerKind kind) =>
        MousePointerCatalog.Resolve(_sprites, _systemSettings.MousePointers.Sprites.GetValueOrDefault(kind))
        ?? (MousePointerCatalog.IsRefusal(kind) ? MousePointerCatalog.Resolve(_sprites, _systemSettings.MousePointers.Sprites.GetValueOrDefault(MousePointerKind.RedX)) : null);

    /// <summary>The direction arrow showing now ("Walk.North"...), or null.</summary>
    private string? _shownArrow;

    private void ShowPointer(MousePointerKind kind)
    {
        if (_shownPointer == kind && _shownArrow is null) return;
        _shownPointer = kind;
        _shownArrow = null;
        ShowPointerSprite(PointerSprite(kind));
    }

    /// <summary>A direction arrow (MousePointerCatalog.U8ArrowSets): its set's arrow, else the walking one. False: none set.</summary>
    private bool ShowArrow(string set, SpriteDirection direction)
    {
        var arrows = _systemSettings.MousePointers.U8Arrows;
        string key = MousePointerCatalog.ArrowKey(set, direction);
        var sprite = MousePointerCatalog.Resolve(_sprites, arrows.GetValueOrDefault(key));
        if (sprite is null && set != "Walk") { key = MousePointerCatalog.ArrowKey("Walk", direction); sprite = MousePointerCatalog.Resolve(_sprites, arrows.GetValueOrDefault(key)); }
        if (sprite is null) return false;
        if (_shownArrow == key) return true;
        _shownArrow = key;
        _shownPointer = null;
        ShowPointerSprite(sprite);
        return true;
    }

    /// <summary>
    /// Each frame (Pentagram's Mouse::getMouseFrame): in combat mode the combat arrow toward the pointer (the running arrow
    /// while running); walking with the right mouse button, the step, walk or run arrow by how far the pointer is; else the
    /// hand (or the pointer for choosing a target). A flashed refusal, a drag or a choice keeps its own.
    /// </summary>
    private void UpdateU8Pointer()
    {
        if (_pointerFlashTimer is { IsEnabled: true } || IsChoosingTarget || _pointer is { IsDragging: true } || _gameOver) return;
        bool moving = _rightMouseHeld && Mouse.RightButton == MouseButtonState.Pressed && !IsShowingMessage && !InCinema;
        if (!_combatMode && !moving) { ShowPointer(RestingPointer); return; }
        var (screen, length) = PointerFromAvatar();
        var direction = screen switch
        {
            (0, -1) => SpriteDirection.North, (1, -1) => SpriteDirection.NorthEast, (1, 0) => SpriteDirection.East, (1, 1) => SpriteDirection.SouthEast,
            (0, 1) => SpriteDirection.South, (-1, 1) => SpriteDirection.SouthWest, (-1, 0) => SpriteDirection.West, _ => SpriteDirection.NorthWest
        };
        if (!_combatMode && length < 0) { ShowPointer(RestingPointer); return; } // too close to move (Step beyond): the hand
        string set = _combatMode && !(moving && length == 2) ? "Combat" : length <= 0 ? "Step" : length == 2 ? "Run" : "Walk"; // the combat arrows are off while running
        if (!ShowArrow(set, direction)) ShowPointer(RestingPointer);
    }

    /// <summary>Ultima 8: a pointer is drawn at its own sprite's size (pointers of different sizes may be picked, each locked to its sprite).</summary>
    private (int Width, int Height)? _pointerOwnSize;

    private void ShowPointerSprite(SpriteEntry? sprite)
    {
        _pointerOwnSize = sprite is not null ? MousePointerCatalog.PixelSize(sprite) : null;
        _pointerAnimator?.Stop();
        _pointerAnimator = null;
        _pointerImage.Source = null;
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
        if (_pointerOwnSize is { } own) return own; // its own size
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
