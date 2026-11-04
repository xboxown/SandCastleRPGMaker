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
        BuildHungerHabits(character, hungerBox); // its own food, food it carries, the inn; a Waiter's menu
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

        // ---------- Gives a liquid (milking) ----------
        HealthPanel.Children.Add(Heading("Gives a liquid (milking a cow)"));
        var liquids = new List<WorldObject> { new() { Id = -1, Name = "(Nothing)" } };
        liquids.AddRange((ProjectSession.ObjectsProvider?.Invoke()?.Objects ?? new List<WorldObject>()).Where(o => o.TypeChosen && o.Type == ObjectType.Food && o.Food.IsLiquid));
        var liquidCombo = new ComboBox { ItemsSource = liquids, DisplayMemberPath = "Name", Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 2) };
        liquidCombo.SelectedItem = liquids.FirstOrDefault(l => l.Id == character.GivesLiquidFoodId) ?? liquids[0];
        liquidCombo.SelectionChanged += (_, _) => character.GivesLiquidFoodId = (liquidCombo.SelectedItem as WorldObject)?.Id ?? -1;
        HealthPanel.Children.Add(liquidCombo);
        var barkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        barkRow.Children.Add(new TextBlock { Text = "It says:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        var barkBox = new TextBox { Width = 200, Text = character.GivesLiquidBark };
        barkBox.TextChanged += (_, _) => character.GivesLiquidBark = barkBox.Text;
        barkRow.Children.Add(barkBox);
        HealthPanel.Children.Add(barkRow);
        var sounds = new List<ResourceEntry> { new() { Id = -1, Name = "(None)" } };
        sounds.AddRange((ProjectSession.ResourcesProvider?.Invoke()?.Entries ?? new List<ResourceEntry>()).Where(r => r.Kind == ResourceKind.Sound));
        var soundRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        soundRow.Children.Add(new TextBlock { Text = "Milking sound:", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        var soundCombo = new ComboBox { ItemsSource = sounds, DisplayMemberPath = "Name", Width = 200, SelectedItem = sounds.FirstOrDefault(r => r.Id == character.GivesLiquidSoundResourceId) ?? sounds[0] };
        soundCombo.SelectionChanged += (_, _) => character.GivesLiquidSoundResourceId = (soundCombo.SelectedItem as ResourceEntry)?.Id ?? -1;
        soundRow.Children.Add(soundCombo);
        HealthPanel.Children.Add(soundRow);
        var help = new Button { Content = "How liquids work (with pictures)...", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 2, 0, 2) };
        help.Click += (_, _) => new LiquidsHelpWindow { Owner = this }.ShowDialog();
        HealthPanel.Children.Add(help);
        HealthPanel.Children.Add(new TextBlock
        {
            Text = "A cow gives milk (Exult's milking): an empty bucket or bottle used on it is filled with this liquid - if the container can hold it. " +
                   "The list shows your liquids (Food objects set to Liquid). (Nothing): a full bucket used on it just douses it.",
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
        var noBody = new CheckBox { Content = "No Body (it leaves no body: what it carried falls on the ground around where it died)", IsChecked = body.NoBody, Margin = new Thickness(0, 2, 0, 0) };
        noBody.Checked += (_, _) => body.NoBody = true;
        noBody.Unchecked += (_, _) => body.NoBody = false;
        HealthPanel.Children.Add(noBody);

        // ---------- What it takes to hurt it ----------
        HealthPanel.Children.Add(Heading("What it takes to hurt it"));
        var immortal = new CheckBox { Content = "Immortal (it loses health and can go unconscious, but can't be killed)", IsChecked = character.Immortal };
        var needsWeapon = new CheckBox { Content = "Only hurt by this weapon, equipped:", IsChecked = character.RequiresWeapon, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var needsSpell = new CheckBox { Content = "Only hurt by this spell:", IsChecked = character.RequiresSpell, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var allObjects = ProjectSession.ObjectsProvider?.Invoke()?.Objects ?? new List<WorldObject>();
        var weapons = allObjects.Where(o => o.Type == ObjectType.Weapon).OrderBy(o => o.Name).ToList();
        var weaponBox = new ComboBox { Width = 220, ItemsSource = weapons, DisplayMemberPath = nameof(WorldObject.Name), SelectedItem = weapons.FirstOrDefault(o => o.Id == character.RequiredWeaponId) };
        weaponBox.SelectionChanged += (_, _) => character.RequiredWeaponId = (weaponBox.SelectedItem as WorldObject)?.Id ?? -1;
        var spellBox = new ComboBox { Width = 220, IsEditable = true, ItemsSource = allObjects.Where(o => o.Type == ObjectType.Spellbook).SelectMany(o => o.Spellbook.Spells).Select(sp => sp.Name).Distinct().OrderBy(n => n).ToList(), Text = character.RequiredSpellName };
        spellBox.LostFocus += (_, _) => character.RequiredSpellName = spellBox.Text.Trim();
        spellBox.SelectionChanged += (_, _) => { if (spellBox.SelectedItem is string name) character.RequiredSpellName = name; };
        void ShowHurtRules()
        {
            needsWeapon.IsEnabled = needsSpell.IsEnabled = immortal.IsChecked != true;
            weaponBox.IsEnabled = needsWeapon.IsEnabled && needsWeapon.IsChecked == true;
            spellBox.IsEnabled = needsSpell.IsEnabled && needsSpell.IsChecked == true;
        }
        immortal.Checked += (_, _) => { character.Immortal = true; ShowHurtRules(); };
        immortal.Unchecked += (_, _) => { character.Immortal = false; ShowHurtRules(); };
        needsWeapon.Checked += (_, _) => { character.RequiresWeapon = true; ShowHurtRules(); };
        needsWeapon.Unchecked += (_, _) => { character.RequiresWeapon = false; ShowHurtRules(); };
        needsSpell.Checked += (_, _) => { character.RequiresSpell = true; ShowHurtRules(); };
        needsSpell.Unchecked += (_, _) => { character.RequiresSpell = false; ShowHurtRules(); };
        HealthPanel.Children.Add(immortal);
        var weaponRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        weaponRow.Children.Add(needsWeapon);
        weaponRow.Children.Add(weaponBox);
        HealthPanel.Children.Add(weaponRow);
        var spellRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        spellRow.Children.Add(needsSpell);
        spellRow.Children.Add(spellBox);
        HealthPanel.Children.Add(spellRow);
        HealthPanel.Children.Add(new TextBlock
        {
            Text = "Both ticked: either one hurts it. With either ticked, nothing else does: not other weapons, traps, fire, poison or hunger. An event's Set HP or Kill Character still works.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 4)
        });
        ShowHurtRules();

        // ---------- Joining ----------
        HealthPanel.Children.Add(Heading("Joining (monsters that touch)"));
        HealthPanel.Children.Add(new TextBlock
        {
            Text = "Ultima 7's slimes, but for any monster. Joined look: a sprite in the Sprite Editor's Joined Monsters group (New Sprite there shows a guide): it shows which sides touch its own kind, and each stays its own character. Rules: touching, it multiplies, or fuses into a new monster.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4)
        });
        var joining = character.Joining;
        var lookChoices = new List<string> { "(None - its ordinary poses)" };
        lookChoices.AddRange(_sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.JoinedMonstersGroupName)?.Sprites.Select(b => b.Name) ?? Enumerable.Empty<string>());
        var lookCombo = new ComboBox { ItemsSource = lookChoices, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 4), SelectedIndex = Math.Max(0, lookChoices.IndexOf(joining.JoinedSprite.SpriteName)) };
        lookCombo.SelectionChanged += (_, _) =>
        {
            joining.JoinedSprite.SpriteGroup = lookCombo.SelectedIndex <= 0 ? string.Empty : SpriteDatabase.JoinedMonstersGroupName;
            joining.JoinedSprite.SpriteName = lookCombo.SelectedIndex <= 0 ? string.Empty : (string)lookCombo.SelectedItem;
        };
        HealthPanel.Children.Add(lookCombo);
        var kinds = (ProjectSession.CharactersProvider?.Invoke()?.Characters ?? new List<CharacterDefinition>()).ToList();
        var rules = new StackPanel();
        void ShowRules()
        {
            rules.Children.Clear();
            foreach (var rule in joining.Rules.ToList())
            {
                var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
                var partnerChoices = new List<string> { "its own kind" };
                partnerChoices.AddRange(kinds.Select(k => k.Name));
                var partner = new ComboBox { Width = 130, ItemsSource = partnerChoices, SelectedIndex = rule.PartnerCharacterId < 0 ? 0 : kinds.FindIndex(k => k.Id == rule.PartnerCharacterId) + 1 };
                partner.SelectionChanged += (_, _) => rule.PartnerCharacterId = partner.SelectedIndex <= 0 ? -1 : kinds[partner.SelectedIndex - 1].Id;
                var how = new ComboBox { Width = 100, ItemsSource = new[] { "multiplies", "fuses" }, SelectedIndex = rule.Fuses ? 1 : 0, Margin = new Thickness(4, 0, 4, 0) };
                how.SelectionChanged += (_, _) => rule.Fuses = how.SelectedIndex == 1;
                var result = new ComboBox { Width = 130, ItemsSource = kinds.Select(k => k.Name).ToList(), SelectedIndex = kinds.FindIndex(k => k.Id == rule.ResultCharacterId) };
                result.SelectionChanged += (_, _) => rule.ResultCharacterId = result.SelectedIndex < 0 ? -1 : kinds[result.SelectedIndex].Id;
                row.Children.Add(new TextBlock { Text = "Touching ", VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(partner);
                row.Children.Add(how);
                row.Children.Add(new TextBlock { Text = "into ", VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(result);
                var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(6, 0, 0, 0) };
                remove.Click += (_, _) => { joining.Rules.Remove(rule); ShowRules(); };
                row.Children.Add(remove);
                rules.Children.Add(row);
                rules.Children.Add(NumberRow("Chance %:", () => rule.ChancePercent, v => rule.ChancePercent = Math.Clamp(v, 0, 100), "each time they touch"));
                rules.Children.Add(NumberRow("Wait (seconds):", () => rule.CooldownSeconds, v => rule.CooldownSeconds = Math.Max(1, v), "before the same two can try again"));
                rules.Children.Add(NumberRow("Touching within:", () => rule.ReachTiles, v => rule.ReachTiles = Math.Max(1, v), "tiles of 8 pixels"));
                rules.Children.Add(NumberRow("Most on the map:", () => rule.MaxOnMap, v => rule.MaxOnMap = Math.Max(0, v), "of the new monster (0: no limit)"));
            }
        }
        ShowRules();
        HealthPanel.Children.Add(rules);
        var addRule = new Button { Content = "Add a rule", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 2, 0, 0) };
        addRule.Click += (_, _) => { joining.Rules.Add(new JoinRule { ResultCharacterId = character.Id }); ShowRules(); };
        HealthPanel.Children.Add(addRule);

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
