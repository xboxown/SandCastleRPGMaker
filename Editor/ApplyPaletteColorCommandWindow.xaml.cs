using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Same effect mechanism as Change Day/Night (fades BookPreviewWindow's
/// LightingOverlay), but instead of three fixed named periods, the author
/// can pick any color from any of the 12 real palette groups in
/// UltimaPaletteData. The stored command carries the resolved hex color
/// directly (not just a group+index reference) so playback never needs to
/// re-look up UltimaPaletteData at all - it stays correct even if that
/// data's storage or shape changes later.
/// </summary>
public partial class ApplyPaletteColorCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.ApplyPaletteColor };

    private string _selectedGroup = "";
    private int _selectedIndex = -1;
    private Color _selectedColor;

    public ApplyPaletteColorCommandWindow(EventCommand? existing = null)
    {
        InitializeComponent();
        BuildTabs();

        if (existing is not null && !string.IsNullOrEmpty(existing.Get("Hex")))
        {
            _selectedGroup = existing.Get("PaletteGroup");
            int.TryParse(existing.Get("ColorIndex"), out _selectedIndex);
            _selectedColor = (Color)ColorConverter.ConvertFromString("#" + existing.Get("Hex"));
            UpdateSelectedLabel();
        }

        int.TryParse(existing?.Get("OpacityPercent"), out int opacityPercent);
        OpacitySlider.Value = opacityPercent > 0 ? opacityPercent : 40;
    }

    private void BuildTabs()
    {
        foreach (var group in UltimaPaletteData.Groups)
        {
            var grid = new UniformGrid { Columns = 16 };
            for (int i = 0; i < group.Colors.Length; i++)
            {
                var c = group.Colors[i];
                var swatch = new Button
                {
                    Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
                    Width = 18,
                    Height = 18,
                    BorderThickness = new Thickness(0.4),
                    Margin = new Thickness(0.5),
                    Tag = (group.Name, i)
                };
                ToolTipService.SetToolTip(swatch, $"{group.Name} #{i} - RGB({c.R},{c.G},{c.B})");
                swatch.Click += Swatch_Click;
                grid.Children.Add(swatch);
            }
            var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            GroupTabs.Items.Add(new TabItem { Header = group.Name, Content = scroll });
        }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not (string groupName, int idx)) return;
        var group = Array.Find(UltimaPaletteData.Groups, g => g.Name == groupName);
        if (group is null) return;

        var c = group.Colors[idx];
        _selectedGroup = groupName;
        _selectedIndex = idx;
        _selectedColor = Color.FromRgb(c.R, c.G, c.B);
        UpdateSelectedLabel();
    }

    private void UpdateSelectedLabel()
    {
        SelectedColorSwatch.Fill = new SolidColorBrush(_selectedColor);
        SelectedColorLabel.Text = $"{_selectedGroup} #{_selectedIndex} - #{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueLabel is not null) OpacityValueLabel.Text = $"{(int)OpacitySlider.Value}%";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0)
        {
            MessageBox.Show("Click a color swatch first.", "Apply Palette Color", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new EventCommand { Type = EventCommandType.ApplyPaletteColor };
        command.Parameters["PaletteGroup"] = _selectedGroup;
        command.Parameters["ColorIndex"] = _selectedIndex.ToString();
        command.Parameters["Hex"] = $"{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";
        command.Parameters["OpacityPercent"] = ((int)OpacitySlider.Value).ToString();
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
