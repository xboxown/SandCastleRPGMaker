// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Avatar's movement as Ultima 8 (Pentagram's U8AvatarMoverProcess, GravityProcess):
///  - Directions are the world's (north is up-right on the screen, as Ultima 8's art faces); what the player presses
///    is the screen's: an arrow key (two at once: a diagonal) walks that way up, down, across the screen. Shift runs.
///  - The right mouse button held walks toward the pointer: near the Avatar a step, further a walk, far a run - once
///    already walking (Mouse::getMouseLength, its walk / run thresholds 50 and 160 of 255 of half the view).
///  - Both mouse buttons (or J) jump: toward the pointer or the way they face - a running jump when running; with the
///    pointer on the Avatar (or standing still with J), up: onto a ledge 16 to 72 high in front (climb16..climb72 - here
///    2 to 9 lifts), or a hop on the spot.
///  - The Avatar can walk off a ledge and fall: 80 or more (10 lifts) hurts, (height - 72) / 4; 104 or more (13 lifts)
///    kills (GravityProcess::actorFallStoppedU8). Ultima 8's z: a lift here is 8 of its units.
/// </summary>
public partial class U8GameWindow
{
    private const int U8UnitsPerLift = 8;
    private const double U8RunFactor = 2.0, U8StepFactor = 0.5;
    private int U8WalkThreshold => _systemSettings.Ultima8.WalkThreshold; // of 255 (Pentagram's walk_threshold, 50) - System Settings > Ultima 8
    private int U8RunThreshold => _systemSettings.Ultima8.RunThreshold;   // (run_threshold, 160)

    private bool _rightMouseHeld;
    private Point _mouseOnCanvas;
    private bool _u8Running;     // the last frame was a run (a run carries on; a running jump)
    private double _u8SpeedFactor = 1.0;
    private int? _u8FallFrom;    // falling from this lift: hurt on landing

    private sealed class U8Jump
    {
        public double FromX, FromY, ToX, ToY, T, Duration;
        public int FromLift, ToLift;
        public double Arc;
    }
    private U8Jump? _jump;

    /// <summary>The mouse's part in moving (called from the constructor): the right button held walks; both buttons jump.</summary>
    private void InitU8Movement()
    {
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Right) _rightMouseHeld = true;
            if (U8CombatMouseDown(e)) { e.Handled = true; return; } // combat mode: a swing, a kick, a block (U8GameWindow.U8Combat.cs)
            if (e.ChangedButton == MouseButton.Right)
            {
                _rightMouseHeld = true;
                _mouseOnCanvas = e.GetPosition(GameCanvas);
                if (e.LeftButton == MouseButtonState.Pressed) { TryStartU8Jump(fromMouse: true); e.Handled = true; }
            }
            else if (e.ChangedButton == MouseButton.Left && _rightMouseHeld) { TryStartU8Jump(fromMouse: true); e.Handled = true; } // both buttons: a jump
        };
        PreviewMouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Right) _rightMouseHeld = false; U8CombatMouseUp(e); };
        PreviewMouseMove += (_, e) => _mouseOnCanvas = e.GetPosition(GameCanvas);
        LostMouseCapture += (_, _) => { if (Mouse.RightButton != MouseButtonState.Pressed) _rightMouseHeld = false; };
    }

    /// <summary>A screen direction (one of 8) as the world's: up the screen is north-west, right is north-east (Direction_Get).</summary>
    private static (int X, int Y) WorldFromScreen(int sx, int sy) => (Math.Sign(sx + sy), Math.Sign(sy - sx));

    /// <summary>Where the Avatar is drawn on the canvas (their feet).</summary>
    private Point PlayerOnScreen() => GameCanvas.ToScreen(_playerPixelX - _playerVisualLiftZ * LiftPixelShift, _playerPixelY - _playerVisualLiftZ * LiftPixelShift);

    /// <summary>The pointer from the Avatar, as one of 8 screen directions, and how far: 0 a step, 1 a walk, 2 a run (getMouseLength).</summary>
    private ((int X, int Y) Screen, int Length) PointerFromAvatar()
    {
        var at = PlayerOnScreen();
        double dx = _mouseOnCanvas.X - at.X, dy = _mouseOnCanvas.Y - at.Y;
        double angle = Math.Atan2(dy, dx); // screen: y down
        int sector = (int)Math.Round(angle / (Math.PI / 4)); // -4..4, 0 = right
        (int, int)[] dirs = { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };
        var screen = dirs[((sector % 8) + 8) % 8];
        double halfW = ViewportWidth / 2, halfH = ViewportHeight / 2;
        int length = Math.Abs(dx) > halfW * U8RunThreshold / 255 || Math.Abs(dy) > halfH * U8RunThreshold / 255 ? 2
            : Math.Abs(dx) > halfW * U8WalkThreshold / 255 || Math.Abs(dy) > halfH * U8WalkThreshold / 255 ? 1 : 0;
        return (screen, length);
    }

    /// <summary>The way the player wants the Avatar to go this frame (world, -1..1 each), and at what speed; false: no input.</summary>
    private bool TryGetU8Direction(out int dx, out int dy)
    {
        dx = dy = 0;
        if (_pointer is { IsDragging: true }) return false; // holding something on the mouse: no walking until it's put down
        if (IsShowingMessage || InCinema) return false; // talking (the world goes on; the Avatar waits for the answer), or a cutscene
        int sx = 0, sy = 0;
        if (_heldDirectionKeys.Contains(Key.Left)) sx--;
        if (_heldDirectionKeys.Contains(Key.Right)) sx++;
        if (_heldDirectionKeys.Contains(Key.Up)) sy--;
        if (_heldDirectionKeys.Contains(Key.Down)) sy++;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (sx != 0 || sy != 0)
        {
            (dx, dy) = WorldFromScreen(sx, sy);
            _u8SpeedFactor = shift ? U8RunFactor : 1.0;
            return true;
        }
        if (_rightMouseHeld && Mouse.RightButton == MouseButtonState.Pressed)
        {
            var (screen, length) = PointerFromAvatar();
            (dx, dy) = WorldFromScreen(screen.X, screen.Y);
            // a run only once walking (or running) already, as U8AvatarMoverProcess
            _u8SpeedFactor = length == 0 ? U8StepFactor : length == 2 && (_wasMovingLastFrame || shift) ? U8RunFactor : 1.0;
            return true;
        }
        _rightMouseHeld = false;
        return false;
    }

    private static SpriteDirection FacingOf(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => SpriteDirection.North, (1, -1) => SpriteDirection.NorthEast, (1, 0) => SpriteDirection.East, (1, 1) => SpriteDirection.SouthEast,
        (0, 1) => SpriteDirection.South, (-1, 1) => SpriteDirection.SouthWest, (-1, 0) => SpriteDirection.West, _ => SpriteDirection.NorthWest
    };

    private static (int X, int Y) VectorOf(SpriteDirection facing) => facing switch
    {
        SpriteDirection.North => (0, -1), SpriteDirection.NorthEast => (1, -1), SpriteDirection.East => (1, 0), SpriteDirection.SouthEast => (1, 1),
        SpriteDirection.South => (0, 1), SpriteDirection.SouthWest => (-1, 1), SpriteDirection.West => (-1, 0), _ => (-1, -1)
    };

    /// <summary>The pose for moving at this speed: Run (Ultima 8) when running and the project has it, Step when stepping, else Walking.</summary>
    private PoseDefinition? U8MovePose() =>
        (_u8SpeedFactor >= U8RunFactor ? PoseByName("Run") : _u8SpeedFactor <= U8StepFactor ? PoseByName("Step") : null) ?? _walkingPose;

    // ---------- Jumping ----------

    /// <summary>J, or both mouse buttons: a jump - toward the pointer (with the mouse), the way pressed, or the way they face.</summary>
    private void TryStartU8Jump(bool fromMouse)
    {
        if (_player is null || _jump is not null || _gamePaused || PlayerSeated || IsIncapacitated(_player) || PlayerLevitating) return;
        int dx, dy;
        bool up;
        if (fromMouse)
        {
            var (screen, length) = PointerFromAvatar();
            (dx, dy) = WorldFromScreen(screen.X, screen.Y);
            up = length == 0; // the pointer on the Avatar: up
        }
        else if (TryGetU8Direction(out dx, out dy)) up = false;
        else { (dx, dy) = VectorOf(_playerFacing); up = true; }
        _playerFacing = FacingOf(dx, dy);
        if (up) { StartJumpUp(dx, dy); return; }
        bool running = _u8Running;
        double len = Math.Sqrt(dx * dx + dy * dy), cells = running ? 3 : 2; // a running jump goes further
        double toX = _playerPixelX + dx / len * cells * _tileWidth, toY = _playerPixelY + dy / len * cells * _tileHeight;
        // Through the air: as far as there's room at the jump's height; it lands on whatever is under it there.
        double arc = running ? 3 : 2;
        double lastX = _playerPixelX, lastY = _playerPixelY;
        const int samples = 12;
        for (int i = 1; i <= samples; i++)
        {
            double s = i / (double)samples;
            double x = _playerPixelX + (toX - _playerPixelX) * s, y = _playerPixelY + (toY - _playerPixelY) * s;
            if (x < 0 || y < 0 || x >= _map.Width * _tileWidth || y >= _map.Height * _tileHeight) break;
            int liftHere = _playerZ + (int)Math.Round(arc * 4 * s * (1 - s));
            if (!TryFlyStep(PlayerFootprintAt(x, y), liftHere, _playerSize.Z, null, null, out _, out _)) break;
            (lastX, lastY) = (x, y);
        }
        int landLift = StandingLiftAt(PlayerFootprintAt(lastX, lastY), _player);
        if (landLift > _playerZ + 2) landLift = _playerZ; // can't land up there
        _jump = new U8Jump { FromX = _playerPixelX, FromY = _playerPixelY, ToX = lastX, ToY = lastY, Duration = running ? 0.75 : 0.6, FromLift = _playerZ, ToLift = landLift, Arc = arc };
        ApplyPoseSprite(PoseByName(running ? "Running Jump" : "Jump") ?? PoseByName("Jump") ?? _walkingPose);
    }

    /// <summary>Up: onto a ledge in front, 2 to 9 lifts higher (climb16..climb72), the highest that fits - else a hop on the spot.</summary>
    private void StartJumpUp(int dx, int dy)
    {
        double len = Math.Sqrt(dx * dx + dy * dy);
        double x = _playerPixelX + dx / len * _tileWidth, y = _playerPixelY + dy / len * _tileHeight;
        var foot = PlayerFootprintAt(x, y);
        int top = StandingLiftAt(foot, _player), rise = top - _playerZ;
        if (rise >= 2 && rise <= 9 && TryFlyStep(foot, top, _playerSize.Z, null, null, out _, out _))
        {
            _jump = new U8Jump { FromX = _playerPixelX, FromY = _playerPixelY, ToX = x, ToY = y, Duration = 0.5 + rise * 0.05, FromLift = _playerZ, ToLift = top, Arc = 1 };
            ApplyPoseSprite(PoseByName("Climb") ?? PoseByName("Climb Up") ?? PoseByName("Jump Up") ?? _walkingPose);
            return;
        }
        _jump = new U8Jump { FromX = _playerPixelX, FromY = _playerPixelY, ToX = _playerPixelX, ToY = _playerPixelY, Duration = 0.45, FromLift = _playerZ, ToLift = _playerZ, Arc = 2 };
        ApplyPoseSprite(PoseByName("Jump Up") ?? PoseByName("Jump") ?? _idlePose);
    }

    /// <summary>A jump in the air this frame; true while it lasts (nothing else moves the Avatar).</summary>
    private bool UpdateU8Jump(double deltaSeconds)
    {
        if (_jump is not { } j) return false;
        j.T = Math.Min(j.Duration, j.T + deltaSeconds);
        double s = j.T / j.Duration;
        double x = j.FromX + (j.ToX - j.FromX) * s, y = j.FromY + (j.ToY - j.FromY) * s;
        double visualLift = j.FromLift + (j.ToLift - j.FromLift) * s + j.Arc * 4 * s * (1 - s);
        MovePlayerU8(x, y);
        _playerVisualLiftZ = (int)Math.Round(visualLift); // drawn up in the air; it stands at its old lift till it lands
        DrawPlayerU8();
        if (j.T < j.Duration) return true;
        _jump = null;
        int fellFrom = j.FromLift;
        _playerZ = j.ToLift;
        _playerVisualLiftZ = _playerZ;
        DrawPlayerU8();
        if (fellFrom > _playerZ) LandU8(fellFrom); else ApplyPoseSprite(PoseByName("Land") ?? _idlePose);
        return true;
    }

    /// <summary>The Avatar to a point on the map: its cell, and the map's events for leaving and entering cells.</summary>
    private void MovePlayerU8(double x, double y)
    {
        _playerPixelX = x;
        _playerPixelY = y;
        int cellX = (int)Math.Floor(x / _tileWidth), cellY = (int)Math.Floor(y / _tileHeight);
        if (cellX == _playerX && cellY == _playerY) return;
        var leavingEvent = _eventUnderPlayer;
        _playerX = cellX;
        _playerY = cellY;
        if (leavingEvent is not null && (leavingEvent.X != _playerX || leavingEvent.Y != _playerY))
            TryFireEventPages(leavingEvent, leavingEvent.EventPages, BookTriggerConditionBased.ByExit, selfBookId: -1, $"\"{leavingEvent.Name}\" fired (ByExit)");
        _eventUnderPlayer = EventAt(_playerX, _playerY, _playerZ);
        if (_eventUnderPlayer is not null)
            TryFireEventPages(_eventUnderPlayer, _eventUnderPlayer.EventPages, BookTriggerConditionBased.ByEnter, selfBookId: -1, $"\"{_eventUnderPlayer.Name}\" fired (ByEnter)");
    }

    private void DrawPlayerU8()
    {
        Panel.SetZIndex(_playerImage, PlayerZIndex());
        Canvas.SetLeft(_playerImage, _playerPixelX - _playerImage.Width / 2 - _playerVisualLiftZ * LiftPixelShift);
        Canvas.SetTop(_playerImage, _playerPixelY + _tileHeight / 2 - _playerImage.Height - _playerVisualLiftZ * LiftPixelShift);
        UpdateCamera();
    }

    // ---------- Standing idle (U8AvatarMoverProcess::idle) ----------

    private double _u8IdleTicks;           // Pentagram's _idleTime: its ticks (30 a second) standing still
    private bool _u8Looking;               // looking left or right now
    private string _u8LastLook = "Look Left";

    /// <summary>
    /// Standing still: after a while the Avatar looks left or right (random(3000) + 150 under the ticks idle - usually
    /// the other way from last time, 1 in 5 the same), and goes back to standing (random(1500) + 30 under them).
    /// Only with art for it (the Look Left / Look Right poses). Anything else the Avatar does starts the count again.
    /// </summary>
    private void UpdateU8Idle(double deltaSeconds, bool idle)
    {
        if (!idle || _combatMode || _player is null || PlayerSeated || IsIncapacitated(_player) || _jump is not null)
        {
            _u8IdleTicks = 0;
            if (_u8Looking) { _u8Looking = false; if (idle) ApplyPoseSprite(_idlePose); }
            return;
        }
        double before = _u8IdleTicks;
        _u8IdleTicks += deltaSeconds * 30;
        for (int tick = (int)before + 1; tick <= (int)_u8IdleTicks; tick++)
        {
            if (_u8Looking)
            {
                if (U8Random(1500) + 30 >= tick) continue;
                _u8Looking = false;
                ApplyPoseSprite(_idlePose);
            }
            else
            {
                if (U8Random(3000) + 150 >= tick) continue;
                string look = U8Random(4) == 0 ? _u8LastLook : _u8LastLook == "Look Left" ? "Look Right" : "Look Left";
                if (PoseByName(look) is not { } pose) { _u8IdleTicks = 1; return; } // no art for it: just standing
                _u8LastLook = look;
                _u8Looking = true;
                ApplyPoseSprite(pose);
            }
            _u8IdleTicks = 1;
            return;
        }
    }

    // ---------- Falling ----------

    /// <summary>Walked off a ledge (more than a step down): the fall is drawn, and hurts on landing.</summary>
    private void StartFallU8(int fromLift) => _u8FallFrom = Math.Max(_u8FallFrom ?? fromLift, fromLift);

    /// <summary>Each frame: a fall that has landed hurts (as Pentagram's actorFallStoppedU8).</summary>
    private void UpdateFallU8()
    {
        if (_u8FallFrom is not { } from || _playerVisualLiftZ > _playerZ) return;
        _u8FallFrom = null;
        LandU8(from);
    }

    /// <summary>Landed from a height: 80 or more of Ultima 8's units hurts, (height - 72) / 4; 104 or more kills; the Land pose.</summary>
    private void LandU8(int fromLift)
    {
        if (_player is null) return;
        int height = (fromLift - _playerZ) * U8UnitsPerLift;
        if (height >= 80)
        {
            int damage = height < 104 ? (height - 72) / 4 : 100000;
            Damage(_player, Math.Max(1, damage), "a fall");
        }
        if (!IsIncapacitated(_player)) ApplyPoseSprite(PoseByName("Land") ?? _idlePose);
    }
}
