using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// System Settings > Slider: the look of the game's number slider (Ultima
/// VII's Slider_gump) - its sprites (from the Sprite Editor) and where each
/// part sits on the background - with a live preview to click, drag and try
/// the keys on. Used wherever the game asks "how many" (a bed: how many hours
/// to sleep; a stack being moved; a haggle). Exult's own layout is the default.
/// </summary>
public partial class SystemSettingsWindow
{
    private sealed record SliderSpriteChoice(string Group, string Name, string Display);

    private UIElement BuildSliderTab()
    {
        var s = _settings.Slider;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "The slider the game shows whenever it asks \"how many\" - how many hours to sleep in a bed, how many of a stack (gold, lockpicks...) to pick up, give or drop, a merchant's haggle. As Ultima VII's (Exult's Slider_gump): " +
                   "a background, a left and a right arrow with a diamond sliding on a track between them, the number, and a check mark to accept. " +
                   "Arrows step, the diamond drags, Left / Right keys step, Enter accepts, Escape cancels. Each use sets its own range (never past 999). " +
                   "Pick the sprites to give it your game's look; with none, a plain stand-in is drawn.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10)
        });
        // The words over it when a stack is moved (gold, lockpicks...) and the game asks how many - none in Exult.
        var quantityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        quantityRow.Children.Add(new TextBlock { Text = "Over it when a stack is moved (how many?):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var quantityQuestion = new TextBox { Text = s.QuantityQuestion, Width = 260, ToolTip = "Empty: no words, as in Exult. E.g. \"How many?\"" };
        quantityQuestion.TextChanged += (_, _) => s.QuantityQuestion = quantityQuestion.Text;
        quantityRow.Children.Add(quantityQuestion);
        panel.Children.Add(quantityRow);

        var previewHost = new Border { Background = Brushes.Black, Padding = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        var previewNote = new TextBlock { Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        void Preview()
        {
            var slider = new NumberSliderControl(s, _sprites, _resources, 1, 12, 1, 8) { LayoutTransform = new ScaleTransform(3, 3) };
            slider.ValueChanged += v => previewNote.Text = $"Value: {v} (a test range of 1 to 12)";
            slider.Accepted += v => previewNote.Text = $"Accepted: {v}";
            slider.Cancelled += () => previewNote.Text = "Cancelled (Escape)";
            slider.PreviewKeyDown += (_, e) => { if (slider.HandleKey(e.Key)) e.Handled = true; };
            slider.MouseLeftButtonDown += (_, _) => slider.Focus();
            previewHost.Child = slider;
            previewNote.Text = "Try it: click the arrows, drag the diamond, click it and use Left / Right / Enter / Escape. (A test range of 1 to 12.)";
        }

        var sprites = new List<SliderSpriteChoice> { new("", "", "(none - the plain stand-in)") };
        foreach (var group in _sprites.Groups.Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            sprites.AddRange(group.Sprites.Select(sp => new SliderSpriteChoice(group.Name, sp.Name, $"{sp.Name}   ({group.Name})")));
        FrameworkElement Picker(string label, SpriteLink link)
        {
            var combo = new ComboBox { Width = 240, ItemsSource = sprites, DisplayMemberPath = nameof(SliderSpriteChoice.Display) };
            combo.SelectedItem = sprites.FirstOrDefault(c => c.Name == link.SpriteName && c.Group == link.SpriteGroup) ?? sprites[0];
            combo.SelectionChanged += (_, _) =>
            {
                var choice = combo.SelectedItem as SliderSpriteChoice ?? sprites[0];
                link.SpriteGroup = choice.Group;
                link.SpriteName = choice.Name;
                Preview();
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(combo);
            return row;
        }
        panel.Children.Add(new TextBlock { Text = "Sprites", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(Picker("Background:", s.Background));
        panel.Children.Add(Picker("Left arrow:", s.LeftArrow));
        panel.Children.Add(Picker("Right arrow:", s.RightArrow));
        panel.Children.Add(Picker("Diamond:", s.Diamond));
        panel.Children.Add(Picker("Check mark:", s.CheckMark));

        FrameworkElement Number(string label, Func<int> get, Action<int> set, string hint)
        {
            var box = new TextBox { Width = 50, Text = get().ToString() };
            void Commit() { if (int.TryParse(box.Text.Trim(), out int v)) { set(Math.Clamp(v, 0, 2000)); Preview(); } box.Text = get().ToString(); }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = "  " + hint, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }
        panel.Children.Add(new TextBlock { Text = "Where each part sits (pixels on the background)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) });
        panel.Children.Add(Number("Left arrow X:", () => s.SliderX, v => s.SliderX = v, "Exult: 24"));
        panel.Children.Add(Number("Left arrow Y:", () => s.SliderY, v => s.SliderY = v, "Exult: 6"));
        panel.Children.Add(Number("Track width:", () => s.TrackWidth, v => s.TrackWidth = Math.Max(8, v), "between the arrows - Exult: 64"));
        panel.Children.Add(Number("Number ends at X:", () => s.NumberX, v => s.NumberX = v, "right-aligned - Exult: 128"));
        panel.Children.Add(Number("Number Y:", () => s.NumberY, v => s.NumberY = v, "Exult: 7"));
        panel.Children.Add(Number("Check mark X:", () => s.CheckX, v => s.CheckX = v, "Exult: 22"));
        panel.Children.Add(Number("Check mark Y:", () => s.CheckY, v => s.CheckY = v, "Exult: 18"));

        var sounds = new List<ResourceEntry> { new() { Id = -1, Name = "(None)" } };
        sounds.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var sound = new ComboBox { Width = 200, ItemsSource = sounds, DisplayMemberPath = "Name", SelectedItem = sounds.FirstOrDefault(r => r.Id == s.ClickSoundResourceId) ?? sounds[0] };
        sound.SelectionChanged += (_, _) => s.ClickSoundResourceId = (sound.SelectedItem as ResourceEntry)?.Id ?? -1;
        var soundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
        soundRow.Children.Add(new TextBlock { Text = "Click sound:", Width = 130, VerticalAlignment = VerticalAlignment.Center });
        soundRow.Children.Add(sound);
        panel.Children.Add(soundRow);

        var reset = new Button { Content = "Exult's layout", Padding = new Thickness(8, 1, 8, 1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), ToolTip = "Put every position back to Exult's" };
        reset.Click += (_, _) =>
        {
            var exult = new SliderSettings();
            s.SliderX = exult.SliderX; s.SliderY = exult.SliderY; s.TrackWidth = exult.TrackWidth;
            s.NumberX = exult.NumberX; s.NumberY = exult.NumberY; s.CheckX = exult.CheckX; s.CheckY = exult.CheckY;
            _sliderTabHost!.Content = BuildSliderStyleChooser();
        };
        panel.Children.Add(reset);

        panel.Children.Add(new TextBlock { Text = "Preview (three times its size)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(previewHost);
        panel.Children.Add(previewNote);
        Preview();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private ContentControl? _sliderTabHost;
}
