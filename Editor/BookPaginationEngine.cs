using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Drives the "warping"/overflow behavior: BookData.FullContentXaml is the
/// one true continuous text stream for a book. This engine walks it through
/// the book's frames in draw order, measuring how much fits in each frame's
/// bounding box, cycling to a new page once every frame on the current page
/// is full, and continuing until the content is exhausted. The result is
/// cached into BookData.Pages[n].FrameTexts purely for fast rendering - it
/// is never hand-edited, always recomputed here.
///
/// Word-boundary splitting: a paragraph that doesn't fully fit a frame is
/// split at the last word boundary that does fit - WHAT'S STORED in
/// FullContentXaml is never changed by this; the split only exists inside
/// this computation, recomputed fresh every time (so resizing a frame later
/// naturally finds a different split point with no stale leftover breaks).
///
/// Editing works by mapping a specific (page, frame) slice back to a
/// [start, end) SYMBOL OFFSET range within the full stream (WPF TextPointer
/// units, not plain character count), splicing the edited content into that
/// exact range, and re-running the whole pagination pass.
///
/// Honest v1 caveat: the word-boundary search is computed from the
/// paragraph's plain text length, then converted back to a TextPointer via
/// GetPositionAtOffset. This is exact for a uniformly-formatted paragraph.
/// For a paragraph with SEVERAL DIFFERENT formatting runs inside it (e.g.
/// part bold, part not), WPF's symbol counting includes non-visible
/// structural markers at run boundaries, so the chosen split point can
/// occasionally land a very small distance off from the intended word - it
/// will still land on a word boundary, just possibly not exactly the one a
/// character-count search predicted. Flagging this rather than hiding it.
/// </summary>
internal static class BookPaginationEngine
{
    /// <summary>A [start, end) range of symbol offsets into the full document that a given (page, frame) slice came from.</summary>
    public readonly struct SliceLocation
    {
        public int StartOffset { get; init; }
        public int EndOffset { get; init; }
    }

    /// <summary>The Text Font of the book being paginated right now (empty = system font) - pages are measured with the font they're drawn with.</summary>
    [ThreadStatic] private static string? _measureFontName;

    public static Dictionary<(int PageIndex, int FrameId), SliceLocation> Repaginate(BookData book)
    {
        string? previous = _measureFontName;
        _measureFontName = book.TextFontSpriteName;
        try { return RepaginateCore(book); }
        finally { _measureFontName = previous; }
    }

    private static Dictionary<(int PageIndex, int FrameId), SliceLocation> RepaginateCore(BookData book)
    {
        var map = new Dictionary<(int, int), SliceLocation>();
        var newPages = new List<BookPage>();

        if (book.Frames.Count == 0)
        {
            book.Pages = new List<BookPage> { new() };
            return map;
        }

        string remaining = book.FullContentXaml;
        int globalOffset = 0;
        const int safetyPageCap = 3000;

        while (true)
        {
            var page = new BookPage();
            foreach (var frame in book.Frames)
            {
                if (!HasRealContent(remaining)) continue;

                var bbox = FrameGeometry.BoundingBox(frame);
                var (frameXaml, rest, consumedLength) = SplitContentForFrame(remaining, Math.Max(20, bbox.Width), Math.Max(10, bbox.Height));

                // An image with no surrounding text can have a near-zero
                // symbol length by this counting method even though it's
                // real, displayable content - the old ">0" check silently
                // skipped assigning it to the frame entirely in that case.
                bool hasMeaningfulContent = consumedLength > 0
                    || (!string.IsNullOrWhiteSpace(frameXaml) && HasEmbeddedImage(frameXaml));

                if (hasMeaningfulContent)
                {
                    page.FrameTexts[frame.Id] = frameXaml;
                    map[(newPages.Count, frame.Id)] = new SliceLocation { StartOffset = globalOffset, EndOffset = globalOffset + consumedLength };
                    globalOffset += consumedLength;
                }
                remaining = rest;
            }
            newPages.Add(page);

            if (!HasRealContent(remaining)) break;
            if (newPages.Count > safetyPageCap) break; // safety valve against runaway loops
        }

        book.Pages = newPages;
        return map;
    }

    /// <summary>
    /// Snaps a [start, end) offset range out to the boundaries of whichever
    /// WHOLE paragraphs it touches. This is the fix for a real bug: word-
    /// boundary splitting is a DISPLAY-time computation only, but editing
    /// previously spliced directly against the display-truncated range. WPF's
    /// TextRange.Save/Load cannot represent "this fragment continues the
    /// previous paragraph" once saved and reloaded separately - every
    /// reload wraps a partial range as its OWN paragraph. Splicing against a
    /// mid-paragraph fragment therefore permanently re-fragmented the
    /// paragraph a little more on every keystroke, which is what produced
    /// "each new letter replaces the last" - pressing Enter "fixed" it only
    /// because a real paragraph break happens to match what serialization
    /// would produce anyway, hiding the mismatch. Editing must always target
    /// whole paragraphs; only display may show a word-limited slice of one.
    /// </summary>
    public static (int Start, int End) SnapToWholeParagraphs(string fullXaml, int start, int end)
    {
        var box = new RichTextBox();
        RichTextXamlHelper.Load(box, fullXaml);
        var doc = box.Document;
        var blocks = doc.Blocks.ToList();
        if (blocks.Count == 0) return (start, end);

        int total = doc.ContentStart.GetOffsetToPosition(doc.ContentEnd);
        if (start >= total) return (start, end); // appending past the end - nothing to snap to yet

        var startPtr = ClampOffset(doc, start);
        int clampedEnd = Math.Max(start, Math.Min(end, total));
        // Use a point INSIDE the range (end-1) to find the enclosing block for
        // the end side, so landing exactly on a paragraph boundary doesn't
        // accidentally pull in the next paragraph too.
        var endLookupPtr = ClampOffset(doc, Math.Max(start, clampedEnd - 1));

        Block? FindBlock(TextPointer p) => blocks.FirstOrDefault(b => p.CompareTo(b.ContentStart) >= 0 && p.CompareTo(b.ContentEnd) <= 0);

        var startBlock = FindBlock(startPtr) ?? blocks[0];
        var endBlock = FindBlock(endLookupPtr) ?? startBlock;

        int snappedStart = doc.ContentStart.GetOffsetToPosition(startBlock.ContentStart);
        int snappedEnd = doc.ContentStart.GetOffsetToPosition(endBlock.ContentEnd);
        return (snappedStart, Math.Max(snappedStart, snappedEnd));
    }

    /// <summary>Extracts [start, end) of the full stream as its own saved fragment - used to load the FULL (unsplit) paragraph(s) into the edit popup.</summary>
    public static string ExtractRange(string fullXaml, int start, int end)
    {
        if (end <= start) return string.Empty;
        var box = new RichTextBox();
        RichTextXamlHelper.Load(box, fullXaml);
        var doc = box.Document;
        var startPtr = ClampOffset(doc, start);
        var endPtr = ClampOffset(doc, end);
        return SaveRange(new TextRange(startPtr, endPtr));
    }

    /// <summary>
    /// Replaces the content at symbol offsets [startOffset, endOffset) in the
    /// full stream with replacementXaml, and returns the new full stream.
    /// This runs on every single edit, whether or not pagination splitting is
    /// involved - it was previously done by manually extracting "before" and
    /// "after" as separate saved fragments and re-appending all three pieces
    /// via three sequential Load() calls into a fresh document. That
    /// hand-rolled reconstruction was a real risk point for losing embedded
    /// content (an image) during reassembly. Using TextRange.Load directly on
    /// the target [startPtr, endPtr) range is WPF's own native "replace this
    /// range" operation - one well-defined step instead of a manual rebuild.
    /// </summary>
    public static string Splice(string fullXaml, int startOffset, int endOffset, string replacementXaml)
    {
        var source = new RichTextBox();
        RichTextXamlHelper.Load(source, fullXaml);
        var doc = source.Document;

        // A brand-new/empty book has ZERO paragraphs at all (Load() clears
        // blocks and stops there for empty input). TextRange.Load needs an
        // existing paragraph to anchor into - a Floater must structurally
        // live inside a paragraph's Inlines. Loading complex content like an
        // image into a collapsed position with no paragraph at all could
        // silently fail to attach. Guarantee one exists first.
        if (doc.Blocks.Count == 0)
        {
            doc.Blocks.Add(new Paragraph());
        }

        var startPtr = ClampOffset(doc, startOffset);
        var endPtr = ClampOffset(doc, Math.Max(startOffset, endOffset));
        var replaceRange = new TextRange(startPtr, endPtr);

        if (string.IsNullOrWhiteSpace(replacementXaml))
        {
            replaceRange.Text = string.Empty; // pure deletion
        }
        else
        {
            using var stream = new MemoryStream(Convert.FromBase64String(replacementXaml));
            replaceRange.Load(stream, DataFormats.XamlPackage);
        }

        // Something about repeated range-replace operations over a long
        // editing session was leaving behind extra EMPTY paragraphs as a
        // side effect - confirmed by diagnostics showing well over a hundred
        // blocks accumulate where only 2 should exist. Rather than chase the
        // exact WPF mechanism producing them, strip genuinely empty
        // paragraphs (no text, no embedded image) after every edit so they
        // can never pile up in the first place.
        var emptyBlocks = doc.Blocks
            .Where(b => !ContainsEmbeddedElement(b) && string.IsNullOrEmpty(new TextRange(b.ContentStart, b.ContentEnd).Text))
            .ToList();
        foreach (var empty in emptyBlocks)
        {
            if (doc.Blocks.Count <= 1) break; // always keep at least one block, even if empty
            doc.Blocks.Remove(empty);
        }

        return RichTextXamlHelper.Save(source);
    }

    /// <summary>Total symbol length of a saved stream, for finding the "append at the end" point when a frame/page slice doesn't exist yet.</summary>
    public static int GetTotalLength(string xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return 0;
        var box = new RichTextBox();
        RichTextXamlHelper.Load(box, xaml);
        return box.Document.ContentStart.GetOffsetToPosition(box.Document.ContentEnd);
    }

    private static TextPointer ClampOffset(FlowDocument doc, int offset)
    {
        int total = doc.ContentStart.GetOffsetToPosition(doc.ContentEnd);
        int clamped = Math.Max(0, Math.Min(total, offset));
        return doc.ContentStart.GetPositionAtOffset(clamped) ?? doc.ContentStart;
    }

    /// <summary>
    /// Takes as much of remainingXaml as fits in (width, height) - as many
    /// whole paragraphs as fit, plus if the next paragraph is too tall,
    /// a word-boundary-limited prefix of it. Returns (thatSlice, whatsLeft,
    /// symbolLengthConsumed).
    /// </summary>
    private static (string FrameXaml, string RemainingXaml, int ConsumedLength) SplitContentForFrame(string remainingXaml, double width, double height)
    {
        if (!HasRealContent(remainingXaml)) return (string.Empty, string.Empty, 0);

        var scratch = new RichTextBox();
        RichTextXamlHelper.Load(scratch, remainingXaml);
        var doc = scratch.Document;
        var blocks = doc.Blocks.ToList();
        if (blocks.Count == 0) return (string.Empty, string.Empty, 0);

        // Measuring a range's height via a detached, never-displayed probe
        // RichTextBox has been the prime remaining suspect after ruling out
        // both Splice and word-boundary splitting as the cause of images
        // vanishing during pagination - Floater layout may simply not
        // resolve correctly outside a real visual tree. Rather than keep
        // debugging that measurement, stop relying on it for image-bearing
        // paragraphs: use a fixed conservative height estimate for those
        // instead of ever measuring a range that contains one.
        const double estimatedImageParagraphHeight = 170;

        // Measuring each paragraph in isolation and separately reading its
        // Margin property (an earlier attempt at this fix) turned out to
        // read as NaN: a detached document that's never been part of a
        // live, styled visual tree never gets its implicit theme margin
        // resolved to a real number. Measuring a CUMULATIVE range instead -
        // from the last safe boundary through a candidate paragraph -
        // sidesteps that: it only ever reads MeasureRangeHeight's actual
        // layout result, never a stored property WPF never resolved, and
        // margins between paragraphs are naturally included because the
        // range spans real, complete paragraphs together.
        //
        // Checking every paragraph one at a time this way is O(N) calls,
        // each re-serializing the whole growing prefix - O(N^2) total,
        // which is what visibly froze on a large paste. Building the probe
        // up incrementally (appending one paragraph via TextRange.Load at
        // ContentEnd) was the first attempt at fixing that, but proved to
        // be flat-out wrong: it silently replaces the probe's content
        // instead of adding to it, so every "cumulative" height came back
        // identical - which is exactly why pagination stopped spilling
        // into a second frame or a new page at all (it always believed
        // everything already fit). The actual fix keeps the exact same
        // per-candidate measurement that was already proven correct, but
        // uses BINARY SEARCH to pick which candidates to check - O(log N)
        // measurements instead of O(N) for a run of N paragraphs, each
        // still a full, real, already-trusted MeasureRangeHeight call.
        int fitBlocks = 0;
        double baseHeight = 0;
        var runStart = doc.ContentStart;
        int runStartIndex = 0;
        int i = 0;
        while (i < blocks.Count && !ContainsEmbeddedElement(blocks[i])) i++;

        while (true)
        {
            int runLength = i - runStartIndex;
            double runHeight = 0;
            int fitCount = runLength == 0
                ? 0
                : FindFittingParagraphCount(runStart, blocks, runStartIndex, i, width, height - baseHeight, out runHeight);
            fitBlocks = runStartIndex + fitCount;

            if (fitCount < runLength) break; // the run itself overflowed before reaching the next image

            baseHeight += runHeight;
            if (i >= blocks.Count) break; // resolved every paragraph, no image left to consider

            // blocks[i] is an image - see if it fits after the run that just did.
            double totalWithImage = baseHeight + estimatedImageParagraphHeight;
            if (totalWithImage > height) break; // image itself doesn't fit - fitBlocks is already correct

            baseHeight = totalWithImage;
            fitBlocks = i + 1;
            runStart = blocks[i].ContentEnd;
            runStartIndex = i + 1;
            i = runStartIndex;
            while (i < blocks.Count && !ContainsEmbeddedElement(blocks[i])) i++;
        }

        if (fitBlocks == blocks.Count)
        {
            // Everything remaining fits as whole paragraphs.
            int consumed = doc.ContentStart.GetOffsetToPosition(doc.ContentEnd);
            return (remainingXaml, string.Empty, consumed);
        }

        // blocks[fitBlocks] is the first paragraph that doesn't fit as a whole.
        // Try a word-boundary partial fit of it - UNLESS it contains an
        // embedded image (Floater). Word-boundary offsets are computed from
        // TextRange.Text, which is PLAIN TEXT ONLY and does not count a
        // Floater's own internal symbols - so converting a plain-text offset
        // back into a real TextPointer via GetPositionAtOffset becomes
        // misaligned once an image is present, silently dropping or
        // misplacing it. This was the actual cause of an inserted picture
        // showing correctly in the edit popup but vanishing from the main
        // canvas preview. Simplest robust fix: a paragraph with an image in
        // it is never split mid-paragraph - it either fits whole, or moves
        // whole to the next frame, exactly like the "no word fits" fallback
        // below already does.
        var boundaryPara = blocks[fitBlocks];
        // Real, confirmed fix - re-enabled. The diagnostic below that left this
        // off was investigating the vanishing-image bug, but that bug's real
        // fix is the SEPARATE !ContainsEmbeddedElement(boundaryPara) guard
        // immediately below, which structurally prevents word-splitting from
        // ever touching an image-bearing paragraph regardless of this flag -
        // so leaving it off bought no extra safety, only cost. And leaving it
        // off is exactly what caused the real, confirmed freeze on large text
        // or a large font size: with no word-splitting at all, a paragraph
        // too big to fit whole falls through to the "not even one word fits"
        // emergency fallback below, which force-consumes ONE WORD AT A TIME -
        // and every single one of those words means another full call back
        // into SplitContentForFrame, which re-parses the ENTIRE remaining
        // XAML string from scratch via RichTextXamlHelper.Load. For a large
        // paste, or once a bigger font makes ordinary paragraphs too tall to
        // fit whole, that turns into thousands of full re-parses of a still-
        // nearly-full-size document - genuine, catastrophic O(N^2) work, not
        // a "slow but finite" cost, which is exactly what reads as a true
        // freeze. With word-splitting back on, a too-large paragraph is
        // measured via the same already-proven MeasureRangeHeight calls but
        // stops at however many WORDS actually fit in the frame (bounded by
        // that one frame's own capacity, not by the whole document), so the
        // total number of pages - and re-parses - returns to being
        // proportional to content-size / frame-capacity, not to word count.
        const bool enableWordBoundarySplitting = true;

        if (enableWordBoundarySplitting && !ContainsEmbeddedElement(boundaryPara))
        {
            string paraText = new TextRange(boundaryPara.ContentStart, boundaryPara.ContentEnd).Text;
            var wordBreaks = FindWordBreakOffsets(paraText);

            int bestWordOffset = 0;
            foreach (int wb in wordBreaks)
            {
                var splitPointer = boundaryPara.ContentStart.GetPositionAtOffset(wb);
                if (splitPointer is null) continue;
                var candidate = new TextRange(doc.ContentStart, splitPointer);
                if (MeasureRangeHeight(candidate, width) > height) break;
                bestWordOffset = wb;
            }

            if (bestWordOffset > 0)
            {
                var splitPointer = boundaryPara.ContentStart.GetPositionAtOffset(bestWordOffset)!;
                string frameXaml = SaveRange(new TextRange(doc.ContentStart, splitPointer));
                string remainingOut = splitPointer.CompareTo(doc.ContentEnd) < 0
                    ? SaveRange(new TextRange(splitPointer, doc.ContentEnd))
                    : string.Empty;
                int consumed = doc.ContentStart.GetOffsetToPosition(splitPointer);
                return (frameXaml, remainingOut, consumed);
            }
        }

        if (fitBlocks > 0)
        {
            // Some earlier whole paragraphs fit; ship those, leave the boundary paragraph whole for the next frame.
            var cut = blocks[fitBlocks - 1].ContentEnd;
            string frameXaml = SaveRange(new TextRange(doc.ContentStart, cut));
            string remainingOut = SaveRange(new TextRange(blocks[fitBlocks].ContentStart, doc.ContentEnd));
            int consumed = doc.ContentStart.GetOffsetToPosition(cut);
            return (frameXaml, remainingOut, consumed);
        }

        // Nothing fits at all - not even the boundary paragraph on its own.
        if (ContainsEmbeddedElement(boundaryPara))
        {
            // Can't usefully split an image-bearing paragraph at the word
            // level - force the WHOLE paragraph through so the image is
            // never dropped. It may visually overflow this frame slightly,
            // which is preferable to silently losing the picture.
            var forcedCut = boundaryPara.ContentEnd;
            string forcedWholeXaml = SaveRange(new TextRange(doc.ContentStart, forcedCut));
            string forcedWholeRemaining = forcedCut.CompareTo(doc.ContentEnd) < 0
                ? SaveRange(new TextRange(forcedCut, doc.ContentEnd))
                : string.Empty;
            int forcedWholeConsumed = Math.Max(1, doc.ContentStart.GetOffsetToPosition(forcedCut));
            return (forcedWholeXaml, forcedWholeRemaining, forcedWholeConsumed);
        }

        // Plain text paragraph, not even one word fits - force at least one
        // word through so we never loop forever; it may visually overflow
        // this frame slightly.
        string paraTextForForce = new TextRange(boundaryPara.ContentStart, boundaryPara.ContentEnd).Text;
        var wordBreaksForForce = FindWordBreakOffsets(paraTextForForce);
        int forceOffset = wordBreaksForForce.Count > 0 ? wordBreaksForForce[0] : Math.Max(1, paraTextForForce.Length);
        var forcedPointer = boundaryPara.ContentStart.GetPositionAtOffset(forceOffset) ?? boundaryPara.ContentEnd;
        string forcedFrameXaml = SaveRange(new TextRange(doc.ContentStart, forcedPointer));
        string forcedRemaining = forcedPointer.CompareTo(doc.ContentEnd) < 0
            ? SaveRange(new TextRange(forcedPointer, doc.ContentEnd))
            : string.Empty;
        int forcedConsumed = Math.Max(1, doc.ContentStart.GetOffsetToPosition(forcedPointer));
        return (forcedFrameXaml, forcedRemaining, forcedConsumed);
    }

    /// <summary>Diagnostic: whether a saved stream (any book-level or per-frame slice) contains an embedded image anywhere in it.</summary>
    public static bool HasEmbeddedImage(string xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return false;
        var box = new RichTextBox();
        RichTextXamlHelper.Load(box, xaml);
        return box.Document.Blocks.Any(ContainsEmbeddedElement);
    }

    /// <summary>Diagnostic: reports what the fit-measurement logic actually computes for the FIRST paragraph of a stream, against a given frame size - to see real numbers instead of guessing whether measurement is the broken link.</summary>
    /// <summary>Diagnostic: a human-readable summary of exactly what's structurally inside a saved stream - block count, whether each block has an image, and a short plain-text preview.</summary>
    public static string DescribeContent(string xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return "(empty/null string)";

        var box = new RichTextBox();
        RichTextXamlHelper.Load(box, xaml);
        var blocks = box.Document.Blocks.ToList();
        if (blocks.Count == 0) return "(loaded, but zero blocks)";

        var lines = new List<string>();
        for (int i = 0; i < blocks.Count; i++)
        {
            bool hasImg = ContainsEmbeddedElement(blocks[i]);
            string preview = new TextRange(blocks[i].ContentStart, blocks[i].ContentEnd).Text;
            preview = preview.Length > 40 ? preview.Substring(0, 40) + "..." : preview;
            lines.Add($"  Block {i}: hasImage={hasImg}, text=\"{preview}\"");
        }
        return $"({blocks.Count} blocks)\n" + string.Join("\n", lines);
    }

    public static string DiagnoseFirstParagraphFit(string xaml, double frameWidth, double frameHeight)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return "No content to measure.";

        var box = new RichTextBox();
        RichTextXamlHelper.Load(box, xaml);
        var blocks = box.Document.Blocks.ToList();
        if (blocks.Count == 0) return "Zero blocks - nothing to measure.";

        bool hasImage = ContainsEmbeddedElement(blocks[0]);
        double measured = hasImage ? 170 : MeasureRangeHeight(new TextRange(box.Document.ContentStart, blocks[0].ContentEnd), frameWidth);

        return $"Block count: {blocks.Count}\n" +
               $"First block contains image: {hasImage}\n" +
               $"Frame size given to measurement: {frameWidth:0.0} x {frameHeight:0.0}\n" +
               $"Measured height of first block: {measured:0.0}\n" +
               $"Would be judged as fitting: {(measured <= frameHeight)}";
    }

    /// <summary>Whether a block (paragraph) contains any embedded UI element (image via Floater) - such a paragraph must never be split at a word-boundary offset, since those offsets are computed from plain text and become misaligned once non-text content is present.</summary>
    private static bool ContainsEmbeddedElement(Block block)
    {
        if (block is not Paragraph para) return false;
        return para.Inlines.Any(inline => inline is Floater or InlineUIContainer);
    }

    /// <summary>Character offsets (within a paragraph's plain text) that are safe to split at - right after each run of whitespace.</summary>
    private static List<int> FindWordBreakOffsets(string text)
    {
        var offsets = new List<int>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                int j = i;
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                offsets.Add(j);
                i = j;
            }
            else
            {
                i++;
            }
        }
        return offsets;
    }

    /// <summary>
    /// Whether a saved stream has any real (non-whitespace) text in it.
    /// Unlike string.IsNullOrWhiteSpace(xaml) - which checks the base64
    /// BLOB itself and is essentially always false once any content exists,
    /// even an "empty" trailing paragraph - this actually loads the document
    /// and checks its real text. This was the root cause of the runaway
    /// endless-empty-pages bug: the old blob-level check could never detect
    /// "nothing meaningful is left," so the loop kept manufacturing pages to
    /// consume structural leftovers that had no visible text at all.
    /// </summary>
    private static bool HasRealContent(string xaml)
    {
        if (string.IsNullOrEmpty(xaml)) return false;
        var box = new RichTextBox();
        RichTextXamlHelper.Load(box, xaml);
        if (box.Document.Blocks.Any(ContainsEmbeddedElement)) return true; // an image is real, displayable content even with zero plain text
        string text = new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;
        return !string.IsNullOrWhiteSpace(text);
    }

    /// <summary>
    /// Binary search for how many of blocks[startIndex..endIndexExclusive)
    /// fit within budget, each candidate measured as one cumulative range
    /// from runStart through that candidate's end - the exact same
    /// MeasureRangeHeight call already proven correct, just invoked
    /// O(log K) times instead of O(K) times for a run of K paragraphs.
    /// Valid because cumulative height only ever grows as more paragraphs
    /// are included, never shrinks.
    /// </summary>
    private static int FindFittingParagraphCount(TextPointer runStart, List<Block> blocks, int startIndex, int endIndexExclusive, double width, double budget, out double fitHeight)
    {
        int count = endIndexExclusive - startIndex;
        fitHeight = 0;
        if (count <= 0) return 0;

        int lo = 1, hi = count;
        int best = 0;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            double h = MeasureRangeHeight(new TextRange(runStart, blocks[startIndex + mid - 1].ContentEnd), width);
            if (h <= budget)
            {
                best = mid;
                fitHeight = h;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    private static RichTextBox CreateMeasureProbe(double width) => new()
    {
        Width = width,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        // Must match the font used everywhere content is actually displayed
        // (overlay/popup both default to Segoe UI 12) - measuring with a
        // different default font than what's displayed was producing
        // wrong fit decisions.
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 12
    };

    private static double MeasureRangeHeight(TextRange range, double width)
    {
        string xaml = SaveRange(range);
        // A book written in a sprite font is drawn with it - so it's measured with it too.
        if (!string.IsNullOrEmpty(_measureFontName) && FormattedContentRenderer.MeasureWithSpriteFont(xaml, width, _measureFontName) is double fontHeight) return fontHeight;
        var probe = CreateMeasureProbe(width);
        RichTextXamlHelper.Load(probe, xaml);
        probe.Measure(new Size(width, double.PositiveInfinity));
        return probe.DesiredSize.Height;
    }

    /// <summary>
    /// Real numbers instead of another guess: reports the SAME cumulative
    /// measurement SplitContentForFrame actually uses, one entry per
    /// paragraph, so a report of "content overflows but pagination didn't
    /// react" can be checked against real figures. Deliberately calls
    /// MeasureRangeHeight directly here (the O(N^2)-but-proven-correct
    /// form) rather than the binary-search path the real logic uses - this
    /// is a manually-triggered, one-frame-at-a-time debug tool, not
    /// something that runs on every keystroke, so the slower form is an
    /// acceptable trade for showing every individual paragraph's number
    /// instead of only the handful of points binary search actually checks.
    /// </summary>
    public static string DiagnosePagination(string xaml, double width, double height)
    {
        var scratch = new RichTextBox();
        RichTextXamlHelper.Load(scratch, xaml);
        var doc = scratch.Document;
        var blocks = doc.Blocks.ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Target: width={width:0.#}, height={height:0.#}");
        sb.AppendLine($"Paragraph count: {blocks.Count}");

        double baseHeight = 0;
        double running = 0;
        var runStart = doc.ContentStart;
        const double estimatedImageParagraphHeight = 170;

        for (int i = 0; i < blocks.Count; i++)
        {
            bool hasImage = ContainsEmbeddedElement(blocks[i]);
            double totalThroughHere = hasImage
                ? running + estimatedImageParagraphHeight
                : baseHeight + MeasureRangeHeight(new TextRange(runStart, blocks[i].ContentEnd), width);

            running = totalThroughHere;
            string flag = running > height ? "  <-- EXCEEDS TARGET HEIGHT HERE" : string.Empty;
            sb.AppendLine($"  [{i}] {(hasImage ? "(image, fixed estimate) " : string.Empty)}cumulative height={running:0.#}{flag}");

            if (hasImage)
            {
                baseHeight = running;
                runStart = blocks[i].ContentEnd;
            }
        }

        // Direct proof, not another guess: read the ACTUAL font size/weight
        // straight off the original document's first real Run (what the
        // person genuinely applied via the toolbar), and separately what
        // the SAME paragraph's re-extracted range reports once it's gone
        // through SaveRange/Load in isolation - if these two disagree, the
        // extraction step itself is where formatting is being lost, not
        // the measurement math.
        if (blocks.Count > 0 && blocks[0] is Paragraph firstPara)
        {
            var firstRun = firstPara.Inlines.OfType<Run>().FirstOrDefault();
            if (firstRun is not null)
            {
                sb.AppendLine();
                sb.AppendLine($"Original first paragraph's actual Run: FontSize={firstRun.FontSize:0.#}, FontWeight={firstRun.FontWeight}, FontFamily={firstRun.FontFamily}");

                string extractedXaml = SaveRange(new TextRange(blocks[0].ContentStart, blocks[0].ContentEnd));
                var reloadCheck = new RichTextBox();
                RichTextXamlHelper.Load(reloadCheck, extractedXaml);
                var reloadedRun = reloadCheck.Document.Blocks.OfType<Paragraph>().FirstOrDefault()?.Inlines.OfType<Run>().FirstOrDefault();
                sb.AppendLine(reloadedRun is not null
                    ? $"Same paragraph after SaveRange/Load round-trip: FontSize={reloadedRun.FontSize:0.#}, FontWeight={reloadedRun.FontWeight}, FontFamily={reloadedRun.FontFamily}"
                    : "Same paragraph after SaveRange/Load round-trip: NO Run found at all (content structure itself changed).");
            }
        }

        return sb.ToString();
    }

    private static string SaveRange(TextRange range)
    {
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.XamlPackage);
        return Convert.ToBase64String(stream.ToArray());
    }

}

/// <summary>Small shared geometry helper so the pagination engine doesn't need to know about WPF Shape rendering.</summary>
internal static class FrameGeometry
{
    public static Rect BoundingBox(BookTextFrame frame)
    {
        double minX = frame.Points.Min(p => p.X), maxX = frame.Points.Max(p => p.X);
        double minY = frame.Points.Min(p => p.Y), maxY = frame.Points.Max(p => p.Y);
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    /// <summary>Same shape-to-clip-geometry logic used for the editor canvas overlay, shared here so the reading preview renders frames identically.</summary>
    public static Geometry BuildRelativeClipGeometry(BookTextFrame frame, Rect bbox)
    {
        return frame.ShapeType switch
        {
            FrameShapeType.Rectangle => new RectangleGeometry(new Rect(0, 0, bbox.Width, bbox.Height)),
            FrameShapeType.Ellipse => new EllipseGeometry(new Point(bbox.Width / 2, bbox.Height / 2), bbox.Width / 2, bbox.Height / 2),
            _ => new PathGeometry(new[]
            {
                new PathFigure(
                    new Point(frame.Points[0].X - bbox.X, frame.Points[0].Y - bbox.Y),
                    frame.Points.Skip(1).Select(p => new LineSegment(new Point(p.X - bbox.X, p.Y - bbox.Y), true)),
                    true)
            })
        };
    }
}
