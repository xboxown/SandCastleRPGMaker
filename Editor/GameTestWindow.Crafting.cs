using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Growing, harvesting and making things, modelled on Exult's own item
/// usecode (content/bgkeyring/src):
///  - Plants grow a stage every so many game hours. Ripe, they're harvested
///    by hand or with the tool they need: the produce appears where the
///    plant is (as Ultima VII's crops are objects you pick up), and the
///    plant goes back a stage - or is taken whole.
///  - Tools (items/hammer.uc, shears.uc...): double-click, then click what
///    to use them on - a plant, a crafting station, or anything whose By
///    Tool Used event wants it.
///  - Crafting stations (items/wheat.uc's millstone): the first recipe the
///    party has the ingredients for (and the tool, if it needs one) is made:
///    the ingredients are used up, the work plays out (its sound, the
///    station's working sprite), then what it makes appears on the station
///    ("createFlour": on the millstone). With nothing to make, the party
///    says its hint ("Try grinding wheat upon this millstone.").
///  - Musical instruments: a tune, notes over the player, their pose.
/// </summary>
public partial class GameTestWindow
{
    // ================= Plants =================

    /// <summary>Each game minute's growth: a stage gained every Hours Per Stage, until ripe.</summary>
    private void GrowPlants(double minutes)
    {
        foreach (var plant in _map.Objects.Where(o => o.ContainerInstanceId < 0).ToList())
        {
            if (DefinitionOf(plant) is not { Type: ObjectType.Plant } obj || obj.Plant.HoursPerStage <= 0) continue;
            int last = Math.Max(0, obj.Plant.Stages.Count - 1);
            if (plant.FrameRuntime >= last) continue;
            plant.GrowMinutesRuntime += minutes;
            double perStage = obj.Plant.HoursPerStage * 60;
            bool grew = false;
            while (plant.GrowMinutesRuntime >= perStage && plant.FrameRuntime < last)
            {
                plant.GrowMinutesRuntime -= perStage;
                plant.FrameRuntime++;
                grew = true;
            }
            if (!grew) continue;
            RefreshObjectLook(plant);
            FireObjectEvent(plant, obj, BookTriggerConditionBased.ByStay); // By Grown
        }
    }

    /// <summary>Double-click (toolKind null) or a tool used on a plant: harvest it if it's ripe and the right tool is used.</summary>
    private void UsePlant(ObjectInstance plant, WorldObject obj, string? toolKind)
    {
        if (toolKind is null && FireObjectEvent(plant, obj, BookTriggerConditionBased.ByUseItem)) return;
        var p = obj.Plant;
        if (p.ProduceObjectId < 0) { StatusText.Text = $"\"{obj.Name}\" - nothing happens."; return; }
        string needs = p.HarvestToolKind.Trim();
        if (needs.Length > 0 && !string.Equals(needs, toolKind?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            PartySay($"Perhaps we could use a {needs.ToLowerInvariant()} on that.");
            StatusText.Text = $"You need a {needs.ToLowerInvariant()} to harvest \"{obj.Name}\".";
            return;
        }
        int last = Math.Max(0, p.Stages.Count - 1);
        if (p.OnlyWhenRipe && plant.FrameRuntime < last) { StatusText.Text = $"\"{obj.Name}\" isn't ripe yet."; return; }
        if (ObjectOwner(obj) is { } owned) Theft(owned.Owner, owned.FactionId); // someone else's crop
        PlaySound(p.HarvestSoundResourceId);
        var (x, y) = (plant.X, plant.Y);
        if (p.RemovedWhenHarvested) RemoveMapObject(plant);
        else
        {
            plant.FrameRuntime = Math.Clamp(p.StageAfterHarvest, 0, last);
            plant.GrowMinutesRuntime = 0;
            RefreshObjectLook(plant);
        }
        SpawnObjectAt(p.ProduceObjectId, Math.Max(1, p.ProduceQuantity), x, y);
        string produce = ObjectDef(p.ProduceObjectId)?.Name ?? "something";
        StatusText.Text = $"You harvest {(p.ProduceQuantity > 1 ? $"{p.ProduceQuantity} " : "")}\"{produce}\".";
        if (_map.Objects.Contains(plant)) FireObjectEvent(plant, obj, BookTriggerConditionBased.ByAfterReadItem); // By Harvested
    }

    // ================= Tools =================

    private void UseTool(Used u)
    {
        if (FireUseEvent(u)) return;
        string kind = u.Obj.Tool.ToolKind.Trim();
        BeginChoosingTarget($"Use \"{u.Obj.Name}\" on what?", target =>
        {
            if (!ToolStillThere(u.Source) || ObjectTargeted(target) is not { } hit) return;
            PlaySound(u.Obj.Tool.UseSoundResourceId);
            ShowPlayerAtWork(600);
            switch (hit.Obj.Type)
            {
                case ObjectType.Plant when hit.Obj.Plant.HarvestToolKind.Trim().Length > 0:
                    UsePlant(hit.Instance, hit.Obj, kind);
                    return;
                case ObjectType.CraftingStation:
                    UseStation(hit.Instance, hit.Obj, kind);
                    return;
            }
            if (!FireObjectEvent(hit.Instance, hit.Obj, BookTriggerConditionBased.ByTradeItem)) // By Tool Used On It
                StatusText.Text = $"Using \"{u.Obj.Name}\" on \"{hit.Obj.Name}\" does nothing.";
        });
    }

    /// <summary>The player's work frames for a moment (Exult: actor frame ready / standing while working).</summary>
    private void ShowPlayerAtWork(double milliseconds)
    {
        if (PoseByName("Attacking Melee") is { } working)
        {
            ApplyPoseSprite(working);
            _playerPoseResetMs = milliseconds;
        }
    }

    // ================= Crafting stations =================

    private sealed class CraftingJob
    {
        public required ObjectInstance Station;
        public required WorldObject Obj;
        public required CraftingRecipe Recipe;
        public double RemainingMs;
        public double NextWorkFrameMs;
    }

    private readonly List<CraftingJob> _craftingJobs = new();

    /// <summary>Double-clicked (toolKind null) or a tool used on it: make the first recipe the party can.</summary>
    private void UseStation(ObjectInstance station, WorldObject obj, string? toolKind)
    {
        if (toolKind is null && FireObjectEvent(station, obj, BookTriggerConditionBased.ByUseItem)) return;
        if (_craftingJobs.Any(j => ReferenceEquals(j.Station, station))) { StatusText.Text = $"\"{obj.Name}\" is busy."; return; }
        string kind = toolKind?.Trim() ?? "";
        var candidates = obj.Crafting.Recipes.Where(r => string.Equals(r.ToolKind.Trim(), kind, StringComparison.OrdinalIgnoreCase) && r.OutputObjectId >= 0).ToList();
        var recipe = candidates.FirstOrDefault(PartyHasIngredients);
        if (recipe is null)
        {
            if (obj.Crafting.Hint.Trim().Length > 0) PartySay(obj.Crafting.Hint.Trim());
            StatusText.Text = candidates.Count == 0 ? $"Nothing to make on \"{obj.Name}\" that way."
                : $"You don't have what it takes: {DescribeIngredients(candidates[0])}.";
            return;
        }
        foreach (var input in recipe.Inputs.Where(i => i.ObjectId >= 0)) TakeFromParty(input.ObjectId, Math.Max(1, input.Quantity));
        RefreshAfterMove();
        station.IsLitRuntime = true; // working: its working sprite
        RefreshObjectLook(station);
        PlaySound(recipe.WorkSoundResourceId);
        _craftingJobs.Add(new CraftingJob { Station = station, Obj = obj, Recipe = recipe, RemainingMs = recipe.WorkSeconds * 1000, NextWorkFrameMs = 800 });
        ShowPlayerAtWork(400);
        StatusText.Text = $"You set to work on \"{obj.Name}\"...";
    }

    private void UpdateCraftingWork(double elapsedMs)
    {
        foreach (var job in _craftingJobs.ToList())
        {
            job.RemainingMs -= elapsedMs;
            if ((job.NextWorkFrameMs -= elapsedMs) <= 0 && job.RemainingMs > 0)
            {
                job.NextWorkFrameMs = 800; // Exult's millstone: three turns, each with its sound
                PlaySound(job.Recipe.WorkSoundResourceId);
                ShowPlayerAtWork(400);
            }
            if (job.RemainingMs > 0) continue;
            _craftingJobs.Remove(job);
            if (!_map.Objects.Contains(job.Station)) continue;
            job.Station.IsLitRuntime = false;
            RefreshObjectLook(job.Station);
            SpawnObjectAt(job.Recipe.OutputObjectId, Math.Max(1, job.Recipe.OutputQuantity), job.Station.X, job.Station.Y);
            string made = ObjectDef(job.Recipe.OutputObjectId)?.Name ?? "something";
            StatusText.Text = $"\"{job.Obj.Name}\" has made {(job.Recipe.OutputQuantity > 1 ? $"{job.Recipe.OutputQuantity} " : "")}\"{made}\".";
            FireObjectEvent(job.Station, job.Obj, BookTriggerConditionBased.ByAfterReadItem); // By Made Something
        }
    }

    private string DescribeIngredients(CraftingRecipe recipe) => string.Join(", ", recipe.Inputs.Where(i => i.ObjectId >= 0)
        .Select(i => $"{(i.Quantity > 1 ? $"{i.Quantity} " : "")}{ObjectDef(i.ObjectId)?.Name ?? "?"}"));

    // ================= What the party carries =================

    /// <summary>Everything the main character and the party carry, at any depth, with the list each one is in.</summary>
    private IEnumerable<(InventoryItemInstance Item, List<InventoryItemInstance> List, CharacterDefinition Carrier)> PartyItems()
    {
        IEnumerable<(InventoryItemInstance, List<InventoryItemInstance>)> Walk(List<InventoryItemInstance> list)
        {
            foreach (var item in list.ToList())
            {
                yield return (item, list);
                foreach (var inner in Walk(item.Contents)) yield return inner;
            }
        }
        foreach (var member in AllLivingCharacters().Where(IsPartyOrPlayer).ToList())
            foreach (var (item, list) in Walk(member.Inventory))
                yield return (item, list, member);
    }

    private int PartyCount(int objectId) =>
        PartyItems().Where(p => p.Item.Category == ReferencedItemCategory.Object && p.Item.ItemId == objectId).Sum(p => Math.Max(1, p.Item.Quantity));

    private bool PartyHasIngredients(CraftingRecipe recipe) =>
        recipe.Inputs.Where(i => i.ObjectId >= 0).GroupBy(i => i.ObjectId).All(g => PartyCount(g.Key) >= g.Sum(i => Math.Max(1, i.Quantity)));

    /// <summary>Takes this many of an object from what the party carries (stacks first shrink, then go).</summary>
    private void TakeFromParty(int objectId, int quantity) => TakeFrom(PartyItems().ToList(), objectId, quantity);

    private void TakeFrom(List<(InventoryItemInstance Item, List<InventoryItemInstance> List, CharacterDefinition Carrier)> items, int objectId, int quantity)
    {
        foreach (var (item, list, carrier) in items)
        {
            if (quantity <= 0) return;
            if (item.Category != ReferencedItemCategory.Object || item.ItemId != objectId) continue;
            int have = Math.Max(1, item.Quantity);
            if (have > quantity) { item.Quantity = have - quantity; return; }
            quantity -= have;
            list.Remove(item);
            foreach (var slot in carrier.EquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) carrier.EquippedSlots.Remove(slot);
            foreach (var slot in carrier.CustomEquippedSlots.Where(kv => kv.Value == item.InstanceId).Select(kv => kv.Key).ToList()) carrier.CustomEquippedSlots.Remove(slot);
        }
    }

    // ================= Musical instruments =================

    private void UseInstrument(Used u)
    {
        if (FireUseEvent(u)) return;
        var songs = u.Obj.Instrument.SongSoundIds;
        if (songs.Count == 0) { StatusText.Text = $"\"{u.Obj.Name}\" - nothing happens (no tunes set)."; return; }
        PlaySound(songs[Rng.Next(songs.Count)]);
        var player = UserOf(u);
        if (player is not null) ShowFloatingText(player, "♪ ♫ ♪", 3000);
        if (player == _player && u.Obj.Instrument.PlayingPose.Length > 0 && PoseByName(u.Obj.Instrument.PlayingPose) is { } pose)
        {
            ApplyPoseSprite(pose);
            _playerPoseResetMs = 3000;
        }
        StatusText.Text = $"You play \"{u.Obj.Name}\".";
    }
}
