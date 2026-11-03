using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The PaperDoll Editor's form controls, drawn the same in the editor and in
/// play: buttons with a caption, text boxes, check boxes, radio buttons,
/// drop-down lists, list boxes and progress bars. Each one is its sprites
/// (its looks) when it has them, else a plain look of its own, so a form
/// works before any art is made. Drawn at the paperdoll's own pixels.
/// </summary>
internal static class DollControls
{
    /// <summary>A control as it is now: its look, value, typed text, whether it's being typed in / open.</summary>
    public readonly record struct Shown(int Look, int Value, string Text, bool Typing, bool Open);

    public static Shown StartOf(DollPart part) =>
        new(part.Kind is DollPartKind.CheckBox or DollPartKind.RadioButton ? (part.StartValue != 0 ? 1 : 0) : part.StartLook, part.StartValue, part.Kind == DollPartKind.TextBox ? part.Text : string.Empty, false, false);

    private static readonly Brush PlainFace = Frozen(Color.FromRgb(0x3A, 0x3A, 0x42));
    private static readonly Brush PlainPressed = Frozen(Color.FromRgb(0x26, 0x26, 0x2C));
    private static readonly Brush PlainBox = Frozen(Color.FromRgb(0x1C, 0x1C, 0x20));
    private static readonly Brush PlainEdge = Frozen(Color.FromRgb(0x8A, 0x8A, 0x94));
    private static readonly Brush PlainTyping = Frozen(Color.FromRgb(0xE0, 0xB8, 0x40));
    private static readonly Brush PlainPicked = Frozen(Color.FromArgb(90, 255, 255, 255));
    private static readonly Brush PlainFill = Frozen(Color.FromRgb(0x4C, 0xA8, 0x50));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>One row of a list, in the part's font.</summary>
    public static double RowHeight(DollPart part, SpriteDatabase sprites) =>
        (DollLook.TextPicture(part, "Ag", sprites)?.PixelHeight ?? 10 * Math.Max(1, part.TextScale)) + 2;

    /// <summary>A check box's or radio button's box: its look's sprite, else 10 x 10.</summary>
    public static Size BoxSize(DollPart part, int look, SpriteDatabase sprites) =>
        DollLook.Frame(DollLook.LookSprite(part, look, sprites) ?? DollLook.LookSprite(part, 0, sprites)) is { } bitmap ? new Size(bitmap.PixelWidth, bitmap.PixelHeight) : new Size(10, 10);

    /// <summary>The control's size (a drop-down's closed box; a check box with its words).</summary>
    public static Size SizeOf(DollPart part, Shown shown, SpriteDatabase sprites)
    {
        if (part.Kind is DollPartKind.CheckBox or DollPartKind.RadioButton)
        {
            var box = BoxSize(part, shown.Look, sprites);
            double words = Words(part, part.Text, sprites) is { } w ? w.Width + 4 : 0;
            return new Size(box.Width + words, Math.Max(box.Height, RowHeight(part, sprites)));
        }
        return DollLook.SizeOf(part, shown.Look, sprites);
    }

    /// <summary>The words of a control in its font (a plain one with no font in the project); null with no words.</summary>
    public static FrameworkElement? Words(DollPart part, string text, SpriteDatabase sprites, double opacity = 1)
    {
        if (string.IsNullOrEmpty(text)) return null;
        FrameworkElement words;
        if (DollLook.TextPicture(part, text, sprites) is { } picture)
        {
            var image = new Image { Source = picture, Width = picture.PixelWidth, Height = picture.PixelHeight, Stretch = Stretch.None };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            words = image;
        }
        else
        {
            var block = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 8 * Math.Max(1, part.TextScale) };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            block.Width = block.DesiredSize.Width;
            block.Height = block.DesiredSize.Height;
            words = block;
        }
        words.IsHitTestVisible = false;
        words.Opacity = opacity;
        return words;
    }

    private static void Place(Canvas holder, UIElement? element, double x, double y)
    {
        if (element is null) return;
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        holder.Children.Add(element);
    }

    private static Border Plain(double width, double height, Brush face, Brush edge) =>
        new() { Width = width, Height = height, Background = face, BorderBrush = edge, BorderThickness = new Thickness(1), IsHitTestVisible = false };

    /// <summary>
    /// The control as it is now. <paramref name="look"/> turns a look's sprite
    /// into its picture (animated in play, a still frame in the editor);
    /// <paramref name="barValue"/> is a progress bar's value (its own, or the slider's it follows).
    /// </summary>
    /// <param name="lostFrom">Icons: the value they had just before a loss - the hearts lost flash (Flashes the hearts lost).</param>
    public static FrameworkElement Build(DollPart part, Shown shown, SpriteDatabase sprites, Func<SpriteEntry, FrameworkElement> look, int? barValue = null, int? barMax = null, int? lostFrom = null)
    {
        var size = SizeOf(part, shown, sprites);
        var holder = new Canvas { Width = size.Width, Height = size.Height, Background = Brushes.Transparent };
        SpriteEntry? Sprite(int index) => DollLook.LookSprite(part, index, sprites);
        double row = RowHeight(part, sprites);
        double Middle(FrameworkElement? words, double height) => words is null ? 0 : Math.Max(0, (height - words.Height) / 2);

        switch (part.Kind)
        {
            case var kind when DollPart.IsButtonKind(kind):
            {
                bool pressed = shown.Look == 1;
                if ((Sprite(shown.Look) ?? Sprite(0)) is { } sprite) Place(holder, look(sprite), 0, 0);
                else Place(holder, Plain(size.Width, size.Height, pressed ? PlainPressed : PlainFace, PlainEdge), 0, 0);
                var caption = Words(part, part.Text, sprites);
                double nudge = pressed ? 1 : 0; // a pressed caption sinks a pixel, as a real button's does
                if (caption is not null) Place(holder, caption, Math.Max(0, (size.Width - caption.Width) / 2) + nudge, Middle(caption, size.Height) + nudge);
                break;
            }
            case DollPartKind.TextBox:
            {
                var behind = shown.Typing ? Sprite(1) ?? Sprite(0) : Sprite(0);
                if (behind is not null) Place(holder, look(behind), 0, 0);
                else Place(holder, Plain(size.Width, size.Height, PlainBox, shown.Typing ? PlainTyping : PlainEdge), 0, 0);
                var words = Words(part, shown.Text + (shown.Typing ? "_" : ""), sprites);
                Place(holder, words, 3, Middle(words, size.Height));
                holder.ClipToBounds = true;
                break;
            }
            case DollPartKind.CheckBox or DollPartKind.RadioButton:
            {
                var box = BoxSize(part, shown.Look, sprites);
                double top = Math.Max(0, (size.Height - box.Height) / 2);
                if ((Sprite(shown.Look) ?? Sprite(0)) is { } sprite) Place(holder, look(sprite), 0, top);
                else
                {
                    bool radio = part.Kind == DollPartKind.RadioButton;
                    var frame = Plain(box.Width, box.Height, PlainBox, PlainEdge);
                    if (radio) frame.CornerRadius = new CornerRadius(box.Width / 2);
                    Place(holder, frame, 0, top);
                    if (shown.Look == 1)
                    {
                        var mark = new Border { Width = box.Width - 4, Height = box.Height - 4, Background = Brushes.White, IsHitTestVisible = false, CornerRadius = radio ? new CornerRadius((box.Width - 4) / 2) : new CornerRadius(0) };
                        Place(holder, mark, 2, top + 2);
                    }
                }
                var words = Words(part, part.Text, sprites);
                Place(holder, words, box.Width + 4, Middle(words, size.Height));
                break;
            }
            case DollPartKind.DropDown:
            {
                if (Sprite(0) is { } box) Place(holder, look(box), 0, 0);
                else
                {
                    Place(holder, Plain(size.Width, size.Height, PlainBox, PlainEdge), 0, 0);
                    var arrow = new TextBlock { Text = "▼", Foreground = PlainEdge, FontSize = Math.Max(6, size.Height - 6), IsHitTestVisible = false };
                    Place(holder, arrow, size.Width - Math.Max(6, size.Height - 6) - 2, 0);
                }
                string picked = shown.Value >= 1 && shown.Value <= part.Options.Count ? part.Options[shown.Value - 1] : string.Empty;
                var words = Words(part, picked, sprites);
                Place(holder, words, 3, Middle(words, size.Height));
                if (shown.Open) // the choices, under the box
                {
                    double height = Math.Max(1, part.Options.Count) * row;
                    var list = new Canvas { Width = size.Width, Height = height };
                    if (Sprite(1) is { } behind) Place(list, look(behind), 0, 0);
                    else Place(list, Plain(size.Width, height, PlainBox, PlainEdge), 0, 0);
                    for (int i = 0; i < part.Options.Count; i++)
                    {
                        if (i + 1 == shown.Value) Place(list, Sprite(2) is { } mark ? look(mark) : new Border { Width = size.Width, Height = row, Background = PlainPicked, IsHitTestVisible = false }, 0, i * row);
                        Place(list, Words(part, part.Options[i], sprites), 3, i * row + 1);
                    }
                    list.IsHitTestVisible = false; // the holder takes the click; the row is worked out from where
                    Place(holder, list, 0, size.Height);
                    holder.Height = size.Height + height;
                }
                break;
            }
            case DollPartKind.ListBox:
            {
                if (Sprite(0) is { } behind) Place(holder, look(behind), 0, 0);
                else Place(holder, Plain(size.Width, size.Height, PlainBox, PlainEdge), 0, 0);
                int rows = Math.Max(1, (int)(size.Height / row));
                int first = FirstRow(part, shown.Value, rows);
                for (int i = first; i < Math.Min(part.Options.Count, first + rows); i++)
                {
                    double y = (i - first) * row;
                    if (i + 1 == shown.Value) Place(holder, Sprite(1) is { } mark ? look(mark) : new Border { Width = size.Width, Height = row, Background = PlainPicked, IsHitTestVisible = false }, 0, y);
                    Place(holder, Words(part, part.Options[i], sprites), 3, y + 1);
                }
                holder.ClipToBounds = true;
                break;
            }
            case DollPartKind.ProgressBar:
            {
                int value = barValue ?? shown.Value;
                int max = barMax ?? part.Max;
                double t = Math.Clamp((value - part.Min) / (double)Math.Max(1, max - part.Min), 0, 1);
                if (part.BarStyle == DollGaugeStyle.Icons) return Icons(part, value - part.Min, max - part.Min, Sprite(0), lostFrom is { } from ? from - part.Min : null);
                FrameworkElement Skin(SpriteEntry sprite) => Fitted(part, sprite, size, look);
                if (part.BarStyle == DollGaugeStyle.Pie)
                    return Pie(holder, size, t,
                        Sprite(0) is { } emptyPie ? Skin(emptyPie) : new Ellipse { Width = size.Width, Height = size.Height, Fill = EmptyBrush(part), Stroke = PlainEdge, IsHitTestVisible = false },
                        Sprite(1) is { } fullPie ? Skin(fullPie) : new Ellipse { Width = size.Width, Height = size.Height, Fill = FillBrush(part), IsHitTestVisible = false });
                if (Sprite(0) is { } empty) Place(holder, Skin(empty), 0, 0);
                else Place(holder, Plain(size.Width, size.Height, EmptyBrush(part), PlainEdge), 0, 0);
                var full = Sprite(1) is { } fullSprite ? Skin(fullSprite) : new Border { Width = size.Width, Height = size.Height, Background = FillBrush(part), IsHitTestVisible = false };
                // Only as much of the full bar as the value reaches: left to right (or right to left), or Upright bottom up (or top down).
                double w = size.Width, h = size.Height;
                full.Clip = new RectangleGeometry((part.Vertical, part.ReverseFill) switch
                {
                    (true, false) => new Rect(0, h * (1 - t), w, h * t),
                    (true, true) => new Rect(0, 0, w, h * t),
                    (false, true) => new Rect(w * (1 - t), 0, w * t, h),
                    _ => new Rect(0, 0, w * t, h)
                });
                Place(holder, full, 0, 0);
                holder.ClipToBounds = true;
                break;
            }
        }
        return holder;
    }

    private static Brush FillBrush(DollPart part) => PaperdollReadouts.Brush(part.FillColor) ?? PlainFill;
    private static Brush EmptyBrush(DollPart part) => PaperdollReadouts.Brush(part.EmptyColor) ?? PlainBox;

    /// <summary>A skin (a look's sprite) fitted to the bar: as drawn, stretched to its size, or tiled across it (its first frame).</summary>
    private static FrameworkElement Fitted(DollPart part, SpriteEntry sprite, Size size, Func<SpriteEntry, FrameworkElement> look)
    {
        switch (part.SkinFit)
        {
            case DollSkinFit.Stretch:
            {
                var box = new Viewbox { Width = size.Width, Height = size.Height, Stretch = Stretch.Fill, Child = look(sprite), IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(box, BitmapScalingMode.NearestNeighbor);
                return box;
            }
            case DollSkinFit.Tile when DollLook.Frame(sprite) is { } tile:
            {
                var brush = new ImageBrush(tile) { TileMode = TileMode.Tile, Stretch = Stretch.None, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, tile.PixelWidth, tile.PixelHeight) };
                var area = new Rectangle { Width = size.Width, Height = size.Height, Fill = brush, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(area, BitmapScalingMode.NearestNeighbor);
                return area;
            }
            default:
                return look(sprite);
        }
    }

    /// <summary>The size icons take: as many as the range holds (Max - Min, its own Max), at the icon's size.</summary>
    public static Size IconsSize(DollPart part, SpriteEntry? icon)
    {
        int per = Math.Max(1, part.IconValue), count = Math.Clamp((int)Math.Ceiling(Math.Max(0, part.Max - part.Min) / (double)per), 1, 200);
        int perRow = Math.Max(1, part.IconsPerRow), gap = Math.Max(0, part.IconGap);
        var first = DollLook.Frame(icon, 0);
        double w = first?.PixelWidth ?? 8, h = first?.PixelHeight ?? 8;
        return new Size(Math.Max(4, Math.Min(count, perRow) * (w + gap) - gap), Math.Max(4, (int)Math.Ceiling(count / (double)perRow) * (h + gap) - gap));
    }

    /// <summary>
    /// Icons (hearts, Zelda's way): one icon per IconValue of the range, IconsPerRow to a row. The icon's sprite has its frames
    /// from full to empty - 2 frames: full, empty; 3: full, half, empty; 5: quarters - each icon showing as much as is in it.
    /// No sprite: dots in its Full / Empty colours. Beats: the last one holding something pulses while low. Flashes: the
    /// ones just lost blink as they were before emptying.
    /// </summary>
    private static FrameworkElement Icons(DollPart part, int value, int range, SpriteEntry? icon, int? lostFrom)
    {
        int per = Math.Max(1, part.IconValue), count = Math.Clamp((int)Math.Ceiling(Math.Max(0, range) / (double)per), 0, 200);
        int perRow = Math.Max(1, part.IconsPerRow), gap = Math.Max(0, part.IconGap);
        var first = DollLook.Frame(icon, 0);
        double w = first?.PixelWidth ?? 8, h = first?.PixelHeight ?? 8;
        int frames = icon?.Frames.Count ?? 0;
        var holder = new Canvas
        {
            Width = Math.Max(1, Math.Min(count, perRow) * (w + gap) - gap),
            Height = Math.Max(1, (int)Math.Ceiling(count / (double)perRow) * (h + gap) - gap),
            Background = Brushes.Transparent, IsHitTestVisible = false
        };
        // How the icon looks this full; null: not drawn (one frame only, and empty).
        FrameworkElement? IconAt(double inIt)
        {
            if (frames > 0)
            {
                if (frames == 1 && inIt <= 0) return null; // one frame only: an empty one isn't drawn
                int frame = frames == 1 ? 0 : (int)Math.Round((1 - inIt) * (frames - 1)); // frame 1 full ... the last empty
                var image = new Image { Source = DollLook.Frame(icon, frame), Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                return image;
            }
            var dot = new Grid { Width = w, Height = h, IsHitTestVisible = false };
            dot.Children.Add(new Ellipse { Fill = PaperdollReadouts.Brush(part.EmptyColor) ?? Brushes.DimGray });
            if (inIt > 0) dot.Children.Add(new Ellipse { Fill = PaperdollReadouts.Brush(part.FillColor) ?? Brushes.Crimson, Clip = new RectangleGeometry(new Rect(0, 0, w * inIt, h)) });
            return dot;
        }
        bool beating = part.IconsBeatBelow > 0 && range > 0 && value > 0 && value * 100.0 / range <= part.IconsBeatBelow;
        int lastHolding = (int)Math.Ceiling(value / (double)per) - 1;
        for (int i = 0; i < count; i++)
        {
            double inIt = Math.Clamp((value - i * per) / (double)per, 0, 1); // how full this one is
            double x = i % perRow * (w + gap), y = i / perRow * (h + gap);
            if (IconAt(inIt) is { } shown)
            {
                if (beating && i == lastHolding)
                {
                    var pulse = new ScaleTransform(1, 1, w / 2, h / 2);
                    shown.RenderTransform = pulse;
                    var beat = new System.Windows.Media.Animation.DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(220)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
                    pulse.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
                    pulse.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
                }
                Canvas.SetLeft(shown, x);
                Canvas.SetTop(shown, y);
                holder.Children.Add(shown);
            }
            // Just lost: as it was, blinking three times over it, then gone.
            if (part.IconsFlashOnLoss && lostFrom is { } from && from > value)
            {
                double before = Math.Clamp((from - i * per) / (double)per, 0, 1);
                if (before > inIt && IconAt(before) is { } was)
                {
                    was.Opacity = 0;
                    was.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
                        { AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3), FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
                    Canvas.SetLeft(was, x);
                    Canvas.SetTop(was, y);
                    holder.Children.Add(was);
                }
            }
        }
        return holder;
    }

    /// <summary>A pie: its empty look, and as much of its full look as the value reaches - from the top, clockwise.</summary>
    private static FrameworkElement Pie(Canvas holder, Size size, double t, FrameworkElement empty, FrameworkElement full)
    {
        holder.Children.Add(empty);
        if (t <= 0) return holder;
        if (t < 1)
        {
            double cx = size.Width / 2, cy = size.Height / 2, r = Math.Sqrt(cx * cx + cy * cy) + 1, angle = t * 2 * Math.PI;
            var figure = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
            figure.Segments.Add(new LineSegment(new Point(cx, cy - r), false));
            figure.Segments.Add(new ArcSegment(new Point(cx + r * Math.Sin(angle), cy - r * Math.Cos(angle)), new Size(r, r), 0, t > 0.5, SweepDirection.Clockwise, false));
            full.Clip = new PathGeometry(new[] { figure });
        }
        holder.Children.Add(full);
        return holder;
    }

    /// <summary>The first row a list box shows: from the top, scrolled just enough that the picked one is seen.</summary>
    public static int FirstRow(DollPart part, int value, int rows) => Math.Max(0, Math.Min(value, part.Options.Count) - rows);
}
