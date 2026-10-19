using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Title Screen (System Settings > Title Screen): a PaperDoll Editor
/// paperdoll filling the game's view before play - after an optional intro
/// video, with its own music. Its New Game button (or closing it) starts the
/// game; Continue loads the newest save; Load Game opens the Save/Load screen
/// to load (no Save there); Quit quits, asking with the Quit prompt first.
/// Any other button runs its own events. Return To Title (event command)
/// comes back here with a fresh game. Ctrl+Q asks to quit at any time.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Starts at the Title Screen (a built game; Game Test when asked).</summary>
    public bool StartAtTitle { get; init; }
    /// <summary>A New Game button pressed during play: a fresh game is started in a new window.</summary>
    public bool RestartRequested { get; private set; }

    private OpenDollGump? _titleGump;
    private bool InTitle => _titleGump is not null;
    private static readonly object TitleMusicOwner = new();

    /// <summary>On loading: the intro video, then the title - if the game has one.</summary>
    private void ShowTitleIfAny()
    {
        if (!StartAtTitle || _objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.Title.DollDesignId) is not { } design) return;
        ViewportBorder.Opacity = 0; // the world isn't seen behind the title
        int video = _systemSettings.Title.IntroVideoId;
        if (video >= 0 && ProjectSession.Videos.Videos.Any(v => v.Id == video))
            PlayVideoCommand(new EventCommand { Parameters = { ["VideoId"] = video.ToString(), ["Skippable"] = "True" } }, _ => OpenTitle(design));
        else OpenTitle(design);
    }

    private void OpenTitle(DollDesign design)
    {
        if (_systemSettings.Title.MusicId >= 0) SoundPlayback.TryPlayFor(TitleMusicOwner, _resources, _systemSettings.Title.MusicId, _systemSettings.Title.MusicLoops, null);
        if (OpenForm(design, modal: true, answered: null, fresh: true) is not { } gump) { ViewportBorder.Opacity = 1; return; }
        _titleGump = gump;
        // Continue: greyed when there's nothing to continue.
        bool anySave = ProjectSession.ProjectIdProvider?.Invoke() is { } projectId && GameSaveStore.List(projectId).Count > 0;
        foreach (var part in design.Parts.Where(p => p.Kind == DollPartKind.ContinueButton)) gump.State.Of(part).Disabled = !anySave;
        PlaceTitle();
        StatusText.Text = "Title Screen.";
    }

    /// <summary>The title fills the game's view (its picture scaled to fit, centred), whatever the window's size.</summary>
    private void PlaceTitle()
    {
        if (_titleGump is not { } gump) return;
        var design = gump.Design;
        double scale = ViewportScale.ScaleX * Math.Min(ViewportWidth / Math.Max(1, design.Width), ViewportHeight / Math.Max(1, design.Height));
        gump.Scale = scale;
        var at = ViewportOffset();
        System.Windows.Controls.Canvas.SetLeft(gump.GumpBorder, at.X + (ViewportWidth * ViewportScale.ScaleX - design.Width * scale) / 2);
        System.Windows.Controls.Canvas.SetTop(gump.GumpBorder, at.Y + (ViewportHeight * ViewportScale.ScaleY - design.Height * scale) / 2);
        RefreshDollGump(gump);
    }

    /// <summary>The title closing - its New Game, or a Close: the game begins.</summary>
    private void TitleClosed(OpenDollGump gump)
    {
        if (!ReferenceEquals(gump, _titleGump)) return;
        _titleGump = null;
        SoundPlayback.StopFor(TitleMusicOwner);
        ViewportBorder.Opacity = 1;
        StatusText.Text = "";
        GameCanvas.Focus();
    }

    /// <summary>A New Game / Continue / Load Game / Quit button pressed (on the title or any paperdoll).</summary>
    private void TitleButton(OpenDollGump gump, DollPartKind kind)
    {
        switch (kind)
        {
            case DollPartKind.NewGameButton:
                if (ReferenceEquals(gump, _titleGump)) CloseDollGump(gump);
                else { RestartRequested = true; Close(); }
                break;
            case DollPartKind.ContinueButton:
            {
                if (ProjectSession.ProjectIdProvider?.Invoke() is not { } projectId || GameSaveStore.List(projectId).OrderByDescending(s => s.SavedAtUtc).FirstOrDefault() is not { } newest)
                {
                    StatusText.Text = "There's no saved game to continue.";
                    return;
                }
                try { LoadGame(GameSaveStore.Read(projectId, newest), $"Loaded \"{newest.Name}\"."); }
                catch (Exception ex) { StatusText.Text = $"\"{newest.Name}\" couldn't be loaded: {ex.Message}"; }
                break;
            }
            case DollPartKind.LoadGameButton:
                OpenSaveLoad();
                if (_saveScreen is { } screen && gump.ModalLayer > 0)
                {
                    screen.Gump.ModalLayer = gump.ModalLayer + 2; // over the title (or the paperdoll it was opened from)
                    BringDollToFront(screen.Gump);
                }
                break;
            case DollPartKind.QuitButton:
                AskToQuit();
                break;
        }
    }

    /// <summary>Ctrl+Q or a Quit button: the Quit prompt (System Settings > Save / Load) asks first; Yes quits.</summary>
    private void AskToQuit()
    {
        if (_objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.Quit.DollDesignId) is { } prompt)
        {
            if (OpenForm(prompt, modal: true, answered: yes => { if (yes) QuitNow(); }, fresh: true) is { } gump && _titleGump is { } title && gump.ModalLayer <= title.ModalLayer)
            {
                gump.ModalLayer = title.ModalLayer + 2;
                BringDollToFront(gump);
            }
            return;
        }
        QuitNow();
    }

    private void QuitNow()
    {
        if (IsShippedGame) Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
        else Dispatcher.BeginInvoke(new Action(Close));
    }
}
