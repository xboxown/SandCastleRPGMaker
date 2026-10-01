using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ShakeScreenCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.ShakeScreen };

    public ShakeScreenCommandWindow(EventCommand? existing = null)
    {
        InitializeComponent();

        if (existing is not null)
        {
            if (double.TryParse(existing.Get("Intensity"), out double intensity)) IntensitySlider.Value = intensity;
            DurationBox.Text = existing.Get("DurationSeconds");
            AffectsCharactersCheck.IsChecked = existing.Get("AffectsCharacters") == "True";
        }
        else
        {
            IntensitySlider.Value = 5;
            DurationBox.Text = "1.0";
        }
    }

    private void IntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IntensityValueLabel is not null) IntensityValueLabel.Text = ((int)IntensitySlider.Value).ToString();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(DurationBox.Text, out double duration) || duration <= 0)
        {
            MessageBox.Show("Duration needs to be a positive number of seconds.", "Shake Screen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new EventCommand { Type = EventCommandType.ShakeScreen };
        command.Parameters["Intensity"] = ((int)IntensitySlider.Value).ToString();
        command.Parameters["DurationSeconds"] = duration.ToString();
        command.Parameters["AffectsCharacters"] = (AffectsCharactersCheck.IsChecked == true).ToString();
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
