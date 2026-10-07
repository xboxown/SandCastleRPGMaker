using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Things a traveller carries, ported from Exult:
///  - Keychain (content/bgkeyring/src/items/keyring.uc): double-click, then
///    click yourself - every key the party carries goes onto it ("Added 3
///    keys", the key sound, its frame showing how many: 0-4); or a door or
///    container - it works as whichever key fits (KeyInternal), or "We
///    don't have that key." / "It's not on the keyring.".
///  - Sextant (cheat_screen.cc's coordinates): (tile - origin) / 10,
///    "North"/"South", "West"/"East". Under a roof it can't see the sky.
///  - Map (intrinsics.cc display_map): its picture over the game until a
///    click (Get_click); a cross marks the party if someone in it carries a
///    sextant and they're not inside (is_main_actor_inside).
///  - Clock (items/pocketwatch.uc): " 3:05pm" said over it. A sundial: only
///    outdoors, by day.
///  - Bedroll (misc/rest_functions.uc advanceTimeDueToRest): the party
///    sleeps - for each member not starving (food 10 or more): ailments
///    cleared, health and mana back, food down by the hours slept; lights
///    burn down by the time passed. Not with enemies in sight.
/// </summary>
public partial class GameTestWindow
{
    // ================= Keychain =================

    private void UseKeychain(Used u)
    {
        if (FireUseEvent(u)) return;
        BeginChoosingTarget($"Use \"{u.Obj.Name}\" on what?", target =>
        {
            if (!ToolStillThere(u.Source)) return;
            if (target is PlayerTarget || CharacterTargeted(target) == _player) { AddPartyKeys(u); return; }
            if (CharacterTargeted(target) is not null) return;
            var keys = KeysOf(u);
            string[] barks = { "We don't have that key.", "It's not on the keyring." };
            // A door (KeyInternal: doors and chests alike).
            if (ObjectTargeted(target) is { Obj.Type: ObjectType.Door } door)
            {
                var fitting = keys.Contains(door.Obj.Door.RequiredKeyItemId) && !door.Obj.Door.MagicallyLocked ? _objects.Objects.FirstOrDefault(o => o.Id == door.Obj.Door.RequiredKeyItemId) : null;
                if (fitting is null) { FlashPointer(MousePointerKind.Blocked); PartySay(barks); return; }
                PlaySound(u.Obj.Keychain.AddKeySoundResourceId);
                UseKeyOnDoor(fitting, door.Instance, door.Obj);
                return;
            }
            if (ContainerTargeted(target) is { } hit)
            {
                var (container, data) = hit;
                var key = data.RequiredKeyCategory == ReferencedItemCategory.Object && keys.Contains(data.RequiredKeyItemId)
                    ? _objects.Objects.FirstOrDefault(o => o.Id == data.RequiredKeyItemId) : null;
                if (key is null) { FlashPointer(MousePointerKind.Blocked); PartySay(barks); return; }
                PlaySound(u.Obj.Keychain.AddKeySoundResourceId);
                UseKeyOn(key, u.Source, container, data);
            }
            // Anything else: "The target cannot be unlocked" - nothing happens.
        });
    }

    /// <summary>Keyring_data.add_party_keys: every key the party carries goes on the ring (the keys are gone from the world).</summary>
    private void AddPartyKeys(Used u)
    {
        var keys = KeysOf(u);
        int added = 0;
        foreach (var (item, list, carrier) in PartyItems().ToList())
        {
            if (item.Category != ReferencedItemCategory.Object || DefinitionOf(item) is not { Type: ObjectType.Key } key || ReferenceEquals(item, u.Carried)) continue;
            if (!keys.Contains(key.Id)) keys.Add(key.Id);
            int count = Math.Max(1, item.Quantity);
            list.Remove(item);
            foreach (var slot in carrier.EquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) carrier.EquippedSlots.Remove(slot);
            foreach (var slot in carrier.CustomEquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) carrier.CustomEquippedSlots.Remove(slot);
            added += count;
        }
        PlaySound(u.Obj.Keychain.AddKeySoundResourceId);
        SetFrame(u, Math.Min(keys.Count, 4)); // set_keyring_frame
        RefreshAfterMove();
        PartySay($"Added {added} keys");
        StatusText.Text = $"\"{u.Obj.Name}\" now holds {keys.Count} key{(keys.Count == 1 ? "" : "s")}.";
    }

    // ================= Sextant =================

    private void UseSextant(Used u)
    {
        if (FireUseEvent(u)) return;
        if (PlayerUnderRoof) { StatusText.Text = $"\"{u.Obj.Name}\" can't see the sky from in here."; return; }
        var (lat, longi) = Coordinates(u.Obj.Sextant);
        SayOver(u, $"{Math.Abs(lat)} {(lat < 0 ? "North" : "South")}, {Math.Abs(longi)} {(longi < 0 ? "West" : "East")}", 4000);
    }

    /// <summary>Exult's coordinates: (tile - origin) / tiles-per-degree, in 8-pixel tiles, truncated toward 0 as C does.</summary>
    private (int Latitude, int Longitude) Coordinates(SextantSettings s)
    {
        int originCellX = s.OriginCellX >= 0 ? s.OriginCellX : _map.Width / 2, originCellY = s.OriginCellY >= 0 ? s.OriginCellY : _map.Height / 2;
        double originX = (originCellX + 0.5) * _tileWidth / SpriteSizing.BaseUnit, originY = (originCellY + 0.5) * _tileHeight / SpriteSizing.BaseUnit;
        int perDegree = Math.Max(1, s.TilesPerDegree);
        int tx = (int)Math.Floor(_playerPixelX / SpriteSizing.BaseUnit), ty = (int)Math.Floor(_playerPixelY / SpriteSizing.BaseUnit);
        return ((int)((ty - originY) / perDegree), (int)((tx - originX) / perDegree));
    }

    // ================= Map =================

    private void UseMap(Used u)
    {
        if (FireUseEvent(u)) return;
        var m = u.Obj.MapPicture;
        var sprite = m.Picture.IsSet ? ObjectLooks.Find(_sprites, m.Picture.SpriteGroup, m.Picture.SpriteName) : null;
        var frame = sprite is { Frames.Count: > 0 } ? LoadStaticFrame(sprite.Frames[0].FilePath) : null;
        if (frame is null) { StatusText.Text = $"\"{u.Obj.Name}\" has no picture (Object Editor > Map)."; return; }
        // Fits the screen, keeping its shape, in whole steps where it can (pixel art).
        double scale = Math.Min(ViewportWidth / (double)frame.PixelWidth, ViewportHeight / (double)frame.PixelHeight);
        if (scale >= 1) scale = Math.Floor(scale);
        double width = frame.PixelWidth * scale, height = frame.PixelHeight * scale;
        var canvas = new Canvas { Width = width, Height = height };
        var image = new Image { Source = frame, Width = width, Height = height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        canvas.Children.Add(image);
        // Paint_map: a cross where the party is - with a sextant in the party, and not inside.
        bool sextant = PartyItems().Any(p => DefinitionOf(p.Item) is { Type: ObjectType.Sextant });
        if (m.ShowLocation && !PlayerUnderRoof && (!m.LocationNeedsSextant || sextant))
        {
            double x = _playerPixelX / Math.Max(1, _map.Width * _tileWidth) * width, y = _playerPixelY / Math.Max(1, _map.Height * _tileHeight) * height;
            canvas.Children.Add(new Line { X1 = x, Y1 = y - 2.5, X2 = x, Y2 = y + 2.5, Stroke = Brushes.Red, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = x - 2.5, Y1 = y, X2 = x + 2.5, Y2 = y, Stroke = Brushes.Red, StrokeThickness = 1 });
        }
        ShowObjectOverlay(canvas, takesClicks: false, scaleLikeViewport: true);
        StatusText.Text = "Click to put the map away.";
    }

    // ================= Clock =================

    private void UseClock(Used u)
    {
        if (FireUseEvent(u)) return;
        if (u.Obj.Clock.Sundial && (PlayerUnderRoof || _clock.Hour < 6 || _clock.Hour >= 18))
        {
            StatusText.Text = $"\"{u.Obj.Name}\" casts no shadow.";
            return;
        }
        // Pocketwatch: " " + hour + ":" + minute (two digits) + "am" / "pm", noon being pm.
        int hour = _clock.Hour % 12 == 0 ? 12 : _clock.Hour % 12;
        SayOver(u, $" {hour}:{_clock.Minute:00}{(_clock.Hour < 12 ? "am" : "pm")}");
    }

    // ================= Bedroll =================

    /// <summary>The party is asleep (the screen dark): the game waits.</summary>
    private bool _restingNow;

    private void UseBedroll(Used u)
    {
        if (FireUseEvent(u)) return;
        if (_player is null || IsIncapacitated(_player)) return;
        if (AllLivingCharacters().Any(c => !IsPartyOrPlayer(c) && !IsIncapacitated(c) && Attitude(c, _player) == FactionBehavior.Attack && CanSee(c, _player)))
        {
            AvatarBark("We cannot rest with foes about!");
            StatusText.Text = "You can't sleep with enemies in sight.";
            return;
        }
        int hours = u.Obj.Bedroll.SleepHours;
        if (u.Obj.Bedroll.WakeAtDawn) hours = (6 - _clock.Hour + 24) % 24 is int untilDawn && untilDawn > 0 ? untilDawn : 24;
        hours = Math.Clamp(hours, 1, 24);
        if (u.Placed is { } laid) { laid.IsOpenRuntime = true; RefreshObjectLook(laid); } // laid out
        _restingNow = true;
        ApplyPoseSprite(PoseByName("Sleeping") ?? _idlePose);
        var dark = new Rectangle { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
        Panel.SetZIndex(dark, 999);
        ViewportHost.Children.Add(dark);
        var fall = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.2));
        fall.Completed += (_, _) =>
        {
            Rest(hours);
            var rise = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.2));
            rise.Completed += (_, _) =>
            {
                ViewportHost.Children.Remove(dark);
                _restingNow = false;
                ApplyPoseSprite(_idlePose);
                if (u.Placed is { } placed && _map.Objects.Contains(placed)) { placed.IsOpenRuntime = false; RefreshObjectLook(placed); }
                StatusText.Text = $"You wake, rested ({hours} hour{(hours == 1 ? "" : "s")} have passed).";
            };
            dark.BeginAnimation(UIElement.OpacityProperty, rise);
        };
        dark.BeginAnimation(UIElement.OpacityProperty, fall);
    }

    /// <summary>advanceTimeDueToRest for the party, then the clock moves on.</summary>
    private void Rest(int hours)
    {
        foreach (var member in AllLivingCharacters().Where(IsPartyOrPlayer).ToList())
        {
            var vitals = VitalsOf(member);
            if (vitals.Food < FoodWellFed && member.Survival.CanGetHungry) continue; // "The party member gets nothing from rest if he is starving"
            foreach (var ailment in new[] { Ailment.Asleep, Ailment.Poisoned, Ailment.Paralyzed, Ailment.Cursed, Ailment.Charmed, Ailment.Invisible, Ailment.Protected })
                Cure(member, ailment);
            ShowInvisibility(member);
            // recoverPropByRest (the original's own usecode, not in Exult's source): here a full night (8 hours) brings health and mana all the way back.
            int maxHp = MaxHp(member);
            if (GetHp(member) is int hp && maxHp > 0) SetHp(member, Math.Min(maxHp, hp + (int)Math.Ceiling(maxHp * hours / 8.0)));
            int maxMana = Stat(member, BuiltInStatKind.Magic, 0);
            if (maxMana > 0) SetStat(member, BuiltInStatKind.Mana, Math.Min(maxMana, Stat(member, BuiltInStatKind.Mana, 0) + (int)Math.Ceiling(maxMana * hours / 8.0)));
            if (member.Survival.CanGetHungry) vitals.Food = Math.Clamp(vitals.Food - hours, 0, SurvivalSettings.MaxFood); // set_npc_prop(FOODLEVEL, -sleep_time)
        }
        int minutes = hours * 60;
        _clock.AdvanceMinutes(minutes);
        // Lights burn down by the time slept; plants grow; the hour's schedules are taken up.
        BurnLights(minutes);
        GrowPlants(minutes);
        _lastObjectMinute = TotalMinutes();
        _lastNewTypeMinute = TotalMinutes();
        _lastClockMinute = TotalMinutes();
        ScheduleNpcs(_clock.Hour);
        ShowClock();
    }
}
