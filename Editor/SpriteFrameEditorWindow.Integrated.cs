using System.IO;
using System.Text.Json;
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Editing a sprite already integrated into the project (in shapes.sdbx): it's taken out as it is when the window opens -
/// its settings and every picture of it, byte for byte. Closing the window with anything at all changed (a pixel, a frame,
/// the fps from 20 to 19, a hand point) asks: Yes - the project is saved, so the sprite is integrated again with its
/// changes; No - it's put back exactly as it was; Cancel - back to editing. A sprite not integrated yet (just imported)
/// closes as before: it goes in with the next save.
/// </summary>
public partial class SpriteFrameEditorWindow
{
    private string? _integratedJson;
    private Dictionary<string, byte[]>? _integratedFiles;
    private bool _integratedDecided;

    /// <summary>As the window opens: an integrated sprite's settings and pictures, to compare and to put back.</summary>
    private void TakeIntegratedSnapshot()
    {
        if (!_sprite.IsProjectIntegrated) return;
        _integratedJson = JsonSerializer.Serialize(_sprite);
        _integratedFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in PicturePaths(_sprite))
            try { if (File.Exists(path)) _integratedFiles[path] = File.ReadAllBytes(path); } catch (IOException) { }
        Closing += AskAboutIntegratedChanges;
    }

    /// <summary>Every picture file a sprite uses: its own, each frame's, each frame's layers.</summary>
    private static IEnumerable<string> PicturePaths(SpriteEntry sprite)
    {
        if (!string.IsNullOrEmpty(sprite.FilePath)) yield return sprite.FilePath;
        foreach (var frame in sprite.Frames)
        {
            if (!string.IsNullOrEmpty(frame.FilePath)) yield return frame.FilePath;
            foreach (var layer in frame.LayerFilePaths.Values)
                if (!string.IsNullOrEmpty(layer)) yield return layer;
        }
    }

    /// <summary>Anything at all different from when the window opened - a setting, or a picture's bytes.</summary>
    private bool IntegratedSpriteChanged()
    {
        if (_integratedJson is null || _integratedFiles is null) return false;
        if (JsonSerializer.Serialize(_sprite) != _integratedJson) return true;
        foreach (var path in PicturePaths(_sprite).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_integratedFiles.TryGetValue(path, out var before)) return true; // a picture it didn't have
            try { if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(before)) return true; }
            catch (IOException) { return true; }
        }
        return false;
    }

    private void AskAboutIntegratedChanges(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_integratedDecided || !IntegratedSpriteChanged()) return;
        var answer = MessageBox.Show(this,
            $"\"{_sprite.Name}\" is integrated into the project, and it's been changed.\n\n" +
            "Yes - save: the project is saved, and the sprite is integrated again with its changes.\n" +
            "No - put it back exactly as it was (every change is undone).\n" +
            "Cancel - go back to editing.",
            "Integrated sprite changed", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        switch (answer)
        {
            case MessageBoxResult.Yes:
                if (ProjectSession.SaveProjectNow?.Invoke() != true) { e.Cancel = true; return; } // not saved (it said why): still open, nothing lost
                break;
            case MessageBoxResult.No:
                PutIntegratedSpriteBack();
                break;
            default:
                e.Cancel = true;
                return;
        }
        _integratedDecided = true;
        _playbackTimer.Stop();
    }

    /// <summary>Its pictures and settings exactly as they were when the window opened.</summary>
    private void PutIntegratedSpriteBack()
    {
        if (_integratedJson is null || _integratedFiles is null) return;
        foreach (var (path, bytes) in _integratedFiles)
            try
            {
                if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
                File.WriteAllBytes(path, bytes);
            }
            catch (IOException) { }
        if (JsonSerializer.Deserialize<SpriteEntry>(_integratedJson) is not { } before) return;
        foreach (var property in typeof(SpriteEntry).GetProperties())
            if (property.CanWrite && property.Name != nameof(SpriteEntry.IsProjectIntegrated))
                property.SetValue(_sprite, property.GetValue(before));
        _sprite.SyncLayerIdAfterLoad();
        _sprite.IsProjectIntegrated = true;
    }
}
