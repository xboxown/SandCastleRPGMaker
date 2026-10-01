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

        if (existing is not null)
        {
            if (int.TryParse(existing.Get("SoundResourceId"), out int id)) SoundCombo.SelectedValue = id;
            VolumeBox.Text = string.IsNullOrEmpty(existing.Get("Volume")) ? "1.0" : existing.Get("Volume");
        }
        else
        {
            VolumeBox.Text = "1.0";
            if (sounds.Count > 0) SoundCombo.SelectedIndex = 0;
        }
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
        command.Parameters["Volume"] = VolumeBox.Text;
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
