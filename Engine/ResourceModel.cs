namespace SandcastleRpgMaker.Engine;

public enum ResourceKind
{
    Sprite,
    Sound,
    Music
}

/// <summary>Matches the four radio options in the Sound Editor's Attributes group.</summary>
public enum SoundAttributeMode
{
    UncompressedNotStreamed,
    CompressedNotStreamed,
    UncompressOnLoadNotStreamed,
    CompressedStreamed
}

public enum SoundChannelFormat { Mono, Stereo }

/// <summary>A named bucket sound/music resources can be assigned to - what both the Sound Editor's "Audio Group" dropdown and the Sound Mixer's per-column "Load Audio Group" dropdown point at.</summary>
public class AudioGroupDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Audio Group";
}

/// <summary>
/// One managed media asset. This is the v1 shape (a named file reference);
/// sprite-sheet slicing/animation-frame data and per-frame sound will extend
/// this later without changing the id/kind/path core.
///
/// Volume through ExportDirectory below are only meaningful when Kind is
/// Sound or Music - they're the Sound Editor's actual data fields (volume,
/// the Attributes radio group, Target Options, Audio Group assignment, and
/// export directory). Left at their defaults and simply unused for Sprite
/// resources rather than split into a separate subclass, since every
/// resource already shares one flat, freely-JSON-serializable shape.
/// </summary>
public class ResourceEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Resource";
    public ResourceKind Kind { get; set; } = ResourceKind.Sprite;
    public string FilePath { get; set; } = string.Empty;

    public double Volume { get; set; } = 1.0;
    public SoundAttributeMode AttributeMode { get; set; } = SoundAttributeMode.UncompressedNotStreamed;

    /// <summary>"Conversion (for OGG only)" dropdown - only "Automatic" exists for now; real conversion options land with the format-conversion piece.</summary>
    public string ConversionMode { get; set; } = "Automatic";

    public SoundChannelFormat ChannelFormat { get; set; } = SoundChannelFormat.Mono;
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 16;
    public int CompressionQuality { get; set; } = 4;
    public int AudioGroupId { get; set; } = -1;
    public string ExportDirectory { get; set; } = string.Empty;

    /// <summary>Set true after a Save/New/Open Project, mirroring SpriteEntry's own flag - "nothing about Integrated Into System is true until a Save has actually happened" applies here identically.</summary>
    public bool IsProjectIntegrated { get; set; }

    /// <summary>Made in the Sound Mixer (Compose): it never existed as a file outside the editor - it lives only in the project.</summary>
    public bool IsMix { get; set; }

    public string DisplayLocation => IsMix ? "\U0001F39B Composed in the Sound Mixer" + (IsProjectIntegrated ? " (in the project)" : " (goes into the project at the next Save)")
        : IsProjectIntegrated ? "\U0001F5A5 Integrated Into System" : FilePath;
}

/// <summary>One column's saved state in the Sound Mixer - which sounds it holds (by ResourceEntry.Id, so a rename elsewhere doesn't break it), its master volume/loop, and which Audio Group (if any) it's tracking.</summary>
public class MixerColumnLayout
{
    public double MasterVolume { get; set; } = 1.0;
    public bool Loop { get; set; }
    public int? AudioGroupId { get; set; }
    public List<int> SoundResourceIds { get; set; } = new();
    /// <summary>Each sound's start in the mix, in seconds (same order as SoundResourceIds).</summary>
    public List<double> SoundDelays { get; set; } = new();
}

/// <summary>A Link Columns connection between two columns, referenced by their index in MixerLayout.Columns. A plain class with named properties, not a tuple - System.Text.Json doesn't reliably serialize bare ValueTuples (this exact mistake already broke book frame geometry once).</summary>
public class MixerLinkedPair
{
    public int ColumnIndexA { get; set; }
    public int ColumnIndexB { get; set; }
}

/// <summary>The Sound Mixer's whole live arrangement - columns, what's in them, and what's linked. Travels with ResourceDatabase itself since every window that can open the mixer already shares that same object reference.</summary>
public class MixerLayout
{
    public List<MixerColumnLayout> Columns { get; set; } = new();
    public List<MixerLinkedPair> LinkedPairs { get; set; } = new();
}

public class ResourceDatabase
{
    public List<ResourceEntry> Entries { get; set; } = new();
    public List<AudioGroupDefinition> AudioGroups { get; set; } = new();
    public MixerLayout Mixer { get; set; } = new();

    private int _nextId = 1;
    private int _nextAudioGroupId = 1;

    public ResourceEntry CreateNew(ResourceKind kind)
    {
        var entry = new ResourceEntry { Id = _nextId++, Kind = kind, Name = $"{kind} {_nextId - 1}" };
        Entries.Add(entry);
        return entry;
    }

    public AudioGroupDefinition CreateAudioGroup()
    {
        var group = new AudioGroupDefinition { Id = _nextAudioGroupId++, Name = $"Audio Group {_nextAudioGroupId - 1}" };
        AudioGroups.Add(group);
        return group;
    }

    public void SyncNextIdAfterLoad()
    {
        _nextId = Entries.Count == 0 ? 1 : Entries.Max(e => e.Id) + 1;
        _nextAudioGroupId = AudioGroups.Count == 0 ? 1 : AudioGroups.Max(g => g.Id) + 1;
    }
}
