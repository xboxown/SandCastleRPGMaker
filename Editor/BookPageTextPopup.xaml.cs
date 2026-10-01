using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class BookPageTextPopup : Window
{
    /// <summary>Raised (already debounced) with the page content saved (base64 XamlPackage) after a typing pause or an immediate action.</summary>
    public event Action<string>? XamlChanged;

    private bool _suppress;
    private readonly Action<string> _onBackgroundColorChanged;

    // Serializing the whole popup content (base64 XamlPackage - expensive,
    // more so with an embedded image) on every single keystroke was the real
    // remaining cause of typing lag. Debounce it: only actually Save() and
    // notify after a short pause in typing, not per character.
    private readonly DispatcherTimer _typingDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

    private static readonly int[] FontSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 40, 48 };

    // A curated standard palette, similar in spirit to the reference screenshot's swatch grid.
    private static readonly Color[] StandardSwatches =
    {
        Colors.Black, Colors.DimGray, Colors.Gray, Colors.DarkGray, Colors.Silver, Colors.White,
        Colors.DarkRed, Colors.Red, Colors.OrangeRed, Colors.Orange, Colors.Gold, Colors.Yellow,
        Colors.DarkGreen, Colors.Green, Colors.SeaGreen, Colors.LimeGreen, Colors.YellowGreen, Colors.PaleGreen,
        Colors.Navy, Colors.DarkBlue, Colors.Blue, Colors.RoyalBlue, Colors.SkyBlue, Colors.LightBlue,
        Colors.Indigo, Colors.Purple, Colors.MediumPurple, Colors.Violet, Colors.Orchid, Colors.Plum,
        Colors.SaddleBrown, Colors.Brown, Colors.Sienna, Colors.Peru, Colors.Tan, Colors.Wheat
    };

    private readonly double _frameWidth;
    private readonly double _frameHeight;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private string? _spriteGroupFilter;
    private readonly List<SpriteAnimator> _activeAnimators = new();

    /// <summary>
    /// Real, confirmed bug fix - per spec ("I have to highlight it, reselect
    /// the same formatting for them to take effect"). TextRange.ApplyPropertyValue
    /// on a COLLAPSED selection (a plain blinking caret, about to type NEW
    /// text - not actual selected text) is a well-documented WPF
    /// RichTextBox limitation: it has nothing to attach the property to yet,
    /// so it does not reliably affect what gets typed next, no matter how
    /// correctly it's called. Rather than fight that undocumented internal
    /// behavior, this tracks the toolbar's own real intent explicitly - set
    /// the instant a formatting control is used with an empty selection,
    /// cleared the instant an actual text selection is reformatted instead -
    /// and Editor_TextChanged below applies whichever of these are set
    /// directly to whatever text just landed, every single keystroke, for
    /// as long as they remain set (a real "stays on until toggled off"
    /// typing mode, exactly matching what every other rich text editor does).
    /// </summary>
    private bool? _pendingBold, _pendingItalic, _pendingUnderline;
    private FontFamily? _pendingFontFamily;
    private double? _pendingFontSize;
    private Brush? _pendingForeground;

    public BookPageTextPopup(string title, string initialBackgroundHex, Action<string> onBackgroundColorChanged, double frameWidth, double frameHeight, SpriteDatabase sprites, ResourceDatabase resources)
    {
        InitializeComponent();
        Title = title;
        _onBackgroundColorChanged = onBackgroundColorChanged;
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
        _sprites = sprites;
        _resources = resources;

        var fontNames = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n).ToList();
        FontFamilyCombo.ItemsSource = fontNames;
        FontFamilyCombo.SelectedItem = fontNames.Contains("Segoe UI") ? "Segoe UI" : fontNames.FirstOrDefault();

        FontSizeCombo.ItemsSource = FontSizes;
        FontSizeCombo.SelectedItem = 12;

        BuildSwatchGrid(SwatchGrid, color => { ApplyFontColor(color); FontColorPopup.IsOpen = false; });
        BuildSwatchGrid(BackgroundSwatchGrid, color => { ApplyBackgroundColor(color); BackgroundColorPopup.IsOpen = false; });
        PopulateSpriteCombo();

        // Sane visible defaults from the moment the popup opens.
        Editor.FontFamily = new FontFamily("Segoe UI");
        Editor.FontSize = 12;
        Editor.Foreground = Brushes.Black;

        Editor.PreviewKeyDown += Editor_PreviewKeyDown;

        ApplyBackgroundColorHex(initialBackgroundHex, notify: false);

        DataObject.AddPastingHandler(Editor, Editor_Pasting);

        _typingDebounce.Tick += (_, _) =>
        {
            _typingDebounce.Stop();
            if (_suppress) return;
            XamlChanged?.Invoke(RichTextXamlHelper.Save(Editor));
        };

        Closing += (_, _) =>
        {
            foreach (var animator in _activeAnimators) animator.Stop();
            if (!_typingDebounce.IsEnabled) return;
            _typingDebounce.Stop();
            NotifyChangedImmediate(); // flush whatever was typed right before closing
        };
    }

    /// <summary>
    /// A RichTextBox with AcceptsTab unset (the default here) sends Tab
    /// straight to focus navigation - it never reaches the document at
    /// all. Intercepting it and inserting spaces instead of a literal Tab
    /// character is deliberate: FormattedContentRenderer draws text via
    /// FormattedText, which has no tab-stop concept, so a raw '\t' would
    /// render at an unpredictable width (often nothing at all) rather than
    /// a real paragraph-starting indent.
    /// </summary>
    private const string ParagraphIndent = "    ";

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        e.Handled = true;
        Editor.Selection.Text = ParagraphIndent;
        Editor.CaretPosition = Editor.Selection.End;
    }

    private void BuildSwatchGrid(UniformGrid grid, Action<Color> onPick)
    {
        grid.Children.Clear();
        foreach (var color in StandardSwatches)
        {
            var swatch = new Rectangle
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(2),
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.Gray,
                StrokeThickness = 0.5,
                Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (_, _) => onPick(color);
            grid.Children.Add(swatch);
        }
    }

    public void LoadXaml(string data)
    {
        _suppress = true;
        RichTextXamlHelper.Load(Editor, data);
        ReattachLiveSprites();
        _suppress = false;
    }

    /// <summary>Immediate (non-debounced) notify - used for formatting actions and other one-off changes, which aren't rapid-fire like typing.</summary>
    private void NotifyChangedImmediate()
    {
        if (_suppress) return;
        XamlChanged?.Invoke(RichTextXamlHelper.Save(Editor));
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;

        // Real, confirmed bug fix - applies whichever pending format(s) are currently set (see the fields' own doc comment) directly to whatever text just landed, every keystroke, for as long as they remain set. This is what makes "type in bold" actually stay in effect for everything typed afterward, instead of only ever affecting the one call that set the toolbar state.
        if (_pendingBold is not null || _pendingItalic is not null || _pendingUnderline is not null || _pendingFontFamily is not null || _pendingFontSize is not null || _pendingForeground is not null)
        {
            foreach (var change in e.Changes)
            {
                if (change.AddedLength <= 0) continue;
                var rangeStart = Editor.Document.ContentStart.GetPositionAtOffset(change.Offset);
                var rangeEnd = Editor.Document.ContentStart.GetPositionAtOffset(change.Offset + change.AddedLength);
                if (rangeStart is null || rangeEnd is null) continue;
                var insertedRange = new TextRange(rangeStart, rangeEnd);

                if (_pendingBold is { } bold) insertedRange.ApplyPropertyValue(TextElement.FontWeightProperty, bold ? FontWeights.Bold : FontWeights.Normal);
                if (_pendingItalic is { } italic) insertedRange.ApplyPropertyValue(TextElement.FontStyleProperty, italic ? FontStyles.Italic : FontStyles.Normal);
                if (_pendingUnderline is { } underline) insertedRange.ApplyPropertyValue(Inline.TextDecorationsProperty, underline ? TextDecorations.Underline : null);
                if (_pendingFontFamily is { } family) insertedRange.ApplyPropertyValue(TextElement.FontFamilyProperty, family);
                if (_pendingFontSize is { } size) insertedRange.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                if (_pendingForeground is { } foreground) insertedRange.ApplyPropertyValue(TextElement.ForegroundProperty, foreground);
            }
        }

        _typingDebounce.Stop();
        _typingDebounce.Start();
    }

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        bool wasEmpty = Editor.Selection.IsEmpty;
        EditingCommands.ToggleBold.Execute(null, Editor);
        // Real, self-owned toggle state, per spec - rather than ask WPF what the resulting state of an EMPTY selection now "is" (the same unreliable territory as the bug itself), this simply flips its own tracked intent: not pending -> bold; pending-bold -> not bold; pending-not-bold -> bold again. A real, existing selection being reformatted directly is unaffected - that already works, and clearing here would only interfere with it.
        if (wasEmpty) _pendingBold = _pendingBold != true;
        NotifyChangedImmediate();
        RestoreFocus();
    }
    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        bool wasEmpty = Editor.Selection.IsEmpty;
        EditingCommands.ToggleItalic.Execute(null, Editor);
        if (wasEmpty) _pendingItalic = _pendingItalic != true;
        NotifyChangedImmediate();
        RestoreFocus();
    }
    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        bool wasEmpty = Editor.Selection.IsEmpty;
        EditingCommands.ToggleUnderline.Execute(null, Editor);
        if (wasEmpty) _pendingUnderline = _pendingUnderline != true;
        NotifyChangedImmediate();
        RestoreFocus();
    }

    /// <summary>
    /// Clicking a toolbar ComboBox/Button steals keyboard focus away from the
    /// RichTextBox. Applying a format while focus has moved elsewhere is why
    /// changes sometimes silently failed to "stick" until re-tried several
    /// times - this restores focus (and caret) right after every formatting
    /// action so the format reliably takes effect for what's typed next.
    /// </summary>
    private void RestoreFocus()
    {
        Editor.Focus();
        Keyboard.Focus(Editor);
    }

    private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontFamilyCombo.SelectedItem is not string family) return;
        bool wasEmpty = Editor.Selection.IsEmpty;
        var fontFamily = new FontFamily(family);
        Editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, fontFamily);
        _pendingFontFamily = wasEmpty ? fontFamily : null;
        NotifyChangedImmediate();
        RestoreFocus();
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo.SelectedItem is not int size) return;
        bool wasEmpty = Editor.Selection.IsEmpty;
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, (double)size);
        _pendingFontSize = wasEmpty ? (double)size : null;
        NotifyChangedImmediate();
        RestoreFocus();
    }

    // ---------- Font color: swatch grid + native "custom color" dialog ----------

    private void FontColorButton_Click(object sender, RoutedEventArgs e) => FontColorPopup.IsOpen = !FontColorPopup.IsOpen;

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AllowFullOpen = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            ApplyFontColor(Color.FromArgb(c.A, c.R, c.G, c.B));
        }
        FontColorPopup.IsOpen = false;
    }

    private void ApplyFontColor(Color color)
    {
        bool wasEmpty = Editor.Selection.IsEmpty;
        var brush = new SolidColorBrush(color);
        Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        _pendingForeground = wasEmpty ? brush : null;
        NotifyChangedImmediate();
        RestoreFocus();
    }

    // ---------- Background color: per-frame, persisted, swatch + custom dialog ----------

    private void BackgroundColorButton_Click(object sender, RoutedEventArgs e) => BackgroundColorPopup.IsOpen = !BackgroundColorPopup.IsOpen;

    private void CustomBackgroundColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AllowFullOpen = true };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var c = dialog.Color;
            ApplyBackgroundColor(Color.FromArgb(c.A, c.R, c.G, c.B));
        }
        BackgroundColorPopup.IsOpen = false;
    }

    private void ApplyBackgroundColor(Color color)
    {
        EditorFrameBorder.Background = new SolidColorBrush(color);
        _onBackgroundColorChanged(color.ToString());
        RestoreFocus();
    }

    private void ApplyBackgroundColorHex(string hex, bool notify)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            EditorFrameBorder.Background = new SolidColorBrush(color);
            if (notify) _onBackgroundColorChanged(hex);
        }
        catch { /* bad/legacy stored color - keep the XAML default */ }
    }

    // ---------- Image insertion (real text wrap via Floater, drag-to-resize) ----------

    /// <summary>
    /// Repopulates every time this popup opens (never cached) - a sprite's
    /// size can change in the Sprite Editor between edits, so what fits
    /// here can change too. Only sprites that fit inside this frame, minus
    /// the same 32px-per-side margin the old "Insert Image" size check
    /// used, are listed; anything too big is silently left off the list
    /// rather than shown and then rejected after picking it - the red
    /// count text beside the dropdown is the only sign anything was
    /// excluded.
    /// </summary>
    private void PopulateSpriteCombo()
    {
        const double margin = 32;
        double allowedWidth = _frameWidth - margin * 2;
        double allowedHeight = _frameHeight - margin * 2;

        IEnumerable<(string Group, SpriteEntry Sprite)> candidates;
        bool showGroup;
        if (!string.IsNullOrEmpty(_spriteGroupFilter))
        {
            var group = _sprites.Groups.FirstOrDefault(g => g.Name == _spriteGroupFilter);
            candidates = group?.Sprites.Select(s => (_spriteGroupFilter!, s)) ?? Enumerable.Empty<(string, SpriteEntry)>();
            showGroup = false;
        }
        else
        {
            candidates = _sprites.Groups.SelectMany(g => g.Sprites.Select(s => (g.Name, s)));
            showGroup = true;
        }

        var fitting = new List<SpriteInsertChoice>();
        int ignored = 0;
        foreach (var (group, sprite) in candidates)
        {
            if (TryGetSpriteSize(sprite, out int w, out int h) && w <= allowedWidth && h <= allowedHeight)
                fitting.Add(new SpriteInsertChoice(group, sprite, showGroup));
            else
                ignored++;
        }

        SpriteCombo.ItemsSource = fitting;
        SpriteCombo.DisplayMemberPath = "DisplayName";
        if (fitting.Count > 0) SpriteCombo.SelectedIndex = 0;

        SpriteIgnoredCountText.Text = ignored > 0 ? $"{ignored} sprite{(ignored == 1 ? "" : "s")} ignored - too big for this frame" : string.Empty;
    }

    private static bool TryGetSpriteSize(SpriteEntry sprite, out int width, out int height)
    {
        width = height = 0;
        string path = sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try
        {
            // DelayCreation reads just the header, not the full pixel data -
            // checking dozens of sprites every time this popup opens should
            // stay fast even for a large sprite database.
            var frame = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            return true;
        }
        catch { return false; }
    }

    private sealed class SpriteInsertChoice
    {
        public string Group { get; }
        public SpriteEntry Sprite { get; }
        public string DisplayName { get; }
        public SpriteInsertChoice(string group, SpriteEntry sprite, bool showGroup)
        {
            Group = group;
            Sprite = sprite;
            DisplayName = showGroup ? $"{group} / {sprite.Name}" : sprite.Name;
        }
    }

    /// <summary>Mirrors the "Filter By Group" pattern already used for a book's own Sprite/Paper Doll/etc fields elsewhere in the Book Editor, for consistency.</summary>
    private void SpriteFilterByGroupCheck_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        var groupNames = _sprites.Groups.Select(g => g.Name).ToList();
        string? chosen = SelectGroupWindow.Show(this, groupNames, _spriteGroupFilter);
        if (chosen is null)
        {
            _suppress = true;
            SpriteFilterByGroupCheck.IsChecked = false;
            _suppress = false;
            return;
        }
        _spriteGroupFilter = chosen;
        PopulateSpriteCombo();
    }

    private void SpriteFilterByGroupCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _spriteGroupFilter = null;
        PopulateSpriteCombo();
    }

    private void InsertSprite_Click(object sender, RoutedEventArgs e)
    {
        if (SpriteCombo.SelectedItem is not SpriteInsertChoice choice)
        {
            MessageBox.Show("Pick a sprite from the list first - add one in the Sprite Editor if it's empty.", "Insert Sprite", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        InsertSpriteFloater(choice.Group, choice.Sprite);
    }

    /// <summary>
    /// Same proven Floater(BlockUIContainer(bare Image)) structure
    /// InsertImageFloater always used - nothing extra wrapped around the
    /// Image, since that's the one structure confirmed to survive the
    /// XamlPackage save/load round-trip. The "live" part is entirely
    /// Image.Uid (a plain string, safe to serialize) plus a freshly
    /// attached SpriteAnimator - the animator itself obviously can't
    /// survive serialization, which is exactly why ReattachLiveSprites
    /// exists to redo this every time saved content is loaded back in.
    /// </summary>
    private void InsertSpriteFloater(string group, SpriteEntry sprite)
    {
        double defaultWidth = Math.Max(60, Math.Min(150, Editor.ActualWidth > 0 ? Editor.ActualWidth * 0.4 : 150));

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            Cursor = Cursors.Hand,
            ToolTip = "Double-click to resize or move to the other side - this is a live sprite reference: its animation (and later, per-frame sound) always matches whatever the Sprite Editor currently has for it."
        };
        var newAnimator = new SpriteAnimator(sprite, image, _resources);
        newAnimator.Ticked += OnAnySpriteTicked;
        _activeAnimators.Add(newAnimator);

        var floater = new Floater(new BlockUIContainer(image))
        {
            Width = defaultWidth,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        image.Tag = floater;

        image.MouseLeftButtonDown += (s, args) =>
        {
            if (args.ClickCount == 2)
            {
                ResizeFloaterImage(floater, image);
                args.Handled = true;
            }
        };
        image.MouseRightButtonDown += (s, args) =>
        {
            args.Handled = true;
            var result = MessageBox.Show("Delete this sprite?", "Delete Sprite", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            if (floater.Parent is Paragraph parentPara)
            {
                parentPara.Inlines.Remove(floater);
                NotifyChangedImmediate();
            }
        };

        InsertFloaterAtCaret(floater);
    }

    /// <summary>Called after every load into this popup's Editor - see LiveSpriteEmbedding for why (a DispatcherTimer can't survive serialization, so any embedded sprite needs a fresh SpriteAnimator reattached each time).</summary>
    private void ReattachLiveSprites()
    {
        var newAnimators = LiveSpriteEmbedding.ReattachLiveSprites(Editor, _sprites, _resources);
        foreach (var animator in newAnimators) animator.Ticked += OnAnySpriteTicked;
        _activeAnimators.AddRange(newAnimators);
    }

    /// <summary>Temporary diagnostic (DEBUG-BUILD-46) - see BookPreviewWindow/BookEditorWindow for why hover-based tooltips can't be trusted as the check everywhere; the title bar needs no interaction to see.</summary>
    private int _diagnosticTickTotal;
    private string? _baseTitle;

    private void OnAnySpriteTicked(int _)
    {
        _baseTitle ??= Title;
        _diagnosticTickTotal++;
        Title = $"{_baseTitle} - sprite ticks: {_diagnosticTickTotal}";
    }

    /// <summary>Shared caret-insertion mechanics for any Floater (image or sprite alike) - preserves the formatting active at the caret and starts a fresh paragraph afterward so WPF's per-paragraph text-wrap narrowing from the Floater doesn't linger for everything typed next.</summary>
    private void InsertFloaterAtCaret(Floater floater)
    {
        Editor.Focus();
        var caretPos = Editor.CaretPosition;
        var caretParagraph = caretPos?.Paragraph;
        if (caretParagraph is null)
        {
            caretParagraph = new Paragraph();
            Editor.Document.Blocks.Add(caretParagraph);
        }

        var formatSource = caretPos ?? Editor.Document.ContentEnd;
        var fontFamily = formatSource.Parent is TextElement te1 ? te1.FontFamily : Editor.FontFamily;
        var fontSize = formatSource.Parent is TextElement te2 ? te2.FontSize : Editor.FontSize;
        var foreground = formatSource.Parent is TextElement te3 ? te3.Foreground : Editor.Foreground;
        var fontWeight = formatSource.Parent is TextElement te4 ? te4.FontWeight : FontWeights.Normal;
        var fontStyle = formatSource.Parent is TextElement te5 ? te5.FontStyle : FontStyles.Normal;

        caretParagraph.Inlines.Add(floater);

        var afterInsert = Editor.CaretPosition;
        if (afterInsert is not null)
        {
            Editor.CaretPosition = afterInsert;
            EditingCommands.EnterParagraphBreak.Execute(null, Editor);

            var resumePos = Editor.CaretPosition;
            var resumeRange = new TextRange(resumePos, resumePos);
            resumeRange.ApplyPropertyValue(TextElement.FontFamilyProperty, fontFamily);
            resumeRange.ApplyPropertyValue(TextElement.FontSizeProperty, fontSize);
            resumeRange.ApplyPropertyValue(TextElement.ForegroundProperty, foreground);
            resumeRange.ApplyPropertyValue(TextElement.FontWeightProperty, fontWeight);
            resumeRange.ApplyPropertyValue(TextElement.FontStyleProperty, fontStyle);
            Editor.CaretPosition = resumePos;
        }

        NotifyChangedImmediate();
    }

    /// <summary>
    /// Ctrl+V with an image on the clipboard used to fall through to WPF's own
    /// default RichTextBox paste handling, which does NOT reliably survive our
    /// custom XamlPackage save/load round-tripping (used constantly - every
    /// keystroke debounce tick, every repagination pass). That's why a pasted
    /// picture could vanish, and why the corrupted stream that resulted could
    /// then make SUBSEQUENT typing splice incorrectly, looking like the old
    /// "garbled/overlapping text" bug again as a downstream side effect.
    /// Intercepting paste and routing an image through the SAME reliable,
    /// file-based path as the "Insert Image" button fixes this at the source.
    /// </summary>
    private void Editor_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Bitmap))
        {
            // Raw raster image insertion (button and paste alike) was
            // retired in favor of Insert Sprite - a pasted picture would
            // have been a dead, unlinked snapshot; a sprite reference stays
            // live (its animation, and later its per-frame sound, always
            // matches whatever the Sprite Editor currently has for it).
            e.CancelCommand();
            MessageBox.Show("Pasting a picture directly isn't supported - use Insert Sprite instead, so it stays linked to the Sprite Editor.",
                "Paste Image", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Text paste. Was previously left to WPF's own default RichTextBox
        // paste handling - which imports the SOURCE's own HTML/XAML
        // structure verbatim (a webpage's own paragraph/span/div nesting),
        // not clean plain text. A real-world paste from a wiki-style page
        // produced a far more fragmented document than anything typed by
        // hand - many more, much smaller paragraphs - and the pagination
        // engine's per-paragraph fit loop pays for every single one of
        // them. That's what actually produced a multi-second freeze and a
        // 3000-page runaway from one paste. Normalizing every paste to
        // plain text, split into paragraphs only on real line breaks,
        // keeps pasted content exactly as predictable as anything typed
        // directly, regardless of where it came from.
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText) || e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            string? text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? e.DataObject.GetData(DataFormats.Text) as string;
            if (!string.IsNullOrEmpty(text)) InsertPlainText(text);
        }
    }

    /// <summary>
    /// Inserts text at the caret as plain paragraphs (one per line, blank
    /// lines preserved), routed through the SAME reliable XamlPackage
    /// save/load round-trip the rest of this file already uses for content
    /// splicing - not manual TextPointer arithmetic, which is one of the
    /// trickier corners of WPF's text API and not worth re-debugging when a
    /// proven-safe path already exists right here.
    /// </summary>
    private void InsertPlainText(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');

        var scratch = new RichTextBox();
        scratch.Document.Blocks.Clear(); // a fresh RichTextBox's Document starts with one empty default Paragraph already - clear it so only the pasted lines end up in the spliced content
        foreach (var line in lines)
            scratch.Document.Blocks.Add(new Paragraph(new Run(line)));

        string xaml = RichTextXamlHelper.Save(scratch);
        var caretRange = new TextRange(Editor.CaretPosition, Editor.CaretPosition);
        using var stream = new MemoryStream(Convert.FromBase64String(xaml));
        caretRange.Load(stream, DataFormats.XamlPackage);
    }

    /// <summary>Wraps an Image with a small draggable corner grip so it can be resized visually, right where it's shown - not via a text-input dialog.</summary>
    private void ResizeFloaterImage(Floater floater, Image image)
    {
        var widthText = SimpleInputWindow.Show(this, "New width in pixels:", "Resize Image", floater.Width.ToString("0"));
        if (widthText is null) return;

        if (double.TryParse(widthText, out double newWidth) && newWidth > 10)
        {
            floater.Width = newWidth;
            NotifyChangedImmediate();
        }

        var sideText = SimpleInputWindow.Show(this, "Float side - type Left or Right:", "Resize Image",
            floater.HorizontalAlignment == HorizontalAlignment.Right ? "Right" : "Left");
        if (sideText is not null)
        {
            floater.HorizontalAlignment = sideText.Trim().Equals("Right", StringComparison.OrdinalIgnoreCase)
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
            NotifyChangedImmediate();
        }
    }
}
