using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map Editor: what each placed object of the build 248 types can be set
/// to on the map (right-click) - a window's shutters open or closed, a
/// plant's starting stage, a lever's starting position and the things it's
/// linked to, a ladder's destination, what a liquid container starts full
/// of. Linking and choosing a destination are done by clicking the map
/// (Escape when done); the selected lever shows its links in cyan.
/// </summary>
public partial class MainWindow
{
    /// <summary>Waiting for a click on the map (linking a lever, a ladder's destination); null = not picking.</summary>
    private Action<int, int>? _objectPick;
    private readonly List<UIElement> _linkVisuals = new();

    private void AddNewTypeMenuItems(ContextMenu menu, WorldObject obj, ObjectInstance existing)
    {
        switch (obj.Type)
        {
            case ObjectType.Window when obj.Window.HasShutters:
            {
                bool open = existing.StartsOpen ?? obj.Window.StartsOpen;
                AddStateItem(menu, "Shutters start open", open, () => existing.StartsOpen = true, existing);
                AddStateItem(menu, "Shutters start closed", !open, () => existing.StartsOpen = false, existing);
                break;
            }
            case ObjectType.Plant:
            {
                var stages = new MenuItem { Header = "Starting stage" };
                int current = existing.StartStage ?? 0;
                for (int i = 0; i < obj.Plant.Stages.Count; i++)
                {
                    int stage = i;
                    var item = new MenuItem { Header = i == obj.Plant.Stages.Count - 1 ? $"Stage {i + 1} (ripe)" : $"Stage {i + 1}", IsCheckable = true, IsChecked = current == i };
                    item.Click += (_, _) => { existing.StartStage = stage; PlaceObjectVisual(existing); StatusText.Text = $"\"{obj.Name}\" starts at stage {stage + 1}."; };
                    stages.Items.Add(item);
                }
                menu.Items.Add(stages);
                break;
            }
            case ObjectType.Lever:
            {
                if (obj.Lever.Kind != LeverKind.Button)
                {
                    bool on = existing.StartsOpen ?? obj.Lever.StartsOn;
                    AddStateItem(menu, "Starts on", on, () => existing.StartsOpen = !on, existing);
                }
                var link = new MenuItem { Header = $"Link to... ({existing.LinkedInstanceIds.Count} linked)" };
                link.Click += (_, _) => BeginLinking(existing, obj);
                menu.Items.Add(link);
                var clear = new MenuItem { Header = "Clear links", IsEnabled = existing.LinkedInstanceIds.Count > 0 };
                clear.Click += (_, _) => { existing.LinkedInstanceIds.Clear(); UpdateObjectSelectionVisual(); StatusText.Text = $"\"{obj.Name}\" is linked to nothing now."; };
                menu.Items.Add(clear);
                break;
            }
            case ObjectType.Stairs when obj.Stairs.Kind == StairsKind.Ladder:
            {
                string where = existing.DestinationX >= 0 ? $"({existing.DestinationX}, {existing.DestinationY}), lift {existing.DestinationLift}" : "not set";
                var destination = new MenuItem { Header = $"Ladder destination... ({where})" };
                destination.Click += (_, _) => BeginPickingDestination(existing, obj);
                menu.Items.Add(destination);
                var lift = new MenuItem { Header = $"Destination lift ({existing.DestinationLift})...", IsEnabled = existing.DestinationX >= 0 };
                lift.Click += (_, _) =>
                {
                    var text = SimpleInputWindow.Show(this, "Lift to arrive at (0 = the ground; Exult: 5 a storey):", "Ladder destination", existing.DestinationLift.ToString());
                    if (int.TryParse(text, out int n)) existing.DestinationLift = Math.Clamp(n, 0, 60);
                };
                menu.Items.Add(lift);
                break;
            }
            case ObjectType.LiquidContainer:
            {
                var full = new MenuItem { Header = "Starts full of" };
                int current = ObjectLooks.StartLiquid(obj, existing);
                var empty = new MenuItem { Header = "(empty)", IsCheckable = true, IsChecked = current < 0 };
                empty.Click += (_, _) => { existing.StartsFullOfId = -1; PlaceObjectVisual(existing); };
                full.Items.Add(empty);
                foreach (var liquid in obj.Liquid.Liquids)
                {
                    if (_objectDatabase.Objects.FirstOrDefault(o => o.Id == liquid.FoodId) is not { } food) continue;
                    int id = food.Id;
                    var item = new MenuItem { Header = food.Name, IsCheckable = true, IsChecked = current == id };
                    item.Click += (_, _) => { existing.StartsFullOfId = id; PlaceObjectVisual(existing); StatusText.Text = $"\"{obj.Name}\" starts full of {food.Name}."; };
                    full.Items.Add(item);
                }
                menu.Items.Add(full);
                break;
            }
        }
    }

    // ---------- Picking on the map ----------

    private void BeginLinking(ObjectInstance lever, WorldObject obj)
    {
        SelectObjectInstance(lever);
        StatusText.Text = $"Linking \"{obj.Name}\": click the doors, lights, explosives or other objects it works (click a linked one again to unlink). Escape when done.";
        _objectPick = (x, y) =>
        {
            if (ObjectAt(x, y) is not { } target || ReferenceEquals(target, lever))
            {
                StatusText.Text = "No object there. Click an object to link it, or Escape when done.";
                return;
            }
            string name = ObjectName(target);
            if (lever.LinkedInstanceIds.Remove(target.InstanceId)) StatusText.Text = $"Unlinked \"{name}\" ({lever.LinkedInstanceIds.Count} linked). Escape when done.";
            else { lever.LinkedInstanceIds.Add(target.InstanceId); StatusText.Text = $"Linked \"{name}\" ({lever.LinkedInstanceIds.Count} linked). Escape when done."; }
            UpdateObjectSelectionVisual();
        };
    }

    private void BeginPickingDestination(ObjectInstance ladder, WorldObject obj)
    {
        StatusText.Text = $"Click the cell \"{obj.Name}\" takes the player to. Escape to cancel.";
        _objectPick = (x, y) =>
        {
            ladder.DestinationX = x;
            ladder.DestinationY = y;
            EndObjectPick($"\"{obj.Name}\" now leads to ({x}, {y}), lift {ladder.DestinationLift} (right-click it to change the lift).");
        };
    }

    private void EndObjectPick(string message)
    {
        _objectPick = null;
        StatusText.Text = message;
    }

    // ---------- A lever's links, shown while it's selected ----------

    private void ClearLinkVisuals()
    {
        foreach (var visual in _linkVisuals) MapCanvas.Children.Remove(visual);
        _linkVisuals.Clear();
    }

    private void ShowLinksOf(ObjectInstance selected)
    {
        if (_map is null || selected.LinkedInstanceIds.Count == 0 || !_mapObjectVisuals.TryGetValue(selected.InstanceId, out var from)) return;
        var start = new Point(Canvas.GetLeft(from) + from.Width / 2, Canvas.GetTop(from) + from.Height / 2);
        foreach (int id in selected.LinkedInstanceIds)
        {
            if (!_mapObjectVisuals.TryGetValue(id, out var to)) continue;
            var outline = new Border { BorderBrush = Brushes.Cyan, BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Width = to.Width + 4, Height = to.Height + 4 };
            Canvas.SetLeft(outline, Canvas.GetLeft(to) - 2);
            Canvas.SetTop(outline, Canvas.GetTop(to) - 2);
            Panel.SetZIndex(outline, 17);
            var line = new Line
            {
                X1 = start.X, Y1 = start.Y, X2 = Canvas.GetLeft(to) + to.Width / 2, Y2 = Canvas.GetTop(to) + to.Height / 2,
                Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false
            };
            Panel.SetZIndex(line, 17);
            MapCanvas.Children.Add(outline);
            MapCanvas.Children.Add(line);
            _linkVisuals.Add(outline);
            _linkVisuals.Add(line);
        }
    }
}
