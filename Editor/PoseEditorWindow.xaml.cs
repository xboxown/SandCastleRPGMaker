using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class PoseEditorWindow : Window
{
    private readonly PoseDatabase _poses;

    public PoseEditorWindow(PoseDatabase poses)
    {
        InitializeComponent();
        _poses = poses;
        Refresh();
    }

    private void Refresh()
    {
        var selected = PosesListView.SelectedItem as PoseDefinition;
        PosesListView.ItemsSource = _poses.Poses.ToList();
        if (selected is not null)
            PosesListView.SelectedItem = ((List<PoseDefinition>)PosesListView.ItemsSource).FirstOrDefault(p => p.Id == selected.Id);
    }

    private void AddPose_Click(object sender, RoutedEventArgs e)
    {
        var name = SimpleInputWindow.Show(this, "Pose name:", "Add Custom Pose", "");
        if (string.IsNullOrWhiteSpace(name)) return;

        var newPose = new PoseDefinition { Id = _poses.GetNextId(), Name = name.Trim(), IsBuiltIn = false };
        _poses.Poses.Add(newPose);
        Refresh();
        PosesListView.SelectedItem = newPose;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (PosesListView.SelectedItem is not PoseDefinition selected)
        {
            MessageBox.Show("Select a pose first.", "Pose Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var name = SimpleInputWindow.Show(this, "Pose name:", "Rename Pose", selected.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        selected.Name = name.Trim();
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PosesListView.SelectedItem is not PoseDefinition selected)
        {
            MessageBox.Show("Select a pose first.", "Pose Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.IsBuiltIn)
        {
            MessageBox.Show($"\"{selected.Name}\" is one of the 11 built-in poses and can't be deleted - it can still be renamed.",
                "Pose Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Delete \"{selected.Name}\"? Any character with a sprite linked to it will simply find nothing there anymore.",
            "Delete Pose", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _poses.Poses.Remove(selected);
        Refresh();
    }
}
