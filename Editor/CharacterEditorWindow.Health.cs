using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>The Health tab: hunger (Exult's food level), poison, and ailment immunities.</summary>
public partial class CharacterEditorWindow
{
    private void BuildHealthPanel(CharacterDefinition character)
    {
        var s = character.Survival;
        HealthPanel.Children.Clear();

        HealthPanel.Children.Add(new TextBlock
        {
            Text = "Health is the Hits stat; Strength is the most it can be (Exult). Every game hour a character heals 1 HP unless poisoned or starving. " +
                   "Unlike Exult, starvation and poison kill - and they apply to every character, not just the party.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10)
        });

        // ---------- Hunger ----------
        HealthPanel.Children.Add(Heading("Hunger"));
        var canHunger = new RadioButton { Content = "Can get hungry", GroupName = "Hunger" + character.Id, IsChecked = s.CanGetHungry, Margin = new Thickness(0, 0, 16, 0) };
        var never = new RadioButton { Content = "Never gets hungry", GroupName = "Hunger" + character.Id, IsChecked = !s.CanGetHungry };
        var radios = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        radios.Children.Add(canHunger);
        radios.Children.Add(never);
        HealthPanel.Children.Add(radios);

        var hungerBox = new StackPanel { Margin = new Thickness(12, 0, 0, 8) };
        var estimate = new TextBlock { Opacity = 0.75, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        void Estimate()
        {
            double perHour = Math.Max(0.01, s.FoodUsedPerHourMax / 2.0);
            double hours = Math.Max(0, s.StartingFood - s.HungryAtOrBelow) / perHour;
            double starveHours = Math.Max(0, s.StartingFood) / perHour;
            estimate.Text = s.FoodUsedPerHourMax <= 0
                ? "Never uses food, so never gets hungry."
                : $"Lasts about {hours:0.#} game hours (~{hours * 5:0} real minutes) before getting hungry, and about {starveHours:0.#} game hours before starving.";
        }
        hungerBox.Children.Add(NumberRow("Starting food level:", () => s.StartingFood, v => { s.StartingFood = Math.Clamp(v, 0, SurvivalSettings.MaxFood); Estimate(); }, "0-31 (Exult's food level)"));
        hungerBox.Children.Add(NumberRow("Hungry at or below:", () => s.HungryAtOrBelow, v => { s.HungryAtOrBelow = Math.Clamp(v, 0, SurvivalSettings.MaxFood); Estimate(); }, "Exult: 9"));
        hungerBox.Children.Add(NumberRow("Food used each hour:", () => s.FoodUsedPerHourMax, v => { s.FoodUsedPerHourMax = Math.Clamp(v, 0, SurvivalSettings.MaxFood); Estimate(); }, "0 up to this, at random (Exult: 3)"));
        hungerBox.Children.Add(NumberRow("Starving damage:", () => s.StarvingDamage, v => s.StarvingDamage = Math.Max(0, v), "HP lost when starving (Exult: 1)"));
        hungerBox.Children.Add(NumberRow("Starving chance:", () => s.StarvingChancePercent, v => s.StarvingChancePercent = Math.Clamp(v, 0, 100), "% each game minute at food 0 (Exult: 40)"));
        hungerBox.Children.Add(estimate);
        Estimate();
        HealthPanel.Children.Add(hungerBox);
        void HungerChanged() { s.CanGetHungry = canHunger.IsChecked == true; hungerBox.IsEnabled = s.CanGetHungry; }
        canHunger.Checked += (_, _) => HungerChanged();
        never.Checked += (_, _) => HungerChanged();
        hungerBox.IsEnabled = s.CanGetHungry;

        // ---------- Poison ----------
        HealthPanel.Children.Add(Heading("Poison"));
        var wearsOff = new CheckBox { Content = "Wears off by itself", IsChecked = s.PoisonWearsOff, Margin = new Thickness(0, 2, 0, 4) };
        HealthPanel.Children.Add(wearsOff);
        var cureBox = new StackPanel { Margin = new Thickness(12, 0, 0, 4) };
        cureBox.Children.Add(NumberRow("Cured after at least:", () => s.PoisonCureMinSeconds, v => s.PoisonCureMinSeconds = Math.Max(1, v), "seconds (Exult: 60)"));
        cureBox.Children.Add(NumberRow("and at most:", () => s.PoisonCureMaxSeconds, v => s.PoisonCureMaxSeconds = Math.Max(1, v), "seconds (Exult: 180)"));
        cureBox.IsEnabled = s.PoisonWearsOff;
        wearsOff.Checked += (_, _) => { s.PoisonWearsOff = true; cureBox.IsEnabled = true; };
        wearsOff.Unchecked += (_, _) => { s.PoisonWearsOff = false; cureBox.IsEnabled = false; };
        HealthPanel.Children.Add(cureBox);
        HealthPanel.Children.Add(NumberRow("Poison damage:", () => s.PoisonDamageMax, v => s.PoisonDamageMax = Math.Max(0, v), "0 up to this HP every 10-20 s (Exult: 2)"));

        // ---------- Blood ----------
        HealthPanel.Children.Add(Heading("Blood"));
        var bloodChoices = new List<string> { "(None - never bleeds)" };
        bloodChoices.AddRange(_sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.BloodGroupName)?.Sprites.Select(b => b.Name) ?? Enumerable.Empty<string>());
        var bloodCombo = new ComboBox { ItemsSource = bloodChoices, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) };
        bloodCombo.SelectedIndex = Math.Max(0, bloodChoices.IndexOf(character.BloodSpriteName));
        bloodCombo.SelectionChanged += (_, _) => character.BloodSpriteName = bloodCombo.SelectedIndex <= 0 ? string.Empty : (string)bloodCombo.SelectedItem;
        HealthPanel.Children.Add(bloodCombo);
        HealthPanel.Children.Add(new TextBlock
        {
            Text = "Its blood type - a sprite in the Sprite Editor's Blood group (red, green, purple...). As in Exult, a blow that lands may start it bleeding, " +
                   "leaving a trail of puddles that grow where it keeps bleeding. None: it never bleeds (a robot, a ghost).",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 8)
        });

        // ---------- Body ----------
        HealthPanel.Children.Add(Heading("Body (after death)"));
        HealthPanel.Children.Add(new TextBlock
        {
            Text = "When this character dies it leaves a body (its Body pose) holding everything it carried - opened as the Body Container from System Settings. " +
                   "As in Exult, a body can be picked up and carried like any object, and things can be taken out of it but not put back in.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4)
        });
        var body = character.Body;
        HealthPanel.Children.Add(NumberRow("Body weight:", () => (int)body.Weight, v => body.Weight = Math.Max(0, v), ""));
        HealthPanel.Children.Add(NumberRow("Body volume:", () => (int)body.Volume, v => body.Volume = Math.Max(0, v), "space it takes when carried"));
        var tooHeavy = new CheckBox { Content = "Too Heavy (can't be picked up or moved - only opened where it lies)", IsChecked = body.TooHeavy, Margin = new Thickness(0, 2, 0, 0) };
        tooHeavy.Checked += (_, _) => body.TooHeavy = true;
        tooHeavy.Unchecked += (_, _) => body.TooHeavy = false;
        HealthPanel.Children.Add(tooHeavy);

        // ---------- Immunities ----------
        HealthPanel.Children.Add(Heading("Immune to"));
        var immunities = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var ailment in Ailments.All)
        {
            var box = new CheckBox { Content = Ailments.Describe(ailment), IsChecked = s.Immunities.HasFlag(ailment), Margin = new Thickness(0, 0, 16, 4), ToolTip = Ailments.Explain(ailment) };
            box.Checked += (_, _) => s.Immunities |= ailment;
            box.Unchecked += (_, _) => s.Immunities &= ~ailment;
            immunities.Children.Add(box);
        }
        HealthPanel.Children.Add(immunities);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) };

    private static FrameworkElement NumberRow(string label, Func<int> get, Action<int> set, string hint)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        row.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { Text = get().ToString(), Width = 50, TextAlignment = TextAlignment.Right };
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim(), out int value)) set(value);
            box.Text = get().ToString();
        };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = "  " + hint, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, Foreground = Brushes.Black });
        return row;
    }
}
