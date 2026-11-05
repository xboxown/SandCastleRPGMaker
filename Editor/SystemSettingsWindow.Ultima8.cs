using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>System Settings > Ultima 8 (only in an Ultima 8 project): what its engine shows and how it moves.</summary>
public partial class SystemSettingsWindow
{
    private UIElement BuildUltima8Tab()
    {
        var u8 = _settings.Ultima8;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = "Only this Ultima 8 project's engine uses these.", Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10) });

        panel.Children.Add(new TextBlock { Text = "Moving with the mouse (the right button held)", FontWeight = FontWeights.Bold });
        var mouse = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        mouse.Children.Add(Number("Step beyond (of 255)", () => u8.StepThreshold, v => u8.StepThreshold = Math.Clamp(v, 0, 255)));
        mouse.Children.Add(Number("Walk beyond (of 255)", () => u8.WalkThreshold, v => u8.WalkThreshold = Math.Clamp(v, 0, 255)));
        mouse.Children.Add(Number("Run beyond (of 255)", () => u8.RunThreshold, v => u8.RunThreshold = Math.Clamp(v, 0, 255)));
        panel.Children.Add(mouse);
        panel.Children.Add(new TextBlock { Text = "How far the pointer is from the Avatar, out of 255 of half the view: nearer than Step, the Avatar stays put (0: never); beyond Step a step, beyond Walk a walk, beyond Run a run (Pentagram's 0, 50 and 160).", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });

        panel.Children.Add(new TextBlock { Text = "Conversations (words over the speaker's head, answers over the Avatar)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
        var talk = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        talk.Children.Add(Number("Words wrap at (pixels)", () => u8.BarkWidth, v => u8.BarkWidth = Math.Clamp(v, 40, 320)));
        talk.Children.Add(Number("Answer rows (pixels)", () => u8.AskWidth, v => u8.AskWidth = Math.Clamp(v, 40, 320)));
        talk.Children.Add(Number("Time per letter (ms)", () => u8.BarkMsPerChar, v => u8.BarkMsPerChar = Math.Max(0, v)));
        talk.Children.Add(Number("At least (ms)", () => u8.BarkMinMs, v => u8.BarkMinMs = Math.Max(200, v)));
        panel.Children.Add(talk);
        panel.Children.Add(new TextBlock { Text = "A page with no answers goes on by itself after its time (or a click). Pentagram's BarkGump is 194 wide, its AskGump rows 160. Narration (nobody on the map speaking) keeps its box.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11 });
        panel.Children.Add(new TextBlock { Text = "Growing and healing", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
        var grow = new CheckBox { Content = "Stats grow by use, as Ultima 8 (no experience, levels or training points)", IsChecked = u8.StatsGrowByUse, Margin = new Thickness(0, 4, 0, 0) };
        grow.Checked += (_, _) => u8.StatsGrowByUse = true;
        grow.Unchecked += (_, _) => u8.StatsGrowByUse = false;
        panel.Children.Add(grow);
        var growRow = new WrapPanel { Margin = new Thickness(18, 2, 0, 0) };
        growRow.Children.Add(Number("Points for a certain gain", () => u8.PointsForStatGain, v => u8.PointsForStatGain = Math.Max(1, v)));
        panel.Children.Add(growRow);
        panel.Children.Add(new TextBlock { Text = "Fighting and climbing build up points for Strength and Dexterity; each time a chance of a gain, certain at this number (Ultima 8: 650). Intelligence grows by the Build Up Stat command. Where each stat stops is on the character's Level tab. Off: experience, levels and training points, as Ultima 7.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(18, 2, 0, 4) });
        var eat = new CheckBox { Content = "Eating and healing as Ultima 8", IsChecked = u8.EatingAsUltima8, Margin = new Thickness(0, 4, 0, 0) };
        eat.Checked += (_, _) => u8.EatingAsUltima8 = true;
        eat.Unchecked += (_, _) => u8.EatingAsUltima8 = false;
        panel.Children.Add(eat);
        panel.Children.Add(new TextBlock { Text = "The main character and the party regain 1 HP and 1 mana each real minute; hunger slowly builds and eating gives back HP. Off: Ultima 7's food rules.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(18, 2, 0, 4) });
        panel.Children.Add(BuildU8KeysSection(u8));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        static FrameworkElement Number(string label, Func<int> get, Action<int> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2) };
            row.Children.Add(new TextBlock { Text = label + ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var box = new TextBox { Text = get().ToString(), Width = 50 };
            box.LostFocus += (_, _) => { if (int.TryParse(box.Text, out int v)) set(v); box.Text = get().ToString(); };
            row.Children.Add(box);
            return row;
        }
    }

    /// <summary>The keys the game starts with, the paperdoll the player changes them on and the words shown while it waits (Engine/U8Keys.cs).</summary>
    private UIElement BuildU8KeysSection(Ultima8Settings u8)
    {
        var section = new StackPanel();
        section.Children.Add(new TextBlock { Text = "The keyboard", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 0) });
        section.Children.Add(new TextBlock { Text = "The keys this game starts with: every key the game reads. Click a key, then press the new one, alone or held with Ctrl, Shift or Alt (click elsewhere: no change). A key that clashes: they swap.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });

        var keys = U8Keys.MakerKeys(u8);
        var buttons = new Dictionary<string, Button>();
        var refused = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
        void ShowKeys()
        {
            foreach (var action in U8Keys.Actions) buttons[action.Id].Content = U8Keys.Display(keys[action.Id]);
        }
        void Store()
        {
            u8.Keys = keys.Where(kv => U8Keys.Actions.First(a => a.Id == kv.Key).DefaultKey != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        foreach (var (group, heading) in new[] { (U8KeyGroup.World, "On the map"), (U8KeyGroup.Everywhere, "Everywhere (no other action can share these keys)"), (U8KeyGroup.Screens, "On dialogs and screens (can share a key with the map's)") })
        {
            section.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            foreach (var action in U8Keys.Actions.Where(a => a.Group == group))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new TextBlock { Text = action.Name + ":", Width = 270, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Width = 130 };
                buttons[action.Id] = button;
                var capture = new U8KeyCapture();
                bool waiting = false;
                void Take(string key)
                {
                    waiting = false;
                    bool ok = U8Keys.Assign(keys, action.Id, key, swap: true);
                    refused.Text = ok ? "" : $"{U8Keys.Display(key)} can't go to \"{action.Name}\": the action that has it can't take {U8Keys.Display(keys[action.Id])} in return. Move that one first.";
                    Store();
                    ShowKeys();
                }
                button.Click += (_, _) => { waiting = true; capture.Reset(); refused.Text = ""; button.Content = u8.KeyAskWords; };
                button.LostFocus += (_, _) => { waiting = false; ShowKeys(); };
                button.PreviewKeyDown += (_, e) =>
                {
                    if (!waiting) return;
                    var pressed = U8KeyCapture.RealKey(e);
                    if (pressed == System.Windows.Input.Key.F4 && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) return; // Windows' own: the window closes
                    e.Handled = true;
                    if (!e.IsRepeat && capture.Down(pressed, System.Windows.Input.Keyboard.Modifiers) is { } key) Take(key);
                };
                button.PreviewKeyUp += (_, e) =>
                {
                    if (!waiting) return;
                    e.Handled = true;
                    if (capture.Up(U8KeyCapture.RealKey(e)) is { } key) Take(key);
                };
                row.Children.Add(button);
                section.Children.Add(row);
            }
        }
        ShowKeys();
        section.Children.Add(refused);
        section.Children.Add(new TextBlock { Text = "Only what Windows keeps for itself can't be taken: the Windows key, Print Screen, Alt+F4. While a name is typed, a key that writes a letter writes it, whatever it does elsewhere.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 4, 0, 4) });
        var back = new Button { Content = "Back to Ultima 8's keys", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 0, 10) };
        back.Click += (_, _) =>
        {
            foreach (var action in U8Keys.Actions) keys[action.Id] = action.DefaultKey;
            Store();
            ShowKeys();
        };
        section.Children.Add(back);

        var designs = (ProjectSession.ObjectsProvider?.Invoke()?.DollDesigns ?? new List<DollDesign>()).Select(d => new DollChoice(d.Id, d.Name)).ToList();
        designs.Insert(0, new DollChoice(-1, "(None - the key does nothing)"));
        var doll = new ComboBox { Width = 260, ItemsSource = designs, SelectedItem = designs.FirstOrDefault(c => c.Id == u8.KeysDollDesignId) ?? designs[0] };
        doll.SelectionChanged += (_, _) => u8.KeysDollDesignId = (doll.SelectedItem as DollChoice)?.Id ?? -1;
        var dollRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        dollRow.Children.Add(new TextBlock { Text = "Keys paperdoll:", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        dollRow.Children.Add(doll);
        section.Children.Add(dollRow);
        var words = new TextBox { Text = u8.KeyAskWords, Width = 260 };
        words.TextChanged += (_, _) => u8.KeyAskWords = words.Text;
        var wordsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        wordsRow.Children.Add(new TextBlock { Text = "While it waits for a key:", Width = 170, VerticalAlignment = VerticalAlignment.Center });
        wordsRow.Children.Add(words);
        section.Children.Add(wordsRow);
        section.Children.Add(new TextBlock { Text = "On a paperdoll, %keyup %keydown %keyleft %keyright %keyrun %keyjump %keycombat %keyinventory %keystats %keysaveload %keymenu %keycancel %keyaccept and so on (%key + the action's id, CommandList Guide 23) show each key's name in a text or a caption. Its buttons use the commands in the event editor's Ultima 8 tab: Ask For Key, Set Key, Get Key Name, Reset Keys.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });

        if (ProjectSession.ProjectIdProvider?.Invoke() is { } projectId)
        {
            var forget = new Button { Content = "Forget the keys changed while playing", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), IsEnabled = U8Keys.HasPlayerKeys(projectId) };
            forget.ToolTip = "What was changed in Game Test on this computer wins over the keys above. This forgets it, so Game Test starts with the keys above again.";
            forget.Click += (_, _) => { U8Keys.ForgetPlayerKeys(projectId); forget.IsEnabled = false; };
            section.Children.Add(forget);
        }
        return section;
    }
}
