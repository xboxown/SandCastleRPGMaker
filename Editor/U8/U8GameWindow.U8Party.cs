// Ultima 8 engine (U8GameWindow) only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The party in an Ultima 8 project:
///  - The most who can join (System Settings > Ultima 8 > The party): one more asking says its "Party is full" words
///    (Character Editor > Verbose) and doesn't join.
///  - Members keep pace up and down: where the player climbed a ledge, jumped up or down, or dropped off one, a member
///    left below (or above) walks to the same spot, waits till the player has moved off the top, and climbs (jumps,
///    climbs down) the same way - no fall hurts them doing it.
/// </summary>
public partial class U8GameWindow
{
    // ---------- How many may join ----------

    /// <summary>Join Party's check: false (and its words said) when the party is full.</summary>
    private bool PartyHasRoomFor(CharacterDefinition joining)
    {
        int most = U8Rules.MaxPartyMembers;
        if (most <= 0 || _characters is null) return true;
        var main = MainCharacter;
        int members = _characters.Characters.Count(c => c.IsInParty && c != main && c != joining && !IsDead(c));
        if (members < most) return true;
        if (!Bark(joining, VerboseKind.PartyFull)) ShowFloatingText(joining, VerboseLines.Defaults(VerboseKind.PartyFull)[0], 3000);
        StatusText.Text = $"The party is full ({most}): {joining.Name} doesn't join.";
        SoakLog($"party full: {joining.Name} ({members} of {most})");
        return false;
    }

    // ---------- Climbing after the player ----------

    /// <summary>Where the player went up or down: from a spot to a spot (map pixels) and lift.</summary>
    private sealed record Leap(double FromX, double FromY, int FromLift, double ToX, double ToY, int ToLift);

    private sealed class Leaping
    {
        public required Leap Leap;
        public double StartX, StartY, T, Duration;
        public int StartLift;
    }

    private readonly List<Leap> _leaps = new();
    private readonly Dictionary<MapCharacter, Leaping> _leaping = new(ReferenceEqualityComparer.Instance);
    private (double X, double Y, int Lift)? _lastGround;

    /// <summary>A change of level a walker can't make (2 lifts or more): kept, the last few, for the party to follow.</summary>
    private void NoteLeap(double fromX, double fromY, int fromLift, double toX, double toY, int toLift)
    {
        if (Math.Abs(toLift - fromLift) < 2) return;
        _leaps.Add(new Leap(fromX, fromY, fromLift, toX, toY, toLift));
        if (_leaps.Count > 8) _leaps.RemoveAt(0);
        SoakLog($"leap: {fromLift} to {toLift}");
    }

    /// <summary>Each frame: the player's last spot on the ground (the top of a ledge before walking off it).</summary>
    private void TrackLastGround()
    {
        if (_player is null || _jump is not null || _u8FallFrom is not null || Math.Abs(_playerVisualLiftZ - _playerZ) > 0.01) return;
        _lastGround = (_playerPixelX, _playerPixelY, _playerZ);
    }

    /// <summary>Walked off a ledge and landed: a leap down from where it last stood.</summary>
    private void NoteDropLanded()
    {
        if (_lastGround is { } g) NoteLeap(g.X, g.Y, g.Lift, _playerPixelX, _playerPixelY, _playerZ);
    }

    /// <summary>
    /// A follower of the player on another level: to the foot (or top) of where the player went, then up (or down) the same way.
    /// True: it's seen to (walking there, waiting or climbing); false: it follows as usual.
    /// </summary>
    private bool FollowByLeap(MapCharacter follower)
    {
        if (_leaping.ContainsKey(follower)) return true;
        if (_player is null || IsFlier(follower)) return false;
        int at = follower.Lift, goal = _playerZ;
        if (Math.Abs(at - goal) <= 1) return false;
        int way = Math.Sign(goal - at);
        var leap = _leaps.LastOrDefault(l => Math.Abs(l.FromLift - at) <= 1 && Math.Sign(l.ToLift - l.FromLift) == way);
        if (leap is null) return false;
        var fromCell = ((int)Math.Floor(leap.FromX / _tileWidth), (int)Math.Floor(leap.FromY / _tileHeight));
        bool atFoot = Math.Abs(follower.PixelX - leap.FromX) <= _tileWidth * 0.75 && Math.Abs(follower.PixelY - leap.FromY) <= _tileHeight * 0.75;
        if (!atFoot)
        {
            if (follower.Npc.Path is null && FindPath(follower, fromCell, allowPartial: true) is { Count: > 0 } path) StartPath(follower, path);
            return true;
        }
        // At the foot: it waits till the player and the others are off the top, then goes.
        var toCell = ((int)Math.Floor(leap.ToX / _tileWidth), (int)Math.Floor(leap.ToY / _tileHeight));
        if (CellsApart(CellOf(_player), toCell) <= 1 || _leaping.Values.Any(l => l.Leap == leap)) { StopWalking(follower); return true; }
        if (_mapCharacters.Any(m => m != follower && Math.Abs(m.Lift - leap.ToLift) <= 1 && CellsApart((m.Placement.X, m.Placement.Y), toCell) == 0)) { StopWalking(follower); return true; }
        StopWalking(follower);
        int rise = leap.ToLift - at;
        string pose = rise > 0 ? (HasArtFor(follower.Character, "Climb") ? "Climb" : "Jump Up") : "Jump";
        double seconds = rise > 0 ? 0.5 + rise * 0.05 : 0.45;
        if (HasArtFor(follower.Character, pose)) seconds = Math.Max(seconds, PoseLengthMs(follower.Character, pose) / 1000.0);
        _leaping[follower] = new Leaping { Leap = leap, StartX = follower.PixelX, StartY = follower.PixelY, StartLift = at, Duration = seconds };
        SetCharacterPose(follower, pose, FacingTowards(follower.PixelX, follower.PixelY, leap.ToX, leap.ToY));
        SoakLog($"leap follow: {follower.Character.Name} {at} to {leap.ToLift}");
        return true;
    }

    private void UpdateLeaps(double deltaSeconds)
    {
        if (_leaping.Count == 0) return;
        foreach (var (mc, l) in _leaping.ToList())
        {
            if (!_mapCharacters.Contains(mc) || IsDead(mc.Character)) { _leaping.Remove(mc); continue; }
            l.T = Math.Min(l.Duration, l.T + deltaSeconds);
            double s = l.T / l.Duration;
            mc.PixelX = l.StartX + (l.Leap.ToX - l.StartX) * s;
            mc.PixelY = l.StartY + (l.Leap.ToY - l.StartY) * s;
            double arc = l.Leap.ToLift > l.StartLift ? 1 : 0.5;
            mc.Lift = (int)Math.Round(l.StartLift + (l.Leap.ToLift - l.StartLift) * s + arc * 4 * s * (1 - s));
            mc.Placement.X = (int)Math.Floor(mc.PixelX / _tileWidth);
            mc.Placement.Y = (int)Math.Floor(mc.PixelY / _tileHeight);
            if (l.T >= l.Duration)
            {
                mc.Lift = l.Leap.ToLift;
                _leaping.Remove(mc);
                SetCharacterPose(mc, "Idle", mc.Facing);
            }
            PositionCharacterVisual(mc);
        }
    }
}
