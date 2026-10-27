using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// System Settings > Levels: the level curve, as RPG Maker's parameter
/// curves. Max Level; then a bar for every level - the experience needed to
/// reach it, or the training points it gives - whose height is dragged with
/// the mouse, and the same numbers in a table to type into. Generate fills
/// the curve from a formula (Exult's own: level 2 at 100, doubling, 3
/// training points a level) to adjust by hand afterwards.
/// </summary>
internal sealed class LevelCurveEditor : DockPanel
{
    private readonly LevelSettings _levels;
    private readonly Canvas _chart = new() { Height = 230, Background = new SolidColorBrush(Color.FromRgb(24, 24, 32)), ClipToBounds = true, Cursor = Cursors.SizeNS };
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 150 };
    private readonly RadioButton _showExp = new() { Content = "Experience needed", IsChecked = true, GroupName = "curve", Margin = new Thickness(0, 0, 14, 0) };
    private readonly RadioButton _showPoints = new() { Content = "Training points given", GroupName = "curve" };
    private readonly TextBox _maxLevel = new() { Width = 50 };
    private readonly TextBlock _hover = new() { Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _warning = new() { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    /// <summary>While dragging: the bar's level and the chart's scale when the drag began (so the bar doesn't run away from the mouse as the top grows).</summary>
    private (LevelStep Step, double Scale)? _drag;

    private bool ShowingExp => _showExp.IsChecked == true;

    /// <summary>The colour the experience bars are drawn in (a character's own graph colour).</summary>
    private readonly Color? _barColor;

    public LevelCurveEditor(LevelSettings levels, Color? barColor = null)
    {
        _levels = levels;
        _barColor = barColor;
        _levels.Normalize();
        Margin = new Thickness(10);

        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        Children.Add(top);

        var maxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        maxRow.Children.Add(new TextBlock { Text = "Max Level (1-99):", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _maxLevel.Text = _levels.MaxLevel.ToString();
        _maxLevel.LostFocus += (_, _) => ApplyMaxLevel();
        _maxLevel.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyMaxLevel(); };
        maxRow.Children.Add(_maxLevel);
        maxRow.Children.Add(new TextBlock { Text = "   Each character can be locked lower: Character Editor > Level Lock.", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(maxRow);

        // Generate: the curve from a formula, to adjust by hand afterwards.
        var first = new TextBox { Width = 60, Text = "100" };
        var growth = new TextBox { Width = 45, Text = "100" };
        var points = new TextBox { Width = 35, Text = "3" };
        var generate = new Button { Content = "Generate", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        var exult = new Button { Content = "Exult's own", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Ultima VII: Max Level 8, level 2 at 100 experience, doubling each level, 3 training points a level" };
        var genRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        void Text(string t) => genRow.Children.Add(new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        Text("Level 2 at"); genRow.Children.Add(first); Text(" experience, each level"); genRow.Children.Add(growth); Text("% more,"); genRow.Children.Add(points); Text("training points each");
        genRow.Children.Add(generate);
        genRow.Children.Add(exult);
        generate.Click += (_, _) =>
        {
            if (!int.TryParse(first.Text.Trim(), out int f) || f < 1 || !double.TryParse(growth.Text.Trim(), out double g) || g < 0 || !int.TryParse(points.Text.Trim(), out int p) || p < 0)
            {
                MessageBox.Show(Window.GetWindow(this), "Experience must be 1 or more; the growth and the training points 0 or more.", "Levels", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!AskReplace()) return;
            _levels.Steps = LevelSettings.Generate(_levels.MaxLevel, f, g, p);
            Refresh();
        };
        exult.Click += (_, _) =>
        {
            if (!AskReplace()) return;
            _levels.MaxLevel = 8;
            _levels.Steps = LevelSettings.Generate(8, 100, 100, 3);
            _maxLevel.Text = "8";
            Refresh();
        };
        top.Children.Add(genRow);

        var showRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        showRow.Children.Add(new TextBlock { Text = "Curve:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) });
        showRow.Children.Add(_showExp);
        showRow.Children.Add(_showPoints);
        _showExp.Checked += (_, _) => DrawChart();
        _showPoints.Checked += (_, _) => DrawChart();
        top.Children.Add(showRow);
        top.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _chart });
        top.Children.Add(_hover);
        top.Children.Add(_warning);
        top.Children.Add(new TextBlock
        {
            Text = "Drag a bar's top to set it, or type in the table. Whoever lands the killing blow gets the dead one's EXP - the player, the party, and monsters killing each other - and on reaching a level's experience gets its training points (for training, as Ultima VII's trainers).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 6)
        });

        _grid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new System.Windows.Data.Binding(nameof(LevelStep.Level)), IsReadOnly = true, Width = 60 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Experience needed (total)", Binding = new System.Windows.Data.Binding(nameof(LevelStep.Experience)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Training points given", Binding = new System.Windows.Data.Binding(nameof(LevelStep.TrainingPoints)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(() => { Clean(); DrawChart(); });
        Children.Add(_grid);

        _chart.SizeChanged += (_, _) => DrawChart();
        _chart.MouseLeftButtonDown += Chart_MouseDown;
        _chart.MouseMove += Chart_MouseMove;
        _chart.MouseLeftButtonUp += (_, _) => { _drag = null; _chart.ReleaseMouseCapture(); _grid.Items.Refresh(); CheckOrder(); };
        Refresh();
    }

    private bool AskReplace() =>
        MessageBox.Show(Window.GetWindow(this), "Replace every level's experience and training points?", "Levels", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void ApplyMaxLevel()
    {
        if (!int.TryParse(_maxLevel.Text.Trim(), out int max)) { _maxLevel.Text = _levels.MaxLevel.ToString(); return; }
        _levels.MaxLevel = Math.Clamp(max, 1, LevelSettings.MaxMaxLevel);
        _maxLevel.Text = _levels.MaxLevel.ToString();
        Refresh();
    }

    private void Refresh()
    {
        _levels.Normalize();
        _grid.ItemsSource = null;
        _grid.ItemsSource = _levels.Steps;
        DrawChart();
        CheckOrder();
    }

    /// <summary>Typed values made valid: never below 0 (experience at least 1).</summary>
    private void Clean()
    {
        foreach (var step in _levels.Steps)
        {
            step.Experience = Math.Max(1, step.Experience);
            step.TrainingPoints = Math.Max(0, step.TrainingPoints);
        }
        CheckOrder();
    }

    /// <summary>A level needing no more experience than the one before is reached at the same moment - said, not forbidden.</summary>
    private void CheckOrder()
    {
        var same = _levels.Steps.Where((s, i) => i > 0 && s.Experience <= _levels.Steps[i - 1].Experience).Select(s => s.Level).ToList();
        _warning.Text = same.Count == 0 ? "" : $"Level{(same.Count == 1 ? "" : "s")} {string.Join(", ", same)} need{(same.Count == 1 ? "s" : "")} no more experience than the level before - reached at the same moment.";
    }

    private int ValueOf(LevelStep step) => ShowingExp ? step.Experience : step.TrainingPoints;

    private void SetValue(LevelStep step, int value)
    {
        if (ShowingExp) step.Experience = Math.Max(1, value);
        else step.TrainingPoints = Math.Max(0, value);
    }

    private double Scale() => Math.Max(ShowingExp ? 10 : 5, _levels.Steps.Count == 0 ? 1 : _levels.Steps.Max(ValueOf)) * 1.15;

    private (double Left, double Width) BarAt(int index)
    {
        double slot = _chart.ActualWidth / Math.Max(1, _levels.Steps.Count);
        return (index * slot + Math.Min(2, slot * 0.15), Math.Max(1, slot - Math.Min(4, slot * 0.3)));
    }

    private void DrawChart()
    {
        _chart.Children.Clear();
        if (_chart.ActualWidth <= 0 || _levels.Steps.Count == 0) return;
        double height = _chart.ActualHeight, scale = _drag?.Scale ?? Scale();
        var fill = ShowingExp ? _barColor ?? Color.FromRgb(90, 160, 230) : Color.FromRgb(120, 200, 110);
        for (int i = 0; i < _levels.Steps.Count; i++)
        {
            var step = _levels.Steps[i];
            var (left, width) = BarAt(i);
            double barHeight = Math.Min(height, ValueOf(step) / scale * height);
            bool dragging = ReferenceEquals(_drag?.Step, step);
            var bar = new Rectangle { Width = width, Height = Math.Max(1, barHeight), Fill = new SolidColorBrush(dragging ? Colors.Gold : fill) };
            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, height - bar.Height);
            _chart.Children.Add(bar);
            if (width >= 18)
            {
                var label = new TextBlock { Text = step.Level.ToString(), Foreground = Brushes.White, FontSize = 9, Width = width, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
                Canvas.SetLeft(label, left);
                Canvas.SetTop(label, height - 13);
                _chart.Children.Add(label);
            }
        }
    }

    private LevelStep? StepAt(Point p)
    {
        if (_levels.Steps.Count == 0) return null;
        int index = (int)(p.X / (_chart.ActualWidth / _levels.Steps.Count));
        return index >= 0 && index < _levels.Steps.Count ? _levels.Steps[index] : null;
    }

    private int ValueAt(Point p, double scale) => (int)Math.Round(Math.Clamp(_chart.ActualHeight - p.Y, 0, _chart.ActualHeight) / _chart.ActualHeight * scale);

    private void Chart_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_chart);
        if (StepAt(p) is not { } step) return;
        _drag = (step, Scale());
        _chart.CaptureMouse();
        SetValue(step, ValueAt(p, _drag.Value.Scale));
        ShowHover(step);
        DrawChart();
    }

    private void Chart_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_chart);
        if (_drag is { } drag && e.LeftButton == MouseButtonState.Pressed)
        {
            SetValue(drag.Step, ValueAt(p, drag.Scale));
            ShowHover(drag.Step);
            DrawChart();
            return;
        }
        if (StepAt(p) is { } step) ShowHover(step);
    }

    private void ShowHover(LevelStep step) =>
        _hover.Text = $"Level {step.Level}: {step.Experience} experience needed, {step.TrainingPoints} training point{(step.TrainingPoints == 1 ? "" : "s")} given";
}
