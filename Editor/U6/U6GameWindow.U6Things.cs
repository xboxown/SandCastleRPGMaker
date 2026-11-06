// Ultima 6 engine (U6GameWindow): bags, books and bodies as Ultima 6 has them - Ultima 6 projects only.
using System.Text.RegularExpressions;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The last of the Ultima 7 windows gone from Ultima 6 games (Nuvie):
///  - A bag carried: Use opens it in the panel - the carried cells show what's in it, the bag itself first (Use it again: back
///    to what's carried). Move puts things into the open bag.
///  - A book (or a scroll, a sign with a book): read in the text area, a screenful at a time - any key goes on, Escape stops.
///  - A body: searched like a chest - "Searching here, you find ..." and its belongings lie on its square.
/// </summary>
public partial class U6GameWindow
{
    /// <summary>The bag open in the panel (one the shown member carries).</summary>
    private InventoryItemInstance? _hudOpenBag;

    private static bool IsBag(InventoryItemInstance item) => item.Category == ReferencedItemCategory.Container;

    /// <summary>Whether the member carries this (at any depth).</summary>
    private static bool Carries(CharacterDefinition member, InventoryItemInstance item)
    {
        bool In(List<InventoryItemInstance> list) => list.Any(i => ReferenceEquals(i, item) || In(i.Contents));
        return In(member.Inventory);
    }

    /// <summary>The panel's carried cells: the open bag (itself first, to go back), else what's carried and the pack's contents.</summary>
    private IEnumerable<InventoryItemInstance> HudCarriedItems(CharacterDefinition member)
    {
        if (_hudOpenBag is { } bag && Carries(member, bag))
        {
            yield return bag;
            foreach (var inside in bag.Contents) yield return inside;
            yield break;
        }
        _hudOpenBag = null;
        var worn = member.EquippedSlots.Values.ToHashSet();
        foreach (var item in member.Inventory.Where(i => !worn.Contains(i.InstanceId))) yield return item;
        if (member.EquippedSlots.TryGetValue(EquipmentSlot.Backpack, out int pack) && member.Inventory.FirstOrDefault(i => i.InstanceId == pack) is { } backpack)
            foreach (var inside in backpack.Contents) yield return inside;
    }

    /// <summary>Use on a carried bag: open it in the panel, or (the open one) close it.</summary>
    private bool U6UseBag(InventoryItemInstance item, string name)
    {
        if (!IsBag(item)) return false;
        if (ReferenceEquals(item, _hudOpenBag)) _hudOpenBag = null;
        else
        {
            if (ContainerDef(item.ItemId) is { } data) EnsureContainerSeeded(item, data);
            _hudOpenBag = item;
        }
        FinishHudVerb(name);
        RefreshHudPanel();
        return true;
    }

    /// <summary>Move into the open bag: taken from where it is and put in.</summary>
    private bool PutInOpenBag(CharacterDefinition owner, InventoryItemInstance item)
    {
        if (_hudOpenBag is not { } bag || ReferenceEquals(item, bag)) return false;
        if (TakeFromCarrier(owner, item.Category, item.ItemId, Math.Max(1, item.Quantity)) is not { } taken) return false;
        bag.Contents.AddRange(taken);
        RefreshAfterMove();
        return true;
    }

    // ---------- Books ----------

    /// <summary>A page frame's words as plain text (they're kept as the editor's formatted text).</summary>
    private static string PlainText(string data)
    {
        if (data.Length == 0) return string.Empty;
        try
        {
            var box = new System.Windows.Controls.RichTextBox();
            RichTextXamlHelper.Load(box, data);
            return new System.Windows.Documents.TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Replace("\r\n", "\n").Trim();
        }
        catch { return Regex.Replace(data, "<[^>]+>", string.Empty).Trim(); }
    }

    /// <summary>A book read the Ultima 6 way: its words in the text area, a screenful at a time.</summary>
    private void U6ReadBook(BookData book)
    {
        var lines = new List<string>();
        var area = Hud.First(HudPieceKind.TextArea);
        for (int page = 0; page < book.Pages.Count; page++)
            foreach (var frame in book.Frames)
            {
                string xaml = _execContext.GetContentOverride(book.Id, page, frame.Id) ?? (book.Pages[page].FrameTexts.TryGetValue(frame.Id, out var t) ? t : string.Empty);
                foreach (var paragraph in PlainText(xaml).Split('\n'))
                    lines.AddRange(area is null ? new List<string> { paragraph } : HudWrap(paragraph, area.Width, area.FontName));
            }
        _hudHeard = new List<string>(); // the engine's "Opened ... - playing ..." stays out
        try { PlayOpenSound(book.OpenSoundResourceId, book.Name); } finally { _hudHeard = null; }
        if (lines.Count == 0) { U6Say(W("NothingWritten")); return; }
        int perScreen = area is null ? 8 : Math.Max(2, area.Height / Math.Max(1, HudLineHeight(area.FontName)) - 1);
        void Show(int from)
        {
            foreach (var line in lines.Skip(from).Take(perScreen)) U6Say(line);
            if (from + perScreen >= lines.Count) return;
            AskLetter(W("More"), " 0", key => { if (key == ' ') Show(from + perScreen); });
        }
        Show(0);
    }

    // ---------- Bodies ----------

    /// <summary>A body used: what it had spills out where it lies.</summary>
    private void U6SearchBody(MapCharacter body)
    {
        var who = body.Character;
        var things = who.Inventory.ToList();
        if (things.Count == 0) { U6Say(W("SearchNothing")); return; }
        var names = new List<string>();
        _hudHeard = new List<string>();
        try
        {
            foreach (var item in things)
            {
                RemoveFromCharacter(who, item);
                names.Add(item.Quantity > 1 ? $"{item.Quantity} {HudItemName(item)}" : HudItemName(item));
                PutDownAt(item, _player!, (body.Placement.X, body.Placement.Y));
            }
        }
        finally { _hudHeard = null; }
        U6Say(W("Searching", string.Join(", ", names)));
    }
}
