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
        if (ProjectSession.IsAppleIIgs) Loaded += (_, _) => HideForAppleIIgs(this); // Ultima 6 Apple IIgs mode: no videos on the IIgs
        if (ProjectSession.IsUltima6) Loaded += (_, _) => HideTags(this, ScriptCommands.NotInUltima6.Select(t => t.ToString()).ToHashSet()); // Ultima 7's own commands: not in Ultima 6
        Loaded += (_, _) => { CollectCommands(); SearchBox.Focus(); };
        SearchBox.TextChanged += (_, _) => Search();
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Down && SearchResults.Items.Count > 0) { SearchResults.SelectedIndex = Math.Min(SearchResults.SelectedIndex + 1, SearchResults.Items.Count - 1); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Up && SearchResults.Items.Count > 0) { SearchResults.SelectedIndex = Math.Max(SearchResults.SelectedIndex - 1, 0); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Enter && SearchResults.Visibility == Visibility.Visible) { PickResult(); e.Handled = true; }
        };
        SearchResults.MouseDoubleClick += (_, _) => PickResult();
        SearchResults.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) PickResult(); };
    }

    /// <summary>A command offered: its button (clicked when picked), name, tab, group and description.</summary>
    private sealed record CommandEntry(System.Windows.Controls.Button Button, string Name, string Tab, string Group, string Help)
    {
        public override string ToString() => $"{Name}    ({Tab} > {Group})";
    }

    private readonly List<CommandEntry> _commands = new();

    /// <summary>Every command button on every tab (not those hidden in this mode), with its tab and group heading.</summary>
    private void CollectCommands()
    {
        _commands.Clear();
        foreach (var tab in Tabs.Items.OfType<System.Windows.Controls.TabItem>())
        {
            string group = "";
            void Walk(DependencyObject node)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                {
                    if (child is System.Windows.Controls.TextBlock { FontWeight: var fw } heading && fw == FontWeights.Bold) group = heading.Text;
                    else if (child is System.Windows.Controls.Button { Visibility: Visibility.Visible } b && b.Content is string text)
                    {
                        string help = b.ToolTip switch { string t => t, System.Windows.Controls.TextBlock tb => tb.Text, _ => "" };
                        _commands.Add(new CommandEntry(b, text.TrimEnd('.'), tab.Header?.ToString() ?? "", group, help));
                        continue;
                    }
                    Walk(child);
                }
            }
            Walk(tab);
        }
    }

    /// <summary>The commands with any of the words typed - in the name first, then the group or tab, then the description.</summary>
    private void Search()
    {
        var words = SearchBox.Text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToList();
        if (words.Count == 0) { SearchResults.Visibility = Visibility.Collapsed; Tabs.Visibility = Visibility.Visible; return; }
        int Score(CommandEntry c)
        {
            int score = 0;
            string name = c.Name.ToLowerInvariant(), place = (c.Tab + " " + c.Group).ToLowerInvariant(), help = c.Help.ToLowerInvariant();
            foreach (var w in words)
            {
                if (name.Contains(w)) score += name.StartsWith(w) ? 120 : 100;
                else if (place.Contains(w)) score += 30;
                else if (help.Contains(w)) score += 10;
            }
            return score;
        }
        var found = _commands.Select(c => (c, s: Score(c))).Where(x => x.s > 0).OrderByDescending(x => x.s).ThenBy(x => x.c.Name).Select(x => x.c).ToList();
        SearchResults.ItemsSource = found;
        if (found.Count > 0) SearchResults.SelectedIndex = 0;
        SearchResults.Visibility = Visibility.Visible;
        Tabs.Visibility = Visibility.Collapsed;
    }

    private void PickResult()
    {
        if (SearchResults.SelectedItem is CommandEntry c) c.Button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, c.Button));
    }

    /// <summary>Ultima 6 Apple IIgs mode: the IIgs plays no video - Play Video isn't offered.</summary>
    private static void HideForAppleIIgs(DependencyObject root) => HideLogical(root);

    /// <summary>The buttons of these commands hidden (their Tag is the command's type).</summary>
    private static void HideTags(DependencyObject root, HashSet<string> tags)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is System.Windows.Controls.Button { Tag: string tag } b && tags.Contains(tag)) b.Visibility = Visibility.Collapsed;
            HideTags(child, tags);
        }
    }

    private static void HideLogical(DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is System.Windows.Controls.Button { Tag: "PlayVideo" } b) b.Visibility = Visibility.Collapsed;
            HideLogical(child);
        }
    }

    /// <summary>The script commands' tabs (Engine/ScriptCommands.cs): each tab's groups in two columns, a button per command.</summary>
    private void AddScriptTabs()
    {
        foreach (var tab in ScriptCommands.All.Where(s => s.Tab.Length > 0 && (!ScriptCommands.IsUltima8Only(s.Tab) || ProjectSession.IsUltima8) && (!ScriptCommands.IsUltima6Only(s.Tab) || ProjectSession.IsUltima6)
                                                     && !(ProjectSession.IsUltima6 && ScriptCommands.NotInUltima6.Contains(s.Type))).GroupBy(s => s.Tab))
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
