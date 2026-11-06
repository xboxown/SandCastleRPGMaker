// Ultima 6 engine (U6GameWindow): Ultima 6's levels, Rest, searching and the mouse - Ultima 6 projects only.
using System.Windows;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6 as Nuvie plays it (Engine/U6Leveling):
///  - Levels: experience comes from kills, but a level isn't taken by itself. At a shrine (Meditate At Shrine) every member who
///    has earned one rises and gets the virtue's points; or, Savage Empire / Martian Dreams, in a dream when the party rests.
///    The most hit points are 30 a level. Off when System Settings > Ultima 6 turns Ultima 7 leveling on.
///  - Rest (Events::rest, Party::can_rest, TimedRest): not in combat, with foes or others near, or in solo mode; "How many
///    hours?", "Who will guard?", mealtime, sleep; fed members heal - once every 12 hours.
///  - The mouse (MapWindow): a click looks (any distance), a double click "multiuses": talks to someone (attacks in combat),
///    uses a thing - only from the square next to it. A container used on the map is searched: what's in it spills out.
/// </summary>
public partial class U6GameWindow
{
    private bool U6LevelsOn => !_systemSettings.Ultima6.UseUltima7Leveling;

    // ---------- Levels ----------

    /// <summary>At the start (Nuvie's actor init: level = (hp + 29) / 30): someone with more hit points than their level allows is that much higher a level.</summary>
    private void InitU6Levels()
    {
        _execContext.GameHandlers[EventCommandType.U6Meditate] = MeditateCommand;
        _execContext.GameHandlers[EventCommandType.U6RiseLevel] = RiseLevelCommand;
        _execContext.GameActions[EventCommandType.U6RunLevelEvent] = (c, done) =>
        {
            if (LevelSystem?.Events.FirstOrDefault(e => string.Equals(e.Name, c.Get("Name").Trim(), StringComparison.OrdinalIgnoreCase)) is not { } called) return false;
            RunLevelCommands(called.Commands, _activator?.Character, () => done(true));
            return true;
        };
        U6Leveling.Use(_systemSettings.Ultima6);
        if (!U6LevelsOn) return;
        var everyone = new List<CharacterDefinition>();
        if (_player is not null) everyone.Add(_player);
        if (_characters is not null) everyone.AddRange(_characters.Characters.Where(c => c != _player));
        foreach (var c in everyone)
        {
            int level = Math.Clamp(Stat(c, BuiltInStatKind.Level, 1), 1, U6Leveling.MaxLevel);
            if (GetHp(c) is int hp && hp > U6Leveling.MaxHits(level)) level = Math.Clamp((hp + 29) / 30, 1, U6Leveling.MaxLevel);
            SetStat(c, BuiltInStatKind.Level, level);
            SetStat(c, BuiltInStatKind.NextExp, U6Leveling.NextExp(level));
        }
    }

    /// <summary>Meditate At Shrine: every member (alive) who has earned a level rises, with the virtue's points.</summary>
    private bool MeditateCommand(EventCommand command)
    {
        if (!U6LevelsOn || _player is null) return false;
        var shrine = U6Leveling.Shrines.FirstOrDefault(s => s.Virtue == command.Get("Virtue"));
        var (str, dex, intel) = shrine.Virtue is null
            ? ((int)FormulaNumber(command, "Str"), (int)FormulaNumber(command, "Dex"), (int)FormulaNumber(command, "Int"))
            : (shrine.Str, shrine.Dex, shrine.Int);
        bool any = false;
        foreach (var member in GetPartyList().Where(m => !IsDead(m)))
        {
            if (!U6Leveling.HasEarned(Stat(member, BuiltInStatKind.Level, 1), Stat(member, BuiltInStatKind.Exp, 0))) continue;
            RiseLevel(member, str, dex, intel, U6Leveling.HitsPerLevel, tell: true);
            any = true;
        }
        if (!any) U6Say(W("NotReady", "", _player.Name));
        RefreshHudPanel();
        return any;
    }

    /// <summary>The Leveling Editor's system in use (none: levels are only what events set).</summary>
    private U6LevelSystem? LevelSystem => U6LevelsOn ? _systemSettings.Ultima6.Active : null;

    /// <summary>Experience gained: a level earned - taken at once (Rise by themselves) or not - and the system's When someone earns a level events run, once a level.</summary>
    private void U6ExperienceGained(CharacterDefinition who)
    {
        int level = Math.Max(1, Stat(who, BuiltInStatKind.Level, 1));
        int exp = Stat(who, BuiltInStatKind.Exp, 0);
        int cap = who.LevelLock > 0 ? Math.Min(who.LevelLock, U6Leveling.MaxLevel) : U6Leveling.MaxLevel;
        if (LevelSystem is { RiseByThemselves: true })
        {
            while (level < cap && U6Leveling.HasEarned(level, exp))
            {
                RiseLevel(who, 0, 0, 0, U6Leveling.HitsPerLevel, tell: true);
                level++;
                who.U6LevelNoticed = level;
                LevelEvents(U6LevelEvent.WhenEarned, who, level, rose: true);
            }
        }
        else if (level < cap && U6Leveling.HasEarned(level, exp) && who.U6LevelNoticed < level + 1)
        {
            who.U6LevelNoticed = level + 1;
            LevelEvents(U6LevelEvent.WhenEarned, who, level + 1, rose: false);
        }
        SetStat(who, BuiltInStatKind.NextExp, U6Leveling.NextExp(level));
    }

    /// <summary>The variables a leveling event reads: LevelWho, LevelWhoId, LevelNow, LevelEarned, LevelRose.</summary>
    private void SetLevelVariables(CharacterDefinition who, int earned, bool rose)
    {
        SetScriptVariable("LevelWho", who.Name);
        SetScriptVariable("LevelWhoId", (double)who.Id);
        SetScriptVariable("LevelNow", (double)Stat(who, BuiltInStatKind.Level, 1));
        SetScriptVariable("LevelEarned", (double)earned);
        SetScriptVariable("LevelRose", rose ? 1.0 : 0.0);
    }

    private void LevelEvents(string trigger, CharacterDefinition who, int earned, bool rose)
    {
        if (LevelSystem is not { } system) return;
        foreach (var e in system.Events.Where(e => e.Trigger == trigger && e.Commands.Count > 0))
        {
            SetLevelVariables(who, earned, rose);
            RunLevelCommands(e.Commands, who, null);
        }
    }

    /// <summary>A leveling event's commands, with the one it is about as its Self (Activator) till they finish.</summary>
    private void RunLevelCommands(List<EventCommand> commands, CharacterDefinition? who, Action? after)
    {
        var previous = (_activator, _activatedByCharacterStep);
        _activator = who is null || who == _player ? null : MapCharacterOf(who);
        _activatedByCharacterStep = false;
        _execContext.SelfBookId = -1;
        EventCommandInterpreter.Run(commands.ToList(), _execContext, new List<string>(), finished: () =>
        {
            (_activator, _activatedByCharacterStep) = previous;
            RefreshHudPanel();
            after?.Invoke();
        });
    }

    /// <summary>Rise A Level: someone (or the party) up a level with these points - only if earned, if so asked.</summary>
    private bool RiseLevelCommand(EventCommand c)
    {
        if (!U6LevelsOn) return false;
        var targets = c.Get("Party") == "True" ? GetPartyList().Where(m => !IsDead(m)).ToList()
            : CharacterDefinitionFor(c.Get("Who")) is { } one ? new List<CharacterDefinition> { one } : new List<CharacterDefinition>();
        bool onlyIfEarned = c.Get("OnlyIfEarned") != "False", tell = c.Get("Tell") != "False";
        int hits = c.Get("Hits").Trim().Length == 0 ? U6Leveling.HitsPerLevel : (int)FormulaNumber(c, "Hits");
        bool any = false;
        foreach (var member in targets)
        {
            int level = Stat(member, BuiltInStatKind.Level, 1);
            int cap = member.LevelLock > 0 ? Math.Min(member.LevelLock, U6Leveling.MaxLevel) : U6Leveling.MaxLevel;
            if (level >= cap || (onlyIfEarned && !U6Leveling.HasEarned(level, Stat(member, BuiltInStatKind.Exp, 0)))) continue;
            RiseLevel(member, (int)FormulaNumber(c, "Str"), (int)FormulaNumber(c, "Dex"), (int)FormulaNumber(c, "Int"), hits, tell);
            any = true;
        }
        if (!any && c.Get("NotReady").Trim().Length > 0) U6Say(SceneWords(c.Get("NotReady")));
        RefreshHudPanel();
        return any;
    }

    /// <summary>One level up: the stats' points (never past the system's limit), hit points back (never past the new most), told in the text area if asked.</summary>
    private void RiseLevel(CharacterDefinition member, int str, int dex, int intel, int hitsBack, bool tell)
    {
        int level = Math.Clamp(Stat(member, BuiltInStatKind.Level, 1) + 1, 1, U6Leveling.MaxLevel);
        SetStat(member, BuiltInStatKind.Level, level);
        SetStat(member, BuiltInStatKind.NextExp, U6Leveling.NextExp(level));
        var gains = new List<string>();
        void Raise(BuiltInStatKind kind, int by, string word)
        {
            if (by == 0) return;
            int was = Stat(member, kind, 0), now = Math.Clamp(was + by, 0, U6Leveling.StatCap);
            SetStat(member, kind, now);
            if (now != was) gains.Add($"{now - was} {W(word)}");
        }
        Raise(BuiltInStatKind.Strength, str, "Strength");
        Raise(BuiltInStatKind.Dexterity, dex, "Dexterity");
        Raise(BuiltInStatKind.Intelligence, intel, "Intelligence");
        if (GetHp(member) is int hp) SetHp(member, Math.Min(U6Leveling.MaxHits(level), hp + Math.Max(0, hitsBack)));
        member.U6LevelNoticed = Math.Max(member.U6LevelNoticed, level);
        if (!tell) return;
        U6Say(W("NewLevel", level.ToString(), member.Name));
        if (gains.Count > 0) U6Say(W("Gains", string.Join(gains.Count == 2 ? " and " : ", ", gains), member.Name));
    }

    // ---------- A key asked for in the text area (Rest's hours, its guard, a dream's choice) ----------

    private (string Allowed, Action<char> Answer)? _letterAsk;

    private void AskLetter(string question, string allowed, Action<char> answer)
    {
        _hudPrompt = question;
        RefreshHudText();
        _letterAsk = (allowed, answer);
    }

    /// <summary>A key asked for takes the key press first: only the allowed ones (Escape: the first, "0" - none).</summary>
    private bool LetterAskTook(KeyEventArgs e)
    {
        if (_letterAsk is not { } ask) return false;
        e.Handled = true;
        if (e.IsRepeat) return true;
        var key = U8KeyCapture.RealKey(e);
        char? c = key is >= Key.D0 and <= Key.D9 ? (char)('0' + (key - Key.D0))
            : key is >= Key.NumPad0 and <= Key.NumPad9 ? (char)('0' + (key - Key.NumPad0))
            : key is >= Key.A and <= Key.Z ? (char)('A' + (key - Key.A))
            : key == Key.Escape && ask.Allowed.Contains('0') ? '0'
            : key is Key.Space or Key.Enter or Key.Return ? ' ' : null;
        if (c is not { } letter || !ask.Allowed.Contains(letter)) return true;
        _letterAsk = null;
        string question = _hudPrompt;
        _hudPrompt = string.Empty;
        if (letter != ' ') U6Say(question + letter);
        ask.Answer(letter);
        return true;
    }

    // ---------- Rest ----------

    private long _restHealsAfter;

    private void U6Rest()
    {
        if (_player is null) return;
        string verb = W("Rest");
        string? refused = _combatMode ? W("NotWhileCombat")
            : HostileNearby() || _mapCharacters.Any(mc => !mc.Character.IsInParty && !IsDead(mc.Character) && mc.Npc.InCombat && IsOnScreen(mc.PixelX, mc.PixelY)) ? W("FoesNear")
            : _mapCharacters.Any(mc => !mc.Character.IsInParty && !IsDead(mc.Character) && SameFloor(mc.Character, _player)
                && Math.Max(Math.Abs(mc.Placement.X - _playerX), Math.Abs(mc.Placement.Y - _playerY)) <= 5) ? W("OthersNear")
            : _soloMode ? W("RestNotSolo")
            : null;
        if (refused is not null) { U6Say(verb + refused); return; }
        U6Say(verb);
        AskLetter(W("HowManyHours"), "0123456789", hoursKey =>
        {
            int hours = hoursKey - '0';
            if (hours == 0) return;
            var party = GetPartyList().Where(m => !IsDead(m)).ToList();
            if (party.Count <= 1) { Sleep(hours, null, party); return; }
            AskLetter(W("WhoGuards"), "0123456789".Substring(0, Math.Min(10, party.Count + 1)), guardKey =>
            {
                int g = guardKey - '0';
                var guard = g >= 1 && g <= party.Count ? party[g - 1] : null;
                U6Say(guard?.Name ?? W("NoOne"));
                Sleep(hours, guard, party);
            });
        });
    }

    private void Sleep(int hours, CharacterDefinition? guard, List<CharacterDefinition> party)
    {
        // Mealtime (TimedRest::eat): each eats one food, their own or anyone's in the party.
        U6Say(W("Mealtime"));
        var fed = new HashSet<CharacterDefinition>();
        foreach (var member in party)
        {
            if (EatOneFood(member)) { fed.Add(member); U6Say(W("HasFood", "", member.Name)); }
            else U6Say(W("NoFood", "", member.Name));
        }
        if (guard is not null) U6Say(W("StandsGuard", "", guard.Name));
        int changed = _clock.AdvanceMinutes(hours * 60);
        if (changed > 0) ScheduleNpcs(_clock.Hour);
        ShowClock();
        // Waking (~TimedRest): those asleep and fed heal - once every 12 hours.
        long now = TotalMinutes();
        if (now >= _restHealsAfter)
        {
            foreach (var member in party.Where(m => m != guard && fed.Contains(m)))
            {
                if (GetHp(member) is not int hp) continue;
                int diff = MaxHp(member) - hp;
                if (diff <= 0) continue;
                if (diff == 1) diff = 2;
                SetHp(member, Math.Min(MaxHp(member), hp + Random.Shared.Next(Math.Max(1, diff / 2)) + diff / 2));
                U6Say(W("Healed", "", member.Name));
            }
            _restHealsAfter = now + 12 * 60;
        }
        RefreshHudPanel();
        // The leveling system's rest events (Leveling Editor): once, then once for each member who has earned a level - a dream, anything.
        if (LevelSystem is { } system)
        {
            SetScriptVariable("RestHours", (double)hours);
            SetScriptVariable("RestGuard", guard?.Name ?? "");
            foreach (var e in system.Events.Where(e => e.Trigger == U6LevelEvent.WhenRested && e.Commands.Count > 0)) RunLevelCommands(e.Commands, null, null);
            var each = system.Events.Where(e => e.Trigger == U6LevelEvent.WhenRestedEach).SelectMany(e => e.Commands).ToList();
            if (each.Count > 0)
                RestEach(each, party.Where(m => m != guard && U6Leveling.HasEarned(Stat(m, BuiltInStatKind.Level, 1), Stat(m, BuiltInStatKind.Exp, 0))).ToList(), 0);
        }
    }

    /// <summary>The rest events for each member who has earned a level, one after the other.</summary>
    private void RestEach(List<EventCommand> commands, List<CharacterDefinition> members, int index)
    {
        if (index >= members.Count) { RefreshHudPanel(); return; }
        var member = members[index];
        SetLevelVariables(member, Stat(member, BuiltInStatKind.Level, 1) + 1, rose: false);
        RunLevelCommands(commands, member, () => RestEach(commands, members, index + 1));
    }

    private bool EatOneFood(CharacterDefinition member)
    {
        foreach (var who in new[] { member }.Concat(GetPartyList().Where(m => m != member)))
            foreach (var (item, list, carrier) in CarriedItems(who))
            {
                if (DefinitionOf(item) is not { Type: ObjectType.Food, Food.IsLiquid: false }) continue;
                if (item.Quantity > 1) item.Quantity--;
                else if (ReferenceEquals(list, carrier.Inventory)) RemoveFromCharacter(carrier, item);
                else list.Remove(item);
                return true;
            }
        return false;
    }

    // ---------- Searching a container on the map ----------

    /// <summary>U6UseCode::use_container on the map: locked - no; otherwise "Searching here, you find ..." and what's in it spills out where it stands.</summary>
    private void U6Search(PlacedItemInstance box, ContainerData data)
    {
        if (IsLockedNow(box, data)) { PlaySound(data.LockedRattleSoundResourceId); U6Say(W("Locked")); return; }
        if (TryFireEventPages(data, data.EventPages, BookTriggerConditionBased.ByUseItem, data.Id, string.Empty)) return; // its own events say what opening it does
        _hudHeard = new List<string>(); // the engine's own notes ("Opened ... - playing ...") stay out of the text area
        try { PlayOpenSound(data.OpenSoundResourceId, data.Name); EnsureContainerSeeded(box, data); }
        finally { _hudHeard = null; }
        var found = box.Contents.ToList();
        if (found.Count == 0) { U6Say(W("SearchNothing")); return; }
        box.Contents.Clear();
        var names = new List<string>();
        _hudHeard = new List<string>(); // the engine's "put down" words stay out
        try
        {
            foreach (var item in found)
            {
                names.Add(item.Quantity > 1 ? $"{item.Quantity} {HudItemName(item)}" : HudItemName(item));
                PutDownAt(item, _player!, (box.X, box.Y));
            }
        }
        finally { _hudHeard = null; }
        U6Say(W("Searching", string.Join(", ", names)));
    }

    // ---------- The mouse ----------

    /// <summary>A click on the map (MapWindow::MouseDelayed, look_on_left_click): "Look-" what is there, from any distance.</summary>
    private void U6ClickLook(InteractionTarget target)
    {
        if (_hudVerb is not null || _letterAsk is not null || _currentMessage is not null) return;
        object? thing = target switch { PlayerTarget => _player, MapEntityTarget m => m.Entity, _ => null };
        if (thing is not null && SquareOfThing(thing) is { } at && !SquareSeen(at.X, at.Y)) thing = null; // black: nothing is seen there
        string name = ThingName(thing);
        U6Say(W("Look") + "-" + name);
        U6Say(W("YouSee", name));
    }

    /// <summary>A double click on the map (Events::multiuse): someone - talk (in combat, attack a foe); a thing - use it (in combat, attack it); the avatar - nothing.</summary>
    private void U6MultiUse(InteractionTarget target)
    {
        if (_hudVerb is not null || _letterAsk is not null || _currentMessage is not null || _player is null || PlayerKnockedOut) return;
        if (target is MapEntityTarget { Entity: var e } && SquareOfThing(e) is { } at && !SquareSeen(at.X, at.Y)) return; // Events::multiuse: a black square - nothing
        switch (target)
        {
            case MapEntityTarget { Entity: PlacedCharacter placed } when _mapCharacters.FirstOrDefault(m => m.Placement == placed) is { } body && IsDead(body.Character):
                RunVerbOn(HudVerb.Use, body.Placement.X, body.Placement.Y, placed); // a body: searched (U6GameWindow.U6Things.cs)
                break;
            case MapEntityTarget { Entity: PlacedCharacter placed } when _mapCharacters.FirstOrDefault(m => m.Placement == placed) is { } mc:
                bool foe = _combatMode && !mc.Character.IsInParty && !IsDead(mc.Character);
                RunVerbOn(foe ? HudVerb.Attack : HudVerb.Talk, mc.Placement.X, mc.Placement.Y, placed);
                break;
            case MapEntityTarget { Entity: PlacedItemInstance item }:
                RunVerbOn(_combatMode ? HudVerb.Attack : HudVerb.Use, item.X, item.Y, item);
                break;
            case MapEntityTarget { Entity: ObjectInstance obj }:
                RunVerbOn(_combatMode ? HudVerb.Attack : HudVerb.Use, obj.X, obj.Y, obj);
                break;
        }
    }

    private (int X, int Y)? SquareOfThing(object thing) => thing switch
    {
        PlacedItemInstance i => (i.X, i.Y),
        ObjectInstance o => (o.X, o.Y),
        PlacedCharacter p => _mapCharacters.FirstOrDefault(m => m.Placement == p) is { } mc ? (mc.Placement.X, mc.Placement.Y) : (p.X, p.Y),
        _ => null
    };

    private void RunVerbOn(HudVerb verb, int cx, int cy, object thing)
    {
        StartU6Verb(verb);
        PickMapCell(cx, cy, thing);
    }

    /// <summary>Ultima 6 drags only what is on a square next to the avatar (MapWindow::MouseDown: distance > 1 - no drag).</summary>
    private bool U6CanDrag(int x, int y) => Math.Max(Math.Abs(x - _playerX), Math.Abs(y - _playerY)) <= 1;
}
