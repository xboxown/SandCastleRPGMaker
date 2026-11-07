// Ultima 6 engine (U6GameWindow): the verbs, the cursor and the panel, as Ultima 6 plays them - Ultima 6 projects only.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6's way: a verb first (its key or its button), then what it's done to - with the cursor on the map (arrows, Enter;
/// it starts on the avatar) or, Tab, in the inventory panel; a click picks too. Drop and Move ask a second time: where to, or
/// (Move, in the panel) to whom - F1-F9 shows another member, so things go between members. Everything that happens is told
/// in the text area: the engine's own messages too, so nothing is said only in a status bar.
/// </summary>
public partial class U6GameWindow
{
    private enum HudFocus { Map, Panel }
    private HudFocus _hudFocus = HudFocus.Map;
    private (int X, int Y) _mapCursor;
    private int _panelCursor;
    private Rectangle? _mapCursorVisual;
    private System.Windows.Controls.Image? _mapCursorCross;
    private SpriteAnimator? _mapCursorAnimator;
    private Rectangle? _panelCursorVisual;
    /// <summary>Drop / Move: what was picked first, waiting for where it goes.</summary>
    private (CharacterDefinition Owner, InventoryItemInstance Item)? _hudPickedItem;
    private object? _hudPickedThing;
    /// <summary>F10: the panel shows the whole party.</summary>
    private bool _hudPartyView;
    /// <summary>Solo mode (1-9): the party stays where it is; 0 or the party-mode key: it follows again.</summary>
    private bool _soloMode;
    /// <summary>The engine's words while a verb acts: kept, so the verb's own words (or the engine's refusal) are told once.</summary>
    private List<string>? _hudHeard;
    private string _lastStatusSaid = string.Empty;

    private sealed record PanelCell(Rect Box, EquipmentSlot? Slot, int CarriedIndex);

    /// <summary>Called before the engine's own mouse handling is wired up, so a click that picks a verb's target is taken first.</summary>
    private void InitU6Verbs()
    {
        PreviewMouseLeftButtonDown += U6Verbs_PreviewMouseLeftButtonDown;
        // The engine's messages (its status bar in Game Test) go to the text area too: in Ultima 6 everything is told there.
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(StatusText, (_, _) =>
        {
            string said = StatusText.Text?.Trim() ?? string.Empty;
            if (said.Length == 0 || said == _lastStatusSaid || said.StartsWith("Frame rate") || said.Contains("\" fired (") || (said.StartsWith('(') && said.Contains(" lift "))) return; // Game Test's own notes stay out
            _lastStatusSaid = said;
            if (U6Notes.Rewrite(_systemSettings.Ultima6, said) is not { } note) return; // the maker's rules: changed, or not said (System Settings > Ultima 6 > Text area notes)
            if (_hudHeard is not null) _hudHeard.Add(note);
            else U6Say(note);
        });
    }

    private string W(string id) => U6Words.Of(_systemSettings.Ultima6, id);
    private string W(string id, string thing, string? who = null) => W(id).Replace("{0}", thing).Replace("{1}", who ?? "");

    // ---------- Keys ----------

    /// <summary>Ultima 6's keys, before the Ultima 7 engine's: true when taken (every key not Ultima 6's is swallowed - no Z stats, no I, no S).</summary>
    private bool U6KeyTook(KeyEventArgs e)
    {
        var key = U8KeyCapture.RealKey(e);
        if (_casting is not null) { if (!e.IsRepeat || key is Key.Up or Key.Down or Key.Back) U6CastKey(key); return true; } // the book is open (U6GameWindow.U6Cast.cs)
        var arrows = WalkArrowsFor(key);
        if (_hudVerb is not null || _hudFocus == HudFocus.Panel)
        {
            if (e.IsRepeat && arrows.Length == 0) return true;
            if (KeyIs(key, "Cancel")) { CancelHudVerb(); return true; }
            if (KeyIs(key, "ToggleCursor"))
            {
                if (_hudFocus == HudFocus.Map && FirstFilledCell() is int f) _panelCursor = f;
                SetHudFocus(_hudFocus == HudFocus.Map ? HudFocus.Panel : HudFocus.Map);
                return true;
            }
            if (MemberKey(key) is int m) { ShowHudMember(m); return true; }
            if (KeyIs(key, "TogglePortrait") || IsStarKey(key)) { ToggleHudView(); return true; }
            if (arrows.Length > 0) { MoveHudCursor(arrows); return true; }
            if (KeyIs(key, "Accept") || KeyIs(key, "Accept2")) { PickUnderHudCursor(); return true; }
            if (_hudVerb is null && VerbKey(key) is { } next) { StartU6Verb(next); return true; } // browsing the inventory, a verb starts there
            return true;
        }
        if (e.IsRepeat && arrows.Length == 0) return true;
        if (VerbKey(key) is { } verb) { StartU6Verb(verb); return true; }
        if (KeyIs(key, "ToggleCursor")) { if (FirstFilledCell() is int f) _panelCursor = f; SetHudFocus(HudFocus.Panel); return true; }
        if (KeyIs(key, "TogglePortrait") || IsStarKey(key)) { ToggleHudView(); return true; }
        if (MemberKey(key) is int member) { ShowHudMember(member); return true; }
        if (IsStarKey(key)) { ToggleHudView(); return true; }
        if (KeyIs(key, "PartyView")) { _hudPartyView = true; RefreshHudPanel(); return true; }
        if (SoloKey(key) is int solo) { StartSolo(solo); return true; }
        if (KeyIs(key, "PartyMode")) { EndSolo(); return true; }
        if (KeyIs(key, "Save")) { QuickSaveOrLoad(save: true); return true; }
        if (KeyIs(key, "Load")) { QuickSaveOrLoad(save: false); return true; }
        if (KeyIs(key, "Quit")) { AskToQuit(); return true; }
        if (KeyIs(key, "TestNextPeriod") && !IsShippedGame) { SkipToNextPeriod(); return true; }
        if (KeyIs(key, "TestScriptState") && !IsShippedGame) { ShowScriptState(); return true; }
        if (arrows.Length == 0 && _hudVerb is null && KeyIs(key, "Accept2") && TurnPass()) return true; // Space in turn-by-turn combat: pass
        if (arrows.Length > 0)
        {
            if (IsGamePaused) return true;
            if (e.IsRepeat) return true;
            if (TurnStep(arrows)) return true; // turn-by-turn combat: one cell, one round
            foreach (var arrow in arrows) _heldDirectionKeys.Add(arrow);
            _lastDirectionKeyPressed = arrows[^1];
            return true;
        }
        return !KeyIs(key, "Cancel") && !(key is Key.Escape); // Escape still closes what the engine has open; anything else that isn't Ultima 6's does nothing
    }

    private bool U6KeyUp(KeyEventArgs e)
    {
        var released = WalkArrowsReleased(U8KeyCapture.RealKey(e));
        if (released.Length == 0) return false;
        if (_turnStepFrom is not null) return true; // turn by turn: the step goes on to the next cell, the key let go or not
        foreach (var arrow in released) _heldDirectionKeys.Remove(arrow);
        return true;
    }

    /// <summary>"*" typed on the main keys (Shift+8) works as the numpad's * - Portrait / inventory.</summary>
    private bool IsStarKey(Key key) => key == Key.D8 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _keyNames.GetValueOrDefault("TogglePortrait") == "Multiply";

    private HudVerb? VerbKey(Key key)
    {
        foreach (var verb in Enum.GetValues<HudVerb>())
            if (KeyIs(key, verb.ToString())) return verb;
        return null;
    }

    private int? MemberKey(Key key)
    {
        for (int i = 1; i <= 9; i++) if (KeyIs(key, "Member" + i)) return i - 1;
        return null;
    }

    private int? SoloKey(Key key)
    {
        for (int i = 1; i <= 9; i++) if (KeyIs(key, "Solo" + i)) return i - 1;
        return null;
    }

    // ---------- Verbs ----------

    /// <summary>A verb picked: its prompt ("Get-"), the cursor where Ultima 6 puts it - on the avatar, or in the inventory for Drop.</summary>
    /// <summary>Someone looked at: their portrait in the panel (Ultima 6) till the next action.</summary>
    private CharacterDefinition? _hudLooked;

    private void ShowLooked(object? thing)
    {
        var who = thing switch { PlacedCharacter p => _mapCharacters.FirstOrDefault(m => m.Placement == p)?.Character, CharacterDefinition c => c, _ => null };
        if (ReferenceEquals(_hudLooked, who)) return;
        _hudLooked = who;
        RefreshHudPanel();
    }

    private void StartU6Verb(HudVerb verb)
    {
        if (_hudLooked is not null) { _hudLooked = null; RefreshHudPanel(); } // the next action: the panel back
        if (_hudVerb is not null) CancelHudVerb();
        if (verb == HudVerb.Combat) { U6ToggleCombat(); return; }
        if (verb == HudVerb.Rest) { U6Rest(); return; } // no target: the party camps (U6GameWindow.U6Levels.cs)
        if (verb == HudVerb.Cast) { U6StartCast(); return; } // the book in the panel (U6GameWindow.U6Cast.cs)
        _hudPickedItem = null;
        _hudPickedThing = null;
        StartHudVerb(verb);
        _hudPrompt = W(verb.ToString()) + "-";
        RefreshHudText();
        _mapCursor = (_playerX, _playerY);
        bool inPanel = verb == HudVerb.Drop || _hudFocus == HudFocus.Panel;
        if (inPanel && FirstFilledCell() is int filled) _panelCursor = filled; // on the first thing carried, as Ultima 6's inventory cursor
        SetHudFocus(inPanel ? HudFocus.Panel : HudFocus.Map);
    }

    /// <summary>The first panel cell with something in it - the carried things first, then what's worn.</summary>
    private int? FirstFilledCell()
    {
        if (HudMember is not { } member) return null;
        var cells = PanelCells();
        int carried = cells.FindIndex(c => c.Slot is null && ItemInPanelCell(member, c) is not null);
        if (carried >= 0) return carried;
        int worn = cells.FindIndex(c => ItemInPanelCell(member, c) is not null);
        return worn >= 0 ? worn : null;
    }

    private void CancelHudVerb()
    {
        if (_hudVerb is not null) U6Say(_hudPrompt + W("Nothing"));
        if (_casting is not null || _castAiming is not null) { _casting = null; _castAiming = null; RefreshHudPanel(); }
        _hudPickedItem = null;
        _hudPickedThing = null;
        EndHudVerb();
        SetHudFocus(HudFocus.Map);
    }

    /// <summary>The verb's line is finished ("Look-wall") and what happened is told after it.</summary>
    private void FinishHudVerb(string target, params string[] told)
    {
        // A second pick (Drop's place, Move's member): the line is the verb and the thing, the question gone ("Drop-dagger").
        bool second = (_hudPickedItem is not null || _hudPickedThing is not null) && _hudVerb is not null;
        U6Say(second ? W(_hudVerb!.Value.ToString()) + "-" + target : _hudPrompt + target);
        foreach (var line in told.Where(t => t.Length > 0)) U6Say(line);
        _hudPickedItem = null;
        _hudPickedThing = null;
        EndHudVerb();
        SetHudFocus(HudFocus.Map);
        RefreshHudPanel();
        PassTurnIfFighting(); // turn-by-turn combat: the verb took the turn
    }

    /// <summary>The engine acts; its words are kept and told after the verb's line, or the verb's own words when it said nothing.</summary>
    private void HudAct(string target, Func<bool> act, string saidIfDone)
    {
        _hudHeard = new List<string>();
        bool done;
        try { done = act(); }
        catch { _hudHeard = null; throw; }
        var heard = _hudHeard;
        _hudHeard = null;
        if (done) FinishHudVerb(target, saidIfDone);
        else FinishHudVerb(target, heard.Count > 0 ? heard.ToArray() : new[] { W("NotPossible") });
    }

    // ---------- Picking with the cursor or the mouse ----------

    private void PickUnderHudCursor()
    {
        if (_hudFocus == HudFocus.Panel)
        {
            var cells = PanelCells();
            if (_hudPartyView || cells.Count == 0) return;
            PickPanelCell(cells[Math.Clamp(_panelCursor, 0, cells.Count - 1)]);
        }
        else PickMapCell(_mapCursor.X, _mapCursor.Y);
    }

    private void U6Verbs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_scene is not null) { SceneClick(e.GetPosition(HudLayer)); e.Handled = true; return; } // a scene (U6GameWindow.U6Scene.cs)
        if (_currentMessage is not null && ReplyClicked(e.GetPosition(HudLayer))) { e.Handled = true; return; } // a reply picked from the list (U6GameWindow.U6TalkStyle.cs)
        if (_u6Title is not null) { if (!IsPlayingVideo) U6TitleClick(e.GetPosition(HudLayer)); e.Handled = true; return; } // the title menu (U6GameWindow.U6Title.cs)
        if (Hud.First(HudPieceKind.GameView) is null) return;
        var onScreen = e.GetPosition(HudLayer);
        if (_hudVerb is null && CombatModeClicked(onScreen)) { e.Handled = true; return; } // a member's combat mode, round the list (U6GameWindow.U6CombatModes.cs)
        if (_hudVerb is null && Hud.Pieces.Any(p => p.Kind is HudPieceKind.MemberName or HudPieceKind.Portrait && (HudPiece.ViewOf(p.Kind) == HudPanelView.Always || HudPiece.ViewOf(p.Kind) == _hudView) && new Rect(p.X, p.Y, p.Width, p.Height).Contains(onScreen)))
        { ToggleHudView(); e.Handled = true; return; } // the member's name or portrait clicked: portrait / inventory
        // The panel: a click on a slot or a carried thing, or on a member's name in the party view.
        if (Hud.First(HudPieceKind.Panel) is { } panel && new Rect(panel.X, panel.Y, panel.Width, panel.Height).Contains(onScreen))
        {
            if (_hudPartyView) { PartyViewClicked(onScreen); e.Handled = true; return; }
            var cells = PanelCells();
            int hit = cells.FindIndex(c => c.Box.Contains(onScreen));
            if (hit < 0) return;
            _panelCursor = hit;
            _hudFocus = HudFocus.Panel;
            if (_hudVerb is null) StartU6Verb(HudVerb.Look);
            PickPanelCell(cells[hit]);
            e.Handled = true;
            return;
        }
        if (_hudVerb is null)
        {
            // A click on bare ground (nothing on it): Look names the tile, as Ultima 6 (the engine's own clicks see only things).
            var inView = e.GetPosition(WorldView);
            if (e.ClickCount == 1 && inView.X >= 0 && inView.Y >= 0 && inView.X < ViewportWidth && inView.Y < ViewportHeight && FindMapTarget(e.GetPosition(PaperdollGumpsCanvas)) is null)
            {
                var spot = e.GetPosition(GameCanvas);
                int gx = (int)Math.Floor(spot.X / _tileWidth), gy = (int)Math.Floor(spot.Y / _tileHeight);
                if (_letterAsk is null && _currentMessage is null)
                {
                    string ground = SquareSeen(gx, gy) && TileNameAt(gx, gy) is { } tile ? tile : W("Nothing");
                    U6Say(W("Look") + "-" + ground);
                    U6Say(W("YouSee", ground));
                }
            }
            return; // the engine's own clicks (things: look, use; walking)
        }
        var inWorld = e.GetPosition(WorldView);
        if (inWorld.X < 0 || inWorld.Y < 0 || inWorld.X >= ViewportWidth || inWorld.Y >= ViewportHeight) return;
        var world = e.GetPosition(GameCanvas);
        PickMapCell((int)Math.Floor(world.X / _tileWidth), (int)Math.Floor(world.Y / _tileHeight));
        e.Handled = true;
    }

    /// <summary>What is on a map cell, the topmost: the avatar, a character, then a thing; null: nothing.</summary>
    private object? ThingOnCell(int cx, int cy)
    {
        if (cx == _playerX && cy == _playerY) return _player;
        var world = new Point((cx + 0.5) * _tileWidth, (cy + 0.5) * _tileHeight);
        if (HitTestCharacter(world) is { } placed) return placed;
        var (item, obj) = HitTestStack(world);
        if (item is not null) return item;
        if (obj is not null) return obj;
        return _mapCharacters.FirstOrDefault(mc => mc.Placement.X == cx && mc.Placement.Y == cy)?.Placement;
    }

    private string ThingName(object? thing) => thing switch
    {
        null => W("Nothing"),
        PlacedItemInstance item => DollLook.ItemName(new ItemReference { Category = item.Category, ItemId = item.ItemId }, _objects, _books, _containers),
        _ => NameOfEntity(thing)
    };

    private bool NextTo(CharacterDefinition who, int cx, int cy, int reach = 1)
    {
        var (x, y) = CellOf(who);
        return Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) <= reach;
    }

    private CharacterDefinition Actor => HudMember ?? _player!;

    private void PickMapCell(int cx, int cy, object? given = null)
    {
        if (_hudVerb is not { } verb || _player is null) return;
        var thing = given ?? ThingOnCell(cx, cy); // a double click names what was clicked
        string name = ThingName(thing);
        switch (verb)
        {
            case HudVerb.Look:
                if (thing is null && SquareSeen(cx, cy) && TileNameAt(cx, cy) is { } ground) name = ground; // the ground: its tile's name (Tile Properties)
                FinishHudVerb(name, W("YouSee", name));
                ShowLooked(thing); // a person: their portrait
                break;
            case HudVerb.Cast:
                CastOnCell(cx, cy, thing); // a spell's target (U6GameWindow.U6Cast.cs)
                break;
            case HudVerb.Get:
                if (thing is not (PlacedItemInstance or ObjectInstance)) { FinishHudVerb(name, W("NotPossible")); break; }
                if (!NextTo(_player, cx, cy)) { FinishHudVerb(name, W("OutOfRange")); break; }
                HudAct(name, () => PickUp(Actor, thing), W("Gets", name, Actor.Name));
                break;
            case HudVerb.Use:
                if (thing is null or CharacterDefinition) { FinishHudVerb(name, W("NotPossible")); break; }
                if (thing is PlacedCharacter bodyPlaced && _mapCharacters.FirstOrDefault(m => m.Placement == bodyPlaced) is { } body && IsDead(body.Character))
                {
                    if (!NextTo(_player, cx, cy)) { FinishHudVerb(name, W("OutOfRange")); break; }
                    U6Say(_hudPrompt + name);
                    EndHudVerb();
                    SetHudFocus(HudFocus.Map);
                    U6SearchBody(body); // a body: searched (U6GameWindow.U6Things.cs)
                    PassTurnIfFighting();
                    break;
                }
                if (thing is PlacedCharacter) { FinishHudVerb(name, W("NotPossible")); break; }
                if (!NextTo(_player, cx, cy)) { FinishHudVerb(name, W("OutOfRange")); break; }
                U6Say(_hudPrompt + name);
                EndHudVerb();
                SetHudFocus(HudFocus.Map);
                if (thing is PlacedItemInstance { Category: ReferencedItemCategory.Container } box && ContainerDef(box.ItemId) is { } boxData) { U6Search(box, boxData); RefreshHudPanel(); PassTurnIfFighting(); break; } // searched: it spills out
                UseTarget(new MapEntityTarget(thing));
                RefreshHudPanel();
                break;
            case HudVerb.Talk:
                if (thing is CharacterDefinition) { FinishHudVerb(name, W("TalkSelf")); break; } // Events::perform_talk
                if (thing is not PlacedCharacter talker) { FinishHudVerb(name, W("NotPossible")); break; }
                if (_soloMode) { FinishHudVerb(name, W("NotInSolo")); break; }
                if (_mapCharacters.FirstOrDefault(mc => mc.Placement == talker) is not { } listener) { FinishHudVerb(name, W("NotPossible")); break; }
                if (IsDead(listener.Character) || IsIncapacitated(listener.Character)
                    || (listener.Npc.Activity == ScheduleType.Sleep && listener.PoseName == "Sleeping" && !listener.Character.IsInParty)
                    || (!listener.Character.IsInParty && (listener.Npc.InCombat || Attitude(listener.Character, _player) == FactionBehavior.Attack)))
                { FinishHudVerb(name, W("NoResponse")); break; } // asleep, paralyzed, hostile: "No response"
                if (listener.Character.TalkCommands.Count == 0) { FinishHudVerb(name, W("FunnyNoResponse")); break; }
                U6Say(_hudPrompt + name);
                EndHudVerb();
                SetHudFocus(HudFocus.Map);
                TalkTo(talker);
                break;
            case HudVerb.Attack:
                if (thing is PlacedCharacter foe && _mapCharacters.FirstOrDefault(mc => mc.Placement == foe) is { } target && !target.Character.IsInParty)
                {
                    if (!_combatMode) U6ToggleCombat();
                    SetPlayerTarget(target.Character);
                    _turnRunMs = 0;
                    FinishHudVerb(name); // the turn: long enough for a blow
                    if (TurnByTurn) { _turnRunMs = 0; PassTurnIfFighting(attack: true); }
                }
                else if (thing is ObjectInstance or PlacedItemInstance) HudAct(name, () => TryAttackInCombatMode(thing), "");
                else FinishHudVerb(name, W("NotPossible"));
                break;
            case HudVerb.Drop:
            case HudVerb.Move when _hudPickedItem is not null:
                if (_hudPickedItem is not { } picked) { SetHudFocus(HudFocus.Panel); break; }
                if (!NextTo(_player, cx, cy, 5)) { FinishHudVerb(W("WhatPlace"), W("OutOfRange")); break; } // MapWindow::can_drop_or_move_obj: within 5
                {
                    string itemName = HudItemName(picked.Item);
                    HudAct(itemName, () => DropCarriedAt(picked.Owner, picked.Item, (cx, cy)), W("Drops", itemName, picked.Owner.Name));
                }
                break;
            case HudVerb.Move when _hudPickedThing is null:
                if (thing is not (PlacedItemInstance or ObjectInstance) || !IsMovable(thing)) { FinishHudVerb(name, W("NotPossible")); break; }
                if (!NextTo(_player, cx, cy)) { FinishHudVerb(name, W("OutOfRange")); break; }
                _hudPickedThing = thing;
                _hudPrompt = W("Move") + "-" + name + " " + W("WhatPlace");
                RefreshHudText();
                break;
            case HudVerb.Move:
            {
                var moving = _hudPickedThing!;
                string movedName = ThingName(moving);
                var (mx, my) = moving switch { PlacedItemInstance i => (i.X, i.Y), ObjectInstance o => (o.X, o.Y), _ => (_playerX, _playerY) };
                if (Math.Max(Math.Abs(mx - cx), Math.Abs(my - cy)) > 1) { FinishHudVerb(movedName, W("OutOfRange")); break; } // Events::pushTo: one square on
                HudAct(movedName, () => MoveThingTo(moving, (cx, cy)), "");
                break;
            }
            default:
                FinishHudVerb(name, W("NotPossible"));
                break;
        }
    }

    private void PickPanelCell(PanelCell cell)
    {
        if (_hudVerb is not { } verb || HudMember is not { } member) return;
        var item = ItemInPanelCell(member, cell);
        string name = item is null ? W("Nothing") : HudItemName(item);
        // Second pick of Move: where the thing goes - a body slot (readied), the carried things (put away), another member's (given).
        if (verb == HudVerb.Move && _hudPickedItem is { } picked)
        {
            string itemName = HudItemName(picked.Item);
            if (OriginOf(picked.Owner, picked.Item) is not { } origin) { FinishHudVerb(itemName, W("NotPossible")); return; }
            if (member != picked.Owner)
            {
                HudAct(itemName, () => DropOnCharacter(origin, member), W("Gives", itemName, member.Name));
                return;
            }
            if (cell.Slot is null && _hudOpenBag is not null) { HudAct(itemName, () => PutInOpenBag(member, picked.Item), W("Gets", itemName, member.Name)); return; } // into the open bag
            if (cell.Slot is { } slot) HudAct(itemName, () => TryEquipInSlot(origin, member, slot), W("Readies", itemName, member.Name));
            else HudAct(itemName, () => DropOnCharacter(origin, member), W("Unreadies", itemName, member.Name));
            return;
        }
        switch (verb)
        {
            case HudVerb.Look:
                FinishHudVerb(name, W("YouSee", item is null ? W("Nothing") : name));
                break;
            case HudVerb.Use:
                if (item is not null && U6UseBag(item, name)) break; // a bag opens in the panel (U6GameWindow.U6Things.cs)
                if (item is null || OriginOf(member, item) is not { } source) { FinishHudVerb(name, W("NotPossible")); break; }
                U6Say(_hudPrompt + name);
                EndHudVerb();
                SetHudFocus(HudFocus.Map);
                UseCarriedObject(item, source);
                RefreshHudPanel();
                break;
            case HudVerb.Drop:
                if (item is null) { FinishHudVerb(name, W("NotPossible")); break; }
                _hudPickedItem = (member, item);
                _hudPrompt = W("Drop") + "-" + name + " " + W("WhatPlace");
                RefreshHudText();
                _mapCursor = (_playerX, _playerY);
                SetHudFocus(HudFocus.Map);
                break;
            case HudVerb.Move:
                if (item is null) { FinishHudVerb(name, W("NotPossible")); break; }
                _hudPickedItem = (member, item);
                _hudPrompt = W("Move") + "-" + name + " " + W("ToWhom");
                RefreshHudText();
                break;
            default:
                FinishHudVerb(name, W("NotPossible"));
                break;
        }
    }

    private string HudItemName(InventoryItemInstance item) =>
        DollLook.ItemName(new ItemReference { Category = item.Category, ItemId = item.ItemId }, _objects, _books, _containers);

    /// <summary>Where a carried thing is, as a drag would pick it up: a slot, the pack, or inside a container they carry.</summary>
    private DragOrigin? OriginOf(CharacterDefinition owner, InventoryItemInstance item)
    {
        InventoryItemInstance? holder = null;
        bool Find(List<InventoryItemInstance> list, InventoryItemInstance? container)
        {
            foreach (var i in list)
            {
                if (ReferenceEquals(i, item)) { holder = container; return true; }
                if (Find(i.Contents, i)) return true;
            }
            return false;
        }
        return Find(owner.Inventory, null) ? OriginOfCarried(owner, item, holder) : null;
    }

    private bool DropCarriedAt(CharacterDefinition owner, InventoryItemInstance item, (int X, int Y) cell)
    {
        if (TakeFromCarrier(owner, item.Category, item.ItemId, Math.Max(1, item.Quantity)) is not { } taken) return false;
        foreach (var dropped in taken) PutDownAt(dropped, owner, cell);
        RefreshAfterMove();
        return true;
    }

    /// <summary>Move on the map: picked up and put down where it goes (too heavy, someone on it: the engine says why).</summary>
    private bool MoveThingTo(object thing, (int X, int Y) cell)
    {
        if (!PickUp(Actor, thing)) return false;
        var carried = Actor.Inventory.LastOrDefault();
        if (carried is null) return false;
        return DropCarriedAt(Actor, carried, cell);
    }

    // ---------- The cursor ----------

    private void SetHudFocus(HudFocus focus)
    {
        _hudFocus = focus;
        if (focus == HudFocus.Panel) _hudPartyView = false;
        DrawHudCursors();
    }

    private void MoveHudCursor(Key[] arrows)
    {
        int dx = arrows.Contains(Key.Left) ? -1 : arrows.Contains(Key.Right) ? 1 : 0;
        int dy = arrows.Contains(Key.Up) ? -1 : arrows.Contains(Key.Down) ? 1 : 0;
        if (_hudFocus == HudFocus.Map)
        {
            int rangeX = (int)(ViewportWidth / _tileWidth / 2), rangeY = (int)(ViewportHeight / _tileHeight / 2);
            _mapCursor = (Math.Clamp(_mapCursor.X + dx, _playerX - rangeX, _playerX + rangeX), Math.Clamp(_mapCursor.Y + dy, _playerY - rangeY, _playerY + rangeY));
        }
        else
        {
            // The nearest cell that way, by their middles.
            var cells = PanelCells();
            if (cells.Count == 0) return;
            var from = cells[Math.Clamp(_panelCursor, 0, cells.Count - 1)].Box;
            var mid = new Point(from.X + from.Width / 2, from.Y + from.Height / 2);
            int best = -1;
            double bestScore = double.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                var b = cells[i].Box;
                double ox = b.X + b.Width / 2 - mid.X, oy = b.Y + b.Height / 2 - mid.Y;
                double along = ox * dx + oy * dy;
                if (along <= 0.5) continue;
                double across = Math.Abs(dx != 0 ? oy : ox);
                double score = along + across * 2;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) _panelCursor = best;
        }
        DrawHudCursors();
    }

    /// <summary>The map cursor's box on its cell (only while a verb waits there), the panel's round its cell.</summary>
    private void DrawHudCursors()
    {
        if (_mapCursorVisual is null)
        {
            _mapCursorVisual = new Rectangle { Stroke = Brushes.Yellow, StrokeThickness = 1, IsHitTestVisible = false };
            Panel.SetZIndex(_mapCursorVisual, 5000);
            GameCanvas.Children.Add(_mapCursorVisual);
        }
        _mapCursorVisual.Width = _tileWidth;
        _mapCursorVisual.Height = _tileHeight;
        Canvas.SetLeft(_mapCursorVisual, _mapCursor.X * _tileWidth);
        Canvas.SetTop(_mapCursorVisual, _mapCursor.Y * _tileHeight);
        bool mapCursorShown = _hudVerb is not null && _hudFocus == HudFocus.Map;
        var cross = MousePointerCatalog.Resolve(_sprites, _systemSettings.MousePointers.U8Arrows.GetValueOrDefault("U6.Crosshair"));
        _mapCursorVisual.Visibility = mapCursorShown && cross is null ? Visibility.Visible : Visibility.Collapsed;
        if (cross is not null)
        {
            // The crosshairs on the cell (its hot spot, its Origin, on the cell's middle) - the arrows move it, Enter picks.
            if (_mapCursorCross is null)
            {
                _mapCursorCross = new System.Windows.Controls.Image { IsHitTestVisible = false, Stretch = Stretch.None };
                RenderOptions.SetBitmapScalingMode(_mapCursorCross, BitmapScalingMode.NearestNeighbor);
                Panel.SetZIndex(_mapCursorCross, AboveEverythingZIndex);
                GameCanvas.Children.Add(_mapCursorCross);
                _mapCursorAnimator = new SpriteAnimator(cross, _mapCursorCross, _resources);
            }
            var crossFrame = DollLook.Frame(cross);
            double cw = crossFrame?.PixelWidth ?? _tileWidth, ch = crossFrame?.PixelHeight ?? _tileHeight;
            Canvas.SetLeft(_mapCursorCross, _mapCursor.X * _tileWidth + (_tileWidth - cw) / 2);
            Canvas.SetTop(_mapCursorCross, _mapCursor.Y * _tileHeight + (_tileHeight - ch) / 2);
            _mapCursorCross.Visibility = mapCursorShown ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_panelCursorVisual is null)
        {
            _panelCursorVisual = new Rectangle { Stroke = Brushes.Yellow, StrokeThickness = 1, IsHitTestVisible = false };
            Panel.SetZIndex(_panelCursorVisual, 5000);
            HudLayer.Children.Add(_panelCursorVisual);
        }
        var cells = PanelCells();
        bool show = _hudFocus == HudFocus.Panel && cells.Count > 0 && !_hudPartyView;
        _panelCursorVisual.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        var box = cells[Math.Clamp(_panelCursor, 0, cells.Count - 1)].Box;
        _panelCursorVisual.Width = box.Width;
        _panelCursorVisual.Height = box.Height;
        Canvas.SetLeft(_panelCursorVisual, box.X);
        Canvas.SetTop(_panelCursorVisual, box.Y);
        if (!HudLayer.Children.Contains(_panelCursorVisual)) HudLayer.Children.Add(_panelCursorVisual);
    }

    /// <summary>The panel's places the cursor goes, in the inventory view: the body slots, then the carried cells.</summary>
    private List<PanelCell> PanelCells()
    {
        var cells = new List<PanelCell>();
        if (_hudView != HudPanelView.Inventory) return cells;
        foreach (var piece in Hud.Pieces.Where(p => p.Kind == HudPieceKind.BodySlot))
            cells.Add(new PanelCell(new Rect(piece.X, piece.Y, piece.Width, piece.Height), piece.Slot, -1));
        foreach (var piece in Hud.Pieces.Where(p => p.Kind == HudPieceKind.CarriedItems))
        {
            int cell = Math.Max(4, piece.CellSize);
            for (int i = 0; i < piece.Columns * piece.Rows; i++)
                cells.Add(new PanelCell(new Rect(piece.X + i % piece.Columns * cell, piece.Y + i / piece.Columns * cell, cell, cell), null, i));
        }
        return cells;
    }

    private InventoryItemInstance? ItemInPanelCell(CharacterDefinition member, PanelCell cell)
    {
        if (cell.Slot is { } slot)
            return member.EquippedSlots.TryGetValue(slot, out int id) ? member.Inventory.FirstOrDefault(i => i.InstanceId == id) : null;
        return HudCarriedItems(member).ElementAtOrDefault(cell.CarriedIndex);
    }

    // ---------- Members, the views, solo ----------

    private void ShowHudMember(int index)
    {
        _hudLooked = null;
        var party = GetPartyList();
        if (index >= party.Count) return;
        _hudMember = party[index];
        _hudOpenBag = null;
        _hudPartyView = false;
        RefreshHudPanel();
        DrawHudCursors();
    }

    private void ToggleHudView()
    {
        _hudView = _hudView == HudPanelView.Inventory ? HudPanelView.Portrait : HudPanelView.Inventory;
        _hudPartyView = false;
        if (_hudView == HudPanelView.Portrait && _hudFocus == HudFocus.Panel) _hudFocus = HudFocus.Map;
        RefreshHudPanel();
        DrawHudCursors();
    }

    private void PartyViewClicked(Point onScreen)
    {
        if (Hud.First(HudPieceKind.Panel) is not { } panel) return;
        int line = (int)((onScreen.Y - panel.Y) / Math.Max(1, HudLineHeight("")));
        var party = GetPartyList();
        if (line >= 0 && line < party.Count) ShowHudMember(line);
    }

    /// <summary>F10: the party, one name a line, in the panel (a click shows that member).</summary>
    private FrameworkElement? HudPartyList()
    {
        if (!_hudPartyView || Hud.First(HudPieceKind.Panel) is not { } panel) return null;
        var piece = new HudPiece { X = panel.X, Y = panel.Y, Width = panel.Width, Height = panel.Height };
        return HudWordsAt(piece, string.Join("\n", GetPartyList().Select((c, i) => $"{i + 1} {c.Name}")));
    }

    private void StartSolo(int index)
    {
        var party = GetPartyList();
        if (index >= party.Count) return;
        var member = party[index];
        if (IsDead(member) || IsIncapacitated(member) || (member != _player && member != Avatar && MapCharacterOf(member) is null)) { U6Say(W("NotPossible")); return; }
        if (!SoloSwapTo(member)) { U6Say(W("NotPossible")); return; } // the member is the one moved now (U6GameWindow.U6Solo.cs)
        _soloMode = true;
        _followers.Clear(); // the party stays where it is (Ultima 6's solo mode)
        _hudMember = member;
        RefreshHudPanel();
        U6Say(W("Solo", "", member.Name));
    }

    private void EndSolo()
    {
        SoloBackToAvatar(); // the avatar is moved again
        _soloMode = false;
        _hudMember = null;
        foreach (var member in _mapCharacters.Where(mc => mc.Character.IsInParty && !IsDead(mc.Character))) _followers[member] = _player!;
        RefreshHudPanel();
        U6Say(W("PartyMode"));
    }

    private void U6ToggleCombat()
    {
        if (_combatForbidden) { U6Say(W("NotPossible")); return; }
        _hudHeard = new List<string>(); // the Ultima 7 engine's own combat words stay out: Ultima 6's are told
        try { ToggleCombatMode(); } finally { _hudHeard = null; }
        if (!_combatMode) CombatOverTakeAvatar(); // a Command member was being moved on its turn: the avatar again
        U6Say(_combatMode ? W("CombatBegin") : W("CombatBreakOff"));
        _turnRunMs = 0; // turn by turn from now: the world waits for the avatar (U6GameWindow.U6Combat.cs)
        _heldDirectionKeys.Clear();
        if (_combatMode) AskTurn();
        else { _hudPrompt = string.Empty; RefreshHudText(); }
    }

    /// <summary>Ultima 6's save and restore keys: the first slot, told in the text area.</summary>
    private void QuickSaveOrLoad(bool save)
    {
        if (save && (_soloMode || ControllingMember)) EndSolo(); // saved in party mode, the avatar moved (U6GameWindow.U6Solo.cs)
        var command = new EventCommand { Type = save ? EventCommandType.SaveGame : EventCommandType.LoadGame };
        command.Parameters["Slot"] = "1";
        command.Parameters["Name"] = "\"" + (_player?.Name ?? "Game") + "\"";
        if (save) SaveGameCommand(command);
        else LoadGameCommand(command);
    }
}
