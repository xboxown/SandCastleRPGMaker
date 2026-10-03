using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ShowTextCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.ShowText };

    /// <summary>Speaker choices: key stored in the command ("" none, "Self", "Player", or a character id) and what the list shows.</summary>
    private sealed record SpeakerChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    public ShowTextCommandWindow(EventCommand? existing = null)
    {
        InitializeComponent();
        var speakers = new List<SpeakerChoice>
        {
            new("", "(None - narration)"),
            new("Self", "(Self - the character this event belongs to, or who set it off)"),
            new("Player", "(Main Player)")
        };
        speakers.AddRange(ProjectSession.Characters.Characters.Select(c => new SpeakerChoice(c.Id.ToString(), c.Name)));
        SpeakerCombo.ItemsSource = speakers;
        SpeakerCombo.SelectedItem = speakers.FirstOrDefault(s => s.Key == (existing?.Get("SpeakerId") ?? "")) ?? speakers[0];
        if (existing?.Get("FaceSide") == "Right") FaceSideCombo.SelectedIndex = 1;

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
        command.Parameters["SpeakerId"] = (SpeakerCombo.SelectedItem as SpeakerChoice)?.Key ?? "";
        command.Parameters["FaceSide"] = FaceSideCombo.SelectedIndex == 1 ? "Right" : "Left";
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
