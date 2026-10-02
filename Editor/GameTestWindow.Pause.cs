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
public partial class GameTestWindow
{
    /// <summary>Every animator that belongs to the game world (map items/objects), keyed by its visual so it can be stopped when that visual leaves the map. Gump animators are not here - the interface keeps animating while the game is paused.</summary>
    private readonly Dictionary<UIElement, SpriteAnimator> _worldAnimators = new();
    private bool _gamePaused;

    /// <summary>True while any gump-mode window is open - Exult's gump_mode() with the default "gumps pause game".</summary>
    private bool IsGamePaused => _openPaperdolls.Count > 0 || _openContainers.Count > 0 || _isReadingBook;

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

    /// <summary>A map visual's animator is now part of the game world - frozen while paused like everything else.</summary>
    private void RegisterWorldAnimator(UIElement visual, SpriteAnimator animator)
    {
        _worldAnimators[visual] = animator;
        if (_gamePaused) animator.Pause();
    }

    /// <summary>A map visual left the world - its animator stops for good.</summary>
    private void UnregisterWorldAnimator(UIElement visual)
    {
        if (_worldAnimators.Remove(visual, out var animator)) animator.Stop();
    }
}
