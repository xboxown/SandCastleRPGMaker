using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Character Editor: what a hungry character does about it (its own food, the
/// food it carries, its inn), a Waiter's menu, and - on the Verbose tab -
/// locking its house door. The game side is GameTestWindow.Meals.cs.
/// </summary>
public partial class CharacterEditorWindow
{
    private void BuildHungerHabits(CharacterDefinition character, Panel host)
    {
        var s = character.Survival;
        host.Children.Add(new TextBlock { Text = "When hungry, it goes to eat (not the party - you feed them, as in Exult):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        host.Children.Add(HabitCheck("1. Its own food - food on the map that belongs to it (Belong To): it walks there and eats", () => s.EatsOwnFood, v => s.EatsOwnFood = v));
        host.Children.Add(HabitCheck("2. Food it carries - eaten where it stands", () => s.EatsCarriedFood, v => s.EatsCarriedFood = v));
        host.Children.Add(HabitCheck("3. The inn - it walks there, sits down and waits for the Waiter to serve it", () => s.GoesToInn, v => s.GoesToInn = v));

        var innRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 2, 0, 2) };
        var innText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 190 };
        void ShowInn() => innText.Text = s.InnX >= 0 ? $"Inn: cell ({s.InnX}, {s.InnY})" : "Inn: not set";
        var pick = new Button { Content = "Pick the inn on the map...", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 0) };
        pick.Click += (_, _) =>
        {
            if (ProjectSession.MapSnapshotProvider?.Invoke() is not { } map)
            {
                MessageBox.Show(this, "Open or create a map in the Map Editor first.", "Pick the inn", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var placed = ProjectSession.PlacedSpotProvider?.Invoke(character.Id);
            var picker = new MapCellPickerWindow(map.Picture, map.TileWidth, map.TileHeight, map.Columns, map.Rows, s.InnX >= 0 ? (s.InnX, s.InnY) : placed, placed) { Owner = this };
            if (picker.ShowDialog() == true && picker.Picked is { } cell) { s.InnX = cell.X; s.InnY = cell.Y; ShowInn(); }
        };
        var clear = new Button { Content = "Clear", Padding = new Thickness(6, 1, 6, 1) };
        clear.Click += (_, _) => { s.InnX = -1; s.InnY = -1; ShowInn(); };
        innRow.Children.Add(innText);
        innRow.Children.Add(pick);
        innRow.Children.Add(clear);
        ShowInn();
        host.Children.Add(innRow);
        host.Children.Add(InnZoneRow(s)); // or the nearest Pub (zones)
        host.Children.Add(new TextBlock
        {
            Text = "It tries them in this order and eats until it's full (31), then gets up and goes back to its schedule. Nothing to eat: it carries on and tries again later. " +
                   "At the inn, the Waiter puts real food on the table in front of it - its food: take it, and that's theft (and it says so); keep taking it, and it stays hungry.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(18, 2, 0, 6)
        });

        // ---------- A Waiter's menu ----------
        host.Children.Add(new TextBlock { Text = "As a Waiter (Schedule tab), it serves:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        var menu = new WrapPanel { Margin = new Thickness(18, 0, 0, 2) };
        void ShowMenu()
        {
            menu.Children.Clear();
            foreach (int id in character.WaiterServesFoodIds.ToList())
            {
                var name = _objects.Objects.FirstOrDefault(o => o.Id == id)?.Name ?? $"(missing #{id})";
                var chip = new Button { Content = $"{name}  ✕", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 2, 6, 2), ToolTip = "Click to take it off the menu" };
                chip.Click += (_, _) => { character.WaiterServesFoodIds.Remove(id); ShowMenu(); };
                menu.Children.Add(chip);
            }
            var choices = new List<WorldObject> { new() { Id = -1, Name = "Add a dish..." } };
            choices.AddRange(_objects.Objects.Where(o => o.TypeChosen && o.Type == ObjectType.Food && !o.Food.IsLiquid));
            var add = new ComboBox { Width = 170, ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 2, 0, 2) };
            add.SelectionChanged += (_, _) => { if (add.SelectedItem is WorldObject { Id: >= 0 } food) { character.WaiterServesFoodIds.Add(food.Id); ShowMenu(); } };
            menu.Children.Add(add);
        }
        ShowMenu();
        host.Children.Add(menu);
        host.Children.Add(new TextBlock
        {
            Text = "Food objects (solid). On its Waiter schedule it brings customers one of these, picked at random. With nothing on the menu, it takes orders but brings nothing.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(18, 0, 0, 6)
        });
    }

    private static CheckBox HabitCheck(string text, Func<bool> get, Action<bool> set)
    {
        var check = new CheckBox { Content = text, IsChecked = get(), Margin = new Thickness(6, 2, 0, 2) };
        check.Checked += (_, _) => set(true);
        check.Unchecked += (_, _) => set(false);
        return check;
    }

    /// <summary>Verbose tab: humans can lock their house door (with its key, carried).</summary>
    private UIElement BuildLocksHouseDoor(CharacterDefinition character)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
        var check = new CheckBox { Content = "Locks its house door (humans; needs its key)", IsChecked = character.LocksHouseDoor };
        check.Checked += (_, _) => character.LocksHouseDoor = true;
        check.Unchecked += (_, _) => character.LocksHouseDoor = false;
        panel.Children.Add(check);
        panel.Children.Add(new TextBlock
        {
            Text = "For a door whose key (Object Editor > Door > Required key) belongs to this character (Belong To on the key), with the key in its inventory: " +
                   "it unlocks the door with its key to get through, locks it behind itself when it leaves (out from under the roof), and locks it again when it goes to sleep. " +
                   "If the door has no key, the key isn't its own, or it doesn't carry it, none of this happens.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left
        });
        return panel;
    }
}
