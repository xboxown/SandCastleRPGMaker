using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The screen, time, sound and game script commands (CommandList groups
/// 9-11): fades and flashes, the camera, pictures over the game, the clock,
/// going to another spot of the world, music fades and volumes, saving and
/// loading by command, allowing / forbidding saving and controls, game over,
/// back to the title, quitting, and learning / casting spells.
/// </summary>
public partial class GameTestWindow
{
    private sealed class ScreenTween
    {
        /// <summary>Called with the ms since the last frame; true once done.</summary>
        public required Func<double, bool> Step;
        public Action? Done;
    }

    private readonly List<ScreenTween> _screenTweens = new();
    private Rectangle? _fadeOverlay;
    private Canvas? _pictureLayer;
    private readonly Dictionary<int, FrameworkElement> _pictures = new();
    /// <summary>Move Camera: the world spot the view is centred on (null = it follows as usual).</summary>
    private (double X, double Y)? _cameraHold;
    /// <summary>Camera Follows: the character the view follows instead of the player.</summary>
    private CharacterDefinition? _cameraFollow;
    private bool _savingForbidden, _combatForbidden, _keysForbidden;
    /// <summary>Allow / Forbid Controls > Player control: held in Cinema Mode's list until allowed again.</summary>
    private static readonly object PlayerControlLock = new();
    /// <summary>Return To Title: the game window closed so the Title Screen can come back.</summary>
    public bool ReturnToTitleRequested { get; private set; }

    private void InitializeScriptGame()
    {
        var h = _execContext.GameHandlers;
        var a = _execContext.GameActions;
        a[EventCommandType.FadeScreen] = FadeScreenCommand;
        h[EventCommandType.FlashScreen] = FlashScreenCommand;
        a[EventCommandType.MoveCamera] = MoveCameraCommand;
        h[EventCommandType.CameraFollow] = c =>
        {
            _cameraFollow = CharacterDefinitionFor(c.Get("Who"));
            if (_cameraFollow == _player) _cameraFollow = null;
            _cameraHold = null;
            UpdateCamera();
            return true;
        };
        h[EventCommandType.ShowPicture] = ShowPictureCommand;
        h[EventCommandType.ErasePicture] = c =>
        {
            int number = (int)FormulaNumber(c, "Number");
            foreach (var n in _pictures.Keys.Where(k => number <= 0 || k == number).ToList())
            {
                UnregisterWorldAnimator(_pictures[n]);
                _pictureLayer?.Children.Remove(_pictures[n]);
                _pictures.Remove(n);
            }
            return true;
        };
        h[EventCommandType.SetTime] = SetTimeCommand;
        a[EventCommandType.ChangeMap] = ChangeMapCommand;

        h[EventCommandType.StopMusic] = c => { SoundPlayback.Fade(BookEventPreviewExecutionContext.MusicOwner, Math.Max(0, FormulaNumber(c, "Seconds")), fadeIn: false); return true; };
        h[EventCommandType.FadeMusicIn] = c =>
        {
            if (!int.TryParse(c.Get("SoundId"), out int id)) return false;
            SoundPlayback.StopFor(BookEventPreviewExecutionContext.MusicOwner);
            if (!SoundPlayback.TryPlayFor(BookEventPreviewExecutionContext.MusicOwner, _resources, id, c.Get("Loop") == "True", null, startFade: 0)) return false;
            SoundPlayback.Fade(BookEventPreviewExecutionContext.MusicOwner, Math.Max(0.05, FormulaNumber(c, "Seconds")), fadeIn: true);
            return true;
        };
        h[EventCommandType.SetVolume] = c => { SoundPlayback.SetVolumes(FormulaNumber(c, "Music") / 100.0, FormulaNumber(c, "Sounds") / 100.0); return true; };
        h[EventCommandType.ResumeMapMusic] = _ =>
        {
            SoundPlayback.StopFor(BookEventPreviewExecutionContext.MusicOwner);
            StopCombatMusic();
            _ambientPlaying = -2; // the map's own sound starts again (GameTestWindow.Ambient.cs)
            return true;
        };

        h[EventCommandType.SaveGame] = SaveGameCommand;
        h[EventCommandType.LoadGame] = LoadGameCommand;
        h[EventCommandType.OpenSaveLoadScreen] = _ => { OpenSaveLoad(); return _saveScreen is not null; };
        h[EventCommandType.AllowSaving] = c => { _savingForbidden = c.Get("Value") == "Forbidden"; return true; };
        h[EventCommandType.AllowControls] = AllowControlsCommand;
        h[EventCommandType.GameOver] = _ =>
        {
            if (_combatMode) ToggleCombatMode();
            _gameOver = true;
            DeathMusic();
            ShowPointer(RestingPointer);
            StatusText.Text = "Game over.";
            return true;
        };
        h[EventCommandType.ReturnToTitle] = _ => { ReturnToTitleRequested = true; Dispatcher.BeginInvoke(new Action(Close)); return true; };
        h[EventCommandType.QuitGame] = _ =>
        {
            if (IsShippedGame) Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
            else Dispatcher.BeginInvoke(new Action(Close));
            return true;
        };
        h[EventCommandType.LearnSpell] = LearnSpellCommand;
        h[EventCommandType.CastSpell] = CastSpellCommand;
    }

    private void UpdateScreenTweens(double elapsedMs)
    {
        for (int i = _screenTweens.Count - 1; i >= 0; i--)
        {
            if (i >= _screenTweens.Count) continue;
            var tween = _screenTweens[i];
            if (!tween.Step(elapsedMs)) continue;
            _screenTweens.Remove(tween);
            tween.Done?.Invoke();
        }
        if (_cameraFollow is not null) UpdateCamera(); // following someone else: every frame
    }

    /// <summary>The world's own grid inside the camera window (fades and pictures go there, over the world, under the boxes).</summary>
    private Grid ViewportGrid => (Grid)LightingOverlay.Parent;

    private static Brush BrushOf(string hex, Color fallback)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex.Trim())); }
        catch { return new SolidColorBrush(fallback); }
    }

    // ---------- Screen ----------

    private bool FadeScreenCommand(EventCommand command, Action<bool> done)
    {
        if (_fadeOverlay is null)
        {
            _fadeOverlay = new Rectangle { Width = 320, Height = 200, IsHitTestVisible = false, Opacity = 0 };
            Panel.SetZIndex(_fadeOverlay, 39); // over the world and its light, under the dialog boxes
            ViewportGrid.Children.Add(_fadeOverlay);
        }
        var overlay = _fadeOverlay;
        overlay.Fill = BrushOf(command.Get("Color"), Colors.Black);
        bool fadeOut = command.Get("Value") != "In";
        double from = overlay.Opacity, to = fadeOut ? 1 : 0, total = Math.Max(0, FormulaNumber(command, "Seconds")) * 1000, ms = 0;
        if (total <= 0) { overlay.Opacity = to; done(true); return true; }
        _screenTweens.Add(new ScreenTween
        {
            Step = elapsed => { ms += elapsed; overlay.Opacity = from + (to - from) * Math.Min(1, ms / total); return ms >= total; },
            Done = () => done(true)
        });
        return true;
    }

    private bool FlashScreenCommand(EventCommand command)
    {
        var flash = new Rectangle { Width = 320, Height = 200, IsHitTestVisible = false, Fill = BrushOf(command.Get("Color"), Colors.White), Opacity = 1 };
        Panel.SetZIndex(flash, 38);
        ViewportGrid.Children.Add(flash);
        double total = Math.Max(0.05, FormulaNumber(command, "Seconds")) * 1000, ms = 0;
        _screenTweens.Add(new ScreenTween
        {
            Step = elapsed => { ms += elapsed; flash.Opacity = 1 - Math.Min(1, ms / total); return ms >= total; },
            Done = () => ViewportGrid.Children.Remove(flash)
        });
        return true;
    }

    private bool MoveCameraCommand(EventCommand command, Action<bool> done)
    {
        var from = _cameraHold ?? (_cameraX + ViewportWidth / 2, _cameraY + ViewportHeight / 2);
        bool back = command.Get("Value") == "Back to the player";
        (double X, double Y) to;
        if (back) to = (_playerPixelX, _playerPixelY);
        else if (int.TryParse(command.Get("X"), out int x) && int.TryParse(command.Get("Y"), out int y)) to = (x * _tileWidth + _tileWidth / 2.0, y * _tileHeight + _tileHeight / 2.0);
        else return false;
        _cameraFollow = null;
        double total = Math.Max(0, FormulaNumber(command, "Seconds")) * 1000, ms = 0;
        void Finish()
        {
            if (back) _cameraHold = null;
            UpdateCamera();
            done(true);
        }
        if (total <= 0) { _cameraHold = to; Finish(); return true; }
        _screenTweens.Add(new ScreenTween
        {
            Step = elapsed =>
            {
                ms += elapsed;
                double t = Math.Min(1, ms / total);
                t = t * t * (3 - 2 * t); // ease in and out
                _cameraHold = (from.Item1 + (to.X - from.Item1) * t, from.Item2 + (to.Y - from.Item2) * t);
                UpdateCamera();
                return ms >= total;
            },
            Done = Finish
        });
        return true;
    }

    private bool ShowPictureCommand(EventCommand command)
    {
        int number = Math.Clamp((int)FormulaNumber(command, "Number"), 1, 50);
        var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == command.Get("SpriteGroup"))?.Sprites.FirstOrDefault(s => s.Name == command.Get("SpriteName"));
        if (sprite is null) return false;
        if (_pictureLayer is null)
        {
            _pictureLayer = new Canvas { Width = 320, Height = 200, IsHitTestVisible = false, ClipToBounds = true };
            Panel.SetZIndex(_pictureLayer, 37);
            ViewportGrid.Children.Add(_pictureLayer);
        }
        if (_pictures.Remove(number, out var old)) { UnregisterWorldAnimator(old); _pictureLayer.Children.Remove(old); }
        var first = DollLook.Frame(sprite);
        var image = new Image { Width = first?.PixelWidth ?? 32, Height = first?.PixelHeight ?? 32, Stretch = Stretch.Fill, Opacity = Math.Clamp(FormulaNumber(command, "Opacity") / 100.0, 0, 1) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources));
        Canvas.SetLeft(image, FormulaNumber(command, "X"));
        Canvas.SetTop(image, FormulaNumber(command, "Y"));
        Panel.SetZIndex(image, number);
        _pictureLayer.Children.Add(image);
        _pictures[number] = image;
        return true;
    }

    // ---------- Time and travel ----------

    private bool SetTimeCommand(EventCommand command)
    {
        int amount = (int)Math.Round(FormulaNumber(command, "Hour"));
        int minutes = command.Get("Value") switch
        {
            "Advance by minutes" => Math.Max(0, amount),
            "Advance by hours" => Math.Max(0, amount) * 60,
            _ => (((Math.Clamp(amount, 0, 23) * 60 + Math.Clamp((int)FormulaNumber(command, "Minute"), 0, 59)) - (_clock.Hour * 60 + _clock.Minute)) % 1440 + 1440) % 1440
        };
        if (minutes <= 0) return true;
        int hours = _clock.AdvanceMinutes(minutes);
        if (hours > 0) ScheduleNpcs(_clock.Hour); // Exult re-schedules everyone on the hour
        ShowClock();
        return true;
    }

    private bool ChangeMapCommand(EventCommand command, Action<bool> done)
    {
        if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y) || !InsideMap(x, y)) return false;
        int floor = int.TryParse(command.Get("Floor"), out int f) ? f : 1;
        void Arrive()
        {
            _playerX = x;
            _playerY = y;
            _playerPixelX = x * _tileWidth + _tileWidth / 2.0;
            _playerPixelY = y * _tileHeight + _tileHeight / 2.0;
            _playerZ = StandingLiftOnFloor(PlayerFootprint(), floor);
            _playerVisualLiftZ = _playerZ;
            _eventUnderPlayer = EventAt(x, y, _playerZ);
            SnapPlayerPosition();
            _cameraHold = null;
            _cameraFollow = null;
            UpdateCamera();
            // The party comes along, beside the main character.
            int n = 0;
            foreach (var member in _mapCharacters.Where(mc => mc.Character.IsInParty).ToList())
            {
                var spot = (x + (n % 3) - 1, y + 1 + n / 3);
                if (!InsideMap(spot.Item1, spot.Item2)) spot = (x, y);
                Teleport(member, spot);
                member.Lift = StandingLiftOnFloor(CharacterFootprint(member), floor, member.Placement);
                PositionCharacterVisual(member);
                n++;
            }
        }
        if (command.Get("Fade") != "True") { Arrive(); done(true); return true; }
        var fadeOut = new EventCommand { Parameters = { ["Value"] = "Out", ["Seconds"] = "0.4", ["Color"] = "#000000" } };
        return FadeScreenCommand(fadeOut, _ =>
        {
            Arrive();
            FadeScreenCommand(new EventCommand { Parameters = { ["Value"] = "In", ["Seconds"] = "0.4", ["Color"] = "#000000" } }, ok => done(ok));
        });
    }

    // ---------- The game ----------

    private bool SaveGameCommand(EventCommand command)
    {
        if (ProjectSession.ProjectIdProvider?.Invoke() is not { } projectId) return false;
        if (_savingForbidden) { StatusText.Text = "Saving isn't allowed here."; return false; }
        int slot = Math.Max(1, (int)FormulaNumber(command, "Slot"));
        string name = ScriptValues.Text(Formula(command, "Name")).Trim();
        if (name.Length == 0) name = $"Slot {slot}";
        try
        {
            var existing = GameSaveStore.List(projectId).FirstOrDefault(s => s.Slot == slot);
            var info = new SaveSlotInfo { Name = name, SavedAtUtc = DateTime.UtcNow, GameTime = _clock.ToString(), PlayerName = _player?.Name ?? string.Empty, Slot = slot };
            GameSaveStore.Write(projectId, CaptureSave(), info, TakeScreenshot(), existing);
            StatusText.Text = $"Game saved: \"{name}\".";
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"The game couldn't be saved: {ex.Message}";
            return false;
        }
    }

    private bool LoadGameCommand(EventCommand command)
    {
        if (ProjectSession.ProjectIdProvider?.Invoke() is not { } projectId) return false;
        int slot = Math.Max(1, (int)FormulaNumber(command, "Slot"));
        if (GameSaveStore.List(projectId).FirstOrDefault(s => s.Slot == slot) is not { } save) { StatusText.Text = $"Slot {slot} is empty."; return false; }
        GameSaveData data;
        try { data = GameSaveStore.Read(projectId, save); }
        catch (Exception ex) { StatusText.Text = $"\"{save.Name}\" couldn't be loaded: {ex.Message}"; return false; }
        Dispatcher.BeginInvoke(new Action(() => LoadGame(data, $"Loaded \"{save.Name}\".")));
        return true;
    }

    private bool AllowControlsCommand(EventCommand command)
    {
        bool forbid = command.Get("Value") == "Forbidden";
        switch (command.Get("What"))
        {
            case "Combat mode":
                _combatForbidden = forbid;
                if (forbid && _combatMode) ToggleCombatMode();
                break;
            case "Keys":
                _keysForbidden = forbid;
                break;
            default:
                if (forbid) BeginCinema(PlayerControlLock);
                else EndCinema(PlayerControlLock);
                break;
        }
        return true;
    }

    // ---------- Spells ----------

    /// <summary>The spellbooks a character carries (objects of the Spellbook type), with their objects.</summary>
    private List<(InventoryItemInstance Item, WorldObject Book)> SpellbooksOf(CharacterDefinition who) =>
        CarriedItems(who).Where(c => c.Item.Category == ReferencedItemCategory.Object)
            .Select(c => (c.Item, Book: _objects.Objects.FirstOrDefault(o => o.Id == c.Item.ItemId)))
            .Where(c => c.Book is { Type: ObjectType.Spellbook }).Select(c => (c.Item, c.Book!)).ToList();

    /// <summary>A spellbook's spells this copy knows: all of them, or (its type starting empty) those learned.</summary>
    private static bool KnowsSpell(WorldObject book, List<string>? learned, SpellDefinition spell) =>
        book.Spellbook.StartsWithAllSpells || (learned?.Contains(spell.Name, StringComparer.OrdinalIgnoreCase) ?? false);

    private bool LearnSpellCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who) return false;
        string name = ScriptValues.Text(Formula(command, "Value")).Trim();
        foreach (var (item, book) in SpellbooksOf(who))
        {
            var spell = book.Spellbook.Spells.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (spell is null) continue;
            item.LearnedSpellsRuntime ??= new List<string>();
            if (!item.LearnedSpellsRuntime.Contains(spell.Name, StringComparer.OrdinalIgnoreCase)) item.LearnedSpellsRuntime.Add(spell.Name);
            StatusText.Text = $"{who.Name} learns {spell.Name}.";
            return true;
        }
        StatusText.Text = $"Learn Spell: {who.Name} carries no spellbook with the spell \"{name}\".";
        return false;
    }

    private bool CastSpellCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } caster || IsDead(caster)) return false;
        string name = ScriptValues.Text(Formula(command, "Value")).Trim();
        foreach (var (item, book) in SpellbooksOf(caster))
        {
            var spell = book.Spellbook.Spells.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (spell is null || !KnowsSpell(book, item.LearnedSpellsRuntime, spell)) continue;
            if (WhyCannotCast(caster, spell) is { } why) { StatusText.Text = why; return false; }
            var target = command.Get("Other").Length > 0 ? CharacterDefinitionFor(command.Get("Other")) : null;
            CastSpell(caster, spell, target ?? (spell.NeedsTarget ? caster : null));
            return true;
        }
        StatusText.Text = $"Cast Spell: {caster.Name} has no spellbook that knows \"{name}\".";
        return false;
    }
}
