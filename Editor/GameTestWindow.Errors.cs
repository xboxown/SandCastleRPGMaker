using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The game's errors (System Settings > Errors): every error has a number
/// and words (the maker's, else the engine's); when the game has an error
/// paperdoll it opens with them - modal, with the paperdoll opening sound -
/// its text and captions showing %errnum and %errdescrp. Without one the
/// message shows where it happened, as before. Errors that come before the
/// game can draw anything (its data files gone) show the same number and
/// words in a plain box (App, MainWindow).
/// </summary>
public partial class GameTestWindow
{
    private OpenDollGump? _errorGump;

    /// <summary>An error happened: its paperdoll, if the game has one (then true); its words in the status bar either way. closed: when the player closes it (at once without a paperdoll).</summary>
    private bool GameError(int number, string detail = "", Action? closed = null)
    {
        string words = GameErrors.Describe(_systemSettings.Errors, number, detail);
        StatusText.Text = $"Error {number}: {words}";
        if (_objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.Errors.DollDesignId) is not { } design)
        {
            closed?.Invoke();
            return false;
        }
        if (_errorGump is { } showing && _openDolls.Contains(showing)) // one error at a time: the newest is shown
        {
            showing.Fill = (number, words, detail);
            RefreshDollGump(showing);
            closed?.Invoke();
            return true;
        }
        var gump = OpenForm(design, modal: true, answered: _ => { _errorGump = null; closed?.Invoke(); }, fresh: true);
        if (gump is null) { closed?.Invoke(); return false; }
        gump.Fill = (number, words, detail);
        RefreshDollGump(gump);
        _errorGump = gump;
        return true;
    }

    /// <summary>An error from an exception: the disk's own trouble (full, read-only, missing) by Windows' reason, else the number given; its detail is the exception's words.</summary>
    private bool GameError(Exception ex, int otherwise, Action? closed = null) =>
        GameError(GameErrors.Classify(ex, otherwise), ex.Message, closed);

    /// <summary>On the Save/Load screen: the error paperdoll, else the message by the button or slot as before.</summary>
    private void SaveScreenError(SaveScreen screen, int number, string detail, DollPart? near)
    {
        if (!GameError(number, detail)) SaveScreenSay(screen, GameErrors.Describe(_systemSettings.Errors, number, detail), near);
    }

    /// <summary>Show Error (event command): an error by number - the maker's own numbers too (System Settings > Errors) - and its detail. The event goes on once it's closed.</summary>
    private bool ShowErrorCommand(EventCommand command, Action<bool> done)
    {
        int number = (int)FormulaNumber(command, "Number");
        string detail = ScriptValues.Text(Formula(command, "Detail"));
        GameError(number, detail, () => done(true));
        return true;
    }

    /// <summary>Something unexpected went wrong in the game (a bug): error 900 instead of the game vanishing. False when it keeps happening (then the game can't go on).</summary>
    public bool UnexpectedError(Exception ex)
    {
        var now = DateTime.UtcNow;
        _unexpectedTimes.RemoveAll(t => (now - t).TotalSeconds > 2);
        _unexpectedTimes.Add(now);
        if (_unexpectedTimes.Count > 5) return false; // every frame: it can't go on
        GameError(GameErrors.Unexpected, ex.Message);
        return true;
    }

    private readonly List<DateTime> _unexpectedTimes = new();
}
