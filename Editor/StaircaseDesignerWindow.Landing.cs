using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A staircase's landing: floor tiles at the top, at the height of the floor
/// the stairs climb to - stepped onto off the top step, and from them onto
/// that floor's own tiles. In play a landing tile is a floor slab like an
/// upper floor's tile (solid only in its top lift), drawn exactly where that
/// floor's tiles are drawn, so it joins them seamlessly.
/// </summary>
public partial class StaircaseDesignerWindow
{
    /// <summary>The way the stairs go up: from the step below the top one to the top one (north if it can't tell).</summary>
    private (int X, int Y) RiseDirection(StairPose pose)
    {
        var climbing = pose.Steps.Where(s => !s.IsLanding).OrderBy(s => s.TopLift).ToList();
        if (climbing.Count >= 2)
        {
            var (a, b) = (climbing[^2], climbing[^1]);
            int dx = Math.Sign(b.FootX + b.SizeX / 2.0 - (a.FootX + a.SizeX / 2.0)), dy = Math.Sign(b.FootY + b.SizeY / 2.0 - (a.FootY + a.SizeY / 2.0));
            if (dx != 0 || dy != 0) return Math.Abs(dx) >= Math.Abs(dy) && dx != 0 ? (dx, 0) : (0, dy);
        }
        return (0, -1);
    }

    private int UnitsX => Math.Max(1, _tileW / SpriteSizing.BaseUnit);
    private int UnitsY => Math.Max(1, _tileH / SpriteSizing.BaseUnit);

    /// <summary>Puts a landing tile at a cell (relative to the anchor cell) and lift, drawn where that floor's tiles are drawn.</summary>
    private void PlaceLanding(StairStep step, int cellX, int cellY, int topLift)
    {
        step.IsLanding = true;
        step.TopLift = Math.Max(1, topLift);
        step.FootX = cellX * UnitsX;
        step.FootY = cellY * UnitsY;
        step.SizeX = UnitsX;
        step.SizeY = UnitsY;
        step.PictureX = cellX * _tileW - step.TopLift * LiftPixels;
        step.PictureY = cellY * _tileH - step.TopLift * LiftPixels;
    }

    /// <summary>The landing tile's cell (relative to the anchor cell).</summary>
    private (int X, int Y) LandingCell(StairStep step) =>
        ((int)Math.Floor(step.FootX / (double)UnitsX), (int)Math.Floor(step.FootY / (double)UnitsY));

    /// <summary>Add landing tile: one cell beyond the top step, the way the stairs go up, at the top step's height (the floor above).</summary>
    private void AddLanding()
    {
        if (Design is null) NewDesign();
        var pose = EnsurePose();
        var top = pose.Steps.Where(s => !s.IsLanding).OrderBy(s => s.TopLift).LastOrDefault();
        int lift = top?.TopLift ?? MapFloors.LiftsPerFloor;
        var (dx, dy) = RiseDirection(pose);
        // From the last landing tile, or else from the top step's cell.
        var from = pose.Steps.LastOrDefault(s => s.IsLanding);
        (int X, int Y) cell;
        if (from is not null) { var c = LandingCell(from); cell = (c.X + dx, c.Y + dy); }
        else if (top is not null) cell = ((int)Math.Floor((top.FootX + top.SizeX / 2.0) / UnitsX) + dx, (int)Math.Floor((top.FootY + top.SizeY / 2.0) / UnitsY) + dy);
        else cell = (0, -1);
        var step = new StairStep();
        if (from is not null) step.Sprite = new SpriteLink { SpriteGroup = from.Sprite.SpriteGroup, SpriteName = from.Sprite.SpriteName };
        if (from is not null) step.Frame = from.Frame;
        PlaceLanding(step, cell.X, cell.Y, lift);
        pose.Steps.Add(step);
        _selectedStep = pose.Steps.Count - 1;
        RefreshAll();
        _check.Text = "A landing tile: pick its tile (Pick tile...), then add more beside it - arrows move a landing tile a whole cell. In play you step off the top step onto it, and from it onto the floor above's own tiles.";
    }

    /// <summary>A landing tile's tile: a tilesheet (the Sprite Editor's Tileset group) and which of its tiles.</summary>
    private FrameworkElement LandingTilePicker(StairStep step)
    {
        var panel = new StackPanel();
        var sheets = _sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.TilesetGroupName)?.Sprites.ToList() ?? new List<SpriteEntry>();
        var sheet = new ComboBox { Width = 220, ItemsSource = sheets, DisplayMemberPath = nameof(SpriteEntry.Name), HorizontalAlignment = HorizontalAlignment.Left };
        sheet.SelectedItem = sheets.FirstOrDefault(s => s.Name == step.Sprite.SpriteName) ?? sheets.FirstOrDefault();
        var preview = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, Margin = new Thickness(8, 0, 0, 0) };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        void Show() => preview.Source = StairLook.FirstFrameOf(step, _sprites);
        void Use(SpriteEntry? entry, int frame)
        {
            step.Sprite = entry is null ? new SpriteLink() : new SpriteLink { SpriteGroup = SpriteDatabase.TilesetGroupName, SpriteName = entry.Name };
            step.Frame = Math.Max(0, frame);
            Show();
            Redraw();
        }
        sheet.SelectionChanged += (_, _) => Use(sheet.SelectedItem as SpriteEntry, 0);
        if (!step.Sprite.IsSet && sheet.SelectedItem is SpriteEntry firstSheet) Use(firstSheet, 0);
        var pick = new Button { Content = "Pick tile...", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0) };
        pick.Click += (_, _) =>
        {
            if (sheet.SelectedItem is not SpriteEntry entry || entry.Frames.Count == 0) return;
            if (PickTile(entry, step.Frame) is { } frame) Use(entry, frame);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(sheet);
        row.Children.Add(pick);
        row.Children.Add(new Border { Background = Brushes.Black, Child = preview, Margin = new Thickness(8, 0, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Tile (a tilesheet of the Sprite Editor's Tileset group)" });
        panel.Children.Add(row);
        panel.Children.Add(new TextBlock { Text = "Use the floor above's own tile, so the landing joins it.", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) });
        Show();
        return panel;
    }

    /// <summary>A tilesheet's tiles in a grid: click one.</summary>
    private int? PickTile(SpriteEntry sheet, int current)
    {
        int? picked = null;
        var window = new Window { Title = $"Pick a tile - {sheet.Name}", Width = 560, Height = 520, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var grid = new WrapPanel { Margin = new Thickness(6) };
        for (int i = 0; i < sheet.Frames.Count; i++)
        {
            int index = i;
            var image = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform, Source = DollLook.Frame(sheet, i) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var button = new Button { Content = image, Margin = new Thickness(2), Padding = new Thickness(1), ToolTip = $"Tile {i}", BorderBrush = i == current ? Brushes.Gold : Brushes.Gray, BorderThickness = new Thickness(i == current ? 2 : 1) };
            button.Click += (_, _) => { picked = index; window.DialogResult = true; };
            grid.Children.Add(button);
        }
        window.Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
        return picked;
    }
}
