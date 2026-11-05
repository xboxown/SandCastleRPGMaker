// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Things used up or emptied, ported from Exult's own usecode
/// (content/bgkeyring/src):
///  - Food (items/food.uc useEdible, misc/food_stuff/functions.uc
///    consumeEdible): double-click, then click who eats it. Only a party
///    member who is awake, alive and not paralyzed eats. At food 25 or more
///    (FOODLEVEL_FULL): "No, thank thee." Otherwise it's taken (stealItem -
///    eaten in sight of its owner's side, that's theft), the eat sound
///    plays, and they say how they feel by their food level before and
///    after (PECKISH 5, WELLFED 10, STUFFED 20, FULL 25 - the exact barks
///    below), then their food goes up by its nutrition.
///  - Potions: double-click, then click who drinks: its effects, used up,
///    maybe leaving an empty bottle where it was.
///  - Liquid containers (items/bucket.uc): double-click, then click a
///    source (filled - "The bucket is full." if it isn't empty), yourself
///    ("Ahhh, how refreshing." - drunk; "The bucket is empty."), someone
///    else (doused and woken: "Hey, stop that!"), a lit light (water puts
///    it out), or the floor (spilt).
///  - Useable objects: effects (on you or on a target), By Use events, and
///    charges.
/// </summary>
public partial class U8GameWindow
{
    // Exult's food levels (misc/food_stuff/constants.uc).
    private const int FoodPeckish = 5, FoodWellFed = 10, FoodStuffed = 20, FoodFull = 25;

    // ================= Food =================

    private void UseFoodItem(Used u)
    {
        if (FireUseEvent(u)) return;
        BeginChoosingTarget($"Who eats \"{u.Obj.Name}\"?", target =>
        {
            if (!ToolStillThere(u.Source)) return;
            // A liquid on its own (a bottle of water) used on an empty container: poured into it.
            if (u.Obj.Food.IsLiquid && ThingTargeted(target) is { Obj.Type: ObjectType.LiquidContainer } container && PourBetween(container, u)) return;
            if (CharacterTargeted(target) is { } eater) Eat(u, eater);
        });
    }

    /// <summary>consumeEdible.</summary>
    private void Eat(Used u, CharacterDefinition npc)
    {
        if (!IsPartyOrPlayer(npc) || IsDead(npc) || Has(npc, Ailment.Asleep) || Has(npc, Ailment.Paralyzed)) return;
        if (u.Obj.Food.IsLiquid && u.Obj.Food.Undrinkable) { if (npc.IsHuman) ShowFloatingText(npc, "No, thank thee.", 2500); return; }
        if (U8Rules.EatingAsUltima8)
        {
            if (u.Placed is not null && ObjectOwner(u.Obj, u.Placed) is { } taken) Theft(taken.Owner, taken.FactionId);
            PlaySound(u.Obj.Food.EatSoundResourceId);
            UseUpOne(u.Source);
            U8Feed(npc, u.Obj.Food.Nutrition); // HealProcess::feedAvatar (U8GameWindow.U8Reactions.cs)
            ApplyEffects(u.Obj.Food.Effects, npc, $"\"{u.Obj.Name}\"");
            if (npc == _player && string.IsNullOrEmpty(StatusText.Text)) StatusText.Text = $"You eat \"{u.Obj.Name}\".";
            ShowClock();
            return;
        }
        var vitals = VitalsOf(npc);
        int foodLevel = vitals.Food;
        int nutrition = u.Obj.Food.Nutrition;
        int after = foodLevel + nutrition;
        if (foodLevel >= FoodFull)
        {
            if (npc.IsHuman) ShowFloatingText(npc, "No, thank thee.", 2500);
            return;
        }
        if (u.Placed is not null && ObjectOwner(u.Obj, u.Placed) is { } owned) Theft(owned.Owner, owned.FactionId); // stealItem: "Make sure it has been properly nicked"
        PlaySound(u.Obj.Food.EatSoundResourceId);
        UseUpOne(u.Source);

        int rand = Rng.Next(10);
        string bark = "";
        if (foodLevel >= FoodStuffed) { if (rand > 4) bark = "I'll soon be plump."; }
        else if (foodLevel >= FoodPeckish) bark = after >= FoodFull && rand < 3 ? "Belch" : "Ahh, very tasty.";
        else if (after >= FoodStuffed) bark = rand > 5 ? "That hit the spot!" : "Burp";
        else if (after >= FoodWellFed) bark = "Ah yes, much better.";
        else if (after >= FoodPeckish) bark = rand > 5 || npc == _player ? "I am still hungry." : "May I have some more?";
        else bark = rand > 5 ? "More!" : "I must have more!";
        if (bark.Length > 0 && npc.IsHuman) ShowFloatingText(npc, bark, 2500);

        vitals.Food = Math.Clamp(after, 0, SurvivalSettings.MaxFood);
        ApplyEffects(u.Obj.Food.Effects, npc, $"\"{u.Obj.Name}\"");
        if (npc == _player && string.IsNullOrEmpty(StatusText.Text)) StatusText.Text = $"You eat \"{u.Obj.Name}\".";
        ShowClock();
    }

    // ================= Potions =================

    private void UsePotion(Used u)
    {
        if (FireUseEvent(u)) return;
        BeginChoosingTarget($"Who drinks \"{u.Obj.Name}\"?", target =>
        {
            if (!ToolStillThere(u.Source) || CharacterTargeted(target) is not { } drinker || IsDead(drinker)) return;
            if (u.Placed is not null && ObjectOwner(u.Obj) is { } owned) Theft(owned.Owner, owned.FactionId);
            PlaySound(u.Obj.Potion.DrinkSoundResourceId);
            ReplaceWithLeftover(u, u.Obj.Potion.EmptyObjectId);
            StatusText.Text = drinker == _player ? $"You drink \"{u.Obj.Name}\"." : $"{drinker.Name} drinks \"{u.Obj.Name}\".";
            ApplyEffects(u.Obj.Potion.Effects, drinker, $"\"{u.Obj.Name}\"");
        });
    }

    /// <summary>One of it is used up, leaving something else in its place if set (an empty bottle): a single one simply becomes it, where it is; one of a stack leaves it at the user's feet.</summary>
    private void ReplaceWithLeftover(Used u, int leftoverId)
    {
        bool single = (u.Placed?.Quantity ?? u.Carried?.Quantity ?? 1) <= 1;
        if (leftoverId >= 0 && single && _objects.Objects.Any(o => o.Id == leftoverId))
        {
            if (u.Placed is { } placed) { placed.ObjectId = leftoverId; RefreshObjectLook(placed); }
            if (u.Carried is { } carried) { carried.ItemId = leftoverId; RefreshAfterMove(); }
            return;
        }
        var (x, y) = CellOfUsed(u);
        UseUpOne(u.Source);
        if (leftoverId >= 0) SpawnObjectAt(leftoverId, 1, x, y);
    }

    // ================= Liquid containers (Exult's bucket) =================

    /// <summary>A liquid: a Food object set to Liquid, by id.</summary>
    private WorldObject? LiquidFood(int foodId) => foodId < 0 ? null : _objects.Objects.FirstOrDefault(o => o.Id == foodId && o.Type == ObjectType.Food);

    /// <summary>Whether this container can hold that liquid (it's on its list).</summary>
    private static bool CanHold(WorldObject container, int foodId) => container.Liquid.Liquids.Any(l => l.FoodId == foodId);

    /// <summary>
    /// Pouring (not in Exult - the user's rule): a liquid container used on
    /// another one - the liquid goes from the full one into the empty one
    /// (the poured-out one is used up if set so, else left empty) - or used on
    /// a liquid on its own (a Food set to Liquid: a bottle of water), which
    /// fills it and is used up. False = the other thing isn't a liquid.
    /// </summary>
    private bool PourBetween(Used container, Used other)
    {
        string name = container.Obj.Name.ToLowerInvariant();
        if (other.Obj.Type == ObjectType.Food && other.Obj.Food.IsLiquid)
        {
            if (LiquidOf(container) >= 0) { AvatarBark($"The {name} is full."); return true; }
            if (!CanHold(container.Obj, other.Obj.Id)) { StatusText.Text = $"\"{container.Obj.Name}\" can't hold {other.Obj.Name.ToLowerInvariant()}."; return true; }
            PlaySound(container.Obj.Liquid.FillSoundResourceId);
            SetLiquid(container, other.Obj.Id);
            UseUpOne(other.Source);
            StatusText.Text = $"You pour \"{other.Obj.Name}\" into \"{container.Obj.Name}\".";
            return true;
        }
        if (other.Obj.Type != ObjectType.LiquidContainer) return false;
        bool full = LiquidOf(container) >= 0, otherFull = LiquidOf(other) >= 0;
        if (!full && !otherFull) { AvatarBark($"The {name} is empty."); return true; }
        if (full && otherFull) { AvatarBark($"The {other.Obj.Name.ToLowerInvariant()} is full."); return true; }
        var (from, into) = full ? (container, other) : (other, container);
        int liquidId = LiquidOf(from);
        if (!CanHold(into.Obj, liquidId)) { StatusText.Text = $"\"{into.Obj.Name}\" can't hold {LiquidFood(liquidId)?.Name.ToLowerInvariant() ?? "that"}."; return true; }
        PlaySound(from.Obj.Liquid.PourSoundResourceId);
        PlaySound(into.Obj.Liquid.FillSoundResourceId);
        SetLiquid(into, liquidId);
        if (from.Obj.Liquid.UsedUpWhenPouredOut) UseUpOne(from.Source);
        else SetLiquid(from, -1);
        StatusText.Text = $"You pour \"{from.Obj.Name}\" into \"{into.Obj.Name}\".";
        return true;
    }

    private void UseLiquid(Used u)
    {
        if (FireUseEvent(u)) return;
        string name = u.Obj.Name.ToLowerInvariant();
        BeginChoosingTarget($"Use \"{u.Obj.Name}\" on what?", target =>
        {
            if (!ToolStillThere(u.Source)) return;
            if (ThingTargeted(target) is { } other && !IsSameThing(other, u) && PourBetween(u, other)) return; // another container, a bottle of water
            int liquidId = LiquidOf(u);
            var liquid = LiquidFood(liquidId);
            bool empty = liquid is null;

            // Yourself: drink it (bucket.uc: "Ahhh, how refreshing.").
            if (target is PlayerTarget || CharacterTargeted(target) == _player)
            {
                if (empty) { AvatarBark($"The {name} is empty."); return; }
                if (liquid!.Food.Undrinkable) { AvatarBark("No, thank thee."); return; }
                AvatarBark("Ahhh, how refreshing.");
                SetLiquid(u, -1);
                if (_player is not null)
                {
                    if (liquid.Food.Nutrition > 0) VitalsOf(_player).Food = Math.Clamp(VitalsOf(_player).Food + liquid.Food.Nutrition, 0, SurvivalSettings.MaxFood);
                    ApplyEffects(liquid.Food.Effects, _player, liquid.Name);
                    ShowClock();
                }
                return;
            }

            if (CharacterTargeted(target) is { } npc)
            {
                // Someone who gives a liquid (a cow): milked into an empty container (milking.uc).
                if (npc.GivesLiquidFoodId >= 0 && LiquidFood(npc.GivesLiquidFoodId) is { } given && !IsDead(npc))
                {
                    if (!empty) { AvatarBark($"The {name} is full."); return; }
                    if (!CanHold(u.Obj, given.Id)) { StatusText.Text = $"\"{u.Obj.Name}\" can't hold {given.Name.ToLowerInvariant()}."; return; }
                    Milk(u, npc, given);
                    return;
                }
                // Anyone else: doused and woken (useBucketOnNPC: "Hey, stop that!").
                if (empty) { AvatarBark($"The {name} is empty."); return; }
                Cure(npc, Ailment.Asleep);
                if (npc.IsHuman) ShowFloatingText(npc, "Hey, stop that!", 2500);
                PlaySound(u.Obj.Liquid.PourSoundResourceId);
                SetLiquid(u, -1);
                var (nx, ny) = CellOf(npc);
                if (u.Obj.Liquid.SpillObjectId >= 0) SpawnObjectAt(u.Obj.Liquid.SpillObjectId, 1, nx, ny);
                return;
            }

            if (ObjectTargeted(target) is { } hit && !ReferenceEquals(hit.Instance, u.Placed))
            {
                // A source (a well, a trough): filled (useBucketOnWell: "The bucket is full." if it isn't empty).
                if (LiquidFood(hit.Obj.LiquidSourceId) is { } source)
                {
                    if (!empty) { AvatarBark($"The {name} is full."); return; }
                    if (!CanHold(u.Obj, source.Id)) { StatusText.Text = $"\"{u.Obj.Name}\" can't hold {source.Name.ToLowerInvariant()}."; return; }
                    FireObjectEvent(hit.Instance, hit.Obj, BookTriggerConditionBased.ByTradeItem); // the well's own script first (By Tool Used On It: the rope, the gear...), then it's filled
                    PlaySound(u.Obj.Liquid.FillSoundResourceId);
                    SetLiquid(u, source.Id);
                    StatusText.Text = $"You fill \"{u.Obj.Name}\" with {source.Name.ToLowerInvariant()}.";
                    return;
                }
                // A lit light: water puts it out (useBucketOnDousable).
                if (hit.Obj.Type == ObjectType.LightSource && hit.Instance.IsLitRuntime == true)
                {
                    if (empty) { AvatarBark($"The {name} is empty."); return; }
                    if (!liquid!.Food.PutsOutFires) return;
                    PlaySound(u.Obj.Liquid.PourSoundResourceId);
                    SetLiquid(u, -1);
                    PutOutLight(hit.Instance, hit.Obj);
                    StatusText.Text = $"You douse \"{hit.Obj.Name}\".";
                    return;
                }
                // A lit fuse: water puts that out too.
                if (hit.Obj.Type == ObjectType.Explosive && hit.Instance.FuseRuntime >= 0 && liquid?.Food.PutsOutFires == true)
                {
                    hit.Instance.FuseRuntime = -1;
                    hit.Instance.IsLitRuntime = false;
                    RefreshObjectLook(hit.Instance);
                    SetLiquid(u, -1);
                    StatusText.Text = $"You douse the fuse of \"{hit.Obj.Name}\".";
                    return;
                }
            }

            // Anything else - the floor: spilt (useBucketOnGround).
            if (empty) { AvatarBark($"The {name} is empty."); return; }
            PlaySound(u.Obj.Liquid.PourSoundResourceId);
            SetLiquid(u, -1);
            var (sx, sy) = LastTargetCell();
            if (u.Obj.Liquid.SpillObjectId >= 0) SpawnObjectAt(u.Obj.Liquid.SpillObjectId, 1, sx, sy);
            StatusText.Text = $"You empty \"{u.Obj.Name}\".";
        });
    }

    /// <summary>
    /// milking.uc milkCow: "Hold still..." (3 in 10), the milking sound three
    /// times while the animal stands still, then the container is full, the
    /// Avatar says "That's a good girl!", the animal its bark ("MoooOOOoooo"),
    /// and 1 in 3 a party member asks "Don't we have a quest?".
    /// </summary>
    private void Milk(Used u, CharacterDefinition animal, WorldObject liquid)
    {
        if (Rng.Next(10) < 3) AvatarBark("Hold still...");
        if (MapCharacterOf(animal) is { } standing) { StopWalking(standing); standing.Npc.WaitMs = 3500; } // it stands still to be milked
        ShowPlayerAtWork(400);
        StatusText.Text = $"You milk {animal.Name}...";
        int beats = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) =>
        {
            if (_gamePaused) return; // waits while the game is paused
            beats++;
            if (beats <= 3)
            {
                PlaySound(animal.GivesLiquidSoundResourceId);
                ShowPlayerAtWork(400);
                return;
            }
            timer.Stop();
            if (!ToolStillThere(u.Source) || LiquidOf(u) >= 0) return;
            SetLiquid(u, liquid.Id);
            AvatarBark("That's a good girl!");
            if (animal.GivesLiquidBark.Trim().Length > 0 && !IsDead(animal)) ShowFloatingText(animal, animal.GivesLiquidBark.Trim(), 2500);
            if (Rng.Next(3) == 0)
            {
                var others = AllLivingCharacters().Where(c => c.IsInParty && c != _player && c.IsHuman && !IsIncapacitated(c)).ToList();
                if (others.Count > 0) ShowFloatingText(others[Rng.Next(others.Count)], "Don't we have a quest?", 3000);
            }
            StatusText.Text = $"\"{u.Obj.Name}\" is full of {liquid.Name.ToLowerInvariant()}.";
        };
        PlaySound(animal.GivesLiquidSoundResourceId);
        timer.Start();
    }

    // ================= Useable objects =================

    private void UseUseable(Used u)
    {
        var settings = u.Obj.Useable;
        if (settings.Charges > 0 && (ChargesOf(u) <= 0 || IsSpent(u)))
        {
            StatusText.Text = $"\"{u.Obj.DisplayName(spent: true)}\" - nothing happens.";
            return;
        }
        if (settings.AsksForTarget && settings.Effects.Count > 0)
        {
            BeginChoosingTarget($"Use \"{u.Obj.Name}\" on what?", target =>
            {
                if (!ToolStillThere(u.Source)) return;
                if (CharacterTargeted(target) is { } who) ApplyEffects(settings.Effects, who, $"\"{u.Obj.Name}\"");
                else if (ObjectTargeted(target) is { } hit) ApplyEffectsToObject(settings.Effects, hit.Instance, hit.Obj);
                else return;
                FinishUse(u);
            });
            return;
        }
        if (UserOf(u) is { } user) ApplyEffects(settings.Effects, user, $"\"{u.Obj.Name}\"");
        FinishUse(u);
    }

    /// <summary>After a use: the sound, its By Use event, and a charge gone (used up: gone, or spent).</summary>
    private void FinishUse(Used u)
    {
        var settings = u.Obj.Useable;
        PlaySound(settings.UseSoundResourceId);
        bool fired = FireUseEvent(u);
        if (!fired && settings.Effects.Count == 0) StatusText.Text = $"\"{u.Obj.Name}\" - nothing happens.";
        if (settings.Charges <= 0) return;
        int left = ChargesOf(u) - 1;
        SetCharges(u, left);
        if (left > 0) return;
        if (settings.RemovedWhenUsedUp) UseUpOne(u.Source);
        else SetSpent(u);
    }
}
