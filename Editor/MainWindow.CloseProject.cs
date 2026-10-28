using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Closing the project open in the editor before another is opened or a new one made, so nothing of one project can
/// end up in another: asked whether to save it first; every editor window still open on it closed; its map taken off
/// the screen; every database, the worlds, the houses, the clipboards, the selection and the undo history emptied;
/// the menus as when the editor starts.
/// </summary>
public partial class MainWindow
{
    private bool HasOpenProject => _currentProjectId is not null || _map is not null;

    /// <summary>File > Close Project: closed (asked whether to save it), the editor left with no project.</summary>
    private void CloseProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!HasOpenProject) { StatusText.Text = "No project open."; return; }
        CloseCurrentProject("closing it");
    }

    /// <summary>Closes the project open now (asking to save it); false when the person cancels - it stays open.</summary>
    /// <param name="doing">What's next, for the question: "opening another project", "starting the new project".</param>
    private bool CloseCurrentProject(string doing)
    {
        if (!HasOpenProject) return true;
        string name = _currentProjectName is { Length: > 0 } n ? n : "the current project";
        var answer = MessageBox.Show(this, $"Save \"{name}\" before {doing}?\n\nYes: save it, then close it.  No: close it without saving.  Cancel: keep it open.",
            "Close Project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes && _currentProjectId is not null && !SaveProject()) return false; // not saved: it stays open
        ResetToNoProject();
        return true;
    }

    /// <summary>The editor as it starts: no project, nothing of the last one left anywhere.</summary>
    private void ResetToNoProject()
    {
        // Every window still open on its data (Character Editor, Object Editor, Sprite Editor...).
        foreach (var window in OwnedWindows.Cast<Window>().ToList()) window.Close();

        // Its map, off the screen.
        MapCanvas.Children.Clear();
        foreach (var animator in _mapCellAnimators.Values) animator.Stop();
        _mapCellAnimators.Clear();
        _mapCellVisuals.Clear();
        _tileChunks.Clear();
        _dirtyChunks.Clear();
        _mapObjectVisuals.Clear();
        _landingVisuals.Clear();
        _mapEventVisuals.Clear();
        _startPositionVisual = null;
        _hoverCursorVisual = null;
        _houseGhost = null;
        _houseGhostOf = null;
        _placedHouseVisuals.Clear();
        _shapePreviewVisuals.Clear();
        _shapeStartCell = null;
        _mapItemVisuals.Clear();
        foreach (var animator in _itemHoverAnimators) animator.Stop();
        _itemHoverAnimators.Clear();
        foreach (var animator in _paletteTileAnimators) animator.Stop();
        _paletteTileAnimators.Clear();
        PaletteCanvas.Children.Clear();
        _sheetCache.Clear();
        _map = null;
        _tileset = null;
        _tilesetBitmap = null;

        // Its data.
        _spriteDatabase = new SpriteDatabase();
        _spriteDatabase.EnsureBuiltInGroups();
        _bookDatabase = new BookDatabase();
        _containerDatabase = new ContainerDatabase();
        _systemSettings = new SystemSettings();
        _resourceDatabase = new ResourceDatabase();
        _videoDatabase = new VideoDatabase();
        _factionDatabase = new FactionDatabase();
        _characterDatabase = new CharacterDatabase();
        _variableDatabase = new VariableDatabase();
        _statDatabase = StatDatabase.CreateDefault();
        _poseDatabase = PoseDatabase.CreateDefault();
        _tilePropertiesDatabase = new TilePropertiesDatabase();
        _objectDatabase = new ObjectDatabase();
        _currencyDatabase = new CurrencyDatabase();
        _objectDatabasePath = null;
        _tilePropertiesPath = null;
        LoadWorlds(null, null);
        LoadHouses(null);
        _projectStyle = GameStyle.Ultima7;

        // What was copied, chosen and undoable in it.
        _copiedCharacter = null;
        _freeCopied = null;
        _copiedObject = null;
        _copiedSpawn = null;
        _copiedEventPages = null;
        _freeSelected = null;
        _selectedObjectInstance = null;
        _undoSteps.Clear();
        _redoSteps.Clear();
        _tileStroke.Clear();
        _undoMap = null;
        _undoBaseline = null;

        // Which project it was: none.
        _currentProjectFilePath = null;
        _currentProjectId = null;
        _currentProjectFolder = null;
        _currentProjectName = null;
        _currentProjectPlatform = null;

        // The editor as it starts.
        SaveProjectMenuItem.IsEnabled = false;
        EditMenu.IsEnabled = false;
        ModeMenu.IsEnabled = false;
        GameMenu.IsEnabled = false;
        DrawMenu.IsEnabled = false;
        ToolsMenu.IsEnabled = false;
        MainToolBar.IsEnabled = false;
        MainContentGrid.IsEnabled = false;
        RefreshTilesetCombo();
        RefreshCharactersPalette();
        RefreshObjectPalette();
        Title = "Sandcastle RPG Maker - Map Editor";
        StatusText.Text = "No project open.";
    }
}
