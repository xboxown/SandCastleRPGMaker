// Ultima 6 engine (U6GameWindow): each member's combat mode, and each Command member's own turn - Ultima 6 projects only.
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6's combat modes (Nuvie InventoryView combat_mode_tbl; actor.lua worktypes): COMMAND, FRONT, REAR, FLANK, BERSERK,
/// RETREAT, ASSAULT. The avatar is always COMMAND; a member's mode is changed by clicking it in the panel (round the list),
/// or by the Ultima 6 tab's Set Combat Mode. In turn-by-turn combat every COMMAND member gets their own turn - the player
/// moves them, "Dupre:" in the text area (actor.lua: "swap player to next party member with 'command' combat worktype") -
/// and the others fight by their mode: FRONT the nearest foe, REAR holding back, FLANK from the side, BERSERK never
/// running, RETREAT running from foes, ASSAULT the strongest. Out of combat mode they fight live by the same mode.
/// </summary>
public partial class U6GameWindow
{
    private static readonly string[] U6CombatModes = { "Command", "Front", "Rear", "Flank", "Berserk", "Retreat", "Assault" };

    private void InitU6CombatModes()
    {
        _execContext.GameHandlers[EventCommandType.U6ChangePortrait] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
            if (c.Get("Own") == "True")
            {
                if (who.U6OwnFace.Length == 0) return true; // never changed
                var parts = who.U6OwnFace.Split('/', 2);
                who.FaceSpriteGroup = parts[0];
                who.FaceSpriteName = parts.Length > 1 ? parts[1] : string.Empty;
                who.U6OwnFace = string.Empty;
            }
            else
            {
                if (c.Get("SpriteName").Length == 0) return false;
                if (who.U6OwnFace.Length == 0) who.U6OwnFace = who.FaceSpriteGroup + "/" + who.FaceSpriteName;
                who.FaceSpriteGroup = c.Get("SpriteGroup");
                who.FaceSpriteName = c.Get("SpriteName");
            }
            RefreshHudPanel();
            return true;
        };
        _execContext.GameHandlers[EventCommandType.U6SetCombatMode] = c =>
        {
            if (CharacterDefinitionFor(c.Get("Who")) is not { } who || !U6CombatModes.Contains(c.Get("Mode"))) return false;
            if (who == Avatar && c.Get("Mode") != "Command") return false; // the avatar is always in Command
            SetU6CombatMode(who, c.Get("Mode"));
            return true;
        };
        foreach (var member in GetPartyList()) SetU6CombatMode(member, U6ModeOf(member));
    }

    /// <summary>A character's mode: the avatar Command; a member its own, Front till set.</summary>
    private string U6ModeOf(CharacterDefinition who) =>
        who == Avatar ? "Command" : U6CombatModes.Contains(who.U6CombatMode) ? who.U6CombatMode : "Front";

    private void SetU6CombatMode(CharacterDefinition who, string mode)
    {
        who.U6CombatMode = mode;
        // The fighting itself is the engine's (Combat_schedule), by the matching behaviour.
        who.AttackMode = mode switch
        {
            "Rear" => AttackMode.Defend,
            "Flank" => AttackMode.Flank,
            "Berserk" => AttackMode.Berserk,
            "Retreat" => AttackMode.Flee,
            "Assault" => AttackMode.Strongest,
            _ => AttackMode.Nearest
        };
        RefreshHudPanel();
    }

    /// <summary>The mode shown in the panel ("COMMAND"), as the words have it.</summary>
    private string U6ModeWord(CharacterDefinition who) => W("Mode" + U6ModeOf(who));

    /// <summary>A click on the panel's combat mode: the next mode (not the avatar's).</summary>
    private bool CombatModeClicked(Point onScreen)
    {
        if (Hud.First(HudPieceKind.CombatMode) is not { } piece || HudMember is not { } member) return false;
        if (!new Rect(piece.X, piece.Y, piece.Width, piece.Height).Contains(onScreen)) return false;
        if (_hudView != HudPanelView.Inventory && HudPiece.ViewOf(piece.Kind) != HudPanelView.Always && HudPiece.ViewOf(piece.Kind) != _hudView) return false;
        if (member == Avatar) return true; // "You can't change combat modes for the avatar."
        int i = Array.IndexOf(U6CombatModes, U6ModeOf(member));
        SetU6CombatMode(member, U6CombatModes[(i + 1) % U6CombatModes.Length]);
        return true;
    }

    /// <summary>Turn by turn: a Command member waits for the player's turn rather than fighting by itself.</summary>
    private bool WaitsForCommand(CharacterDefinition who) => TurnByTurn && !_soloMode && who.IsInParty && U6ModeOf(who) == "Command";

    /// <summary>The round is over: the next Command member (in party order, round again) is the one moved.</summary>
    private void NextCommander()
    {
        if (!TurnByTurn || _soloMode || _player is null) return;
        var commanders = GetPartyList().Where(m => !IsDead(m) && !IsIncapacitated(m) && U6ModeOf(m) == "Command" && (m == _player || MapCharacterOf(m) is not null)).ToList();
        if (commanders.Count <= 1) { if (commanders.Count == 1 && commanders[0] != _player) SoloSwapTo(commanders[0]); return; }
        int at = commanders.IndexOf(_player);
        var next = commanders[(at + 1) % commanders.Count];
        if (next != _player && SoloSwapTo(next)) _hudMember = next;
    }

    /// <summary>Combat over: the avatar is moved again (unless in solo mode) and the party follows it.</summary>
    private void CombatOverTakeAvatar()
    {
        if (_soloMode || !ControllingMember) return;
        SoloBackToAvatar();
        _hudMember = null;
        foreach (var member in _mapCharacters.Where(mc => mc.Character.IsInParty && !IsDead(mc.Character))) _followers[member] = _player!;
        RefreshHudPanel();
    }
}
