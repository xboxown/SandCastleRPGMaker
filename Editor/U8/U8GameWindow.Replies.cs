// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Conversations: a Show Text with replies puts its box up with the replies
/// on offer (those whose conditions pass right now) and everything waits -
/// the game stays paused, as in an Exult conversation - until the player
/// picks one: click it, or press its number, or Up/Down and Enter. Then the
/// commands go on in that reply's branch, as whoever was running them
/// (Self is still the character being talked to). A click or Enter away
/// from the replies does nothing: the conversation only ends when the
/// author's commands end it.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>How many times each reply was picked (a reply's Cycle condition: Once, a number of times).</summary>
    private readonly Dictionary<(EventCommand ShowText, int ReplyId), int> _replyCounts = new();
    /// <summary>The reply Up/Down has moved to (-1 = none).</summary>
    private int _replyCursor = -1;
    /// <summary>The page of replies showing (More / Previous).</summary>
    private int _replyPage;

    /// <summary>A reply spot on the box picked: More / Previous turn the page, a reply is answered.</summary>
    private void PickReplySlot(int slot)
    {
        if (_messageDrawing is not { } drawing || slot < 0 || slot >= drawing.ReplySlots.Count) return;
        switch (drawing.ReplySlots[slot])
        {
            case DialogBoxDrawing.More:
                _replyPage++;
                _replyCursor = -1;
                DrawCurrentMessage();
                break;
            case DialogBoxDrawing.Previous:
                _replyPage--;
                _replyCursor = -1;
                DrawCurrentMessage();
                break;
            case int reply:
                PickReply(reply);
                break;
        }
    }

    private bool AskChoice(EventCommand showText, List<DialogChoice> choices, Action<int> chosen)
    {
        var offered = choices.Where(c => ReplyOffered(showText, c)).ToList();
        if (offered.Count == 0)
        {
            QueueMessage(showText); // nothing to answer: just the box
            return false;
        }
        // Whoever is running these commands now runs the reply's branch too.
        var (activator, byStepping, selfBookId) = (_activator, _activatedByCharacterStep, _execContext.SelfBookId);
        void Picked(int replyId)
        {
            _replyCounts[(showText, replyId)] = _replyCounts.TryGetValue((showText, replyId), out int n) ? n + 1 : 1;
            var (previous, previousStep, previousSelf) = (_activator, _activatedByCharacterStep, _execContext.SelfBookId);
            (_activator, _activatedByCharacterStep, _execContext.SelfBookId) = (activator, byStepping, selfBookId);
            try { chosen(replyId); }
            finally { (_activator, _activatedByCharacterStep, _execContext.SelfBookId) = (previous, previousStep, previousSelf); }
        }
        if (BuildMessage(showText, offered, Picked) is not { } message) return false; // the speaker isn't in sight: no conversation
        _messages.Enqueue(message);
        if (_currentMessage is null) ShowNextMessage();
        return true;
    }

    /// <summary>Stop Conversation: with a box up, the commands after it wait (as whoever is running them) until it and any queued after it are read.</summary>
    private bool WaitUntilRead(Action goOn)
    {
        if (!IsShowingMessage) return false;
        var (activator, byStepping, selfBookId) = (_activator, _activatedByCharacterStep, _execContext.SelfBookId);
        void GoOn()
        {
            var (previous, previousStep, previousSelf) = (_activator, _activatedByCharacterStep, _execContext.SelfBookId);
            (_activator, _activatedByCharacterStep, _execContext.SelfBookId) = (activator, byStepping, selfBookId);
            try { goOn(); }
            finally { (_activator, _activatedByCharacterStep, _execContext.SelfBookId) = (previous, previousStep, previousSelf); }
        }
        _messages.Enqueue(new GameMessage("", null, "", false) { AfterRead = GoOn });
        return true;
    }

    /// <summary>Whether this reply is on offer right now: its condition, checked live.</summary>
    private bool ReplyOffered(EventCommand showText, DialogChoice reply)
    {
        if (reply.Condition is not { } condition) return true;
        int picked = _replyCounts.TryGetValue((showText, reply.Id), out int n) ? n : 0;
        var context = BuildConditionContext(BookTriggerConditionBased.Always, BookTriggerConditionBased.Always, _execContext.SelfBookId, picked);
        if (condition.TriggerConditionBased is BookTriggerConditionBased.ByHavingAnItem or BookTriggerConditionBased.ByNotHavingAnItem)
        {
            bool has = PartyHas(condition.TriggerItemReference);
            context.TriggerActuallyOccurred = condition.TriggerConditionBased == BookTriggerConditionBased.ByHavingAnItem ? has : !has;
        }
        return BookEventConditionEvaluator.Evaluate(condition, context, new List<string>());
    }

    /// <summary>Anyone in the party carries it - equipped, in the backpack, or in a bag inside.</summary>
    private bool PartyHas(ItemReference? item)
    {
        if (item is null || item.ItemId < 0) return false;
        bool Search(IEnumerable<InventoryItemInstance> items) =>
            items.Any(i => (i.Category == item.Category && i.ItemId == item.ItemId) || Search(i.Contents));
        return GetPartyList().Any(member => Search(member.Inventory));
    }

    private void PickReply(int index)
    {
        if (_currentMessage is not { Replies: { } replies, OnChosen: { } onChosen } || index < 0 || index >= replies.Count) return;
        int replyId = replies[index].Id;
        // The box goes; the reply's branch may put up the next one.
        _messageDrawing?.Stop();
        _messageDrawing = null;
        MessageLayer.Children.Clear();
        _currentMessage = null;
        onChosen(replyId);
        if (_currentMessage is null) ShowNextMessage();
    }

    /// <summary>A click while a box is up: on a reply picks it; with no replies, goes on.</summary>
    private void MessageClicked(MouseButtonEventArgs e)
    {
        if (_currentMessage is { HasReplies: true })
        {
            if (_messageDrawing?.ReplyAt(e.GetPosition(MessageLayer)) is int index and >= 0) PickReplySlot(index);
            return;
        }
        AdvanceMessage();
    }

    /// <summary>The mouse over the replies lights the one under it.</summary>
    private void MessageMouseMoved(MouseEventArgs e)
    {
        if (_currentMessage is not { HasReplies: true } || _messageDrawing is null) return;
        int index = _messageDrawing.ReplyAt(e.GetPosition(MessageLayer));
        if (index >= 0) _replyCursor = index;
        _messageDrawing.Highlight(index >= 0 ? index : _replyCursor);
    }

    /// <summary>Keys while a box is up: with replies, the number keys pick one and Previous / Next + Accept choose; without, Accept or Cancel go on.</summary>
    private void MessageKey(Key key)
    {
        if (_currentMessage is not { HasReplies: true } || _messageDrawing is not { ReplySlots.Count: > 0 } drawing)
        {
            if (KeyIs(key, "Accept") || KeyIs(key, "Accept2") || KeyIs(key, "Cancel")) AdvanceMessage();
            return;
        }
        // The spots on the box now (this page's replies, More / Previous), in reading order.
        int count = drawing.ReplySlots.Count;
        int number = NumberOf(key);
        if (number >= 0)
        {
            // 1-9: this page's replies, counted without More / Previous.
            var replySpots = Enumerable.Range(0, count).Where(i => drawing.ReplySlots[i] >= 0).ToList();
            if (number < replySpots.Count) PickReplySlot(replySpots[number]);
            return;
        }
        if (KeyIs(key, "ScreenUp") || KeyIs(key, "ScreenUp2"))
        {
            _replyCursor = _replyCursor <= 0 ? count - 1 : _replyCursor - 1;
            drawing.Highlight(_replyCursor);
        }
        else if (KeyIs(key, "ScreenDown") || KeyIs(key, "ScreenDown2") || KeyIs(key, "ScreenDown3"))
        {
            _replyCursor = (_replyCursor + 1) % count;
            drawing.Highlight(_replyCursor);
        }
        else if (KeyIs(key, "PageNext"))
        {
            if (drawing.ReplyPage < drawing.ReplyPageCount - 1) PickReplySlot(drawing.ReplySlots.IndexOf(DialogBoxDrawing.More));
        }
        else if (KeyIs(key, "PagePrev"))
        {
            if (drawing.ReplyPage > 0) PickReplySlot(drawing.ReplySlots.IndexOf(DialogBoxDrawing.Previous));
        }
        else if (KeyIs(key, "Accept") || KeyIs(key, "Accept2"))
        {
            if (_replyCursor >= 0) PickReplySlot(_replyCursor);
        }
    }
}
