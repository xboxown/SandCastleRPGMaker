using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Shared by both Change Self Book Content and Change External Book
/// Content - per spec they're "100% identical" except External also shows
/// a book picker at the top. Runtime-only by design: this never touches
/// the target book's own authored FullContentXaml/Pages - it just
/// captures a standalone replacement XAML string, stored on the command
/// itself, for a real future engine to apply during actual gameplay. The
/// Book Editor's own Preview deliberately never reflects this - the
/// original content is genuinely never modified here.
/// </summary>
public partial class ChangeBookContentCommandWindow : Window
{
    private readonly bool _isExternal;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private readonly EventCommandType _commandType;

    private BookData _targetBook;
    private int _pageIndex;
    private int _selectedFrameId = -1;
    private string _replacementXaml = "";
    private BookPageTextPopup? _activePopup;

    public EventCommand Result { get; private set; }

    public ChangeBookContentCommandWindow(bool isExternal, BookData selfBook, BookDatabase books, ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites, ResourceDatabase resources, EventCommand? existing = null)
    {
        InitializeComponent();
        _isExternal = isExternal;
        _books = books;
        _objects = objects;
        _containers = containers;
        _sprites = sprites;
        _resources = resources;
        _commandType = isExternal ? EventCommandType.ChangeExternalBookContent : EventCommandType.ChangeSelfBookContent;
        _targetBook = selfBook;
        Result = new EventCommand { Type = _commandType };

        BookPickerPanel.Visibility = isExternal ? Visibility.Visible : Visibility.Collapsed;
        if (isExternal) BookNameBox.Text = selfBook.Name;

        if (existing is not null)
        {
            if (isExternal && int.TryParse(existing.Get("TargetBookId"), out int targetId))
            {
                var found = _books.Books.FirstOrDefault(b => b.Id == targetId);
                if (found is not null)
                {
                    _targetBook = found;
                    BookNameBox.Text = found.Name;
                }
            }
            int.TryParse(existing.Get("PageIndex"), out _pageIndex);
            int.TryParse(existing.Get("FrameId"), out _selectedFrameId);
            _replacementXaml = existing.Get("ReplacementXaml");
        }

        RefreshPageLabel();
        RefreshFrameCombo();
        UpdateReplacementStatus();
    }

    private void RefreshPageLabel()
    {
        int pageCount = Math.Max(1, _targetBook.Pages.Count);
        _pageIndex = Math.Clamp(_pageIndex, 0, pageCount - 1);
        PageLabel.Text = $"Page {_pageIndex + 1} of {pageCount}";
    }

    private void RefreshFrameCombo()
    {
        FrameCombo.SelectionChanged -= FrameCombo_SelectionChanged;
        FrameCombo.Items.Clear();

        if (_targetBook.Frames.Count == 0)
        {
            EmptyFrameNote.Visibility = Visibility.Visible;
            FrameCombo.SelectionChanged += FrameCombo_SelectionChanged;
            return;
        }
        EmptyFrameNote.Visibility = Visibility.Collapsed;

        for (int i = 0; i < _targetBook.Frames.Count; i++)
            FrameCombo.Items.Add(new ComboBoxItem { Content = $"Frame {i + 1}", Tag = _targetBook.Frames[i].Id });

        int selectIndex = _targetBook.Frames.FindIndex(f => f.Id == _selectedFrameId);
        FrameCombo.SelectedIndex = selectIndex >= 0 ? selectIndex : 0;
        _selectedFrameId = _targetBook.Frames[FrameCombo.SelectedIndex].Id;

        FrameCombo.SelectionChanged += FrameCombo_SelectionChanged;
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex > 0) { _pageIndex--; _replacementXaml = ""; RefreshPageLabel(); UpdateReplacementStatus(); }
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex < _targetBook.Pages.Count - 1) { _pageIndex++; _replacementXaml = ""; RefreshPageLabel(); UpdateReplacementStatus(); }
    }

    private void FrameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FrameCombo.SelectedItem is ComboBoxItem item && item.Tag is int frameId)
        {
            // Changing the frame targets a different (page, frame) slot entirely - any replacement text typed for the PREVIOUS frame must not silently carry over and get reused/shown here too. This, and the same reset in Prev/NextPage_Click, is the actual fix: without it, _replacementXaml stayed set to whatever was last typed regardless of which page/frame the selectors had since moved to.
            if (frameId != _selectedFrameId) { _selectedFrameId = frameId; _replacementXaml = ""; UpdateReplacementStatus(); }
        }
    }

    private void PickBook_Click(object sender, RoutedEventArgs e)
    {
        var current = new ItemReference { Category = ReferencedItemCategory.Book, ItemId = _targetBook.Id };
        var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, current) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is null) return;
        if (picker.Result.Category != ReferencedItemCategory.Book)
        {
            MessageBox.Show("Pick a Book specifically - this command edits book page content.", "Change Book Content", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var found = _books.Books.FirstOrDefault(b => b.Id == picker.Result.ItemId);
        if (found is null) return;

        _targetBook = found;
        BookNameBox.Text = found.Name;
        _pageIndex = 0;
        _selectedFrameId = -1;
        _replacementXaml = "";
        RefreshPageLabel();
        RefreshFrameCombo();
        UpdateReplacementStatus();
    }

    private void EditContent_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameId < 0)
        {
            MessageBox.Show("Pick a frame first.", "Change Book Content", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var frame = _targetBook.Frames.First(f => f.Id == _selectedFrameId);
        string startingXaml = !string.IsNullOrEmpty(_replacementXaml)
            ? _replacementXaml
            : (_targetBook.Pages.Count > _pageIndex && _targetBook.Pages[_pageIndex].FrameTexts.TryGetValue(_selectedFrameId, out var original) ? original : "");

        var frameBbox = BoundingBoxOf(frame.Points);
        _activePopup?.Close();
        _activePopup = new BookPageTextPopup(
            $"Replacement Content - {_targetBook.Name}, Page {_pageIndex + 1}, Frame {_targetBook.Frames.IndexOf(frame) + 1}",
            frame.EditorBackgroundColorHex,
            _ => { }, // background color tweaks here are just for the editing view - not part of the stored runtime replacement
            frameBbox.Width,
            frameBbox.Height,
            _sprites,
            _resources)
        { Owner = this };
        _activePopup.LoadXaml(startingXaml);
        _activePopup.XamlChanged += newXaml =>
        {
            _replacementXaml = newXaml;
            UpdateReplacementStatus();
        };
        _activePopup.Closed += (_, _) => _activePopup = null;
        _activePopup.Show();
    }

    private static Rect BoundingBoxOf(List<BookPoint> points)
    {
        if (points.Count == 0) return new Rect(0, 0, 300, 150);
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private void UpdateReplacementStatus()
    {
        ReplacementStatusLabel.Text = string.IsNullOrEmpty(_replacementXaml)
            ? "Replacement content: not set yet"
            : "Replacement content: set (edited)";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFrameId < 0)
        {
            MessageBox.Show("Pick a frame first.", "Change Book Content", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(_replacementXaml))
        {
            MessageBox.Show("Click \"Edit Replacement Content...\" and enter the new content first.", "Change Book Content", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _activePopup?.Close();

        var command = new EventCommand { Type = _commandType };
        if (_isExternal) command.Parameters["TargetBookId"] = _targetBook.Id.ToString();
        command.Parameters["PageIndex"] = _pageIndex.ToString();
        command.Parameters["FrameId"] = _selectedFrameId.ToString();
        command.Parameters["ReplacementXaml"] = _replacementXaml;
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _activePopup?.Close();
        DialogResult = false;
    }
}
