// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's own rules that need the engine, from Pentagram:
///  - Stats grow by use (MainActor::accumulateStr/Dex/Int), when System Settings > Ultima 8 turns it on: points build up by fighting and
///    climbing; each time a random chance, certain at the maker's number (650). Each stat stops at the character's cap (0: none).
///  - Eating and healing (HealProcess), when turned on: 1 HP and 1 mana a minute; hunger builds; eating gives back HP.
///  - Stumble Backwards when a blow hurts (Actor::receiveHitU8); a fall doing 6 or more knocks the Avatar down, stunned.
///  - Ledge hang (AvatarGravityProcess): holding a direction while falling, in front of a ledge. Jump climbs from it.
///  - Keep Balance (U8AvatarMoverProcess::step): a step off an edge wobbles first; the next one goes over.
/// Poses a character has no art for aren't played, and what needs its art doesn't happen.
/// </summary>
public partial class U8GameWindow
{
    private Ultima8Settings U8Rules => _systemSettings.Ultima8;

    // ---------- Stats grow by use ----------

    /// <summary>Builds up points toward a stat: only the main character and the party (Pentagram: the Avatar).</summary>
    private void BuildUpStat(CharacterDefinition who, BuiltInStatKind kind, int points)
    {
        if (!U8Rules.StatsGrowByUse || points <= 0 || !IsPartyOrPlayer(who) || IsDead(who)) return;
        int cap = kind switch { BuiltInStatKind.Strength => who.U8MaxStrength, BuiltInStatKind.Dexterity => who.U8MaxDexterity, _ => who.U8MaxIntelligence };
        int value = Stat(who, kind, 0);
        if (cap > 0 && value >= cap) return;
        var vitals = VitalsOf(who);
        int built = kind switch { BuiltInStatKind.Strength => vitals.BuiltStrength, BuiltInStatKind.Dexterity => vitals.BuiltDexterity, _ => vitals.BuiltIntelligence } + points;
        int need = Math.Max(1, U8Rules.PointsForStatGain);
        bool gained = built >= need || Rng.Next(need - built + 1) == 0;
        if (gained)
        {
            built = 0;
            SetStat(who, kind, value + 1);
            PlaySoundFrom(U8Rules.StatGainSoundId, who);
            if (who == _player || who.IsInParty) StatusText.Text = $"{who.Name} gains {kind} ({value + 1}).";
            SoakLog($"stat gain: {who.Name} {kind} {value + 1}");
        }
        switch (kind)
        {
            case BuiltInStatKind.Strength: vitals.BuiltStrength = built; break;
            case BuiltInStatKind.Dexterity: vitals.BuiltDexterity = built; break;
            default: vitals.BuiltIntelligence = built; break;
        }
    }

    /// <summary>A swing or a kick: attacking gives Strength 1-2, Dexterity 2-3 (U8AvatarMoverProcess).</summary>
    private void BuildUpFromSwing(CharacterDefinition who)
    {
        BuildUpStat(who, BuiltInStatKind.Strength, 1 + Rng.Next(2));
        BuildUpStat(who, BuiltInStatKind.Dexterity, 2 + Rng.Next(2));
    }

    /// <summary>A blow that landed: Dexterity by how much better the attacker is; Strength by the damage, unless a kick (Actor::receiveHitU8, calculateAttackDamage).</summary>
    private void BuildUpFromBlow(CharacterDefinition attacker, CharacterDefinition defender, int damage, bool kick)
    {
        int attack = Stat(attacker, BuiltInStatKind.Dexterity, 0), defend = Stat(defender, BuiltInStatKind.Dexterity, 0);
        BuildUpStat(attacker, BuiltInStatKind.Dexterity, attack > defend ? 2 * (attack - defend) : 2);
        if (!kick) BuildUpStat(attacker, BuiltInStatKind.Strength, damage / 4);
    }

    /// <summary>Climbing a ledge this many lifts up (2 is climb16): Strength 2 and up, Dexterity twice that.</summary>
    private void BuildUpFromClimb(int rise)
    {
        if (_player is null) return;
        int points = 2 + Math.Max(0, rise - 2);
        BuildUpStat(_player, BuiltInStatKind.Strength, points);
        BuildUpStat(_player, BuiltInStatKind.Dexterity, 2 * points);
    }

    private bool BuildUpStatCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        var kind = c.Get("Stat") switch { "Strength" => BuiltInStatKind.Strength, "Dexterity" => BuiltInStatKind.Dexterity, _ => BuiltInStatKind.Intelligence };
        BuildUpStat(who, kind, (int)FormulaNumber(c, "Points"));
        return true;
    }

    // ---------- Eating and healing ----------

    private const double U8HealPeriodMs = 60000; // HealProcess: 1800 frames at 30 a second
    private const int U8MostHunger = 200;

    /// <summary>HealProcess: for the main character and the party, mana at half the minute, health at the minute; hunger builds.</summary>
    private void UpdateU8Healing(double elapsedMs)
    {
        foreach (var who in PartyAndPlayer().ToList())
        {
            if (IsDead(who)) continue;
            var vitals = VitalsOf(who);
            double before = vitals.HealClockMs;
            vitals.HealClockMs += elapsedMs;
            if (before < U8HealPeriodMs / 2 && vitals.HealClockMs >= U8HealPeriodMs / 2)
            {
                int mana = Stat(who, BuiltInStatKind.Mana, 0);
                if (mana < Stat(who, BuiltInStatKind.Magic, 0)) SetStat(who, BuiltInStatKind.Mana, mana + 1);
            }
            if (vitals.HealClockMs < U8HealPeriodMs) continue;
            vitals.HealClockMs = 0;
            if (GetHp(who) is int hp && hp < MaxHp(who)) SetHp(who, hp + 1);
            if (vitals.U8Hunger < U8MostHunger) vitals.U8Hunger++;
            SoakLog($"heal minute: {who.Name} hp {GetHp(who)} hunger {vitals.U8Hunger}");
        }
    }

    /// <summary>HealProcess::feedAvatar: eating takes hunger off, and gives back a quarter of it as health.</summary>
    private void U8Feed(CharacterDefinition who, int food)
    {
        var vitals = VitalsOf(who);
        food = Math.Min(Math.Max(0, food), vitals.U8Hunger);
        if (food == 0) return;
        int old = vitals.U8Hunger;
        vitals.U8Hunger -= food;
        if (GetHp(who) is int hp) SetHp(who, Math.Min(MaxHp(who), hp - vitals.U8Hunger / 4 + old / 4));
    }

    // ---------- Hurt ----------

    private double _u8StunMs;
    private bool U8Stunned => _u8StunMs > 0;

    /// <summary>Actor::receiveHitU8: a blow that hurt and didn't kill: Stumble Backwards, if it has the art. Not while blocking or knocked down.</summary>
    private void StumbleFromHit(CharacterDefinition who)
    {
        if (IsDead(who) || IsIncapacitated(who) || (who == _player && (_u8Blocking || U8Stunned || _jump is not null)) || !HasArtFor(who, "Stumble Backwards")) return;
        ShowPose(who, "Stumble Backwards", restart: true);
        double ms = PoseLengthMs(who, "Stumble Backwards");
        if (who == _player) _playerPoseResetMs = ms;
        else if (MapCharacterOf(who) is { } mapCharacter) mapCharacter.Npc.WaitMs = Math.Max(mapCharacter.Npc.WaitMs, ms);
        SoakLog($"stumble: {who.Name}");
    }

    /// <summary>Actor::receiveHitU8: a fall doing 6 or more: down, stunned while Fall Backwards plays. True: knocked down (else it just lands).</summary>
    private bool KnockedDownByFall(int damage)
    {
        if (_player is null || damage < 6 || IsIncapacitated(_player) || !HasArtFor(_player, "Fall Backwards")) return false;
        ShowPose(_player, "Fall Backwards", restart: true);
        _u8StunMs = Math.Max(300, PoseLengthMs(_player, "Fall Backwards"));
        _heldDirectionKeys.Clear();
        SoakLog("knocked down by a fall");
        return true;
    }

    private void TickU8Stun(double deltaSeconds)
    {
        if (!U8Stunned) return;
        _u8StunMs -= deltaSeconds * 1000;
        if (U8Stunned || _player is null || IsIncapacitated(_player)) return;
        ApplyPoseSprite(U8RestPose());
    }

    // ---------- Ledge hang ----------

    private bool _u8Hanging;
    private double _u8HangToX, _u8HangToY;
    private int _u8HangTop;

    /// <summary>
    /// AvatarGravityProcess: falling with a direction held and a ledge in front within the climb's reach (climb40: 5 lifts): the fall stops and
    /// they hang. Let go: the fall goes on. Called before the fall moves them each frame; true: hanging.
    /// </summary>
    private bool HoldingALedge()
    {
        bool holding = _player is not null && _u8FallFrom is not null && !IsIncapacitated(_player)
            && (_heldDirectionKeys.Count > 0 || (_rightMouseHeld && Mouse.RightButton == MouseButtonState.Pressed));
        if (!holding) { _u8Hanging = false; return false; }
        if (_u8Hanging) return true;
        var (dx, dy) = VectorOf(_playerFacing);
        double len = Math.Sqrt(dx * dx + dy * dy), x = _playerPixelX + dx / len * _tileWidth, y = _playerPixelY + dy / len * _tileHeight;
        if (x < 0 || y < 0 || x >= _map.Width * _tileWidth || y >= _map.Height * _tileHeight || !HasArtFor(_player!, "Hang")) return false;
        var foot = PlayerFootprintAt(x, y);
        int top = StandingLiftAt(foot, _player);
        double rise = top - _playerVisualLiftZ;
        if (top <= _playerZ || rise <= 0 || rise > 5 || !TryFlyStep(foot, top, _playerSize.Z, null, null, out _, out _)) return false;
        (_u8Hanging, _u8HangToX, _u8HangToY, _u8HangTop) = (true, x, y, top);
        ApplyPoseSprite(PoseByName("Hang"));
        _playerFallVelocity = 0;
        SoakLog($"hanging: ledge at lift {top}");
        return true;
    }

    /// <summary>Jump while hanging: up onto the ledge.</summary>
    private void ClimbFromHang()
    {
        int from = (int)Math.Round(_playerVisualLiftZ);
        _u8Hanging = false;
        _u8FallFrom = null;
        BuildUpFromClimb(_u8HangTop - from);
        _jump = new U8Jump { FromX = _playerPixelX, FromY = _playerPixelY, ToX = _u8HangToX, ToY = _u8HangToY, Duration = 0.5, FromLift = from, ToLift = _u8HangTop, Arc = 1 };
        ApplyPoseSprite(PoseByName("Climb Up") ?? PoseByName("Climb") ?? _idlePose);
    }

    // ---------- Keep Balance ----------

    private bool _u8Balanced;

    /// <summary>U8AvatarMoverProcess::step: the step ends off the land: Keep Balance instead, once; the step after that goes over. True: balancing.</summary>
    private bool BalancingAtEdge(int dx, int dy)
    {
        if (_player is null || _u8Balanced) { _u8Balanced = false; return false; }
        double len = Math.Sqrt(dx * dx + dy * dy), reach = U8StepUnits * _tileWidth / 128.0;
        double x = _playerPixelX + dx / len * reach, y = _playerPixelY + dy / len * reach;
        if (x < 0 || y < 0 || x >= _map.Width * _tileWidth || y >= _map.Height * _tileHeight) return false;
        if (_playerZ - StandingLiftAt(PlayerFootprintAt(x, y), _player) <= ActorMaxDrop || !HasArtFor(_player, "Keep Balance")) return false;
        _u8Balanced = true;
        _currentAppliedPose = null;
        ApplyPoseSprite(PoseByName("Keep Balance"));
        _u8Step = new U8StepMove { Dx = dx, Dy = dy, TurnOnly = true, DurationMs = Math.Max(150, PoseLengthMs(_player, "Keep Balance")) };
        SoakLog("keep balance");
        return true;
    }
}
