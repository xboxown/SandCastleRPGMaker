// Ultima 6 engine (U6GameWindow): combat turn by turn once the combat key is pressed, and every blow told in the text area.
using SandcastleRpgMaker.Engine;
using System.Windows.Input;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6 combat, the way the user asked: outside combat mode the world is live and characters fight each other live; the
/// combat key (Ultima 6: B, begin / break off combat) makes it turn by turn. Then the world waits for the avatar: a step, a
/// verb (Attack, Use...), or Pass (Space) plays one round - the party and the foes act in it - and the world waits again,
/// "Andrew:" in the text area. Every blow in sight is told there, as Ultima 6: "Rat attacks Peter.", "Missed!", "Peter lightly
/// wounded." The words are in System Settings > Ultima 6 > Words.
/// </summary>
public partial class U6GameWindow
{
    /// <summary>What's left of the round being played, in ms; 0 or less: the world waits for the avatar's turn.</summary>
    private double _turnRunMs;
    /// <summary>A step's round: the cell it started from - the step ends at the next cell.</summary>
    private (int X, int Y)? _turnStepFrom;
    private const double RoundMs = 700, AttackRoundMs = 1100, StepRoundMs = 600;
    /// <summary>This round is an Attack turn: the avatar may strike in it (otherwise it doesn't, as Ultima 6's Command mode).</summary>
    private bool _turnIsAttack;

    /// <summary>Turn by turn: in combat mode with the HUD's screen (an Ultima 6 game).</summary>
    private bool TurnByTurn => _combatMode && Hud.First(HudPieceKind.GameView) is not null;

    /// <summary>The game loop's question each frame: true while the world waits for the avatar's turn (nothing in it moves).</summary>
    private bool TurnFrozenFrame(double elapsedMs)
    {
        if (!TurnByTurn) { _turnRunMs = 0; _turnStepFrom = null; return false; }
        if (_turnRunMs <= 0) return true;
        if (_turnStepFrom is { } from && (_playerX, _playerY) != from) { _heldDirectionKeys.Clear(); _turnStepFrom = null; } // one cell a step
        _turnRunMs -= elapsedMs;
        if (_turnRunMs <= 0) EndRound();
        return false;
    }

    private void StartRound(double ms, bool attack = false)
    {
        if (!TurnByTurn) return;
        _turnRunMs = ms;
        _turnIsAttack = attack;
        SoakLog($"turn: a round of {ms} ms from {_playerX},{_playerY}");
        _hudPrompt = string.Empty;
        RefreshHudText();
    }

    /// <summary>The round is over: the avatar stops, and its turn is asked for.</summary>
    private void EndRound()
    {
        _turnRunMs = 0;
        _turnIsAttack = false;
        _heldDirectionKeys.Clear();
        _turnStepFrom = null;
        SoakLog($"turn: round over, the avatar at {_playerX},{_playerY}");
        if (_wasMovingLastFrame) { ApplyPoseSprite(_idlePose); _wasMovingLastFrame = false; }
        NextCommander(); // the next Command member's turn (U6GameWindow.U6CombatModes.cs)
        AskTurn();
    }

    private void AskTurn()
    {
        if (!TurnByTurn || _hudVerb is not null) return;
        _hudPrompt = W("TurnOf", "", _player?.Name);
        RefreshHudText();
    }

    /// <summary>A step in turn-by-turn combat: the arrow held for one cell, the round played.</summary>
    private bool TurnStep(Key[] arrows)
    {
        if (!TurnByTurn) return false;
        if (_turnRunMs > 0) return true; // the round is still being played
        foreach (var arrow in arrows) _heldDirectionKeys.Add(arrow);
        _lastDirectionKeyPressed = arrows[^1];
        _turnStepFrom = (_playerX, _playerY);
        StartRound(StepRoundMs);
        return true;
    }

    /// <summary>A verb done in turn-by-turn combat: its round is played (Attack: long enough for a blow).</summary>
    private void PassTurnIfFighting(bool attack = false)
    {
        if (TurnByTurn && _turnRunMs <= 0) StartRound(attack ? AttackRoundMs : RoundMs, attack);
    }

    /// <summary>Pass (Space with no verb): the round goes by.</summary>
    private bool TurnPass()
    {
        if (!TurnByTurn || _turnRunMs > 0) return TurnByTurn;
        U6Say(W("Pass"));
        StartRound(RoundMs);
        return true;
    }

    // ---------- Telling the blows ----------

    /// <summary>On the screen now (a blow there is told; one far away isn't).</summary>
    private bool InView(CharacterDefinition who)
    {
        if (who == _player || who.IsInParty) return true;
        var (x, y) = PixelOf(who);
        return !double.IsNaN(x) && x >= _cameraX && x <= _cameraX + ViewportWidth && y >= _cameraY && y <= _cameraY + ViewportHeight;
    }

    private void TellAttack(CharacterDefinition attacker, CharacterDefinition defender, bool hit)
    {
        if (Hud.First(HudPieceKind.TextArea) is null || (!InView(attacker) && !InView(defender))) return;
        U6Say(W("Attacks", defender.Name, attacker.Name));
        if (!hit) U6Say(W("Missed"));
    }

    /// <summary>How badly it was hurt, as Ultima 6 tells it - by what's left of its hit points.</summary>
    private void TellWound(CharacterDefinition who, int hpLeft)
    {
        if (Hud.First(HudPieceKind.TextArea) is null || !InView(who)) return;
        double left = hpLeft / (double)Math.Max(1, MaxHp(who));
        string id = IsDying(who, hpLeft) ? "Killed" : hpLeft <= 0 ? "KnockedOut" : left >= 0.75 ? "Barely" : left >= 0.5 ? "Lightly" : left >= 0.25 ? "Heavily" : "Critical";
        U6Say(W(id, who.Name));
    }
}
