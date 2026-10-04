using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Knocked out, ported from Exult (actors.h is_knocked_out / is_dying,
/// actors.cc reduce_health, combat.cc stop_attacking_npc / find_opponents,
/// npctime.cc Npc_sleep_timer):
///  - Health at 0 or below: knocked out - lying in the Unconscious pose,
///    unable to do anything, and everyone attacking it stops (picks a new
///    target).
///  - Health below -(Strength / 3): dead (-10 at Strength 30, -5 at 15).
///  - Enemies go for opponents who are awake first; only when none are left
///    do they turn on the unconscious - so a party still standing draws the
///    attack, and someone alone keeps being beaten. The player never attacks
///    the unconscious.
///  - While at 0 HP or below: a 1 in 6 chance every half minute to mend
///    (about 1 HP every 3 minutes); healing of any kind counts. Back at 1 HP
///    or more, it gets up and carries on.
///  - The main character, knocked out, can still identify things (single
///    click) but can't move, use, drag, talk or open anything.
/// </summary>
public partial class GameTestWindow
{
    private const int MendCheckMs = 30000;

    private bool IsUnconscious(CharacterDefinition? character) =>
        character is not null && _vitals.TryGetValue(character, out var v) && v.Unconscious && !v.Dead;

    private bool PlayerKnockedOut => IsUnconscious(_player);

    /// <summary>is_dying: health below -(Strength / 3).</summary>
    private bool IsDying(CharacterDefinition character, int hp) => hp < -(MaxHp(character) / 3);

    /// <summary>reduce_health, at 0 HP or below and not dying: Combat_schedule::stop_attacking_npc, then lie down asleep.</summary>
    private void KnockOut(CharacterDefinition character)
    {
        var vitals = VitalsOf(character);
        if (vitals.Unconscious) return;
        vitals.Unconscious = true;
        vitals.MendCheckMs = MendCheckMs;

        // Everyone attacking it stops and looks for someone else.
        foreach (var mapCharacter in _mapCharacters.Where(mc => mc.Npc.Target == character)) mapCharacter.Npc.Target = null;
        if (_playerTarget == character) _playerTarget = null;

        if (character == _player)
        {
            _heldDirectionKeys.Clear();
            _wasMovingLastFrame = false;
            _playerTarget = null;
            _playerTargetThing = null;
            CancelPointer();
            _choosingTarget = null;
            ApplyPoseSprite(PoseByName("Unconscious") ?? PoseByName("Sleeping") ?? _idlePose);
            StatusText.Text = "You have been knocked unconscious!";
            return;
        }
        if (MapCharacterOf(character) is not { } knockedOut) return;
        knockedOut.Npc.Path = null;
        SetCharacterPose(knockedOut, "Unconscious", knockedOut.Facing);
        StatusText.Text = $"{character.Name} is knocked unconscious.";
    }

    /// <summary>Npc_sleep_timer for the knocked out: mending now and then, and getting up once back at 1 HP or more.</summary>
    private void UpdateUnconscious(CharacterDefinition character, double elapsedMs)
    {
        var vitals = VitalsOf(character);
        if (!vitals.Unconscious || vitals.Dead) return;
        int hp = GetHp(character) ?? 1;
        if (hp <= 0 && (vitals.MendCheckMs -= elapsedMs) <= 0)
        {
            vitals.MendCheckMs = MendCheckMs;
            if (Rng.Next(6) == 0) MendWounds(character); // "1 in 6 every half minute = approx. 1 HP every 3 min."
            hp = GetHp(character) ?? 1;
        }
        if (hp < 1) return; // "Don't wake up someone beaten into unconsciousness."
        vitals.Unconscious = false;
        if (character == _player) StatusText.Text = "You come to and get back up.";
        if (!IsIncapacitated(character)) ResumeActing(character); // still asleep or paralyzed? stays down
    }

    /// <summary>find_opponents: of those it would fight, the ones awake - or, with none awake, the unconscious.</summary>
    private IEnumerable<CharacterDefinition> AwakeFirst(IEnumerable<CharacterDefinition> foes)
    {
        var list = foes.ToList();
        var awake = list.Where(f => !IsUnconscious(f)).ToList();
        return awake.Count > 0 ? awake : list;
    }
}
