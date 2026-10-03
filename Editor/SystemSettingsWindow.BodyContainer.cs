using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// System Settings > Body Container: the one container every dead body
/// opens as. No world sprite (a body looks like the dead character's Body
/// pose); just its paperdoll picture from the Sprite Editor, the frame its
/// belongings lie within (drag to move, drag the corner to size - as in the
/// Container Editor), and its sounds. Exult's rule: take out, never put in.
/// </summary>
public partial class SystemSettingsWindow
{
    private sealed record SpriteOption(string Group, SpriteEntry Sprite)
    {
        public override string ToString() => $"{Group} / {Sprite.Name}";
    }

    private UIElement BuildBodyContainerTab()
    {
        var body = _settings.BodyContainer;
        body.IsBodyContainer = true;
        if (body.Frame.Points.Count == 0) body.Frame.Points = new List<BookPoint> { new(60, 45), new(260, 45), new(260, 195), new(60, 195) };

        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "Every dead character's body opens as this container. It has no world sprite - in the world a body is the character's Body pose " +
                   "(its weight, volume and Too Heavy are set per character in Character Editor > Health). Choose the paperdoll shown when it's opened, " +
                   "then drag the frame (where its belongings lie) to move it, or its corner to size it. As in Exult, things can be taken out of a body but never put back in.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        var spriteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        spriteRow.Children.Add(new TextBlock { Text = "Paperdoll:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        var options = _sprites.Groups.SelectMany(g => g.Sprites.Select(s => new SpriteOption(g.Name, s))).ToList();
        var spriteCombo = new ComboBox { Width = 300 };
        spriteCombo.Items.Add("(None)");
        foreach (var option in options) spriteCombo.Items.Add(option);
        spriteCombo.SelectedItem = options.FirstOrDefault(o => o.Group == body.PaperDollSpriteGroup && o.Sprite.Name == body.PaperDollSpriteName)
            ?? options.FirstOrDefault(o => o.Sprite.Name == body.PaperDollSpriteName) as object ?? "(None)";
        spriteRow.Children.Add(spriteCombo);
        panel.Children.Add(spriteRow);

        panel.Children.Add(SoundRow("Open sound:", () => body.OpenSoundResourceId, v => body.OpenSoundResourceId = v));
        panel.Children.Add(SoundRow("Close sound:", () => body.CloseSoundResourceId, v => body.CloseSoundResourceId = v));

        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        var host = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = canvas, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 4) };
        var frameInfo = new TextBlock { Opacity = 0.75 };
        panel.Children.Add(host);
        panel.Children.Add(frameInfo);

        var image = new Image { Stretch = Stretch.None };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var rect = new Rectangle { Stroke = Brushes.Gold, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(40, 255, 215, 0)), Cursor = Cursors.SizeAll };
        var handle = new Rectangle { Width = 10, Height = 10, Fill = Brushes.Gold, Stroke = Brushes.Black, Cursor = Cursors.SizeNWSE };
        canvas.Children.Add(image);
        canvas.Children.Add(rect);
        canvas.Children.Add(handle);

        (double X, double Y, double W, double H) Bounds() { var b = body.GetFrameBounds(); return (b.X, b.Y, b.Width, b.Height); }
        void SetBounds(double x, double y, double w, double h)
        {
            body.Frame.Points = new List<BookPoint> { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h) };
            Redraw();
        }
        void Redraw()
        {
            var (x, y, w, h) = Bounds();
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); rect.Width = Math.Max(1, w); rect.Height = Math.Max(1, h);
            Canvas.SetLeft(handle, x + w - 5); Canvas.SetTop(handle, y + h - 5);
            frameInfo.Text = $"Frame: {x:0}, {y:0} - {w:0} x {h:0} px";
        }
        void LoadPicture()
        {
            var sprite = spriteCombo.SelectedItem is SpriteOption o ? o.Sprite : null;
            image.Source = ProjectSession.FirstFrame(sprite);
            double width = image.Source?.Width ?? 320, height = image.Source?.Height ?? 240;
            canvas.Width = Math.Max(width, 120);
            canvas.Height = Math.Max(height, 90);
            Redraw();
        }
        spriteCombo.SelectionChanged += (_, _) =>
        {
            if (spriteCombo.SelectedItem is SpriteOption o) { body.PaperDollSpriteGroup = o.Group; body.PaperDollSpriteName = o.Sprite.Name; }
            else { body.PaperDollSpriteGroup = string.Empty; body.PaperDollSpriteName = string.Empty; }
            LoadPicture();
        };

        // Drag to move / resize - captured on the canvas, which is never rebuilt.
        Point start = default;
        (double X, double Y, double W, double H) startBounds = default;
        bool resizing = false, dragging = false;
        rect.MouseLeftButtonDown += (_, e) => { dragging = true; resizing = false; start = e.GetPosition(canvas); startBounds = Bounds(); canvas.CaptureMouse(); e.Handled = true; };
        handle.MouseLeftButtonDown += (_, e) => { dragging = true; resizing = true; start = e.GetPosition(canvas); startBounds = Bounds(); canvas.CaptureMouse(); e.Handled = true; };
        canvas.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(canvas);
            double dx = Math.Round(p.X - start.X), dy = Math.Round(p.Y - start.Y);
            if (resizing) SetBounds(startBounds.X, startBounds.Y, Math.Max(8, startBounds.W + dx), Math.Max(8, startBounds.H + dy));
            else SetBounds(Math.Clamp(startBounds.X + dx, 0, Math.Max(0, canvas.Width - startBounds.W)), Math.Clamp(startBounds.Y + dy, 0, Math.Max(0, canvas.Height - startBounds.H)), startBounds.W, startBounds.H);
        };
        canvas.MouseLeftButtonUp += (_, _) => { dragging = false; canvas.ReleaseMouseCapture(); };

        LoadPicture();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private FrameworkElement SoundRow(string label, Func<int> get, Action<int> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox { Width = 300, DisplayMemberPath = nameof(ResourceEntry.Name) };
        var sounds = new List<ResourceEntry> { new() { Id = -1, Name = "(None)" } };
        sounds.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        combo.ItemsSource = sounds; // its own list (shared lists make WPF combos change each other)
        combo.SelectedItem = sounds.FirstOrDefault(s => s.Id == get()) ?? sounds[0];
        combo.SelectionChanged += (_, _) => set((combo.SelectedItem as ResourceEntry)?.Id ?? -1);
        row.Children.Add(combo);
        return row;
    }
}
