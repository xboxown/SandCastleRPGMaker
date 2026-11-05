using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class AddCommandPickerWindow : Window
{
    public EventCommandType? ChosenType { get; private set; }

    public AddCommandPickerWindow()
    {
        InitializeComponent();
        AddScriptTabs();
    }

    /// <summary>The script commands' tabs (Engine/ScriptCommands.cs): each tab's groups in two columns, a button per command.</summary>
    private void AddScriptTabs()
    {
        foreach (var tab in ScriptCommands.All.Where(s => s.Tab.Length > 0 && (!ScriptCommands.IsUltima8Only(s.Tab) || ProjectSession.IsUltima8)).GroupBy(s => s.Tab))
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 10, 8, 0) };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            var columns = new[] { new System.Windows.Controls.StackPanel { Margin = new Thickness(0, 0, 10, 0) }, new System.Windows.Controls.StackPanel() };
            System.Windows.Controls.Grid.SetColumn(columns[1], 1);
            grid.Children.Add(columns[0]);
            grid.Children.Add(columns[1]);
            var groups = tab.GroupBy(s => s.Group).ToList();
            int half = (tab.Count() + 1) / 2, placed = 0;
            foreach (var group in groups)
            {
                var column = columns[placed >= half && placed > 0 ? 1 : 0];
                column.Children.Add(new System.Windows.Controls.TextBlock { Text = group.Key, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
                foreach (var spec in group)
                {
                    var button = new System.Windows.Controls.Button
                    {
                        Content = spec.Name + (spec.HasWindow ? "..." : ""),
                        Tag = spec.Type.ToString(),
                        ToolTip = new System.Windows.Controls.TextBlock { Text = spec.Help, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    button.Click += Registered_Click;
                    column.Children.Add(button);
                    placed++;
                }
                ((FrameworkElement)column.Children[^1]).Margin = new Thickness(0, 0, 0, 12);
            }
            Tabs.Items.Add(new System.Windows.Controls.TabItem
            {
                Header = tab.Key,
                Content = new System.Windows.Controls.ScrollViewer { Content = grid, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled }
            });
        }
    }

    /// <summary>A command from CommandWindowRegistry: its button's Tag is its EventCommandType name.</summary>
    private void Registered_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name } || !Enum.TryParse<EventCommandType>(name, out var type)) return;
        ChosenType = type;
        DialogResult = true;
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

    private void StopSound_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.StopSound;
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

    private void AdvancedIf_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.AdvancedIf;
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

    private void ChangeWeather_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ChangeWeather;
        DialogResult = true;
    }

    private void ExchangeCurrency_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.ExchangeCurrency;
        DialogResult = true;
    }

    private void SleepOnBed_Click(object sender, RoutedEventArgs e)
    {
        ChosenType = EventCommandType.SleepOnBed;
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
