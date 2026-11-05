// Ultima 8 engine (U8GameWindow): the same as the Ultima 7 engine's GameTestWindow.Pets.cs (the user asked for pets in both).
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Pets, as Serpent Isle's and Ultima 8's: a creature that follows the main character without joining the party, and is talked to for
/// orders. The orders are ordinary commands (Follow / Stop Following, Attack, Attack Object, Pickup Item, Play Pose...) on its Talk replies;
/// these three are what a pet needs besides:
///  - Call Character: a whistle, a bell - from off the screen it comes to the main character (and follows, if asked).
///  - Order On A Target: the player clicks someone or something, and it attacks it, fetches it, goes to it.
///  - Sniff And Lead: it follows a scent - to a character, to where an object lies, or to an object's owner - with the main character behind.
/// </summary>
public partial class U8GameWindow
{
    // ---------- Call Character ----------

    private bool CallCharacterCommand(EventCommand c, Action<bool> done)
    {
        if (_player is null || CharacterDefinitionFor(c.Get("Who")) is not { } who || who == _player || IsDead(who) || MapCharacterOf(who) is not { } pet) return false;
        if (int.TryParse(c.Get("SoundId"), out int sound)) PlaySound(sound);
        ReleaseForOrders(pet);
        var (px, py) = CellOf(_player);
        if (!IsOnScreen(pet.PixelX, pet.PixelY))
        {
            // From off the screen: put just outside the view on the side it is on, on the main character's floor, then it walks in.
            int halfX = (int)Math.Ceiling(ViewportWidth / 2.0 / _tileWidth) + 1, halfY = (int)Math.Ceiling(ViewportHeight / 2.0 / _tileHeight) + 1;
            int dx = Math.Sign(pet.Placement.X - px), dy = Math.Sign(pet.Placement.Y - py);
            if (dx == 0 && dy == 0) dx = 1;
            var edge = (X: Math.Clamp(px + dx * halfX, 0, _map.Width - 1), Y: Math.Clamp(py + dy * halfY, 0, _map.Height - 1));
            Teleport(pet, edge);
            pet.Lift = StandingLiftOnFloor(CharacterFootprint(pet), MapFloors.FloorOfLift(LiftOf(_player)), pet.Placement);
            PositionCharacterVisual(pet);
        }
        var words = c.Get("Value").Trim();
        if (words.Length > 0) ShowFloatingText(who, words, 2500);
        bool follows = c.Get("Follows") != "False";
        var walk = WalkTo(who, (px, py), beside: true);
        if (walk is null) { if (follows) _followers[pet] = _player; return false; }
        BeginScriptAction(who, elapsed =>
        {
            var result = walk(elapsed);
            if (result is bool && follows && _mapCharacters.Contains(pet)) _followers[pet] = _player;
            return result;
        }, done);
        SoakLog($"called: {who.Name} comes");
        return true;
    }

    /// <summary>Off its timetable for orders: no schedule pulling it away (as Follow does).</summary>
    private static void ReleaseForOrders(MapCharacter pet)
    {
        pet.Npc.Entry = null;
        pet.Npc.Activity = null;
        pet.Npc.WalkingToSchedule = false;
    }

    // ---------- Order On A Target ----------

    private bool OrderOnTargetCommand(EventCommand c, Action<bool> done)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who || who == _player || IsDead(who) || MapCharacterOf(who) is not { } pet) return false;
        string order = c.Get("Order");
        string prompt = c.Get("Prompt").Trim().Length > 0 ? c.Get("Prompt").Trim() : $"{order}: which one?";
        bool answered = false;
        Action<InteractionTarget?> chosen = target =>
        {
            answered = true;
            var someone = CharacterTargeted(target);
            var thing = target is MapEntityTarget { Entity: ObjectInstance or PlacedItemInstance } m ? m.Entity : null;
            bool keepFollowing = _followers.ContainsKey(pet);
            switch (order)
            {
                case "Attack" when someone is not null && someone != who:
                    if (someone == _player) { done(false); return; }
                    StartCombat(pet, someone);
                    done(true);
                    return;
                case "Attack" when thing is not null:
                    BeginScriptAction(who, Chain(GoTo(who, thing), () => StrikeUntilBroken(who, thing)), ok => { if (keepFollowing) _followers[pet] = _player!; done(ok); });
                    return;
                case "Fetch" when thing is not null:
                    BeginScriptAction(who, Chain(GoTo(who, thing), PoseOnce(who, "Picking"), Do(() => PickUp(who, thing))), ok => { if (keepFollowing) _followers[pet] = _player!; done(ok); });
                    return;
                case "Go to" when someone is not null || thing is not null:
                {
                    var cell = someone is not null ? CellOf(someone) : CellOfThing(thing!);
                    if (WalkTo(who, cell, beside: true) is not { } walk) { done(false); return; }
                    BeginScriptAction(who, walk, done);
                    return;
                }
            }
            done(false); // nothing it can do that to
        };
        BeginChoosingTarget(prompt, chosen);
        _scriptWaits.Add((_ => answered ? true : ReferenceEquals(_choosingTarget, chosen) ? null : false, ok => { if (!ok && !answered) done(false); })); // Escape: no order
        return true;
    }

    // ---------- Sniff And Lead ----------

    /// <summary>Where the scent leads now: a character, the nearest placed copy of an object, or the owner of an object (its Belongs To). Null: nowhere.</summary>
    private (int X, int Y)? ScentAt(EventCommand c, CharacterDefinition sniffer)
    {
        int.TryParse(c.Get("ItemId"), out int itemId);
        switch (c.Get("Find"))
        {
            case "A character (below)":
                return CharacterDefinitionFor(c.Get("Other")) is { } other && other != sniffer && !IsDead(other) ? CellOf(other) : null;
            case "The owner of the object (below)":
                if (ObjectDef(itemId) is not { BelongToId: >= 0 } owned) return null;
                var owner = _player?.Id == owned.BelongToId ? _player : _mapCharacters.FirstOrDefault(mc => mc.Character.Id == owned.BelongToId)?.Character;
                return owner is null || IsDead(owner) ? null : CellOf(owner);
            default:
            {
                var (sx, sy) = CellOf(sniffer);
                var cells = _map.Objects.Where(o => o.ContainerInstanceId < 0 && o.ObjectId == itemId).Select(o => (o.X, o.Y))
                    .Concat(_map.Items.Where(i => i.ItemId == itemId && i.Category.ToString() == c.Get("Category")).Select(i => (i.X, i.Y))).ToList();
                return cells.Count == 0 ? null : cells.OrderBy(p => Math.Abs(p.Item1 - sx) + Math.Abs(p.Item2 - sy)).First();
            }
        }
    }

    private bool SniffAndLeadCommand(EventCommand c, Action<bool> done)
    {
        if (_player is null || CharacterDefinitionFor(c.Get("Who")) is not { } who || who == _player || IsDead(who) || MapCharacterOf(who) is not { } pet) return false;
        if (ScentAt(c, who) is null) { ShowFloatingText(who, c.Get("Lost").Trim().Length > 0 ? c.Get("Lost").Trim() : "...", 2000); return false; }
        bool keepFollowing = _followers.Remove(pet);
        ReleaseForOrders(pet);
        double waitCells = Math.Max(2, FormulaNumber(c, "WaitCells")), giveUpMs = Math.Max(5, FormulaNumber(c, "Seconds")) * 1000, thinkMs = 0, elapsedAll = 0;
        Func<double, bool?>? walk = null;
        bool waiting = false;
        BeginScriptAction(who, elapsed =>
        {
            elapsedAll += elapsed;
            if (elapsedAll > giveUpMs) return false;
            if (walk is not null && walk(elapsed) is bool) walk = null;
            if ((thinkMs -= elapsed) > 0) return null;
            thinkMs = 500;
            if (ScentAt(c, who) is not { } goal) return false; // the scent went cold
            var at = CellOf(who);
            if (CellsApart(at, goal) <= 1) { FaceCell(who, goal.X, goal.Y); return true; } // there
            bool behind = CellsApart(at, CellOf(_player)) > waitCells;
            if (behind)
            {
                // The main character fell behind: it stops, sniffs and waits.
                if (!waiting) { StopWalking(pet); walk = null; waiting = true; if (HasArtFor(who, "Sniffing")) ShowPose(who, "Sniffing", restart: true); }
                return null;
            }
            waiting = false;
            walk ??= WalkTo(who, goal, beside: true);
            return null;
        }, ok =>
        {
            if (keepFollowing && _mapCharacters.Contains(pet)) _followers[pet] = _player;
            SoakLog($"sniffed: {who.Name} {(ok ? "found it" : "lost the scent")}");
            done(ok);
        });
        return true;
    }
}
