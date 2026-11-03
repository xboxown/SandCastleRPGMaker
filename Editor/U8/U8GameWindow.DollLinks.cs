// Ultima 8 engine (U8GameWindow) - the same as the Ultima 7 engine's GameTestWindow.DollLinks.cs.
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// PaperDoll Editor paperdolls brought up by an inventory (Character Editor > Paperdoll > Other paperdolls) - a button on
/// it, or opening and closing with it: Ultima 8's small hit points / mana bars - and the ones always on screen (PaperDoll
/// Editor > Always on screen): a HUD kept to a corner of the view, never pausing the game, never closed by Escape.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>The holder of a paperdoll an inventory brought up: one per character and paperdoll, so each keeps how it was left.</summary>
    private readonly Dictionary<(int DesignId, CharacterDefinition Character), FormOwner> _linkedOwners = new();
    private readonly Dictionary<int, FormOwner> _hudOwners = new();

    private DollDesign? DesignById(int id) => _objects.DollDesigns.FirstOrDefault(d => d.Id == id);

    private OpenDollGump? LinkedDollOpen(OpenPaperdollGump inventory, PaperdollDollLink link) =>
        _linkedOwners.TryGetValue((link.DesignId, inventory.Character), out var owner) ? _openDolls.FirstOrDefault(g => ReferenceEquals(g.Owner, owner)) : null;

    /// <summary>Opens the link's paperdoll for the inventory's character, beside the inventory (its Dollx / DollY, in the inventory's pixels).</summary>
    private void OpenLinkedDoll(OpenPaperdollGump inventory, PaperdollDollLink link)
    {
        if (DesignById(link.DesignId) is not { } design || LinkedDollOpen(inventory, link) is not null) return;
        var key = (link.DesignId, inventory.Character);
        if (!_linkedOwners.TryGetValue(key, out var owner)) _linkedOwners[key] = owner = new FormOwner();
        OpenDoll(owner, new WorldObject { Name = design.Name }, design);
        if (_openDolls.LastOrDefault(g => ReferenceEquals(g.Owner, owner)) is not { } gump) return;
        gump.Character = inventory.Character;
        gump.OpenedWith = inventory;
        gump.StatsShown.Clear();
        double scale = inventory.GumpBorder.ActualWidth > 0 ? inventory.GumpBorder.ActualWidth / 320 : ViewportScale.ScaleX;
        Canvas.SetLeft(gump.GumpBorder, Canvas.GetLeft(inventory.GumpBorder) + link.DollX * scale);
        Canvas.SetTop(gump.GumpBorder, Canvas.GetTop(inventory.GumpBorder) + link.DollY * scale);
        RefreshDollGump(gump);
    }

    /// <summary>The inventory has opened: its "opens with the inventory" paperdolls open too.</summary>
    private void OpenDollsWithInventory(OpenPaperdollGump inventory)
    {
        foreach (var link in inventory.Character.Paperdoll.DollLinks.Where(l => l.Way == DollLinkWay.WithInventory)) OpenLinkedDoll(inventory, link);
    }

    /// <summary>The inventory is closing: whatever it brought up closes with it.</summary>
    private void CloseDollsWithInventory(OpenPaperdollGump inventory)
    {
        foreach (var gump in _openDolls.Where(g => ReferenceEquals(g.OpenedWith, inventory)).ToList()) CloseDollGump(gump);
    }

    /// <summary>A link's button on the inventory: opens its paperdoll, or closes it if it's open.</summary>
    private void ToggleLinkedDoll(OpenPaperdollGump inventory, PaperdollDollLink link)
    {
        if (LinkedDollOpen(inventory, link) is { } open) CloseDollGump(open);
        else OpenLinkedDoll(inventory, link);
    }

    /// <summary>
    /// The always-on-screen paperdolls: opened when missing (the game started, a save loaded), kept to their corner (the
    /// view resized), closed when no longer wanted (at the title, after death). Checked with the paperdolls' stats.
    /// </summary>
    private void KeepHudDolls()
    {
        bool shown = !InTitle && !_gameOver;
        foreach (var design in _objects.DollDesigns)
        {
            if (!_hudOwners.TryGetValue(design.Id, out var owner)) _hudOwners[design.Id] = owner = new FormOwner();
            var gump = _openDolls.FirstOrDefault(g => ReferenceEquals(g.Owner, owner));
            if (!design.OnScreen || !shown)
            {
                if (gump is not null) CloseDollGump(gump);
                continue;
            }
            if (gump is null)
            {
                string said = StatusText.Text;
                OpenDoll(owner, new WorldObject { Name = design.Name }, design, title: true); // quietly - no paperdoll-open sound
                StatusText.Text = said; // nor "You open..."
                gump = _openDolls.LastOrDefault(g => ReferenceEquals(g.Owner, owner));
                if (gump is null) continue;
                gump.Hud = true;
                gump.Fixed = true;
                gump.Character = _player;
                UntrackGumpFocus(gump);
                Panel.SetZIndex(gump.GumpBorder, 50); // under every window
            }
            double scale = DollGumpScale, w = design.Width * scale, h = design.Height * scale;
            double viewW = PaperdollGumpsCanvas.ActualWidth, viewH = PaperdollGumpsCanvas.ActualHeight;
            double x = design.ScreenCorner is DollScreenCorner.TopRight or DollScreenCorner.BottomRight ? viewW - w - design.ScreenX * scale : design.ScreenX * scale;
            double y = design.ScreenCorner is DollScreenCorner.BottomLeft or DollScreenCorner.BottomRight ? viewH - h - design.ScreenY * scale : design.ScreenY * scale;
            Canvas.SetLeft(gump.GumpBorder, x);
            Canvas.SetTop(gump.GumpBorder, y);
        }
    }
}
