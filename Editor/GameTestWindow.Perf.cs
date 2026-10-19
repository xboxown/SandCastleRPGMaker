using System.Diagnostics;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Keeping the game at 25-30 frames a second on big maps:
///  - Object, book and container definitions found by id at once (a
///    dictionary), not by searching every definition for every thing.
///  - The world's still things (items, objects, floor slabs) as bodies,
///    built once and kept until something in the world changes (or the
///    next frame), with a grid of them: a step or a path only looks at
///    the bodies near it, not at every thing on the map.
///  - The game's own work runs about 30 times a second (WPF can draw 60).
///  - F10 in Game Test: the frame rate and how long a frame's work takes.
/// </summary>
public partial class GameTestWindow
{
    // ---------- Definitions by id ----------

    private Dictionary<int, WorldObject>? _objectById;
    private Dictionary<int, BookData>? _bookById;
    private Dictionary<int, ContainerData>? _containerById;
    private int _objectCount = -1, _bookCount = -1, _containerCount = -1;

    private WorldObject? ObjectDef(int id)
    {
        if (_objectById is null || _objectCount != _objects.Objects.Count) { _objectById = ById(_objects.Objects, o => o.Id); _objectCount = _objects.Objects.Count; }
        if (_objectById.TryGetValue(id, out var found)) return found;
        var slow = _objects.Objects.FirstOrDefault(o => o.Id == id); // the list changed under the same count: look, and know it next time
        if (slow is not null) _objectById[id] = slow;
        return slow;
    }

    private BookData? BookDef(int id)
    {
        if (_bookById is null || _bookCount != _books.Books.Count) { _bookById = ById(_books.Books, b => b.Id); _bookCount = _books.Books.Count; }
        if (_bookById.TryGetValue(id, out var found)) return found;
        var slow = _books.Books.FirstOrDefault(b => b.Id == id);
        if (slow is not null) _bookById[id] = slow;
        return slow;
    }

    private ContainerData? ContainerDef(int id)
    {
        if (_containerById is null || _containerCount != _containers.Containers.Count) { _containerById = ById(_containers.Containers, c => c.Id); _containerCount = _containers.Containers.Count; }
        if (_containerById.TryGetValue(id, out var found)) return found;
        var slow = _containers.Containers.FirstOrDefault(c => c.Id == id);
        if (slow is not null) _containerById[id] = slow;
        return slow;
    }

    private static Dictionary<int, T> ById<T>(IEnumerable<T> items, Func<T, int> id)
    {
        var map = new Dictionary<int, T>();
        foreach (var item in items) map.TryAdd(id(item), item); // the first of a duplicate id, as FirstOrDefault
        return map;
    }

    // ---------- The world's still bodies, kept ----------

    /// <summary>Bodies grouped by area: a query looks only at the cells a rectangle covers.</summary>
    private sealed class BodyIndex
    {
        private const int CellUnits = 16;
        private readonly List<Body> _bodies;
        private readonly Dictionary<(int, int), List<int>> _cells = new();
        private int[] _seen;
        private int _stamp;

        public BodyIndex(List<Body> bodies)
        {
            _bodies = bodies;
            _seen = new int[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                for (int cy = Floor(b.Top); cy <= Floor(b.Bottom - 1); cy++)
                    for (int cx = Floor(b.Left); cx <= Floor(b.Right - 1); cx++)
                    {
                        if (!_cells.TryGetValue((cx, cy), out var list)) _cells[(cx, cy)] = list = new List<int>(4);
                        list.Add(i);
                    }
            }
        }

        private static int Floor(int units) => (int)Math.Floor(units / (double)CellUnits);

        /// <summary>Every body overlapping the rectangle (each once).</summary>
        public IEnumerable<Body> Overlapping(UnitRect r)
        {
            if (++_stamp == int.MaxValue) { Array.Clear(_seen); _stamp = 1; }
            int stamp = _stamp;
            var found = new List<Body>();
            for (int cy = Floor(r.Top); cy <= Floor(r.Bottom - 1); cy++)
                for (int cx = Floor(r.Left); cx <= Floor(r.Right - 1); cx++)
                {
                    if (!_cells.TryGetValue((cx, cy), out var list)) continue;
                    foreach (int i in list)
                    {
                        if (_seen[i] == stamp) continue;
                        _seen[i] = stamp;
                        if (_bodies[i].Overlaps(r)) found.Add(_bodies[i]);
                    }
                }
            return found;
        }
    }

    /// <summary>A view of the world's bodies for steps and path searches: the kept still bodies (doors aside) by area, and a short list of the rest - the doors (as this walker finds them) and the characters.</summary>
    private sealed class BodySource
    {
        public required BodyIndex Index;
        public required List<Body> Extra;
        public IEnumerable<Body> Overlapping(UnitRect r) => Index.Overlapping(r).Concat(Extra.Where(b => b.Overlaps(r)));
    }

    /// <summary>TryStepActor's list of the bodies over one square, used again and again.</summary>
    private readonly List<Body> _stepHere = new();

    private List<Body>? _stillBodies;
    private BodyIndex? _stillIndex;
    private List<Body> _doorBodies = new();
    private long _stillVersion = -1;
    /// <summary>Goes up whenever a thing in the world moves, comes, goes or changes how solid it is.</summary>
    private long _worldVersion;
    /// <summary>Goes up every frame of the game's work: kept bodies are never older than a frame.</summary>
    private long _logicFrame;

    private void WorldChanged() => _worldVersion++;

    /// <summary>Items, objects (stairs a body a step) and upper floors' slabs - everything but the characters - kept till the world changes; the doors among them also apart.</summary>
    private (List<Body> Bodies, BodyIndex Index, List<Body> Doors) StillBodies()
    {
        if (_stillBodies is null || _stillIndex is null || _stillVersion != _worldVersion)
        {
            using var measured = Measure("StillBodies.Build");
            _stillBodies = _map.Items.Select(i => BodyOf(i, null))
                .Concat(_map.Objects.Where(o => o.ContainerInstanceId < 0).SelectMany(ObjectBodies))
                .Concat(_floorSlabBodies)
                .ToList();
            _doorBodies = _stillBodies.Where(IsDoorBody).ToList();
            _stillIndex = new BodyIndex(_stillBodies.Where(b => !IsDoorBody(b)).ToList());
            _stillVersion = _worldVersion;
        }
        return (_stillBodies, _stillIndex, _doorBodies);
    }

    private bool IsDoorBody(Body b) => b.Entity is ObjectInstance o && ObjectDef(o.ObjectId)?.Type == ObjectType.Door;

    /// <summary>The world as it is now (doors shut or open as they are), for the player's own path.</summary>
    private BodySource WorldNow()
    {
        var (_, index, doors) = StillBodies();
        var extra = new List<Body>(doors.Count + _mapCharacters.Count);
        extra.AddRange(doors);
        extra.AddRange(CharacterBodies());
        return new BodySource { Index = index, Extra = extra };
    }

    /// <summary>The solid bodies a footprint overlaps (not the mover's own): from the kept still bodies, the doors and the characters as they are now - or from a path search's view of the world.</summary>
    private IEnumerable<Body> SolidsOverlapping(UnitRect rect, object? mover, BodySource? world)
    {
        if (world is not null) return world.Overlapping(rect).Where(b => b.Solid && !ReferenceEquals(b.Entity, mover));
        var (_, index, doors) = StillBodies();
        return index.Overlapping(rect).Where(b => b.Solid && !ReferenceEquals(b.Entity, mover))
            .Concat(doors.Where(b => b.Solid && b.Overlaps(rect)))
            .Concat(CharacterBodies().Where(b => b.Solid && b.Overlaps(rect) && !ReferenceEquals(b.Entity, mover)));
    }

    // ---------- Dormant characters (Exult actors.cc) ----------

    /// <summary>
    /// Exult's dormant actors: a character (not in the party) off screen and
    /// more than a screenful from the camera stops and goes dormant - it does
    /// nothing at all - and wakes half a second after it's on screen again
    /// (Npc_actor::step, ::paint). One walking to its schedule's place gets
    /// there "by any means" (Walk_to_schedule::im_dormant): put there, unseen.
    /// Never the party, followers, nor anyone carrying out an event's command.
    /// True: it stays dormant this frame.
    /// </summary>
    private bool KeepDormant(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        var character = mapCharacter.Character;
        bool exempt = character.IsInParty || _followers.ContainsKey(mapCharacter) || IsScripted(character) || IsDead(character);
        bool onScreen = IsOnScreen(mapCharacter.PixelX, mapCharacter.PixelY);
        if (npc.Dormant)
        {
            if (!onScreen && !exempt) return true;
            npc.Dormant = false; // painted again: its schedule resumes in half a second
            if (npc.Path is null) npc.WaitMs = Math.Max(npc.WaitMs, 500);
            return false;
        }
        if (exempt || onScreen) return false;
        double cx = _cameraX + ViewportWidth / 2, cy = _cameraY + ViewportHeight / 2;
        if (Math.Max(Math.Abs(mapCharacter.PixelX - cx), Math.Abs(mapCharacter.PixelY - cy)) <= ViewportWidth + SpriteSizing.BaseUnit) return false; // within a screenful
        StopWalking(mapCharacter);
        npc.Dormant = true;
        if (npc.WalkingToSchedule && npc.Entry is not null)
        {
            Teleport(mapCharacter, npc.Dest); // Walk_to_schedule::im_dormant: there by any means
            BeginActivity(mapCharacter);
            StopWalking(mapCharacter);
        }
        return true;
    }

    // ---------- Only what's near the view is on the canvas ----------

    /// <summary>The area whose things are on the canvas now: the view and a screen all round it.</summary>
    private System.Windows.Rect _streamRect = System.Windows.Rect.Empty;
    private long _streamVersion = -1;

    /// <summary>
    /// Things far from the view are taken off the canvas (and put back as the
    /// view comes near): WPF sorts every picture on the canvas by its draw
    /// order whenever one changes - with thousands of things on a big map,
    /// every frame a character walked. Their game data and pictures stay as
    /// they are; only whether WPF draws them changes.
    /// </summary>
    private void StreamWorldVisuals()
    {
        var camera = new System.Windows.Rect(_cameraX, _cameraY, ViewportWidth, ViewportHeight);
        var inner = camera;
        inner.Inflate(ViewportWidth / 2, ViewportHeight / 2);
        bool moved = _streamRect.IsEmpty || !_streamRect.Contains(inner);
        if (moved || _streamVersion != _worldVersion)
        {
            if (moved) { _streamRect = camera; _streamRect.Inflate(ViewportWidth, ViewportHeight); }
            _streamVersion = _worldVersion;
            foreach (var visual in _objectVisuals.Values) KeepIfNear(visual);
            foreach (var visual in _itemVisuals.Values) KeepIfNear(visual);
            foreach (var visual in _upperTileImages.Values) KeepIfNear(visual);
        }
        foreach (var mapCharacter in _mapCharacters) // characters walk: every frame
            if (mapCharacter.Visual is { } visual) KeepIfNear(visual);
    }

    private void KeepIfNear(System.Windows.UIElement visual)
    {
        var at = ScreenRectOf(visual);
        bool near = at.IsEmpty || double.IsNaN(at.X) || double.IsNaN(at.Y) || at.IntersectsWith(_streamRect);
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(visual);
        if (near && parent is null) GameCanvas.Children.Add(visual);
        else if (!near && ReferenceEquals(parent, GameCanvas)) GameCanvas.Children.Remove(visual);
    }

    // ---------- Where a frame's time goes (only with SANDCASTLE_PERF_LOG) ----------

    private static readonly Dictionary<string, (double Ms, int Count)> Sections = new();
    private static readonly bool Profiling = Environment.GetEnvironmentVariable("SANDCASTLE_PERF_LOG") is not null;

    private readonly struct Section : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;
        public Section(string name) { _name = Profiling ? name : null; _start = Profiling ? Stopwatch.GetTimestamp() : 0; }
        public void Dispose()
        {
            if (_name is null) return;
            double ms = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            Sections[_name] = Sections.TryGetValue(_name, out var s) ? (s.Ms + ms, s.Count + 1) : (ms, 1);
        }
    }

    private static Section Measure(string name) => new(name);

    private static string TopSections()
    {
        var top = Sections.OrderByDescending(kv => kv.Value.Ms).Take(12).Select(kv => $"{kv.Key} {kv.Value.Ms:0}ms/{kv.Value.Count}");
        string text = string.Join(" | ", top);
        Sections.Clear();
        return text;
    }

    private double _soakMessageMs, _soakPeriodMs;
    private bool _soakHidden;

    /// <summary>Measuring only: SANDCASTLE_PERF_HIDE=ground,weather,light,things hides those layers, to see what WPF's drawing costs.</summary>
    private void SoakHide()
    {
        if (_soakHidden || Environment.GetEnvironmentVariable("SANDCASTLE_PERF_HIDE") is not { } hide) return;
        _soakHidden = true;
        if (hide.Contains("ground")) foreach (var chunk in _groundChunks.Values) chunk.Opacity = 0;
        if (hide.Contains("weather")) { WeatherLayer.Visibility = System.Windows.Visibility.Collapsed; CloudLayer.Visibility = System.Windows.Visibility.Collapsed; }
        if (hide.Contains("light")) LightingOverlay.Visibility = System.Windows.Visibility.Collapsed;
        if (hide.Contains("things")) foreach (var v in _objectVisuals.Values) v.Opacity = 0;
    }

    /// <summary>Measuring (SANDCASTLE_PERF_LOG): nobody is there to read the boxes - each goes on after half a second - and every 20 seconds the clock skips to the next period, so schedule changes are measured too.</summary>
    private void SoakTest(double elapsedMs)
    {
        if (!Profiling) return;
        SoakHide();
        if (_currentMessage is not null && (_soakMessageMs += elapsedMs) > 500) { _soakMessageMs = 0; MessageKey(System.Windows.Input.Key.Enter); }
        if ((_soakPeriodMs += elapsedMs) > 20000) { _soakPeriodMs = 0; using (Measure("SkipToNextPeriod")) SkipToNextPeriod(); }
    }

    // ---------- Frame rate ----------

    /// <summary>The game's own work runs at most this often (about 30 a second); WPF may draw more often.</summary>
    private const double MinLogicMs = 12; // every frame WPF draws (up to about 80 a second; on a 144 Hz screen every other one)
    private bool _showFps;
    /// <summary>Every frame WPF drew (Rendering raised for a new time), whether or not the game's work ran.</summary>
    private int _drawnFrames;
    private readonly Stopwatch _frameWork = new();
    private int _fpsFrames;
    private double _fpsWorkMs, _fpsWorstMs;
    private readonly Stopwatch _fpsWindow = Stopwatch.StartNew();
    private readonly string? _perfLogPath = Environment.GetEnvironmentVariable("SANDCASTLE_PERF_LOG");

    /// <summary>A frame's work measured: every second, the frame rate and the work's average and worst (F10 shows it; SANDCASTLE_PERF_LOG writes it to a file).</summary>
    private void CountFrame(double workMs)
    {
        _fpsFrames++;
        _fpsWorkMs += workMs;
        _fpsWorstMs = Math.Max(_fpsWorstMs, workMs);
        if (_fpsWindow.Elapsed.TotalMilliseconds < 1000) return;
        double seconds = _fpsWindow.Elapsed.TotalSeconds;
        string line = $"{_fpsFrames / seconds:0.0} fps, work {_fpsWorkMs / Math.Max(1, _fpsFrames):0.0} ms avg / {_fpsWorstMs:0.0} ms worst, {_mapCharacters.Count} characters, {_map.Objects.Count + _map.Items.Count} things, {GameCanvas.Children.Count} drawn, {AnimationClock.Count} animations {AnimationClock.TickMs:0}ms, {_drawnFrames} drawn frames, graphics tier {System.Windows.Media.RenderCapability.Tier >> 16}";
        _drawnFrames = 0;
        AnimationClock.TickMs = 0;
        if (_showFps && !IsShippedGame) StatusText.Text = line;
        if (_perfLogPath is not null) try { System.IO.File.AppendAllText(_perfLogPath, DateTime.Now.ToString("HH:mm:ss ") + line + Environment.NewLine + "    " + TopSections() + Environment.NewLine); } catch { /* no log then */ }
        _fpsFrames = 0;
        _fpsWorkMs = _fpsWorstMs = 0;
        _fpsWindow.Restart();
    }
}
