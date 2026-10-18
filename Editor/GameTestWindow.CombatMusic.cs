using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The music around fights (System Settings > Combat), as Exult plays it
/// (combat.cc Combat_schedule::start_music_combat, danger_music, start_battle,
/// monster_died, ending):
///  - Danger: not in combat mode, and an enemy close by (on screen) is
///    fighting (Game_window::is_hostile_nearby) - checked every second.
///    A blow landing on the main character or the party while it plays
///    switches to the battle music.
///  - Battle: the main character's battle begins in combat mode (one of the
///    two pieces at random) - at most once every 30 seconds, so the danger
///    music can be heard.
///  - Run away: combat mode turned off with a foe within 18 tiles.
///  - Escaped: the enemies are out of sight while danger or battle music plays.
///  - Victory / Battle over: the main character or a party member kills an
///    enemy and none is left close by; a fight over 15 seconds may end with
///    Battle over instead (the longer, the likelier).
/// Exult plays each of these once (never looping); each has a Loop box here.
/// Not Exult: Near death (below a quarter of its health with an enemy
/// around) and Death (until the Save/Load screen opens or a game is loaded).
/// Combat music replaces the map's ambient sound (birds, crickets), as in
/// Exult one music track replaces the other; weather (rain, thunder) plays on.
/// The ambient also stays off while the main character battles in combat mode.
/// </summary>
public partial class GameTestWindow
{
    private enum CombatCue { None, Danger, Battle, RunAway, Escaped, Victory, BattleOver, NearDeath, Death }

    private readonly object _combatMusicOwner = new();
    private CombatCue _combatCue;
    /// <summary>Game::get_ticks for the music, in ms.</summary>
    private double _musicClockMs;
    /// <summary>Combat_schedule::battle_time / battle_end_time (static): when the last battle began and ended. Ended at or after it began = no battle raging.</summary>
    private double _battleTimeMs = -30000, _battleEndTimeMs;
    /// <summary>Combat_schedule::started_battle for the main character: set once its battle begins, cleared when combat mode is turned on.</summary>
    private bool _startedBattle;
    private bool _nearDeath;
    private double _dangerCheckMs;

    private CombatMusicSettings CombatMusic => _systemSettings.Combat.Music;

    /// <summary>A piece of combat music still playing (one that played once and ended is none).</summary>
    private bool CombatMusicPlaying()
    {
        if (_combatCue != CombatCue.None && !SoundPlayback.IsPlayingFor(_combatMusicOwner)) _combatCue = CombatCue.None;
        return _combatCue != CombatCue.None;
    }

    /// <summary>The map's ambient sound is silenced: combat music is playing, or the main character is battling.</summary>
    private bool AmbientSilencedByCombat() => CombatMusicPlaying() || (_combatMode && _startedBattle && (_playerTarget is not null || HostileNearby()));

    private CombatMusicCue? CueSettings(CombatCue cue)
    {
        var m = CombatMusic;
        return cue switch
        {
            CombatCue.Danger => m.Danger,
            // CSAttacked1 or CSAttacked2 at random (just the one, if only one is set)
            CombatCue.Battle => m.Battle.MusicId < 0 ? m.Battle2 : m.Battle2.MusicId < 0 || Rng.Next(2) == 0 ? m.Battle : m.Battle2,
            CombatCue.RunAway => m.RunAway,
            CombatCue.Escaped => m.Escaped,
            CombatCue.Victory => m.Victory,
            CombatCue.BattleOver => m.BattleOver,
            CombatCue.NearDeath => m.NearDeath,
            CombatCue.Death => m.Death,
            _ => null
        };
    }

    /// <summary>Starts a piece of combat music in place of whatever combat music was playing; none set = nothing changes.</summary>
    private void PlayCombatCue(CombatCue cue, CombatMusicCue? settings = null)
    {
        settings ??= CueSettings(cue);
        if (settings is null || settings.MusicId < 0) return;
        SoundPlayback.StopFor(_combatMusicOwner);
        SilenceAmbient();
        if (SoundPlayback.TryPlayFor(_combatMusicOwner, _resources, settings.MusicId, settings.Loop)) _combatCue = cue;
        else _combatCue = CombatCue.None;
    }

    private void StopCombatMusic()
    {
        SoundPlayback.StopFor(_combatMusicOwner);
        _combatCue = CombatCue.None;
    }

    /// <summary>An enemy close by is fighting (Game_window::is_hostile_nearby: on screen, not dead, evil, in combat with its battle begun).</summary>
    private bool HostileNearby()
    {
        if (_player is null) return false;
        foreach (var mc in _mapCharacters)
        {
            var npc = mc.Npc;
            if (IsDead(mc.Character) || mc.Character.IsInParty || !npc.InCombat || npc.Fleeing || npc.Target is not { } target) continue;
            bool hostile = Attitude(mc.Character, _player) == FactionBehavior.Attack || target == _player || target.IsInParty;
            if (hostile && IsOnScreen(mc.PixelX, mc.PixelY)) return true;
        }
        return false;
    }

    // ---------- Each frame ----------

    private void UpdateCombatMusic(double elapsedMs)
    {
        _musicClockMs += elapsedMs;
        if (_player is null) return;

        // Not Exult: near death - below a quarter of its health, with an enemy around.
        int maxHp = MaxHp(_player);
        bool nearDeath = maxHp > 0 && GetHp(_player) is int hp && hp < maxHp / 4 && (_combatMode || HostileNearby());
        if (nearDeath != _nearDeath)
        {
            _nearDeath = nearDeath;
            if (nearDeath) PlayCombatCue(CombatCue.NearDeath);
            else if (_combatCue == CombatCue.NearDeath) StopCombatMusic(); // recovered, or the danger is gone
        }
        if (_nearDeath && _combatCue == CombatCue.NearDeath && CombatMusicPlaying()) return; // nothing else while it plays

        if (_combatMode) StartBattle();

        // Background_noise checks danger_music about once a second.
        _dangerCheckMs -= elapsedMs;
        if (_dangerCheckMs > 0) return;
        _dangerCheckMs = 1000;
        DangerMusic();
    }

    /// <summary>Combat_schedule::danger_music.</summary>
    private void DangerMusic()
    {
        if (_combatMode) return; // in combat: the battle music handles it
        bool hostile = HostileNearby();
        if (hostile && !CombatMusicPlaying()) PlayCombatCue(CombatCue.Danger);
        else if (!hostile && CombatMusicPlaying() && _combatCue is CombatCue.Danger or CombatCue.Battle) PlayCombatCue(CombatCue.Escaped);
    }

    /// <summary>Combat_schedule::start_battle for the main character: battle music if it's been half a minute since the last.</summary>
    private void StartBattle()
    {
        if (_startedBattle || (_playerTarget is null && !HostileNearby())) return; // no foe yet
        if (_musicClockMs - _battleTimeMs >= 30000)
        {
            PlayCombatCue(CombatCue.Battle);
            _battleTimeMs = _musicClockMs;
            _battleEndTimeMs = _musicClockMs - 1;
        }
        _startedBattle = true;
    }

    // ---------- Moments ----------

    /// <summary>Combat mode turned on (a new Combat_schedule) or off (Combat_schedule::ending: running from a foe close by).</summary>
    private void CombatModeChangedMusic()
    {
        if (_combatMode) { _startedBattle = false; return; }
        _startedBattle = false;
        if (_player is null || IsDead(_player)) return;
        foreach (var mc in _mapCharacters)
        {
            if (IsDead(mc.Character) || mc.Character.IsInParty) continue;
            bool foe = Attitude(mc.Character, _player) == FactionBehavior.Attack || (mc.Npc.InCombat && (mc.Npc.Target == _player || mc.Npc.Target?.IsInParty == true));
            if (foe && TilesBetween(_player, mc.Character) < 40 / 2 - 2 && CanSee(_player, mc.Character)) // c_screen_tile_size / 2 - 2
            {
                PlayCombatCue(CombatCue.RunAway);
                return;
            }
        }
    }

    /// <summary>A blow landed on the main character or a party member: from the danger music to the battle music (combat.cc attack_target, CSAttacked1).</summary>
    private void PartyHitMusic(CharacterDefinition defender)
    {
        if ((defender == _player || defender.IsInParty) && _combatCue == CombatCue.Danger && CombatMusicPlaying())
            PlayCombatCue(CombatCue.Battle, CombatMusic.Battle.MusicId >= 0 ? CombatMusic.Battle : CombatMusic.Battle2);
    }

    /// <summary>Combat_schedule::monster_died: the main character or the party killed an enemy - with none left close by, victory (or, after a long fight, battle over).</summary>
    private void EnemyKilledMusic(CharacterDefinition victim, CharacterDefinition? killer)
    {
        if (_player is null || killer is null || (killer != _player && !killer.IsInParty) || victim.IsInParty) return;
        if (Attitude(victim, _player) != FactionBehavior.Attack) return; // only an enemy
        if (_battleEndTimeMs >= _battleTimeMs) return; // no battle raging
        foreach (var mc in _mapCharacters)
        {
            if (mc.Character == victim || IsDead(mc.Character) || !IsOnScreen(mc.PixelX, mc.PixelY)) continue;
            if (mc.Npc.Fleeing || mc.Character.AttackMode == AttackMode.Flee) continue;
            if (Attitude(mc.Character, _player) == FactionBehavior.Attack) return; // still possible enemies
        }
        _battleEndTimeMs = _musicClockMs;
        double seconds = (_battleEndTimeMs - _battleTimeMs) / 1000;
        bool hard = seconds > 15 && Rng.Next(60) < seconds;
        PlayCombatCue(hard ? CombatCue.BattleOver : CombatCue.Victory);
    }

    /// <summary>The main character died: its death music, until the Save/Load screen opens or a game is loaded.</summary>
    private void DeathMusic()
    {
        _nearDeath = false;
        StopCombatMusic();
        PlayCombatCue(CombatCue.Death);
    }

    /// <summary>The Save/Load screen opened: the death music ends.</summary>
    private void SaveScreenOpenedMusic()
    {
        if (_combatCue == CombatCue.Death) StopCombatMusic();
    }
}
