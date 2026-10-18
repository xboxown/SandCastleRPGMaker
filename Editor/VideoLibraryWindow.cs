using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > Video Library: the project's videos, for the Play Video command
/// (and the title screen's intro). Only .mp4 in H.264, H.265 (HEVC) or AV1 -
/// played by Windows' own decoder, so nothing about codecs is bundled with a
/// game. Saved into videos.sdbx with the project.
/// </summary>
public class VideoLibraryWindow : Window
{
    private readonly VideoDatabase _videos;
    private readonly ListBox _list = new() { Width = 300, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _name = new() { Width = 220 };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _hevcWarning = new()
    {
        TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed,
        Text = "H.265 (HEVC): it costs you nothing, but a player's PC plays it only with Microsoft's HEVC Video Extensions (about $1 in the Microsoft Store). Without it, the video doesn't show and the game goes on. H.264 plays everywhere."
    };
    private readonly MediaElement _preview = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, Stretch = Stretch.Uniform };

    public VideoLibraryWindow(VideoDatabase videos)
    {
        _videos = videos;
        Title = $"Video Library - {BuildInfo.CurrentBuild}";
        Width = 900;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var add = new Button { Content = "Add Video...", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        var remove = new Button { Content = "Remove", Padding = new Thickness(8, 2, 8, 2) };
        add.Click += (_, _) => AddVideo();
        remove.Click += (_, _) => RemoveVideo();
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        listButtons.Children.Add(add);
        listButtons.Children.Add(remove);
        var left = new DockPanel();
        DockPanel.SetDock(listButtons, Dock.Bottom);
        left.Children.Add(listButtons);
        left.Children.Add(_list);

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = "Name ", VerticalAlignment = VerticalAlignment.Center });
        nameRow.Children.Add(_name);
        _name.LostFocus += (_, _) => Rename();
        _name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Rename(); };

        var play = new Button { Content = "Play", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        var stop = new Button { Content = "Stop", Padding = new Thickness(10, 2, 10, 2) };
        play.Click += (_, _) => { if (Selected is { } v && System.IO.File.Exists(v.FilePath)) { _preview.Source = new Uri(v.FilePath); _preview.Play(); } };
        stop.Click += (_, _) => _preview.Stop();
        _preview.MediaFailed += (_, e) => _details.Text += $"\n\nThis PC can't play it: {e.ErrorException?.Message}";
        var playRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        playRow.Children.Add(play);
        playRow.Children.Add(stop);

        var right = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(nameRow);
        top.Children.Add(_details);
        top.Children.Add(_hevcWarning);
        top.Children.Add(playRow);
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        right.Children.Add(new Border { Background = Brushes.Black, Child = _preview });

        var root = new DockPanel { Margin = new Thickness(10) };
        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "Videos for the Play Video command: .mp4 in H.264 (plays on every PC - the safe choice), AV1 (small files, free) or H.265 (HEVC). They're saved inside the project (videos.sdbx) and go into a built game."
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        Content = root;

        _list.SelectionChanged += (_, _) => ShowSelected();
        Closed += (_, _) => _preview.Close();
        RefreshList();
    }

    private VideoEntry? Selected => _list.SelectedItem is ListBoxItem { Tag: VideoEntry v } ? v : null;

    private void RefreshList(VideoEntry? select = null)
    {
        _list.Items.Clear();
        foreach (var video in _videos.Videos)
        {
            var item = new ListBoxItem { Content = $"{video.Name}  ({video.Codec}, {Length(video.DurationSeconds)})", Tag = video };
            _list.Items.Add(item);
            if (video == select) _list.SelectedItem = item;
        }
        if (_list.SelectedItem is null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        ShowSelected();
    }

    private static string Length(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private void ShowSelected()
    {
        _preview.Stop();
        _preview.Source = null;
        if (Selected is not { } video)
        {
            _name.Text = string.Empty;
            _name.IsEnabled = false;
            _details.Text = "No video chosen. Add Video... brings one in.";
            _hevcWarning.Visibility = Visibility.Collapsed;
            return;
        }
        _name.IsEnabled = true;
        _name.Text = video.Name;
        _details.Text = $"{video.Codec}, {video.Width} x {video.Height}, {Length(video.DurationSeconds)} long." + (System.IO.File.Exists(video.FilePath) ? "" : "\nIts file is missing - add it again.");
        _hevcWarning.Visibility = video.Codec.StartsWith("H.265", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Rename()
    {
        if (Selected is not { } video) return;
        string name = _name.Text.Trim();
        if (name.Length == 0 || name == video.Name) { _name.Text = video.Name; return; }
        video.Name = name;
        RefreshList(video);
    }

    private void AddVideo()
    {
        var pick = new OpenFileDialog { Title = "Add a video", Filter = "MP4 video (*.mp4;*.m4v)|*.mp4;*.m4v" };
        if (pick.ShowDialog(this) != true) return;
        if (VideoProbe.Probe(pick.FileName, out string problem) is not { } info)
        {
            MessageBox.Show(this, problem, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (info.IsHevc && MessageBox.Show(this, "This video is H.265 (HEVC). It costs you nothing, but players need Microsoft's HEVC Video Extensions (about $1) to see it - without it the video is skipped. H.264 plays on every PC.\n\nAdd it anyway?",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var video = _videos.Add(System.IO.Path.GetFileNameWithoutExtension(pick.FileName), pick.FileName, info);
        RefreshList(video);
    }

    private void RemoveVideo()
    {
        if (Selected is not { } video) return;
        if (MessageBox.Show(this, $"Remove \"{video.Name}\" from the project? Play Video commands that use it will skip it.", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _preview.Stop();
        _preview.Source = null;
        _videos.Videos.Remove(video);
        RefreshList();
    }
}
