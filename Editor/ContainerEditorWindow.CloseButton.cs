using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Container Editor: the optional close button on the container's open view - off by default (Escape closes it, as
/// before). On: a sprite (frame 1 up, frame 2 while pressed; it closes when let go over it), where it is on the 320 x 240
/// view - typed, or dragged on the preview - and at its own size or in a 24 x 24 box.
/// </summary>
public partial class ContainerEditorWindow
{
    private sealed record CloseSpriteChoice(string Group, string Name, string Display);

    private UIElement BuildCloseButtonEditor(ContainerData container)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock { Text = "Close Button (optional)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        var details = new StackPanel { Margin = new Thickness(16, 2, 0, 0) };
        var on = new CheckBox { Content = "Add a close button to its open view", IsChecked = container.HasCloseButton };
        on.Checked += (_, _) => { container.HasCloseButton = true; details.IsEnabled = true; BuildFramePreview(container); };
        on.Unchecked += (_, _) => { container.HasCloseButton = false; details.IsEnabled = false; BuildFramePreview(container); };
        panel.Children.Add(on);
        panel.Children.Add(new TextBlock { Text = "Off: Escape closes it, as before. On: a click on the button closes it too (frame 1 up, frame 2 while held; it closes when let go over it). Drag it on the picture below, or type where.", Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });

        var choices = new List<CloseSpriteChoice> { new("", "", "(none - a plain X box)") };
        foreach (var group in _sprites.Groups.Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            choices.AddRange(group.Sprites.Select(s => new CloseSpriteChoice(group.Name, s.Name, $"{s.Name}   ({group.Name})")));
        var combo = new ComboBox { Width = 230, ItemsSource = choices, DisplayMemberPath = nameof(CloseSpriteChoice.Display) };
        combo.SelectedItem = choices.FirstOrDefault(c => c.Name == container.CloseButtonSpriteName && (c.Group == container.CloseButtonSpriteGroup || container.CloseButtonSpriteGroup.Length == 0)) ?? choices[0];
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not CloseSpriteChoice c) return;
            (container.CloseButtonSpriteGroup, container.CloseButtonSpriteName) = (c.Group, c.Name);
            BuildFramePreview(container);
        };
        var expand = new Button { Content = "Expand...", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(6, 0, 0, 0) };
        expand.Click += (_, _) =>
        {
            if (SpriteBrowserWindow.Pick(this, _sprites, SpriteFolderIdentity.Paperdoll, null, container.CloseButtonSpriteGroup, container.CloseButtonSpriteName) is not { } picked) return;
            var choice = choices.FirstOrDefault(c => c.Group == picked.Group && c.Name == picked.Name);
            if (choice is null) { choice = new CloseSpriteChoice(picked.Group, picked.Name, $"{picked.Name}   ({picked.Group})"); choices.Add(choice); combo.Items.Refresh(); }
            combo.SelectedItem = choice;
        };
        var spriteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        spriteRow.Children.Add(new TextBlock { Text = "Sprite:", Width = 50, VerticalAlignment = VerticalAlignment.Center });
        spriteRow.Children.Add(combo);
        spriteRow.Children.Add(expand);
        details.Children.Add(spriteRow);

        var placeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        TextBox Number(string label, Func<double> get, Action<double> set)
        {
            placeRow.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            var box = new TextBox { Width = 50, Text = get().ToString("0"), Margin = new Thickness(0, 0, 10, 0) };
            box.LostFocus += (_, _) => { if (double.TryParse(box.Text, out double v)) { set(Math.Clamp(v, 0, 320)); BuildFramePreview(container); } box.Text = get().ToString("0"); };
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter && double.TryParse(box.Text, out double v)) { set(Math.Clamp(v, 0, 320)); BuildFramePreview(container); } };
            placeRow.Children.Add(box);
            return box;
        }
        _closeXBox = Number("X:", () => container.CloseButtonX, v => container.CloseButtonX = v);
        _closeYBox = Number("Y:", () => container.CloseButtonY, v => container.CloseButtonY = v);
        details.Children.Add(placeRow);
        var own = new CheckBox { Content = "At its own size - growing as the container's picture does (else in a 24 x 24 box)", IsChecked = container.CloseButtonOwnSize, Margin = new Thickness(0, 2, 0, 0) };
        own.Checked += (_, _) => { container.CloseButtonOwnSize = true; BuildFramePreview(container); };
        own.Unchecked += (_, _) => { container.CloseButtonOwnSize = false; BuildFramePreview(container); };
        details.Children.Add(own);
        details.IsEnabled = container.HasCloseButton;
        panel.Children.Add(details);
        return panel;
    }

    private TextBox? _closeXBox, _closeYBox;

    /// <summary>The close button on the open-view preview, where it will be in play - dragged to move it.</summary>
    private void DrawCloseButtonPreview(ContainerData container)
    {
        if (!container.HasCloseButton) return;
        var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == container.CloseButtonSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == container.CloseButtonSpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == container.CloseButtonSpriteName);
        FrameworkElement look;
        // At its own size it grows as the container's picture is blown up to fill this 320 x 240 view - as in the game.
        var background = _sprites.Groups.FirstOrDefault(g => g.Name == container.PaperDollSpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == container.PaperDollSpriteName)
            ?? _sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == container.PaperDollSpriteName);
        double grow = DollLook.Frame(background) is { } bgPicture ? ContainerData.OpenViewScale(bgPicture.PixelWidth, bgPicture.PixelHeight) : 1.2;
        if (sprite is { Frames.Count: > 0 } && DollLook.Frame(sprite) is { } picture)
        {
            var image = new Image { Source = picture, Stretch = container.CloseButtonOwnSize ? Stretch.Fill : Stretch.Uniform,
                Width = container.CloseButtonOwnSize ? picture.PixelWidth * grow : 24, Height = container.CloseButtonOwnSize ? picture.PixelHeight * grow : 24 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            look = new Border { Child = image, BorderBrush = Brushes.Violet, BorderThickness = new Thickness(1) };
        }
        else look = new Border { Width = 24, Height = 24, BorderBrush = Brushes.Violet, BorderThickness = new Thickness(1), Background = Brushes.Black,
            Child = new TextBlock { Text = "X", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        look.Cursor = Cursors.SizeAll;
        look.ToolTip = "The close button - drag it where it goes";
        Canvas.SetLeft(look, container.CloseButtonX);
        Canvas.SetTop(look, container.CloseButtonY);
        Panel.SetZIndex(look, 20);
        Point? grab = null;
        look.MouseLeftButtonDown += (_, e) => { grab = e.GetPosition(look); look.CaptureMouse(); e.Handled = true; };
        look.MouseMove += (_, e) =>
        {
            if (grab is not { } g || e.LeftButton != MouseButtonState.Pressed) return;
            var at = e.GetPosition(FramePreviewCanvas);
            container.CloseButtonX = Math.Clamp(Math.Round(at.X - g.X), 0, 320);
            container.CloseButtonY = Math.Clamp(Math.Round(at.Y - g.Y), 0, 240);
            Canvas.SetLeft(look, container.CloseButtonX);
            Canvas.SetTop(look, container.CloseButtonY);
            if (_closeXBox is not null) _closeXBox.Text = container.CloseButtonX.ToString("0");
            if (_closeYBox is not null) _closeYBox.Text = container.CloseButtonY.ToString("0");
            e.Handled = true;
        };
        look.MouseLeftButtonUp += (_, e) => { grab = null; look.ReleaseMouseCapture(); e.Handled = true; };
        FramePreviewCanvas.Children.Add(look);
    }
}
