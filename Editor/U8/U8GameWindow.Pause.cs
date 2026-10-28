// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Exult's "gumps pause the game" (Gump_manager::add_gump / remove_gump,
/// default gumps_dont_pause_game = no): while ANY gump-mode window is open -
/// a paperdoll, its backpack, a container, a book being read - the game's
/// time queue is paused. Everything driven by time freezes on its current
/// frame: map animations, the player's own animation, timed events (By
/// Stay), and - once they exist - NPC walking and schedules. The avatar
/// can't walk (Game_window::start_actor returns early in gump mode). Closing
/// the last one resumes everything exactly where it stopped.
///
/// What still works while paused, as in Exult: dragging and dropping,
/// identifying, using items, and the gumps themselves (their own animations
/// keep playing - they're the interface, not the game world).
///
/// Rules the NPC system must follow with IsGamePaused (from Exult's source):
/// - NPCs don't move, animate or run schedules (they live on the paused time queue).
/// - No attacking by double-click (Game_window::double_clicked only enters
///   combat when !gump_mode()).
/// - Double-clicking a party member opens their inventory instead of
///   talking (Actor::activate: show_party_inv = showing_gumps()).
/// - Double-clicking any other NPC still runs its conversation - Exult does
///   not block talking while gumps are open.
/// - Using an item on an NPC still works.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>Every animator that belongs to the game world (map items/objects), keyed by its visual so it can be stopped when that visual leaves the map. Gump animators are not here - the interface keeps animating while the game is paused.</summary>
    private readonly Dictionary<UIElement, SpriteAnimator> _worldAnimators = new();
    private bool _gamePaused;

    /// <summary>True while any gump-mode window is open - Exult's gump_mode() with the default "gumps pause game".</summary>
    private bool IsGamePaused => _openPaperdolls.Count > 0 || _openStatsDolls.Count > 0 || _openContainers.Count > 0 || _openDolls.Count > 0 || _isReadingBook || IsShowingMessage || _gameOver || IsChoosingTarget || _objectOverlayOpen || _restingNow || IsPlayingVideo || _scriptInput is not null; // Get_click pauses the time queue while a target is chosen

    /// <summary>Checked every frame by the game loop: pauses or resumes the game the moment the first gump opens or the last one closes (Time_queue::pause / resume).</summary>
    private void UpdateGamePause()
    {
        bool paused = IsGamePaused;
        if (paused == _gamePaused) return;
        _gamePaused = paused;

        foreach (var animator in _worldAnimators.Values)
        {
            if (paused) animator.Pause(); else animator.Resume();
        }
        if (paused) _playerAnimator?.Pause(); else _playerAnimator?.Resume();
        if (paused) _stayTimer?.Stop(); else _stayTimer?.Start();

        if (paused)
        {
            // Exult stops the avatar where it stands (start_actor refuses in gump mode): drop any held direction and settle into Idle.
            _heldDirectionKeys.Clear();
            if (_wasMovingLastFrame)
            {
                ApplyPoseSprite(_idlePose);
                _wasMovingLastFrame = false;
            }
            _playerAnimator?.Pause(); // ApplyPoseSprite builds a fresh animator - freeze that one too
        }
    }

    /// <summary>
    /// Game Test closing: every animation it started stops for good - the
    /// map's, the player's, and every open gump's. Left running, their timers
    /// kept ticking invisibly for as long as the editor stayed open (and kept
    /// playing any per-frame sounds), one more set after every Game Test -
    /// until Windows ran out of sound channels and every sound failed.
    /// </summary>
    private void StopAllAnimators()
    {
        foreach (var animator in _worldAnimators.Values) animator.Stop();
        _worldAnimators.Clear();
        _playerAnimator?.Stop();
        foreach (var gump in _openPaperdolls) foreach (var animator in gump.ItemAnimators) animator.Stop();
        foreach (var gump in _openContainers) foreach (var animator in gump.ItemAnimators) animator.Stop();
        foreach (var gump in _openDolls) foreach (var animator in gump.Animators) animator.Stop();
        _bookClose = null;
        _bookNextPage = null;
    }

    /// <summary>A map visual's animator is now part of the game world - frozen while paused like everything else.</summary>
    private void RegisterWorldAnimator(UIElement visual, SpriteAnimator animator)
    {
        _worldAnimators[visual] = animator;
        animator.Hearing = () => HearingOf(visual); // its frame sounds from where it is (U8GameWindow.Hearing)
        if (_gamePaused) animator.Pause();
    }

    /// <summary>A map visual left the world - its animator stops for good.</summary>
    private void UnregisterWorldAnimator(UIElement visual)
    {
        if (_worldAnimators.Remove(visual, out var animator)) animator.Stop();
    }
}
