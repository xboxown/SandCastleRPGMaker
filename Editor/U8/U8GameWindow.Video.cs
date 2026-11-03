// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Play Video: a Video Library video over the whole game screen, black
/// bars keeping its shape, the game paused while it plays (as Exult halts
/// the game for its FLIC cutscenes). A click, Escape, Space or Enter skips
/// it when the command allows. The commands after it wait till it ends,
/// is skipped, or can't be played on this PC (an H.265 video without
/// Microsoft's HEVC extension) - then it's cancelled and they go on.
/// Windows' own decoder plays it: nothing about codecs ships with a game.
/// </summary>
public partial class U8GameWindow
{
    private sealed class PlayingVideo
    {
        public required Grid Overlay { get; init; }
        public required MediaElement Player { get; init; }
        public required bool Skippable { get; init; }
        public required Action<bool> Done { get; init; }
    }

    private PlayingVideo? _video;

    private bool IsPlayingVideo => _video is not null;

    private bool PlayVideoCommand(EventCommand command, Action<bool> done)
    {
        if (_video is not null) return Cancel("A video is already playing.");
        if (!int.TryParse(command.Get("VideoId"), out int id) || ProjectSession.Videos.Videos.FirstOrDefault(v => v.Id == id) is not { } video)
            return Cancel("Play Video: that video isn't in the Video Library any more.");
        if (!System.IO.File.Exists(video.FilePath)) return Cancel($"Play Video: the file of \"{video.Name}\" is missing.");

        var player = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            Stretch = Stretch.Uniform,
            Volume = double.TryParse(command.Get("Volume"), out double volume) ? Math.Clamp(volume, 0, 100) / 100 : 1,
            Source = new Uri(video.FilePath)
        };
        var overlay = new Grid { Background = Brushes.Black, Cursor = Cursors.None };
        overlay.Children.Add(player);
        Panel.SetZIndex(overlay, int.MaxValue);
        _video = new PlayingVideo { Overlay = overlay, Player = player, Skippable = command.Get("Skippable") != "False", Done = done };

        player.MediaEnded += (_, _) => EndVideo(true);
        player.MediaFailed += (_, e) =>
        {
            EndVideo(false);
            StatusText.Text = $"\"{video.Name}\" can't be played on this PC ({video.Codec}){(video.Codec.StartsWith("H.265", StringComparison.Ordinal) ? " - it needs Microsoft's HEVC Video Extensions" : "")}: {e.ErrorException?.Message}";
        };
        overlay.MouseDown += (_, e) =>
        {
            e.Handled = true; // nothing under it in the game is clicked
            if (e.ChangedButton == MouseButton.Left) SkipVideo();
        };
        ViewportHost.Children.Add(overlay);
        _heldDirectionKeys.Clear();
        player.Play();
        return true;
    }

    /// <summary>A key while a video plays: Cancel or Accept skips it (if allowed); no other key does anything.</summary>
    private bool VideoKey(Key key)
    {
        if (_video is null) return false;
        if (KeyIs(key, "Cancel") || KeyIs(key, "Accept") || KeyIs(key, "Accept2")) SkipVideo();
        return true;
    }

    private void SkipVideo()
    {
        if (_video is { Skippable: true }) EndVideo(true);
    }

    private void EndVideo(bool ok)
    {
        if (_video is not { } video) return;
        _video = null; // first: what runs next may start another video
        video.Player.Stop();
        video.Player.Close();
        ViewportHost.Children.Remove(video.Overlay);
        GameCanvas.Focus();
        video.Done(ok);
    }

    /// <summary>The game closing mid-video: the video ends with it (its sound would otherwise play on).</summary>
    private void StopVideo()
    {
        if (_video is not { } video) return;
        _video = null;
        video.Player.Stop();
        video.Player.Close();
    }
}
