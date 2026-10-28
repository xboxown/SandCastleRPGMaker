using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Sprite Editor's weapon preview for a character's sprite: a weapon from the Weapon in hand group, drawn in the hand
/// of the frame being edited exactly as the game draws it (GameTestWindow.WeaponInHand: the body fitted in its box, the
/// weapon frame chosen by WeaponInHandLook, its grip on the hand, mirrored facing east or west, behind the body where the
/// frame says). It follows the editor's frame, and redraws as the hand point moves. Drag the weapon (or click, or the
/// arrow keys) to move the frame's hand point - the one thing it changes; the rest is only a preview.
/// </summary>
public class WeaponPreviewWindow : Window
{
    private static string? _lastWeapon; // the weapon last previewed, offered first next time

    private readonly SpriteEntry _sprite;
    private readonly ComboBox _weaponCombo = new() { MinWidth = 160, Margin = new Thickness(4, 0, 12, 0) };
    private readonly ComboBox _facingCombo = new() { Width = 80, Margin = new Thickness(4, 0, 12, 0) };
    private readonly CheckBox _attackingCheck = new() { Content = "Attacking (swing)", VerticalAlignment = VerticalAlignment.Center, ToolTip = "A frame on Automatic shows the swing frame for its point of the attack - else held" };
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Cross }; // clickable all over: the hand goes where it's clicked
    private readonly TextBlock _info = new() { Margin = new Thickness(8, 4, 8, 6), TextWrapping = TextWrapping.Wrap };
    private int _frameIndex;
    // Where the last drawing put the body (in _canvas), for turning the mouse into the frame's pixels.
    private double _bodyLeft, _bodyTop, _scale = 1;
    private int _bodyWidth, _bodyHeight;
    private double _reach; // how far outside the picture the hand may go, in its pixels (the room drawn round it)
    private Image? _weaponImage;
    private Vector _dragOffset; // from the mouse to the hand, so a dragged weapon doesn't jump
    private bool _dragging;

    private static readonly SpriteDirection[] Facings = { SpriteDirection.North, SpriteDirection.East, SpriteDirection.South, SpriteDirection.West };

    /// <summary>The frame's hand point was moved here (dragging the weapon, a click, the arrow keys): the Sprite Editor shows it.</summary>
    public event Action? HandMoved;

    /// <summary>The weapon, facing or attacking changed (the Sprite Editor checks the hand point again).</summary>
    public event Action? ChoiceChanged;

    /// <summary>What it previews now: the weapon (null: none drawn yet), the facing, attacking or not.</summary>
    internal (SpriteEntry? Weapon, SpriteDirection Facing, bool Attacking) Choice
    {
        get
        {
            var weapons = WeaponSprites();
            var weapon = _weaponCombo.SelectedIndex >= 0 && _weaponCombo.SelectedIndex < weapons.Count ? weapons[_weaponCombo.SelectedIndex] : null;
            return (weapon, _facingCombo.SelectedIndex >= 0 ? Facings[_facingCombo.SelectedIndex] : SpriteDirection.South, _attackingCheck.IsChecked == true);
        }
    }

    /// <summary>With no preview open: the weapon last previewed (else the first), the facing the sprite's name says, attacking if it's an attack.</summary>
    internal static (SpriteEntry? Weapon, SpriteDirection Facing, bool Attacking) DefaultChoice(SpriteEntry sprite)
    {
        var weapons = WeaponSprites();
        return (weapons.FirstOrDefault(w => w.Name == _lastWeapon) ?? weapons.FirstOrDefault(), GuessFacing(sprite.Name), GuessAttacking(sprite.Name));
    }

    private static bool GuessAttacking(string name) => name.Contains("Attack", StringComparison.OrdinalIgnoreCase) || name.Contains("Shoot", StringComparison.OrdinalIgnoreCase);

    public WeaponPreviewWindow(SpriteEntry sprite)
    {
        _sprite = sprite;
        Title = $"Weapon preview: {sprite.Name}";
        Width = 420;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var weapons = WeaponSprites();
        _weaponCombo.ItemsSource = weapons.Select(w => w.Name).ToList();
        _weaponCombo.SelectedIndex = Math.Max(0, weapons.FindIndex(w => w.Name == _lastWeapon));
        _weaponCombo.ToolTip = "A weapon from the Sprite Editor's Weapon in hand group";
        _facingCombo.ItemsSource = Facings.Select(f => f.ToString()).ToList();
        _facingCombo.SelectedIndex = Array.IndexOf(Facings, GuessFacing(sprite.Name));
        _facingCombo.ToolTip = "Which way the character faces: Exult mirrors the weapon facing east or west";

        _attackingCheck.IsChecked = GuessAttacking(sprite.Name);
        _weaponCombo.SelectionChanged += (_, _) => { _lastWeapon = _weaponCombo.SelectedItem as string; Redraw(); ChoiceChanged?.Invoke(); };
        _facingCombo.SelectionChanged += (_, _) => { Redraw(); ChoiceChanged?.Invoke(); };
        _attackingCheck.Checked += (_, _) => { Redraw(); ChoiceChanged?.Invoke(); };
        _attackingCheck.Unchecked += (_, _) => { Redraw(); ChoiceChanged?.Invoke(); };

        var top = new WrapPanel { Margin = new Thickness(8, 8, 8, 4) };
        top.Children.Add(new TextBlock { Text = "Weapon:", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(_weaponCombo);
        top.Children.Add(new TextBlock { Text = "Facing:", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(_facingCombo);
        top.Children.Add(_attackingCheck);

        var view = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas, Margin = new Thickness(8) };
        RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.NearestNeighbor);
        var stage = new Border { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), Margin = new Thickness(8, 0, 8, 0), Child = view };

        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (_frameIndex < 0 || _frameIndex >= _sprite.Frames.Count || _scale <= 0) return;
            var frame = _sprite.Frames[_frameIndex];
            var at = e.GetPosition(_canvas);
            // On the weapon: dragged by where it was taken hold of. Elsewhere: the hand goes to the click.
            bool onWeapon = _weaponImage is not null && frame.HasHand && _weaponImage.InputHitTest(e.GetPosition(_weaponImage)) is not null;
            _dragOffset = onWeapon ? new Point(_bodyLeft + frame.HandX * _scale, _bodyTop + frame.HandY * _scale) - at : new Vector();
            _dragging = true;
            _canvas.CaptureMouse();
            MoveHandTo(at);
            e.Handled = true;
        };
        _canvas.MouseMove += (_, e) => { if (_dragging) MoveHandTo(e.GetPosition(_canvas)); };
        _canvas.MouseLeftButtonUp += (_, _) => { _dragging = false; _canvas.ReleaseMouseCapture(); };
        _canvas.LostMouseCapture += (_, _) => _dragging = false;
        PreviewKeyDown += (_, e) =>
        {
            var (dx, dy) = e.Key switch { System.Windows.Input.Key.Left => (-1, 0), System.Windows.Input.Key.Right => (1, 0), System.Windows.Input.Key.Up => (0, -1), System.Windows.Input.Key.Down => (0, 1), _ => (0, 0) };
            if ((dx, dy) == (0, 0) || _frameIndex < 0 || _frameIndex >= _sprite.Frames.Count || !_sprite.Frames[_frameIndex].HasHand) return;
            if (e.OriginalSource is ComboBox) return; // the arrows choose in a list
            var frame = _sprite.Frames[_frameIndex];
            SetHand(frame.HandX + dx, frame.HandY + dy);
            e.Handled = true;
        };

        var root = new DockPanel();
        var hint = new TextBlock { Text = "Drag the weapon (or click where the hand goes, or the arrow keys) to move this frame's hand point.", Margin = new Thickness(8, 0, 8, 4), Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(_info, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(hint);
        root.Children.Add(_info);
        root.Children.Add(stage);
        Content = root;
    }

    /// <summary>The editor's frame now (or its hand point, weapon frame or behind changed): drawn again.</summary>
    public void ShowFrame(int index)
    {
        _frameIndex = index;
        Redraw();
    }

    /// <summary>The hand point under a spot in the picture (less the drag offset), kept inside the frame.</summary>
    private void MoveHandTo(Point at)
    {
        var hand = at + _dragOffset;
        SetHand((int)Math.Round((hand.X - _bodyLeft) / _scale), (int)Math.Round((hand.Y - _bodyTop) / _scale));
    }

    private void SetHand(int x, int y)
    {
        var frame = _sprite.Frames[_frameIndex];
        int reach = (int)_reach; // a little outside the frame too: a weapon held out from the body
        (x, y) = (Math.Clamp(x, -reach, _bodyWidth + reach), Math.Clamp(y, -reach, _bodyHeight + reach));
        if ((x, y) == (SpriteFrame.NoHand, SpriteFrame.NoHand)) return; // that's "No weapon here"
        if (frame.HandX == x && frame.HandY == y) return;
        (frame.HandX, frame.HandY) = (x, y);
        Redraw();
        HandMoved?.Invoke();
    }

    private static List<SpriteEntry> WeaponSprites() =>
        ProjectSession.Sprites.Groups.Where(g => g.Name == SpriteDatabase.WeaponInHandGroupName).SelectMany(g => g.Sprites).Where(s => s.Frames.Count > 0).ToList();

    /// <summary>A sprite named for its direction (Man Walking Left): that facing. Else south.</summary>
    private static SpriteDirection GuessFacing(string name)
    {
        var words = name.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        bool Has(params string[] any) => words.Any(w => any.Any(a => w.Equals(a, StringComparison.OrdinalIgnoreCase)));
        if (Has("Left", "West")) return SpriteDirection.West;
        if (Has("Right", "East")) return SpriteDirection.East;
        if (Has("Up", "North")) return SpriteDirection.North;
        return SpriteDirection.South;
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        _weaponImage = null;
        if (_frameIndex < 0 || _frameIndex >= _sprite.Frames.Count) { _info.Text = "No frame."; return; }
        var frame = _sprite.Frames[_frameIndex];
        if (FrameCache.Get(frame.FilePath) is not { } body) { _info.Text = "This frame has no picture yet."; return; }

        // The body as the game draws it: fitted in the box its first frame sets (SpriteSizing), in the middle.
        var first = FrameCache.Get(_sprite.Frames[0].FilePath) ?? body;
        var (boxW, boxH) = SpriteSizing.Quantize(first.PixelWidth, first.PixelHeight);
        double scale = Math.Min(boxW / (double)Math.Max(1, body.PixelWidth), boxH / (double)Math.Max(1, body.PixelHeight));
        double insetX = (boxW - body.PixelWidth * scale) / 2, insetY = (boxH - body.PixelHeight * scale) / 2;

        var settings = ProjectSession.SettingsProvider?.Invoke()?.WeaponInHand ?? new WeaponInHandSettings();
        var (held, facing, attacking) = Choice;
        var (weaponFrame, mirrored) = WeaponInHandLook.FrameFor(settings, frame, _frameIndex, _sprite.Frames.Count, facing, attacking);
        var heldFrame = held?.Frames[Math.Clamp(weaponFrame, 0, held.Frames.Count - 1)];
        var weaponPicture = heldFrame is null ? null : FrameCache.Get(heldFrame.FilePath);

        // Room round the box for a weapon reaching out of it.
        double pad = weaponPicture is null ? 4 : Math.Max(weaponPicture.PixelWidth, weaponPicture.PixelHeight) * scale + 2;
        _canvas.Width = boxW + pad * 2;
        _canvas.Height = boxH + pad * 2;

        var box = new Rectangle { Width = boxW, Height = boxH, Stroke = Brushes.Gray, StrokeThickness = 0.15, StrokeDashArray = new DoubleCollection { 4, 4 } };
        Canvas.SetLeft(box, pad);
        Canvas.SetTop(box, pad);
        _canvas.Children.Add(box);

        var bodyImage = new Image { Source = body, Stretch = Stretch.Fill, Width = body.PixelWidth * scale, Height = body.PixelHeight * scale };
        (_bodyLeft, _bodyTop, _scale, _bodyWidth, _bodyHeight) = (pad + insetX, pad + insetY, scale, body.PixelWidth, body.PixelHeight);
        _reach = Math.Floor(Math.Min(_bodyLeft, _bodyTop) / scale);
        Canvas.SetLeft(bodyImage, pad + insetX);
        Canvas.SetTop(bodyImage, pad + insetY);
        Panel.SetZIndex(bodyImage, 1);
        _canvas.Children.Add(bodyImage);

        string where = $"Frame {_frameIndex + 1} / {_sprite.Frames.Count}";
        if (held is null) { _info.Text = $"{where}. No weapon to show: draw one in the Sprite Editor's \"{SpriteDatabase.WeaponInHandGroupName}\" group (with a grip point on each frame)."; return; }
        if (!frame.HasHand) { _info.Text = $"{where}: no hand point (No weapon here) - the game shows no weapon in this frame."; return; }
        if (weaponPicture is null || heldFrame is null) { _info.Text = $"{where}: {held.Name}'s frame {weaponFrame + 1} has no picture yet."; return; }

        double handX = pad + insetX + frame.HandX * scale, handY = pad + insetY + frame.HandY * scale;
        double gripX = (heldFrame.GripX >= 0 ? heldFrame.GripX : weaponPicture.PixelWidth / 2.0) * scale;
        double gripY = (heldFrame.GripY >= 0 ? heldFrame.GripY : weaponPicture.PixelHeight / 2.0) * scale;
        var weaponImage = new Image
        {
            Source = weaponPicture,
            Stretch = Stretch.Fill,
            Width = weaponPicture.PixelWidth * scale,
            Height = weaponPicture.PixelHeight * scale,
            RenderTransform = mirrored ? WeaponInHandLook.MirrorAboutGrip(gripX, gripY) : Transform.Identity
        };
        Canvas.SetLeft(weaponImage, handX - gripX);
        Canvas.SetTop(weaponImage, handY - gripY);
        Panel.SetZIndex(weaponImage, frame.WeaponBehind ? 0 : 2);
        weaponImage.Cursor = System.Windows.Input.Cursors.SizeAll;
        _canvas.Children.Add(weaponImage);
        _weaponImage = weaponImage;

        var names = SpriteDatabase.WeaponInHandFrameNames(settings.Smooth, settings.SwingFrames);
        string shown = weaponFrame >= 0 && weaponFrame < names.Length ? names[weaponFrame] : $"frame {weaponFrame + 1}";
        _info.Text = $"{where}: {held.Name} - {shown}" +
            (frame.WeaponFrame < 0 ? " (Automatic)" : "") +
            (mirrored ? ", mirrored (facing east or west)" : "") +
            (frame.WeaponBehind ? ", behind the body" : "") +
            (WeaponInHandLook.WeaponBounds(settings, _sprite, _frameIndex, held, facing, attacking) is { } reach && !new Rect(0, 0, body.PixelWidth, body.PixelHeight).Contains(reach) ? ". It reaches out of the frame" : "") +
            (heldFrame.GripX < 0 ? ". This weapon frame has no grip point: it's held by its middle." : ".");
    }
}
