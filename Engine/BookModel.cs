namespace SandcastleRpgMaker.Engine;

public enum FrameShapeType
{
    Rectangle,
    Ellipse,
    /// <summary>Straight-edged shape built by clicking points in sequence (the "Line" tool).</summary>
    Polygon,
    /// <summary>Freehand pencil-drawn outline (continuous drag).</summary>
    Freehand
}

/// <summary>
/// One user-drawn text frame. This is shared LAYOUT for the whole book - its
/// shape/position is the same on every page (drawn once, like a template),
/// exactly like the physical paper-doll artwork itself. Only the TEXT that
/// appears inside a frame changes per page (see BookPage.FrameTexts).
/// </summary>
/// <summary>
/// A plain point with real, named properties - deliberately NOT a
/// ValueTuple. This was the actual root cause of a real bug: a frame's
/// Points were previously List&lt;(double X, double Y)&gt;, and
/// System.Text.Json does not reliably serialize bare ValueTuples (it only
/// reflects over public PROPERTIES by default, and ValueTuple exposes only
/// public FIELDS) - every frame's geometry silently came back empty/default
/// after a save/load round trip. That collapsed every frame's bounding box
/// to near-zero size, which in turn made the pagination engine's
/// safety-valve loop grind through thousands of pages trying to fit real
/// content into frames it could no longer see - explaining the "20 second
/// load", "Page 1 of 3001", and "frames disappeared" all as one root cause.
/// Engine deliberately has no WPF reference (see Engine.csproj), so this
/// can't just be System.Windows.Point - it's a minimal equivalent that
/// serializes correctly and needs no extra JsonSerializerOptions.
/// </summary>
public struct BookPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public BookPoint() { }
    public BookPoint(double x, double y) { X = x; Y = y; }
}

public class BookTextFrame
{
    public int Id { get; set; }
    public FrameShapeType ShapeType { get; set; } = FrameShapeType.Rectangle;
    public List<BookPoint> Points { get; set; } = new();
    public string OutlineColorHex { get; set; } = "#FFFF0000"; // default red

    /// <summary>Background color of the text-entry popup for THIS frame specifically - saved with the frame, changeable anytime.</summary>
    public string EditorBackgroundColorHex { get; set; } = "#FFFCEBB6"; // default pale yellow
}

/// <summary>
/// One page's worth of content: for each shared frame (by Id), the text that
/// currently shows on THIS page. This is a COMPUTED CACHE, not authored data -
/// it's rebuilt by the pagination engine (BookEditorWindow.RepaginateBook)
/// from BookData.FullContentXaml every time content or frame layout changes.
/// A frame with no entry here is simply blank on this page.
/// </summary>
public class BookPage
{
    public Dictionary<int, string> FrameTexts { get; set; } = new();
}

/// <summary>
/// A book: inherits every shared placeable-item property (Name, Weight,
/// Destructible, Sprite, Faction, etc.) and adds book-specific content -
/// a shared frame layout, one continuous text stream, and interaction sounds.
/// NOTE (v1 scope): overflow is computed at PARAGRAPH granularity (a single
/// paragraph too tall for any frame is not further split line-by-line);
/// non-rectangular frames (ellipse/polygon/freehand) are measured against
/// their bounding box, not their exact silhouette; animated "Image Fonts"
/// are not implemented. Flagging honestly rather than claiming more than
/// what's built.
/// </summary>
public class BookData : PlaceableItemBase
{
    public int OpenSoundResourceId { get; set; } = -1;
    public int CloseSoundResourceId { get; set; } = -1;
    public int PageFlipSoundResourceId { get; set; } = -1;

    /// <summary>The book's open-book background artwork (static or animated, from Sprite Editor). Stays fixed for every page - only how the text distributes across frames changes.</summary>
    public string PaperDollSpriteGroup { get; set; } = string.Empty;
    public string PaperDollSpriteName { get; set; } = string.Empty;

    /// <summary>
    /// The font this book's pages are written in: the name of a sprite in the
    /// Sprite Editor's Fonts group, or empty for the default (the ordinary
    /// system font, exactly as books have always looked).
    /// </summary>
    public string TextFontSpriteName { get; set; } = string.Empty;

    /// <summary>The language it's written in (System Settings > Languages), -1 none: not understood, its pages show in that language's font.</summary>
    public int LanguageId { get; set; } = -1;

    /// <summary>The shared text-frame layout, drawn once, reused on every page.</summary>
    public List<BookTextFrame> Frames { get; set; } = new();

    /// <summary>
    /// The book's ENTIRE text as one continuous stream (base64 XamlPackage).
    /// This is the canonical source of truth - Pages/FrameTexts are always
    /// recomputed from this plus the current frame layout, never authored
    /// independently per page.
    /// </summary>
    public string FullContentXaml { get; set; } = string.Empty;

    /// <summary>Computed pagination cache - rebuilt automatically, not hand-edited.</summary>
    public List<BookPage> Pages { get; set; } = new();

    /// <summary>The book's event: one or more pages, each with its own conditions and command list. Lazily edited via the Event tab.</summary>
    public List<BookEventPage> EventPages { get; set; } = new();

    /// <summary>Variables scoped to just this book - visible/shared across all of its own event pages, but never outside this book. Distinct from the project-wide global variable database.</summary>
    public List<VariableDefinition> LocalVariables { get; set; } = new();
    private int _nextLocalVariableId = 1;
    public int GetNextLocalVariableId() => _nextLocalVariableId++;

    /// <summary>Re-syncs the local variable id counter after loading from disk (the counter itself, being private, isn't persisted).</summary>
    public void SyncLocalVariableIdAfterLoad()
    {
        _nextLocalVariableId = LocalVariables.Count == 0 ? 1 : LocalVariables.Max(v => v.Id) + 1;
    }

    public int GetNextFrameId() => Frames.Count == 0 ? 1 : Frames.Max(f => f.Id) + 1;
}

public class BookDatabase
{
    public List<BookData> Books { get; set; } = new();

    private int _nextId = 1;

    public BookData CreateNew()
    {
        var book = new BookData { Id = _nextId++, Name = $"Book {_nextId - 1}" };
        book.Pages.Add(new BookPage());
        Books.Add(book);
        return book;
    }

    public void SyncNextIdAfterLoad()
    {
        _nextId = Books.Count == 0 ? 1 : Books.Max(b => b.Id) + 1;
        foreach (var book in Books) book.SyncLocalVariableIdAfterLoad();
    }
}
