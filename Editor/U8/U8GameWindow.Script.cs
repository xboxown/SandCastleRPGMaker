// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The script commands in play (Engine/ScriptCommands.cs; the data ones run
/// in Engine/ScriptRuntime.cs):
///  - A long loop carries on next frame (2000 steps a frame), so it never
///    freezes the game; Wait Until checks its condition every frame - both
///    keep who set the event off, as when it started.
///  - Formulas get the game's own functions: hp(Player), stat(Player,
///    "Strength"), x / y / floorof(who), gold(), items("Torch"), hour()...
///  - Control Self Switch: A-D for each placed thing (or event) on its own.
///  - Input Number / Input Text / Select Item: a box over the game, the event
///    waiting for it. Debug Log (F9 shows it) and Pause Here are Game Test
///    only - a built game ignores them.
/// </summary>
public partial class U8GameWindow
{
    private sealed class ConditionWait
    {
        public required Func<bool> Condition;
        public required Action GoOn;
        public double TimeoutMs, WaitedMs;
    }

    private List<Action> _deferredScripts = new();
    private readonly List<ConditionWait> _conditionWaits = new();
    /// <summary>A built game (not Game Test in the editor): no debug log, no Pause Here, no testing keys.</summary>
    public bool IsShippedGame { get; init; }

    private void InitializeScripts()
    {
        _execContext.OnKeepWhoSetItOff = KeepingWhoSetItOff;
        _execContext.OnDefer = goOn => _deferredScripts.Add(KeepingWhoSetItOff(goOn));
        _execContext.OnWaitUntil = (condition, timeout, goOn) => _conditionWaits.Add(new ConditionWait { Condition = KeepingWhoSetItOff(condition), GoOn = KeepingWhoSetItOff(goOn), TimeoutMs = timeout * 1000 });
        _execContext.CommonEvents = _systemSettings.CommonEvents;
        _execContext.OnGameFunction = GameFunction;
        InitializeScriptDolls(); // group 6 (U8GameWindow.ScriptDolls.cs)
        InitializeScriptWorld(); // groups 7-8 (U8GameWindow.ScriptWorld.cs)
        InitializeScriptGame(); // groups 9-11 (U8GameWindow.ScriptGame.cs)
        _execContext.ScriptData.Logged += line => { if (!IsShippedGame) StatusText.Text = "Debug: " + line; };

        var h = _execContext.GameHandlers;
        h[EventCommandType.ControlSelfSwitch] = ControlSelfSwitchCommand;
        h[EventCommandType.ChangeState] = ChangeStateCommand;
        h[EventCommandType.RemoveItem] = RemoveItemCommand;
        h[EventCommandType.ChangeHp] = ChangeHpCommand;
        h[EventCommandType.ChangeTile] = ChangeTileCommand;
        h[EventCommandType.GetGameData] = GetGameDataCommand;
        h[EventCommandType.GridFromMap] = GridFromMapCommand;
        h[EventCommandType.GridToMap] = GridToMapCommand;

        var a = _execContext.GameActions;
        a[EventCommandType.InputNumber] = (c, done) => AskInput(c, numbers: true, done);
        a[EventCommandType.InputText] = (c, done) => AskInput(c, numbers: false, done);
        a[EventCommandType.SelectItem] = SelectItemCommand;
        a[EventCommandType.PauseHere] = PauseHereCommand;
    }

    /// <summary>Formula values as numbers and texts, read from the command (errors go to the debug log).</summary>
    private object Formula(EventCommand command, string key)
    {
        if (command.Get(key + "Mode") == "Words") return command.Get(key); // words as typed (ScriptFieldKind.Words)
        try { return ScriptExpression.Evaluate(command.Get(key), _execContext); }
        catch (ScriptException ex) { _execContext.ScriptData.AddLog($"Script error in {ScriptCommands.Of(command.Type)?.Name}: {ex.Message}"); return 0.0; }
    }

    private double FormulaNumber(EventCommand command, string key) => ScriptValues.Number(Formula(command, key));

    private void SetScriptVariable(string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try { ScriptExpression.SetVariable(_execContext, name, value); }
        catch (ScriptException ex) { _execContext.ScriptData.AddLog(ex.Message); }
    }

    /// <summary>Something run later (a loop's next frame, the end of a wait) still acts for whoever set the event off, and as that event.</summary>
    private Action KeepingWhoSetItOff(Action action)
    {
        var (activator, step, owner, book) = (_activator, _activatedByCharacterStep, _firingOwner, _execContext.SelfBookId);
        return () =>
        {
            var (a0, s0, o0, b0) = (_activator, _activatedByCharacterStep, _firingOwner, _execContext.SelfBookId);
            (_activator, _activatedByCharacterStep, _firingOwner, _execContext.SelfBookId) = (activator, step, owner, book);
            try { action(); }
            finally { (_activator, _activatedByCharacterStep, _firingOwner, _execContext.SelfBookId) = (a0, s0, o0, b0); }
        };
    }

    private Func<bool> KeepingWhoSetItOff(Func<bool> condition)
    {
        bool result = false;
        var run = KeepingWhoSetItOff(() => result = condition());
        return () => { run(); return result; };
    }

    /// <summary>Every frame, paused or not: long loops go on, Wait Untils are checked.</summary>
    private void UpdateScripts(double elapsedMs)
    {
        UpdatePartSlides(elapsedMs); // Move Part over a time
        UpdateScreenTweens(elapsedMs); // fades, flashes, the camera moving
        if (!_gamePaused) UpdateFollowers(elapsedMs); // Follow, timed Change Schedule
        if (_deferredScripts.Count > 0)
        {
            var now = _deferredScripts;
            _deferredScripts = new List<Action>();
            foreach (var goOn in now) goOn();
        }
        for (int i = _conditionWaits.Count - 1; i >= 0; i--)
        {
            if (i >= _conditionWaits.Count) continue;
            var wait = _conditionWaits[i];
            wait.WaitedMs += elapsedMs;
            bool timedOut = wait.TimeoutMs > 0 && wait.WaitedMs >= wait.TimeoutMs;
            if (!timedOut && !wait.Condition()) continue;
            _conditionWaits.Remove(wait);
            wait.GoOn();
        }
    }

    // ---------- Who a formula means ----------

    /// <summary>"Player", "Self" / "Activator", a character's id, or its name.</summary>
    private CharacterDefinition? ScriptCharacter(object who)
    {
        string text = ScriptValues.Text(who).Trim();
        if (text.Length == 0 || text.Equals("Self", StringComparison.OrdinalIgnoreCase) || text.Equals(WhoChoice.Activator, StringComparison.OrdinalIgnoreCase))
            return _activator?.Character ?? _player;
        if (text.Equals(WhoChoice.Player, StringComparison.OrdinalIgnoreCase)) return _player;
        if (int.TryParse(text, out int id)) return _mapCharacters.FirstOrDefault(mc => mc.Character.Id == id)?.Character ?? GetPartyList().FirstOrDefault(c => c.Id == id);
        return (_player?.Name.Equals(text, StringComparison.OrdinalIgnoreCase) == true ? _player : null)
            ?? _mapCharacters.FirstOrDefault(mc => mc.Character.Name.Equals(text, StringComparison.OrdinalIgnoreCase))?.Character;
    }

    /// <summary>The game's formula functions.</summary>
    private object? GameFunction(string name, IReadOnlyList<object> args)
    {
        CharacterDefinition? Who(int i = 0) => ScriptCharacter(args.Count > i ? args[i] : "Self");
        switch (name)
        {
            case "hp": return Who() is { } c1 ? (double)(GetHp(c1) ?? 0) : 0.0;
            case "maxhp": return Who() is { } c2 ? (double)MaxHp(c2) : 0.0;
            case "stat":
            {
                if (Who() is not { } c || args.Count < 2) return 0.0;
                string statName = ScriptValues.Text(args[1]);
                var stat = _stats.Stats.FirstOrDefault(s => s.Name.Equals(statName, StringComparison.OrdinalIgnoreCase));
                if (stat is null || !c.StatValues.TryGetValue(stat.Id, out var value)) return 0.0;
                return ScriptValues.Literal(value);
            }
            case "x": return Who() is { } cx ? (double)CellOf(cx).X : -1.0;
            case "y": return Who() is { } cy ? (double)CellOf(cy).Y : -1.0;
            case "floorof": return Who() is { } cf ? (double)MapFloors.FloorOfLift(LiftOf(cf)) : 1.0;
            case "name": return Who()?.Name ?? "";
            case "isdead": return Who() is { } cd && IsDead(cd) ? 1.0 : 0.0;
            case "inparty": return Who() is { } cp && (cp == _player || cp.IsInParty) ? 1.0 : 0.0;
            case "food": return Who() is { } cfo ? (double)VitalsOf(cfo).Food : 0.0;
            case "facing": return Who() is { } cfa ? (cfa == _player ? _playerFacing : MapCharacterOf(cfa)?.Facing ?? SpriteDirection.South).ToString() : "";
            case "partysize": return (double)PartyAndPlayer().Count();
            case "gold": return (double)PartyMoney();
            case "items":
            {
                if (args.Count < 1) return 0.0;
                string itemName = ScriptValues.Text(args[0]);
                var ids = _objects.Objects.Where(o => o.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)).Select(o => o.Id).ToHashSet();
                return (double)PartyItems().Where(p => p.Item.Category == ReferencedItemCategory.Object && ids.Contains(p.Item.ItemId)).Sum(p => Math.Max(1, p.Item.Quantity));
            }
            case "hour": return (double)_clock.Hour;
            case "minute": return (double)_clock.Minute;
            case "day": return (double)_clock.Day;
            case "self": return _execContext.ScriptData.SelfSwitches.Contains(SelfSwitchKey(args.Count > 0 ? ScriptValues.Text(args[0]) : "A")) ? 1.0 : 0.0;
            case "paperdollopen":
            {
                string dollName = args.Count > 0 ? ScriptValues.Text(args[0]) : "";
                return _openDolls.Any(g => g.Design.Name.Equals(dollName, StringComparison.OrdinalIgnoreCase)) ? 1.0 : 0.0;
            }
            case "combat": return _combatMode ? 1.0 : 0.0;
        }
        return null;
    }

    /// <summary>Every coin the party carries, in the first currency's units (a Money object's count times its currency's worth).</summary>
    private double PartyMoney()
    {
        var currencies = ProjectSession.Currencies.Currencies;
        double total = 0;
        foreach (var (item, _, _) in PartyItems())
        {
            if (item.Category != ReferencedItemCategory.Object || ObjectDef(item.ItemId) is not { Type: ObjectType.Money } money) continue;
            double worth = currencies.FirstOrDefault(c => c.Id == money.Money.CurrencyId)?.Value ?? 1;
            total += Math.Max(1, item.Quantity) * worth;
        }
        return total;
    }

    // ---------- Self switches ----------

    /// <summary>"what's running the event|A": each placed thing, event, character or paperdoll part has its own four.</summary>
    private string SelfSwitchKey(string letter)
    {
        object? owner = _firingOwner ?? _activator?.Character;
        string key = owner switch
        {
            ObjectInstance o => $"obj:{o.InstanceId}",
            PlacedItemInstance p => $"item:{p.Category}:{p.ItemId}:{p.X}:{p.Y}:{p.InstanceId}",
            InventoryItemInstance i => $"inv:{i.Category}:{i.ItemId}:{i.InstanceId}",
            GameEvent e => $"event:{e.Id}",
            CharacterDefinition c => $"char:{c.Id}",
            ValueTuple<DollState, int> part => $"doll:{part.Item1.DesignId}:{part.Item2}",
            _ => "global"
        };
        return key + "|" + letter.Trim().ToUpperInvariant();
    }

    private bool ControlSelfSwitchCommand(EventCommand command)
    {
        string key = SelfSwitchKey(command.Get("Letter"));
        if (command.Get("Value") == "Off") _execContext.ScriptData.SelfSwitches.Remove(key);
        else _execContext.ScriptData.SelfSwitches.Add(key);
        return true;
    }

    // ---------- Group 1 ----------

    private bool ChangeStateCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || !Enum.TryParse<Ailment>(command.Get("State"), out var state)) return false;
        if (command.Get("Value") == "Off") { Cure(who, state); return true; }
        return Inflict(who, state);
    }

    private bool RemoveItemCommand(EventCommand command)
    {
        if (!Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) || !int.TryParse(command.Get("ItemId"), out int itemId)) return false;
        int wanted = Math.Max(1, (int)FormulaNumber(command, "Quantity"));
        var carriers = command.Get("FromParty") == "True" ? PartyAndPlayer().ToList()
            : CharacterDefinitionFor(command.Get("Who")) is { } one ? new List<CharacterDefinition> { one } : new List<CharacterDefinition>();
        int left = wanted;
        foreach (var carrier in carriers)
        {
            while (left > 0)
            {
                int have = CountCarried(carrier, category, itemId);
                if (have <= 0) break;
                int take = Math.Min(have, left);
                if (TakeFromCarrier(carrier, category, itemId, take) is null) break;
                left -= take;
            }
            if (left <= 0) break;
        }
        RefreshAfterMove();
        FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByNotHavingAnItem);
        return left == 0;
    }

    private static int CountCarried(CharacterDefinition carrier, ReferencedItemCategory category, int itemId)
    {
        int count = 0;
        void Walk(List<InventoryItemInstance> list)
        {
            foreach (var item in list)
            {
                if (item.Category == category && item.ItemId == itemId) count += Math.Max(1, item.Quantity);
                Walk(item.Contents);
            }
        }
        Walk(carrier.Inventory);
        return count;
    }

    private bool ChangeHpCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who) || GetHp(who) is not int hp) return false;
        int amount = (int)Math.Round(FormulaNumber(command, "Amount"));
        int target = command.Get("Operation") switch { "Subtract" => hp - amount, "Set" => amount, _ => hp + amount };
        target = Math.Min(target, Math.Max(MaxHp(who), hp));
        bool canKill = command.Get("CanKill") == "True";
        if (target < hp)
        {
            if (!canKill) target = Math.Max(target, -(MaxHp(who) / 3)); // knocked out at worst
            DamageAllowed(who, hp - target, "an event");
            return true;
        }
        SetHp(who, target);
        if (target > 0 && IsUnconscious(who)) { /* Exult: they come round on their own once healed */ }
        return true;
    }

    private bool ChangeTileCommand(EventCommand command)
    {
        if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y) || !InsideMap(x, y)) return false;
        int floor = int.TryParse(command.Get("Floor"), out int f) ? f : 1;
        int tile = (int)FormulaNumber(command, "TileIndex");
        return SetMapTile(x, y, floor, tile);
    }

    // ---------- Get Game Data ----------

    private bool GetGameDataCommand(EventCommand command)
    {
        var who = CharacterDefinitionFor(command.Get("Who"));
        string whoKey = who is null ? "" : who == _player ? WhoChoice.Player : who.Id.ToString();
        object? value = command.Get("What") switch
        {
            "X (cell)" => GameFunction("x", new object[] { whoKey }),
            "Y (cell)" => GameFunction("y", new object[] { whoKey }),
            "Floor" => GameFunction("floorof", new object[] { whoKey }),
            "HP" => GameFunction("hp", new object[] { whoKey }),
            "Max HP" => GameFunction("maxhp", new object[] { whoKey }),
            "Stat" => GameFunction("stat", new object[] { whoKey, command.Get("StatName") }),
            "Gold (all money)" => PartyMoney(),
            "Item count" => Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) && int.TryParse(command.Get("ItemId"), out int itemId) && who is not null
                ? (double)CountCarried(who, category, itemId) : 0.0,
            "Food level" => GameFunction("food", new object[] { whoKey }),
            "Name" => who?.Name ?? "",
            "Is dead" => GameFunction("isdead", new object[] { whoKey }),
            "Is in party" => GameFunction("inparty", new object[] { whoKey }),
            "Party size" => GameFunction("partysize", Array.Empty<object>()),
            "Hour" => (double)_clock.Hour,
            "Minute" => (double)_clock.Minute,
            "Day" => (double)_clock.Day,
            "Map name" => _map.MapName,
            "Leader's name" => _player?.Name ?? "",
            "Facing" => GameFunction("facing", new object[] { whoKey }),
            _ => null
        };
        if (value is null) return false;
        SetScriptVariable(command.Get("Variable"), value);
        return true;
    }

    // ---------- Grids and the map ----------

    private bool GridFromMapCommand(EventCommand command)
    {
        if (!int.TryParse(command.Get("X"), out int x0) || !int.TryParse(command.Get("Y"), out int y0)) return false;
        int floor = int.TryParse(command.Get("Floor"), out int f) ? f : 1;
        int w = Math.Clamp((int)FormulaNumber(command, "Width"), 1, 2000), h = Math.Clamp((int)FormulaNumber(command, "Height"), 1, 2000);
        var layer = _map.LayerForFloor(floor);
        var grid = new ScriptGrid(w, h, -1.0);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                grid[x, y] = (double)(layer?.GetTile(x0 + x, y0 + y, _map.Width) ?? -1);
        _execContext.ScriptData.Grids[command.Get("Grid")] = grid;
        return true;
    }

    private bool GridToMapCommand(EventCommand command)
    {
        if (!int.TryParse(command.Get("X"), out int x0) || !int.TryParse(command.Get("Y"), out int y0)) return false;
        if (!_execContext.ScriptData.Grids.TryGetValue(command.Get("Grid"), out var grid)) return false;
        int floor = int.TryParse(command.Get("Floor"), out int f) ? f : 1;
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Columns; x++)
            {
                int tile = (int)ScriptValues.Number(grid[x, y]);
                if (tile >= 0) SetMapTile(x0 + x, y0 + y, floor, tile);
            }
        return true;
    }

    // ---------- Input boxes ----------

    private Border? _scriptInput;

    /// <summary>Input Number / Input Text: a box over the game; Enter accepts, Escape keeps what was there.</summary>
    private bool AskInput(EventCommand command, bool numbers, Action<bool> done)
    {
        string variable = command.Get("Variable");
        int longest = Math.Clamp((int)FormulaNumber(command, numbers ? "Digits" : "Letters"), 1, 200);
        string start = numbers ? ScriptValues.Text(ScriptExpression.GetVariable(_execContext, variable)) : ScriptValues.Text(Formula(command, "Start"));
        var box = new TextBox { Text = start.Length > longest ? start[..longest] : start, MaxLength = longest, MinWidth = 200, FontSize = 16, Margin = new Thickness(0, 8, 0, 0) };
        if (numbers) box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(ch => char.IsDigit(ch) || ch == '-');
        ShowScriptInput(command.Get("Prompt"), box, accepted =>
        {
            if (accepted) SetScriptVariable(variable, numbers ? ScriptValues.Number(box.Text) : box.Text);
            done(accepted);
        });
        box.Focus();
        box.SelectAll();
        return true;
    }

    /// <summary>Select Item: the main character's things in a list; the one picked - its name and id into the variables.</summary>
    private bool SelectItemCommand(EventCommand command, Action<bool> done)
    {
        if (_player is null) return false;
        var items = new List<(string Name, int Id)>();
        void Walk(List<InventoryItemInstance> list)
        {
            foreach (var item in list)
            {
                items.Add((DollLook.ItemName(new ItemReference { Category = item.Category, ItemId = item.ItemId }, _objects, _books, _containers), item.ItemId));
                Walk(item.Contents);
            }
        }
        Walk(_player.Inventory);
        var list = new ListBox { ItemsSource = items.Select(i => i.Name).ToList(), MinWidth = 220, MaxHeight = 220, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) };
        if (items.Count > 0) list.SelectedIndex = 0;
        ShowScriptInput(command.Get("Prompt"), list, accepted =>
        {
            bool picked = accepted && list.SelectedIndex >= 0;
            SetScriptVariable(command.Get("Variable"), picked ? items[list.SelectedIndex].Name : "");
            if (command.Get("IdVariable").Length > 0) SetScriptVariable(command.Get("IdVariable"), picked ? (double)items[list.SelectedIndex].Id : -1.0);
            done(picked);
        });
        list.MouseDoubleClick += (_, _) => FinishScriptInput(true);
        list.Focus();
        return true;
    }

    private Action<bool>? _scriptInputDone;

    private void ShowScriptInput(string prompt, Control input, Action<bool> finished)
    {
        FinishScriptInput(false);
        var panel = new StackPanel();
        if (prompt.Length > 0) panel.Children.Add(new TextBlock { Text = prompt, Foreground = Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = $"{KeyWords("Accept")}: OK    {KeyWords("Cancel")}: cancel", Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        _scriptInput = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 16, 12)), BorderBrush = Brushes.Goldenrod, BorderThickness = new Thickness(2),
            Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = panel
        };
        Panel.SetZIndex(_scriptInput, 900000);
        input.PreviewKeyDown += (_, e) =>
        {
            var pressed = U8KeyCapture.RealKey(e);
            if (TypingKeyIs(pressed, "Accept")) { FinishScriptInput(true); e.Handled = true; }
            else if (TypingKeyIs(pressed, "Cancel")) { FinishScriptInput(false); e.Handled = true; }
        };
        _scriptInputDone = finished;
        ((Grid)Content).Children.Add(_scriptInput);
    }

    private void FinishScriptInput(bool accepted)
    {
        if (_scriptInput is null) return;
        ((Grid)Content).Children.Remove(_scriptInput);
        _scriptInput = null;
        var done = _scriptInputDone;
        _scriptInputDone = null;
        GameCanvas.Focus();
        done?.Invoke(accepted);
    }

    // ---------- Testing helpers ----------

    private bool PauseHereCommand(EventCommand command, Action<bool> done)
    {
        if (IsShippedGame) { done(true); return true; }
        var window = new U8ScriptStateWindow(_execContext, "Paused here - the event goes on when you press Continue.") { Owner = this };
        window.Closed += (_, _) => { done(true); GameCanvas.Focus(); };
        window.Show();
        return true;
    }

    /// <summary>F9 in Game Test: the debug log and every variable, text, array and grid as they are now.</summary>
    private void ShowScriptState()
    {
        if (IsShippedGame) return;
        new U8ScriptStateWindow(_execContext, $"The game's script data now ({KeyWords("TestScriptState")}).") { Owner = this }.Show();
    }
}

/// <summary>Game Test's view of the script data: the variables (numbers and texts), arrays, grids, self switches and the debug log.</summary>
internal sealed class U8ScriptStateWindow : Window
{
    public U8ScriptStateWindow(BookEventPreviewExecutionContext context, string heading)
    {
        Title = "Script Data";
        Width = 560;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var data = context.ScriptData;
        var text = new System.Text.StringBuilder();
        text.AppendLine(heading).AppendLine();
        text.AppendLine("Number variables and switches:");
        foreach (var (name, value) in context.Variables.OrderBy(kv => kv.Key)) text.AppendLine($"  {name} = {ScriptValues.Text(value)}");
        text.AppendLine().AppendLine("Text variables:");
        foreach (var (name, value) in data.Texts.OrderBy(kv => kv.Key)) text.AppendLine($"  {name} = \"{value}\"");
        text.AppendLine().AppendLine("Arrays:");
        foreach (var (name, list) in data.Arrays.OrderBy(kv => kv.Key)) text.AppendLine($"  {name} ({list.Count}) = [{string.Join(", ", list.Take(60).Select(Show))}{(list.Count > 60 ? ", ..." : "")}]");
        text.AppendLine().AppendLine("Grids:");
        foreach (var (name, grid) in data.Grids.OrderBy(kv => kv.Key))
        {
            text.AppendLine($"  {name} ({grid.Columns} x {grid.Rows}):");
            for (int y = 0; y < Math.Min(grid.Rows, 24); y++)
                text.AppendLine("    " + string.Join(" ", Enumerable.Range(0, Math.Min(grid.Columns, 24)).Select(x => Show(grid[x, y]).PadLeft(3))));
        }
        text.AppendLine().AppendLine("Self switches on:");
        foreach (var s in data.SelfSwitches.OrderBy(s => s)) text.AppendLine("  " + s);
        text.AppendLine().AppendLine("Debug log (newest last):");
        foreach (var line in data.Log.TakeLast(200)) text.AppendLine("  " + line);

        var root = new DockPanel { Margin = new Thickness(10) };
        var close = new Button { Content = "Continue", Width = 100, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new TextBox { Text = text.ToString(), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    private static string Show(object value) => value is string s ? $"\"{s}\"" : ScriptValues.Text(value);
}
