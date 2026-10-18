namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Cinema Mode: between an event's Cinema Mode and End Cinema Mode the
/// player has no controls - no walking (arrow keys), no clicks or
/// double-clicks on the world, no keys - only the dialog boxes can be read
/// and their replies picked. Each running command list that turned it on
/// holds it; it ends at its End Cinema Mode, or when the event ends
/// (its last line, Exit Event Processing). Should an event die halfway
/// (nothing of it left showing or going on), the controls come back
/// within a second - never locked for good.
/// </summary>
public partial class GameTestWindow
{
    private readonly HashSet<object> _cinemaRuns = new(ReferenceEqualityComparer.Instance);
    private double _cinemaIdleMs;

    private bool InCinema => _cinemaRuns.Count > 0;

    private void BeginCinema(object run)
    {
        _cinemaRuns.Add(run);
        _cinemaIdleMs = 0;
        // Let go of everything the player was doing: held walking keys, a press or drag on the way.
        _heldDirectionKeys.Clear();
        CancelPointer();
    }

    private void EndCinema(object run) => _cinemaRuns.Remove(run);

    /// <summary>Each frame: a cinema whose event has nothing left going (no box up, no command being carried out, no Wait) for a second is over.</summary>
    private void UpdateCinema(double elapsedMs)
    {
        if (!InCinema) return;
        bool going = IsShowingMessage || IsPlayingVideo || _scriptWaits.Count > 0 || _scriptActions.Values.Any(a => a.Done is not null) || _scriptEffects.Any(e => e.Done is not null);
        if (going) { _cinemaIdleMs = 0; return; }
        if ((_cinemaIdleMs += elapsedMs) < 1000) return;
        _cinemaRuns.Clear();
        _cinemaIdleMs = 0;
    }
}
