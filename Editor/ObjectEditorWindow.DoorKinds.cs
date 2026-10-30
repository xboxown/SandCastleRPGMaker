using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Door section's "How it opens" (DoorOpening): swing (Ultima VII), an animation, a slide or a fade, with what each needs;
/// double doors; opening by itself; and a preview - Open and Close play it as the game does (DoorMotion).
/// </summary>
public partial class ObjectEditorWindow
{
    private static readonly (DoorOpening Kind, string Label)[] DoorKinds =
    {
        (DoorOpening.Swing, "Swing - Ultima VII: the open sprite at its hinge, at once"),
        (DoorOpening.Animation, "Animation - plays an opening animation (double doors, void doors, anything drawn)"),
        (DoorOpening.Slide, "Slide - glides away (a sliding door, a portcullis, a sci-fi door)"),
        (DoorOpening.Fade, "Fade - fades out (a laser, a force field, an energy door)"),
    };

    private void BuildDoorOpening(WorldObject obj, StackPanel body)
    {
        var d = obj.Door;
        var combo = new ComboBox { Width = 470, ItemsSource = DoorKinds.Select(k => k.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(DoorKinds, k => k.Kind == d.Opening)) };
        body.Children.Add(Labeled("How it opens", combo));

        var animation = new StackPanel();
        animation.Children.Add(SpritePicker("Opening animation (played once where the closed door is - then the open sprite shows)",
            () => (d.OpeningSprite.SpriteGroup, d.OpeningSprite.SpriteName), (g, n) => { d.OpeningSprite.SpriteGroup = g; d.OpeningSprite.SpriteName = n; }));
        animation.Children.Add(SpritePicker("Closing animation (optional - empty: the opening animation backwards)",
            () => (d.ClosingSprite.SpriteGroup, d.ClosingSprite.SpriteName), (g, n) => { d.ClosingSprite.SpriteGroup = g; d.ClosingSprite.SpriteName = n; }));
        animation.Children.Add(Note("Each frame's time is set in the Sprite Editor. A double door can be one picture of both doors, or two double doors (below)."));

        var slide = new WrapPanel();
        slide.Children.Add(NumberField("Slide X (px: - left, + right)", () => d.SlideX, v => d.SlideX = (int)v, "How far it moves sideways to open: a sliding door's width"));
        slide.Children.Add(NumberField("Slide Y (px: - up, + down)", () => d.SlideY, v => d.SlideY = (int)v, "How far it moves up or down to open: a portcullis rises (minus)"));
        slide.Children.Add(Check("Hidden past where it stands (into the wall or the ceiling)", () => d.SlideHidden, v => d.SlideHidden = v,
            "Ticked: what slides past where the closed door is drawn can't be seen. Unticked: the whole door is seen where it slides to"));

        var time = NumberField("Time (ms)", () => d.MotionMs, v => d.MotionMs = Math.Max(50, (int)v), "How long opening (and closing) takes");
        var fadeNote = Note("Fade: the door fades out and back in. Its open sprite (optional) is what stays when it's open - the posts of a laser door without the beam.");
        var slideNote = Note("Slide: the door itself slides away (the open sprite isn't used). A sliding door isn't opened by hand - not by a double-click, nor by people walking through: a lever, an event (Open Door, Set Door State), or \"Opens by itself\" opens it.");

        void Show()
        {
            var kind = d.Opening;
            animation.Visibility = kind == DoorOpening.Animation ? Visibility.Visible : Visibility.Collapsed;
            slide.Visibility = slideNote.Visibility = kind == DoorOpening.Slide ? Visibility.Visible : Visibility.Collapsed;
            fadeNote.Visibility = kind == DoorOpening.Fade ? Visibility.Visible : Visibility.Collapsed;
            time.Visibility = kind is DoorOpening.Slide or DoorOpening.Fade ? Visibility.Visible : Visibility.Collapsed;
        }
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { d.Opening = DoorKinds[combo.SelectedIndex].Kind; Show(); } };
        foreach (var part in new FrameworkElement[] { animation, slide, slideNote, time, fadeNote }) body.Children.Add(part);
        Show();

        var byHand = Check("Opens by hand (double-click, people walking through)", () => !d.NotByHand, v => d.NotByHand = !v,
            "Unticked: only a lever, a button, an event (Open Door, Set Door State) or \"Opens by itself\" opens it - a laser door with its button");
        body.Children.Add(byHand);
        void ShowByHand() => byHand.Visibility = d.Opening == DoorOpening.Slide ? Visibility.Collapsed : Visibility.Visible; // a sliding door never is
        combo.SelectionChanged += (_, _) => ShowByHand();
        ShowByHand();
        var together = new WrapPanel();
        together.Children.Add(Check("Double door - opens and closes with the double doors right next to it", () => d.OpensWithNeighbours, v => d.OpensWithNeighbours = v,
            "Place two (or more) side by side: opening one opens the others, closing one closes them"));
        body.Children.Add(together);
        var self = new WrapPanel();
        self.Children.Add(Check("Opens by itself when someone comes near", () => d.OpensByItself, v => d.OpensByItself = v, "A sci-fi door: anyone (the party, any character) near opens it; a locked one stays shut"));
        self.Children.Add(NumberField("within (cells)", () => d.OpensWithinCells, v => d.OpensWithinCells = Math.Max(0, (int)v)));
        self.Children.Add(NumberField("closes after nobody's near (ms)", () => d.StaysOpenMs, v => d.StaysOpenMs = Math.Max(0, (int)v)));
        body.Children.Add(self);
        body.Children.Add(BuildDoorPreview(obj));
    }

    /// <summary>The door, twice its size: Open and Close play it as the game does, with its settings as they are now.</summary>
    private FrameworkElement BuildDoorPreview(WorldObject obj)
    {
        const double zoom = 2, viewW = 360, viewH = 200;
        var outer = new Canvas { Width = viewW, Height = viewH, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left }; // over its buttons, not centred
        var stage = new Canvas { RenderTransform = new ScaleTransform(zoom, zoom) };
        outer.Children.Add(stage);
        double baseX = viewW / zoom / 2, baseY = viewH / zoom - 8;
        stage.Children.Add(new Line { X1 = 0, X2 = viewW / zoom, Y1 = baseY, Y2 = baseY, Stroke = Brushes.Gray, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 2, 3 } });
        var still = new Image { Stretch = Stretch.Fill };
        var moving = new Image { Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
        RenderOptions.SetBitmapScalingMode(still, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(moving, BitmapScalingMode.NearestNeighbor);
        stage.Children.Add(still);
        stage.Children.Add(moving);
        bool open = false;
        DispatcherTimer? timer = null;

        void Place(Image image, SpriteEntry? sprite, int frame)
        {
            image.RenderTransform = null;
            image.Clip = null;
            image.Opacity = 1;
            image.Source = sprite is { Frames.Count: > 0 } ? FrameCache.Get(sprite.Frames[Math.Clamp(frame, 0, sprite.Frames.Count - 1)].FilePath) : null;
            var (w, h) = image.Source is System.Windows.Media.Imaging.BitmapSource b ? SpriteSizing.Quantize(b.PixelWidth, b.PixelHeight) : (0, 0);
            image.Width = w;
            image.Height = h;
            Canvas.SetLeft(image, baseX - w / 2.0);
            Canvas.SetTop(image, baseY - h);
        }

        // The door as it stays: closed, or open as the game leaves it (DoorMotion.ApplyOpen).
        void ShowStill()
        {
            var d = obj.Door;
            var closed = ObjectLooks.Find(_sprites, obj.SpriteGroup, obj.SpriteName);
            var openLook = ObjectLooks.Find(_sprites, d.OpenSprite.SpriteGroup, d.OpenSprite.SpriteName);
            if (!open) { Place(still, closed, 0); return; }
            switch (d.Opening)
            {
                case DoorOpening.Swing:
                case DoorOpening.Animation:
                    Place(still, openLook ?? closed, 0);
                    break;
                case DoorOpening.Slide:
                    Place(still, closed, 0);
                    break;
                case DoorOpening.Fade:
                    Place(still, openLook ?? closed, 0);
                    if (openLook is not null) { DoorMotion.ApplyOpen(still, d); return; } // stays as it is
                    break;
            }
            DoorMotion.ApplyOpen(still, d);
        }

        void Play(bool opening)
        {
            timer?.Stop();
            moving.Visibility = Visibility.Collapsed;
            open = opening;
            ShowStill();
            var d = obj.Door;
            if (!DoorMotion.Moves(d) || DoorMotion.MovingPicture(obj, opening, _sprites) is not { } pic) return;
            double duration = DoorMotion.DurationMs(d, pic.Sprite);
            Place(moving, pic.Sprite, pic.Backwards ? pic.Sprite.Frames.Count - 1 : 0);
            moving.Visibility = Visibility.Visible;
            still.Visibility = opening && d.Opening == DoorOpening.Fade ? Visibility.Visible : Visibility.Hidden;
            var clock = Stopwatch.StartNew();
            void Step()
            {
                double elapsed = clock.Elapsed.TotalMilliseconds, t = Math.Clamp(elapsed / duration, 0, 1);
                if (d.Opening == DoorOpening.Animation) moving.Source = FrameCache.Get(pic.Sprite.Frames[DoorMotion.FrameAt(pic.Sprite, elapsed, pic.Backwards)].FilePath);
                else { double e = DoorMotion.Ease(t); DoorMotion.Apply(moving, d, opening ? e : 1 - e); }
                if (t < 1) return;
                timer?.Stop();
                moving.Visibility = Visibility.Collapsed;
                still.Visibility = Visibility.Visible;
            }
            Step();
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) => Step();
            timer.Start();
        }

        var openButton = new Button { Content = "Open", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        var closeButton = new Button { Content = "Close", Padding = new Thickness(10, 2, 10, 2) };
        openButton.Click += (_, _) => Play(true);
        closeButton.Click += (_, _) => Play(false);
        outer.Unloaded += (_, _) => timer?.Stop();
        ShowStill();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(openButton);
        buttons.Children.Add(closeButton);
        buttons.Children.Add(new TextBlock { Text = "  Preview - as the game plays it, with the settings as they are now", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        panel.Children.Add(outer);
        panel.Children.Add(buttons);
        return panel;
    }
}
