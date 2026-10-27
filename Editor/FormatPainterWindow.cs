using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Format Painter (as a word processor's brush): one thing's settings copied onto others in one go -
/// a character's inventory paperdoll or stats paperdoll onto every other character, one wall's size onto
/// twenty walls. On the left the settings (with their parts) to paste, ticked or not; on the right
/// what to paste them onto. Each setting pasted is a copy of the source's own, so the two never share it.
/// </summary>
internal sealed class FormatPainterWindow : Window
{
    /// <summary>One setting: a property path from the thing ("Paperdoll" or "Paperdoll.StatsDoll").</summary>
    private sealed class Setting
    {
        public required string Path;
        public required CheckBox Box;
        public List<Setting> Parts = new();
    }

    private readonly List<Setting> _settings = new();
    private readonly List<(object Item, CheckBox Box, string Group)> _targets = new();

    private FormatPainterWindow() { }

    /// <summary>
    /// Paints the chosen settings of source onto the chosen ones of all. skip: settings never offered (its id, its
    /// name); unticked: offered but not ticked at first (its look, its events); only: offer just these settings.
    /// groupOf: what kind each one is (an object's type), for "only of the same kind". True when something was pasted.
    /// </summary>
    public static bool Paint<T>(Window owner, T source, IEnumerable<T> all, Func<T, string> nameOf, string title,
        IEnumerable<string>? skip = null, IEnumerable<string>? unticked = null, IEnumerable<string>? only = null, Func<T, string>? groupOf = null) where T : class
    {
        var window = new FormatPainterWindow { Owner = owner, Title = $"Format Painter - {title}", Width = 860, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var skipped = new HashSet<string>(skip ?? Enumerable.Empty<string>()) { "Id" };
        var notTicked = new HashSet<string>(unticked ?? Enumerable.Empty<string>());
        var onlyThese = only?.ToHashSet();

        // ---- the settings ----
        var tree = new StackPanel();
        foreach (var property in Settable(typeof(T)))
        {
            if (skipped.Contains(property.Name) || onlyThese is not null && !onlyThese.Contains(property.Name) && !onlyThese.Any(o => o.StartsWith(property.Name + "."))) continue;
            var setting = new Setting { Path = property.Name, Box = new CheckBox { Content = Words(property.Name), IsChecked = !notTicked.Contains(property.Name), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) } };
            tree.Children.Add(setting.Box);
            if (IsPlainClass(property.PropertyType))
            {
                var parts = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
                foreach (var part in Settable(property.PropertyType))
                {
                    string path = property.Name + "." + part.Name;
                    if (skipped.Contains(path) || onlyThese is not null && !onlyThese.Contains(property.Name) && !onlyThese.Contains(path)) continue;
                    var child = new Setting { Path = path, Box = new CheckBox { Content = Words(part.Name), IsChecked = setting.Box.IsChecked == true && !notTicked.Contains(path), Margin = new Thickness(0, 1, 0, 0) } };
                    setting.Parts.Add(child);
                    parts.Children.Add(child.Box);
                }
                if (setting.Parts.Count > 0)
                {
                    tree.Children.Add(parts);
                    // The whole setting ticks / unticks its parts.
                    setting.Box.Click += (_, _) => { foreach (var child in setting.Parts) child.Box.IsChecked = setting.Box.IsChecked; };
                    foreach (var child in setting.Parts) child.Box.Click += (_, _) => setting.Box.IsChecked = setting.Parts.All(c => c.Box.IsChecked == true) ? true : setting.Parts.Any(c => c.Box.IsChecked == true) ? null : false;
                }
            }
            window._settings.Add(setting);
        }

        // ---- what to paste onto ----
        var targetList = new StackPanel();
        string sourceGroup = groupOf?.Invoke(source) ?? "";
        foreach (var item in all.Where(i => !ReferenceEquals(i, source)))
        {
            string group = groupOf?.Invoke(item) ?? "";
            var box = new CheckBox { Content = group.Length > 0 ? $"{nameOf(item)}   ({group})" : nameOf(item), Margin = new Thickness(0, 1, 0, 1) };
            window._targets.Add((item, box, group));
            targetList.Children.Add(box);
        }
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 4) };
        var sameKind = new CheckBox { Content = $"Only {sourceGroup}", IsChecked = sourceGroup.Length > 0, Margin = new Thickness(0, 0, 0, 4), Visibility = sourceGroup.Length > 0 ? Visibility.Visible : Visibility.Collapsed };
        void Filter()
        {
            string words = search.Text.Trim();
            foreach (var (item, box, group) in window._targets)
                box.Visibility = (words.Length == 0 || box.Content?.ToString()?.Contains(words, StringComparison.OrdinalIgnoreCase) == true)
                                 && (sameKind.IsChecked != true || group == sourceGroup) ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => Filter();
        sameKind.Click += (_, _) => Filter();
        Filter();
        var allShown = new Button { Content = "Tick all shown", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 4) };
        allShown.Click += (_, _) => { foreach (var t in window._targets.Where(t => t.Box.Visibility == Visibility.Visible)) t.Box.IsChecked = true; };
        var none = new Button { Content = "Untick all", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 0, 4) };
        none.Click += (_, _) => { foreach (var t in window._targets) t.Box.IsChecked = false; };

        // ---- layout ----
        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var leftHead = new TextBlock { Text = $"Settings of \"{nameOf(source)}\" to paste", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(leftHead);
        var left = new ScrollViewer { Content = tree, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(left, 1);
        grid.Children.Add(left);
        var rightHead = new StackPanel();
        rightHead.Children.Add(new TextBlock { Text = "Paste onto", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        var searchRow = new DockPanel();
        searchRow.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
        searchRow.Children.Add(search);
        rightHead.Children.Add(searchRow);
        rightHead.Children.Add(sameKind);
        var tickRow = new StackPanel { Orientation = Orientation.Horizontal };
        tickRow.Children.Add(allShown);
        tickRow.Children.Add(none);
        rightHead.Children.Add(tickRow);
        var rightPanel = new DockPanel();
        DockPanel.SetDock(rightHead, Dock.Top);
        rightPanel.Children.Add(rightHead);
        rightPanel.Children.Add(new ScrollViewer { Content = targetList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(rightPanel, 2); Grid.SetRowSpan(rightPanel, 2);
        grid.Children.Add(rightPanel);
        var paste = new Button { Content = "Paste", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(paste);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2); Grid.SetColumnSpan(buttons, 3);
        grid.Children.Add(buttons);
        window.Content = grid;

        bool pasted = false;
        paste.Click += (_, _) =>
        {
            var paths = window._settings.SelectMany(s => s.Parts.Count == 0 ? (s.Box.IsChecked == true ? new[] { s.Path } : Array.Empty<string>())
                : s.Box.IsChecked == true ? new[] { s.Path } : s.Parts.Where(p => p.Box.IsChecked == true).Select(p => p.Path).ToArray()).ToList();
            var targets = window._targets.Where(t => t.Box.IsChecked == true && t.Box.Visibility == Visibility.Visible).Select(t => t.Item).ToList();
            if (paths.Count == 0 || targets.Count == 0) { MessageBox.Show(window, "Tick at least one setting and one thing to paste onto.", "Format Painter", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (MessageBox.Show(window, $"Paste {paths.Count} setting(s) of \"{nameOf(source)}\" onto {targets.Count} other(s)? What they had there is replaced.", "Format Painter",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            foreach (var target in targets)
                foreach (var path in paths) CopyPath(source, target, path);
            pasted = true;
            window.DialogResult = true;
        };
        window.ShowDialog();
        return pasted;
    }

    /// <summary>The properties a thing's settings are: public, read and write, not indexers.</summary>
    private static IEnumerable<PropertyInfo> Settable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.GetSetMethod() is not null);

    /// <summary>A setting with parts of its own (a paperdoll, a size), not a list or a plain value.</summary>
    private static bool IsPlainClass(Type type) =>
        type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type) && type.Namespace?.StartsWith("SandcastleRpgMaker") == true;

    /// <summary>"PaperDollSpriteName" as "Paper Doll Sprite Name".</summary>
    private static string Words(string name) => System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

    /// <summary>Copies the source's value at a path ("A" or "A.B") onto the target - a copy of its own (through JSON).</summary>
    private static void CopyPath(object source, object target, string path)
    {
        var names = path.Split('.');
        object? from = source, to = target;
        for (int i = 0; i < names.Length - 1; i++)
        {
            var step = from!.GetType().GetProperty(names[i])!;
            from = step.GetValue(from);
            var next = step.GetValue(to);
            if (from is null) return;
            if (next is null) { next = Clone(from, step.PropertyType); step.SetValue(to, next); return; } // the target had none: the whole of it
            to = next;
        }
        var last = from!.GetType().GetProperty(names[^1])!;
        last.SetValue(to, Clone(last.GetValue(from), last.PropertyType));
    }

    private static object? Clone(object? value, Type type)
    {
        if (value is null || type.IsValueType || type == typeof(string)) return value;
        return JsonSerializer.Deserialize(JsonSerializer.Serialize(value, type), type);
    }
}
