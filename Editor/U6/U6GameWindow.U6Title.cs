// Ultima 6 engine (U6GameWindow): Ultima 6's own title menu - Ultima 6 projects only (the Ultima 7 engine's paperdoll title and Save/Load screen are not used).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6's main menu (Nuvie's original menu): over the title picture (System Settings > Ultima 6 > Title), four choices -
/// Introduction (the intro video again), Create New Character (a new game), Acknowledgments (the maker's words), Journey Onward
/// (the saved game; greyed when there is none). Up / Down and Enter, or a click. Saving is Ultima 6's: one saved game, Ctrl+S
/// saves it and Ctrl+R restores it - there is no Save/Load screen. Wherever the Ultima 7 engine would open its Save/Load screen
/// (the "Open Save/Load Screen" command, a dead avatar double-clicked) this menu comes up instead.
/// </summary>
public partial class U6GameWindow
{
    private Canvas? _u6Title;
    private int _u6TitlePick;
    private bool _u6Acks;
    /// <summary>Brought up during play (not the game's start): Escape goes back to the game, if the avatar lives.</summary>
    private bool _u6TitleInPlay;
    private static readonly string[] U6TitleChoices = { "TitleIntro", "TitleCreate", "TitleAcks", "TitleJourney" };

    private bool HasSavedGame => ProjectSession.ProjectIdProvider?.Invoke() is { } projectId && GameSaveStore.List(projectId).Count > 0;
    private bool IntroVideo => _systemSettings.Title.IntroVideoId is int v && v >= 0 && ProjectSession.Videos.Videos.Any(x => x.Id == v);

    /// <summary>The game's start: the intro video, then the menu.</summary>
    private void ShowU6Title()
    {
        if (IsShippedGame) GameStatusBar.Visibility = Visibility.Collapsed;
        SilenceAmbient();
        if (IntroVideo) PlayVideoCommand(new EventCommand { Parameters = { ["VideoId"] = _systemSettings.Title.IntroVideoId.ToString(), ["Skippable"] = "True" } }, _ => OpenU6Title(inPlay: false));
        else OpenU6Title(inPlay: false);
    }

    private void OpenU6Title(bool inPlay)
    {
        _u6TitleInPlay = inPlay;
        _u6Acks = false;
        _u6TitlePick = HasSavedGame ? 3 : 1;
        SilenceAmbient();
        if (_systemSettings.Title.MusicId >= 0) SoundPlayback.TryPlayFor(TitleMusicOwner, _resources, _systemSettings.Title.MusicId, _systemSettings.Title.MusicLoops, null);
        if (_u6Title is null)
        {
            _u6Title = new Canvas { Width = ScreenWidth, Height = ScreenHeight, Background = Brushes.Black, ClipToBounds = true };
            Panel.SetZIndex(_u6Title, 100000);
            HudLayer.Children.Add(_u6Title);
        }
        DrawU6Title();
        StatusText.Text = "";
    }

    private void DrawU6Title()
    {
        if (_u6Title is not { } title) return;
        title.Children.Clear();
        var u6 = _systemSettings.Ultima6;
        if (SpriteOfLink(u6.TitleSprite) is { } picture) title.Children.Add(HudImage(picture, 0, 0, ScreenWidth, ScreenHeight, null, fit: true));
        int lineH = HudLineHeight("");
        if (_u6Acks)
        {
            var page = new HudPiece { X = 16, Y = 16, Width = (int)ScreenWidth - 32, Height = (int)ScreenHeight - 32 };
            var lines = u6.Acknowledgments.Replace("\r", "").Split('\n').SelectMany(l => HudWrap(l, page.Width, "")).ToList();
            title.Children.Add(HudWordsAt(page, string.Join("\n", lines)));
            return;
        }
        double width = U6TitleChoices.Max(c => HudTextWidth(W(c), "")) + 8;
        double left = (ScreenWidth - width) / 2, top = ScreenHeight - (U6TitleChoices.Length + 1) * (lineH + 2);
        for (int i = 0; i < U6TitleChoices.Length; i++)
        {
            double y = top + i * (lineH + 2);
            bool enabled = i switch { 0 => IntroVideo, 3 => HasSavedGame, _ => true };
            if (i == _u6TitlePick)
            {
                var mark = new Rectangle { Width = width, Height = lineH + 2, Fill = new SolidColorBrush(Color.FromArgb(160, 0x60, 0x30, 0x10)), Stroke = Brushes.Gold, StrokeThickness = 1 };
                Canvas.SetLeft(mark, left);
                Canvas.SetTop(mark, y - 1);
                title.Children.Add(mark);
            }
            var words = HudWords(W(U6TitleChoices[i]), null);
            words.Opacity = enabled ? 1 : 0.4;
            Canvas.SetLeft(words, left + (width - HudTextWidth(W(U6TitleChoices[i]), "")) / 2);
            Canvas.SetTop(words, y);
            title.Children.Add(words);
        }
    }

    private void CloseU6Title()
    {
        if (_u6Title is null) return;
        HudLayer.Children.Remove(_u6Title);
        _u6Title = null;
        SoundPlayback.StopFor(TitleMusicOwner);
        GameCanvas.Focus();
    }

    /// <summary>A key at the menu: Up / Down, Enter; Escape - the acknowledgments put away, or (brought up in play) back to the game.</summary>
    private bool U6TitleKey(KeyEventArgs e)
    {
        if (_u6Title is null) return false;
        e.Handled = true;
        if (e.IsRepeat && e.Key is not (Key.Up or Key.Down)) return true;
        if (_u6Acks) { _u6Acks = false; DrawU6Title(); return true; }
        switch (e.Key)
        {
            case Key.Up: _u6TitlePick = (_u6TitlePick + U6TitleChoices.Length - 1) % U6TitleChoices.Length; DrawU6Title(); break;
            case Key.Down: _u6TitlePick = (_u6TitlePick + 1) % U6TitleChoices.Length; DrawU6Title(); break;
            case Key.Enter or Key.Return or Key.Space: U6TitleChoose(_u6TitlePick); break;
            case Key.Escape when _u6TitleInPlay && !_gameOver: CloseU6Title(); break;
        }
        return true;
    }

    /// <summary>A click at the menu: the choice under it (the acknowledgments: put away).</summary>
    private void U6TitleClick(Point onScreen)
    {
        if (_u6Title is null) return;
        if (_u6Acks) { _u6Acks = false; DrawU6Title(); return; }
        int lineH = HudLineHeight("");
        double top = ScreenHeight - (U6TitleChoices.Length + 1) * (lineH + 2);
        int i = (int)Math.Floor((onScreen.Y - top + 1) / (lineH + 2));
        if (i < 0 || i >= U6TitleChoices.Length) return;
        _u6TitlePick = i;
        DrawU6Title();
        U6TitleChoose(i);
    }

    private void U6TitleChoose(int choice)
    {
        switch (choice)
        {
            case 0 when IntroVideo:
                SoundPlayback.StopFor(TitleMusicOwner);
                _u6Title!.Visibility = Visibility.Collapsed;
                PlayVideoCommand(new EventCommand { Parameters = { ["VideoId"] = _systemSettings.Title.IntroVideoId.ToString(), ["Skippable"] = "True" } }, _ => { if (_u6Title is { } t) { t.Visibility = Visibility.Visible; OpenU6Title(_u6TitleInPlay); } });
                break;
            case 1: // a new game: at the start, the game begins; in play, a fresh one
                if (_u6TitleInPlay) { RestartRequested = true; Close(); }
                else CloseU6Title();
                break;
            case 2:
                _u6Acks = true;
                DrawU6Title();
                break;
            case 3 when HasSavedGame:
            {
                var projectId = ProjectSession.ProjectIdProvider!.Invoke()!.Value;
                var newest = GameSaveStore.List(projectId).OrderByDescending(s => s.SavedAtUtc).First();
                CloseU6Title();
                BeginDiskProgress();
                try { LoadGame(GameSaveStore.Read(projectId, newest, ReportDiskProgress), ""); EndDiskProgress(); }
                catch (Exception ex) { CancelDiskProgress(); GameError(ex, 1000); }
                break;
            }
        }
    }
}
