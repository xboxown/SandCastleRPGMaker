using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6 projects only: the HUD Editor, in place of the PaperDoll Editor. The game's whole screen - a frame drawn in
/// the Sprite Editor - and its pieces placed on it with their size: where the world shows, the text area, the panel
/// (inventory or portrait and stats, one member at a time, in the same place), the command buttons, your own buttons,
/// compass, sun and moons, date. One HUD per project (System Settings' Ultima6.Hud).
/// </summary>
public sealed class HudEditorWindow : Window
{
    private readonly HudDesign _hud;
    private readonly SpriteDatabase _sprites;
    private readonly StatDatabase _stats;
    private readonly ResourceDatabase _resources;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly VariableDatabase _variables;
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x1c)), ClipToBounds = true, Focusable = true };
    private readonly ListBox _pieceList = new() { Height = 220, Margin = new Thickness(0, 4, 0, 4) };
    private readonly StackPanel _props = new();
    private readonly StackPanel _hudProps = new();
    private readonly ComboBox _zoomBox = new() { Width = 60, ItemsSource = new[] { 1, 2, 3, 4 }, SelectedItem = 2 };
    private readonly ComboBox _viewBox = new() { Width = 170, ItemsSource = new[] { "Inventory", "Portrait and stats" }, SelectedIndex = 0 };
    private readonly CheckBox _showNames = new() { Content = "Show names", IsChecked = true, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
    private int _selectedId = -1;
    private bool _refreshing;
    private (Point Start, int X, int Y)? _drag;
    private (Point Start, int Width, int Height)? _resize;

    private int Zoom => _zoomBox.SelectedItem is int z ? z : 2;
    private HudPanelView ShownView => _viewBox.SelectedIndex == 1 ? HudPanelView.Portrait : HudPanelView.Inventory;
    private HudPiece? Piece => _hud.Piece(_selectedId);

    public HudEditorWindow(HudDesign hud, SpriteDatabase sprites, StatDatabase stats, ResourceDatabase resources, BookDatabase books, ObjectDatabase objects,
        ContainerDatabase containers, VariableDatabase variables)
    {
        _hud = hud;
        _sprites = sprites;
        _stats = stats;
        _resources = resources;
        _books = books;
        _objects = objects;
        _containers = containers;
        _variables = variables;
        Title = $"HUD Editor (Ultima 6) [{BuildInfo.CurrentBuild}]";
        Width = 1360;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

        // ---- The HUD itself ----
        var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        left.Children.Add(new TextBlock { Text = "The HUD", FontWeight = FontWeights.Bold });
        left.Children.Add(Note("The game's whole screen: its frame is a picture you draw in the Sprite Editor (any Paperdoll folder), the size of the screen. Pieces go on it. One HUD per project - the event command Change HUD Frame swaps its frame in play."));
        left.Children.Add(_hudProps);
        left.Children.Add(MakeButton("Ultima 6 starter layout", StarterLayout));
        left.Children.Add(Note("Puts the pieces where Ultima 6 has them (Nuvie): the world at 8,8 (160 x 160), the panel at 176,8, the messages at 176,112, the ten command buttons at 8,176 with the date over them. Pieces already there stay; move anything anywhere."));
        left.Children.Add(new TextBlock { Text = "The title menu's scenes", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 0) });
        left.Children.Add(MakeButton("Introduction scene...", () => OpenSceneEvent("Introduction", _hud.IntroCommands, c => _hud.IntroCommands = c)));
        left.Children.Add(MakeButton("Create New Character scene...", () => OpenSceneEvent("Create New Character", _hud.CreateCommands, c => _hud.CreateCommands = c)));
        if (ProjectSession.IsAppleIIgs) // Ultima 6 Apple IIgs mode: the screen that asks for another floppy
            left.Children.Add(MakeButton("Disk Swap screen (Apple IIgs)...", () => new DiskSwapScreenWindow(_hud.DiskSwap, _sprites, _resources) { Owner = this }.ShowDialog()));
        left.Children.Add(Note("As Ultima 6: black, then pictures, words and choices (Ultima 6 tab > Scenes: Begin Scene, Scene Picture, Scene Text, Scene Wait For Key, Scene Choice, Scene Remove, End Scene). Introduction plays this (none: the intro video); Create New Character plays this before the game begins - its choices can set stats, give items, anything. The same commands make cutscenes in any event during play."));
        Grid.SetColumn(left, 0);
        root.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn((UIElement)root.Children[^1], 0);

        // ---- Toolbox and canvas ----
        var middle = new DockPanel();
        var top = new StackPanel();
        foreach (var (label, group) in new[]
        {
            ("Screen:", new[] { HudPieceKind.GameView, HudPieceKind.TextArea, HudPieceKind.Panel, HudPieceKind.Picture }),
            ("Panel:", new[] { HudPieceKind.BodySlot, HudPieceKind.CarriedItems, HudPieceKind.Portrait, HudPieceKind.Stats, HudPieceKind.MemberName, HudPieceKind.CombatMode }),
            ("Buttons:", new[] { HudPieceKind.CommandButton, HudPieceKind.CustomButton }),
            ("Extras:", new[] { HudPieceKind.Compass, HudPieceKind.SunMoon, HudPieceKind.DateText })
        })
        {
            var toolbox = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            toolbox.Children.Add(new TextBlock { Text = label, Width = 70, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            foreach (var kind in group)
            {
                var k = kind;
                var button = MakeButton(HudPiece.KindName(kind), () => AddPiece(k));
                button.ToolTip = KindHelp(kind);
                toolbox.Children.Add(button);
            }
            top.Children.Add(toolbox);
        }
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        tools.Children.Add(_zoomBox);
        tools.Children.Add(new TextBlock { Text = "Panel shows:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
        tools.Children.Add(_viewBox);
        tools.Children.Add(_showNames);
        top.Children.Add(tools);
        top.Children.Add(Note("Click a piece to pick it; drag to move it, drag its corner to size it. Arrows: 1px (Shift: 8px). Delete: remove. Ctrl+D: duplicate. " +
                              "The panel shows one member at a time: inventory pieces (body slots, carried items) or portrait pieces (portrait, stats) - \"Panel shows\" switches which you see here; in play the player switches (1-9 picks the member)."));
        DockPanel.SetDock(top, Dock.Top);
        middle.Children.Add(top);
        DockPanel.SetDock(_status, Dock.Bottom);
        middle.Children.Add(_status);
        middle.Children.Add(new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(middle, 1);
        root.Children.Add(middle);

        // ---- Pieces ----
        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(new TextBlock { Text = "Pieces (top of the list = drawn at the back)", FontWeight = FontWeights.Bold });
        var buttons = new WrapPanel();
        buttons.Children.Add(MakeButton("Duplicate", DuplicatePiece));
        buttons.Children.Add(MakeButton("Delete", DeletePiece));
        buttons.Children.Add(MakeButton("Backward", () => MovePiece(-1)));
        buttons.Children.Add(MakeButton("Forward", () => MovePiece(1)));
        right.Children.Add(buttons);
        right.Children.Add(_pieceList);
        right.Children.Add(_props);
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);
        Content = root;

        _pieceList.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selectedId = _pieceList.SelectedItem is HudPiece p ? p.Id : -1;
            ShowProps();
            Redraw();
        };
        _zoomBox.SelectionChanged += (_, _) => Redraw();
        _viewBox.SelectionChanged += (_, _) => Redraw();
        _showNames.Click += (_, _) => Redraw();
        _canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) =>
        {
            if (_drag is null && _resize is null) return;
            _drag = null;
            _resize = null;
            _canvas.ReleaseMouseCapture();
            ShowProps();
        };
        PreviewKeyDown += Window_PreviewKeyDown;

        ShowHudProps();
        RefreshList();
        Redraw();
    }

    private static string KindHelp(HudPieceKind kind) => kind switch
    {
        HudPieceKind.GameView => "Where the world shows, and how big (Ultima 6: 160 x 160 at 8,8). Only one.",
        HudPieceKind.TextArea => "The messages, scrolling, and what the player types: Move-, Talk to-, the conversation, everything that happens in a fight. Only one.",
        HudPieceKind.Panel => "The panel's place (Ultima 6: 176,8, 136 x 96): a member's inventory or portrait and stats show here, one member at a time. Its sprite, if any, is drawn behind them. Only one.",
        HudPieceKind.Picture => "A picture (still or animated) laid on the frame.",
        HudPieceKind.BodySlot => "Inventory view: one body slot - head, neck, body, a hand, a ring, feet... What the member wears there shows in it. Its sprite, if any, is the empty slot.",
        HudPieceKind.CarriedItems => "Inventory view: what the member carries, a grid of cells; a container opens into it.",
        HudPieceKind.Portrait => "Portrait view: the member's face (Character Editor's Face).",
        HudPieceKind.Stats => "Portrait view: the member's stats, one per line.",
        HudPieceKind.MemberName => "The shown member's name, in either view.",
        HudPieceKind.CombatMode => "The shown member's combat mode, as words (Command, Front, Rear, Flank, Berserk, Retreat, Assault).",
        HudPieceKind.CommandButton => "A verb: Attack, Cast, Talk, Look, Get, Drop, Move, Use, Rest, Combat - clicked, or its key typed. Its own sprite (animated or not), and one while pressed.",
        HudPieceKind.CustomButton => "Your own button: a click runs its event once (Always, Only Once - it can be clicked again when the event has finished). Its own sprite.",
        HudPieceKind.Compass => "Which way the wind blows: a sprite of 8 frames - N, NE, E, SE, S, SW, W, NW.",
        HudPieceKind.SunMoon => "The sun and the two moons over the day: a sprite whose frames go through the day (frame 0 = midnight).",
        HudPieceKind.DateText => "The date and the wind as words (Ultima 6: \"7-19-0161 Wind:NW\" over the command buttons).",
        _ => ""
    };

    // ---------- The HUD's own properties ----------

    private void ShowHudProps()
    {
        _hudProps.Children.Clear();
        _hudProps.Children.Add(SpritePicker("Frame (the whole screen)", _hud.Frame, link => { _hud.Frame = link; Redraw(); }, 160));
        var size = new WrapPanel();
        size.Children.Add(IntField("Screen width", () => _hud.Width, v => _hud.Width = Math.Clamp(v, 160, 1280)));
        size.Children.Add(IntField("Screen height", () => _hud.Height, v => _hud.Height = Math.Clamp(v, 100, 960)));
        _hudProps.Children.Add(size);
        _hudProps.Children.Add(Note("Ultima 6, Savage Empire and Martian Dreams: 320 x 200 - the game scales it up to the window, pixels kept square."));
    }

    private void StarterLayout()
    {
        void Add(HudPieceKind kind, string name, int x, int y, int w, int h, Action<HudPiece>? also = null)
        {
            if (HudPiece.IsSingle(kind) && _hud.First(kind) is not null) return;
            if (_hud.Pieces.Any(p => p.Name == name)) return;
            var piece = new HudPiece { Id = _hud.NextId(), Kind = kind, Name = name, X = x, Y = y, Width = w, Height = h };
            also?.Invoke(piece);
            if (kind == HudPieceKind.CustomButton) piece.EventPages.Add(NewButtonPage());
            _hud.Pieces.Add(piece);
        }
        Add(HudPieceKind.GameView, "Game View", 8, 8, 160, 160);
        Add(HudPieceKind.Panel, "Panel", 176, 8, 136, 96);
        Add(HudPieceKind.TextArea, "Text Area", 176, 112, 136, 80);
        Add(HudPieceKind.DateText, "Date And Wind", 16, 168, 144, 8);
        int i = 0;
        foreach (var verb in Enum.GetValues<HudVerb>())
        {
            var v = verb;
            Add(HudPieceKind.CommandButton, verb.ToString(), 8 + 16 * i++, 176, 16, 16, p => p.Verb = v);
        }
        Add(HudPieceKind.MemberName, "Member Name", 176, 8, 136, 8);
        Add(HudPieceKind.Portrait, "Portrait", 180, 20, 56, 64);
        Add(HudPieceKind.Stats, "Stats", 240, 20, 72, 80);
        foreach (var (slot, x, y) in new[] { (EquipmentSlot.Head, 196, 18), (EquipmentSlot.Neck, 180, 34), (EquipmentSlot.Body, 196, 42), (EquipmentSlot.RightHand, 180, 58),
                     (EquipmentSlot.LeftHand, 212, 58), (EquipmentSlot.Ring1, 180, 76), (EquipmentSlot.Ring2, 212, 76), (EquipmentSlot.Feet, 196, 84) })
        {
            var s = slot;
            Add(HudPieceKind.BodySlot, SlotName(slot), x, y, 16, 16, p => p.Slot = s);
        }
        Add(HudPieceKind.CarriedItems, "Carried Items", 240, 24, 64, 64, p => { p.Columns = 4; p.Rows = 4; p.CellSize = 16; });
        _hud.Width = 320;
        _hud.Height = 200;
        ShowHudProps();
        RefreshList();
        Redraw();
    }

    private static string SlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.RightHand => "Right Hand",
        EquipmentSlot.LeftHand => "Left Hand",
        EquipmentSlot.Ring1 => "Ring 1",
        EquipmentSlot.Ring2 => "Ring 2",
        _ => slot.ToString()
    };

    private static BookEventPage NewButtonPage() => new()
    {
        Conditions = new BookEventConditions { TriggerConditionBased = BookTriggerConditionBased.Always, CycleCondition = CycleConditionType.Once }
    };

    // ---------- Pieces ----------

    private void AddPiece(HudPieceKind kind)
    {
        if (HudPiece.IsSingle(kind) && _hud.First(kind) is { } existing)
        {
            _status.Text = $"There is one {HudPiece.KindName(kind)} already - picked it.";
            Select(existing.Id);
            return;
        }
        var (w, h) = kind switch
        {
            HudPieceKind.GameView => (160, 160),
            HudPieceKind.TextArea => (136, 80),
            HudPieceKind.Panel => (136, 96),
            HudPieceKind.CarriedItems => (64, 48),
            HudPieceKind.Portrait => (56, 64),
            HudPieceKind.Stats => (72, 80),
            HudPieceKind.MemberName or HudPieceKind.DateText or HudPieceKind.CombatMode => (100, 8),
            HudPieceKind.SunMoon => (136, 16),
            _ => (16, 16)
        };
        var piece = new HudPiece
        {
            Id = _hud.NextId(), Kind = kind, Width = w, Height = h,
            Name = $"{HudPiece.KindName(kind)} {_hud.Pieces.Count(p => p.Kind == kind) + 1}",
            X = Math.Max(0, (_hud.Width - w) / 2), Y = Math.Max(0, (_hud.Height - h) / 2)
        };
        if (kind == HudPieceKind.CustomButton) piece.EventPages.Add(NewButtonPage());
        if (kind == HudPieceKind.CarriedItems) { piece.Columns = 4; piece.Rows = 3; }
        // A piece of the panel's view shows it in the editor.
        if (HudPiece.ViewOf(kind) == HudPanelView.Portrait) _viewBox.SelectedIndex = 1;
        else if (HudPiece.ViewOf(kind) == HudPanelView.Inventory) _viewBox.SelectedIndex = 0;
        _hud.Pieces.Add(piece);
        RefreshList();
        Select(piece.Id);
    }

    private void DuplicatePiece()
    {
        if (Piece is not { } piece) return;
        if (HudPiece.IsSingle(piece.Kind)) { _status.Text = $"Only one {HudPiece.KindName(piece.Kind)}."; return; }
        var copy = System.Text.Json.JsonSerializer.Deserialize<HudPiece>(System.Text.Json.JsonSerializer.Serialize(piece))!;
        copy.Id = _hud.NextId();
        copy.Name = piece.Name + " copy";
        copy.X += 8;
        copy.Y += 8;
        _hud.Pieces.Insert(_hud.Pieces.IndexOf(piece) + 1, copy);
        RefreshList();
        Select(copy.Id);
    }

    private void DeletePiece()
    {
        if (Piece is not { } piece) return;
        if (MessageBox.Show(this, $"Delete \"{piece.Name}\"?", "HUD Editor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int index = _hud.Pieces.IndexOf(piece);
        _hud.Pieces.Remove(piece);
        _selectedId = _hud.Pieces.Count == 0 ? -1 : _hud.Pieces[Math.Min(index, _hud.Pieces.Count - 1)].Id;
        RefreshList();
        ShowProps();
        Redraw();
    }

    private void MovePiece(int by)
    {
        if (Piece is not { } piece) return;
        int index = _hud.Pieces.IndexOf(piece), to = index + by;
        if (to < 0 || to >= _hud.Pieces.Count) return;
        _hud.Pieces.RemoveAt(index);
        _hud.Pieces.Insert(to, piece);
        RefreshList();
        Select(piece.Id);
    }

    private void Select(int id)
    {
        _selectedId = id;
        _refreshing = true;
        _pieceList.SelectedItem = _hud.Piece(id);
        _refreshing = false;
        ShowProps();
        Redraw();
    }

    private void RefreshList()
    {
        _refreshing = true;
        _pieceList.ItemsSource = null;
        _pieceList.ItemsSource = _hud.Pieces;
        _pieceList.SelectedItem = Piece;
        _refreshing = false;
    }

    // ---------- The picked piece's properties ----------

    private void ShowProps()
    {
        _props.Children.Clear();
        if (Piece is not { } piece) { _props.Children.Add(Note("Pick a piece, or add one from the toolbox.")); return; }
        _props.Children.Add(new TextBlock { Text = HudPiece.KindName(piece.Kind), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 2) });
        _props.Children.Add(Note(KindHelp(piece.Kind)));
        _props.Children.Add(Field("Name", piece.Name, s => { piece.Name = s.Length > 0 ? s : piece.Name; RefreshList(); Redraw(); }, 220));
        var place = new WrapPanel();
        place.Children.Add(IntField("X", () => piece.X, v => piece.X = v));
        place.Children.Add(IntField("Y", () => piece.Y, v => piece.Y = v));
        if (piece.Kind != HudPieceKind.CarriedItems)
        {
            place.Children.Add(IntField("Width", () => piece.Width, v => piece.Width = Math.Max(1, v)));
            place.Children.Add(IntField("Height", () => piece.Height, v => piece.Height = Math.Max(1, v)));
        }
        _props.Children.Add(place);

        switch (piece.Kind)
        {
            case HudPieceKind.CommandButton:
            {
                var verbs = Enum.GetValues<HudVerb>();
                var verb = new ComboBox { Width = 160, ItemsSource = verbs, SelectedItem = piece.Verb };
                verb.SelectionChanged += (_, _) => { if (verb.SelectedItem is HudVerb v) { piece.Verb = v; Redraw(); } };
                _props.Children.Add(Labeled("Verb", verb));
                _props.Children.Add(SpritePicker("Sprite (animated or not)", piece.Sprite, link => { piece.Sprite = link; Redraw(); }));
                _props.Children.Add(SpritePicker("Sprite while pressed (none: its own, darkened)", piece.PressedSprite, link => piece.PressedSprite = link));
                _props.Children.Add(Note("Its key is the verb's key (Ultima 6: the first letter; Combat: B) - the keys are set in System Settings > Ultima 6."));
                break;
            }
            case HudPieceKind.CustomButton:
                _props.Children.Add(SpritePicker("Sprite (animated or not)", piece.Sprite, link => { piece.Sprite = link; Redraw(); }));
                _props.Children.Add(SpritePicker("Sprite while pressed (none: its own, darkened)", piece.PressedSprite, link => piece.PressedSprite = link));
                _props.Children.Add(MakeButton("Event...", () => OpenButtonEvent(piece)));
                _props.Children.Add(Note("Trigger: Always. Cycle: Only Once. Both fixed - a click runs the event once; when it has finished, another click runs it again."));
                break;
            case HudPieceKind.BodySlot:
            {
                var slots = Enum.GetValues<EquipmentSlot>().Where(s => s is not (EquipmentSlot.Backpack or EquipmentSlot.Pouch)).ToArray();
                var slot = new ComboBox { Width = 160, ItemsSource = slots, SelectedItem = piece.Slot };
                slot.SelectionChanged += (_, _) => { if (slot.SelectedItem is EquipmentSlot s) { piece.Slot = s; Redraw(); } };
                _props.Children.Add(Labeled("Body slot", slot));
                _props.Children.Add(SpritePicker("Sprite of the empty slot (optional)", piece.Sprite, link => { piece.Sprite = link; Redraw(); }));
                break;
            }
            case HudPieceKind.CarriedItems:
            {
                var grid = new WrapPanel();
                grid.Children.Add(IntField("Across", () => piece.Columns, v => piece.Columns = Math.Clamp(v, 1, 20)));
                grid.Children.Add(IntField("Down", () => piece.Rows, v => piece.Rows = Math.Clamp(v, 1, 20)));
                grid.Children.Add(IntField("Cell size", () => piece.CellSize, v => piece.CellSize = Math.Clamp(v, 4, 64)));
                _props.Children.Add(grid);
                _props.Children.Add(SpritePicker("Sprite of an empty cell (optional)", piece.Sprite, link => { piece.Sprite = link; Redraw(); }));
                break;
            }
            case HudPieceKind.Stats:
                _props.Children.Add(StatsPicker(piece));
                _props.Children.Add(FontRow(piece));
                break;
            case HudPieceKind.TextArea:
                _props.Children.Add(FontRow(piece));
                _props.Children.Add(SpritePicker("Cursor (frames: animated; one frame: blinks; none: _)", piece.Sprite, link => { piece.Sprite = link; Redraw(); }));
                _props.Children.Add(Note("The cursor where the player types, and while words wait for a key to go on (a long speech, a book)."));
                break;
            case HudPieceKind.MemberName or HudPieceKind.DateText or HudPieceKind.CombatMode:
                _props.Children.Add(FontRow(piece));
                break;
            case HudPieceKind.GameView:
                _props.Children.Add(Note($"{piece.Width / 16.0:0.#} x {piece.Height / 16.0:0.#} cells of 16 pixels."));
                break;
            case HudPieceKind.Picture:
                _props.Children.Add(SpritePicker("Sprite (animated or not)", piece.Sprite, link => { piece.Sprite = link; Redraw(); }));
                _props.Children.Add(Check("In front of the panel's pieces", () => piece.InFront, on => { piece.InFront = on; Redraw(); }));
                _props.Children.Add(Note("Off: behind the body slots, what they wear and the carried items (a paperdoll picture under the weapons and armour). On: over them. Backward / Forward still order it among the frame's other pieces."));
                break;
            default:
                _props.Children.Add(SpritePicker(piece.Kind == HudPieceKind.Compass ? "Sprite (8 frames: N, NE, E, SE, S, SW, W, NW)" : piece.Kind == HudPieceKind.SunMoon ? "Sprite (frames through the day)" : "Sprite (animated or not)",
                    piece.Sprite, link => { piece.Sprite = link; Redraw(); }));
                break;
        }
    }

    private UIElement StatsPicker(HudPiece piece)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Stats shown (none ticked: every stat)", FontSize = 11 });
        foreach (var stat in _stats.Stats)
        {
            int id = stat.Id;
            panel.Children.Add(Check(stat.Name, () => piece.StatIds.Contains(id), on =>
            {
                piece.StatIds.Remove(id);
                if (on) piece.StatIds.Add(id);
                Redraw();
            }));
        }
        return panel;
    }

    private UIElement FontRow(HudPiece piece)
    {
        var row = new WrapPanel();
        var fonts = new List<string> { "(InGameFont)" };
        fonts.AddRange(_sprites.Groups.Where(g => g.Name == SpriteDatabase.FontsGroupName).SelectMany(g => g.Sprites.Select(s => s.Name)).Where(n => n != SpriteDatabase.InGameFontName).OrderBy(n => n));
        var font = new ComboBox { Width = 170, ItemsSource = fonts, SelectedItem = fonts.Contains(piece.FontName) ? piece.FontName : fonts[0] };
        font.SelectionChanged += (_, _) => { piece.FontName = font.SelectedIndex <= 0 ? string.Empty : (string)font.SelectedItem; Redraw(); };
        row.Children.Add(Labeled("Font (Sprite Editor > Fonts)", font));
        var swatch = new Button { Width = 40, Height = 20 };
        void Show() => swatch.Background = piece.TextColor.Length > 0 && ColorConverter.ConvertFromString(piece.TextColor) is Color c ? new SolidColorBrush(c) : Brushes.Transparent;
        swatch.Click += (_, _) =>
        {
            var start = piece.TextColor.Length > 0 && ColorConverter.ConvertFromString(piece.TextColor) is Color c ? c : Colors.White;
            var picker = new ColorPickerWindow(start) { Owner = this };
            if (picker.ShowDialog() != true) return;
            piece.TextColor = $"#{picker.Result.R:X2}{picker.Result.G:X2}{picker.Result.B:X2}";
            Show();
            Redraw();
        };
        Show();
        var plain = new Button { Content = "Font's own", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0) };
        plain.Click += (_, _) => { piece.TextColor = string.Empty; Show(); Redraw(); };
        var colour = new StackPanel { Orientation = Orientation.Horizontal };
        colour.Children.Add(swatch);
        colour.Children.Add(plain);
        row.Children.Add(Labeled("Colour", colour));
        return row;
    }

    private void OpenSceneEvent(string name, List<EventCommand> commands, Action<List<EventCommand>> store)
    {
        var pages = new ObservableCollection<EventCommandPage> { new() { Label = "1", Commands = commands } };
        var editor = new EventEditorControl
        {
            ResourceDatabase = _resources,
            Variables = _variables,
            CurrentBookLocalVariables = new List<VariableDefinition>(),
            Books = _books,
            Objects = _objects,
            Containers = _containers,
            Sprites = _sprites,
            CurrentBook = null
        };
        editor.ContentChanged += () => { if (editor.Pages.Count > 0) store(editor.Pages[0].Commands); };
        editor.Pages = pages;
        new Window
        {
            Title = $"{name} scene (title menu) [{BuildInfo.CurrentBuild}]", Width = 960, Height = 720, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Border { Padding = new Thickness(8), Child = editor }
        }.ShowDialog();
    }

    private void OpenButtonEvent(HudPiece piece)
    {
        if (piece.EventPages.Count == 0) piece.EventPages.Add(NewButtonPage());
        var page = piece.EventPages[0];
        page.Conditions.TriggerConditionBased = BookTriggerConditionBased.Always; // fixed, as the user asked
        page.Conditions.CycleCondition = CycleConditionType.Once;
        var pages = new ObservableCollection<EventCommandPage> { new() { Label = "1", Commands = page.Commands } };
        var editor = new EventEditorControl
        {
            ResourceDatabase = _resources,
            Variables = _variables,
            CurrentBookLocalVariables = new List<VariableDefinition>(),
            Books = _books,
            Objects = _objects,
            Containers = _containers,
            Sprites = _sprites,
            CurrentBook = null
        };
        editor.ContentChanged += () => { if (editor.Pages.Count > 0) page.Commands = editor.Pages[0].Commands; };
        editor.Pages = pages;
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(new TextBlock { Text = "Trigger Conditions Based:  Always   (fixed)", FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock { Text = "Cycle Condition:  Only Once   (fixed - again once it has finished, on the next click)", FontWeight = FontWeights.SemiBold });
        var dock = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(editor);
        new Window
        {
            Title = $"Event - {piece.Name} (Custom Button) [{BuildInfo.CurrentBuild}]", Width = 960, Height = 720, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = dock
        }.ShowDialog();
    }

    // ---------- Drawing ----------

    private void Redraw()
    {
        int z = Zoom;
        _canvas.Children.Clear();
        _canvas.Width = _hud.Width * z;
        _canvas.Height = _hud.Height * z;
        if (DollLook.Frame(DollLook.SpriteOf(_hud.Frame, _sprites)) is { } frame) _canvas.Children.Add(Pixel(frame, frame.PixelWidth, frame.PixelHeight, z));
        else _canvas.Children.Add(Dashed(new Size(_hud.Width, _hud.Height), z, Brushes.DimGray, "frame: pick its sprite (left)"));
        // As the game draws them: the HUD's pieces in list order, then the panel's contents over them, then pictures ticked In front.
        foreach (var piece in _hud.Pieces.OrderBy(p => p.Kind == HudPieceKind.Picture && p.InFront ? 2 : HudPiece.IsPanelContent(p.Kind) ? 1 : 0))
        {
            var view = HudPiece.ViewOf(piece.Kind);
            if (view != HudPanelView.Always && view != ShownView) continue;
            var visual = PieceVisual(piece, z);
            Canvas.SetLeft(visual, piece.X * z);
            Canvas.SetTop(visual, piece.Y * z);
            _canvas.Children.Add(visual);
            if (piece.Id == _selectedId)
            {
                var (w, h) = SizeOf(piece);
                var outline = new Rectangle { Width = w * z + 2, Height = h * z + 2, Stroke = Brushes.Cyan, StrokeThickness = 1, IsHitTestVisible = false };
                Canvas.SetLeft(outline, piece.X * z - 1);
                Canvas.SetTop(outline, piece.Y * z - 1);
                _canvas.Children.Add(outline);
                var grip = new Rectangle { Width = 7, Height = 7, Fill = Brushes.Cyan, IsHitTestVisible = false };
                Canvas.SetLeft(grip, (piece.X + w) * z - 4);
                Canvas.SetTop(grip, (piece.Y + h) * z - 4);
                _canvas.Children.Add(grip);
            }
        }
        _status.Text = $"{_hud.Width} x {_hud.Height}, {_hud.Pieces.Count} piece(s)." + (_hud.First(HudPieceKind.GameView) is null ? "  No Game View yet - the world won't show." : "")
            + (_hud.First(HudPieceKind.TextArea) is null ? "  No Text Area yet." : "");
    }

    private static (int W, int H) SizeOf(HudPiece piece) =>
        piece.Kind == HudPieceKind.CarriedItems ? (piece.Columns * piece.CellSize, piece.Rows * piece.CellSize) : (piece.Width, piece.Height);

    private FrameworkElement PieceVisual(HudPiece piece, int z)
    {
        var (w, h) = SizeOf(piece);
        var holder = new Canvas { Width = w * z, Height = h * z };
        string? name = _showNames.IsChecked == true ? piece.Name : null;
        switch (piece.Kind)
        {
            case HudPieceKind.GameView:
                holder.Children.Add(new Rectangle { Width = w * z, Height = h * z, Fill = new SolidColorBrush(Color.FromArgb(0x60, 0x20, 0x70, 0x20)) });
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.LimeGreen, name ?? "game view"));
                return holder;
            case HudPieceKind.TextArea:
                holder.Children.Add(Words("Welcome!\n\nTalk to-Dupre\n:", piece, w, z));
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.Khaki, name));
                return holder;
            case HudPieceKind.Panel:
                if (DollLook.Frame(DollLook.SpriteOf(piece.Sprite, _sprites)) is { } back) holder.Children.Add(Pixel(back, back.PixelWidth, back.PixelHeight, z));
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.SteelBlue, name ?? "panel"));
                return holder;
            case HudPieceKind.CarriedItems:
            {
                var cell = DollLook.Frame(DollLook.SpriteOf(piece.Sprite, _sprites));
                for (int r = 0; r < piece.Rows; r++)
                    for (int c = 0; c < piece.Columns; c++)
                    {
                        FrameworkElement box = cell is not null ? Pixel(cell, piece.CellSize, piece.CellSize, z)
                            : new Rectangle { Width = piece.CellSize * z, Height = piece.CellSize * z, Stroke = Brushes.MediumPurple, StrokeThickness = 1, Opacity = 0.7 };
                        Canvas.SetLeft(box, c * piece.CellSize * z);
                        Canvas.SetTop(box, r * piece.CellSize * z);
                        holder.Children.Add(box);
                    }
                if (name is not null) holder.Children.Add(Label(name, Brushes.MediumPurple));
                return holder;
            }
            case HudPieceKind.Stats:
            {
                var stats = piece.StatIds.Count > 0 ? _stats.Stats.Where(s => piece.StatIds.Contains(s.Id)) : _stats.Stats;
                holder.Children.Add(Words(string.Join("\n", stats.Select(s => $"{s.Name} 10")), piece, w, z));
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.Orange, name));
                return holder;
            }
            case HudPieceKind.MemberName:
                holder.Children.Add(Words("Dupre", piece, w, z));
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.Orange, null));
                return holder;
            case HudPieceKind.DateText:
                holder.Children.Add(Words("7-19-0161 Wind:NW", piece, w, z));
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.Orange, null));
                return holder;
            case HudPieceKind.CombatMode:
                holder.Children.Add(Words("Command", piece, w, z));
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.Orange, null));
                return holder;
            case HudPieceKind.Portrait:
                holder.Children.Add(Dashed(new Size(w, h), z, Brushes.Orange, name ?? "portrait"));
                return holder;
            default:
            {
                if (DollLook.Frame(DollLook.SpriteOf(piece.Sprite, _sprites)) is { } bitmap) holder.Children.Add(Pixel(bitmap, w, h, z));
                string tag = piece.Kind switch
                {
                    HudPieceKind.CommandButton => piece.Verb.ToString(),
                    HudPieceKind.BodySlot => SlotName(piece.Slot),
                    _ => name ?? HudPiece.KindName(piece.Kind)
                };
                holder.Children.Add(Dashed(new Size(w, h), z, piece.Kind is HudPieceKind.CommandButton or HudPieceKind.CustomButton ? Brushes.Gold : Brushes.Tan,
                    _showNames.IsChecked == true || DollLook.SpriteOf(piece.Sprite, _sprites) is null ? tag : null));
                return holder;
            }
        }
    }

    /// <summary>Words as the game draws them: the piece's font (InGameFont when none) and colour, at this zoom.</summary>
    private FrameworkElement Words(string text, HudPiece piece, int width, int z)
    {
        Brush? tint = piece.TextColor.Length > 0 && ColorConverter.ConvertFromString(piece.TextColor) is Color c ? new SolidColorBrush(c) : null;
        var font = SpriteFont.For(_sprites, piece.FontName.Length > 0 ? piece.FontName : SpriteDatabase.InGameFontName) ?? SpriteFont.For(_sprites, SpriteDatabase.InGameFontName);
        var lines = new StackPanel();
        foreach (var line in text.Split('\n'))
        {
            if (font?.RenderText(line.Length == 0 ? " " : line, 1, tint) is { } bitmap) lines.Children.Add(Pixel(bitmap, bitmap.PixelWidth, bitmap.PixelHeight, z));
            else lines.Children.Add(new TextBlock { Text = line, Foreground = tint ?? Brushes.White, FontSize = 5.5 * z, FontFamily = new FontFamily("Consolas") });
        }
        return new Border { Width = width * z, ClipToBounds = true, Child = lines, IsHitTestVisible = false };
    }

    private static Image Pixel(ImageSource source, double width, double height, double z)
    {
        var image = new Image { Source = source, Width = width * z, Height = height * z, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    private static TextBlock Label(string text, Brush brush) => new() { Text = text, Foreground = brush, FontSize = 10, Margin = new Thickness(2, 0, 0, 0), IsHitTestVisible = false };

    private static FrameworkElement Dashed(Size size, double z, Brush brush, string? text) => new Border
    {
        Width = size.Width * z, Height = size.Height * z, BorderBrush = brush, BorderThickness = new Thickness(1), IsHitTestVisible = false,
        Child = text is null ? null : new TextBlock { Text = text, Foreground = brush, FontSize = 10, Margin = new Thickness(2, 0, 0, 0), TextWrapping = TextWrapping.Wrap }
    };

    // ---------- Mouse and keys ----------

    private HudPiece? PieceAt(Point p)
    {
        for (int i = _hud.Pieces.Count - 1; i >= 0; i--)
        {
            var piece = _hud.Pieces[i];
            var view = HudPiece.ViewOf(piece.Kind);
            if (view != HudPanelView.Always && view != ShownView) continue;
            var (w, h) = SizeOf(piece);
            if (p.X >= piece.X && p.Y >= piece.Y && p.X < piece.X + w && p.Y < piece.Y + h) return piece;
        }
        return null;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        var at = e.GetPosition(_canvas);
        var p = new Point(at.X / Zoom, at.Y / Zoom);
        if (Piece is { } current && current.Kind != HudPieceKind.CarriedItems)
        {
            var (w, h) = SizeOf(current);
            if (Math.Abs(p.X - (current.X + w)) * Zoom <= 5 && Math.Abs(p.Y - (current.Y + h)) * Zoom <= 5)
            {
                _resize = (p, current.Width, current.Height);
                _canvas.CaptureMouse();
                return;
            }
        }
        if (PieceAt(p) is not { } piece) return;
        if (piece.Id != _selectedId) Select(piece.Id);
        _drag = (p, piece.X, piece.Y);
        _canvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (Piece is not { } piece || (_drag is null && _resize is null)) return;
        var at = e.GetPosition(_canvas);
        var p = new Point(at.X / Zoom, at.Y / Zoom);
        if (_drag is { } d)
        {
            piece.X = d.X + (int)Math.Round(p.X - d.Start.X);
            piece.Y = d.Y + (int)Math.Round(p.Y - d.Start.Y);
        }
        else if (_resize is { } r)
        {
            piece.Width = Math.Max(1, r.Width + (int)Math.Round(p.X - r.Start.X));
            piece.Height = Math.Max(1, r.Height + (int)Math.Round(p.Y - r.Start.Y));
        }
        Redraw();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (Piece is not { } piece) return;
        int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 8 : 1;
        switch (e.Key)
        {
            case Key.Left: piece.X -= step; break;
            case Key.Right: piece.X += step; break;
            case Key.Up: piece.Y -= step; break;
            case Key.Down: piece.Y += step; break;
            case Key.Delete: DeletePiece(); e.Handled = true; return;
            case Key.D when Keyboard.Modifiers.HasFlag(ModifierKeys.Control): DuplicatePiece(); e.Handled = true; return;
            default: return;
        }
        e.Handled = true;
        Redraw();
        ShowProps();
    }

    // ---------- Small builders ----------

    private sealed record SpriteChoice(string Group, string Name, string Display);

    private FrameworkElement SpritePicker(string label, SpriteLink current, Action<SpriteLink> set, double width = 230)
    {
        var choices = new List<SpriteChoice> { new("", "", "(none)") };
        foreach (var group in _sprites.FoldersFor(SpriteFolderIdentity.Paperdoll).Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            choices.AddRange(group.Sprites.Select(s => new SpriteChoice(group.Name, s.Name, $"{s.Name}   ({group.Name})")));
        var combo = new ComboBox { Width = width, ItemsSource = choices, DisplayMemberPath = nameof(SpriteChoice.Display) };
        combo.SelectedItem = choices.FirstOrDefault(c => c.Name == current.SpriteName && (c.Group == current.SpriteGroup || string.IsNullOrEmpty(current.SpriteGroup))) ?? choices[0];
        combo.SelectionChanged += (_, _) =>
        {
            var c = combo.SelectedItem as SpriteChoice;
            set(new SpriteLink { SpriteGroup = c?.Group ?? "", SpriteName = c?.Name ?? "" });
        };
        var expand = new Button { Content = "Expand...", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "The Paperdoll folders and their sprites, with their pictures and a search box." };
        expand.Click += (_, _) =>
        {
            var now = combo.SelectedItem as SpriteChoice;
            if (SpriteBrowserWindow.Pick(this, _sprites, SpriteFolderIdentity.Paperdoll, null, now?.Group, now?.Name) is not { } picked) return;
            var choice = choices.FirstOrDefault(c => c.Group == picked.Group && c.Name == picked.Name);
            if (choice is null) { choice = new SpriteChoice(picked.Group, picked.Name, $"{picked.Name}   ({picked.Group})"); choices.Add(choice); combo.Items.Refresh(); }
            combo.SelectedItem = choice;
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(combo);
        row.Children.Add(expand);
        return Labeled(label, row);
    }

    private FrameworkElement IntField(string label, Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 50, Text = get().ToString() };
        void Commit()
        {
            if (int.TryParse(box.Text.Trim(), out int v)) { set(v); Redraw(); }
            box.Text = get().ToString();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return Labeled(label, box);
    }

    private static FrameworkElement Field(string label, string value, Action<string> set, double width)
    {
        var box = new TextBox { Width = width, Text = value };
        box.LostFocus += (_, _) => set(box.Text.Trim());
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) set(box.Text.Trim()); };
        return Labeled(label, box);
    }

    private static CheckBox Check(string label, Func<bool> get, Action<bool> set)
    {
        var check = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 2, 0, 2) };
        check.Checked += (_, _) => set(true);
        check.Unchecked += (_, _) => set(false);
        return check;
    }

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11 });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(control);
        return panel;
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 4) };

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 4, 2) };
        button.Click += (_, _) => click();
        return button;
    }
}
