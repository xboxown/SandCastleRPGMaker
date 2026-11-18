using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > Convert Audio (Ultima 6 Apple IIgs mode, in the Sound Mixer's place): pick sound files, Convert, and each becomes
/// a sound the Apple IIgs can play - SND: 8-bit, mono, 22050 a second or less, at most 60 KB (the Ensoniq DOC holds 64 KB) -
/// in the Sound Editor at once as snd1, snd2, snd3... Rename them there; play them there to hear them.
/// </summary>
internal sealed class ConvertAudioWindow : Window
{
    private readonly ResourceDatabase _resources;
    private readonly ListBox _files = new() { Height = 220, SelectionMode = SelectionMode.Extended };
    private readonly ComboBox _rate = new() { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _kind = new() { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>Where converted sounds wait until the project is saved (Save puts every sound's file in audios.sdbx).</summary>
    private static string Folder => Path.Combine(Path.GetTempPath(), "SandcastleRpgMaker", "iigs-sounds");

    public ConvertAudioWindow(ResourceDatabase resources)
    {
        _resources = resources;
        Title = "Convert Audio (Apple IIgs)";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var rows = new StackPanel { Margin = new Thickness(14) };
        rows.Children.Add(new TextBlock
        {
            Text = "The Apple IIgs plays SND sounds: 8-bit, mono, 22 kHz or lower - small, so they don't eat its memory or its processor. Add the sound files to convert (WAV, MP3, OGG, FLAC, AIFF, IFF), click Convert: they go into the Sound Editor as snd1, snd2, snd3... ready to use. Rename them in the Sound Editor.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        rows.Children.Add(_files);
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var add = new Button { Content = "Add Files...", Width = 100, Margin = new Thickness(0, 0, 6, 0) };
        var remove = new Button { Content = "Remove", Width = 80, Margin = new Thickness(0, 0, 6, 0) };
        var clear = new Button { Content = "Clear", Width = 80 };
        add.Click += (_, _) =>
        {
            var pick = new OpenFileDialog { Multiselect = true, Filter = "Sound files (*.wav;*.mp3;*.ogg;*.flac;*.aif;*.aiff;*.iff;*.8svx)|*.wav;*.mp3;*.ogg;*.flac;*.aif;*.aiff;*.iff;*.8svx|All files (*.*)|*.*" };
            if (pick.ShowDialog(this) != true) return;
            foreach (string f in pick.FileNames) if (!_files.Items.Contains(f)) _files.Items.Add(f);
        };
        remove.Click += (_, _) => { foreach (var f in _files.SelectedItems.Cast<object>().ToList()) _files.Items.Remove(f); };
        clear.Click += (_, _) => _files.Items.Clear();
        listButtons.Children.Add(add);
        listButtons.Children.Add(remove);
        listButtons.Children.Add(clear);
        rows.Children.Add(listButtons);
        rows.Children.Add(new TextBlock { Text = "Quality:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
        foreach (int r in AppleIIgsLimits.SampleRates)
            _rate.Items.Add(new ComboBoxItem { Content = $"{r} Hz" + (r == 22050 ? " (best)" : r == 8000 ? " (smallest)" : ""), Tag = r });
        _rate.SelectedIndex = 0;
        _rate.ToolTip = "Lower is smaller and lighter on the IIgs: a sound is at most 60 KB - 2.7 seconds at 22050, 5.5 at 11025, 7.6 at 8000. Longer is cut.";
        rows.Children.Add(_rate);
        rows.Children.Add(new TextBlock { Text = "They are:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        _kind.Items.Add(new ComboBoxItem { Content = "Sounds", Tag = ResourceKind.Sound });
        _kind.Items.Add(new ComboBoxItem { Content = "Music", Tag = ResourceKind.Music });
        _kind.SelectedIndex = 0;
        rows.Children.Add(_kind);
        var convert = new Button { Content = "Convert", Width = 100, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var close = new Button { Content = "Close", Width = 90, IsCancel = true };
        convert.Click += (_, _) => Convert();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(convert);
        buttons.Children.Add(close);
        rows.Children.Add(buttons);
        Content = rows;
    }

    private void Convert()
    {
        if (_files.Items.Count == 0) { MessageBox.Show(this, "Add the sound files to convert first.", Title); return; }
        int rate = (int)((ComboBoxItem)_rate.SelectedItem).Tag;
        var kind = (ResourceKind)((ComboBoxItem)_kind.SelectedItem).Tag;
        Directory.CreateDirectory(Folder);
        var made = new List<string>();
        var failed = new List<string>();
        var cut = new List<string>();
        foreach (string source in _files.Items.Cast<string>().ToList())
        {
            try
            {
                string path = Path.Combine(Folder, $"{Guid.NewGuid():N}.wav");
                bool wasCut = ToSnd(source, path, rate);
                string name = NextName();
                var entry = _resources.CreateNew(kind);
                entry.Name = name;
                entry.FilePath = path;
                entry.Volume = 1.0;
                entry.ChannelFormat = SoundChannelFormat.Mono;
                entry.SampleRate = rate;
                entry.BitDepth = 8;
                entry.ConversionMode = "Apple IIgs SND";
                entry.IsMix = true; // it lives only in the project (as a Sound Mixer mix)
                made.Add($"{name}  <-  {Path.GetFileName(source)}");
                if (wasCut) cut.Add(name);
                _files.Items.Remove(source);
            }
            catch (Exception ex) { failed.Add($"{Path.GetFileName(source)}: {ex.Message}"); }
        }
        string text = made.Count > 0 ? $"In the Sound Editor now (rename them there):\n{string.Join("\n", made)}" : "Nothing was converted.";
        if (cut.Count > 0) text += $"\n\nCut at {AppleIIgsLimits.MaxSoundBytes / 1024} KB (the most the IIgs's sound chip holds at once): {string.Join(", ", cut)}. A lower quality keeps more of a long sound.";
        if (failed.Count > 0) text += "\n\nCouldn't be converted:\n" + string.Join("\n", failed);
        text += "\n\nThey go into the project with the next Save Project.";
        MessageBox.Show(this, text, Title, MessageBoxButton.OK, failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    /// <summary>snd1, snd2...: the first number no sound has yet.</summary>
    private string NextName()
    {
        for (int n = 1; ; n++)
            if (!_resources.Entries.Any(r => string.Equals(r.Name, $"snd{n}", StringComparison.OrdinalIgnoreCase))) return $"snd{n}";
    }

    /// <summary>Any sound file to an 8-bit mono WAV at this rate, at most 60 KB, no 0 bytes (the DOC stops at a 0); true = it was cut.</summary>
    public static bool ToSnd(string source, string target, int rate)
    {
        ISampleProvider input;
        IDisposable? dispose = null;
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext is ".iff" or ".8svx")
        {
            var (samples, sampleRate) = AmigaIffCodec.Read(source);
            var f = new float[samples.Length];
            for (int i = 0; i < f.Length; i++) f[i] = samples[i] / 128f;
            var provider = new BufferedSampleProvider(f, sampleRate);
            input = provider;
        }
        else
        {
            var reader = new AudioFileReader(source);
            dispose = reader;
            input = reader;
        }
        try
        {
            if (input.WaveFormat.Channels == 2) input = new StereoToMonoSampleProvider(input) { LeftVolume = 0.5f, RightVolume = 0.5f };
            else if (input.WaveFormat.Channels > 2) throw new InvalidOperationException($"{input.WaveFormat.Channels} channels");
            if (input.WaveFormat.SampleRate != rate) input = new WdlResamplingSampleProvider(input, rate);
            var bytes = new List<byte>();
            var buffer = new float[4096];
            bool cut = false;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (bytes.Count >= AppleIIgsLimits.MaxSoundBytes) { cut = true; break; }
                    int v = (int)Math.Round(Math.Clamp(buffer[i], -1f, 1f) * 127f) + 128;
                    bytes.Add((byte)Math.Clamp(v, 1, 255));
                }
                if (cut) break;
            }
            using var writer = new WaveFileWriter(target, new WaveFormat(rate, 8, 1));
            writer.Write(bytes.ToArray(), 0, bytes.Count);
            return cut;
        }
        finally { dispose?.Dispose(); }
    }

    private sealed class BufferedSampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _at;
        public BufferedSampleProvider(float[] data, int rate) { _data = data; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1); }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _at);
            Array.Copy(_data, _at, buffer, offset, n);
            _at += n;
            return n;
        }
    }
}
