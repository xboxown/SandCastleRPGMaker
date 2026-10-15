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

    private void StopConversation_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.StopConversation;
        DialogResult = true;
    }

    private void Label_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.Label;
        DialogResult = true;
    }

    private void JumpToLabel_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.GoToLabel;
        DialogResult = true;
    }

    private void ExitEvent_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.End;
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

    private void ChangeFactionRelationship_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeFactionRelationship;
        DialogResult = true;
    }

    private void ChangeFaction_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeFaction;
        DialogResult = true;
    }

    private void Paperdoll_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.Paperdoll;
        DialogResult = true;
    }

    private void Teleport_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.Teleport;
        DialogResult = true;
    }

    private void ChangeBehavior_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeBehavior;
        DialogResult = true;
    }

    private void ChangeFoodLevel_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeFoodLevel;
        DialogResult = true;
    }

    private void ModifyAilments_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ModifyAilments;
        DialogResult = true;
    }

    private void Attack_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.Attack;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
