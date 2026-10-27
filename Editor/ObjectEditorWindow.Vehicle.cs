using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Object Editor > Vehicle part (every type): what the object is for in a
/// vehicle (Vehicles tab) - its driver's seat, its controls, a wheel - and
/// its look facing each way, as the vehicle turns (Exult's rotated frames).
/// </summary>
public partial class ObjectEditorWindow
{
    private void BuildVehiclePart(WorldObject obj)
    {
        var body = Group("Vehicle part (for vehicles - Map Editor > Vehicles tab)");
        body.Children.Add(Note("Vehicles are built facing south (toward you). Pieces that look different facing another way need a look for each way: the vehicle turns in quarter turns, every piece swinging round its middle and showing its look for the new direction (its width and depth swap facing east or west). Leave them empty for pieces that look the same every way."));
        var roles = new[] { "Nothing special", "Driver's seat (sit in it to drive - a Chair)", "Controls (double-click to drive: a helm, a lever)", "Wheel or draft animal (its Moving look while it rolls)" };
        var role = new ComboBox { Width = 340, ItemsSource = roles, SelectedIndex = (int)obj.VehicleRole };
        role.SelectionChanged += (_, _) => { obj.VehicleRole = (VehiclePartRole)Math.Max(0, role.SelectedIndex); ShowCurrent(); };
        body.Children.Add(Labeled("What it is in a vehicle", role));
        if (obj.VehicleRole == VehiclePartRole.DriverSeat && obj.Type != ObjectType.Chair)
            body.Children.Add(Note("A driver's seat must be a Chair - make a Chair object for it."));
        if (obj.VehicleRole == VehiclePartRole.Wheel)
            body.Children.Add(SpritePicker("Moving look (an animation: the wheel turning, the horse trotting)", () => (obj.MovingLook.SpriteGroup, obj.MovingLook.SpriteName),
                (g, n) => { obj.MovingLook.SpriteGroup = g; obj.MovingLook.SpriteName = n; }, identity: SpriteFolderIdentity.Vehicle));
        var looks = new WrapPanel();
        foreach (var (direction, label) in new[] { (SpriteDirection.North, "Facing north"), (SpriteDirection.East, "Facing east"), (SpriteDirection.South, "Facing south (as built - empty: its own sprite)"), (SpriteDirection.West, "Facing west") })
        {
            looks.Children.Add(SpritePicker(label, () => obj.TurnLooks.TryGetValue(direction, out var l) ? (l.SpriteGroup, l.SpriteName) : ("", ""),
                (g, n) =>
                {
                    if (string.IsNullOrEmpty(n)) obj.TurnLooks.Remove(direction);
                    else obj.TurnLooks[direction] = new SpriteLink { SpriteGroup = g, SpriteName = n };
                }, compact: true, identity: SpriteFolderIdentity.Vehicle));
        }
        body.Children.Add(looks);
    }
}
