using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The contents preview's Undo (Ctrl+Z) and right-click menu (Cut, Copy, Paste, Delete, Undo; Facing for what
/// faces a way) - in the Container Editor and a placed container's own contents (Map Editor). What the container
/// holds is kept as it was at each press: anything changed since is one step back.
/// </summary>
public partial class ContainerEditorWindow
{
    private readonly List<string> _contentsUndo = new();
    private string? _contentsBaseline;
    private ContainerData? _contentsUndoOf;
    private static readonly List<InventoryItemInstance> _contentsClipboard = new();

    private static string ContentsJson(List<InventoryItemInstance> contents) => JsonSerializer.Serialize(contents);

    /// <summary>What has changed since the last press becomes one step back.</summary>
    private void ContentsCheckpoint()
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        string now = ContentsJson(container.StartingContents);
        if (!ReferenceEquals(_contentsUndoOf, container)) { _contentsUndo.Clear(); _contentsUndoOf = container; _contentsBaseline = now; return; }
        if (now == _contentsBaseline) return;
        _contentsUndo.Add(_contentsBaseline!);
        if (_contentsUndo.Count > 100) _contentsUndo.RemoveAt(0);
        _contentsBaseline = now;
    }

    private void UndoContents()
    {
        if (ContainerListBox.SelectedItem is not ContainerData container) return;
        ContentsCheckpoint();
        if (_contentsUndo.Count == 0) return;
        string json = _contentsUndo[^1];
        _contentsUndo.RemoveAt(_contentsUndo.Count - 1);
        container.StartingContents = JsonSerializer.Deserialize<List<InventoryItemInstance>>(json) ?? new List<InventoryItemInstance>();
        _contentsBaseline = json;
        _selectedContentsItem = null;
        RefreshContentsList(container);
    }

    /// <summary>Ctrl+Z over the contents (not while typing in a box).</summary>
    private void ContentsUndoKeys(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || !ContentsPreviewCanvas.IsVisible) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z) { UndoContents(); e.Handled = true; }
    }

    private static InventoryItemInstance CopyOf(InventoryItemInstance item) => JsonSerializer.Deserialize<InventoryItemInstance>(JsonSerializer.Serialize(item))!;

    /// <summary>Right-click a thing in the preview (or the empty preview: item null).</summary>
    private void ShowContentsMenu(ContainerData container, InventoryItemInstance? item, FrameworkElement target, Point at)
    {
        ContentsCheckpoint();
        var menu = new ContextMenu { PlacementTarget = target };
        MenuItem Add(string header, bool enabled, Action click, string gesture = "")
        {
            var entry = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture };
            entry.Click += (_, _) => { ContentsCheckpoint(); click(); };
            menu.Items.Add(entry);
            return entry;
        }
        Add("Cut", item is not null, () => { _contentsClipboard.Clear(); _contentsClipboard.Add(CopyOf(item!)); container.StartingContents.Remove(item!); RefreshContentsList(container); });
        Add("Copy", item is not null, () => { _contentsClipboard.Clear(); _contentsClipboard.Add(CopyOf(item!)); });
        Add("Paste", _contentsClipboard.Count > 0, () =>
        {
            var bounds = container.GetFrameBounds();
            foreach (var copied in _contentsClipboard)
            {
                var pasted = CopyOf(copied);
                pasted.InstanceId = 0;
                pasted.ContainerX = Math.Clamp(at.X - 16, bounds.X, bounds.X + bounds.Width - 32);
                pasted.ContainerY = Math.Clamp(at.Y - 16, bounds.Y, bounds.Y + bounds.Height - 32);
                double volume = container.StartingContents.Sum(ResolveItemVolume) + ResolveItemVolume(pasted);
                if (volume > container.MaxVolume) { MessageBox.Show(this, $"This won't fit - the contents would total {volume:0.##}, but Max Volume is {container.MaxVolume:0.##}.", "Container Editor", MessageBoxButton.OK, MessageBoxImage.Warning); break; }
                container.StartingContents.Add(pasted);
            }
            RefreshContentsList(container);
        });
        Add("Delete", item is not null, () => { container.StartingContents.Remove(item!); RefreshContentsList(container); }, "Del");
        menu.Items.Add(new Separator());
        Add(_contentsUndo.Count > 0 ? $"Undo ({_contentsUndo.Count})" : "Undo", _contentsUndo.Count > 0, UndoContents, "Ctrl+Z");
        if (item is { Category: ReferencedItemCategory.Object } && _objects.Objects.FirstOrDefault(o => o.Id == item.ItemId) is { } faced && CanFace(faced))
        {
            // Which way it faces when it's taken out (a bedroll, a chair) - as on the map.
            var facingMenu = new MenuItem { Header = "Facing" };
            foreach (var direction in Enum.GetValues<SpriteDirection>())
            {
                bool hasLook = faced.Furniture.Facings.TryGetValue(direction, out var f) && f.Sprite.IsSet || faced.TurnLooks.TryGetValue(direction, out var t) && t.IsSet;
                var entry = new MenuItem { Header = hasLook ? direction.ToString() : $"{direction} (no sprite)", IsCheckable = true, IsChecked = item.FacingRuntime == direction };
                var d = direction;
                entry.Click += (_, _) => { ContentsCheckpoint(); item.FacingRuntime = d; BuildContentsPreview(container); };
                facingMenu.Items.Add(entry);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(facingMenu);
        }
        menu.IsOpen = true;
    }
}
