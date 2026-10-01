using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ShowTextCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.ShowText };

    public ShowTextCommandWindow(EventCommand? existing = null)
    {
        InitializeComponent();

        if (existing is not null)
        {
            TextBox1.Text = existing.Get("Text");
            SelectComboByContent(BackgroundCombo, existing.Get("Background"), fallbackIndex: 0);
            SelectComboByContent(PositionCombo, existing.Get("Position"), fallbackIndex: 2);
        }
    }

    private static void SelectComboByContent(ComboBox combo, string value, int fallbackIndex)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Content as string, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = fallbackIndex;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var command = new EventCommand { Type = EventCommandType.ShowText };
        command.Parameters["Text"] = TextBox1.Text;
        command.Parameters["Background"] = (BackgroundCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Window";
        command.Parameters["Position"] = (PositionCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Bottom";
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
