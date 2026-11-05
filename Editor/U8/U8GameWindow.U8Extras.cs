// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// What Ultima 8 and its engine's other games (Crusader: No Remorse / No Regret) do, as commands and character rules, from Pentagram:
///  - Move Platform (Item::ascend): up or down, carrying what's on it. Weight On (Item::getSurfaceWeight): a plate's weight.
///  - Flip Screen (InverterProcess, InverterGump): the world turned upside down and back. Speak: words with their voice (BarkGump, SpeechFlex).
///  - Reading Screen (ReadableGump, ScrollGump): a scroll, a plaque, a gravestone. Keypad (KeypadGump). Computer (ComputerGump): words typed out.
///  - Energy shield (MainActor's shields): part of a blow taken from Mana. Surrender (SurrenderProcess). Recharge (BatteryChargerProcess, CruHealerProcess).
///  - Alarm: a faction sent at someone. Difficulty (Item::scaleReceivedDamageCru): how hard blows land, by a variable.
///  - More event triggers: By Look, By Got Hit, By Cast, By Stolen From, By Coming Into View, By Leaving View.
/// </summary>
public partial class U8GameWindow
{
    private void InitU8Extras()
    {
        var a = _execContext.GameActions;
        var h = _execContext.GameHandlers;
        a[EventCommandType.U8MovePlatform] = MovePlatformCommand;
        h[EventCommandType.U8WeightOn] = c =>
        {
            if (ThingOf(c) is not { } thing) return false;
            SetScriptVariable(c.Get("Variable"), WeightOn(thing));
            return true;
        };
        a[EventCommandType.U8FlipScreen] = FlipScreenCommand;
        a[EventCommandType.U8Speak] = SpeakCommand;
        a[EventCommandType.U8ReadingScreen] = ReadingScreenCommand;
        a[EventCommandType.U8Keypad] = KeypadCommand;
        a[EventCommandType.U8Computer] = ComputerCommand;
        a[EventCommandType.U8Recharge] = RechargeCommand;
        h[EventCommandType.U8Alarm] = AlarmCommand;
    }

    // ---------- Move Platform ----------

    private bool MovePlatformCommand(EventCommand c, Action<bool> done)
    {
        if (ThingOf(c) is not { } thing) return false;
        int lifts = (int)FormulaNumber(c, "Lifts");
        if (lifts == 0) return false;
        double msPerLift = Math.Max(10, FormulaNumber(c, "Seconds") * 1000 / Math.Abs(lifts)), since = 0;
        int dir = Math.Sign(lifts), left = Math.Abs(lifts);
        bool wait = c.Get("Wait") != "False";
        _scriptWaits.Add((elapsed =>
        {
            since += elapsed;
            while (since >= msPerLift && left > 0)
            {
                since -= msPerLift;
                left--;
                RaisePlatform(thing, dir);
            }
            if (left > 0) return null;
            if (!wait) return true;
            return true;
        }, ok => { if (wait) done(ok); }));
        if (!wait) done(true);
        return true;
    }

    /// <summary>Item::ascend: one lift, and everything on it goes with it.</summary>
    private void RaisePlatform(object thing, int dir)
    {
        var people = new List<CharacterDefinition>();
        var on = ThingsOn(thing, people);
        var body = BodyOf(thing as PlacedItemInstance, thing as ObjectInstance);
        SetZ(thing, body.Z + dir);
        foreach (var other in on)
            SetZ(other, BodyOf(other as PlacedItemInstance, other as ObjectInstance).Z + dir);
        foreach (var who in people)
        {
            if (who == _player)
            {
                _playerZ += dir;
                _playerVisualLiftZ = _playerZ;
                SnapPlayerPosition();
            }
            else if (MapCharacterOf(who) is { } mapCharacter)
            {
                mapCharacter.Lift += dir;
                PositionCharacterVisual(mapCharacter);
            }
        }
        RefreshStackingVisuals();
        SoakLog($"platform: lift {body.Z + dir}, {on.Count} things and {people.Count} people on it");
    }

    // ---------- Flip Screen ----------

    private bool _screenFlipped;

    private bool FlipScreenCommand(EventCommand c, Action<bool> done)
    {
        bool toFlipped = c.Get("Value") switch { "Upside down" => true, "Right way up" => false, _ => !_screenFlipped };
        double ms = Math.Max(1, FormulaNumber(c, "Seconds") * 1000), since = 0;
        var scale = new ScaleTransform(1, _screenFlipped ? -1 : 1);
        ViewportHost.RenderTransformOrigin = new Point(0.5, 0.5);
        ViewportHost.RenderTransform = scale;
        double from = scale.ScaleY, to = toFlipped ? -1 : 1;
        _screenFlipped = toFlipped;
        _scriptWaits.Add((elapsed =>
        {
            since = Math.Min(ms, since + elapsed);
            scale.ScaleY = from + (to - from) * since / ms;
            return since >= ms ? true : null;
        }, done));
        return true;
    }

    // ---------- Speak ----------

    private bool SpeakCommand(EventCommand c, Action<bool> done)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        double seconds = Math.Max(0.5, FormulaNumber(c, "Seconds"));
        ShowFloatingText(who, ScriptValues.Text(Formula(c, "Value")), (int)(seconds * 1000));
        if (int.TryParse(c.Get("SoundId"), out int sound)) PlaySoundFrom(sound, who);
        if (c.Get("Wait") != "True") { done(true); return true; }
        _scriptWaits.Add((Lasting("Seconds", seconds, 0), done));
        return true;
    }

    // ---------- Screens: reading, a keypad, a computer ----------

    private FrameworkElement ClickToClose(FrameworkElement content, Action close)
    {
        content.MouseDown += (_, e) => { close(); e.Handled = true; };
        _overlayKeys = _ => { close(); return true; };
        return content;
    }

    private bool ReadingScreenCommand(EventCommand c, Action<bool> done)
    {
        var grid = new Grid { Background = Brushes.Transparent };
        if (ObjectLooks.Find(_sprites, c.Get("SpriteGroup"), c.Get("SpriteName")) is { Frames.Count: > 0 } sprite)
        {
            var (width, height) = QuantizedSpriteSize(sprite);
            var image = new Image { Width = width, Height = height, Source = LoadStaticFrame(sprite.Frames[0].FilePath) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            grid.Children.Add(image);
        }
        var words = GameTextLabel(ScriptValues.Text(Formula(c, "Value")));
        words.HorizontalAlignment = HorizontalAlignment.Center;
        words.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(words);
        ShowObjectOverlay(grid, takesClicks: true, scaleLikeViewport: true);
        bool closed = false;
        ClickToClose(grid, () =>
        {
            if (closed) return;
            closed = true;
            CloseObjectOverlay();
            done(true);
        });
        return true;
    }

    /// <summary>KeypadGump: digits, then accept. The code matched: the command succeeds (Last command succeeded).</summary>
    private bool KeypadCommand(EventCommand c, Action<bool> done)
    {
        string code = ScriptValues.Text(Formula(c, "Code")), typed = "";
        var shown = new TextBlock { Foreground = Brushes.LightGreen, Background = Brushes.Black, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 3), MinWidth = 66, Padding = new Thickness(2) };
        var panel = new StackPanel { Background = Brushes.DimGray };
        var question = c.Get("Prompt").Trim();
        if (question.Length > 0) panel.Children.Add(GameTextLabel(question));
        panel.Children.Add(shown);
        bool finished = false;
        void Finish(bool ok)
        {
            if (finished) return;
            finished = true;
            CloseObjectOverlay();
            if (c.Get("Variable").Length > 0) SetScriptVariable(c.Get("Variable"), typed);
            done(ok && typed == code);
        }
        void Press(string digit) { if (typed.Length < 12) typed += digit; shown.Text = new string('*', typed.Length); }
        var keys = new UniformGrid { Columns = 3 };
        foreach (string face in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "X", "0", "OK" })
        {
            var button = new Button { Content = face, Width = 22, Height = 16, FontSize = 9, Padding = new Thickness(0), Focusable = false };
            string f = face;
            button.Click += (_, _) => { if (f == "OK") Finish(true); else if (f == "X") Finish(false); else Press(f); };
            keys.Children.Add(button);
        }
        panel.Children.Add(keys);
        ShowObjectOverlay(panel, takesClicks: true, scaleLikeViewport: true);
        _overlayRawKeys = key =>
        {
            if (TypingKeyIs(key, "Accept")) { Finish(true); return true; }
            if (TypingKeyIs(key, "Cancel")) { Finish(false); return true; }
            string word = key.ToString(); // "D5" or "NumPad5": the digit as typed
            if (word.Length >= 2 && char.IsDigit(word[^1]) && (word.Length == 2 || word.StartsWith("NumPad"))) { Press(word[^1].ToString()); return true; }
            return false;
        };
        return true;
    }

    /// <summary>ComputerGump: words typed out on a screen, a letter at a time; a click shows them all, the next one goes on.</summary>
    private bool ComputerCommand(EventCommand c, Action<bool> done)
    {
        string words = ScriptValues.Text(Formula(c, "Value"));
        double msPerLetter = Math.Max(1, FormulaNumber(c, "MsPerLetter"));
        var text = new TextBlock { Foreground = Brushes.LightGreen, FontFamily = new FontFamily("Consolas"), FontSize = 9, TextWrapping = TextWrapping.Wrap };
        var screen = new Border { Background = Brushes.Black, BorderBrush = Brushes.DarkGreen, BorderThickness = new Thickness(2), Padding = new Thickness(6), Width = 260, Height = 150, Child = text };
        ShowObjectOverlay(screen, takesClicks: true, scaleLikeViewport: true);
        int shown = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(msPerLetter) };
        void Show(int count) { shown = Math.Min(words.Length, count); text.Text = words[..shown]; if (shown >= words.Length) timer.Stop(); }
        timer.Tick += (_, _) => Show(shown + 1);
        timer.Start();
        bool finished = false;
        ClickToClose(screen, () =>
        {
            if (finished) return;
            if (shown < words.Length) { Show(words.Length); return; }
            finished = true;
            timer.Stop();
            CloseObjectOverlay();
            done(true);
        });
        return true;
    }

    // ---------- Recharge, alarm ----------

    /// <summary>BatteryChargerProcess, CruHealerProcess: a stat fills while they stand at it, until full or the time runs out.</summary>
    private bool RechargeCommand(EventCommand c, Action<bool> done)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        bool mana = c.Get("Stat") == "Mana";
        double perSecond = Math.Max(0.1, FormulaNumber(c, "PerSecond")), carry = 0, left = Math.Max(0, FormulaNumber(c, "Seconds")) * 1000;
        BeginScriptAction(who, elapsed =>
        {
            if (IsDead(who)) return false;
            carry += perSecond * elapsed / 1000;
            int whole = (int)carry;
            carry -= whole;
            int now = mana ? Stat(who, BuiltInStatKind.Mana, 0) : GetHp(who) ?? 0, most = mana ? Stat(who, BuiltInStatKind.Magic, 0) : MaxHp(who);
            if (whole > 0)
            {
                now = Math.Min(most, now + whole);
                if (mana) SetStat(who, BuiltInStatKind.Mana, now); else SetHp(who, now);
            }
            left -= elapsed;
            return now >= most || left <= 0 ? true : null;
        }, done);
        return true;
    }

    /// <summary>The alarm: everyone of a faction within reach goes for someone.</summary>
    private bool AlarmCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } target || !int.TryParse(c.Get("Faction"), out int faction)) return false;
        double cells = Math.Max(1, FormulaNumber(c, "Cells"));
        var (x, y) = PixelOf(target);
        int count = 0;
        foreach (var mapCharacter in _mapCharacters.ToList())
        {
            if (mapCharacter.Character.FactionId != faction || mapCharacter.Character == target || IsDead(mapCharacter.Character) || Has(mapCharacter.Character, Ailment.Surrendered)) continue;
            if (Math.Max(Math.Abs(mapCharacter.PixelX - x) / _tileWidth, Math.Abs(mapCharacter.PixelY - y) / _tileHeight) > cells) continue;
            StartCombat(mapCharacter, target);
            count++;
        }
        SoakLog($"alarm: {count} go for {target.Name}");
        return count > 0;
    }

    // ---------- Blows: difficulty, energy shields, surrender ----------

    /// <summary>Item::scaleReceivedDamageCru: by the variable the maker names (1, 2, 3...), what the main character and the party take, and what others take.</summary>
    private int AfterDifficulty(CharacterDefinition defender, int damage)
    {
        string name = U8Rules.DifficultyVariable.Trim();
        if (damage <= 0 || name.Length == 0) return damage;
        int level = (int)_execContext.GetVariable(name);
        int Percent(string list)
        {
            var parts = list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return level >= 1 && level <= parts.Length && int.TryParse(parts[level - 1], out int p) ? Math.Max(0, p) : 100;
        }
        int percent = Percent(IsPartyOrPlayer(defender) ? U8Rules.DifficultyPartyTakes : U8Rules.DifficultyOthersTake);
        return percent == 100 ? damage : Math.Max(damage > 0 && percent > 0 ? 1 : 0, damage * percent / 100);
    }

    /// <summary>MainActor's shields: this part of a blow comes off its Mana, as long as there is any.</summary>
    private int AfterEnergyShield(CharacterDefinition defender, int damage)
    {
        if (damage <= 0 || defender.U8ShieldPercent <= 0) return damage;
        int mana = Stat(defender, BuiltInStatKind.Mana, 0), absorbed = Math.Min(mana, damage * Math.Min(100, defender.U8ShieldPercent) / 100);
        if (absorbed <= 0) return damage;
        SetStat(defender, BuiltInStatKind.Mana, mana - absorbed);
        SoakLog($"shield: {defender.Name} absorbs {absorbed}");
        return damage - absorbed;
    }

    /// <summary>SurrenderProcess: hurt below its point, it gives up: it stops fighting and says so. It can still be struck.</summary>
    private void MaybeSurrender(CharacterDefinition who)
    {
        if (who.U8SurrenderPercent <= 0 || who == _player || who.IsInParty || IsDead(who) || Has(who, Ailment.Surrendered)) return;
        int max = MaxHp(who);
        if (max <= 0 || (GetHp(who) ?? max) * 100 >= max * who.U8SurrenderPercent) return;
        Inflict(who, Ailment.Surrendered);
        if (MapCharacterOf(who) is { } mapCharacter)
        {
            mapCharacter.Npc.InCombat = false;
            mapCharacter.Npc.Target = null;
            StopWalking(mapCharacter);
            if (HasArtFor(who, "Surrender")) ShowPose(who, "Surrender", restart: true);
        }
        if (who.U8SurrenderWords.Trim().Length > 0) ShowFloatingText(who, who.U8SurrenderWords.Trim(), 3000);
        SoakLog($"surrender: {who.Name}");
    }

    // ---------- Coming into and leaving view ----------

    private double _viewCheckMs;
    private readonly HashSet<MapCharacter> _inView = new(ReferenceEqualityComparer.Instance);

    /// <summary>Characters with By Coming Into View / By Leaving View pages: looked at twice a second (Item::enterFastArea, leaveFastArea).</summary>
    private void UpdateViewEvents(double elapsedMs)
    {
        if ((_viewCheckMs += elapsedMs) < 500) return;
        _viewCheckMs = 0;
        foreach (var mapCharacter in _mapCharacters.ToList())
        {
            var pages = mapCharacter.Character.EventPages;
            if (!pages.Any(p => p.Conditions.TriggerConditionBased is BookTriggerConditionBased.ByEnterView or BookTriggerConditionBased.ByLeaveView)) continue;
            bool now = IsOnScreen(mapCharacter.PixelX, mapCharacter.PixelY);
            if (now && _inView.Add(mapCharacter)) FireCharacterEvent(mapCharacter.Character, BookTriggerConditionBased.ByEnterView);
            else if (!now && _inView.Remove(mapCharacter)) FireCharacterEvent(mapCharacter.Character, BookTriggerConditionBased.ByLeaveView);
        }
    }
}
