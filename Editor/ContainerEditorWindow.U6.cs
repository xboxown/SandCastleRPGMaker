// Container Editor, Ultima 6 projects only: no Location (every container opens where the HUD's Carried Items are), no open view
// of its own (background, close button, item frame, open/close sounds), and in place of Max Volume a grid - cells across and down.
// Ultima 7 and Ultima 8 projects never run any of this.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ContainerEditorWindow
{
    private sealed record U6SpriteChoice(string Group, string Name, string Display);

    private readonly StackPanel _u6Grid = new() { Margin = new Thickness(0, 0, 0, 8) };

    /// <summary>Hides what Ultima 6 has no use for, moves the World Sprite above the solid-size preview, and puts the grid where Location was.</summary>
    private void SetUpU6Layout()
    {
        // Location: its title, its note and its list (the two unnamed texts just before the list).
        if (LocationCombo.Parent is Panel settings)
        {
            int at = settings.Children.IndexOf(LocationCombo);
            for (int i = Math.Max(0, at - 2); i <= at; i++) settings.Children[i].Visibility = Visibility.Collapsed;
            settings.Children.Insert(at + 1, _u6Grid);
        }

        // Sprites and Sound: only the World Sprite stays - moved to the top of Behavior, right above the preview that shows it.
        if (WorldSpriteCombo.Parent is DockPanel spriteRow && spriteRow.Parent is Panel sprites && sprites.Parent is GroupBox spritesGroup
            && SolidSize.Parent is Panel behavior)
        {
            int at = sprites.Children.IndexOf(spriteRow);
            var title = sprites.Children[at - 1];
            sprites.Children.Remove(title);
            sprites.Children.Remove(spriteRow);
            int solid = behavior.Children.IndexOf(SolidSize);
            behavior.Children.Insert(solid, spriteRow);
            behavior.Children.Insert(solid, title);
            spritesGroup.Visibility = Visibility.Collapsed; // the locked sprite, the open view's background, its sounds, close button and item frame
        }
    }

    /// <summary>The grid's fields for this container: X and Y are the HUD's Carried Items' (locked), Across / Down / Cell size its own.</summary>
    private void ShowU6Grid(ContainerData container)
    {
        _u6Grid.Children.Clear();
        var carried = ProjectSession.SettingsProvider?.Invoke().Ultima6.Hud.First(HudPieceKind.CarriedItems);
        if (container.U6Columns <= 0 || container.U6Rows <= 0 || container.U6CellSize <= 0) // first time: the HUD's own grid to start from
        {
            container.U6Columns = carried?.Columns ?? 4;
            container.U6Rows = carried?.Rows ?? 3;
            container.U6CellSize = carried?.CellSize ?? 16;
        }

        _u6Grid.Children.Add(new TextBlock { Text = "Inside it (opened)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        _u6Grid.Children.Add(new TextBlock
        {
            Text = carried is null
                ? "Opened, what it holds shows where the HUD's Carried Items are - this HUD has none yet (HUD Editor > Panel > Carried Items)."
                : "Opened, what it holds shows where the HUD's Carried Items are - X and Y come from there (HUD Editor) and can't be changed here.",
            Opacity = 0.6, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2)
        });

        var capacity = new TextBlock { Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        void ShowCapacity() => capacity.Text = $"Holds {container.U6Capacity ?? 0} things - {container.U6Columns} x {container.U6Rows} cells, the first one the container itself (Use it again to close it, as in Ultima 6).";

        var row = new WrapPanel();
        row.Children.Add(U6Field("X", (carried?.X ?? 0).ToString(), null));
        row.Children.Add(U6Field("Y", (carried?.Y ?? 0).ToString(), null));
        row.Children.Add(U6Field("Across", container.U6Columns.ToString(), v => { container.U6Columns = Math.Clamp(v, 1, 20); ShowCapacity(); return container.U6Columns; }));
        row.Children.Add(U6Field("Down", container.U6Rows.ToString(), v => { container.U6Rows = Math.Clamp(v, 1, 20); ShowCapacity(); return container.U6Rows; }));
        row.Children.Add(U6Field("Cell size", container.U6CellSize.ToString(), v => container.U6CellSize = Math.Clamp(v, 4, 64)));
        _u6Grid.Children.Add(row);
        _u6Grid.Children.Add(U6EmptyCellPicker(container));
        ShowCapacity();
        _u6Grid.Children.Add(capacity);
    }

    /// <summary>A number box; set null: locked (shown, not editable). set returns the value kept (clamped).</summary>
    private static FrameworkElement U6Field(string label, string value, Func<int, int>? set)
    {
        var box = new TextBox { Width = 56, Text = value, IsReadOnly = set is null, IsEnabled = set is not null };
        if (set is not null)
        {
            void Commit()
            {
                if (int.TryParse(box.Text.Trim(), out int v)) box.Text = set(v).ToString();
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
            box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        }
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box);
        return panel;
    }

    /// <summary>"Sprite of an empty cell (optional)" - as the HUD Editor's Carried Items has it (none: the HUD's own empty cell).</summary>
    private FrameworkElement U6EmptyCellPicker(ContainerData container)
    {
        var choices = new List<U6SpriteChoice> { new("", "", "(none - the HUD's Carried Items empty cell)") };
        foreach (var group in _sprites.FoldersFor(SpriteFolderIdentity.Paperdoll).Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            choices.AddRange(group.Sprites.Select(s => new U6SpriteChoice(group.Name, s.Name, $"{s.Name}   ({group.Name})")));
        var combo = new ComboBox { Width = 260, ItemsSource = choices, DisplayMemberPath = nameof(U6SpriteChoice.Display) };
        var current = container.U6EmptyCell;
        combo.SelectedItem = choices.FirstOrDefault(c => c.Name == current.SpriteName && (c.Group == current.SpriteGroup || string.IsNullOrEmpty(current.SpriteGroup))) ?? choices[0];
        combo.SelectionChanged += (_, _) =>
        {
            var c = combo.SelectedItem as U6SpriteChoice;
            container.U6EmptyCell = new SpriteLink { SpriteGroup = c?.Group ?? "", SpriteName = c?.Name ?? "" };
        };
        var expand = new Button { Content = "Expand...", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "The Paperdoll folders and their sprites, with their pictures and a search box." };
        expand.Click += (_, _) =>
        {
            var now = combo.SelectedItem as U6SpriteChoice;
            if (SpriteBrowserWindow.Pick(this, _sprites, SpriteFolderIdentity.Paperdoll, null, now?.Group, now?.Name) is not { } picked) return;
            var choice = choices.FirstOrDefault(c => c.Group == picked.Group && c.Name == picked.Name);
            if (choice is null) { choice = new U6SpriteChoice(picked.Group, picked.Name, $"{picked.Name}   ({picked.Group})"); choices.Add(choice); combo.Items.Refresh(); }
            combo.SelectedItem = choice;
        };
        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        pick.Children.Add(combo);
        pick.Children.Add(expand);
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        panel.Children.Add(new TextBlock { Text = "Sprite of an empty cell (optional)" });
        panel.Children.Add(pick);
        return panel;
    }
}
