using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Object Editor: every object in the world that isn't a book, a
/// container or a character - walls, doors, lights, chairs, money, junk... -
/// each a "master" whose every placement on the map is its own copy.
/// Like the Book and Container editors: name, weight, volume, price, Too
/// Heavy, a sprite, Belong To / Faction, and an Event tab. A new object
/// starts with a Type to choose; once chosen the Type is fixed (shown as a
/// label) and the type's own settings appear. To change a type, delete the
/// object and make a new one.
/// </summary>
public partial class ObjectEditorWindow : Window
{
    private readonly ObjectDatabase _db;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    /// <summary>The map's cell size (the Staircase Designer and staircase previews draw on it).</summary>
    public int TileWidth { get; init; } = 32;
    public int TileHeight { get; init; } = 32;
    private readonly BookDatabase _books;
    private readonly ContainerDatabase _containers;
    private readonly VariableDatabase _variables;
    private readonly StatDatabase _stats;

    private readonly ComboBox _filter = new() { Width = 140, Margin = new Thickness(4, 0, 0, 0) };
    private readonly StackPanel _props = new() { Margin = new Thickness(8) };
    private readonly TabControl _tabs = new() { Margin = new Thickness(6) };
    private readonly TabItem _eventTab = new() { Header = "Event" };
    private readonly ContentControl _eventHost = new();
    private WorldObject? _current;
    private EventEditorControl? _eventEditor;
    private BookEventConditionsControl? _conditions;

    private static readonly ResourceEntry NoSound = new() { Id = -1, Name = "(None)" };
    private sealed record SpriteChoice(string Group, string Name, string Display);
    private static readonly SpriteChoice NoSprite = new("", "", "(none)");
    private sealed record ListEntry(WorldObject Obj)
    {
        public override string ToString() => Obj.TypeChosen ? $"{Obj.Name}   ({ObjectTypes.Name(Obj.Type)})" : $"{Obj.Name}   (choose a type)";
    }

    public ObjectEditorWindow(ObjectDatabase objects, SpriteDatabase sprites, ResourceDatabase resources, BookDatabase books,
        ContainerDatabase containers, VariableDatabase variables, StatDatabase stats)
    {
        _db = objects;
        _sprites = sprites;
        _resources = resources;
        _books = books;
        _containers = containers;
        _variables = variables;
        _stats = stats;
        Title = $"Object Editor [{BuildInfo.CurrentBuild}]";
        Width = 1180;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var toolbar = new ToolBar();
        var newButton = new Button { Content = "New Object", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        newButton.Click += (_, _) => NewObject();
        var deleteButton = new Button { Content = "Delete Object", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        deleteButton.Click += (_, _) => DeleteObject();
        var currencyButton = new Button { Content = "Currency Editor...", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        currencyButton.Click += (_, _) => { new CurrencyEditorWindow(ProjectSession.Currencies) { Owner = this }.ShowDialog(); ShowCurrent(); };
        var painterButton = new Button { Content = "Format Painter...", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2),
            ToolTip = "This object's settings (all but its look, ticked) pasted onto the objects you pick - twenty walls given one wall's size in one go." };
        painterButton.Click += (_, _) =>
        {
            if (_current is not { } source) return;
            if (FormatPainterWindow.Paint(this, source, _db.Objects, o => o.Name, "Objects",
                    skip: new[] { "Name", "Type", "TypeChosen", "FolderId" },
                    unticked: new[] { "SpriteGroup", "SpriteName", "IconPath", "TurnLooks", "EventPages" },
                    groupOf: o => ObjectTypes.Name(o.Type)))
                ShowCurrent();
        };
        toolbar.Items.Add(newButton);
        toolbar.Items.Add(deleteButton);
        toolbar.Items.Add(painterButton);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(new TextBlock { Text = "Show:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        toolbar.Items.Add(_filter);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(currencyButton);

        var filterChoices = new List<string> { "All types" };
        filterChoices.AddRange(ObjectTypes.All.Select(ObjectTypes.Name));
        _filter.ItemsSource = filterChoices;
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => RefreshList();

        InitFolders(toolbar); // the list as folders (ObjectEditorWindow.Folders.cs)

        _tabs.Items.Add(new TabItem { Header = "Properties", Content = new ScrollViewer { Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        _eventTab.Content = _eventHost;
        _tabs.Items.Add(_eventTab);
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs && _tabs.SelectedItem == _eventTab) AttachEvents(); };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel();
        var listTitle = new TextBlock { Text = "Objects", FontWeight = FontWeights.Bold, Margin = new Thickness(6, 6, 6, 0) };
        DockPanel.SetDock(listTitle, Dock.Top);
        left.Children.Add(listTitle);
        left.Children.Add(_list);
        grid.Children.Add(left);
        Grid.SetColumn(_tabs, 1);
        grid.Children.Add(_tabs);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(grid);
        Content = root;

        Closed += (_, _) => AnimationPreviewButton.StopAll();
        RefreshList();
        ShowCurrent();
    }

    // ================= The list =================

    private void NewObject()
    {
        var obj = _db.CreateNew();
        obj.TypeChosen = false; // a type to choose first
        _filter.SelectedIndex = 0;
        _current = obj;
        RefreshList(obj);
        ShowCurrent();
    }

    private void DeleteObject()
    {
        if (_current is not { } obj) return;
        if (MessageBox.Show($"Delete \"{obj.Name}\"? Any placed on the map stay there but show as missing.", "Object Editor", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _db.Objects.Remove(obj);
        _current = null;
        RefreshList();
        ShowCurrent();
    }

    // ================= Properties =================

    private void ShowCurrent()
    {
        AnimationPreviewButton.StopAll();
        _props.Children.Clear();
        _eventEditor = null;
        _conditions = null;
        _eventHost.Content = null;
        if (_current is not { } obj)
        {
            _props.Children.Add(new TextBlock { Text = "Select an object, or click New Object.", Opacity = 0.7 });
            _eventTab.IsEnabled = false;
            return;
        }

        if (!obj.TypeChosen)
        {
            BuildTypeChooser(obj);
            _eventTab.IsEnabled = false;
            return;
        }
        _eventTab.IsEnabled = true;
        if (_tabs.SelectedItem == _eventTab) AttachEvents();

        _props.Children.Add(new TextBlock { Text = $"Object Type: {ObjectTypes.Name(obj.Type)}", FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(0, 0, 0, 2) });
        _props.Children.Add(Note(TypeDescription(obj.Type)));

        BuildIdentity(obj);
        BuildLook(obj);
        switch (obj.Type)
        {
            case ObjectType.Door: BuildDoor(obj); break;
            case ObjectType.LightSource: BuildLight(obj); break;
            case ObjectType.Ammo: BuildBundles(obj, SpriteDatabase.AmmoGroupName); BuildAmmo(obj); break;
            case ObjectType.Money: BuildBundles(obj, SpriteDatabase.MoneyGroupName); BuildMoney(obj); break;
            case ObjectType.Lockpick: BuildBundles(obj, SpriteDatabase.LockpickGroupName); break;
            case ObjectType.Chair: BuildFurniture(obj, bed: false); break;
            case ObjectType.Bed: BuildFurniture(obj, bed: true); break;
            case ObjectType.Bedroll: BuildFurniture(obj, bed: true); BuildNewTypePanel(obj); break; // laid out, it's slept on as a bed
            case ObjectType.Trap: BuildTrap(obj); break;
            case ObjectType.Weapon: BuildWeapon(obj); break; // ObjectEditorWindow.Weapons.cs
            case ObjectType.Armor: BuildArmor(obj); break;
            default: BuildNewTypePanel(obj); break;
        }
        if (HasDurability(obj.Type))
            BuildDurability(obj);
        BuildOwnership(obj);
        BuildLiquidSource(obj);
        BuildPaperdoll(obj);
        BuildBody(obj);
        BuildVehiclePart(obj); // ObjectEditorWindow.Vehicle.cs
    }

    private void BuildTypeChooser(WorldObject obj)
    {
        _props.Children.Add(new TextBlock { Text = "Choose this object's type", FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(0, 0, 0, 6) });
        _props.Children.Add(Note("The type decides what the object is and what double-clicking it does, and which settings it has. Once chosen it can't be changed - to change it, delete the object and make a new one."));
        var combo = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 6) };
        combo.ItemsSource = ObjectTypes.All.Select(ObjectTypes.Name).ToList();
        combo.SelectedIndex = 0;
        var description = Note(TypeDescription(ObjectTypes.All[0]));
        combo.SelectionChanged += (_, _) => description.Text = TypeDescription(ObjectTypes.All[Math.Max(0, combo.SelectedIndex)]);
        var choose = new Button { Content = "Choose this type", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        choose.Click += (_, _) =>
        {
            obj.Type = ObjectTypes.All[Math.Max(0, combo.SelectedIndex)];
            obj.TypeChosen = true;
            if (obj.Name.StartsWith("Object ")) obj.Name = $"New {ObjectTypes.Name(obj.Type)}";
            ObjectTypes.ApplyDefaults(obj);
            RefreshList(obj);
            ShowCurrent();
        };
        _props.Children.Add(combo);
        _props.Children.Add(description);
        _props.Children.Add(choose);
    }

    private static string TypeDescription(ObjectType type) => type switch
    {
        ObjectType.Junk => "Can be picked up, carried and identified. Double-clicking it does nothing unless its Event tab says so - which is how one-off items (a quest artifact, a talking skull) are made.",
        ObjectType.Tile => "Floors, roofs, rugs, pressure plates. A tile has no gravity: it stays at the height it's placed. Not solid: flat, no collision - its events fire when someone steps on it, stands on it or steps off, uses it, or drops something on it. Solid: a floor or a roof you walk on; standing under one, you're inside (Exult: anything solid overhead) and everything from its height up disappears.",
        ObjectType.Wall => "Always solid and never moved. Drawn in front of or behind whoever walks by it exactly as Exult decides (Game_object::compare), so nobody ever looks as if they were underneath it.",
        ObjectType.Door => "Double-click to open and close. Can be locked: its key (or a lockpick) opens it. NPCs open unlocked doors in their way and close them behind them (Exult). Breakable only without a key - as Exult's doors.",
        ObjectType.LightSource => "Double-click to light it and put it out. Lights up the night as Exult does (brightness falls off with distance). Can burn out - it's then \"Spent ...\" and can't be lit again.",
        ObjectType.Ammo => "Arrows, bolts...: stacks of up to 100, drawn with eight bundle pictures by how many there are (Exult's quantity frames). Goes in its own paperdoll slot.",
        ObjectType.Money => "Coins, gems...: stacks of up to 100, drawn with eight bundle pictures by how many there are. Worth its currency's value (Currency Editor). Weight is per coin.",
        ObjectType.Chair => "Somewhere to sit, with a look for each facing and as many seats as you like (a sofa can seat three). Double-click to sit; NPCs on the Sit schedule find a free chair (Exult's Sit_schedule).",
        ObjectType.Bed => "Somewhere to sleep, with a look for each facing, sleeping places, and a cover (a Junk object) turned down over the sleeper. NPCs on the Sleep schedule find a free bed (Exult's Sleep_schedule).",
        ObjectType.Trap => "Hurts whoever steps on it, and can poison, sleep or paralyze. Can be hidden until it goes off, and can go off once or again and again.",
        ObjectType.Key => "Double-click it, then click a container or door it's the key of, to lock or unlock it (Exult's key).",
        ObjectType.Lockpick => "Double-click it, then click a locked container or door to try to pick the lock - it breaks when it fails.",
        _ => NewTypeDescription(type)
    };

    private void BuildIdentity(WorldObject obj)
    {
        var body = Group("Identity");
        var row = new WrapPanel();
        row.Children.Add(TextField("Name", 220, () => obj.Name, v => { obj.Name = v.Length > 0 ? v : obj.Name; RefreshListLabels(); }));
        row.Children.Add(NumberField(obj.Type == ObjectType.Money ? "Weight (per coin)" : obj.Type == ObjectType.Ammo ? "Weight (each)" : "Weight", () => obj.Weight, v => obj.Weight = Math.Max(0, v)));
        row.Children.Add(NumberField(ObjectTypes.IsBundle(obj.Type) ? "Volume (each)" : "Volume", () => obj.Volume, v => obj.Volume = Math.Max(0, v)));
        row.Children.Add(NumberField("Price", () => obj.Price, v => obj.Price = (int)Math.Max(0, v)));
        body.Children.Add(row);
        body.Children.Add(TextField("Description", 520, () => obj.Description, v => obj.Description = v));
        if (obj.Type != ObjectType.Wall)
            body.Children.Add(Check("Too heavy (can't be picked up or moved)", () => !obj.IsPortable, v => obj.IsPortable = !v,
                "Ticked: it stays where it is - a wall torch, a street lamp, a heavy bed. Unticked: it can be picked up and carried."));
        if (obj.Type == ObjectType.Junk)
        {
            body.Children.Add(Check("Stackable", () => obj.IsStackable, v => obj.IsStackable = v));
            body.Children.Add(Check("Its frames are states, not an animation (a bed cover: frame 1 made, frame 2 turned down)", () => obj.FramesAreStates, v => obj.FramesAreStates = v));
        }
    }

    private void RefreshListLabels()
    {
        var selected = _current;
        RefreshList(selected);
    }

    private void BuildLook(WorldObject obj)
    {
        if (obj.Type is ObjectType.Chair or ObjectType.Bed) return; // their looks are per facing, below
        if (obj.Type == ObjectType.Keychain) { BuildKeychainLook(obj); return; } // from the Keychain group, frames by key count
        string label = obj.Type switch
        {
            ObjectType.Door => "Closed sprite",
            ObjectType.LightSource => "Unlit sprite",
            ObjectType.Window => "Sprite (closed, if it has shutters)",
            ObjectType.Plant => "Sprite (used for any stage without its own)",
            ObjectType.Keychain => "Sprite (frames: 0, 1, 2, 3, 4+ keys)",
            ObjectType.Lever => "Off sprite",
            ObjectType.Explosive => "Sprite (unlit)",
            ObjectType.Bedroll => "Sprite (rolled up)",
            ObjectType.LiquidContainer => "Empty sprite",
            ObjectType.Reagent => "Sprite",
            ObjectType.Ammo or ObjectType.Money or ObjectType.Lockpick => "Icon (shown in lists; the bundle pictures below are used in play)",
            _ => "Sprite"
        };
        var body = Group("Look");
        body.Children.Add(SpritePicker(label, () => (obj.SpriteGroup, obj.SpriteName), (g, n) => { obj.SpriteGroup = g; obj.SpriteName = n; }));
        if (obj.Type is not (ObjectType.Tile or ObjectType.Wall or ObjectType.Door or ObjectType.Window or ObjectType.Roof or ObjectType.Stairs))
        {
            var worn = Group("Worn on the body (Serpent Isle paperdolls)");
            worn.Children.Add(Note("On a paperdoll set to \"Worn on the body\" (Character Editor > inventory paperdoll), this picture is drawn on the body itself, over the body's picture at the offset (its pixels) - the armour on the chest, the sword in the fist. None: its icon in its slot."));
            worn.Children.Add(SpritePicker("Worn look", () => (obj.Worn.Look.SpriteGroup, obj.Worn.Look.SpriteName), (g, n) => { obj.Worn.Look.SpriteGroup = g; obj.Worn.Look.SpriteName = n; }, identity: SpriteFolderIdentity.Paperdoll));
            worn.Children.Add(SpritePicker("In the left hand (optional)", () => (obj.Worn.LeftHand.SpriteGroup, obj.Worn.LeftHand.SpriteName), (g, n) => { obj.Worn.LeftHand.SpriteGroup = g; obj.Worn.LeftHand.SpriteName = n; }, compact: true, identity: SpriteFolderIdentity.Paperdoll));
            worn.Children.Add(SpritePicker("On a female character (optional)", () => (obj.Worn.Female.SpriteGroup, obj.Worn.Female.SpriteName), (g, n) => { obj.Worn.Female.SpriteGroup = g; obj.Worn.Female.SpriteName = n; }, compact: true, identity: SpriteFolderIdentity.Paperdoll));
            var offsets = new StackPanel { Orientation = Orientation.Horizontal };
            offsets.Children.Add(Labeled("Offset X", InlineNumber(() => obj.Worn.OffsetX, v => obj.Worn.OffsetX = v)));
            offsets.Children.Add(Labeled("Offset Y", InlineNumber(() => obj.Worn.OffsetY, v => obj.Worn.OffsetY = v)));
            worn.Children.Add(offsets);
        }
    }

    private void BuildDoor(WorldObject obj)
    {
        var d = obj.Door;
        var body = Group("Door");
        body.Children.Add(SpritePicker("Open sprite", () => (d.OpenSprite.SpriteGroup, d.OpenSprite.SpriteName), (g, n) => { d.OpenSprite.SpriteGroup = g; d.OpenSprite.SpriteName = n; }));
        body.Children.Add(SpritePicker("Locked sprite (optional - how it looks closed and locked)", () => (d.LockedSprite.SpriteGroup, d.LockedSprite.SpriteName), (g, n) => { d.LockedSprite.SpriteGroup = g; d.LockedSprite.SpriteName = n; }));
        BuildDoorOpening(obj, body); // how it opens: swing, animation, slide, fade; double doors; by itself; the preview (ObjectEditorWindow.DoorKinds.cs)
        var states = new WrapPanel();
        states.Children.Add(Check("Starts open", () => d.StartsOpen, v => d.StartsOpen = v));
        states.Children.Add(Check("Starts locked", () => d.StartsLocked, v => d.StartsLocked = v));
        states.Children.Add(Check("Magically locked (no key or lockpick opens it; can't be broken)", () => d.MagicallyLocked, v => d.MagicallyLocked = v));
        body.Children.Add(states);
        var keyRow = new WrapPanel();
        var keys = new List<object> { new WorldObject { Id = -1, Name = "(No key - a lockpick or force opens it)" } };
        keys.AddRange(_db.Objects.Where(o => o.Type == ObjectType.Key));
        var keyCombo = new ComboBox { Width = 260, ItemsSource = keys, DisplayMemberPath = "Name" };
        keyCombo.SelectedItem = keys.OfType<WorldObject>().FirstOrDefault(k => k.Id == d.RequiredKeyItemId) ?? keys[0];
        keyCombo.SelectionChanged += (_, _) => d.RequiredKeyItemId = keyCombo.SelectedItem is WorldObject key ? key.Id : -1;
        keyRow.Children.Add(Labeled("Required key (Key objects)", keyCombo));
        keyRow.Children.Add(NumberField("Pick-lock difficulty", () => d.PickLockDifficulty, v => d.PickLockDifficulty = (int)v));
        body.Children.Add(keyRow);
        body.Children.Add(Note("As in Exult: a door that has a key can't be broken - only one without a key (and not magically locked) loses Hit Points when struck. NPCs open closed, unlocked doors in their way and close them behind them; a locked door stops them."));
        var sounds = new WrapPanel();
        sounds.Children.Add(SoundPicker("Open sound", () => d.OpenSoundResourceId, v => d.OpenSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Close sound", () => d.CloseSoundResourceId, v => d.CloseSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Unlock sound", () => d.UnlockSoundResourceId, v => d.UnlockSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Lock sound", () => d.LockSoundResourceId, v => d.LockSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Wrong key sound", () => d.WrongKeySoundResourceId, v => d.WrongKeySoundResourceId = v));
        sounds.Children.Add(SoundPicker("Lockpick broke sound", () => d.LockpickBrokeSoundResourceId, v => d.LockpickBrokeSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Locked rattle", () => d.LockedRattleSoundResourceId, v => d.LockedRattleSoundResourceId = v));
        body.Children.Add(sounds);
    }

    private void BuildLight(WorldObject obj)
    {
        var l = obj.Light;
        var body = Group("Light Source");
        body.Children.Add(SpritePicker("Lit sprite (usually an animation)", () => (l.LitSprite.SpriteGroup, l.LitSprite.SpriteName), (g, n) => { l.LitSprite.SpriteGroup = g; l.LitSprite.SpriteName = n; }));
        body.Children.Add(SpritePicker("Spent sprite (burnt out; optional)", () => (l.SpentSprite.SpriteGroup, l.SpentSprite.SpriteName), (g, n) => { l.SpentSprite.SpriteGroup = g; l.SpentSprite.SpriteName = n; }));
        var row = new WrapPanel();
        row.Children.Add(Check("Starts lit", () => l.StartsLit, v => l.StartsLit = v));
        row.Children.Add(NumberField("Brightness", () => l.Brightness, v => l.Brightness = (int)Math.Max(0, v), "Exult light_data: a candle is 1, a sconce 5, a lamp 7."));
        var givesChoices = new[] { ("Exult's light levels (candle, single, many)", LightGives.ExultLevels), ("The dusk look (sunrise / sunset colour)", LightGives.DuskLook), ("Full daylight", LightGives.FullDaylight) };
        var gives = new ComboBox { Width = 260, ItemsSource = givesChoices.Select(g => g.Item1).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(givesChoices, g => g.Item2 == l.Gives)) };
        gives.SelectionChanged += (_, _) => l.Gives = givesChoices[Math.Max(0, gives.SelectedIndex)].Item2;
        row.Children.Add(Labeled("In the dark it gives", gives));
        row.Children.Add(NumberField("Burns (game minutes, 0 = forever)", () => l.BurnMinutes, v => l.BurnMinutes = (int)Math.Max(0, v)));
        body.Children.Add(row);
        var sounds = new WrapPanel();
        sounds.Children.Add(SoundPicker("Lit sound", () => l.LitSoundResourceId, v => l.LitSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Put out sound", () => l.OffSoundResourceId, v => l.OffSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Spent sound", () => l.SpentSoundResourceId, v => l.SpentSoundResourceId = v));
        body.Children.Add(sounds);
        var fire = new WrapPanel();
        fire.Children.Add(Check("Burns whoever steps into it while lit (a fire)", () => l.BurnsWhenLit, v => l.BurnsWhenLit = v));
        fire.Children.Add(NumberField("Burn damage from", () => l.BurnDamageMin, v => l.BurnDamageMin = (int)Math.Max(0, v)));
        fire.Children.Add(NumberField("to", () => l.BurnDamageMax, v => l.BurnDamageMax = (int)Math.Max(0, v)));
        fire.Children.Add(Check("Solid only while lit (walkable once put out)", () => l.SolidOnlyWhileLit, v => l.SolidOnlyWhileLit = v));
        body.Children.Add(fire);
        body.Children.Add(Note("A fire: tick Burns - whoever walks into it while it's lit is burnt, and again every second they stay; put out, it's harmless. A bucket of a liquid that puts out fires (Food > Liquid > Puts out fires) used on it douses it. By After Put Out runs however it went out (a double-click, a bucket, a lever, a spell): Delete Object Self there makes it vanish. " +
                               "To keep players from putting it out by hand, give it a By Use event (\"It's far too hot to touch!\"): its event runs instead of the double-click. Solid only while lit needs Solid ticked too."));
        body.Children.Add(Note($"Double-click to light it or put it out. Once it has burnt for its minutes it's spent: it shows the spent sprite, is called \"{obj.DisplayName(spent: true)}\", and can't be lit again. " +
                               "At night, lit lights on screen and those the party carries brighten the darkness as Exult's do: each adds Brightness x (75 - 2 x tiles across - 3 x tiles down) from the main character."));
    }

    private void BuildBundles(WorldObject obj, string group)
    {
        var body = Group("Bundle pictures (Exult's quantity frames)");
        var guide = new Button { Content = $"How bundles work...", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 0, 6) };
        guide.Click += (_, _) => new BundleGuideWindow(group) { Owner = this }.ShowDialog();
        body.Children.Add(guide);
        body.Children.Add(Note($"One picture per stack size, from the Sprite Editor's {group} group. An empty one uses the nearest smaller one that's set. A stack holds up to {ObjectTypes.MaxQuantity}."));
        while (obj.Bundle.Levels.Count < 8) obj.Bundle.Levels.Add(new SpriteLink());
        var wrap = new WrapPanel();
        for (int i = 0; i < 8; i++)
        {
            var link = obj.Bundle.Levels[i];
            wrap.Children.Add(SpritePicker($"{ObjectTypes.BundleLevelNames[i]}", () => (link.SpriteGroup, link.SpriteName), (g, n) => { link.SpriteGroup = g; link.SpriteName = n; }, onlyGroup: group, compact: true));
        }
        body.Children.Add(wrap);
    }

    private void BuildAmmo(WorldObject obj)
    {
        var a = obj.Ammo;
        var body = Group("Ammo");
        var slots = new List<(string Label, EquipmentSlot? Slot, string? Custom)> { ("(Backpack / Pouch, then hands)", null, null) };
        slots.AddRange(Enum.GetValues<EquipmentSlot>().Select(s => (s.ToString(), (EquipmentSlot?)s, (string?)null)));
        slots.AddRange(ProjectSession.Characters.Characters.SelectMany(c => c.Paperdoll.CustomSlots).Select(s => s.Name).Distinct()
            .Select(n => ($"{n} (custom)", (EquipmentSlot?)null, (string?)n)));
        var slotCombo = new ComboBox { Width = 220, ItemsSource = slots.Select(s => s.Label).ToList() };
        slotCombo.SelectedIndex = Math.Max(0, slots.FindIndex(s => s.Slot == a.PreferredEquipSlot && s.Custom == a.PreferredCustomSlotName));
        slotCombo.SelectionChanged += (_, _) => { var s = slots[Math.Max(0, slotCombo.SelectedIndex)]; a.PreferredEquipSlot = s.Slot; a.PreferredCustomSlotName = s.Custom; };
        var row = new WrapPanel();
        row.Children.Add(Labeled("Goes in paperdoll slot", slotCombo));
        row.Children.Add(TextField("Family (weapons that fire it)", 160, () => a.Family, v => a.Family = v));
        row.Children.Add(NumberField("Damage bonus", () => a.DamageBonus, v => a.DamageBonus = (int)v));
        body.Children.Add(row);
        var flags = new WrapPanel();
        flags.Children.Add(Check("Poisons", () => a.Poisons, v => a.Poisons = v));
        flags.Children.Add(Check("Explodes", () => a.Explodes, v => a.Explodes = v));
        flags.Children.Add(SoundPicker("Hit sound", () => a.HitSoundResourceId, v => a.HitSoundResourceId = v));
        body.Children.Add(flags);
        body.Children.Add(Note("Ranged weapons aren't built yet, so ammo is carried, stacked and equipped for now; firing it comes with the weapons."));
    }

    private void BuildMoney(WorldObject obj)
    {
        var body = Group("Money");
        var currencies = ProjectSession.Currencies.Currencies;
        var choices = new List<object> { new Currency { Id = -1, Name = "(no currency yet)" } };
        choices.AddRange(currencies);
        var combo = new ComboBox { Width = 200, ItemsSource = choices, DisplayMemberPath = "Name" };
        combo.SelectedItem = currencies.FirstOrDefault(c => c.Id == obj.Money.CurrencyId) ?? choices[0];
        combo.SelectionChanged += (_, _) => obj.Money.CurrencyId = combo.SelectedItem is Currency c ? c.Id : -1;
        body.Children.Add(Labeled("Currency", combo));
        body.Children.Add(Note("Each coin is worth its currency's value (Currency Editor, on the toolbar above). A stack of up to 100 weighs this object's Weight times how many."));
    }

    private void BuildTrap(WorldObject obj)
    {
        var t = obj.Trap;
        var body = Group("Trap");
        var kinds = new[] { ("Ground - whoever steps on it is hurt (spikes, broken glass, needles)", TrapKind.Ground),
                            ("Shooting - it shoots a projectile (a fireball, lightning, darts)", TrapKind.Shooting),
                            ("Explosive - stepped on, it blows up at once", TrapKind.Explosive) };
        var kind = new ComboBox { Width = 420, ItemsSource = kinds.Select(k => k.Item1).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(kinds, k => k.Item2 == t.Kind)) };
        kind.SelectionChanged += (_, _) => { t.Kind = kinds[Math.Max(0, kind.SelectedIndex)].Item2; ShowCurrent(); };
        body.Children.Add(Labeled("Kind", kind));
        var row = new WrapPanel();
        if (t.Kind != TrapKind.Shooting)
        {
            row.Children.Add(NumberField("Damage from", () => t.DamageMin, v => t.DamageMin = (int)Math.Max(0, v)));
            row.Children.Add(NumberField("to", () => t.DamageMax, v => t.DamageMax = (int)Math.Max(0, v)));
        }
        if (t.Kind == TrapKind.Ground)
        {
            var ailment = new ComboBox { Width = 120, ItemsSource = Enum.GetValues<TrapAilment>(), SelectedItem = t.Ailment };
            ailment.SelectionChanged += (_, _) => t.Ailment = (TrapAilment)ailment.SelectedItem;
            row.Children.Add(Labeled("Also makes them", ailment));
        }
        if (t.Kind == TrapKind.Explosive)
            row.Children.Add(NumberField("Blast radius (tiles of 8px)", () => t.RadiusTiles, v => t.RadiusTiles = (int)Math.Clamp(v, 1, 64)));
        if (t.Kind == TrapKind.Shooting)
        {
            var projectiles = new List<WorldObject> { new() { Id = -1, Name = "(none - make a Projectile object)" } };
            projectiles.AddRange(_db.Objects.Where(o => o.Type == ObjectType.Projectile));
            var shoots = new ComboBox { Width = 240, ItemsSource = projectiles, DisplayMemberPath = nameof(WorldObject.Name), SelectedItem = projectiles.FirstOrDefault(p => p.Id == t.ProjectileObjectId) ?? projectiles[0] };
            shoots.SelectionChanged += (_, _) => t.ProjectileObjectId = (shoots.SelectedItem as WorldObject)?.Id ?? -1;
            row.Children.Add(Labeled("Shoots", shoots));
            var triggers = new[] { ("Someone stepping on it (a pressure plate)", TrapTrigger.StepOn), ("Someone in its line of fire", TrapTrigger.LineOfFire) };
            var trigger = new ComboBox { Width = 260, ItemsSource = triggers.Select(x => x.Item1).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(triggers, x => x.Item2 == t.TriggeredBy)) };
            trigger.SelectionChanged += (_, _) => { t.TriggeredBy = triggers[Math.Max(0, trigger.SelectedIndex)].Item2; ShowCurrent(); };
            row.Children.Add(Labeled("Set off by", trigger));
            if (t.TriggeredBy == TrapTrigger.LineOfFire)
                row.Children.Add(NumberField("It sees this far (tiles of 8px)", () => t.RangeTiles, v => t.RangeTiles = (int)Math.Clamp(v, 1, 200)));
        }
        body.Children.Add(row);
        if (t.Kind == TrapKind.Explosive)
        {
            body.Children.Add(SpritePicker("Blast (played once where it was)", () => (t.ExplosionSprite.SpriteGroup, t.ExplosionSprite.SpriteName), (g, n) => { t.ExplosionSprite.SpriteGroup = g; t.ExplosionSprite.SpriteName = n; }));
            body.Children.Add(SoundPicker("Blast sound", () => t.ExplosionSoundResourceId, v => t.ExplosionSoundResourceId = v));
            body.Children.Add(Check("Gone once it has blown up", () => t.RemovedAfterExplosion, v => t.RemovedAfterExplosion = v));
        }
        var flags = new WrapPanel();
        flags.Children.Add(Check("Hidden until it goes off", () => t.Hidden, v => t.Hidden = v));
        flags.Children.Add(Check("Goes off only once", () => t.OneShot, v => t.OneShot = v));
        flags.Children.Add(NumberField(t.Kind == TrapKind.Shooting ? "Shoots again after (seconds)" : "Re-arms after (seconds)", () => t.RearmSeconds, v => t.RearmSeconds = Math.Max(0, v)));
        flags.Children.Add(SoundPicker("Trigger sound", () => t.TriggerSoundResourceId, v => t.TriggerSoundResourceId = v));
        body.Children.Add(flags);
        body.Children.Add(Note(t.Kind switch
        {
            TrapKind.Shooting => "It shoots the way each placed copy faces (Map Editor: right-click it > Facing). The projectile flies across the map as a real object, hurting the first one it meets (Object Editor > its Projectile settings). Its Event tab's By Step On fires as it shoots.",
            TrapKind.Explosive => "Stepped on, it blows up at once: everyone within the blast radius is hurt, breakable things nearby break, and powder kegs catch. Its Event tab's By Step On fires first.",
            _ => "Whoever steps onto it is hurt by it (its Event tab's By Step On fires too)."
        }));
    }

    private void BuildDurability(WorldObject obj)
    {
        var body = Group("Durability (combat)");
        var row = new WrapPanel();
        row.Children.Add(Check("Cannot be destroyed", () => obj.CannotBeDestroyed, v => obj.CannotBeDestroyed = v, "A steel door, an iron grate: blows never hurt it."));
        row.Children.Add(NumberField("Hit points (0 = indestructible)", () => obj.HitPoints, v => obj.HitPoints = (int)Math.Max(0, v)));
        row.Children.Add(NumberField("Defense", () => obj.Defense, v => obj.Defense = (int)Math.Max(0, v), "Each blow is reduced by 1 to this much (not in Exult)."));
        row.Children.Add(SoundPicker("Destroyed sound", () => obj.DestroyedSoundResourceId, v => obj.DestroyedSoundResourceId = v));
        body.Children.Add(row);
    }

    private void BuildOwnership(WorldObject obj)
    {
        var body = Group("Ownership");
        var row = new WrapPanel();
        var factions = new List<Faction> { new() { Id = -1, Name = "(None)" } };
        factions.AddRange(ProjectSession.Factions.Factions);
        var factionCombo = new ComboBox { Width = 160, ItemsSource = factions, DisplayMemberPath = "Name", SelectedItem = factions.FirstOrDefault(f => f.Id == obj.FactionId) ?? factions[0] };
        factionCombo.SelectionChanged += (_, _) => obj.FactionId = (factionCombo.SelectedItem as Faction)?.Id ?? -1;
        var owners = new List<CharacterDefinition> { new() { Id = -1, Name = "(None)" } };
        owners.AddRange(ProjectSession.Characters.Characters);
        var ownerCombo = new ComboBox { Width = 160, ItemsSource = owners, DisplayMemberPath = "Name", SelectedItem = owners.FirstOrDefault(c => c.Id == obj.BelongToId) ?? owners[0] };
        ownerCombo.SelectionChanged += (_, _) => obj.BelongToId = (ownerCombo.SelectedItem as CharacterDefinition)?.Id ?? -1;
        row.Children.Add(Labeled("Faction", factionCombo));
        row.Children.Add(Labeled("Belong To", ownerCombo));
        body.Children.Add(row);
        body.Children.Add(Note("Whose it is: taking it, breaking it or (a door) breaking it down while its owner or someone of their faction is watching is a crime - they warn you, then come after you."));
    }

    private void BuildBody(WorldObject obj)
    {
        var body = Group("Size and collision");
        var flags = new WrapPanel();
        if (obj.Type == ObjectType.Tile)
            flags.Children.Add(Check("Solid - a floor or a roof: walked on, and over someone's head it means \"inside\"", () => obj.IsSolid, v => { obj.IsSolid = v; obj.SizeZ = v ? Math.Max(1, obj.SizeZ) : 0; ShowCurrent(); }));
        if (obj.Type == ObjectType.Tile)
            flags.Children.Add(Check("Floor - part of the ground: drawn under everything on its floor, walls included", () => obj.IsFloorTile, v => { obj.IsFloorTile = v; ShowCurrent(); },
                "Like the map's own tiles (Exult's terrain): always placed at its floor's height and drawn before everything else on that floor, so it can go under a wall, a table or a bed without covering them. Tick Solid too for an upper floor people and things stand on (it holds them up, and is a ceiling for whoever is below); without Solid it is flat and holds nothing up. Its step-on events fire either way."));
        else if (obj.Type is not (ObjectType.Wall or ObjectType.Trap or ObjectType.Door or ObjectType.Window or ObjectType.Stairs))
            flags.Children.Add(Check("Solid (blocks walking)", () => obj.IsSolid, v => obj.IsSolid = v));
        if (obj.Type != ObjectType.Window) // a window's sight is its shutters' setting (Window, above)
            flags.Children.Add(Check("Occludes (blocks line of sight)", () => obj.Occludes, v => obj.Occludes = v));
        if (obj.Type is ObjectType.Junk or ObjectType.Ammo or ObjectType.LightSource or ObjectType.Tool or ObjectType.MusicalInstrument or ObjectType.UseableObject)
            flags.Children.Add(Check("Two-handed", () => obj.IsTwoHanded, v => obj.IsTwoHanded = v));
        if (obj.Type is not (ObjectType.Tile or ObjectType.Wall or ObjectType.Door or ObjectType.Trap or ObjectType.Window or ObjectType.Roof or ObjectType.Stairs))
            flags.Children.Add(Check("Things can be put on top of it (they rest at its height: Size Z below)", () => obj.AllowCellElevation,
                v => { obj.AllowCellElevation = v; obj.IsCellStackable = v; },
                "A chair seat, a table, a crate: something dropped on it stacks on top, raised to its height (the blue Height Z of the box below). Unticked: nothing can be put on it."));
        body.Children.Add(flags);
        if (obj.Type == ObjectType.Tile) body.Children.Add(Note(obj.IsSolid
            ? "A solid tile is a slab Size Z lifts thick: you walk on it and things put on it rest on top. It never falls - it stays at the height it's placed (an upper floor, a roof). Standing under it, you're inside, as in Exult: it and everything from its height up disappear until you step out."
            : "A tile that isn't solid is flat (0 lifts high) and never blocks anyone - a rug, a floor pattern, a pressure plate. It never falls either."));
        if (obj.Type == ObjectType.Wall) body.Children.Add(Note("A wall is always solid. Its footprint is what Exult compares to decide who is drawn in front: make a long wall's footprint as long as the wall."));
        if (obj.Type == ObjectType.Door) body.Children.Add(Note("A door is solid while closed and walked through while open."));
        if (obj.Type == ObjectType.Window) body.Children.Add(Note("A window is always solid, and drawn by Exult's wall rule like a wall: make a long window's footprint as long as the window."));
        if (obj.Type == ObjectType.Stairs && obj.Stairs.Kind == StairsKind.Stairs && obj.Stairs.DesignId < 0) body.Children.Add(Note($"Height (blue, Size Z) = how many steps up; the footprint is cut into that many steps toward {obj.Stairs.RisesToward}."));

        var size = new SolidSizeEditor { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        size.Load(SolidSizeEditor.LoadSpriteFrame(_sprites, obj.SpriteGroup, MainSpriteName(obj)), obj.SizeX, obj.SizeY, obj.SizeZ, obj.SolidOffsetX, obj.SolidOffsetY,
            obj.IsSolid || ObjectTypes.IsWallLike(obj.Type));
        size.SizeEdited += (_, _) =>
        {
            obj.SizeX = size.SizeX;
            obj.SizeY = size.SizeY;
            obj.SizeZ = obj.Type == ObjectType.Trap || (obj.Type == ObjectType.Tile && !obj.IsSolid) ? 0 : Math.Max(obj.Type == ObjectType.Tile ? 1 : 0, size.SizeZ);
            obj.SolidOffsetX = size.OffsetX;
            obj.SolidOffsetY = size.OffsetY;
        };
        if (obj.Type != ObjectType.Door) { body.Children.Add(size); return; }
        // A door: its open sprite's position beside the solid size, so it swings open on its hinge.
        var sideBySide = new StackPanel { Orientation = Orientation.Horizontal };
        sideBySide.Children.Add(size);
        sideBySide.Children.Add(BuildOpenPositionEditor(obj));
        body.Children.Add(sideBySide);
    }

    /// <summary>
    /// Where a door's open sprite is drawn relative to its closed one: the closed door faded, the
    /// open one over it, three times their size. Drag the open door (or use the arrows) until it
    /// hangs from the hinge - the Map Editor and the game draw it exactly there.
    /// </summary>
    private FrameworkElement BuildOpenPositionEditor(WorldObject obj)
    {
        const double zoom = 3, canvasW = 300, canvasH = 260, baseY = 230;
        var d = obj.Door;
        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), ClipToBounds = true };
        var closedFrame = SolidSizeEditor.LoadSpriteFrame(_sprites, obj.SpriteGroup, obj.SpriteName);
        var openFrame = SolidSizeEditor.LoadSpriteFrame(_sprites, d.OpenSprite.SpriteGroup, d.OpenSprite.SpriteName);
        var label = new TextBlock { Margin = new Thickness(0, 4, 0, 0) };
        Image? openImage = null;
        (int W, int H) Size(BitmapSource? frame) => frame is null ? (0, 0) : SpriteSizing.Quantize(frame.PixelWidth, frame.PixelHeight);
        void Place()
        {
            label.Text = $"Open sprite offset: {d.OpenOffsetX} px, {d.OpenOffsetY} px";
            if (openImage is null) return;
            var (w, h) = Size(openFrame);
            Canvas.SetLeft(openImage, canvasW / 2 - w * zoom / 2 + d.OpenOffsetX * zoom);
            Canvas.SetTop(openImage, baseY - h * zoom + d.OpenOffsetY * zoom);
        }
        canvas.Children.Add(new Line { X1 = 0, X2 = canvasW, Y1 = baseY, Y2 = baseY, Stroke = Brushes.Gray, StrokeDashArray = new DoubleCollection { 2, 3 } });
        if (closedFrame is not null)
        {
            var (w, h) = Size(closedFrame);
            var closed = new Image { Source = closedFrame, Width = w * zoom, Height = h * zoom, Opacity = 0.35, Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(closed, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(closed, canvasW / 2 - w * zoom / 2);
            Canvas.SetTop(closed, baseY - h * zoom);
            canvas.Children.Add(closed);
        }
        if (openFrame is not null)
        {
            var (w, h) = Size(openFrame);
            openImage = new Image { Source = openFrame, Width = w * zoom, Height = h * zoom, Opacity = 0.9, Stretch = Stretch.Fill, Cursor = Cursors.SizeAll, ToolTip = "Drag to where the open door hangs from its hinge" };
            RenderOptions.SetBitmapScalingMode(openImage, BitmapScalingMode.NearestNeighbor);
            canvas.Children.Add(openImage);
            Point? grab = null;
            int startX = 0, startY = 0;
            openImage.MouseLeftButtonDown += (_, e) => { grab = e.GetPosition(canvas); startX = d.OpenOffsetX; startY = d.OpenOffsetY; openImage.CaptureMouse(); e.Handled = true; };
            openImage.MouseMove += (_, e) =>
            {
                if (grab is not { } g) return;
                var p = e.GetPosition(canvas);
                d.OpenOffsetX = startX + (int)Math.Round((p.X - g.X) / zoom);
                d.OpenOffsetY = startY + (int)Math.Round((p.Y - g.Y) / zoom);
                Place();
            };
            openImage.MouseLeftButtonUp += (_, _) => { grab = null; openImage.ReleaseMouseCapture(); };
        }
        Place();
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (text, dx, dy) in new[] { ("←", -1, 0), ("→", 1, 0), ("↑", 0, -1), ("↓", 0, 1) })
        {
            var button = new Button { Content = text, Width = 28, Margin = new Thickness(0, 0, 4, 0) };
            button.Click += (_, _) => { d.OpenOffsetX += dx; d.OpenOffsetY += dy; Place(); };
            arrows.Children.Add(button);
        }
        var reset = new Button { Content = "Reset", Padding = new Thickness(6, 0, 6, 0) };
        reset.Click += (_, _) => { d.OpenOffsetX = 0; d.OpenOffsetY = 0; Place(); };
        arrows.Children.Add(reset);
        var panel = new StackPanel { Margin = new Thickness(16, 4, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Open sprite position", FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = openFrame is null ? "Pick an open sprite above first." : "The closed door is faded; drag the open one until it hangs from the hinge.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = canvasW, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = canvas, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(arrows);
        panel.Children.Add(label);
        return panel;
    }

    /// <summary>The sprite the size editor measures: the object's own, or (chairs, beds) its south-facing one.</summary>
    private static string MainSpriteName(WorldObject obj) =>
        obj.Type is ObjectType.Chair or ObjectType.Bed ? obj.Furniture.Resolve(SpriteDirection.South)?.Sprite.SpriteName ?? "" : obj.SpriteName;

    // ================= Chairs and beds =================

    private SpriteDirection _furnitureFacing = SpriteDirection.South;

    private void BuildFurniture(WorldObject obj, bool bed)
    {
        var f = obj.Furniture;
        var body = Group(obj.Type == ObjectType.Bedroll ? "Bedroll: laid-out looks and sleeping places" : bed ? "Bed: looks and sleeping places" : "Chair: looks and seats");
        body.Children.Add(Note(bed
            ? "Like a character pose: one look per facing. Pick a facing, give it a sprite, then click on the picture to add a sleeping place (where the sleeper lies), drag one to move it, right-click to remove it. A double bed has two. In the Map Editor you choose which way each placed bed faces."
            : "Like a character pose: one look per facing. Pick a facing, give it a sprite, then click on the picture to add a seat (where the sitter's feet touch the floor - usually the bottom of the chair, under the seat; the Sit pose art does the sitting, as in Ultima VII), drag a seat to move it, right-click to remove it. A sofa can have three. The sitter faces the chair's direction (Exult). In the Map Editor you choose which way each placed chair faces."));

        // Laid out like a Character Editor pose: the picture (with play / stop), the direction dial under it, then mute and volume.
        var block = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        var poseBox = new StackPanel();
        var poseBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Padding = new Thickness(8), Child = poseBox, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0)
        };
        var detail = new StackPanel();
        void ShowFacing()
        {
            AnimationPreviewButton.StopAll();
            poseBox.Children.Clear();
            detail.Children.Clear();
            if (!f.Facings.TryGetValue(_furnitureFacing, out var facing)) f.Facings[_furnitureFacing] = facing = new FurnitureFacing();
            poseBox.Children.Add(new TextBlock { Text = bed ? "Bed pose" : "Chair pose", FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            var picture = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            var square = new Border { Width = 120, Height = 120, Background = Brushes.Black, BorderBrush = Brushes.Black, BorderThickness = new Thickness(2), Child = picture, HorizontalAlignment = HorizontalAlignment.Center };
            poseBox.Children.Add(square);
            var sprite = facing.Sprite.IsSet ? ObjectLooks.Find(_sprites, facing.Sprite.SpriteGroup, facing.Sprite.SpriteName) : null;
            if (sprite is not null)
                poseBox.Children.Add(new AnimationPreviewButton(sprite, picture, _resources, f.Muted ? 0 : f.Volume / 100.0) { Margin = new Thickness(0, 4, 0, 0) });
            else
                square.Child = new TextBlock { Text = "📷", FontSize = 40, Opacity = 0.25, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            poseBox.Children.Add(new TextBlock { Text = $"Direction: {_furnitureFacing}", FontSize = 11, Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Center });
            poseBox.Children.Add(BuildDirectionDial(f, d => { _furnitureFacing = d; ShowFacing(); }));
            poseBox.Children.Add(BuildVolumeRow(f, ShowFacing));

            detail.Children.Add(SpritePicker($"{_furnitureFacing} sprite", () => (facing.Sprite.SpriteGroup, facing.Sprite.SpriteName),
                (g, n) => { facing.Sprite.SpriteGroup = g; facing.Sprite.SpriteName = n; ShowFacing(); }));
            if (!bed)
            {
                detail.Children.Add(SpritePicker($"{_furnitureFacing} chair back (drawn over the sitter - optional)", () => (facing.BackSprite.SpriteGroup, facing.BackSprite.SpriteName),
                    (g, n) => { facing.BackSprite.SpriteGroup = g; facing.BackSprite.SpriteName = n; ShowFacing(); }));
                detail.Children.Add(Note("The chair is always drawn under whoever sits in it. For a chair facing north or west, give it its back: a copy of this sprite with everything but the backrest erased (see-through), the same size - it is laid exactly over the chair and drawn over the sitter, so the seat is under them and the back covers their back."));
            }
            detail.Children.Add(BuildSpotEditor(facing, bed));
        }
        block.Children.Add(poseBorder);
        block.Children.Add(detail);
        body.Children.Add(block);
        ShowFacing();

        if (bed && obj.Type == ObjectType.Bed)
        {
            var covers = new List<object> { new WorldObject { Id = -1, Name = "(No cover)" } };
            covers.AddRange(_db.Objects.Where(o => o.Type == ObjectType.Junk && o.FramesAreStates));
            var coverCombo = new ComboBox { Width = 240, ItemsSource = covers, DisplayMemberPath = "Name" };
            coverCombo.SelectedItem = covers.OfType<WorldObject>().FirstOrDefault(o => o.Id == f.CoverObjectId) ?? covers[0];
            coverCombo.SelectionChanged += (_, _) => f.CoverObjectId = coverCombo.SelectedItem is WorldObject c ? c.Id : -1;
            body.Children.Add(Labeled("Cover (a Junk object whose frames are states: frame 1 made, frame 2 turned down)", coverCombo));
            body.Children.Add(Note("In play the cover lies on the bed as its own object: a sleeper gets frame 2 drawn over them, and getting up makes the bed again (frame 1) - as Exult's Sleep_schedule turns the bedspread down and makes the bed. The cover can be taken off the bed like any object; with none, the bed has no cover."));
        }
        body.Children.Add(SoundPicker(bed ? "Lie down sound" : "Sit sound", () => f.SitSoundResourceId, v => f.SitSoundResourceId = v));
        if (!bed) return;
        var sleep = Group("Sleeping in it (the player)");
        sleep.Children.Add(Check("Ask how many hours to sleep (the slider - Edit > Settings > Slider)", () => f.AsksSleepHours, v => { f.AsksSleepHours = v; ShowCurrent(); }));
        if (!f.AsksSleepHours) { sleep.Children.Add(Note("Unticked: lying down is just lying down - no sleep, no time passing.")); return; }
        sleep.Children.Add(TextField("Question", 320, () => f.SleepQuestion, v => f.SleepQuestion = v));
        sleep.Children.Add(TextField("Choosing 0 hours (don't sleep), the sleeper says", 320, () => f.CancelSleepLine, v => f.CancelSleepLine = v));
        var range = new WrapPanel();
        range.Children.Add(NumberField("Fewest hours", () => f.SleepMinHours, v => f.SleepMinHours = (int)Math.Clamp(v, 1, SliderSettings.MaxValue)));
        range.Children.Add(NumberField("Most hours (999 at most)", () => f.SleepMaxHours, v => f.SleepMaxHours = (int)Math.Clamp(v, 1, SliderSettings.MaxValue)));
        range.Children.Add(NumberField("Starts at", () => f.SleepDefaultHours, v => f.SleepDefaultHours = (int)Math.Clamp(v, 1, SliderSettings.MaxValue)));
        range.Children.Add(NumberField("Step", () => f.SleepStepHours, v => f.SleepStepHours = (int)Math.Clamp(v, 1, SliderSettings.MaxValue)));
        sleep.Children.Add(range);
        sleep.Children.Add(Note("Lying down, the player is asked how long to sleep, within this range. Accepted: the screen fades to black and the hours pass at once, as Exult's rest does - " +
                               "food goes down by the hours slept, health and mana come back, ailments wear off, lights burn down, and everyone moves on to where their schedule puts them. " +
                               "Then it fades back in, still lying in bed (move to get up). The slider starts at 0: 0 is \"don't sleep\" - the line above is said and the sleeper gets up. Escape: just lying there. Not with enemies in sight."));
    }

    /// <summary>The Character Editor's direction wheel: eight dots round a circle (green: has a sprite), the chosen one blue with a line to it. Click a dot to pick that facing.</summary>
    private FrameworkElement BuildDirectionDial(FurnitureSettings f, Action<SpriteDirection> chosen)
    {
        const double size = 70, centre = size / 2, radius = 26, dot = 11;
        var canvas = new Canvas { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        canvas.Children.Add(new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Brushes.Gray, StrokeThickness = 1, Margin = new Thickness(centre - radius, centre - radius, 0, 0) });
        foreach (var direction in Enum.GetValues<SpriteDirection>())
        {
            double angle = ((int)direction * 45 - 90) * Math.PI / 180; // North at the top, clockwise
            double x = centre + radius * Math.Cos(angle), y = centre + radius * Math.Sin(angle);
            bool selected = direction == _furnitureFacing;
            bool hasSprite = f.Facings.TryGetValue(direction, out var fd) && fd.Sprite.IsSet;
            if (selected) canvas.Children.Add(new Line { X1 = centre, Y1 = centre, X2 = x, Y2 = y, Stroke = Brushes.RoyalBlue, StrokeThickness = 2 });
            var marker = new Ellipse
            {
                Width = dot, Height = dot, Stroke = Brushes.DimGray, StrokeThickness = 1, Cursor = Cursors.Hand,
                Fill = selected ? Brushes.RoyalBlue : hasSprite ? Brushes.MediumSeaGreen : Brushes.White,
                ToolTip = hasSprite ? $"{direction}" : $"{direction} (no sprite yet)"
            };
            Canvas.SetLeft(marker, x - dot / 2);
            Canvas.SetTop(marker, y - dot / 2);
            var d = direction;
            marker.MouseLeftButtonDown += (_, e) => { chosen(d); e.Handled = true; };
            canvas.Children.Add(marker);
        }
        return canvas;
    }

    /// <summary>Mute toggle and volume slider, as a character pose's (they're independent: changing the volume doesn't unmute).</summary>
    private static FrameworkElement BuildVolumeRow(FurnitureSettings f, Action changed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var mute = new TextBlock { Text = f.Muted ? "🔇" : "🔊", FontSize = 14, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = f.Muted ? "Muted - click to unmute" : "Unmuted - click to mute" };
        mute.MouseLeftButtonDown += (_, _) => { f.Muted = !f.Muted; changed(); };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = f.Volume, Width = 80, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Volume (doesn't itself unmute)" };
        slider.ValueChanged += (_, e) => f.Volume = (int)e.NewValue;
        row.Children.Add(mute);
        row.Children.Add(slider);
        return row;
    }

    private static string Abbreviation(SpriteDirection d) => d switch
    {
        SpriteDirection.North => "N", SpriteDirection.NorthEast => "NE", SpriteDirection.East => "E", SpriteDirection.SouthEast => "SE",
        SpriteDirection.South => "S", SpriteDirection.SouthWest => "SW", SpriteDirection.West => "W", _ => "NW"
    };

    /// <summary>The character whose Sleeping / Sit pose is drawn on the spots (remembered while the editor is open).</summary>
    private static int _spotPreviewCharacterId = -1;

    /// <summary>
    /// The facing's sprite, three times its size, with room round it, and its
    /// seats or sleeping places: each shows the sitter or sleeper itself - a
    /// character's Sit or Sleeping pose for this facing, drawn exactly where
    /// the game puts it (its feet, the bottom middle of the pose, on the spot) -
    /// with a numbered dot. Click to add one, drag the body or the dot to move
    /// it, right-click to remove it.
    /// </summary>
    private FrameworkElement BuildSpotEditor(FurnitureFacing facing, bool bed)
    {
        const double zoom = 3, pad = 28; // sprite pixels of room all round, to see a body hanging over the edge
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), ClipToBounds = true };
        var frame = SolidSizeEditor.LoadSpriteFrame(_sprites, facing.Sprite.SpriteGroup, facing.Sprite.SpriteName);
        double w = frame?.PixelWidth ?? 32, h = frame?.PixelHeight ?? 32;
        canvas.Width = (w + 2 * pad) * zoom;
        canvas.Height = (h + 2 * pad) * zoom;
        if (frame is not null)
        {
            var picture = new Image { Source = frame, Width = w * zoom, Height = h * zoom, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(picture, pad * zoom);
            Canvas.SetTop(picture, pad * zoom);
            Panel.SetZIndex(picture, 0); // the chair or bed: always under the sitter or sleeper, as in the game
            canvas.Children.Add(picture);
            // A chair's back (optional): laid exactly over the chair, over the sitter - as the game draws it.
            if (!bed && facing.BackSprite.IsSet && SolidSizeEditor.LoadSpriteFrame(_sprites, facing.BackSprite.SpriteGroup, facing.BackSprite.SpriteName) is { } back)
            {
                var backPicture = new Image { Source = back, Width = w * zoom, Height = h * zoom, Stretch = Stretch.Uniform, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(backPicture, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(backPicture, pad * zoom);
                Canvas.SetTop(backPicture, pad * zoom);
                Panel.SetZIndex(backPicture, 2);
                canvas.Children.Add(backPicture);
            }
        }

        // The body drawn on each spot: a character's Sleeping (bed) or Sit (chair) pose for this facing.
        string poseName = bed ? "Sleeping" : "Sit";
        var pose = ProjectSession.Poses.Poses.FirstOrDefault(pd => pd.Name == poseName);
        var withPose = pose is null ? new List<CharacterDefinition>()
            : ProjectSession.Characters.Characters.Where(c => c.PoseSprites.TryGetValue(pose.Id, out var a) && a.Directions.Count > 0).ToList();
        var previewChoices = new List<CharacterDefinition> { new() { Id = -1, Name = "(no body - dots only)" } };
        previewChoices.AddRange(withPose);
        if (_spotPreviewCharacterId == -1 && withPose.Count > 0) _spotPreviewCharacterId = withPose[0].Id;
        var previewCombo = new ComboBox { Width = 200, ItemsSource = previewChoices, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Left };
        previewCombo.SelectedItem = previewChoices.FirstOrDefault(c => c.Id == _spotPreviewCharacterId) ?? previewChoices[0];
        System.Windows.Media.Imaging.BitmapSource? Body()
        {
            if (pose is null || previewCombo.SelectedItem is not CharacterDefinition { Id: >= 0 } who || !who.PoseSprites.TryGetValue(pose.Id, out var assignment)) return null;
            var link = assignment.Resolve(_furnitureFacing);
            return link is null ? null : SolidSizeEditor.LoadSpriteFrame(_sprites, link.SpriteGroup, link.SpriteName);
        }

        int dragging = -1;
        void Redraw()
        {
            foreach (var old in canvas.Children.OfType<FrameworkElement>().Where(c => c.Tag is int).ToList()) canvas.Children.Remove(old);
            var body = Body();
            for (int i = 0; i < facing.Spots.Count; i++)
            {
                var spot = facing.Spots[i];
                int index = i;
                if (body is not null)
                {
                    // Exactly as the game draws it: in a box rounded up to 8 pixels (fitted, centred), the box's bottom middle on the spot.
                    var (bw, bh) = SpriteSizing.Quantize(body.PixelWidth, body.PixelHeight);
                    var ghost = new Image { Source = body, Width = bw * zoom, Height = bh * zoom, Stretch = Stretch.Uniform, Tag = i, Cursor = Cursors.SizeAll, // solid, as the game draws it
                        ToolTip = $"{(bed ? "Sleeper" : "Sitter")} {i + 1} - drag to move, right-click to remove" };
                    RenderOptions.SetBitmapScalingMode(ghost, BitmapScalingMode.NearestNeighbor);
                    Canvas.SetLeft(ghost, (spot.X + pad - bw / 2.0) * zoom);
                    Canvas.SetTop(ghost, (spot.Y + pad - bh) * zoom);
                    Panel.SetZIndex(ghost, 1); // over the chair, under its back
                    ghost.MouseLeftButtonDown += (_, e) => { dragging = index; canvas.CaptureMouse(); e.Handled = true; };
                    ghost.MouseRightButtonDown += (_, e) => { facing.Spots.RemoveAt(index); Redraw(); e.Handled = true; };
                    canvas.Children.Add(ghost);
                }
                var dot = new Grid { Width = 18, Height = 18, Tag = i, Cursor = Cursors.SizeAll, ToolTip = $"{(bed ? "Sleeping place" : "Seat")} {i + 1} - drag to move, right-click to remove" };
                dot.Children.Add(new Ellipse { Fill = bed ? Brushes.MediumPurple : Brushes.Orange, Stroke = Brushes.Black, StrokeThickness = 1 });
                dot.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                Canvas.SetLeft(dot, (spot.X + pad) * zoom - 9);
                Canvas.SetTop(dot, (spot.Y + pad) * zoom - 9);
                Panel.SetZIndex(dot, 3); // always on top, to grab
                dot.MouseLeftButtonDown += (_, e) => { dragging = index; canvas.CaptureMouse(); e.Handled = true; };
                dot.MouseRightButtonDown += (_, e) => { facing.Spots.RemoveAt(index); Redraw(); e.Handled = true; };
                canvas.Children.Add(dot);
            }
        }
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (frame is null) return;
            var p = e.GetPosition(canvas);
            facing.Spots.Add(new SeatSpot { X = Math.Round(p.X / zoom - pad), Y = Math.Round(p.Y / zoom - pad) });
            Redraw();
        };
        canvas.MouseMove += (_, e) =>
        {
            if (dragging < 0 || dragging >= facing.Spots.Count) return;
            var p = e.GetPosition(canvas);
            facing.Spots[dragging].X = Math.Clamp(Math.Round(p.X / zoom - pad), -pad, w + pad);
            facing.Spots[dragging].Y = Math.Clamp(Math.Round(p.Y / zoom - pad), -pad, h + pad);
            Redraw();
        };
        canvas.MouseLeftButtonUp += (_, _) => { dragging = -1; canvas.ReleaseMouseCapture(); };
        previewCombo.SelectionChanged += (_, _) => { _spotPreviewCharacterId = (previewCombo.SelectedItem as CharacterDefinition)?.Id ?? -1; Redraw(); };
        Redraw();

        var border = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = canvas, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 6) };
        var panel = new StackPanel();
        var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        previewRow.Children.Add(new TextBlock { Text = $"Show the {poseName} pose of: ", VerticalAlignment = VerticalAlignment.Center });
        previewRow.Children.Add(previewCombo);
        panel.Children.Add(previewRow);
        if (withPose.Count == 0)
            panel.Children.Add(Note($"No character has a {poseName} pose yet (Character Editor > poses) - give one a {poseName} sprite to see the {(bed ? "sleeper" : "sitter")} here."));
        panel.Children.Add(border);
        panel.Children.Add(Note(frame is null ? "Give this facing a sprite first."
            : $"{facing.Spots.Count} {(bed ? "sleeping place" : "seat")}{(facing.Spots.Count == 1 ? "" : "s")}. Click to add one; drag the {(bed ? "sleeper" : "sitter")} (or its dot) until it lies right on the {(bed ? "mattress" : "seat")} - the game puts it exactly there. Right-click to remove."));
        return panel;
    }

    // ================= Event tab =================

    private void AttachEvents()
    {
        if (_current is not { TypeChosen: true } obj || _eventEditor is not null) return;
        if (obj.EventPages.Count == 0) obj.EventPages.Add(new BookEventPage()); // never headless, as books and containers

        var pages = new System.Collections.ObjectModel.ObservableCollection<EventCommandPage>(
            obj.EventPages.Select((p, i) => new EventCommandPage { Label = (i + 1).ToString(), Commands = p.Commands }));
        _conditions = new BookEventConditionsControl();
        _eventEditor = new EventEditorControl
        {
            ResourceDatabase = _resources,
            Variables = _variables,
            CurrentBookLocalVariables = obj.LocalVariables,
            Books = _books,
            Objects = _db,
            Containers = _containers,
            Sprites = _sprites,
            CurrentBook = null
        };
        var hitChoices = new[] { ("A character or an object", HitTargetKind.Any), ("A character", HitTargetKind.Character), ("An object", HitTargetKind.Object) };
        var hitTarget = new ComboBox { Width = 200, ItemsSource = hitChoices.Select(h => h.Item1).ToList(), Margin = new Thickness(6, 0, 0, 0) };
        BookEventConditions? shownPage = obj.EventPages[0].Conditions;
        void ShowHitTarget() => hitTarget.SelectedIndex = Math.Max(0, Array.FindIndex(hitChoices, h => h.Item2 == (shownPage?.HitTarget ?? HitTargetKind.Any)));
        hitTarget.SelectionChanged += (_, _) => { if (shownPage is not null && hitTarget.SelectedIndex >= 0) shownPage.HitTarget = hitChoices[hitTarget.SelectedIndex].Item2; };
        ShowHitTarget();
        _eventEditor.PageSelected += index =>
        {
            shownPage = index >= 0 && index < obj.EventPages.Count ? obj.EventPages[index].Conditions : null;
            if (_conditions is not null) _conditions.Conditions = shownPage;
            ShowHitTarget();
        };
        _eventEditor.PageInserted += index => { if (index >= 0 && index <= obj.EventPages.Count) obj.EventPages.Insert(index, new BookEventPage()); };
        _eventEditor.PageRemoved += index => { if (index >= 0 && index < obj.EventPages.Count) obj.EventPages.RemoveAt(index); };
        _eventEditor.ContentChanged += () =>
        {
            if (_eventEditor is null) return;
            for (int i = 0; i < _eventEditor.Pages.Count && i < obj.EventPages.Count; i++) obj.EventPages[i].Commands = _eventEditor.Pages[i].Commands;
        };
        _eventEditor.Pages = pages;

        _conditions.CurrentBookId = obj.Id;
        _conditions.CurrentBookLocalVariables = obj.LocalVariables;
        _conditions.SetDatabases(_books, _db, _containers, _sprites, _variables, _stats);
        _conditions.ApplyTriggerLabels(TriggerLabels(obj.Type), TriggersLeftOut(obj.Type));
        _conditions.Conditions = obj.EventPages[0].Conditions;

        var debug = new Button { Content = "Debug", Padding = new Thickness(10, 4, 10, 4) };
        debug.Click += (_, _) =>
        {
            if (_conditions?.Conditions is not { } conditions) { MessageBox.Show("Select an event page first.", "Debug Event", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var commands = _eventEditor?.CurrentEditingPage?.Commands ?? new List<EventCommand>();
            new BookEventDebugWindow(conditions, commands, _resources, _stats, obj.Id) { Owner = this }.Show();
        };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(debug);
        if (obj.Type is ObjectType.Ammo or ObjectType.Projectile)
        {
            top.Children.Add(new TextBlock { Text = "By Hitting Target - when it hits:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            top.Children.Add(hitTarget);
        }
        top.Children.Add(new TextBlock { Text = TriggerHelp(obj.Type), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 760 });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 1, 0), Child = _conditions };
        grid.Children.Add(left);
        Grid.SetColumn(_eventEditor, 1);
        grid.Children.Add(_eventEditor);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(grid);
        _eventHost.Content = dock;
    }

    /// <summary>Each type's own names for the triggers its object fires in play - and, for every type, By Tool Used On It (a Tool object used on it).</summary>
    private static IReadOnlyDictionary<BookTriggerConditionBased, string> TriggerLabels(ObjectType type)
    {
        var labels = new Dictionary<BookTriggerConditionBased, string>(BaseTriggerLabels(type));
        labels[BookTriggerConditionBased.ByTradeItem] = "By Tool Used On It";
        // Every object: something dropped onto it (on the map, in a window) - its event runs first, and can use it up.
        labels[BookTriggerConditionBased.ByCombineItem] = "By Item Combined To It";
        // Every object: something put down on top of it (or, a tile, on it) stays there and its event runs - a page can wait for one item only (Select Item).
        labels[BookTriggerConditionBased.ByDropItem] = type == ObjectType.Tile ? "By Item Dropped On It" : "By Item Dropped On It (put on top)";
        if (ProjectSession.IsUltima8) { labels[BookTriggerConditionBased.ByLook] = "By Look (a single click)"; labels[BookTriggerConditionBased.ByGotHit] = "By Got Hit"; }
        if (type is ObjectType.Ammo or ObjectType.Projectile) labels[BookTriggerConditionBased.ByHitTarget] = "By Hitting Target (a character or an object)";
        return labels;
    }

    /// <summary>The triggers that mean nothing for a type, left out of its list.</summary>
    private static BookTriggerConditionBased[] TriggersLeftOut(ObjectType type) => type switch
    {
        ObjectType.Junk => new[] { BookTriggerConditionBased.ByEnter, BookTriggerConditionBased.ByStay, BookTriggerConditionBased.ByAfterReadItem,
            BookTriggerConditionBased.ByHavingAnItem, BookTriggerConditionBased.ByNotHavingAnItem, BookTriggerConditionBased.ByExit },
        ObjectType.Tile => new[] { BookTriggerConditionBased.ByAfterReadItem },
        ObjectType.Door => new[] { BookTriggerConditionBased.ByPickupItem },
        ObjectType.LightSource => new[] { BookTriggerConditionBased.ByExit },
        ObjectType.Ammo => new[] { BookTriggerConditionBased.ByDestroyItem, BookTriggerConditionBased.ByAfterReadItem,
            BookTriggerConditionBased.ByPickupItem, BookTriggerConditionBased.ByHavingAnItem, BookTriggerConditionBased.ByNotHavingAnItem,
            BookTriggerConditionBased.ByEnter, BookTriggerConditionBased.ByStay, BookTriggerConditionBased.ByExit },
        _ => Array.Empty<BookTriggerConditionBased>()
    };

    private static IReadOnlyDictionary<BookTriggerConditionBased, string> BaseTriggerLabels(ObjectType type) => type switch
    {
        ObjectType.Tile => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByEnter] = "By Step On", [BookTriggerConditionBased.ByStay] = "By Stand On",
            [BookTriggerConditionBased.ByExit] = "By Step Off", [BookTriggerConditionBased.ByDropItem] = "By Item Dropped On It",
            [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)"
        },
        ObjectType.Trap => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByEnter] = "By Trap Triggered", [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)"
        },
        ObjectType.Door => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Open / Close", [BookTriggerConditionBased.ByAfterReadItem] = "By After Open Door",
            [BookTriggerConditionBased.ByEnter] = "By After Close Door", [BookTriggerConditionBased.ByStay] = "By After Unlock Door",
            [BookTriggerConditionBased.ByExit] = "By Failed Picklock Door", [BookTriggerConditionBased.ByDestroyItem] = "By Door Broken"
        },
        ObjectType.LightSource => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Light / Put Out", [BookTriggerConditionBased.ByAfterReadItem] = "By After Lit",
            [BookTriggerConditionBased.ByEnter] = "By After Put Out", [BookTriggerConditionBased.ByStay] = "By Burnt Out"
        },
        ObjectType.Chair => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)", [BookTriggerConditionBased.ByEnter] = "By Sit Down", [BookTriggerConditionBased.ByExit] = "By Get Up"
        },
        ObjectType.Bed => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)", [BookTriggerConditionBased.ByEnter] = "By Lie Down", [BookTriggerConditionBased.ByExit] = "By Get Up"
        },
        ObjectType.Lever => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Pull (double-click)", [BookTriggerConditionBased.ByAfterReadItem] = "By Turned On",
            [BookTriggerConditionBased.ByEnter] = "By Turned Off"
        },
        ObjectType.Plant => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)", [BookTriggerConditionBased.ByAfterReadItem] = "By Harvested",
            [BookTriggerConditionBased.ByStay] = "By Grown"
        },
        ObjectType.CraftingStation => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)", [BookTriggerConditionBased.ByAfterReadItem] = "By Made Something"
        },
        ObjectType.Explosive => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Lit (double-click)", [BookTriggerConditionBased.ByDestroyItem] = "By Exploded"
        },
        ObjectType.Window => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)", [BookTriggerConditionBased.ByDestroyItem] = "By Glass Broken"
        },
        ObjectType.BreakableGlass => new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)", [BookTriggerConditionBased.ByDestroyItem] = "By Shattered"
        },
        _ => new Dictionary<BookTriggerConditionBased, string> { [BookTriggerConditionBased.ByUseItem] = "By Use (double-click)" }
    };

    private static string TriggerHelp(ObjectType type) => type switch
    {
        ObjectType.Tile => "By Step On / Stand On / Step Off fire for whoever walks on this tile (player or NPC); By Item Dropped On It fires when something is put down on it.",
        ObjectType.Door => "By Open / Close fires before the door moves - a page with commands takes over (a talking door, a riddle). The After... triggers fire once it has happened.",
        ObjectType.LightSource => "By Light / Put Out fires on the double-click; a page with commands takes over (batteries, a puzzle lamp).",
        ObjectType.Junk => "By Use fires on the double-click - this is how one-off items get their behaviour.",
        ObjectType.Lever => "By Pull fires on the double-click (a page with commands takes over); By Turned On / Off once it has flipped.",
        ObjectType.Plant => "By Harvested fires once it's been harvested; By Grown each time it reaches its next stage.",
        ObjectType.CraftingStation => "By Made Something fires once its work is done.",
        ObjectType.Explosive => "By Lit fires when its fuse is lit; By Exploded when it goes off.",
        _ => "Evaluates this page's conditions against a simulated scenario, then lets you step through its commands."
    };

    // ================= Building blocks =================

    private StackPanel Group(string header)
    {
        var body = new StackPanel();
        _props.Children.Add(new GroupBox { Header = header, Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 2), Content = body });
        return body;
    }

    private static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 2, 0, 4), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 16, 6), HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = label });
        if (control.HorizontalAlignment == HorizontalAlignment.Stretch) control.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(control);
        return panel;
    }

    private static FrameworkElement TextField(string label, double width, Func<string> get, Action<string> set)
    {
        var box = new TextBox { Width = width, Text = get(), HorizontalAlignment = HorizontalAlignment.Left };
        box.LostFocus += (_, _) => set(box.Text.Trim());
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) set(box.Text.Trim()); };
        return Labeled(label, box);
    }

    private static FrameworkElement NumberField(string label, Func<double> get, Action<double> set, string? tip = null)
    {
        var box = new TextBox { Width = 70, Text = get().ToString("0.##"), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = tip };
        void Commit()
        {
            if (double.TryParse(box.Text.Trim(), out double v)) set(v);
            box.Text = get().ToString("0.##");
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        var labeled = Labeled(label, box);
        if (tip is not null) ((FrameworkElement)labeled).ToolTip = tip;
        return labeled;
    }

    private static CheckBox Check(string label, Func<bool> get, Action<bool> set, string? tip = null)
    {
        var check = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 4, 18, 4), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        check.Checked += (_, _) => set(true);
        check.Unchecked += (_, _) => set(false);
        return check;
    }

    private FrameworkElement SoundPicker(string label, Func<int> get, Action<int> set)
    {
        var sounds = new List<ResourceEntry> { NoSound };
        sounds.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var combo = new ComboBox { Width = 180, ItemsSource = sounds, DisplayMemberPath = "Name" };
        combo.SelectedItem = sounds.FirstOrDefault(s => s.Id == get()) ?? NoSound;
        combo.SelectionChanged += (_, _) => set((combo.SelectedItem as ResourceEntry)?.Id ?? -1);
        return Labeled(label, combo);
    }

    /// <summary>A sprite from the Sprite Editor (optionally only from one group), with a still preview and a play button.</summary>
    private FrameworkElement SpritePicker(string label, Func<(string Group, string Name)> get, Action<string, string> set, string? onlyGroup = null, bool compact = false,
        SpriteFolderIdentity identity = SpriteFolderIdentity.Object)
    {
        var choices = new List<SpriteChoice> { NoSprite };
        var kind = _current?.Type;
        // The folders given this object type (or any object) in the Sprite Editor; none given yet: every folder, as before.
        var folders = onlyGroup is null ? _sprites.FoldersFor(identity, kind) : _sprites.Groups.Where(g => g.Name == onlyGroup).ToList();
        foreach (var group in folders.Where(g => onlyGroup is not null || g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            choices.AddRange(group.Sprites.Select(s => new SpriteChoice(group.Name, s.Name, onlyGroup is null ? $"{s.Name}   ({group.Name})" : s.Name)));
        var (curGroup, curName) = get();
        var combo = new ComboBox { Width = compact ? 150 : 300, ItemsSource = choices, DisplayMemberPath = nameof(SpriteChoice.Display) };
        combo.SelectedItem = choices.FirstOrDefault(c => c.Name == curName && (c.Group == curGroup || string.IsNullOrEmpty(curGroup))) ?? NoSprite;
        var picture = new Image { Width = compact ? 40 : 56, Height = compact ? 40 : 56, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        var playHolder = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        void ShowPreview()
        {
            if (playHolder.Content is AnimationPreviewButton old) old.StopPlaying();
            playHolder.Content = null;
            picture.Source = null;
            if (combo.SelectedItem is SpriteChoice { Name.Length: > 0 } choice
                && _sprites.Groups.FirstOrDefault(g => g.Name == choice.Group)?.Sprites.FirstOrDefault(s => s.Name == choice.Name) is { } sprite)
                playHolder.Content = new AnimationPreviewButton(sprite, picture, _resources);
        }
        combo.SelectionChanged += (_, _) =>
        {
            var choice = combo.SelectedItem as SpriteChoice ?? NoSprite;
            set(choice.Group, choice.Name);
            ShowPreview();
        };
        ShowPreview();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(combo);
        if (onlyGroup is null)
        {
            var expand = new Button { Content = "Expand...", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0), ToolTip = "This type's folders and their sprites, with their pictures and a search box, to pick one." };
            expand.Click += (_, _) =>
            {
                var (g, n) = get();
                if (SpriteBrowserWindow.Pick(this, _sprites, identity, kind, g, n) is not { } picked) return;
                var choice = choices.FirstOrDefault(c => c.Group == picked.Group && c.Name == picked.Name);
                if (choice is null) { choice = new SpriteChoice(picked.Group, picked.Name, $"{picked.Name}   ({picked.Group})"); choices.Add(choice); combo.Items.Refresh(); }
                combo.SelectedItem = choice;
            };
            row.Children.Add(expand);
        }
        row.Children.Add(new Border { Background = Brushes.Black, Child = picture, Margin = new Thickness(6, 0, 0, 0), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) });
        row.Children.Add(playHolder);
        return Labeled(label, row);
    }
}
