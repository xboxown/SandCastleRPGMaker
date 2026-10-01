using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Piece 1 of the Sound Editor feature: the properties window. Playback
/// preview uses NAudio's AudioFileReader, which covers WAV/MP3 and whatever
/// else Windows Media Foundation understands - it does NOT cover Amiga
/// IFF/8SVX or (usually) OGG. That's not a bug here: dedicated readers for
/// those formats are part of the format-conversion piece, not this one.
/// Loading a file AudioFileReader can't open just disables playback/duration
/// for it gracefully instead of crashing.
/// </summary>
public partial class SoundEditorWindow : Window
{
    private readonly ResourceEntry _entry;
    private readonly ResourceDatabase _resourceDatabase;
    private bool _suppress;

    private WaveOutEvent? _waveOut;
    private AudioFileReader? _reader;
    private readonly DispatcherTimer _positionTimer;
    private bool _isDraggingPosition;
    private bool _playbackSupported;

    public SoundEditorWindow(ResourceEntry entry, ResourceDatabase resourceDatabase)
    {
        InitializeComponent();
        _entry = entry;
        _resourceDatabase = resourceDatabase;

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _positionTimer.Tick += PositionTimer_Tick;

        PopulateCombos();
        LoadFromModel();
        TryLoadAudio();

        Closing += (_, _) => DisposePlayback();
    }

    private void PopulateCombos()
    {
        ConversionCombo.ItemsSource = new[] { "Automatic" };
        ConversionCombo.SelectedIndex = 0;
        ConversionCombo.IsEnabled = false; // "for OGG only" - real conversion options land with the format-conversion piece

        ChannelFormatCombo.ItemsSource = Enum.GetNames<SoundChannelFormat>();
        SampleRateCombo.ItemsSource = new[] { "8000", "11025", "22050", "32000", "44100", "48000" };
        BitDepthCombo.ItemsSource = new[] { "8", "16", "24" };
        CompressionQualityCombo.ItemsSource = Enumerable.Range(0, 10).Select(n => n.ToString()).ToList();

        TargetFormatCombo.ItemsSource = AudioConversionEngine.TargetFormats;
        TargetFormatCombo.SelectedIndex = 0;
    }

    private void LoadFromModel()
    {
        _suppress = true;
        TitleText.Text = $"Sound: {_entry.Name}";
        NameBox.Text = _entry.Name;
        VolumeSlider.Value = Math.Clamp(_entry.Volume, 0, 1);
        VolumeBox.Text = _entry.Volume.ToString("0.00");

        switch (_entry.AttributeMode)
        {
            case SoundAttributeMode.UncompressedNotStreamed: AttrUncompressedNotStreamed.IsChecked = true; break;
            case SoundAttributeMode.CompressedNotStreamed: AttrCompressedNotStreamed.IsChecked = true; break;
            case SoundAttributeMode.UncompressOnLoadNotStreamed: AttrUncompressOnLoad.IsChecked = true; break;
            case SoundAttributeMode.CompressedStreamed: AttrCompressedStreamed.IsChecked = true; break;
        }

        ChannelFormatCombo.SelectedItem = _entry.ChannelFormat.ToString();
        SampleRateCombo.SelectedItem = _entry.SampleRate.ToString();
        BitDepthCombo.SelectedItem = _entry.BitDepth.ToString();
        CompressionQualityCombo.SelectedItem = _entry.CompressionQuality.ToString();

        RebuildAudioGroupCombo();
        ExportDirectoryBox.Text = _entry.ExportDirectory;
        _suppress = false;
    }

    // ---------- Playback ----------

    private void TryLoadAudio()
    {
        DisposePlayback();
        _playbackSupported = false;

        if (!File.Exists(_entry.FilePath))
        {
            DurationText.Text = "File not found";
            return;
        }

        string playbackPath = _entry.FilePath;
        string ext = System.IO.Path.GetExtension(_entry.FilePath).ToLowerInvariant();

        try
        {
            if (ext is ".iff" or ".8svx")
            {
                // AudioFileReader has no idea how to open Amiga IFF/8SVX - convert
                // to a throwaway temp WAV purely so this window can preview it the
                // same way as everything else. The sound's own stored FilePath is
                // never touched by this - it's playback-only, not a real edit.
                playbackPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sandcastle_preview_{Guid.NewGuid():N}.wav");
                AudioConversionEngine.ConvertAndExport(_entry.FilePath, playbackPath, "WAV", 44100, 16, SoundChannelFormat.Mono);
            }

            _reader = new AudioFileReader(playbackPath) { Volume = (float)_entry.Volume };
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_reader);
            _waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
            _playbackSupported = true;
            PositionSlider.Maximum = Math.Max(0.01, _reader.TotalTime.TotalSeconds);
            PositionSlider.Value = 0;
            UpdateDurationText();
        }
        catch (Exception)
        {
            // Formats neither AudioFileReader nor AmigaIffCodec can open (OGG
            // without a Vorbis reader, FLAC, etc.) - not a crash, just no
            // preview yet for this specific format.
            DurationText.Text = "Playback preview not supported for this format yet";
        }
    }

    private void DisposePlayback()
    {
        _positionTimer.Stop();
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _reader?.Dispose();
        _reader = null;
    }

    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (_reader is null || _isDraggingPosition) return;
        _suppress = true;
        PositionSlider.Value = _reader.CurrentTime.TotalSeconds;
        _suppress = false;
        UpdateDurationText();
    }

    private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_reader is null || _waveOut is null) return;
            bool reachedEnd = _reader.Position >= _reader.Length - 1;

            if (reachedEnd && LoopToggle.IsChecked == true)
            {
                _reader.Position = 0;
                _waveOut.Play();
                return;
            }

            PlayButton.Content = "\u25B6";
            _positionTimer.Stop();
            if (reachedEnd)
            {
                _reader.Position = 0;
                _suppress = true;
                PositionSlider.Value = 0;
                _suppress = false;
                UpdateDurationText();
            }
        });
    }

    private void UpdateDurationText()
    {
        DurationText.Text = _reader is null
            ? "0.00 / 0.00s"
            : $"{_reader.CurrentTime.TotalSeconds:0.00} / {_reader.TotalTime.TotalSeconds:0.00}s";
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_playbackSupported || _waveOut is null)
        {
            MessageBox.Show("Playback preview isn't available for this file's format yet.", "Sound Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_waveOut.PlaybackState == PlaybackState.Playing)
        {
            _waveOut.Pause();
            PlayButton.Content = "\u25B6";
            _positionTimer.Stop();
        }
        else
        {
            _waveOut.Play();
            PlayButton.Content = "\u23F8";
            _positionTimer.Start();
        }
    }

    private void RewindButton_Click(object sender, RoutedEventArgs e)
    {
        if (_reader is not null) _reader.CurrentTime = TimeSpan.Zero;
        _suppress = true;
        PositionSlider.Value = 0;
        _suppress = false;
        UpdateDurationText();
    }

    private void OpenMixerButton_Click(object sender, RoutedEventArgs e)
    {
        var mixer = new SoundMixerWindow(_resourceDatabase, _entry) { Owner = this };
        mixer.Show(); // non-modal - the mixer is meant to stay open and playable while you keep working in the Sound Editor
    }

    private void PositionSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _isDraggingPosition = true;

    private void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPosition = false;
        if (_reader is not null) _reader.CurrentTime = TimeSpan.FromSeconds(PositionSlider.Value);
        UpdateDurationText();
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _reader is null) return;
        // Live preview of where a drag WOULD seek to - the actual seek only commits on mouse-up (PositionSlider_PreviewMouseUp), matching how a scrubber is expected to feel.
        DurationText.Text = $"{PositionSlider.Value:0.00} / {_reader.TotalTime.TotalSeconds:0.00}s";
    }

    // ---------- Volume ----------

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        _entry.Volume = VolumeSlider.Value;
        if (_reader is not null) _reader.Volume = (float)VolumeSlider.Value;
        _suppress = true;
        VolumeBox.Text = VolumeSlider.Value.ToString("0.00");
        _suppress = false;
    }

    private void VolumeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (!double.TryParse(VolumeBox.Text, out double v)) return;
        v = Math.Clamp(v, 0, 1);
        _entry.Volume = v;
        if (_reader is not null) _reader.Volume = (float)v;
        _suppress = true;
        VolumeSlider.Value = v;
        _suppress = false;
    }

    // ---------- Name / source file ----------

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        _entry.Name = NameBox.Text;
        TitleText.Text = $"Sound: {NameBox.Text}";
    }

    private void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Audio files|*.wav;*.mp3;*.ogg;*.iff;*.8svx|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        _entry.FilePath = dialog.FileName;
        TryLoadAudio();
    }

    // ---------- Attributes / Target Options ----------

    private void AttributeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        if (AttrUncompressedNotStreamed.IsChecked == true) _entry.AttributeMode = SoundAttributeMode.UncompressedNotStreamed;
        else if (AttrCompressedNotStreamed.IsChecked == true) _entry.AttributeMode = SoundAttributeMode.CompressedNotStreamed;
        else if (AttrUncompressOnLoad.IsChecked == true) _entry.AttributeMode = SoundAttributeMode.UncompressOnLoadNotStreamed;
        else if (AttrCompressedStreamed.IsChecked == true) _entry.AttributeMode = SoundAttributeMode.CompressedStreamed;
    }

    private void ChannelFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (ChannelFormatCombo.SelectedItem is string s && Enum.TryParse<SoundChannelFormat>(s, out var v)) _entry.ChannelFormat = v;
    }

    private void SampleRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (SampleRateCombo.SelectedItem is string s && int.TryParse(s, out int v)) _entry.SampleRate = v;
    }

    private void BitDepthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (BitDepthCombo.SelectedItem is string s && int.TryParse(s, out int v)) _entry.BitDepth = v;
    }

    private void CompressionQualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (CompressionQualityCombo.SelectedItem is string s && int.TryParse(s, out int v)) _entry.CompressionQuality = v;
    }

    // ---------- Convert / Export ----------

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_entry.FilePath))
        {
            MessageBox.Show("No source file to convert.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string targetFormat = (string)TargetFormatCombo.SelectedItem;
        bool isAmiga = targetFormat == "Amiga IFF (8SVX)";
        string extension = isAmiga ? ".iff" : ".wav";
        string suggestedName = _entry.Name + extension;

        string outputPath;
        if (!string.IsNullOrWhiteSpace(_entry.ExportDirectory) && Directory.Exists(_entry.ExportDirectory))
        {
            outputPath = System.IO.Path.Combine(_entry.ExportDirectory, suggestedName);
        }
        else
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedName,
                Filter = isAmiga ? "Amiga IFF (*.iff)|*.iff" : "WAV audio (*.wav)|*.wav"
            };
            if (dialog.ShowDialog() != true) return;
            outputPath = dialog.FileName;
        }

        try
        {
            AudioConversionEngine.ConvertAndExport(_entry.FilePath, outputPath, targetFormat, _entry.SampleRate, _entry.BitDepth, _entry.ChannelFormat);
            MessageBox.Show($"Exported to:\n{outputPath}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not convert/export: {ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- Audio Group / Export Directory ----------

    private void RebuildAudioGroupCombo()
    {
        var items = new List<string> { "(None)" };
        items.AddRange(_resourceDatabase.AudioGroups.Select(g => g.Name));
        AudioGroupCombo.ItemsSource = items;

        var current = _resourceDatabase.AudioGroups.FirstOrDefault(g => g.Id == _entry.AudioGroupId);
        AudioGroupCombo.SelectedItem = current?.Name ?? "(None)";
    }

    private void AudioGroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        string? selected = AudioGroupCombo.SelectedItem as string;
        var group = _resourceDatabase.AudioGroups.FirstOrDefault(g => g.Name == selected);
        _entry.AudioGroupId = group?.Id ?? -1;
    }

    private void AddAudioGroupButton_Click(object sender, RoutedEventArgs e)
    {
        string? name = SimpleInputWindow.Show(this, "Audio Group name:", "New Audio Group", "New Audio Group");
        if (string.IsNullOrWhiteSpace(name)) return;

        var group = _resourceDatabase.CreateAudioGroup();
        group.Name = name;
        _entry.AudioGroupId = group.Id;
        RebuildAudioGroupCombo();
    }

    private void ExportDirectoryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        _entry.ExportDirectory = ExportDirectoryBox.Text;
    }

    private void BrowseExportDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose an export directory" };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ExportDirectoryBox.Text = dialog.SelectedPath;
    }
}
