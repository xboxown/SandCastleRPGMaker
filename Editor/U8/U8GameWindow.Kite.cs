// Ultima 8 engine (U8GameWindow) - the same as the Ultima 7 engine's GameTestWindow.Kite.cs.
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A kite (Object Editor > Kite): it really goes up. Double-clicked outdoors it leaves the ground and climbs its floors, playing its
/// frames, stays up for its time and comes back down: while up it is at that height for everything, so from the ground it's out of
/// reach (from a roof that high it isn't).
/// </summary>
public partial class U8GameWindow
{
    private sealed class KiteFlight
    {
        /// <summary>0 going up, 1 staying up, 2 coming down.</summary>
        public int Phase;
        public double Ms;
        public double FrameMs;
        public int BaseZ, TopZ;
    }

    private readonly Dictionary<ObjectInstance, KiteFlight> _kites = new(ReferenceEqualityComparer.Instance);

    private bool IsFlyingKite(object entity) => entity is ObjectInstance instance && _kites.ContainsKey(instance);

    private void UseKite(Used u)
    {
        if (u.Placed is { } up && _kites.ContainsKey(up)) { StatusText.Text = $"\"{u.Obj.Name}\" is already up."; return; }
        if (FireUseEvent(u)) return;
        var k = u.Obj.Kite;
        var user = UserOf(u);
        bool roofOverIt = u.Placed is { } on && BodyOf(null, on) is var body && IsUnderRoof(new UnitRect(body.Left, body.Top, body.Width, body.Depth), on.Z);
        if (k.OutdoorsOnly && (PlayerUnderRoof || roofOverIt))
        {
            StatusText.Text = $"\"{u.Obj.Name}\" can only be used outdoors.";
            if (user is not null) Bark(user, VerboseKind.OutdoorsOnly);
            return;
        }

        var kite = u.Placed;
        if (kite is null && u.Carried is { } carried) // out of the pack: it goes up from the feet of whoever holds it
        {
            RemoveFromSource(u.Source);
            kite = (ObjectInstance)CreateMapEntityFor(carried);
            kite.Quantity = 1;
            kite.ContainerInstanceId = -1;
            kite.InstanceId = _map.GetNextInstanceId();
            AddMapEntity(null, kite);
            SetMapPosition(null, kite, _playerX, _playerY, 0, 0);
            SetZ(kite, _playerZ);
            ForgetHomeFloor(kite);
            RefreshAfterMove();
        }
        if (kite is null) return;
        int top = Math.Min(kite.Z + Math.Max(1, k.FlyFloors) * MapFloors.LiftsPerFloor, MapFloors.BaseLift(MapFloors.MaxFloors));
        _kites[kite] = new KiteFlight { BaseZ = kite.Z, TopZ = top };
        SoakLog($"kite: up from lift {kite.Z} to {top}");
        kite.FrameRuntime = k.RiseFirstFrame;
        PlaySound(k.SoundResourceId);
        RefreshObjectLook(kite);
        StatusText.Text = $"\"{u.Obj.Name}\" takes to the air.";
    }

    private static int FrameCount(int first, int last) => Math.Max(1, last - first + 1);

    private void UpdateKites(double elapsedMs)
    {
        if (_kites.Count == 0) return;
        foreach (var (kite, flight) in _kites.ToList())
        {
            if (!_map.Objects.Contains(kite) || DefinitionOf(kite) is not { Type: ObjectType.Kite } obj) { _kites.Remove(kite); continue; }
            var k = obj.Kite;
            double frameMs = Math.Max(20, k.FrameMilliseconds);
            flight.Ms += elapsedMs;
            flight.FrameMs += elapsedMs;
            var (first, last) = flight.Phase switch { 0 => (k.RiseFirstFrame, k.RiseLastFrame), 1 => (k.FlyFirstFrame, k.FlyLastFrame), _ => (k.LandFirstFrame, k.LandLastFrame) };
            double phaseMs = flight.Phase == 1 ? Math.Max(0, k.FlySeconds) * 1000 : FrameCount(first, last) * frameMs;
            int z = flight.Phase switch
            {
                0 => flight.BaseZ + (int)Math.Round((flight.TopZ - flight.BaseZ) * Math.Min(1, flight.Ms / phaseMs)),
                1 => flight.TopZ,
                _ => flight.TopZ - (int)Math.Round((flight.TopZ - flight.BaseZ) * Math.Min(1, flight.Ms / phaseMs))
            };
            int frame = first + (int)(flight.FrameMs / frameMs) % FrameCount(first, last);
            if (flight.Phase != 1) frame = first + Math.Min(FrameCount(first, last) - 1, (int)(flight.Ms / frameMs)); // up and down play once; in the air it goes round
            if (frame != kite.FrameRuntime || z != kite.Z)
            {
                kite.FrameRuntime = frame;
                SetZ(kite, z);
                RefreshObjectLook(kite);
            }
            if (flight.Ms < phaseMs) continue;
            flight.Ms = 0;
            if (++flight.Phase < 3) continue;
            _kites.Remove(kite); // down: an ordinary thing on the ground again
            SoakLog($"kite: down at lift {kite.Z}");
            kite.FrameRuntime = 0;
            RefreshObjectLook(kite);
            SettleWorld();
            RefreshAfterMove();
        }
    }

    // ---------- A music box: an instrument that opens and plays until closed (Object Editor > Musical Instrument) ----------

    private readonly Dictionary<object, object> _musicBoxOwners = new(ReferenceEqualityComparer.Instance);

    /// <summary>Double-clicked: opens, its picture changes and its tune plays round and round; double-clicked again: closes, the music stops.</summary>
    private void UseMusicBox(Used u)
    {
        object thing = (object?)u.Placed ?? u.Carried!;
        bool open = !((u.Placed?.IsLitRuntime ?? u.Carried?.IsLitRuntime) ?? false);
        if (u.Placed is { } placed) placed.IsLitRuntime = open;
        if (u.Carried is { } carried) carried.IsLitRuntime = open;
        if (!_musicBoxOwners.TryGetValue(thing, out var owner)) _musicBoxOwners[thing] = owner = new object();
        if (open)
        {
            var songs = u.Obj.Instrument.SongSoundIds;
            if (songs.Count > 0) SoundPlayback.TryPlayFor(owner, _resources, songs[Rng.Next(songs.Count)], loop: true);
            StatusText.Text = $"\"{u.Obj.Name}\" opens.";
        }
        else
        {
            SoundPlayback.StopFor(owner);
            StatusText.Text = $"\"{u.Obj.Name}\" closes.";
        }
        RefreshUsed(u);
    }
}
