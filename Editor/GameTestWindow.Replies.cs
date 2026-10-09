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
public partial class GameTestWindow
{
    /// <summary>How many times each reply was picked (a reply's Cycle condition: Once, a number of times).</summary>
    private readonly Dictionary<(EventCommand ShowText, int ReplyId), int> _replyCounts = new();
    /// <summary>The reply Up/Down has moved to (-1 = none).</summary>
    private int _replyCursor = -1;

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
            if (_messageDrawing?.ReplyAt(e.GetPosition(MessageLayer)) is int index and >= 0) PickReply(index);
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

    /// <summary>Keys while a box is up: with replies, 1-9 pick one and Up/Down + Enter/Space choose; without, Enter, Space or Escape go on.</summary>
    private void MessageKey(Key key)
    {
        if (_currentMessage is not { HasReplies: true, Replies: { } replies })
        {
            if (key is Key.Enter or Key.Space or Key.Escape) AdvanceMessage();
            return;
        }
        int number = key is >= Key.D1 and <= Key.D9 ? key - Key.D1 : key is >= Key.NumPad1 and <= Key.NumPad9 ? key - Key.NumPad1 : -1;
        if (number >= 0) { PickReply(number); return; }
        switch (key)
        {
            case Key.Up or Key.Left:
                _replyCursor = _replyCursor <= 0 ? replies.Count - 1 : _replyCursor - 1;
                _messageDrawing?.Highlight(_replyCursor);
                break;
            case Key.Down or Key.Right or Key.Tab:
                _replyCursor = (_replyCursor + 1) % replies.Count;
                _messageDrawing?.Highlight(_replyCursor);
                break;
            case Key.Enter or Key.Space:
                if (_replyCursor >= 0) PickReply(_replyCursor);
                break;
        }
    }
}
