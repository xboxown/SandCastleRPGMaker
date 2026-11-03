using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The saving / loading progress paperdoll (System Settings > Save / Load): open while the game is written or read, its
/// parts following "the save / load progress" filling as each step is done (drawn at once, between the steps), kept at
/// least the time set so a quick save is still seen - full at the end - then closed.
/// </summary>
public partial class GameTestWindow
{
    private OpenDollGump? _diskGump;
    private double _diskProgress;
    private DateTime _diskOpened;

    private DollDesign? DiskDesign => _objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.SaveLoad.ProgressDollDesignId);

    private void BeginDiskProgress()
    {
        _diskProgress = 0;
        if (DiskDesign is not { } design) return;
        if (_diskGump is { } old && _openDolls.Contains(old)) CloseDollGump(old);
        _diskGump = OpenForm(design, modal: true, answered: null, fresh: true);
        _diskOpened = DateTime.UtcNow;
        ReportDiskProgress(0);
    }

    /// <summary>How far it is (0 to 1): the paperdoll drawn now, before the next step.</summary>
    private void ReportDiskProgress(double fraction)
    {
        _diskProgress = Math.Clamp(fraction, 0, 1);
        if (_diskGump is not { } gump || !_openDolls.Contains(gump)) return;
        RefreshDollGump(gump);
        Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { })); // drawn on screen before the work goes on
    }

    /// <summary>Done: full, kept the rest of its time, then closed - and <paramref name="then"/> (the "Game saved" message) after it.</summary>
    private void EndDiskProgress(Action? then = null)
    {
        _diskProgress = 1;
        if (_diskGump is null || DiskDesign is not { } design) { _diskGump = null; then?.Invoke(); return; }
        if (!_openDolls.Contains(_diskGump)) _diskGump = OpenForm(design, modal: true, answered: null, fresh: true); // closed by the load: back, full
        if (_diskGump is { } shown) RefreshDollGump(shown);
        double left = _systemSettings.SaveLoad.ProgressShownAtLeastMs - (DateTime.UtcNow - _diskOpened).TotalMilliseconds;
        var gump = _diskGump;
        void Finish()
        {
            if (gump is not null && _openDolls.Contains(gump)) CloseDollGump(gump);
            if (ReferenceEquals(_diskGump, gump)) _diskGump = null;
            then?.Invoke();
        }
        if (left <= 0) { Finish(); return; }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(left) };
        timer.Tick += (_, _) => { timer.Stop(); Finish(); };
        timer.Start();
    }

    /// <summary>A save or load that failed: the progress paperdoll closes at once.</summary>
    private void CancelDiskProgress()
    {
        if (_diskGump is { } gump && _openDolls.Contains(gump)) CloseDollGump(gump);
        _diskGump = null;
        _diskProgress = 0;
    }
}
