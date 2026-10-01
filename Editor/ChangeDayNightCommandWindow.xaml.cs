using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ChangeDayNightCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.ChangeDayNight };

    public ChangeDayNightCommandWindow(EventCommand? existing = null)
    {
        InitializeComponent();

        if (existing is not null)
        {
            PeriodCombo.SelectedIndex = existing.Get("Period") switch { "Afternoon" => 1, "Evening" => 2, _ => 0 };
        }
        else
        {
            PeriodCombo.SelectedIndex = 0;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        string period = PeriodCombo.SelectedIndex switch { 1 => "Afternoon", 2 => "Evening", _ => "Morning" };
        var command = new EventCommand { Type = EventCommandType.ChangeDayNight };
        command.Parameters["Period"] = period;
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
