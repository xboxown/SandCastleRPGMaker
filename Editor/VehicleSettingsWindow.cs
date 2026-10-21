using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>A vehicle's settings (Vehicles tab > Settings): what it moves on, how fast, the deed it needs, its sounds.</summary>
internal sealed class VehicleSettingsWindow : Window
{
    public VehicleSettingsWindow(string name, VehicleSettings settings, ObjectDatabase objects, ResourceDatabase resources)
    {
        Title = $"Vehicle Settings - {name}";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var rows = new StackPanel { Margin = new Thickness(12) };
        UIElement Row(string label, UIElement control, string? note = null)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(control);
            if (note is not null) panel.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            return panel;
        }

        var movement = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "Land - rolls over walkable ground", "Sea - sails on Water tiles only", "Air - flies" }, SelectedIndex = (int)settings.Movement };
        movement.SelectionChanged += (_, _) => settings.Movement = (MovementKind)Math.Max(0, movement.SelectedIndex);
        rows.Children.Add(Row("Moves on", movement, "Land: a cart, a car, a horse cart - only over walkable, dry ground. Sea: a ship, a raft - only on tiles marked Water (Tile Properties). Air: a carpet, an airship, a rocket."));

        var speed = new TextBox { Width = 70, HorizontalAlignment = HorizontalAlignment.Left, Text = settings.CellsPerSecond.ToString("0.#") };
        speed.LostFocus += (_, _) => { if (double.TryParse(speed.Text, out double v)) settings.CellsPerSecond = Math.Clamp(v, 0.5, 30); speed.Text = settings.CellsPerSecond.ToString("0.#"); };
        rows.Children.Add(Row("Speed (map cells a second)", speed, "Walking is about 3."));

        var deeds = new List<WorldObject> { new() { Id = -1, Name = "(No deed needed - anyone may drive it)" } };
        deeds.AddRange(objects.Objects.Where(o => o.TypeChosen).OrderBy(o => o.Name));
        var deed = new ComboBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = deeds, DisplayMemberPath = "Name" };
        deed.SelectedItem = deeds.FirstOrDefault(d => d.Id == settings.DeedObjectId) ?? deeds[0];
        deed.SelectionChanged += (_, _) => settings.DeedObjectId = (deed.SelectedItem as WorldObject)?.Id ?? -1;
        rows.Children.Add(Row("Deed", deed, "As Ultima VII's ship deeds: only while someone in the party carries this object (anywhere in their things) can the vehicle be driven."));
        var deedText = new TextBox { Width = 400, HorizontalAlignment = HorizontalAlignment.Left, Text = settings.NeedDeedText };
        deedText.TextChanged += (_, _) => settings.NeedDeedText = deedText.Text;
        rows.Children.Add(Row("Said without the deed", deedText));

        var noSound = new ResourceEntry { Id = -1, Name = "(None)" };
        var sounds = new List<ResourceEntry> { noSound };
        sounds.AddRange(resources.Entries.Where(r => r.Kind == ResourceKind.Sound).OrderBy(r => r.Name));
        UIElement Sound(string label, Func<int> get, Action<int> set)
        {
            var combo = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = sounds, DisplayMemberPath = "Name" };
            combo.SelectedItem = sounds.FirstOrDefault(s => s.Id == get()) ?? noSound;
            combo.SelectionChanged += (_, _) => set((combo.SelectedItem as ResourceEntry)?.Id ?? -1);
            return Row(label, combo);
        }
        rows.Children.Add(Sound("Sound as it starts off (taking the controls)", () => settings.StartSoundResourceId, v => settings.StartSoundResourceId = v));
        rows.Children.Add(Sound("Sound while it moves (loops)", () => settings.MovingSoundResourceId, v => settings.MovingSoundResourceId = v));
        rows.Children.Add(Sound("Sound as it stops", () => settings.StopSoundResourceId, v => settings.StopSoundResourceId = v));

        var close = new Button { Content = "Close", Width = 80, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => { speed.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)); Close(); };
        rows.Children.Add(close);
        Content = rows;
    }
}
