using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class AddCommandPickerWindow : Window
{
    public EventCommandType? ChosenType { get; private set; }

    public AddCommandPickerWindow()
    {
        InitializeComponent();
    }

    private void ShowText_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ShowText;
        DialogResult = true;
    }

    private void PlaySound_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.PlaySound;
        DialogResult = true;
    }

    private void PlayMusic_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.PlayMusic;
        DialogResult = true;
    }

    private void SetSwitch_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.SetSwitch;
        DialogResult = true;
    }

    private void SetVariable_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.SetVariable;
        DialogResult = true;
    }

    private void ConditionalBranch_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ConditionalBranchSwitch; // exact Switch-vs-Variable choice is made inside ConditionalBranchCommandWindow itself
        DialogResult = true;
    }

    private void ShakeScreen_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ShakeScreen;
        DialogResult = true;
    }

    private void ChangeDayNight_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeDayNight;
        DialogResult = true;
    }

    private void ApplyPaletteColor_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ApplyPaletteColor;
        DialogResult = true;
    }

    private void ChangeSelfBookContent_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeSelfBookContent;
        DialogResult = true;
    }

    private void ChangeExternalBookContent_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeExternalBookContent;
        DialogResult = true;
    }

    private void ChangeBookSettings_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeBookSettings;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
