// PaperDoll Editor > a Slot > Show on the object > Place on the object...: click on the object's picture, enlarged, where the slot's item goes.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The object (one that opens this paperdoll) drawn large; every slot shown on it marked where its item goes, with the item's look;
/// a click puts this slot's item there (its centre on the click), the arrow keys nudge it a pixel, OK keeps it.
/// </summary>
internal sealed class ObjectSpotPickerWindow : Window
{
    private readonly DollDesign _design;
    private readonly DollPart _slot;
    private readonly ObjectDatabase _objects;
    private readonly SpriteDatabase _sprites;
    private readonly ComboBox _objectCombo = new() { Width = 260, DisplayMemberPath = nameof(WorldObject.Name), Margin = new Thickness(6, 0, 0, 0) };
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)) };
    private readonly TextBlock _where = new() { Margin = new Thickness(0, 6, 0, 0) };
    private int _x, _y;
    private const double Zoom = 8;

    private ObjectSpotPickerWindow(Window owner, DollDesign design, DollPart slot, ObjectDatabase objects, SpriteDatabase sprites)
    {
        Owner = owner;
        _design = design;
        _slot = slot;
        _objects = objects;
        _sprites = sprites;
        _x = slot.ObjectX;
        _y = slot.ObjectY;
        Title = $"Where \"{slot.Name}\" goes on the object";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        var users = objects.Objects.Where(o => o.DollDesignId == design.Id).ToList();
        _objectCombo.ItemsSource = users.Count > 0 ? users : objects.Objects;
        _objectCombo.SelectedIndex = 0;
        _objectCombo.SelectionChanged += (_, _) => Draw();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = users.Count > 0 ? "The object:" : "No object opens this paperdoll yet - show it on:", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(_objectCombo);
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => { slot.ObjectX = _x; slot.ObjectY = _y; DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var body = new StackPanel { Margin = new Thickness(10) };
        body.Children.Add(top);
        body.Children.Add(new TextBlock { Text = "Click where the item's centre goes (the arrow keys move it a pixel). The other slots shown on the object are drawn faded.", Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 });
        body.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = _canvas });
        body.Children.Add(_where);
        body.Children.Add(buttons);
        Content = body;
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            var size = ItemSize(_slot);
            _x = (int)Math.Floor(p.X / Zoom - size.Width / 2);
            _y = (int)Math.Floor(p.Y / Zoom - size.Height / 2);
            Draw();
        };
        PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Left: _x--; break;
                case Key.Right: _x++; break;
                case Key.Up: _y--; break;
                case Key.Down: _y++; break;
                default: return;
            }
            e.Handled = true;
            Draw();
        };
        Draw();
    }

    public static bool Pick(Window owner, DollDesign design, DollPart slot, ObjectDatabase objects, SpriteDatabase sprites) =>
        new ObjectSpotPickerWindow(owner, design, slot, objects, sprites).ShowDialog() == true;

    /// <summary>The look of what goes in a slot on the object: the first item it takes (its look there, else its own), else its first starting item.</summary>
    private SpriteEntry? ItemLook(DollPart slot)
    {
        if (slot.Accepts.FirstOrDefault() is { } fit)
            return DollLook.SpriteOf(fit.ObjectSprite, _sprites) ?? OwnLook(fit.Item);
        return slot.StartsWith.FirstOrDefault() is { } start ? OwnLook(start) : null;
    }

    private SpriteEntry? OwnLook(ItemReference item) =>
        item.Category == ReferencedItemCategory.Object && _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId) is { } obj ? ObjectLooks.SpriteFor(obj, new ObjectState(false, false, false, false, 1, SpriteDirection.South), _sprites) : null;

    private Size ItemSize(DollPart slot) => DollLook.Frame(ItemLook(slot)) is { } f ? new Size(f.PixelWidth, f.PixelHeight) : new Size(4, 4);

    private void Draw()
    {
        _canvas.Children.Clear();
        var obj = _objectCombo.SelectedItem as WorldObject;
        var body = obj is null ? null : DollLook.Frame(ObjectLooks.SpriteFor(obj, new ObjectState(false, false, false, false, 1, SpriteDirection.South), _sprites));
        double w = (body?.PixelWidth ?? 32) * Zoom, h = (body?.PixelHeight ?? 32) * Zoom;
        _canvas.Width = w + 2 * Zoom * 8;
        _canvas.Height = h + 2 * Zoom * 8;
        double ox = Zoom * 8, oy = Zoom * 8; // room round it: an item may stick out
        if (body is not null) Add(new Image { Source = body, Width = w, Height = h, Stretch = Stretch.Fill }, ox, oy, 1);
        _canvas.Children.Add(new Rectangle { Width = w, Height = h, Stroke = Brushes.DimGray, StrokeDashArray = new DoubleCollection { 2, 2 }, IsHitTestVisible = false });
        Canvas.SetLeft(_canvas.Children[^1], ox);
        Canvas.SetTop(_canvas.Children[^1], oy);
        foreach (var part in _design.Parts.Where(p => p.Kind == DollPartKind.Slot && (p.ShowOnObject || ReferenceEquals(p, _slot))))
        {
            bool mine = ReferenceEquals(part, _slot);
            int px = mine ? _x : part.ObjectX, py = mine ? _y : part.ObjectY;
            var size = ItemSize(part);
            for (int i = 0; i < Math.Max(1, part.Capacity); i++)
            {
                double x = ox + (px + i * part.ObjectStepX) * Zoom, y = oy + (py + i * part.ObjectStepY) * Zoom;
                if (DollLook.Frame(ItemLook(part)) is { } look) Add(new Image { Source = look, Width = size.Width * Zoom, Height = size.Height * Zoom, Stretch = Stretch.Fill }, x, y, mine ? 1 : 0.4);
                Add(new Rectangle { Width = size.Width * Zoom, Height = size.Height * Zoom, Stroke = mine ? Brushes.Yellow : Brushes.Gray, StrokeThickness = mine ? 2 : 1 }, x, y, 1);
                if (part.ObjectStepX == 0 && part.ObjectStepY == 0) break; // all in one spot
            }
        }
        _where.Text = $"\"{_slot.Name}\": X {_x}, Y {_y} on the object's picture (its top-left pixel is 0, 0).";
    }

    private void Add(FrameworkElement e, double x, double y, double opacity)
    {
        e.Opacity = opacity;
        e.IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(e, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        _canvas.Children.Add(e);
    }
}
