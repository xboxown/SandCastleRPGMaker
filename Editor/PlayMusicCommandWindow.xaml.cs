using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Deliberately honest V1: this plays the track once via the same
/// SoundPlayback everything else uses - it does NOT loop continuously or
/// stop a previously-playing track the way real background music should.
/// Proper background-music semantics (loop, replace-on-next-play) is a
/// real, separate piece of engineering, not something to fake with a
/// decorative checkbox that wouldn't actually do anything yet.
/// </summary>
public partial class PlayMusicCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.PlayMusic };

    public PlayMusicCommandWindow(ResourceDatabase resourceDatabase, EventCommand? existing = null)
    {
        InitializeComponent();

        var music = resourceDatabase.Entries.Where(e => e.Kind == ResourceKind.Music).ToList();
        MusicCombo.ItemsSource = music;
        MusicCombo.DisplayMemberPath = "Name";
        MusicCombo.SelectedValuePath = "Id";
        EmptyListNote.Visibility = music.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (existing is not null)
        {
            if (int.TryParse(existing.Get("MusicResourceId"), out int id)) MusicCombo.SelectedValue = id;
            VolumeBox.Text = string.IsNullOrEmpty(existing.Get("Volume")) ? "1.0" : existing.Get("Volume");
            LoopBox.IsChecked = existing.Get("Loop") == "True";
        }
        else
        {
            VolumeBox.Text = "1.0";
            LoopBox.IsChecked = true;
            if (music.Count > 0) MusicCombo.SelectedIndex = 0;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (MusicCombo.SelectedValue is not int id)
        {
            MessageBox.Show("Pick a music track - add one in the Sound Editor first if the list is empty.", "Play Music", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new EventCommand { Type = EventCommandType.PlayMusic };
        command.Parameters["MusicResourceId"] = id.ToString();
        command.Parameters["Volume"] = VolumeBox.Text;
        command.Parameters["Loop"] = LoopBox.IsChecked == true ? "True" : "False";
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
