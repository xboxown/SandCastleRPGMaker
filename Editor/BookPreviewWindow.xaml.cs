using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class BookPreviewWindow : Window
{
    private readonly BookData _book;
    private readonly ResourceDatabase _resources;
    private readonly SpriteDatabase _sprites;
    private readonly Func<string, string, SpriteEntry?> _paperDollLookup;
    private SpriteAnimator? _worldSpriteAnimator;
    private SpriteAnimator? _paperDollAnimator;
    private readonly List<SpriteAnimator> _pageOverlaySpriteAnimators = new();

    private bool _dragging;
    private bool _movedDuringDrag;
    private Point _dragStartMouse;
    private Point _dragStartElement;

    private DispatcherTimer? _nameTimer;
    private MediaPlayer? _mediaPlayer;

    private int _pageIndex;

    /// <summary>
    /// Tracks whether the real "open, read through, close" interaction has
    /// actually happened this session - what ByAfterReadItem's trigger
    /// check uses. Once true it stays true (matches a real game's own
    /// persistent "has been read" flag, not something that resets between
    /// interactions).
    /// </summary>
    private bool _hasBeenRead;
    /// <summary>Created once and reused for the whole Preview session - NOT recreated per event fire. Switches and variables have to persist across separate interactions (a counter incremented by one event and checked by a later one is the whole point of a variable), so a fresh context per fire would silently reset all of them back to empty every single time.</summary>
    private readonly BookEventPreviewExecutionContext _execContext;
    /// <summary>Per-page fired count, for Cycle (Once/SpecificNumberOfTimes) to mean something real across repeated interactions in one preview session, not always reset to zero.</summary>
    private readonly Dictionary<BookEventPage, int> _firedCounts = new();

    /// <summary>
    /// Temporary diagnostic (DEBUG-BUILD-46): hover-based tooltips can't
    /// work here at all - the reading view's overlay has
    /// IsHitTestVisible=false so "click to turn the page" works without
    /// text underneath stealing the click, which also means tooltips for
    /// anything inside it (an embedded sprite included) never fire; a
    /// hover lands on whatever's behind it instead. The window title needs
    /// no interaction to see, so it's the diagnostic surface instead.
    /// </summary>
    private int _diagnosticTickTotal;

    private void OnAnySpriteTicked(int _)
    {
        _diagnosticTickTotal++;
        Title = $"Preview - sprite ticks: {_diagnosticTickTotal} (climbing = animation is genuinely running; frozen = it's really stopped)";
    }

    public BookPreviewWindow(BookData book, SpriteEntry worldSprite, ResourceDatabase resources, SpriteDatabase sprites, Func<string, string, SpriteEntry?> paperDollLookup)
    {
        InitializeComponent();
        _book = book;
        _resources = resources;
        _sprites = sprites;
        _paperDollLookup = paperDollLookup;
        _execContext = new BookEventPreviewExecutionContext(_resources, _book.Id)
        {
            OnShakeScreen = PlayShakeEffect,
            OnChangeDayNight = ApplyDayNightPeriod,
            OnApplyPaletteColor = ApplyPaletteColorEffect,
            OnShowText = ShowMessageOverlay
        };

        _worldSpriteAnimator = new SpriteAnimator(worldSprite, BookSpriteImage, _resources);
        EventStatusText.Text = BuildTriggerInstructions();

        Loaded += (_, _) =>
        {
            Canvas.SetLeft(BookSpriteImage, (SpriteCanvas.ActualWidth - BookSpriteImage.Width) / 2);
            Canvas.SetTop(BookSpriteImage, (SpriteCanvas.ActualHeight - BookSpriteImage.Height) / 2);

            // "Always" doesn't wait for the player to do anything - it's
            // eligible the moment the book exists, not just when some
            // unrelated interaction (drag, double-click) happens to occur
            // afterward. Checking once right here is what actually makes
            // an Always+Always page fire the instant Preview opens, rather
            // than sitting there passing its own evaluation but never
            // actually getting a chance to run.
            TryFireEvent();
        };

        Closing += (_, _) =>
        {
            _worldSpriteAnimator?.Stop();
            _paperDollAnimator?.Stop();
            foreach (var animator in _pageOverlaySpriteAnimators) animator.Stop();
        };
    }

    /// <summary>
    /// Tells the author, in plain language, exactly what to actually do to
    /// test whichever trigger condition they've set - written per trigger
    /// type rather than one generic instruction, since "double-click and
    /// read it" only means something for By After Read Item. Trigger types
    /// with no real interaction available in this preview say so honestly
    /// instead of pretending there's a way to test them here.
    /// </summary>
    private string BuildTriggerInstructions()
    {
        if (_book.EventPages.Count == 0) return "This book has no event pages yet - add one on the Event tab first.";

        var lines = new List<string>();
        for (int i = 0; i < _book.EventPages.Count; i++)
        {
            string howTo = _book.EventPages[i].Conditions.TriggerConditionBased switch
            {
                BookTriggerConditionBased.Always =>
                    "You've selected \"Always\" - no interaction is required here; this page's trigger check always passes (any other conditions checked on it still apply).",
                BookTriggerConditionBased.ByAfterReadItem =>
                    "You've selected \"By After Read Item\" - double-click the book, read through it, and close it to see if this page's event fires.",
                BookTriggerConditionBased.ByUseItem =>
                    "You've selected \"By Use Item\" - double-click the book to test it. If this page's conditions pass and it has real commands, those commands take over completely and the book won't open to be read at all; otherwise the book opens normally.",
                BookTriggerConditionBased.ByPickupItem =>
                    "You've selected \"By Pickup Item\" - click and actually drag the book (a small movement, not just a click) to test it. If the condition's Select Item targets a different item than this book, this preview can't test it and it reads as not having occurred.",
                BookTriggerConditionBased.ByDropItem =>
                    "You've selected \"By Drop Item\" - drag the book and release it to test it. If the condition's Select Item targets a different item than this book, this preview can't test it and it reads as not having occurred.",
                BookTriggerConditionBased.ByTradeItem =>
                    "You've selected \"By Trade Item\" - genuinely active, but there's no trading UI in this preview to trigger it live. Test it via the Debug window's simulated trigger instead.",
                BookTriggerConditionBased.ByDestroyItem =>
                    "You've selected \"By Destroy Item\" - right-click the book and choose Destroy to test it live, or use the Debug window's simulated trigger.",
                BookTriggerConditionBased.ByHavingAnItem =>
                    "You've selected \"By Having An Item\" - genuinely active, but there's no inventory in this preview to check live. Test it via the Debug window's simulated trigger instead.",
                BookTriggerConditionBased.ByNotHavingAnItem =>
                    "You've selected \"By Not Having An Item\" - genuinely active, but there's no inventory in this preview to check live. Test it via the Debug window's simulated trigger instead.",
                BookTriggerConditionBased.ByEnter or BookTriggerConditionBased.ByStay or BookTriggerConditionBased.ByExit =>
                    $"You've selected \"{_book.EventPages[i].Conditions.TriggerConditionBased}\" - this is a location-based trigger (stepping onto/staying on/off a map cell), which doesn't apply to a book you read. It's genuinely active - test it via the Debug window's simulated scenario instead, or on a Map Event, where it belongs.",
                _ => "Unrecognized trigger type."
            };
            lines.Add(_book.EventPages.Count > 1 ? $"Page {i + 1}: {howTo}" : howTo);
        }
        return string.Join("\n\n", lines);
    }

    /// <summary>
    /// Runs automatically the moment a real interaction this window can
    /// simulate happens. justOccurred names WHICH momentary trigger type
    /// (Use/Pickup/Drop) just happened, if any - By After Read Item stays
    /// its own persistent case (_hasBeenRead), since closing the book after
    /// reading it isn't a "just this instant" event the way a click or a
    /// drop is. For Pickup/Drop specifically, the design doc is explicit
    /// that the condition's own Select Item target decides whether THIS
    /// book's drag even counts - if the condition targets a different
    /// item, this preview (which only has the one book in it) genuinely
    /// can't test that, and correctly reads as not having occurred here.
    /// Returns true only when a page's conditions passed AND it actually
    /// had commands to run - a passing page with zero commands still
    /// registers as fired (Cycle counts it), but per the doc, only a page
    /// with real commands is allowed to override built-in behavior like
    /// the default open-and-read.
    /// </summary>
    private bool TryFireEvent(BookTriggerConditionBased? justOccurred = null)
    {
        var trace = new List<string>();
        BookEventPage? firedPage = null;

        foreach (var page in _book.EventPages)
        {
            bool matchesTargetItem = page.Conditions.TriggerItemReference is not { } target
                || (target.Category == ReferencedItemCategory.Book && target.ItemId == _book.Id);

            var context = new BookEventPreviewContext
            {
                TriggerActuallyOccurred = page.Conditions.TriggerConditionBased switch
                {
                    BookTriggerConditionBased.Always => true,
                    // Must check justOccurred here too, not just the sticky _hasBeenRead flag -
                    // _hasBeenRead is meant to stay true permanently once read (see its own doc
                    // comment), so without this check, ANY later call to TryFireEvent for a
                    // completely unrelated reason (e.g. a fresh double-click passing ByUseItem)
                    // would see the stale true and wrongly fire this page immediately, before
                    // any reading happened in that interaction at all.
                    BookTriggerConditionBased.ByAfterReadItem => justOccurred == BookTriggerConditionBased.ByAfterReadItem && _hasBeenRead,
                    BookTriggerConditionBased.ByPickupItem or BookTriggerConditionBased.ByDropItem =>
                        matchesTargetItem && justOccurred.HasValue && page.Conditions.TriggerConditionBased == justOccurred.Value,
                    _ => justOccurred.HasValue && page.Conditions.TriggerConditionBased == justOccurred.Value
                },
                // Feeds the SAME real interaction into any Complex Condition
                // sub-conditions referencing a trigger type, so a page whose
                // top-level trigger is Always but has a Complex Condition
                // checking "By Drop Item" still responds to a real drop here.
                MomentaryTriggerJustOccurred = justOccurred,
                HasBeenRead = _hasBeenRead,
                SelfItemId = _book.Id,
                TimesTriggeredSoFar = _firedCounts.TryGetValue(page, out var count) ? count : 0
            };

            if (BookEventConditionEvaluator.Evaluate(page.Conditions, context, trace))
            {
                firedPage = page;
                break;
            }
        }

        if (firedPage is null)
        {
            EventStatusText.Text = "No event page's conditions were met for this interaction.";
            return false;
        }

        _firedCounts[firedPage] = (_firedCounts.TryGetValue(firedPage, out var c) ? c : 0) + 1;
        int pageNumber = _book.EventPages.IndexOf(firedPage) + 1;

        if (firedPage.Commands.Count == 0)
        {
            EventStatusText.Text = $"Event page {pageNumber}'s conditions passed, but it has no commands - default behavior still applies.";
            return false;
        }

        EventCommandInterpreter.Run(firedPage.Commands, _execContext, trace);
        EventStatusText.Text = $"Event page {pageNumber} fired - {firedPage.Commands.Count} command(s) run.";
        // If Change Self Book Content just fired while the reading view happens
        // to still be visible (an "Always" trigger checked mid-read, say), the
        // page should show the change immediately rather than waiting for the
        // next page-turn or a full close/reopen to pick it up.
        if (ReadingGrid.Visibility == Visibility.Visible) RenderCurrentPage();
        return true;
    }

    /// <summary>
    /// Real, verified Ultima 7 palette data - not a placeholder anymore.
    /// Extracted directly from a real PALETTES.FLX (user-provided), parsed
    /// per the confirmed FLX archive format (80-byte title, record count at
    /// offset 84, directory table at 128, 8 bytes/entry) and validated
    /// against Exult's own palette.h constants (PALETTE_DUSK=1,
    /// PALETTE_NIGHT=2). Afternoon is the middle of three candidate shades
    /// from the real Dusk/Dawn (palette 1) light-falloff ramp the user
    /// supplied. Evening is the average color across all 244 usable
    /// entries of the real Night (palette 2) data (excluding index 0, the
    /// documented special-transparency range 244-254, and unused
    /// pure-black slots) - chosen over the raw median or a single sampled
    /// entry because a real scene blends many different darkened materials
    /// at once (foliage, water, sky, walls), and the average is what
    /// actually captures that blend rather than one arbitrary color from
    /// it. It reads as a dark teal, not pure blue - a real, verified
    /// result, not the placeholder "night blue" guess this used to be.
    /// </summary>
    private static readonly Dictionary<string, (Color Color, double Opacity)> DayNightPeriodColors = new()
    {
        ["Morning"] = (Colors.Transparent, 0.0),
        ["Afternoon"] = (Color.FromRgb(0x9C, 0x68, 0x0C), 0.42),
        ["Evening"] = (Color.FromRgb(0x15, 0x57, 0x52), 0.55)
    };

    private void ApplyDayNightPeriod(string period)
    {
        if (!DayNightPeriodColors.TryGetValue(period, out var setting)) return;
        LightingOverlay.Fill = new SolidColorBrush(setting.Color);
        var fade = new DoubleAnimation(setting.Opacity, TimeSpan.FromSeconds(0.6));
        LightingOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>
    /// Reuses the exact same LightingOverlay mechanism as Change Day/Night
    /// - same fade animation, same rectangle - just an author-picked color
    /// (from any of the 12 real UltimaPaletteData groups) and
    /// author-picked strength instead of one of the three fixed periods.
    /// </summary>
    private void ApplyPaletteColorEffect(string hex, double opacity)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString("#" + hex);
            LightingOverlay.Fill = new SolidColorBrush(color);
            var fade = new DoubleAnimation(opacity, TimeSpan.FromSeconds(0.6));
            LightingOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        catch
        {
            // Malformed hex - leave the overlay as it was rather than crash the Preview.
        }
    }

    /// <summary>
    /// A DispatcherTimer jitters RootContentGrid's TranslateTransform by a
    /// random offset within [-intensity*2, +intensity*2] pixels every
    /// 30ms, resetting to (0,0) once the duration elapses. affectsCharacters
    /// is stored and honored as far as this Preview goes - there's no real
    /// NPC/PC on a map here to play a fall-down/stand-up animation on, so
    /// that part is scaffolding until the Map Editor exists, exactly as the
    /// command window's own help text says.
    /// </summary>
    private void PlayShakeEffect(int intensity, double durationSeconds, bool affectsCharacters)
    {
        var transform = new TranslateTransform();
        RootContentGrid.RenderTransform = transform;

        double maxOffset = intensity * 2.0;
        var random = new Random();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            if (stopwatch.Elapsed.TotalSeconds >= durationSeconds)
            {
                timer.Stop();
                transform.X = 0;
                transform.Y = 0;
                return;
            }
            transform.X = (random.NextDouble() * 2 - 1) * maxOffset;
            transform.Y = (random.NextDouble() * 2 - 1) * maxOffset;
        };
        timer.Start();
    }

    /// <summary>
    /// Real, visible Show Text: a centered overlay with the command's own
    /// text, dismissed by clicking it - this is the "further modification"
    /// needed since the command previously had no visual effect at all,
    /// only a Debug-window watch entry showing its Text parameter.
    /// </summary>
    private void ShowMessageOverlay(string text)
    {
        MessageOverlayText.Text = text;
        MessageOverlayBorder.Visibility = Visibility.Visible;
    }

    private void MessageOverlayBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        MessageOverlayBorder.Visibility = Visibility.Collapsed;
        e.Handled = true; // don't let this click also fall through to whatever's underneath (e.g. re-triggering the book itself)
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }

    // ---------- Sprite view: drag, single-click name, double-click to read ----------

    private void BookSpriteImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // By Use Item fires the instant the book is double-clicked -
            // the "use" action itself, not after reading it. Per the
            // design doc: if a matching page's conditions pass AND it has
            // real commands, those commands take over completely and the
            // built-in open-and-read behavior never happens at all. Only
            // when no page matches, or a matching page has zero commands,
            // does the default paper-doll reading still apply.
            if (!TryFireEvent(BookTriggerConditionBased.ByUseItem)) OpenReadingView();
            return;
        }

        _dragging = true;
        _movedDuringDrag = false;
        _dragStartMouse = e.GetPosition(SpriteCanvas);
        _dragStartElement = new Point(Canvas.GetLeft(BookSpriteImage), Canvas.GetTop(BookSpriteImage));
        BookSpriteImage.CaptureMouse();
    }

    /// <summary>Real, live "Destroy" test for By Destroy Item - the one condition among the four newly-activated trigger types that genuinely can be a real interaction in this preview, unlike Trade/Having/Not-Having which need a whole subsystem this project doesn't have yet.</summary>
    private void BookSpriteImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var destroyItem = new MenuItem { Header = "Destroy" };
        destroyItem.Click += (_, _) => TryFireEvent(BookTriggerConditionBased.ByDestroyItem);
        menu.Items.Add(destroyItem);
        menu.PlacementTarget = BookSpriteImage;
        menu.IsOpen = true;
    }

    private void BookSpriteImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var current = e.GetPosition(SpriteCanvas);
        double dx = current.X - _dragStartMouse.X;
        double dy = current.Y - _dragStartMouse.Y;

        if (!_movedDuringDrag && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
        {
            _movedDuringDrag = true;
            TryFireEvent(BookTriggerConditionBased.ByPickupItem); // the real moment of picking it up - grabbing it and actually starting to move it, not just clicking
        }

        Canvas.SetLeft(BookSpriteImage, _dragStartElement.X + dx);
        Canvas.SetTop(BookSpriteImage, _dragStartElement.Y + dy);
    }

    private void BookSpriteImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        BookSpriteImage.ReleaseMouseCapture();

        if (_movedDuringDrag) TryFireEvent(BookTriggerConditionBased.ByDropItem); // the real moment of releasing it after an actual drag
        else ShowNameBriefly();
    }

    private void ShowNameBriefly()
    {
        NameLabel.Text = _book.Name;
        Canvas.SetLeft(NameLabel, Canvas.GetLeft(BookSpriteImage));
        Canvas.SetTop(NameLabel, Canvas.GetTop(BookSpriteImage) - 20);
        NameLabel.Visibility = Visibility.Visible;

        _nameTimer?.Stop();
        _nameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _nameTimer.Tick += (_, _) =>
        {
            _nameTimer!.Stop();
            NameLabel.Visibility = Visibility.Collapsed;
        };
        _nameTimer.Start();
    }

    // ---------- Reading view: real rendered pages, left-click to turn ----------

    private void OpenReadingView()
    {
        if (_paperDollAnimator is null)
        {
            var paperDollSprite = _paperDollLookup(_book.PaperDollSpriteGroup, _book.PaperDollSpriteName);
            // BookEditorWindow already validated the paper doll exists before
            // opening this window, so if this ever comes back null it just
            // means the file moved/was deleted after that check.
            if (paperDollSprite is not null)
                _paperDollAnimator = new SpriteAnimator(paperDollSprite, PaperDollBackgroundImage, _resources);
        }

        PlaySound(_book.OpenSoundResourceId);
        _pageIndex = 0;
        RenderCurrentPage();

        SpriteCanvas.Visibility = Visibility.Collapsed;
        ReadingGrid.Visibility = Visibility.Visible;
    }

    private void RenderCurrentPage()
    {
        foreach (var animator in _pageOverlaySpriteAnimators) animator.Stop();
        _pageOverlaySpriteAnimators.Clear();

        FrameOverlayCanvas.Children.Clear();
        if (_book.Pages.Count == 0 || _pageIndex >= _book.Pages.Count) return;

        var page = _book.Pages[_pageIndex];
        foreach (var frame in _book.Frames)
        {
            var bbox = FrameGeometry.BoundingBox(frame);
            // A runtime override (from Change Self/External Book Content) takes
            // priority over the book's own real content, but ONLY within this
            // simulated running session - it never touches page.FrameTexts or
            // FullContentXaml themselves, so the book's actual authored data
            // stays exactly as designed the moment this Preview closes.
            string xaml = _execContext.GetContentOverride(_book.Id, _pageIndex, frame.Id)
                ?? (page.FrameTexts.TryGetValue(frame.Id, out var t) ? t : string.Empty);

            var result = FormattedContentRenderer.Render(xaml, bbox.Width, bbox.Height, _sprites);

            // A single container so the whole frame's content (text bitmap +
            // any embedded sprites) can be positioned and clipped together,
            // exactly the way the single RichTextBox overlay used to be -
            // nothing else in this method needs to know the display
            // mechanism changed.
            var container = new Canvas
            {
                Width = bbox.Width,
                Height = bbox.Height,
                Clip = FrameGeometry.BuildRelativeClipGeometry(frame, bbox)
            };

            if (result.TextBitmap is not null)
            {
                var textImage = new Image { Source = result.TextBitmap, Width = bbox.Width, Height = bbox.Height, Stretch = Stretch.None };
                container.Children.Add(textImage);
            }

            foreach (var spriteBox in result.Sprites)
            {
                var spriteImage = new Image
                {
                    Width = spriteBox.Bounds.Width,
                    Height = spriteBox.Bounds.Height,
                    Stretch = Stretch.Uniform
                };
                Canvas.SetLeft(spriteImage, spriteBox.Bounds.X);
                Canvas.SetTop(spriteImage, spriteBox.Bounds.Y);
                container.Children.Add(spriteImage);

                var animator = new SpriteAnimator(spriteBox.Sprite, spriteImage, _resources);
                animator.Ticked += OnAnySpriteTicked;
                _pageOverlaySpriteAnimators.Add(animator);
            }

            Canvas.SetLeft(container, bbox.X);
            Canvas.SetTop(container, bbox.Y);
            FrameOverlayCanvas.Children.Add(container);
        }
    }

    private void ReadingGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pageIndex++;
        if (_pageIndex >= _book.Pages.Count)
        {
            PlaySound(_book.CloseSoundResourceId);
            ReadingGrid.Visibility = Visibility.Collapsed;
            SpriteCanvas.Visibility = Visibility.Visible;
            _hasBeenRead = true; // the real moment ByAfterReadItem's trigger check is about
            TryFireEvent(BookTriggerConditionBased.ByAfterReadItem);
            return;
        }

        PlaySound(_book.PageFlipSoundResourceId);
        RenderCurrentPage();
    }

    private void PlaySound(int resourceId)
    {
        if (resourceId < 0) return;
        var entry = _resources.Entries.FirstOrDefault(r => r.Id == resourceId);
        if (entry is null || !File.Exists(entry.FilePath)) return;

        try
        {
            _mediaPlayer?.Close();
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.Open(new Uri(entry.FilePath, UriKind.Absolute));
            _mediaPlayer.Play();
        }
        catch { /* missing/unsupported audio file - silently skip, matching "if not, there is no sound" */ }
    }
}
