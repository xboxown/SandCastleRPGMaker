using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Object Editor's panels for the types added in build 248: windows,
/// plants, keychains, sextants, maps, food, potions, glass, spellbooks,
/// useable objects, levers, stairs and ladders, roofs, clocks, tools,
/// crafting stations, instruments, explosives, bedrolls, reagents, tables
/// and liquid containers. What each does in play is in the GameTestWindow
/// partial files, ported from Exult's own item usecode where it has one.
/// </summary>
public partial class ObjectEditorWindow
{
    /// <summary>The type's own settings panel (types added in build 248). False if the type has none here.</summary>
    private bool BuildNewTypePanel(WorldObject obj)
    {
        switch (obj.Type)
        {
            case ObjectType.Window: BuildWindow(obj); return true;
            case ObjectType.Plant: BuildPlant(obj); return true;
            case ObjectType.Keychain: BuildKeychain(obj); return true;
            case ObjectType.Sextant: BuildSextant(obj); return true;
            case ObjectType.Map: BuildMap(obj); return true;
            case ObjectType.Food: BuildFood(obj); return true;
            case ObjectType.Potion: BuildPotion(obj); return true;
            case ObjectType.BreakableGlass: BuildGlass(obj); return true;
            case ObjectType.Spellbook: BuildSpellbook(obj); return true;
            case ObjectType.UseableObject: BuildUseable(obj); return true;
            case ObjectType.Lever: BuildLever(obj); return true;
            case ObjectType.Stairs: BuildStairs(obj); return true;
            case ObjectType.Roof: BuildRoof(obj); return true;
            case ObjectType.Clock: BuildClock(obj); return true;
            case ObjectType.Tool: BuildTool(obj); return true;
            case ObjectType.CraftingStation: BuildCrafting(obj); return true;
            case ObjectType.MusicalInstrument: BuildInstrument(obj); return true;
            case ObjectType.Explosive: BuildExplosive(obj); return true;
            case ObjectType.Bedroll: BuildBedroll(obj); return true;
            case ObjectType.Reagent: BuildReagent(obj); return true;
            case ObjectType.Table: BuildTable(obj); return true;
            case ObjectType.LiquidContainer: BuildLiquid(obj); return true;
        }
        return false;
    }

    /// <summary>Which types have hit points to lose (the Durability box).</summary>
    private static bool HasDurability(ObjectType type) => ObjectTypes.CanBeBroken(type) && type is ObjectType.Door or ObjectType.Junk or ObjectType.Chair
        or ObjectType.Bed or ObjectType.LightSource or ObjectType.Trap or ObjectType.Window or ObjectType.BreakableGlass
        or ObjectType.UseableObject or ObjectType.Lever or ObjectType.CraftingStation or ObjectType.MusicalInstrument
        or ObjectType.Explosive or ObjectType.Table or ObjectType.LiquidContainer or ObjectType.Clock or ObjectType.Tool or ObjectType.Bedroll;

    private static string NewTypeDescription(ObjectType type) => type switch
    {
        ObjectType.Window => "Solid like a wall (and drawn by the same Exult rule), but you can see through it. It can have shutters (double-click opens and closes them; closed, they block sight) and glass that breaks when struck - leaving a broken window if you give it a Broken sprite.",
        ObjectType.Plant => "Grows through its stage pictures as game hours pass. Ripe, double-click it (or use its tool on it, a scythe on wheat) to harvest its produce; it then goes back to a stage you choose - or is taken whole.",
        ObjectType.Keychain => "Exult's keyring. Double-click it, then click: yourself - every key the party carries goes onto it (\"Added 3 keys\"); a door or container - it opens whatever any of its keys fits (\"We don't have that key.\"). Its frames show how many keys it holds: 0, 1, 2, 3, 4 or more.",
        ObjectType.Sextant => "Double-click to read your position: \"34 South, 12 East\" (Exult's coordinates, 10 tiles a degree). Indoors, under a roof, it can't see the sky. With one in the party, maps show where you are.",
        ObjectType.Map => "Double-click to look at it: its picture fills the screen until you click. With a sextant in the party, outdoors, a cross marks where you are (Exult's display_map).",
        ObjectType.Food => "Solid food: double-click it, then click who eats it (Exult's food) - a party member, awake. At food 25 or more they say \"No, thank thee.\"; otherwise it's eaten and they say how they feel (\"Ah yes, much better.\"). Liquid food (milk, water, wine): what buckets and bottles hold - drunk from them.",
        ObjectType.Potion => "Double-click it, then click who drinks it: its effects (heal, cure poison, sleep, protection, invisibility...) go on them, and it's used up - leaving an empty bottle if you like.",
        ObjectType.BreakableGlass => "Glass, a vase, a bottle: one blow shatters it (its hit points), and so can dropping it from a height. It can leave shards behind. Owned glass broken in front of its owner's side is a crime.",
        ObjectType.Spellbook => "Double-click to open it: its spells by circle. Casting one needs mana (its circle, as Exult), Level at least its circle, and one of each of its reagents carried; targeted spells then ask you to click who or what.",
        ObjectType.UseableObject => "Double-click to use: its effects (on you, or on someone you click - a magic scroll) and its By Use events. It can have charges: used up, it's gone, or shows its Used-up sprite (a spent wand).",
        ObjectType.Lever => "Double-click to pull (a switch: flip; a button: press - it springs back). Each placed one works the things it's linked to in the Map Editor (right-click > Link): doors open and close, lights go on and off, explosives are lit, other levers flip, anything else gets its By Use event.",
        ObjectType.Stairs => "Stairs: walked up and down a step at a time - each step a lift higher, Exult's walking rule (make the footprint at least as long as the height). A ladder: double-click to climb to its destination (Map Editor, right-click > Ladder destination).",
        ObjectType.Roof => "Sits on top of a building at its height. While the player is under it, every roof (and anything up there) vanishes, so you can see inside - Exult's roofs (skip_above).",
        ObjectType.Clock => "Double-click to see the time over it, \" 3:05pm\" (Exult's pocketwatch). A sundial tells the time only by daylight, outdoors.",
        ObjectType.Tool => "Double-click it, then click what to use it on: a plant that needs this tool to harvest, a crafting station whose recipe needs it, or anything whose By Tool Used event wants it (Ultima VII's hammer on an anvil, scythe on wheat).",
        ObjectType.CraftingStation => "An anvil, a millstone, a spinning wheel, an oven. Double-click it (or use a tool on it) and it makes the first recipe the party has the ingredients for: they're used up, the work plays out, and what it makes appears on the station (Exult's millstone).",
        ObjectType.MusicalInstrument => "Double-click to play: one of its tunes plays and notes float up. Carried ones (a lute) can be played from the backpack.",
        ObjectType.Explosive => "A powder keg. Lit (double-click), it blows up when its fuse burns down; struck, at once. Everyone within its reach is hurt, breakable things are struck, and other explosives go off too.",
        ObjectType.Bedroll => "Double-click to sleep: the party rests for the night (Exult's advanceTimeDueToRest) - time passes, health and mana come back, ailments wear off, everyone gets hungrier, lights burn down. Not with enemies about.",
        ObjectType.Reagent => "Spell ingredients: stack up to 100. A spell uses one of each of its reagents (Exult).",
        ObjectType.Table => "Solid: you can't walk through it, but things put on it rest on top, at its height - food on a table, a book on a shelf, an altar's offering. You can reach over it to take things.",
        ObjectType.LiquidContainer => "Exult's bucket. It holds the liquids on its list (liquid Food objects), each with its own look. Double-click it, then click: a well or trough - it fills; a cow - it's milked; yourself - you drink it (\"Ahhh, how refreshing.\"); someone else - they're doused (\"Hey, stop that!\"); a lit light - water puts it out; the floor - it's spilt.",
        _ => ""
    };

    // ================= Windows =================

    private void BuildWindow(WorldObject obj)
    {
        var w = obj.Window;
        var body = Group("Window");
        body.Children.Add(Check("Has shutters (double-click opens and closes them)", () => w.HasShutters, v => { w.HasShutters = v; ShowCurrent(); }));
        if (w.HasShutters)
        {
            body.Children.Add(SpritePicker("Open sprite", () => (w.OpenSprite.SpriteGroup, w.OpenSprite.SpriteName), (g, n) => { w.OpenSprite.SpriteGroup = g; w.OpenSprite.SpriteName = n; }));
            var row = new WrapPanel();
            row.Children.Add(Check("Starts open", () => w.StartsOpen, v => w.StartsOpen = v));
            row.Children.Add(Check("Closed shutters block sight", () => w.ClosedBlocksSight, v => w.ClosedBlocksSight = v));
            body.Children.Add(row);
            var sounds = new WrapPanel();
            sounds.Children.Add(SoundPicker("Open sound", () => w.OpenSoundResourceId, v => w.OpenSoundResourceId = v));
            sounds.Children.Add(SoundPicker("Close sound", () => w.CloseSoundResourceId, v => w.CloseSoundResourceId = v));
            body.Children.Add(sounds);
        }
        body.Children.Add(SpritePicker("Broken sprite (optional - its look once the glass is broken)", () => (w.BrokenSprite.SpriteGroup, w.BrokenSprite.SpriteName), (g, n) => { w.BrokenSprite.SpriteGroup = g; w.BrokenSprite.SpriteName = n; }));
        body.Children.Add(Note("Give it hit points (Durability, below) to make its glass breakable; its Destroyed sound is the glass shattering. Broken, it shows the Broken sprite and stays (still solid); without one it's gone."));
    }

    // ================= Plants =================

    private void BuildPlant(WorldObject obj)
    {
        var p = obj.Plant;
        var body = Group("Plant: growth and harvest");
        body.Children.Add(Note("Its stages, first (just planted) to last (ripe). An empty stage shows the object's own sprite. Each placed plant can start at any stage (Map Editor, right-click)."));
        var stages = new StackPanel();
        void ShowStages()
        {
            AnimationPreviewButton.StopAll();
            stages.Children.Clear();
            for (int i = 0; i < p.Stages.Count; i++)
            {
                var link = p.Stages[i];
                int index = i;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(SpritePicker(i == p.Stages.Count - 1 ? $"Stage {i + 1} (ripe)" : $"Stage {i + 1}", () => (link.SpriteGroup, link.SpriteName), (g, n) => { link.SpriteGroup = g; link.SpriteName = n; }));
                var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8), IsEnabled = p.Stages.Count > 1 };
                remove.Click += (_, _) => { p.Stages.RemoveAt(index); ShowStages(); };
                row.Children.Add(remove);
                stages.Children.Add(row);
            }
            var add = new Button { Content = "Add stage", Padding = new Thickness(8, 1, 8, 1), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) => { p.Stages.Add(new SpriteLink()); ShowStages(); };
            stages.Children.Add(add);
        }
        ShowStages();
        body.Children.Add(stages);
        var growth = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        growth.Children.Add(NumberField("Game hours per stage (0 = never grows)", () => p.HoursPerStage, v => p.HoursPerStage = Math.Max(0, v)));
        body.Children.Add(growth);
        var harvest = new WrapPanel();
        harvest.Children.Add(ObjectPicker("Produce (what harvesting gives)", o => o.Type != ObjectType.Plant, () => p.ProduceObjectId, v => p.ProduceObjectId = v, "(nothing - a decoration)"));
        harvest.Children.Add(NumberField("How many", () => p.ProduceQuantity, v => p.ProduceQuantity = (int)Math.Clamp(v, 1, ObjectTypes.MaxQuantity)));
        harvest.Children.Add(ToolKindPicker("Needs tool to harvest", () => p.HarvestToolKind, v => p.HarvestToolKind = v, "(none - by hand, double-click)"));
        body.Children.Add(harvest);
        var after = new WrapPanel();
        after.Children.Add(Check("Only when ripe (the last stage)", () => p.OnlyWhenRipe, v => p.OnlyWhenRipe = v));
        after.Children.Add(Check("Harvesting takes the whole plant", () => p.RemovedWhenHarvested, v => p.RemovedWhenHarvested = v));
        after.Children.Add(NumberField("Then back to stage", () => p.StageAfterHarvest + 1, v => p.StageAfterHarvest = (int)Math.Max(0, v - 1)));
        after.Children.Add(SoundPicker("Harvest sound", () => p.HarvestSoundResourceId, v => p.HarvestSoundResourceId = v));
        body.Children.Add(after);
    }

    // ================= Keychains =================

    private void BuildKeychain(WorldObject obj)
    {
        var k = obj.Keychain;
        var body = Group("Keychain (Exult's keyring)");
        body.Children.Add(ObjectIdList("Keys it starts with (add as many as you like; click one to take it off)", k.StartingKeyIds, o => o.Type == ObjectType.Key));
        body.Children.Add(SoundPicker("Add key sound", () => k.AddKeySoundResourceId, v => k.AddKeySoundResourceId = v));
        body.Children.Add(Note("In play: double-click it, then click yourself - every key the party carries goes onto it - or click a door or chest to open it with whichever of its keys fits."));
    }

    // ================= Sextants and maps =================

    private void BuildSextant(WorldObject obj)
    {
        var s = obj.Sextant;
        var body = Group("Sextant");
        var row = new WrapPanel();
        row.Children.Add(NumberField("Origin cell X (-1 = map middle)", () => s.OriginCellX, v => s.OriginCellX = (int)Math.Max(-1, v)));
        row.Children.Add(NumberField("Origin cell Y (-1 = map middle)", () => s.OriginCellY, v => s.OriginCellY = (int)Math.Max(-1, v)));
        row.Children.Add(NumberField("Tiles per degree (Exult: 10)", () => s.TilesPerDegree, v => s.TilesPerDegree = (int)Math.Max(1, v)));
        body.Children.Add(row);
        body.Children.Add(Note("Exult's coordinates (cheat_screen.cc): (tile - origin) / 10 of its 8-pixel tiles, \"North\"/\"South\" and \"West\"/\"East\". Ultima VII's origin is Lord British's castle."));
    }

    private void BuildMap(WorldObject obj)
    {
        var m = obj.MapPicture;
        var body = Group("Map");
        body.Children.Add(SpritePicker("Map picture (shown when it's used)", () => (m.Picture.SpriteGroup, m.Picture.SpriteName), (g, n) => { m.Picture.SpriteGroup = g; m.Picture.SpriteName = n; }));
        body.Children.Add(SpritePicker("Paperdoll (optional - the parchment or frame the map is shown in)", () => (m.PaperdollSprite.SpriteGroup, m.PaperdollSprite.SpriteName), (g, n) => { m.PaperdollSprite.SpriteGroup = g; m.PaperdollSprite.SpriteName = n; }));
        var inset = new WrapPanel();
        inset.Children.Add(NumberField("Map inside it: left (px)", () => m.PictureLeft, v => m.PictureLeft = (int)Math.Max(0, v)));
        inset.Children.Add(NumberField("top", () => m.PictureTop, v => m.PictureTop = (int)Math.Max(0, v)));
        inset.Children.Add(NumberField("width (0 = automatic)", () => m.PictureWidth, v => m.PictureWidth = (int)Math.Max(0, v)));
        inset.Children.Add(NumberField("height", () => m.PictureHeight, v => m.PictureHeight = (int)Math.Max(0, v)));
        inset.Children.Add(SoundPicker("Open sound", () => m.OpenSoundResourceId, v => m.OpenSoundResourceId = v));
        body.Children.Add(inset);
        body.Children.Add(Note("With a paperdoll, the map picture is drawn inside it at the place above, in the paperdoll's own pixels (all 0: a 10% border all round). Without one, the map picture alone fills the screen."));
        var row = new WrapPanel();
        row.Children.Add(Check("Mark where the party is", () => m.ShowLocation, v => m.ShowLocation = v));
        row.Children.Add(Check("...only with a sextant in the party (Exult)", () => m.LocationNeedsSextant, v => m.LocationNeedsSextant = v));
        row.Children.Add(Check("...only outdoors (Exult: none inside a house or a roofed cave)", () => m.OutdoorsOnly, v => m.OutdoorsOnly = v));
        body.Children.Add(row);
        var area = new WrapPanel();
        area.Children.Add(Check("Shows only part of the map (a cave, a maze, a house)", () => m.AreaLeft >= 0, v =>
        {
            if (v) { m.AreaLeft = 0; m.AreaTop = 0; m.AreaRight = 19; m.AreaBottom = 19; }
            else m.AreaLeft = m.AreaTop = m.AreaRight = m.AreaBottom = -1;
            ShowCurrent();
        }));
        body.Children.Add(area);
        if (m.AreaLeft >= 0)
        {
            var cells = new WrapPanel();
            cells.Children.Add(NumberField("From cell X", () => m.AreaLeft, v => m.AreaLeft = (int)Math.Max(0, v)));
            cells.Children.Add(NumberField("Y", () => m.AreaTop, v => m.AreaTop = (int)Math.Max(0, v)));
            cells.Children.Add(NumberField("To cell X", () => m.AreaRight, v => m.AreaRight = (int)Math.Max(0, v)));
            cells.Children.Add(NumberField("Y", () => m.AreaBottom, v => m.AreaBottom = (int)Math.Max(0, v)));
            body.Children.Add(cells);
        }
        body.Children.Add(Note("The picture stands for the whole game map (or just the area above), corner to corner: the mark is placed by where the party is on it. Outside the area there's no mark: \"You can't find where you are on this map.\" " +
                               "To let only someone in particular use it, give it a By Use page in its Event tab with that condition: a page with commands takes over the use."));
    }

    // ================= Food, potions, glass =================

    private void BuildFood(WorldObject obj)
    {
        var f = obj.Food;
        var body = Group("Food");
        var kind = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var solid = new RadioButton { Content = "Solid (eaten: bread, an apple, cheese)", GroupName = "FoodKind" + obj.Id, IsChecked = !f.IsLiquid, Margin = new Thickness(0, 0, 16, 0) };
        var liquid = new RadioButton { Content = "Liquid (drunk: milk, water, wine - held in buckets and bottles)", GroupName = "FoodKind" + obj.Id, IsChecked = f.IsLiquid };
        solid.Checked += (_, _) => { if (f.IsLiquid) { f.IsLiquid = false; ShowCurrent(); } };
        liquid.Checked += (_, _) => { if (!f.IsLiquid) { f.IsLiquid = true; ShowCurrent(); } };
        kind.Children.Add(solid);
        kind.Children.Add(liquid);
        body.Children.Add(kind);
        var row = new WrapPanel();
        row.Children.Add(NumberField(f.IsLiquid ? "Nutrition when drunk" : "Nutrition (Exult: apple 2, bread 4, ham 24)", () => f.Nutrition, v => f.Nutrition = (int)Math.Clamp(v, 0, SurvivalSettings.MaxFood)));
        row.Children.Add(SoundPicker(f.IsLiquid ? "Drink sound" : "Eat sound", () => f.EatSoundResourceId, v => f.EatSoundResourceId = v));
        if (!f.IsLiquid) row.Children.Add(Check("Stackable (garlic, berries)", () => obj.IsStackable, v => obj.IsStackable = v));
        body.Children.Add(row);
        if (f.IsLiquid)
        {
            var flags = new WrapPanel();
            flags.Children.Add(Check("Puts out fires (water)", () => f.PutsOutFires, v => f.PutsOutFires = v));
            flags.Children.Add(Check("Undrinkable (blood: \"No, thank thee.\")", () => f.Undrinkable, v => f.Undrinkable = v));
            body.Children.Add(flags);
            body.Children.Add(Note("A liquid is what buckets and bottles hold. Here you say what it does; each Liquid Container says how it looks full of it. Wells, troughs and animals (a cow) are set to give it."));
            body.Children.Add(LiquidsHelpButton());
        }
        body.Children.Add(EffectsEditor(f.IsLiquid ? "Drinking it also does (optional - wine, a poisoned well)" : "Also does (optional - poisoned food, a healing herb)", f.Effects));
    }

    private void BuildPotion(WorldObject obj)
    {
        var p = obj.Potion;
        var body = Group("Potion");
        body.Children.Add(EffectsEditor("What it does to whoever drinks it", p.Effects));
        var row = new WrapPanel();
        row.Children.Add(SoundPicker("Drink sound", () => p.DrinkSoundResourceId, v => p.DrinkSoundResourceId = v));
        row.Children.Add(ObjectPicker("Leaves behind", o => o.Id != obj.Id, () => p.EmptyObjectId, v => p.EmptyObjectId = v, "(nothing)"));
        body.Children.Add(row);
    }

    private void BuildGlass(WorldObject obj)
    {
        var g = obj.Glass;
        var body = Group("Breakable glass");
        var row = new WrapPanel();
        row.Children.Add(NumberField("Shatters when dropped from (lifts; 0 = never)", () => g.BreaksFromLifts, v => g.BreaksFromLifts = (int)Math.Max(0, v)));
        row.Children.Add(ObjectPicker("Leaves behind", o => o.Id != obj.Id, () => g.ShardsObjectId, v => g.ShardsObjectId = v, "(nothing)"));
        body.Children.Add(row);
        body.Children.Add(Note("Its hit points (Durability, below - 1 by default) decide how many blows it takes; its Destroyed sound is the shatter. Dropped onto the floor from a table or a shelf this many lifts up, it shatters too."));
    }

    // ================= Spellbooks =================

    private void BuildSpellbook(WorldObject obj)
    {
        var book = obj.Spellbook;
        var body = Group("Spells");
        body.Children.Add(Note("Exult's rules: casting costs mana equal to the spell's circle (unless you set a cost), needs the caster's Level to be at least its circle, and uses up one of each of its reagents (Reagent objects carried). Linear spells (circle 0) cost nothing."));
        var list = new StackPanel();
        void Show()
        {
            list.Children.Clear();
            foreach (var spell in book.Spells.OrderBy(s => s.Circle).ToList())
            {
                var box = new GroupBox { Header = $"{CircleName(spell.Circle)}: {spell.Name}", Padding = new Thickness(6), Margin = new Thickness(0, 4, 0, 4) };
                var inner = new StackPanel();
                var top = new WrapPanel();
                top.Children.Add(TextField("Name", 160, () => spell.Name, v => { spell.Name = v.Length > 0 ? v : spell.Name; box.Header = $"{CircleName(spell.Circle)}: {spell.Name}"; }));
                var circle = new ComboBox { Width = 110, ItemsSource = Enumerable.Range(0, 9).Select(CircleName).ToList(), SelectedIndex = Math.Clamp(spell.Circle, 0, 8) };
                circle.SelectionChanged += (_, _) => { spell.Circle = Math.Max(0, circle.SelectedIndex); box.Header = $"{CircleName(spell.Circle)}: {spell.Name}"; };
                top.Children.Add(Labeled("Circle", circle));
                top.Children.Add(NumberField("Mana (-1 = its circle)", () => spell.ManaCost, v => spell.ManaCost = (int)Math.Max(-1, v)));
                top.Children.Add(TextField("Words of power", 120, () => spell.Words, v => spell.Words = v));
                top.Children.Add(Check("Click a target", () => spell.NeedsTarget, v => spell.NeedsTarget = v));
                inner.Children.Add(top);
                inner.Children.Add(ObjectIdList("Reagents (one of each used up)", spell.ReagentObjectIds, o => o.Type == ObjectType.Reagent));
                inner.Children.Add(EffectsEditor("Effects", spell.Effects));
                var bottom = new WrapPanel();
                bottom.Children.Add(SoundPicker("Cast sound", () => spell.CastSoundResourceId, v => spell.CastSoundResourceId = v));
                var remove = new Button { Content = "Remove spell", Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
                remove.Click += (_, _) => { book.Spells.Remove(spell); Show(); };
                bottom.Children.Add(remove);
                inner.Children.Add(bottom);
                box.Content = inner;
                list.Children.Add(box);
            }
            var add = new Button { Content = "Add spell", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) => { book.Spells.Add(new SpellDefinition { Effects = { new MagicEffect { Kind = MagicEffectKind.Heal, Min = 5, Max = 10 } } }); Show(); };
            list.Children.Add(add);
        }
        Show();
        body.Children.Add(list);
    }

    private static string CircleName(int circle) => circle switch
    {
        0 => "Linear", 1 => "First circle", 2 => "Second circle", 3 => "Third circle", 4 => "Fourth circle",
        5 => "Fifth circle", 6 => "Sixth circle", 7 => "Seventh circle", _ => "Eighth circle"
    };

    // ================= Useable objects =================

    private void BuildUseable(WorldObject obj)
    {
        var u = obj.Useable;
        var body = Group("Useable object");
        var row = new WrapPanel();
        row.Children.Add(NumberField("Charges (0 = no limit)", () => u.Charges, v => u.Charges = (int)Math.Max(0, v)));
        row.Children.Add(Check("Gone when used up (a scroll)", () => u.RemovedWhenUsedUp, v => u.RemovedWhenUsedUp = v));
        row.Children.Add(Check("Its effects go on a target you click", () => u.AsksForTarget, v => u.AsksForTarget = v));
        row.Children.Add(SoundPicker("Use sound", () => u.UseSoundResourceId, v => u.UseSoundResourceId = v));
        body.Children.Add(row);
        body.Children.Add(SpritePicker("Used-up sprite (when it stays - a spent wand)", () => (u.UsedUpSprite.SpriteGroup, u.UsedUpSprite.SpriteName), (g, n) => { u.UsedUpSprite.SpriteGroup = g; u.UsedUpSprite.SpriteName = n; }));
        body.Children.Add(EffectsEditor("Effects (optional - its events can do the rest)", u.Effects));
        body.Children.Add(Note("Each use: its effects, then its By Use event (Event tab), and one charge is used. Used up, it's \"Used ...\" and does nothing more."));
    }

    // ================= Levers =================

    private void BuildLever(WorldObject obj)
    {
        var l = obj.Lever;
        var body = Group("Lever / switch / button");
        var row = new WrapPanel();
        var kind = new ComboBox { Width = 110, ItemsSource = Enum.GetValues<LeverKind>(), SelectedItem = l.Kind };
        kind.SelectionChanged += (_, _) => { l.Kind = (LeverKind)kind.SelectedItem; ShowCurrent(); };
        row.Children.Add(Labeled("Kind", kind));
        if (l.Kind != LeverKind.Button) row.Children.Add(Check("Starts on", () => l.StartsOn, v => l.StartsOn = v));
        else row.Children.Add(NumberField("Springs back after (seconds)", () => l.ButtonResetSeconds, v => l.ButtonResetSeconds = Math.Max(0.1, v)));
        body.Children.Add(row);
        body.Children.Add(SpritePicker(l.Kind == LeverKind.Button ? "Pressed sprite" : "On sprite", () => (l.OnSprite.SpriteGroup, l.OnSprite.SpriteName), (g, n) => { l.OnSprite.SpriteGroup = g; l.OnSprite.SpriteName = n; }));
        var sounds = new WrapPanel();
        sounds.Children.Add(SoundPicker(l.Kind == LeverKind.Button ? "Press sound" : "On sound", () => l.OnSoundResourceId, v => l.OnSoundResourceId = v));
        sounds.Children.Add(SoundPicker(l.Kind == LeverKind.Button ? "Spring back sound" : "Off sound", () => l.OffSoundResourceId, v => l.OffSoundResourceId = v));
        body.Children.Add(sounds);
        body.Children.Add(Note("What it works is set on each placed one: Map Editor, right-click it > Link to..., then click the doors, lights, explosives, traps or other objects. Each pull toggles them (a door opens or closes - even a locked one, as a portcullis winch does)."));
    }

    // ================= Stairs and ladders =================

    private void BuildStairs(WorldObject obj)
    {
        var s = obj.Stairs;
        var body = Group("Stairs / ladder");
        var row = new WrapPanel();
        var kind = new ComboBox { Width = 110, ItemsSource = Enum.GetValues<StairsKind>(), SelectedItem = s.Kind };
        kind.SelectionChanged += (_, _) => { s.Kind = (StairsKind)kind.SelectedItem; obj.IsSolid = s.Kind == StairsKind.Stairs; ShowCurrent(); };
        row.Children.Add(Labeled("Kind", kind));
        if (s.Kind == StairsKind.Stairs && s.DesignId < 0)
        {
            var dirs = new[] { SpriteDirection.North, SpriteDirection.East, SpriteDirection.South, SpriteDirection.West };
            var toward = new ComboBox { Width = 90, ItemsSource = dirs, SelectedItem = dirs.Contains(s.RisesToward) ? s.RisesToward : SpriteDirection.North };
            toward.SelectionChanged += (_, _) => s.RisesToward = (SpriteDirection)toward.SelectedItem;
            row.Children.Add(Labeled("Goes up toward", toward));
        }
        row.Children.Add(SoundPicker(s.Kind == StairsKind.Ladder ? "Climb sound" : "Step sound", () => s.ClimbSoundResourceId, v => s.ClimbSoundResourceId = v));
        body.Children.Add(row);
        if (s.Kind == StairsKind.Stairs) body.Children.Add(StairDesignPicker(obj));
        body.Children.Add(Note(s.Kind == StairsKind.Stairs
            ? s.DesignId >= 0
                ? "Built from its staircase design: placed on the map as one object, but in play every step is its own piece to stand on, climbed a lift at a time (Exult's walking rule). Its Size and collision below aren't used for walking. Each placed copy picks its pose in the Map Editor (right-click > Facing)."
                : "Its footprint (Size and collision, below) is cut into steps along the way it goes up: its height (Size Z) in lifts, one lift a step, so walkers climb it by Exult's rule (a lift at a time). Make the footprint at least as many tiles long as it is high - 4 lifts high, 4 long. Or build it step by step: Design, above."
            : "A ladder isn't walked on: double-click it and the player climbs to its destination - set on each placed one in the Map Editor (right-click > Ladder destination...)."));
    }

    /// <summary>Stairs: the Staircase Designer design they're built from, a button to open the designer, and the whole staircase as a preview.</summary>
    private FrameworkElement StairDesignPicker(WorldObject obj)
    {
        var s = obj.Stairs;
        var none = new StairDesign { Id = -1, Name = "(none - its own sprite, cut into steps)" };
        var choices = new List<StairDesign> { none };
        choices.AddRange(_db.StairDesigns);
        var combo = new ComboBox { Width = 260, ItemsSource = choices, DisplayMemberPath = nameof(StairDesign.Name) };
        combo.SelectedItem = choices.FirstOrDefault(d => d.Id == s.DesignId) ?? none;
        combo.SelectionChanged += (_, _) => { s.DesignId = (combo.SelectedItem as StairDesign)?.Id ?? -1; ShowCurrent(); };
        var open = new Button { Content = "Staircase Designer...", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Bottom };
        open.Click += (_, _) =>
        {
            var designer = new StaircaseDesignerWindow(_db, _sprites, _resources, TileWidth, TileHeight, s.DesignId) { Owner = this };
            designer.ShowDialog();
            // Opened from a staircase with no design yet: it takes the one left showing.
            if (s.DesignId < 0 || _db.StairDesigns.All(d => d.Id != s.DesignId)) s.DesignId = designer.SelectedDesignId;
            ShowCurrent();
        };
        var row = new WrapPanel();
        row.Children.Add(Labeled("Design (Staircase Designer)", combo));
        row.Children.Add(open);
        var panel = new StackPanel();
        panel.Children.Add(row);
        if (StairLook.DesignOf(obj, _db) is { } design && design.Resolve(SpriteDirection.North) is { } pose && StairLook.Bitmap(pose, _sprites, TileWidth, TileHeight) is { } picture)
        {
            var image = new Image { Source = picture, Stretch = Stretch.None, LayoutTransform = new ScaleTransform(2, 2) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            panel.Children.Add(new Border { Background = Brushes.Black, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, Child = image });
            panel.Children.Add(Note($"\"{design.Name}\": {pose.Steps.Count} steps, the top one {pose.Steps.Max(st => st.TopLift)} lifts up. Poses: {string.Join(", ", design.Poses.Where(p => p.Steps.Count > 0).Select(p => p.Facing))}."));
        }
        return panel;
    }

    // ================= Roofs, clocks, tools =================

    private void BuildRoof(WorldObject obj)
    {
        var r = obj.Roof;
        var body = Group("Roof");
        body.Children.Add(NumberField("Height (lifts; Exult: a storey is 5)", () => r.HeightLifts, v => r.HeightLifts = (int)Math.Max(1, v)));
        body.Children.Add(Note("It sits this many lifts up, over the building. When the player walks in underneath it, it (and every roof at its height or above, and whatever is up there) disappears until they come out - Exult's roofs. Stairs can lead up onto it."));
    }

    private void BuildClock(WorldObject obj)
    {
        var body = Group("Clock");
        body.Children.Add(Check("A sundial (tells the time only by daylight, outdoors)", () => obj.Clock.Sundial, v => obj.Clock.Sundial = v));
    }

    private void BuildTool(WorldObject obj)
    {
        var t = obj.Tool;
        var body = Group("Tool");
        var row = new WrapPanel();
        row.Children.Add(TextField("Kind (Hammer, Scythe, Shears, Saw...)", 160, () => t.ToolKind, v => t.ToolKind = v));
        row.Children.Add(SoundPicker("Use sound", () => t.UseSoundResourceId, v => t.UseSoundResourceId = v));
        body.Children.Add(row);
        body.Children.Add(Note("Plants and crafting recipes name the kind of tool they need. Used on anything else, that thing's By Tool Used event fires (Event tab of the object it's used on)."));
    }

    // ================= Crafting stations =================

    private void BuildCrafting(WorldObject obj)
    {
        var c = obj.Crafting;
        var body = Group("Crafting station: recipes");
        body.Children.Add(SpritePicker("Working sprite (optional - an animation while it works)", () => (c.WorkingSprite.SpriteGroup, c.WorkingSprite.SpriteName), (g, n) => { c.WorkingSprite.SpriteGroup = g; c.WorkingSprite.SpriteName = n; }));
        body.Children.Add(TextField("Hint, when there's nothing to make (Exult: \"Try grinding wheat upon this millstone.\")", 520, () => c.Hint, v => c.Hint = v));
        var list = new StackPanel();
        void Show()
        {
            list.Children.Clear();
            foreach (var recipe in c.Recipes.ToList())
            {
                var box = new GroupBox { Header = recipe.Name, Padding = new Thickness(6), Margin = new Thickness(0, 4, 0, 4) };
                var inner = new StackPanel();
                var top = new WrapPanel();
                top.Children.Add(TextField("Name", 160, () => recipe.Name, v => { recipe.Name = v.Length > 0 ? v : recipe.Name; box.Header = recipe.Name; }));
                top.Children.Add(ToolKindPicker("Needs tool", () => recipe.ToolKind, v => recipe.ToolKind = v, "(none - double-click)"));
                top.Children.Add(NumberField("Work (seconds)", () => recipe.WorkSeconds, v => recipe.WorkSeconds = Math.Max(0, v)));
                top.Children.Add(SoundPicker("Work sound", () => recipe.WorkSoundResourceId, v => recipe.WorkSoundResourceId = v));
                inner.Children.Add(top);
                inner.Children.Add(RecipeInputs(recipe.Inputs));
                var output = new WrapPanel();
                output.Children.Add(ObjectPicker("Makes", _ => true, () => recipe.OutputObjectId, v => recipe.OutputObjectId = v, "(nothing)"));
                output.Children.Add(NumberField("How many", () => recipe.OutputQuantity, v => recipe.OutputQuantity = (int)Math.Clamp(v, 1, ObjectTypes.MaxQuantity)));
                var remove = new Button { Content = "Remove recipe", Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
                remove.Click += (_, _) => { c.Recipes.Remove(recipe); Show(); };
                output.Children.Add(remove);
                inner.Children.Add(output);
                box.Content = inner;
                list.Children.Add(box);
            }
            var add = new Button { Content = "Add recipe", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) => { c.Recipes.Add(new CraftingRecipe()); Show(); };
            list.Children.Add(add);
        }
        Show();
        body.Children.Add(list);
        body.Children.Add(Note("Ingredients come from what the party carries. The work plays out (the sound, the working sprite, the player at work), then what it makes appears on the station - or beside it - as Exult's millstone makes flour."));
    }

    private FrameworkElement RecipeInputs(List<RecipeInput> inputs)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        panel.Children.Add(new TextBlock { Text = "Ingredients (used up)" });
        void Show()
        {
            while (panel.Children.Count > 1) panel.Children.RemoveAt(1);
            foreach (var input in inputs.ToList())
            {
                var row = new WrapPanel();
                row.Children.Add(ObjectPicker("", _ => true, () => input.ObjectId, v => input.ObjectId = v, "(choose)"));
                row.Children.Add(NumberField("How many", () => input.Quantity, v => input.Quantity = (int)Math.Clamp(v, 1, ObjectTypes.MaxQuantity)));
                var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
                remove.Click += (_, _) => { inputs.Remove(input); Show(); };
                row.Children.Add(remove);
                panel.Children.Add(row);
            }
            var add = new Button { Content = "Add ingredient", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) => { inputs.Add(new RecipeInput()); Show(); };
            panel.Children.Add(add);
        }
        Show();
        return panel;
    }

    // ================= Instruments, explosives, bedrolls =================

    private void BuildInstrument(WorldObject obj)
    {
        var m = obj.Instrument;
        var body = Group("Musical instrument");
        body.Children.Add(SoundList("Tunes (one is played each time)", m.SongSoundIds));
        var poses = new List<string> { "(as they stand)" };
        poses.AddRange(ProjectSession.Poses.Poses.Select(p => p.Name).Distinct());
        var pose = new ComboBox { Width = 180, ItemsSource = poses, SelectedItem = poses.Contains(m.PlayingPose) ? m.PlayingPose : poses[0] };
        pose.SelectionChanged += (_, _) => m.PlayingPose = pose.SelectedIndex <= 0 ? string.Empty : (string)pose.SelectedItem;
        body.Children.Add(Labeled("Player's pose while playing (a Character Editor pose)", pose));
    }

    private void BuildExplosive(WorldObject obj)
    {
        var e = obj.Explosive;
        var body = Group("Explosive");
        var flags = new WrapPanel();
        flags.Children.Add(Check("Lit by double-clicking", () => e.LitByDoubleClick, v => e.LitByDoubleClick = v));
        flags.Children.Add(Check("Goes off when struck", () => e.ExplodesWhenStruck, v => e.ExplodesWhenStruck = v));
        flags.Children.Add(NumberField("Fuse (seconds)", () => e.FuseSeconds, v => e.FuseSeconds = Math.Max(0, v)));
        body.Children.Add(flags);
        var blast = new WrapPanel();
        blast.Children.Add(NumberField("Reach (tiles of 8 px)", () => e.RadiusTiles, v => e.RadiusTiles = (int)Math.Max(1, v)));
        blast.Children.Add(NumberField("Damage from", () => e.DamageMin, v => e.DamageMin = (int)Math.Max(0, v)));
        blast.Children.Add(NumberField("to", () => e.DamageMax, v => e.DamageMax = (int)Math.Max(0, v)));
        body.Children.Add(blast);
        body.Children.Add(SpritePicker("Lit sprite (fuse burning)", () => (e.LitSprite.SpriteGroup, e.LitSprite.SpriteName), (g, n) => { e.LitSprite.SpriteGroup = g; e.LitSprite.SpriteName = n; }));
        body.Children.Add(SpritePicker("Explosion sprite (played once)", () => (e.ExplosionSprite.SpriteGroup, e.ExplosionSprite.SpriteName), (g, n) => { e.ExplosionSprite.SpriteGroup = g; e.ExplosionSprite.SpriteName = n; }));
        var sounds = new WrapPanel();
        sounds.Children.Add(SoundPicker("Fuse sound", () => e.FuseSoundResourceId, v => e.FuseSoundResourceId = v));
        sounds.Children.Add(SoundPicker("Explosion sound", () => e.ExplosionSoundResourceId, v => e.ExplosionSoundResourceId = v));
        body.Children.Add(sounds);
        body.Children.Add(Note("The blast hurts everyone in reach (the player and the party too), strikes breakable things in reach for the same damage, and sets off other explosives. Blowing up what belongs to someone, in sight of their side, is a crime."));
    }

    private void BuildBedroll(WorldObject obj)
    {
        var b = obj.Bedroll;
        var body = Group("Bedroll");
        var row = new WrapPanel();
        row.Children.Add(NumberField("Sleep (game hours)", () => b.SleepHours, v => b.SleepHours = (int)Math.Clamp(v, 1, 24)));
        row.Children.Add(Check("Sleep until 6 in the morning instead", () => b.WakeAtDawn, v => b.WakeAtDawn = v));
        body.Children.Add(row);
        body.Children.Add(SpritePicker("Laid-out sprite (optional - while slept on)", () => (b.LaidOutSprite.SpriteGroup, b.LaidOutSprite.SpriteName), (g, n) => { b.LaidOutSprite.SpriteGroup = g; b.LaidOutSprite.SpriteName = n; }));
        body.Children.Add(Note("Exult's rest (advanceTimeDueToRest): for each party member not starving (food 10 or more): ailments wear off, health and mana come back by the hours slept, and food goes down by them. Lights burn down. Can't be used with enemies in sight."));
    }

    private void BuildReagent(WorldObject obj)
    {
        var body = Group("Reagent");
        body.Children.Add(Note("Stacks of up to 100 (set how many on each placed one: Map Editor, right-click > Quantity). Spellbook spells list the reagents they need."));
    }

    private void BuildTable(WorldObject obj)
    {
        var body = Group("Table / surface");
        body.Children.Add(Note("Its height (Size Z, below) is where things put on it rest. It's solid, so nobody walks through it - but you can reach over it to take what's on it, or what's behind it within reach (Exult's is_grabable)."));
    }

    // ================= Liquid containers =================

    private void BuildLiquid(WorldObject obj)
    {
        var l = obj.Liquid;
        var body = Group("Liquid container (Exult's bucket)");
        body.Children.Add(Note("Its own sprite (Look, above) is how it looks empty. Below: the liquids it can hold - each a Food object set to Liquid - and how THIS container looks full of each. A liquid not on the list can't go in it."));
        body.Children.Add(LiquidsHelpButton());
        var liquids = LiquidFoods();
        if (liquids.Count == 0)
            body.Children.Add(new TextBlock { Text = "No liquids yet: make a Food object and set it to Liquid (Milk, Water, Wine...), then add it here.", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        var list = new StackPanel();
        void Show()
        {
            AnimationPreviewButton.StopAll();
            list.Children.Clear();
            foreach (var entry in l.Liquids.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                row.Children.Add(ObjectPicker("Holds", o => o.Type == ObjectType.Food && o.Food.IsLiquid, () => entry.FoodId, v => { entry.FoodId = v; }, "(choose a liquid)"));
                row.Children.Add(SpritePicker("Its look full of it", () => (entry.Sprite.SpriteGroup, entry.Sprite.SpriteName), (g, n) => { entry.Sprite.SpriteGroup = g; entry.Sprite.SpriteName = n; }, compact: true));
                var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
                remove.Click += (_, _) => { l.Liquids.Remove(entry); if (l.StartsFullOfId == entry.FoodId) l.StartsFullOfId = -1; Show(); };
                row.Children.Add(remove);
                list.Children.Add(row);
            }
            var add = new Button { Content = "Add a liquid it can hold", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = liquids.Count > 0 };
            add.Click += (_, _) => { l.Liquids.Add(new LiquidDefinition { FoodId = liquids.FirstOrDefault(f => l.Liquids.All(x => x.FoodId != f.Id))?.Id ?? -1 }); Show(); };
            list.Children.Add(add);
        }
        Show();
        body.Children.Add(list);
        var row2 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row2.Children.Add(ObjectPicker("Starts full of", o => o.Type == ObjectType.Food && o.Food.IsLiquid, () => l.StartsFullOfId, v => l.StartsFullOfId = v, "(empty)"));
        row2.Children.Add(SoundPicker("Fill sound", () => l.FillSoundResourceId, v => l.FillSoundResourceId = v));
        row2.Children.Add(SoundPicker("Pour sound", () => l.PourSoundResourceId, v => l.PourSoundResourceId = v));
        row2.Children.Add(ObjectPicker("A spill leaves", o => o.Id != obj.Id, () => l.SpillObjectId, v => l.SpillObjectId = v, "(nothing)"));
        body.Children.Add(row2);
    }

    private List<WorldObject> LiquidFoods() => _db.Objects.Where(o => o.TypeChosen && o.Type == ObjectType.Food && o.Food.IsLiquid).ToList();

    private Button LiquidsHelpButton()
    {
        var help = new Button { Content = "How liquids work (with pictures)...", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 2, 0, 6) };
        help.Click += (_, _) => new LiquidsHelpWindow { Owner = this }.ShowDialog();
        return help;
    }

    /// <summary>A liquid source (Exult's well, water trough, a wine cask): a liquid container used on it fills with its liquid.</summary>
    private void BuildLiquidSource(WorldObject obj)
    {
        if (obj.Type is not (ObjectType.Junk or ObjectType.UseableObject or ObjectType.CraftingStation)) return; // a well, a trough, a wine cask
        var body = Group("Liquid source (a well, a water trough, a wine cask)");
        body.Children.Add(ObjectPicker("Fills liquid containers with", o => o.Type == ObjectType.Food && o.Food.IsLiquid, () => obj.LiquidSourceId, v => obj.LiquidSourceId = v, "(nothing - not a source)"));
        body.Children.Add(Note("Leave it at (nothing) unless a bucket or bottle is filled here (Exult's well). The list shows your liquids: Food objects set to Liquid."));
        body.Children.Add(LiquidsHelpButton());
    }

    // ================= Building blocks =================

    /// <summary>A drop-down of objects (filtered), with a "none" choice.</summary>
    private FrameworkElement ObjectPicker(string label, Func<WorldObject, bool> filter, Func<int> get, Action<int> set, string none)
    {
        var choices = new List<object> { new WorldObject { Id = -1, Name = none } };
        choices.AddRange(_db.Objects.Where(o => o.TypeChosen && filter(o)));
        var combo = new ComboBox { Width = 200, ItemsSource = choices, DisplayMemberPath = "Name" };
        combo.SelectedItem = choices.OfType<WorldObject>().FirstOrDefault(o => o.Id == get()) ?? choices[0];
        combo.SelectionChanged += (_, _) => set(combo.SelectedItem is WorldObject o ? o.Id : -1);
        return label.Length == 0 ? new StackPanel { Margin = new Thickness(0, 14, 16, 6), Children = { combo } } : Labeled(label, combo);
    }

    /// <summary>A list of objects (keys on a keychain, a spell's reagents): each with Remove, and an Add drop-down.</summary>
    private FrameworkElement ObjectIdList(string label, List<int> ids, Func<WorldObject, bool> filter)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        void Show()
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = label });
            var row = new WrapPanel();
            foreach (int id in ids.ToList())
            {
                var name = _db.Objects.FirstOrDefault(o => o.Id == id)?.Name ?? $"(missing #{id})";
                var chip = new Button { Content = $"{name}  ✕", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 2, 6, 2), ToolTip = "Click to remove" };
                chip.Click += (_, _) => { ids.Remove(id); Show(); };
                row.Children.Add(chip);
            }
            var choices = new List<object> { new WorldObject { Id = -1, Name = "Add..." } };
            choices.AddRange(_db.Objects.Where(o => o.TypeChosen && filter(o)));
            var add = new ComboBox { Width = 160, ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
            add.SelectionChanged += (_, _) => { if (add.SelectedItem is WorldObject { Id: >= 0 } o) { ids.Add(o.Id); Show(); } };
            row.Children.Add(add);
            panel.Children.Add(row);
        }
        Show();
        return panel;
    }

    /// <summary>A list of sounds (an instrument's tunes).</summary>
    private FrameworkElement SoundList(string label, List<int> ids)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
        void Show()
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = label });
            var row = new WrapPanel();
            foreach (int id in ids.ToList())
            {
                var name = _resources.Entries.FirstOrDefault(r => r.Id == id)?.Name ?? $"(missing #{id})";
                var chip = new Button { Content = $"{name}  ✕", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 2, 6, 2), ToolTip = "Click to remove" };
                chip.Click += (_, _) => { ids.Remove(id); Show(); };
                row.Children.Add(chip);
            }
            var choices = new List<object> { new ResourceEntry { Id = -1, Name = "Add..." } };
            choices.AddRange(_resources.Entries.Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music));
            var add = new ComboBox { Width = 160, ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
            add.SelectionChanged += (_, _) => { if (add.SelectedItem is ResourceEntry { Id: >= 0 } r) { ids.Add(r.Id); Show(); } };
            row.Children.Add(add);
            panel.Children.Add(row);
        }
        Show();
        return panel;
    }

    /// <summary>One choice in an effect's drop-down: a built-in effect, or a custom one (green).</summary>
    private sealed record EffectChoice(MagicEffectKind Kind, CustomEffect? Custom)
    {
        public override string ToString() => Custom is null ? MagicEffect.KindName(Kind) : Custom.Name;
    }

    /// <summary>
    /// A list of effects: each a kind - built in, or a custom one (green) -
    /// and (where it means something) an amount from-to, with Remove; and Add.
    /// "Add Custom Stat..." makes a new custom effect: a name and the event
    /// commands it runs (no conditions). Custom ones can be edited and deleted.
    /// </summary>
    private FrameworkElement EffectsEditor(string label, List<MagicEffect> effects)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        void Show()
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = label });
            var choices = Enum.GetValues<MagicEffectKind>().Where(k => k != MagicEffectKind.Custom).Select(k => new EffectChoice(k, null))
                .Concat(_db.CustomEffects.Select(c => new EffectChoice(MagicEffectKind.Custom, c))).ToList();
            foreach (var effect in effects.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var kind = new ComboBox { Width = 170, ItemsSource = choices };
                kind.ItemContainerStyle = CustomEffectStyle();
                kind.SelectedItem = effect.Kind == MagicEffectKind.Custom
                    ? choices.FirstOrDefault(c => c.Custom?.Id == effect.CustomEffectId)
                    : choices.FirstOrDefault(c => c.Custom is null && c.Kind == effect.Kind);
                if (effect.Kind == MagicEffectKind.Custom) kind.Foreground = Brushes.Green;
                kind.SelectionChanged += (_, _) =>
                {
                    if (kind.SelectedItem is not EffectChoice choice) return;
                    effect.Kind = choice.Kind;
                    effect.CustomEffectId = choice.Custom?.Id ?? -1;
                    Show();
                };
                row.Children.Add(kind);
                if (MagicEffect.HasAmount(effect.Kind))
                {
                    row.Children.Add(InlineNumber(() => effect.Min, v => effect.Min = v));
                    row.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                    row.Children.Add(InlineNumber(() => effect.Max, v => effect.Max = v));
                }
                if (effect.Kind == MagicEffectKind.Custom && _db.CustomEffects.FirstOrDefault(c => c.Id == effect.CustomEffectId) is { } custom)
                {
                    var editCustom = new Button { Content = "Edit...", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Its name and event commands" };
                    editCustom.Click += (_, _) => { EditCustomEffect(custom); Show(); };
                    row.Children.Add(editCustom);
                }
                else row.Children.Add(new TextBlock { Text = MagicEffect.Describe(effect), Opacity = 0.6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
                var remove = new Button { Content = "✕", Width = 22, ToolTip = "Remove this effect", Margin = new Thickness(6, 0, 0, 0) };
                remove.Click += (_, _) => { effects.Remove(effect); Show(); };
                row.Children.Add(remove);
                panel.Children.Add(row);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var add = new Button { Content = "Add effect", Padding = new Thickness(6, 1, 6, 1) };
            add.Click += (_, _) => { effects.Add(new MagicEffect()); Show(); };
            var addCustom = new Button { Content = "Add Custom Stat...", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 0, 0), Foreground = Brushes.Green,
                ToolTip = "A new effect of your own: a name, and the event commands it runs on whoever it's used on" };
            addCustom.Click += (_, _) =>
            {
                var custom = new CustomEffect { Id = _db.NextCustomEffectId(), Name = "New Custom Stat" };
                if (!EditCustomEffect(custom, isNew: true)) return;
                _db.CustomEffects.Add(custom);
                effects.Add(new MagicEffect { Kind = MagicEffectKind.Custom, CustomEffectId = custom.Id });
                Show();
            };
            var deleteCustom = new Button { Content = "Delete a Custom Stat...", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 0, 0), IsEnabled = _db.CustomEffects.Count > 0 };
            deleteCustom.Click += (_, _) =>
            {
                var menu = new ContextMenu();
                foreach (var custom in _db.CustomEffects.ToList())
                {
                    var item = new MenuItem { Header = custom.Name, Foreground = Brushes.Green };
                    item.Click += (_, _) =>
                    {
                        if (MessageBox.Show($"Delete the custom stat \"{custom.Name}\"? Every potion, food, spell and object using it loses it.", "Delete Custom Stat", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                        _db.CustomEffects.Remove(custom);
                        foreach (var list in AllEffectLists()) list.RemoveAll(e => e.Kind == MagicEffectKind.Custom && e.CustomEffectId == custom.Id);
                        ShowCurrent();
                    };
                    menu.Items.Add(item);
                }
                menu.PlacementTarget = deleteCustom;
                menu.IsOpen = true;
            };
            buttons.Children.Add(add);
            buttons.Children.Add(addCustom);
            buttons.Children.Add(deleteCustom);
            panel.Children.Add(buttons);
        }
        Show();
        return panel;
    }

    /// <summary>Custom effects in a drop-down are green; built-in ones black.</summary>
    private static Style CustomEffectStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        var trigger = new DataTrigger { Binding = new System.Windows.Data.Binding("Custom") { Converter = new IsNotNullConverter() }, Value = true };
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Green));
        style.Triggers.Add(trigger);
        return style;
    }

    private sealed class IsNotNullConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is not null;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Every list of effects in every object - for removing a deleted custom stat from them all.</summary>
    private IEnumerable<List<MagicEffect>> AllEffectLists()
    {
        foreach (var o in _db.Objects)
        {
            yield return o.Food.Effects;
            yield return o.Potion.Effects;
            yield return o.Useable.Effects;
            foreach (var spell in o.Spellbook.Spells) yield return spell.Effects;
        }
    }

    /// <summary>The custom effect's own window: its name and its event commands (no conditions). True if kept.</summary>
    private bool EditCustomEffect(CustomEffect custom, bool isNew = false)
    {
        var window = new CustomEffectWindow(custom, _resources, _variables, _books, _db, _containers, _sprites, isNew) { Owner = this };
        return window.ShowDialog() == true;
    }

    /// <summary>A drop-down of the Tool objects: the kind of tool needed (any tool of that kind will do).</summary>
    private FrameworkElement ToolKindPicker(string label, Func<string> get, Action<string> set, string none)
    {
        var tools = _db.Objects.Where(o => o.TypeChosen && o.Type == ObjectType.Tool && o.Tool.ToolKind.Trim().Length > 0).ToList();
        var choices = new List<object> { new WorldObject { Id = -1, Name = none } };
        choices.AddRange(tools.Select(t => new WorldObject { Id = t.Id, Name = $"{t.Name}  (any {t.Tool.ToolKind.Trim()})", Tool = t.Tool }));
        string current = get().Trim();
        var combo = new ComboBox { Width = 230, ItemsSource = choices, DisplayMemberPath = "Name" };
        combo.SelectedItem = current.Length == 0 ? choices[0]
            : choices.OfType<WorldObject>().FirstOrDefault(o => o.Id >= 0 && string.Equals(o.Tool.ToolKind.Trim(), current, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        if (current.Length > 0 && combo.SelectedItem == choices[0])
        {
            // A kind no Tool object has (yet): kept, and shown.
            var orphan = new WorldObject { Id = -2, Name = $"(a {current} - no Tool object of that kind yet)", Tool = new ToolSettings { ToolKind = current } };
            choices.Add(orphan);
            combo.ItemsSource = null;
            combo.ItemsSource = choices;
            combo.SelectedItem = orphan;
        }
        combo.SelectionChanged += (_, _) => set(combo.SelectedItem is WorldObject { Id: not -1 } tool ? tool.Tool.ToolKind.Trim() : string.Empty);
        return Labeled(label, combo);
    }

    /// <summary>A keychain's look: a sprite from the Sprite Editor's Keychain group (frames 1-5 = 0, 1, 2, 3, 4+ keys), with its guide.</summary>
    private void BuildKeychainLook(WorldObject obj)
    {
        var body = Group("Look");
        body.Children.Add(SpritePicker("Sprite (from the Keychain group)", () => (obj.SpriteGroup, obj.SpriteName), (g, n) => { obj.SpriteGroup = g; obj.SpriteName = n; }, onlyGroup: SpriteDatabase.KeychainGroupName));
        var guide = new Button { Content = "How keychain sprites work...", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1) };
        guide.Click += (_, _) => new FrameStatesGuideWindow(null, FrameStatesGuideWindow.Keychain) { Owner = this }.ShowDialog();
        body.Children.Add(guide);
    }

    private static TextBox InlineNumber(Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 44, Text = get().ToString(), VerticalAlignment = VerticalAlignment.Center };
        void Commit() { if (int.TryParse(box.Text.Trim(), out int v)) set(Math.Max(0, v)); box.Text = get().ToString(); }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
        return box;
    }
}
