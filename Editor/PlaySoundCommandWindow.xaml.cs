using System.Globalization;
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class PlaySoundCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.PlaySound };

    public PlaySoundCommandWindow(ResourceDatabase resourceDatabase, EventCommand? existing = null)
    {
        InitializeComponent();

        var sounds = resourceDatabase.Entries.Where(e => e.Kind == ResourceKind.Sound).ToList();
        SoundCombo.ItemsSource = sounds;
        SoundCombo.DisplayMemberPath = "Name";
        SoundCombo.SelectedValuePath = "Id";
        EmptyListNote.Visibility = sounds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Volume is stored as 0.0 - 1.0 (as before); the slider shows it as 0 - 100%.
        double volume = 1.0;
        if (existing is not null)
        {
            if (int.TryParse(existing.Get("SoundResourceId"), out int id)) SoundCombo.SelectedValue = id;
            if (double.TryParse(existing.Get("Volume"), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                || double.TryParse(existing.Get("Volume"), out v)) volume = Math.Clamp(v, 0, 1);
            LoopBox.IsChecked = existing.Get("Loop") == "True";
        }
        else if (sounds.Count > 0) SoundCombo.SelectedIndex = 0;
        VolumeSlider.Value = Math.Round(volume * 100);
        VolumeText.Text = $"{VolumeSlider.Value:0}%";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText is not null) VolumeText.Text = $"{e.NewValue:0}%";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (SoundCombo.SelectedValue is not int id)
        {
            MessageBox.Show("Pick a sound - add one in the Sound Editor first if the list is empty.", "Play Sound", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new EventCommand { Type = EventCommandType.PlaySound };
        command.Parameters["SoundResourceId"] = id.ToString();
        command.Parameters["Volume"] = (VolumeSlider.Value / 100.0).ToString("0.##", CultureInfo.InvariantCulture);
        command.Parameters["Loop"] = LoopBox.IsChecked == true ? "True" : "False";
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
