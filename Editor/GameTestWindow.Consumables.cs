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
public partial class GameTestWindow
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
            if (CharacterTargeted(target) is { } eater) Eat(u, eater);
        });
    }

    /// <summary>consumeEdible.</summary>
    private void Eat(Used u, CharacterDefinition npc)
    {
        if (!IsPartyOrPlayer(npc) || IsDead(npc) || Has(npc, Ailment.Asleep) || Has(npc, Ailment.Paralyzed)) return;
        var vitals = VitalsOf(npc);
        int foodLevel = vitals.Food;
        int nutrition = u.Obj.Food.Nutrition;
        int after = foodLevel + nutrition;
        if (foodLevel >= FoodFull)
        {
            if (npc.IsHuman) ShowFloatingText(npc, "No, thank thee.", 2500);
            return;
        }
        if (u.Placed is not null && ObjectOwner(u.Obj) is { } owned) Theft(owned.Owner, owned.FactionId); // stealItem: "Make sure it has been properly nicked"
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

    private void UseLiquid(Used u)
    {
        if (FireUseEvent(u)) return;
        string name = u.Obj.Name.ToLowerInvariant();
        BeginChoosingTarget($"Use \"{u.Obj.Name}\" on what?", target =>
        {
            if (!ToolStillThere(u.Source)) return;
            string liquidName = LiquidOf(u);
            var liquid = u.Obj.Liquid.Liquids.FirstOrDefault(l => string.Equals(l.Name, liquidName, StringComparison.OrdinalIgnoreCase));
            bool empty = liquidName.Length == 0;

            // Yourself: drink it.
            if (target is PlayerTarget || CharacterTargeted(target) == _player)
            {
                if (empty) { AvatarBark($"The {name} is empty."); return; }
                if (liquid?.Undrinkable == true) { AvatarBark("No, thank thee."); return; }
                AvatarBark("Ahhh, how refreshing.");
                SetLiquid(u, "");
                if (liquid is not null && _player is not null)
                {
                    if (liquid.Nutrition > 0) VitalsOf(_player).Food = Math.Clamp(VitalsOf(_player).Food + liquid.Nutrition, 0, SurvivalSettings.MaxFood);
                    ApplyEffects(liquid.Effects, _player, liquid.Name);
                    ShowClock();
                }
                return;
            }

            // Someone else: doused (useBucketOnNPC) - woken, peeved.
            if (CharacterTargeted(target) is { } npc)
            {
                if (empty) { AvatarBark($"The {name} is empty."); return; }
                Cure(npc, Ailment.Asleep);
                if (npc.IsHuman) ShowFloatingText(npc, "Hey, stop that!", 2500);
                PlaySound(u.Obj.Liquid.PourSoundResourceId);
                SetLiquid(u, "");
                var (nx, ny) = CellOf(npc);
                if (u.Obj.Liquid.SpillObjectId >= 0) SpawnObjectAt(u.Obj.Liquid.SpillObjectId, 1, nx, ny);
                return;
            }

            if (ObjectTargeted(target) is { } hit)
            {
                // A source (a well, a trough): filled.
                if (hit.Obj.LiquidSource.Trim().Length > 0 && !ReferenceEquals(hit.Instance, u.Placed))
                {
                    if (!empty) { AvatarBark($"The {name} is full."); return; }
                    string source = hit.Obj.LiquidSource.Trim();
                    var fits = u.Obj.Liquid.Liquids.FirstOrDefault(l => string.Equals(l.Name, source, StringComparison.OrdinalIgnoreCase));
                    if (fits is null) { StatusText.Text = $"\"{u.Obj.Name}\" can't hold {source.ToLowerInvariant()}."; return; }
                    PlaySound(u.Obj.Liquid.FillSoundResourceId);
                    SetLiquid(u, fits.Name);
                    StatusText.Text = $"You fill \"{u.Obj.Name}\" with {fits.Name.ToLowerInvariant()}.";
                    return;
                }
                // A lit light: water puts it out (useBucketOnDousable).
                if (hit.Obj.Type == ObjectType.LightSource && hit.Instance.IsLitRuntime == true)
                {
                    if (empty) { AvatarBark($"The {name} is empty."); return; }
                    if (liquid?.PutsOutFires != true) return;
                    hit.Instance.IsLitRuntime = false;
                    PlaySound(hit.Obj.Light.OffSoundResourceId);
                    PlaySound(u.Obj.Liquid.PourSoundResourceId);
                    RefreshObjectLook(hit.Instance);
                    SetLiquid(u, "");
                    StatusText.Text = $"You douse \"{hit.Obj.Name}\".";
                    return;
                }
                // A lit fuse: water puts that out too.
                if (hit.Obj.Type == ObjectType.Explosive && hit.Instance.FuseRuntime >= 0 && liquid?.PutsOutFires == true)
                {
                    hit.Instance.FuseRuntime = -1;
                    hit.Instance.IsLitRuntime = false;
                    RefreshObjectLook(hit.Instance);
                    SetLiquid(u, "");
                    StatusText.Text = $"You douse the fuse of \"{hit.Obj.Name}\".";
                    return;
                }
            }

            // Anything else - the floor: spilt (useBucketOnGround).
            if (empty) { AvatarBark($"The {name} is empty."); return; }
            PlaySound(u.Obj.Liquid.PourSoundResourceId);
            SetLiquid(u, "");
            var (sx, sy) = LastTargetCell();
            if (u.Obj.Liquid.SpillObjectId >= 0) SpawnObjectAt(u.Obj.Liquid.SpillObjectId, 1, sx, sy);
            StatusText.Text = $"You empty \"{u.Obj.Name}\".";
        });
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
