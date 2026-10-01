using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NAudio.Wave;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Piece 2 of the Sound Editor feature. The mixer's arrangement (columns,
/// what's in each one, links) lives on ResourceDatabase.Mixer and travels
/// with it - restored on open, updated whenever this window closes - so it
/// persists through audios.sdbx exactly like everything else, per explicit
/// request (this was originally session-only, matching GameMaker's own
/// mixer; that's since changed). A column's master volume is still a
/// real-time MULTIPLIER applied on top of each sound's own stored Volume
/// during preview playback - it never overwrites that stored value, which
/// is edited directly via the individual row sliders here or the Sound
/// Editor and persists on its own.
/// </summary>
public partial class SoundMixerWindow : Window
{
    private readonly ResourceDatabase _resourceDatabase;
    private readonly List<MixerColumnState> _columns = new();
    private readonly List<(MixerColumnState A, MixerColumnState B)> _linkedPairs = new();

    public SoundMixerWindow(ResourceDatabase resourceDatabase, ResourceEntry? initialSound = null)
    {
        InitializeComponent();
        _resourceDatabase = resourceDatabase;

        if (resourceDatabase.Mixer.Columns.Count > 0)
            RestoreLayout(resourceDatabase.Mixer);
        else
            _columns.Add(new MixerColumnState());

        if (initialSound is not null)
        {
            var target = new MixerColumnState();
            _columns.Add(target);
            var sound = new MixerPlayableSound(initialSound);
            sound.SetColumnMultiplier(target.MasterVolume);
            target.Sounds.Add(sound);
        }

        RebuildColumnsPanel();

        Closing += (_, _) =>
        {
            SaveLayoutBackToDatabase();
            foreach (var c in _columns) c.Dispose();
        };
    }

    /// <summary>Adds a sound to the first column - used when several sounds are selected at once (e.g. "Open In Sound Mixer" on a multi-selection) rather than one at a time via the constructor.</summary>
    public void AddSound(ResourceEntry entry) => AddSoundToColumn(_columns[0], entry);

    /// <summary>
    /// Rebuilds the live column/sound/link state from what was saved into
    /// ResourceDatabase.Mixer. One deliberate simplification: only the
    /// column-level Loop is restored, not each individual sound row's own
    /// loop toggle - that per-row state was never captured in
    /// MixerColumnLayout, so it resets to off on reload. Everything else
    /// (which sounds, master volume, Audio Group, links) comes back exactly
    /// as it was.
    /// </summary>
    private void RestoreLayout(MixerLayout layout)
    {
        _columns.Clear();
        foreach (var columnLayout in layout.Columns)
        {
            var column = new MixerColumnState
            {
                MasterVolume = columnLayout.MasterVolume,
                Loop = columnLayout.Loop,
                AudioGroupId = columnLayout.AudioGroupId
            };
            foreach (var soundId in columnLayout.SoundResourceIds)
            {
                var entry = _resourceDatabase.Entries.FirstOrDefault(e => e.Id == soundId);
                if (entry is null) continue; // the sound was deleted elsewhere since this layout was saved
                var sound = new MixerPlayableSound(entry) { Loop = columnLayout.Loop };
                sound.SetColumnMultiplier(column.MasterVolume);
                column.Sounds.Add(sound);
            }
            _columns.Add(column);
        }
        if (_columns.Count == 0) _columns.Add(new MixerColumnState());

        _linkedPairs.Clear();
        foreach (var pair in layout.LinkedPairs)
        {
            if (pair.ColumnIndexA >= 0 && pair.ColumnIndexA < _columns.Count &&
                pair.ColumnIndexB >= 0 && pair.ColumnIndexB < _columns.Count)
                _linkedPairs.Add((_columns[pair.ColumnIndexA], _columns[pair.ColumnIndexB]));
        }
    }

    private void SaveLayoutBackToDatabase()
    {
        var layout = new MixerLayout();
        foreach (var column in _columns)
        {
            layout.Columns.Add(new MixerColumnLayout
            {
                MasterVolume = column.MasterVolume,
                Loop = column.Loop,
                AudioGroupId = column.AudioGroupId,
                SoundResourceIds = column.Sounds.Select(s => s.Entry.Id).ToList()
            });
        }
        foreach (var (a, b) in _linkedPairs)
        {
            int indexA = _columns.IndexOf(a), indexB = _columns.IndexOf(b);
            if (indexA >= 0 && indexB >= 0)
                layout.LinkedPairs.Add(new MixerLinkedPair { ColumnIndexA = indexA, ColumnIndexB = indexB });
        }
        _resourceDatabase.Mixer = layout;
    }

    // ---------- Column lifecycle ----------

    private MixerColumnState AddColumn()
    {
        var column = new MixerColumnState();
        _columns.Add(column);
        RebuildColumnsPanel();
        return column;
    }

    private void RemoveColumn(MixerColumnState column)
    {
        _linkedPairs.RemoveAll(p => p.A == column || p.B == column);
        column.Dispose();
        _columns.Remove(column);
        if (_columns.Count == 0) _columns.Add(new MixerColumnState()); // always keep at least one column
        RebuildColumnsPanel();
    }

    private List<MixerColumnState> GetLinkedGroup(MixerColumnState start)
    {
        var group = new List<MixerColumnState> { start };
        var frontier = new Queue<MixerColumnState>();
        frontier.Enqueue(start);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var (a, b) in _linkedPairs)
            {
                MixerColumnState? other = a == current ? b : b == current ? a : null;
                if (other is not null && !group.Contains(other))
                {
                    group.Add(other);
                    frontier.Enqueue(other);
                }
            }
        }
        return group;
    }

    private void ToggleLink(MixerColumnState a, MixerColumnState b)
    {
        int existing = _linkedPairs.FindIndex(p => (p.A == a && p.B == b) || (p.A == b && p.B == a));
        if (existing >= 0) _linkedPairs.RemoveAt(existing);
        else _linkedPairs.Add((a, b));
        RebuildColumnsPanel();
    }

    // ---------- Sound management ----------

    private void AddSoundToColumn(MixerColumnState column, ResourceEntry entry)
    {
        if (column.Sounds.Any(s => s.Entry == entry)) return;
        var sound = new MixerPlayableSound(entry);
        sound.SetColumnMultiplier(column.MasterVolume);
        column.Sounds.Add(sound);
        RebuildColumnsPanel();
    }

    private void RemoveSoundFromColumn(MixerColumnState column, MixerPlayableSound sound)
    {
        sound.Dispose();
        column.Sounds.Remove(sound);
        RebuildColumnsPanel();
    }

    private void LoadAudioGroupIntoColumn(MixerColumnState column, int audioGroupId)
    {
        column.AudioGroupId = audioGroupId;
        var matching = _resourceDatabase.Entries.Where(e =>
            (e.Kind == ResourceKind.Sound || e.Kind == ResourceKind.Music) && e.AudioGroupId == audioGroupId);
        foreach (var entry in matching)
        {
            if (column.Sounds.Any(s => s.Entry == entry)) continue;
            var sound = new MixerPlayableSound(entry);
            sound.SetColumnMultiplier(column.MasterVolume);
            column.Sounds.Add(sound);
        }
        RebuildColumnsPanel();
    }

    // ---------- Column-level playback/volume ----------

    /// <summary>
    /// A linked column's slider gets set here too, which fires its own
    /// ValueChanged and calls back into this method for it - that's fine and
    /// intentional, not a bug: WPF only raises ValueChanged when a value
    /// actually differs, so the recursive call finds every slider already at
    /// the target volume and does nothing further. Self-terminating by
    /// construction, no explicit guard needed.
    /// </summary>
    private void SetColumnVolume(MixerColumnState column, double volume)
    {
        foreach (var linked in GetLinkedGroup(column))
        {
            linked.MasterVolume = volume;
            foreach (var sound in linked.Sounds) sound.SetColumnMultiplier(volume);
            if (linked.VolumeSliderControl is { } slider) slider.Value = volume;
        }
    }

    private void PlayColumn(MixerColumnState column)
    {
        foreach (var linked in GetLinkedGroup(column))
            foreach (var sound in linked.Sounds)
                sound.Play();
    }

    private void RewindColumn(MixerColumnState column)
    {
        foreach (var linked in GetLinkedGroup(column))
            foreach (var sound in linked.Sounds)
                sound.Rewind();
    }

    private void SetColumnLoop(MixerColumnState column, bool loop)
    {
        foreach (var linked in GetLinkedGroup(column))
        {
            linked.Loop = loop;
            foreach (var sound in linked.Sounds) sound.Loop = loop;
        }
    }

    // ---------- UI construction (built in code, not XAML - same pattern used elsewhere in this project for dynamic per-item panels) ----------

    private void RebuildColumnsPanel()
    {
        ColumnsPanel.Children.Clear();
        for (int i = 0; i < _columns.Count; i++)
        {
            ColumnsPanel.Children.Add(BuildColumnBorder(_columns[i]));
            if (i < _columns.Count - 1)
                ColumnsPanel.Children.Add(BuildLinkButton(_columns[i], _columns[i + 1]));
        }
        ColumnsPanel.Children.Add(BuildAddColumnButton());
    }

    private UIElement BuildLinkButton(MixerColumnState left, MixerColumnState right)
    {
        bool linked = _linkedPairs.Any(p => (p.A == left && p.B == right) || (p.A == right && p.B == left));
        var button = new Button
        {
            Content = "\u21D4",
            Width = 24,
            Height = 24,
            Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = linked ? Brushes.SteelBlue : Brushes.Transparent,
            ToolTip = "Link these two columns - their volume and playback move together"
        };
        button.Click += (_, _) => ToggleLink(left, right);
        return button;
    }

    private UIElement BuildAddColumnButton()
    {
        var button = new Button
        {
            Content = "+",
            FontSize = 24,
            Width = 40,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Add Column"
        };
        button.Click += (_, _) => AddColumn();
        return button;
    }

    private Border BuildColumnBorder(MixerColumnState column)
    {
        var root = new StackPanel { Margin = new Thickness(6) };
        var border = new Border
        {
            BorderBrush = Brushes.SeaGreen,
            BorderThickness = new Thickness(1),
            Width = 260,
            Margin = new Thickness(4),
            Child = root
        };

        root.Children.Add(new TextBlock { Text = "Column Volume", FontWeight = FontWeights.Bold, Margin = new Thickness(4, 4, 4, 4) });

        // Master transport row: play/pause, loop, rewind, volume.
        var transport = new Grid { Margin = new Thickness(4, 0, 4, 8) };
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var volumeSlider = new Slider { Minimum = 0, Maximum = 1, Value = column.MasterVolume, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        column.VolumeSliderControl = volumeSlider;
        volumeSlider.ValueChanged += (_, e) => SetColumnVolume(column, e.NewValue);
        Grid.SetColumn(volumeSlider, 0);
        transport.Children.Add(volumeSlider);

        var playButton = new Button { Content = "\u25B6", Width = 26, Margin = new Thickness(0, 0, 2, 0), ToolTip = "Play all sounds in this column" };
        playButton.Click += (_, _) => PlayColumn(column);
        Grid.SetColumn(playButton, 1);
        transport.Children.Add(playButton);

        var loopToggle = new ToggleButton { Content = "\u21BB", Width = 26, Margin = new Thickness(0, 0, 2, 0), IsChecked = column.Loop, ToolTip = "Loop" };
        loopToggle.Checked += (_, _) => SetColumnLoop(column, true);
        loopToggle.Unchecked += (_, _) => SetColumnLoop(column, false);
        Grid.SetColumn(loopToggle, 2);
        transport.Children.Add(loopToggle);

        var rewindButton = new Button { Content = "\u23EE", Width = 26, ToolTip = "Rewind all sounds in this column" };
        rewindButton.Click += (_, _) => RewindColumn(column);
        Grid.SetColumn(rewindButton, 3);
        transport.Children.Add(rewindButton);

        root.Children.Add(transport);

        root.Children.Add(new TextBlock { Text = "Load Audio Group:", Margin = new Thickness(4, 0, 4, 2) });
        var groupRow = new Grid { Margin = new Thickness(4, 0, 4, 8) };
        groupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        groupRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var groupCombo = new ComboBox();
        var groupItems = new List<string> { "(None)" };
        groupItems.AddRange(_resourceDatabase.AudioGroups.Select(g => g.Name));
        groupCombo.ItemsSource = groupItems;
        var currentGroup = _resourceDatabase.AudioGroups.FirstOrDefault(g => g.Id == column.AudioGroupId);
        groupCombo.SelectedItem = currentGroup?.Name ?? "(None)";
        groupCombo.SelectionChanged += (_, _) =>
        {
            string? selected = groupCombo.SelectedItem as string;
            var group = _resourceDatabase.AudioGroups.FirstOrDefault(g => g.Name == selected);
            if (group is not null) LoadAudioGroupIntoColumn(column, group.Id);
        };
        Grid.SetColumn(groupCombo, 0);
        groupRow.Children.Add(groupCombo);

        var removeColumnButton = new Button { Content = "X", Width = 24, Margin = new Thickness(4, 0, 0, 0), Foreground = Brushes.White, Background = Brushes.Firebrick, ToolTip = "Remove this column from the mixer" };
        removeColumnButton.Click += (_, _) => RemoveColumn(column);
        Grid.SetColumn(removeColumnButton, 1);
        groupRow.Children.Add(removeColumnButton);

        root.Children.Add(groupRow);

        // Add a sound manually (drag-and-drop from an asset browser isn't a concept this app has - picking from a list does the same job).
        var addSoundCombo = new ComboBox { Margin = new Thickness(4, 0, 4, 6) };
        var candidates = _resourceDatabase.Entries.Where(e => e.Kind is ResourceKind.Sound or ResourceKind.Music).ToList();
        addSoundCombo.ItemsSource = candidates;
        addSoundCombo.DisplayMemberPath = "Name";
        addSoundCombo.Text = "+ Add Sound...";
        addSoundCombo.IsEditable = true;
        addSoundCombo.IsReadOnly = true;
        addSoundCombo.SelectionChanged += (_, _) =>
        {
            if (addSoundCombo.SelectedItem is ResourceEntry entry) AddSoundToColumn(column, entry);
        };
        root.Children.Add(addSoundCombo);

        var rowsPanel = new StackPanel();
        foreach (var sound in column.Sounds)
            rowsPanel.Children.Add(BuildSoundRow(column, sound));
        root.Children.Add(rowsPanel);

        return border;
    }

    private UIElement BuildSoundRow(MixerColumnState column, MixerPlayableSound sound)
    {
        var container = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4) };
        var stack = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock { Text = sound.Entry.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(nameText, 0);
        header.Children.Add(nameText);

        var playButton = new Button { Content = "\u25B6", Width = 22, Margin = new Thickness(2, 0, 0, 0), ToolTip = sound.IsSupported ? "Play" : "Playback preview not supported for this format yet" };
        playButton.Click += (_, _) => { if (sound.IsSupported) sound.Play(); };
        Grid.SetColumn(playButton, 1);
        header.Children.Add(playButton);

        var loopToggle = new ToggleButton { Content = "\u21BB", Width = 22, Margin = new Thickness(2, 0, 0, 0), ToolTip = "Loop this sound" };
        loopToggle.Checked += (_, _) => sound.Loop = true;
        loopToggle.Unchecked += (_, _) => sound.Loop = false;
        Grid.SetColumn(loopToggle, 2);
        header.Children.Add(loopToggle);

        var removeButton = new Button { Content = "\u2715", Width = 22, Margin = new Thickness(2, 0, 0, 0), ToolTip = "Remove from this column" };
        removeButton.Click += (_, _) => RemoveSoundFromColumn(column, sound);
        Grid.SetColumn(removeButton, 3);
        header.Children.Add(removeButton);

        stack.Children.Add(header);

        var volumeRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volumeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volumeRow.Children.Add(new TextBlock { Text = "\U0001F50A", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        var volumeSlider = new Slider { Minimum = 0, Maximum = 1, Value = sound.Entry.Volume, VerticalAlignment = VerticalAlignment.Center };
        volumeSlider.ValueChanged += (_, e) =>
        {
            sound.Entry.Volume = e.NewValue; // this DOES persist - it's the sound's own stored volume, same field the Sound Editor edits
            sound.ApplyVolume();
        };
        Grid.SetColumn(volumeSlider, 1);
        volumeRow.Children.Add(volumeSlider);
        stack.Children.Add(volumeRow);

        container.Child = stack;
        return container;
    }

    // ---------- Playback plumbing ----------

    private sealed class MixerPlayableSound : IDisposable
    {
        public ResourceEntry Entry { get; }
        public bool Loop { get; set; }
        public bool IsSupported => _waveOut is not null;

        private AudioFileReader? _reader;
        private WaveOutEvent? _waveOut;
        private double _columnMultiplier = 1.0;

        public MixerPlayableSound(ResourceEntry entry)
        {
            Entry = entry;
            TryLoad();
        }

        private void TryLoad()
        {
            try
            {
                if (!File.Exists(Entry.FilePath)) return;
                _reader = new AudioFileReader(Entry.FilePath);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_reader);
                _waveOut.PlaybackStopped += (_, _) =>
                {
                    if (!Loop || _reader is null || _waveOut is null) return;
                    _reader.Position = 0;
                    _waveOut.Play();
                };
                ApplyVolume();
            }
            catch
            {
                // A format AudioFileReader can't open (OGG, Amiga IFF/8SVX, etc.) -
                // this sound just can't preview-play from the mixer yet; its row
                // still shows and its volume is still editable and persists.
            }
        }

        public void SetColumnMultiplier(double multiplier)
        {
            _columnMultiplier = multiplier;
            ApplyVolume();
        }

        public void ApplyVolume()
        {
            if (_reader is not null) _reader.Volume = (float)Math.Clamp(Entry.Volume * _columnMultiplier, 0, 1);
        }

        public void Play() => _waveOut?.Play();
        public void Pause() => _waveOut?.Pause();
        public void Rewind() { if (_reader is not null) _reader.CurrentTime = TimeSpan.Zero; }

        public void Dispose()
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _reader?.Dispose();
        }
    }

    private sealed class MixerColumnState
    {
        public double MasterVolume { get; set; } = 1.0;
        public bool Loop { get; set; }
        public int? AudioGroupId { get; set; }
        public List<MixerPlayableSound> Sounds { get; } = new();
        public Slider? VolumeSliderControl;

        public void Dispose()
        {
            foreach (var sound in Sounds) sound.Dispose();
        }
    }
}
