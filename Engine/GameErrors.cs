using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>One error's number and its words (System Settings > Errors) - the maker's own, or the engine's.</summary>
public class GameErrorMessage
{
    public int Number { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// The game's errors (System Settings > Errors): the paperdoll they show in,
/// and their words by number. In the paperdoll's text and captions,
/// %errnum is the number and %errdescrp the words; in the words, %errdetail
/// is what the engine knows of it (what's going on, the file, Windows' own
/// reason). An error not in the list shows %errdetail as its words.
/// </summary>
public class ErrorSettings
{
    /// <summary>The error paperdoll (-1 = none: the message shows where it happened, as before).</summary>
    public int DollDesignId { get; set; } = -1;
    /// <summary>The words by number, as the maker set them (the engine's own fill in the rest).</summary>
    public List<GameErrorMessage> Messages { get; set; } = new();
}

/// <summary>The engine's error numbers and their own words; finding an error's words; telling a disk error by Windows' reason.</summary>
public static class GameErrors
{
    // Save and load
    public const int NoSaveScreen = 101, CantSaveNow = 102, PickSlotToSave = 103, PickSaveToLoad = 104, EmptySlot = 105,
        SaveFailed = 106, LoadFailed = 107, SaveDamaged = 108, SaveTooNew = 109, DeleteFailed = 110, NeedsName = 111, ProjectNotSaved = 112;
    // The disk
    public const int DiskFull = 201, DiskDenied = 202, FileMissing = 203;
    // The game's own data
    public const int DataMissing = 301, DataNotOwn = 302, DataDamaged = 303, CantStart = 304;
    // Anything else
    public const int Unexpected = 900;

    public static readonly IReadOnlyList<GameErrorMessage> Defaults = new List<GameErrorMessage>
    {
        new() { Number = NoSaveScreen, Description = "This game has no Save/Load screen." },
        new() { Number = CantSaveNow, Description = "You can't save now: %errdetail." },
        new() { Number = PickSlotToSave, Description = "Click a slot to save into first." },
        new() { Number = PickSaveToLoad, Description = "Pick a save first." },
        new() { Number = EmptySlot, Description = "That slot is empty." },
        new() { Number = SaveFailed, Description = "The game couldn't be saved: %errdetail" },
        new() { Number = LoadFailed, Description = "The saved game couldn't be loaded: %errdetail" },
        new() { Number = SaveDamaged, Description = "The saved game is damaged and can't be loaded." },
        new() { Number = SaveTooNew, Description = "This save was made by a newer version of the game." },
        new() { Number = DeleteFailed, Description = "The save couldn't be deleted: %errdetail" },
        new() { Number = NeedsName, Description = "Type a name for the save first." },
        new() { Number = ProjectNotSaved, Description = "Saving needs the project to be saved once first." },
        new() { Number = DiskFull, Description = "There isn't enough room on the disk." },
        new() { Number = DiskDenied, Description = "The disk can't be written to - it's read-only, or access is denied." },
        new() { Number = FileMissing, Description = "A file is missing: %errdetail" },
        new() { Number = DataMissing, Description = "A game data file is missing: %errdetail" },
        new() { Number = DataNotOwn, Description = "A game data file isn't this game's own: %errdetail" },
        new() { Number = DataDamaged, Description = "The game's data is damaged: %errdetail" },
        new() { Number = CantStart, Description = "The game couldn't start: %errdetail" },
        new() { Number = Unexpected, Description = "Something went wrong: %errdetail" },
    };

    /// <summary>An error's words: the maker's, else the engine's, else (an unknown number) the detail itself; %errdetail filled in.</summary>
    public static string Describe(ErrorSettings? settings, int number, string detail)
    {
        string? words = settings?.Messages.FirstOrDefault(m => m.Number == number)?.Description
                        ?? Defaults.FirstOrDefault(m => m.Number == number)?.Description;
        if (string.IsNullOrWhiteSpace(words)) return detail.Length > 0 ? detail : $"Error {number}.";
        return words.Replace("%errdetail", detail, StringComparison.OrdinalIgnoreCase).Trim();
    }

    /// <summary>A paperdoll's text with %errnum and %errdescrp (and %errdetail) filled in.</summary>
    public static string Fill(string text, int number, string description, string detail) =>
        text.Replace("%errnum", number.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("%errdescrp", description, StringComparison.OrdinalIgnoreCase)
            .Replace("%errdetail", detail, StringComparison.OrdinalIgnoreCase);

    /// <summary>A disk's trouble by Windows' reason (full, read-only, missing) - else the number given.</summary>
    public static int Classify(Exception ex, int otherwise)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case UnauthorizedAccessException: return DiskDenied;
                case FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException: return FileMissing;
                case IOException io:
                    int code = io.HResult & 0xFFFF;
                    if (code is 0x70 or 0x27) return DiskFull;      // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL
                    if (code is 0x13 or 0x05 or 0x20) return DiskDenied; // write protected, access denied, in use
                    break;
            }
        }
        return otherwise;
    }

    /// <summary>Why a save couldn't be read: too new, the disk, else damaged.</summary>
    public static int ClassifyLoad(Exception ex) =>
        ex is SaveTooNewException ? SaveTooNew : Classify(ex, ex is JsonException or ProjectFileException or InvalidDataException or System.Security.Cryptography.CryptographicException ? SaveDamaged : LoadFailed);
}

/// <summary>A save made by a newer version of the game.</summary>
public class SaveTooNewException : ProjectFileException
{
    public SaveTooNewException(string message) : base(message) { }
}
