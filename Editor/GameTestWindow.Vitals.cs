using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Health, food, poison and ailments for every character (the player and
/// all placed characters alike), ported from Exult and then changed where
/// asked:
///  - Each game hour (Game_clock::handle_event): mend_npcs - 1 HP back
///    (1 to Strength/3 asleep in a Sleep schedule) unless poisoned or
///    starving - then use_food: 0..3 food used (Character Editor > Health).
///  - Each game minute at food 0 (Npc_hunger_timer): a 2-in-5 chance of
///    losing 1 HP. Unlike Exult this kills, and applies to everyone.
///  - Poison (Npc_poison_timer): first check after 5 s, then 0..2 HP every
///    10-20 s until it wears off (60-180 s) or is cured. Here it can kill.
///  - Asleep 5-10 s; paralyzed / charmed / cursed 60-120 s (Npc_flag_timer).
///    Asleep or paralyzed: can't move or act.
///  - Tile effects (Tile Properties > custom effects): damage on entering,
///    staying (ramping if set) or leaving, unless a mitigating object is
///    carried - the traps, for characters exactly as for the player.
///  - Health is the Hits stat, Strength its maximum (Exult). Reaching 0 is
///    death: a character lies in its Body pose; the player's game ends.
/// All of it stands still while the game is paused.
/// </summary>
public partial class GameTestWindow
{
    private sealed class Vitals
    {
        public int Food;
        public Ailment Ailments;
        public double PoisonRemainingMs, PoisonNextTickMs, SleepRemainingMs, ParalyzeRemainingMs, CharmRemainingMs, CurseRemainingMs, InvisibleRemainingMs, ProtectedRemainingMs;
        public bool Dead;
        /// <summary>Knocked out: health at 0 or below, not yet dead (GameTestWindow.Unconscious.cs).</summary>
        public bool Unconscious;
        public double MendCheckMs;
        public double DamageCarry; // fractional tile-effect damage not yet taken
        public (int X, int Y)? Cell;
        public readonly Dictionary<CustomTileEffect, Exposure> Exposures = new(ReferenceEqualityComparer.Instance);
    }

    private sealed class Exposure
    {
        public int Ticks;
        public double TimerMs;
        public bool OnTile;
        public double RecoveryMs;
    }

    private readonly Dictionary<CharacterDefinition, Vitals> _vitals = new(ReferenceEqualityComparer.Instance);
    private long _lastClockMinute;
    private bool _gameOver;

    private void InitializeVitals()
    {
        foreach (var character in AllLivingCharacters(includeDead: true))
        {
            _vitals[character] = new Vitals { Food = Math.Clamp(character.Survival.StartingFood, 0, SurvivalSettings.MaxFood) };
            // A character with no health set at all starts at full health (its Strength), or 10. A Hits of 0 typed in is 0: knocked out from the start.
            if (HitsStat() is not null && GetHp(character) is not int) SetHp(character, MaxHp(character) > 0 ? MaxHp(character) : 10);
        }
        _lastClockMinute = TotalMinutes();
        _execContext.OnChangeFood = ChangeFoodCommand;
        _execContext.OnModifyAilment = ModifyAilmentCommand;
        _execContext.OnAttack = AttackCommand;
        _execContext.OnPaperdoll = PaperdollCommand;
        _execContext.OnSleepOnBed = SleepOnBedCommand;
        _execContext.OnExchangeCurrency = ExchangeCurrencyCommand;
        _execContext.OnChangeWeather = ChangeWeather;
        _execContext.OnEvaluateCondition = EvaluateGameCondition; // Advance IF (GameTestWindow.Conditions.cs)
        InitializeEventCommands(); // Give Item, Change Stat, Train... (GameTestWindow.EventCommands.cs)
        WatchSounds();
    }

    private IEnumerable<CharacterDefinition> AllLivingCharacters(bool includeDead = false)
    {
        if (_player is not null && (includeDead || !IsDead(_player))) yield return _player;
        foreach (var mapCharacter in _mapCharacters)
            if (includeDead || !IsDead(mapCharacter.Character)) yield return mapCharacter.Character;
    }

    private Vitals VitalsOf(CharacterDefinition character)
    {
        if (!_vitals.TryGetValue(character, out var vitals)) _vitals[character] = vitals = new Vitals { Food = character.Survival.StartingFood };
        return vitals;
    }

    private bool IsDead(CharacterDefinition character) => _vitals.TryGetValue(character, out var v) && v.Dead;
    private bool Has(CharacterDefinition character, Ailment ailment) => _vitals.TryGetValue(character, out var v) && v.Ailments.HasFlag(ailment);
    /// <summary>Can't move or act: dead, asleep or paralyzed.</summary>
    private bool IsIncapacitated(CharacterDefinition? character) =>
        character is not null && _vitals.TryGetValue(character, out var v) && (v.Dead || v.Unconscious || (v.Ailments & (Ailment.Asleep | Ailment.Paralyzed)) != 0);

    private long TotalMinutes() => ((long)_clock.Day * 24 + _clock.Hour) * 60 + _clock.Minute;

    // ---------- Health (the Hits stat; Strength is the maximum) ----------

    private StatDefinition? HitsStat() => _stats.Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.Hits);
    private StatDefinition? StrengthStat() => _stats.Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.Strength);

    private int? GetHp(CharacterDefinition character) =>
        HitsStat() is { } hits && character.StatValues.TryGetValue(hits.Id, out var text) && int.TryParse(text, out int hp) ? hp : null;

    private void SetHp(CharacterDefinition character, int hp)
    {
        if (HitsStat() is { } hits) character.StatValues[hits.Id] = hp.ToString();
    }

    private int MaxHp(CharacterDefinition character) =>
        StrengthStat() is { } str && character.StatValues.TryGetValue(str.Id, out var text) && int.TryParse(text, out int max) ? max : 0;

    private void Damage(CharacterDefinition character, int amount, string cause)
    {
        if (amount <= 0 || IsDead(character) || GetHp(character) is not int hp) return;
        FlashHit(character, amount, hp); // Exult's red outline (or, badly hurt, the red screen)
        int half = MaxHp(character) / 2;
        if (hp >= half && hp - amount < half && hp - amount > 0 && Rng.Next(2) == 0) Bark(character, VerboseKind.Hurt); // reduce_health: "A little oomph." 
        hp -= amount;
        SetHp(character, hp);
        if (character == _player) StatusText.Text = $"You lose {amount} HP ({cause}) - {hp} left.";
        // Exult: dead below -(Strength / 3); otherwise at 0 or below, knocked out.
        if (IsDying(character, hp)) Die(character, cause);
        else if (hp <= 0) KnockOut(character);
    }

    private void Die(CharacterDefinition character, string cause)
    {
        var vitals = VitalsOf(character);
        if (vitals.Dead) return;
        Record($"killed:{character.Id}"); // Character killed (GameTestWindow.Conditions.cs)
        Record("killed:*");
        StandUp(character);
        vitals.Dead = true;
        vitals.Ailments = Ailment.None;
        if (character == _player)
        {
            // The main character: no built-in message - its own By Death events decide what happens (a Show Text, music, ...).
            // From then on the player can do nothing at all (no moving, dragging, using, talking, opening or identifying);
            // only a dialog box raised by those events can still be clicked through. Loading a saved game will come later.
            ApplyPoseSprite(PoseByName("Body") ?? _idlePose);
            _heldDirectionKeys.Clear();
            CancelPointer();
            _choosingTarget = null;
            _playerTarget = null;
            _playerTargetThing = null;
            _gameOver = true;
            ShowPointer(RestingPointer);
            StatusText.Text = $"You have died ({cause}).";
            FireCharacterEvent(character, BookTriggerConditionBased.ByDeath);
            return;
        }
        FireCharacterEvent(character, BookTriggerConditionBased.ByDeath); // its own By Death events, while it's still where it fell
        var mapCharacter = _mapCharacters.FirstOrDefault(mc => mc.Character == character);
        if (mapCharacter is null) return;
        StopWalking(mapCharacter);
        mapCharacter.Npc.WalkingToSchedule = false;
        mapCharacter.Npc.Activity = null;
        mapCharacter.Npc.InCombat = false;
        LeaveBody(mapCharacter); // Exult: out of the world, a body (its Body pose) holding all it carried where it fell
        StatusText.Text = $"{character.Name} has died ({cause}).";
    }

    // ---------- Each frame / minute / hour ----------

    private void UpdateVitals(double elapsedMs, int hourChanges)
    {
        if (_gameOver) return;
        if (hourChanges > 0)
            foreach (var character in AllLivingCharacters().ToList()) { MendWounds(character); UseFood(character); }

        long minute = TotalMinutes();
        if (minute != _lastClockMinute)
        {
            _lastClockMinute = minute;
            foreach (var character in AllLivingCharacters().ToList()) HungerMinute(character);
            ExpireStatChanges(minute); // temporary Change Stats wearing off
        }

        foreach (var character in AllLivingCharacters().ToList())
        {
            UpdateAilments(character, elapsedMs);
            UpdateTileEffects(character, elapsedMs);
        }
    }

    /// <summary>Actor::mend_wounds.</summary>
    private void MendWounds(CharacterDefinition character)
    {
        var vitals = VitalsOf(character);
        bool starving = character.Survival.CanGetHungry && vitals.Food <= character.Survival.HungryAtOrBelow;
        if (vitals.Ailments.HasFlag(Ailment.Poisoned) || GetHp(character) is not int hp || (starving && hp > 0)) return;
        int max = MaxHp(character);
        if (max <= 0 || hp >= max) return;
        bool sleepingSchedule = _mapCharacters.FirstOrDefault(mc => mc.Character == character)?.Npc.Activity == ScheduleType.Sleep;
        hp += max >= 3 && !starving && sleepingSchedule ? 1 + Rng.Next(max / 3) : 1;
        SetHp(character, Math.Min(hp, max));
    }

    /// <summary>Actor::use_food.</summary>
    private void UseFood(CharacterDefinition character)
    {
        var s = character.Survival;
        if (!s.CanGetHungry) return;
        var vitals = VitalsOf(character);
        vitals.Food = Math.Clamp(vitals.Food - Rng.Next(Math.Max(0, s.FoodUsedPerHourMax) + 1), 0, SurvivalSettings.MaxFood);
        if (character == _player && vitals.Food <= s.HungryAtOrBelow)
            StatusText.Text = vitals.Food <= 0 ? "You are starving!" : "You are hungry.";
        SayHungerMessage(character, vitals.Food); // Exult's say_hunger_message
    }

    /// <summary>Npc_hunger_timer: once a game minute while hungry; at food 0, a chance of losing health.</summary>
    private void HungerMinute(CharacterDefinition character)
    {
        var s = character.Survival;
        if (!s.CanGetHungry || IsIncapacitated(character)) return;
        var vitals = VitalsOf(character);
        if (vitals.Food > s.HungryAtOrBelow) return;
        if (character == _player && Rng.Next(3) == 0) StatusText.Text = vitals.Food <= 0 ? "You are starving!" : "You are hungry.";
        if (vitals.Food <= 0 && Rng.Next(100) < s.StarvingChancePercent) Damage(character, s.StarvingDamage, "starvation");
    }

    private void UpdateAilments(CharacterDefinition character, double elapsedMs)
    {
        var vitals = VitalsOf(character);
        if (vitals.Ailments == Ailment.None) return;
        if (vitals.Ailments.HasFlag(Ailment.Poisoned))
        {
            vitals.PoisonRemainingMs -= elapsedMs;
            vitals.PoisonNextTickMs -= elapsedMs;
            if (vitals.PoisonNextTickMs <= 0)
            {
                if (character.Survival.PoisonWearsOff && vitals.PoisonRemainingMs <= 0) Cure(character, Ailment.Poisoned);
                else
                {
                    Damage(character, Rng.Next(Math.Max(0, character.Survival.PoisonDamageMax) + 1), "poison");
                    vitals.PoisonNextTickMs = 10000 + Rng.Next(10000); // check again in 10-20 s
                }
            }
        }
        if (vitals.Ailments.HasFlag(Ailment.Asleep) && (vitals.SleepRemainingMs -= elapsedMs) <= 0 && (GetHp(character) ?? 1) >= 1) Cure(character, Ailment.Asleep);
        if (vitals.Ailments.HasFlag(Ailment.Paralyzed) && (vitals.ParalyzeRemainingMs -= elapsedMs) <= 0) Cure(character, Ailment.Paralyzed);
        if (vitals.Ailments.HasFlag(Ailment.Charmed) && (vitals.CharmRemainingMs -= elapsedMs) <= 0) Cure(character, Ailment.Charmed);
        if (vitals.Ailments.HasFlag(Ailment.Cursed) && (vitals.CurseRemainingMs -= elapsedMs) <= 0) Cure(character, Ailment.Cursed);
        if (vitals.Ailments.HasFlag(Ailment.Invisible) && (vitals.InvisibleRemainingMs -= elapsedMs) <= 0) { Cure(character, Ailment.Invisible); ShowInvisibility(character); }
        if (vitals.Ailments.HasFlag(Ailment.Protected) && (vitals.ProtectedRemainingMs -= elapsedMs) <= 0) Cure(character, Ailment.Protected);
    }

    private bool Inflict(CharacterDefinition character, Ailment ailment)
    {
        if (IsDead(character) || (ailment is not (Ailment.Invisible or Ailment.Protected) && character.Survival.Immunities.HasFlag(ailment))) return false;
        var vitals = VitalsOf(character);
        vitals.Ailments |= ailment;
        var s = character.Survival;
        switch (ailment)
        {
            case Ailment.Poisoned:
                int min = Math.Max(1, s.PoisonCureMinSeconds), max = Math.Max(min, s.PoisonCureMaxSeconds);
                vitals.PoisonRemainingMs = (min + Rng.Next(max - min + 1)) * 1000.0;
                vitals.PoisonNextTickMs = 5000; // Npc_poison_timer starts 5 s later
                break;
            case Ailment.Asleep:
                vitals.SleepRemainingMs = 5000 + Rng.Next(5000);
                break;
            case Ailment.Paralyzed:
                vitals.ParalyzeRemainingMs = 60000 + Rng.Next(60000);
                break;
            case Ailment.Charmed:
                vitals.CharmRemainingMs = 60000 + Rng.Next(60000);
                break;
            case Ailment.Cursed:
                vitals.CurseRemainingMs = 60000 + Rng.Next(60000);
                break;
            case Ailment.Invisible:
                vitals.InvisibleRemainingMs = 60000 + Rng.Next(20000); // Npc_invisibility_timer
                break;
            case Ailment.Protected:
                vitals.ProtectedRemainingMs = 60000 + Rng.Next(20000); // Npc_protection_timer
                break;
        }
        if (ailment is Ailment.Asleep or Ailment.Paralyzed) StopActing(character, ailment == Ailment.Asleep);
        if (character == _player) StatusText.Text = $"You are {Ailments.Describe(ailment).ToLowerInvariant()}.";
        if (ailment == Ailment.Invisible) { StopAttackingInvisible(character); ShowInvisibility(character); }
        if (ailment is not (Ailment.Invisible or Ailment.Protected)) FireCharacterEvent(character, BookTriggerConditionBased.ByAilmentEffect);
        return true;
    }

    private void Cure(CharacterDefinition character, Ailment ailment)
    {
        var vitals = VitalsOf(character);
        if (!vitals.Ailments.HasFlag(ailment)) return;
        vitals.Ailments &= ~ailment;
        if (ailment is Ailment.Asleep or Ailment.Paralyzed && !IsIncapacitated(character)) ResumeActing(character);
        if (character == _player) StatusText.Text = $"You are no longer {Ailments.Describe(ailment).ToLowerInvariant()}.";
    }

    /// <summary>Asleep or paralyzed: stop where it stands (lying down if asleep).</summary>
    private void StopActing(CharacterDefinition character, bool asleep)
    {
        if (character == _player)
        {
            _heldDirectionKeys.Clear();
            ApplyPoseSprite(asleep ? PoseByName("Sleeping") ?? _idlePose : _idlePose);
            _wasMovingLastFrame = false;
            return;
        }
        var mapCharacter = _mapCharacters.FirstOrDefault(mc => mc.Character == character);
        if (mapCharacter is null) return;
        mapCharacter.Npc.Path = null;
        SetCharacterPose(mapCharacter, asleep ? "Sleeping" : "Idle", mapCharacter.Facing);
    }

    private void ResumeActing(CharacterDefinition character)
    {
        if (character == _player) { ApplyPoseSprite(_idlePose); return; }
        var mapCharacter = _mapCharacters.FirstOrDefault(mc => mc.Character == character);
        if (mapCharacter is null) return;
        SetCharacterPose(mapCharacter, mapCharacter.Npc.Activity == ScheduleType.Sleep ? "Sleeping" : "Idle", mapCharacter.Facing);
        mapCharacter.Npc.WaitMs = StdDelay; // carry on with whatever it was doing
    }

    // ---------- Tile effects (the traps) ----------

    private void UpdateTileEffects(CharacterDefinition character, double elapsedMs)
    {
        var vitals = VitalsOf(character);
        (int X, int Y) cell = character == _player
            ? (_playerX, _playerY)
            : _mapCharacters.FirstOrDefault(mc => mc.Character == character) is { } mc ? (mc.Placement.X, mc.Placement.Y) : (-1, -1);
        if (!InsideMap(cell.X, cell.Y)) return;
        var here = EffectsAt(cell);
        bool moved = vitals.Cell != cell;
        if (moved && vitals.Cell is { } previous)
        {
            foreach (var effect in EffectsAt(previous).Where(e => !here.Contains(e)))
            {
                if (effect.Trigger == TileEffectTrigger.OnExit) ApplyTileEffect(character, effect, 0);
                if (vitals.Exposures.TryGetValue(effect, out var left)) { left.OnTile = false; left.RecoveryMs = effect.RecoverySeconds * 1000; }
            }
        }
        vitals.Cell = cell;

        foreach (var effect in here)
        {
            if (!vitals.Exposures.TryGetValue(effect, out var exposure)) vitals.Exposures[effect] = exposure = new Exposure();
            bool entering = !exposure.OnTile;
            exposure.OnTile = true;
            if (entering && effect.Trigger == TileEffectTrigger.OnEnter) ApplyTileEffect(character, effect, 0);
            if (effect.Trigger != TileEffectTrigger.OnStay) continue;
            exposure.TimerMs += elapsedMs;
            double interval = Math.Max(0.05, effect.TickIntervalSeconds) * 1000;
            while (exposure.TimerMs >= interval && !IsDead(character))
            {
                exposure.TimerMs -= interval;
                ApplyTileEffect(character, effect, exposure.Ticks);
                exposure.Ticks++;
            }
        }
        // Away from an effect's tiles: its build-up fades once the recovery time has passed.
        foreach (var (effect, exposure) in vitals.Exposures.Where(kv => !kv.Value.OnTile).ToList())
        {
            exposure.RecoveryMs -= elapsedMs;
            if (exposure.RecoveryMs <= 0) vitals.Exposures.Remove(effect);
        }
    }

    private List<CustomTileEffect> EffectsAt((int X, int Y) cell)
    {
        var effects = new List<CustomTileEffect>();
        foreach (var layer in _map.Layers)
        {
            int tile = layer.GetTile(cell.X, cell.Y, _map.Width);
            // Looked up without creating an entry: only customized tiles have effects.
            if (tile >= 0 && _tileProperties.ByTileKey.TryGetValue($"{(_map.Tileset.SheetNameOf(tile) is { Length: > 0 } sheet ? sheet : _tilesetSpriteName)}:{TileSheets.Local(tile)}", out var props)) effects.AddRange(props.CustomEffects);
        }
        return effects;
    }

    private void ApplyTileEffect(CharacterDefinition character, CustomTileEffect effect, int ticksSoFar)
    {
        if (CarriesAnyObject(character, effect.MitigatingObjectIds)) return;
        double damage = effect.DamagePerTick + (effect.RampingDamage ? effect.RampIncreasePerTick * ticksSoFar : 0);
        if (damage <= 0) return;
        var vitals = VitalsOf(character);
        vitals.DamageCarry += damage;
        int whole = (int)Math.Floor(vitals.DamageCarry);
        vitals.DamageCarry -= whole;
        Damage(character, whole, effect.Name);
    }

    private static bool CarriesAnyObject(CharacterDefinition character, List<int> objectIds)
    {
        if (objectIds.Count == 0) return false;
        bool Search(IEnumerable<InventoryItemInstance> items) =>
            items.Any(i => (i.Category == ReferencedItemCategory.Object && objectIds.Contains(i.ItemId)) || Search(i.Contents));
        return Search(character.Inventory);
    }

    // ---------- Commands ----------

    private CharacterDefinition? CharacterDefinitionFor(string who) => who switch
    {
        WhoChoice.Player => _player,
        WhoChoice.Activator => _activator?.Character ?? _player,
        _ => _mapCharacters.FirstOrDefault(mc => mc.Character.Id.ToString() == who)?.Character
    };

    private bool ChangeFoodCommand(string who, string operation, int amount)
    {
        if (CharacterDefinitionFor(who) is not { } character || IsDead(character) || !character.Survival.CanGetHungry) return false;
        var vitals = VitalsOf(character);
        int food = operation switch { "Set" => amount, "Subtract" => vitals.Food - amount, _ => vitals.Food + amount };
        vitals.Food = Math.Clamp(food, 0, SurvivalSettings.MaxFood);
        return true;
    }

    private bool ModifyAilmentCommand(string who, string ailmentName, bool inflict)
    {
        if (CharacterDefinitionFor(who) is not { } character || !Enum.TryParse<Ailment>(ailmentName, out var ailment)) return false;
        if (!inflict) { Cure(character, ailment); return true; }
        bool done = Inflict(character, ailment);
        if (!done && character.Survival.Immunities.HasFlag(ailment)) StatusText.Text = $"{character.Name} is immune to being {Ailments.Describe(ailment).ToLowerInvariant()}.";
        return done;
    }

    /// <summary>Health, food and ailments in a few words.</summary>
    private string DescribeVitals(CharacterDefinition character)
    {
        if (IsDead(character)) return "dead";
        var vitals = VitalsOf(character);
        var parts = new List<string>();
        if (GetHp(character) is int hp) parts.Add(MaxHp(character) > 0 ? $"HP {hp}/{MaxHp(character)}" : $"HP {hp}");
        if (character.Survival.CanGetHungry)
            parts.Add($"food {vitals.Food}" + (vitals.Food <= 0 ? " (starving)" : vitals.Food <= character.Survival.HungryAtOrBelow ? " (hungry)" : ""));
        parts.AddRange(Ailments.All.Where(a => vitals.Ailments.HasFlag(a)).Select(a => Ailments.Describe(a).ToLowerInvariant()));
        return string.Join(", ", parts);
    }
}
