using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    public static FrameworkElement Build(DollPart part, Shown shown, SpriteDatabase sprites, Func<SpriteEntry, FrameworkElement> look, int? barValue = null)
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
                double t = Math.Clamp((value - part.Min) / (double)Math.Max(1, part.Max - part.Min), 0, 1);
                if (Sprite(0) is { } empty) Place(holder, look(empty), 0, 0);
                else Place(holder, Plain(size.Width, size.Height, PlainBox, PlainEdge), 0, 0);
                var full = Sprite(1) is { } fullSprite ? look(fullSprite) : new Border { Width = size.Width, Height = size.Height, Background = PlainFill, IsHitTestVisible = false };
                // Only as much of the full bar as the value reaches: left to right, or (Vertical) bottom up.
                full.Clip = part.Vertical
                    ? new RectangleGeometry(new Rect(0, size.Height * (1 - t), size.Width, size.Height * t))
                    : new RectangleGeometry(new Rect(0, 0, size.Width * t, size.Height));
                Place(holder, full, 0, 0);
                holder.ClipToBounds = true;
                break;
            }
        }
        return holder;
    }

    /// <summary>The first row a list box shows: from the top, scrolled just enough that the picked one is seen.</summary>
    public static int FirstRow(DollPart part, int value, int rows) => Math.Max(0, Math.Min(value, part.Options.Count) - rows);
}
