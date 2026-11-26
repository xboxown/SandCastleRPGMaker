// Ultima 6 engine (U6GameWindow): the HUD's pieces and panels in play - Ultima 6 projects only.
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The HUD in play, as the HUD commands change it (the user, 2026-11-26: serious power for the HUD, in place of paperdolls):
/// any piece shown, hidden or toggled, moved, its picture or (a Label's) words changed; custom panels - every Panel after the
/// first (the main one shows a member) - closed as the game starts unless ticked Open at the start, opened and closed by
/// their key (HUD Editor) or Show / Hide HUD Panel, their key switched off and on (Enable / Disable Panel Key); a piece in a
/// custom panel shows only while it is open. All kept in saves (GameSave.U6HudPieces). The HUD Editor's pieces stay as made.
/// </summary>
public partial class U6GameWindow
{
    /// <summary>Each piece the commands changed (by its Id).</summary>
    private readonly Dictionary<int, U6HudPieceState> _hudState = new();

    private U6HudPieceState HudStateOf(HudPiece piece)
    {
        if (!_hudState.TryGetValue(piece.Id, out var state)) _hudState[piece.Id] = state = new U6HudPieceState { Id = piece.Id };
        return state;
    }

    /// <summary>Shown by itself: as a command left it, else a custom panel as ticked (Open at the start), anything else shown.</summary>
    private bool HudPieceOwnShown(HudPiece piece) =>
        _hudState.TryGetValue(piece.Id, out var state) && state.Shown is bool shown ? shown : !Hud.IsCustomPanel(piece) || piece.StartsOpen;

    /// <summary>On the screen now: shown, and its custom panel (if in one) open.</summary>
    private bool HudPieceShown(HudPiece piece)
    {
        if (!HudPieceOwnShown(piece)) return false;
        if (piece.PanelId > 0 && piece.Kind != HudPieceKind.Panel)
            return Hud.Piece(piece.PanelId) is { } panel && Hud.IsCustomPanel(panel) && HudPieceOwnShown(panel);
        return true;
    }

    /// <summary>The piece as it is now: moved, another picture, other words (a copy - the HUD Editor's stays as made).</summary>
    private HudPiece HudPieceNow(HudPiece piece)
    {
        if (!_hudState.TryGetValue(piece.Id, out var state) || (state.X is null && state.Y is null && state.Sprite is null && state.Text is null)) return piece;
        var now = piece.Copy();
        if (state.X is int x) now.X = x;
        if (state.Y is int y) now.Y = y;
        if (state.Sprite is { } sprite) now.Sprite = sprite;
        if (state.Text is { } text) now.Text = text;
        return now;
    }

    /// <summary>The HUD's pieces on the screen now, as they are now, in the HUD's order.</summary>
    private IEnumerable<HudPiece> HudPiecesNow => Hud.Pieces.Where(HudPieceShown).Select(HudPieceNow).ToList();

    private void InitHudPanels()
    {
        var h = _execContext.GameHandlers;
        h[EventCommandType.U6ShowHidePiece] = c =>
        {
            if (Hud.Named(c.Get("Piece")) is not { } piece) return false;
            bool now = HudPieceOwnShown(piece);
            HudStateOf(piece).Shown = c.Get("Value") switch { "Hidden" => false, "Toggled" => !now, _ => true };
            HudChanged();
            return true;
        };
        h[EventCommandType.U6ShowHidePanel] = c =>
        {
            if (Hud.Named(c.Get("Piece")) is not { } panel || !Hud.IsCustomPanel(panel)) return false;
            bool open = HudPieceOwnShown(panel);
            HudStateOf(panel).Shown = c.Get("Value") switch { "Closed" => false, "Toggled" => !open, _ => true };
            HudChanged();
            return true;
        };
        h[EventCommandType.U6SetPiecePicture] = c =>
        {
            if (Hud.Named(c.Get("Piece")) is not { } piece) return false;
            HudStateOf(piece).Sprite = c.Get("Own") == "True" || c.Get("SpriteName").Length == 0 ? null : new SpriteLink { SpriteGroup = c.Get("SpriteGroup"), SpriteName = c.Get("SpriteName") };
            HudChanged();
            return true;
        };
        h[EventCommandType.U6SetPieceText] = c =>
        {
            if (Hud.Named(c.Get("Piece")) is not { } piece) return false;
            HudStateOf(piece).Text = SceneWords(c.Get("Text")); // {formulas} worked out now (U6GameWindow.U6Scene.cs)
            HudChanged();
            return true;
        };
        h[EventCommandType.U6MovePiece] = c =>
        {
            if (Hud.Named(c.Get("Piece")) is not { } piece) return false;
            var state = HudStateOf(piece);
            if (c.Get("Back") == "True") { state.X = null; state.Y = null; }
            else { state.X = (int)Math.Round(FormulaNumber(c, "X")); state.Y = (int)Math.Round(FormulaNumber(c, "Y")); }
            HudChanged();
            return true;
        };
        h[EventCommandType.U6EnablePanelKey] = c => SetPanelKey(c, on: true);
        h[EventCommandType.U6DisablePanelKey] = c => SetPanelKey(c, on: false);
    }

    private bool SetPanelKey(EventCommand c, bool on)
    {
        if (Hud.Named(c.Get("Piece")) is not { } panel || !Hud.IsCustomPanel(panel)) return false;
        HudStateOf(panel).KeyOff = !on;
        return true;
    }

    private void HudChanged()
    {
        BuildHud();
        DrawHudCursors();
    }

    /// <summary>A custom panel's key (HUD Editor; not switched off): it opens or closes. True when taken.</summary>
    private bool HudPanelKeyTook(Key pressed)
    {
        var now = Keyboard.Modifiers;
        foreach (var panel in Hud.CustomPanels)
        {
            if (panel.Key.Trim().Length == 0 || (_hudState.TryGetValue(panel.Id, out var s) && s.KeyOff)) continue;
            var (key, held) = U8KeyCapture.Parse(panel.Key.Trim());
            if (key != pressed) continue;
            if (held != ModifierKeys.None ? (now & held) != held : (now & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) continue;
            HudStateOf(panel).Shown = !HudPieceOwnShown(panel);
            HudChanged();
            return true;
        }
        return false;
    }

    /// <summary>A save: what the commands changed. A load: the same back.</summary>
    private List<U6HudPieceState> SavedHudPieces() => _hudState.Values.ToList();
    private void RestoreHudPieces(List<U6HudPieceState>? saved)
    {
        _hudState.Clear();
        foreach (var state in saved ?? new List<U6HudPieceState>()) _hudState[state.Id] = state;
    }
}
