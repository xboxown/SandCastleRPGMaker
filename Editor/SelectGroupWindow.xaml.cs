using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SandcastleRpgMaker.Editor;

public partial class SelectGroupWindow : Window
{
    public string? SelectedGroup { get; private set; }

    public SelectGroupWindow(List<string> groupNames, string? currentGroup)
    {
        InitializeComponent();
        GroupListBox.ItemsSource = groupNames;
        if (currentGroup is not null && groupNames.Contains(currentGroup))
            GroupListBox.SelectedItem = currentGroup;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedGroup = GroupListBox.SelectedItem as string;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void GroupListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupListBox.SelectedItem is not null)
        {
            SelectedGroup = GroupListBox.SelectedItem as string;
            DialogResult = true;
        }
    }

    /// <summary>Shows the picker modally. Returns the chosen group name, or null if cancelled/nothing picked.</summary>
    public static string? Show(Window owner, List<string> groupNames, string? currentGroup)
    {
        var window = new SelectGroupWindow(groupNames, currentGroup) { Owner = owner };
        bool? result = window.ShowDialog();
        return result == true ? window.SelectedGroup : null;
    }
}
