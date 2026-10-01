# Advance If ("aiff") — Design Document

**Status: PAUSED for brainstorming.** Nothing has been implemented yet — no
enum value, no picker button, no command window, no interpreter support.
This document exists so ideas accumulate correctly across sessions instead
of getting rebuilt from memory (or forgotten) each time we come back to it.

Syntax keyword, once built: `aiff` (in place of `if`).

---

## Core Philosophy: Grow On Demand, Not Speculatively

- A condition gets added to the list only once we've identified a **real,
  concrete need** for it — not because "it might be useful someday."
- Whenever a new system gets built elsewhere in the project (NPC editor,
  Weapon Editor, Faction reputation, Map Editor, etc.), come back here and
  ask: *does this system need a new Advance If condition?* We don't try to
  anticipate everything up front.
- The list is expected to grow across many sessions — possibly to 60+
  categories eventually, per the original estimate. That's fine and
  expected, not scope creep, **as long as every entry maps to a real need
  at the time it's added.**
- Corollary: if we ever catch ourselves adding a condition "just in case,"
  that's the signal to stop and not add it yet.

---

## UI Shape (RPG Maker-style)

- A popup window, similar in spirit to Add Command Picker, but with real
  depth behind it.
- **Tabbed and paginated** — multiple pages, each page a *category* of
  related conditions (e.g. "Switches & Variables" today; later "NPC,"
  "Weapons," "Faction," "Theft," etc. as those systems come online).
- Within a page: a list of individual conditions, each behind a **radio
  button** — only one selectable per pick. This matches Complex
  Condition's existing model, where each node is exactly one
  sub-condition.
- Picking one condition and confirming produces **one sub-condition
  node** — same as picking one row already does in Complex Condition.
- Chaining is identical to Complex Condition's existing And/Or/Xor
  grouping: arbitrarily many of these single-picks, combined and nested
  with And/Or/Xor, no theoretical limit. This is *why* only one condition
  can be picked at a time from the huge list — the combining power comes
  from chaining single picks together, not from multi-selecting within a
  page.

---

## Architecture Decisions Already Made

(From the discussion before this pause — not yet built, but agreed on.)

1. **Reuse the Complex Condition engine, don't duplicate it.** Same
   recursive And/Or/Xor evaluator, same grouping UI pattern, extended with
   new sub-condition *types* rather than a parallel system. One evaluator
   to maintain, one place to add a new condition type, benefits both
   Advance If and page-level Complex Conditions at once.

2. **Most conditions fit a generic shape:** *(target) → (attribute) →
   (comparator) → (value)*. "NPC X's HP >= 50" and "Player's Reputation
   with Faction Y <= -10" are the same shape with a different target and
   attribute. Once a target type's backing system exists, adding a new
   *attribute* to it should be cheap — a new option in a list, not a new
   UI panel each time.

3. **Some conditions won't fit that generic shape.** Anything that's
   really a historical/boolean event flag — "did you steal something,"
   "did you get caught," "did NPC X join the party" — needs its own
   dedicated flag-style sub-condition type. Don't force these into
   target/attribute/comparator/value just for consistency.

4. **What's real today vs. what's foundation for later:**
   - **Real, evaluable today:** Switches, Variables, Stats — all exist as
     real systems already, so Advance If conditions built on these could
     genuinely evaluate for real from day one, no simulation needed.
   - **Foundation only, Debug-simulated until the real system exists:**
     anything about NPCs, Weapons, Armor, Items-in-inventory, Faction
     reputation, Theft/crime state, party membership. Same honest
     simulate-until-real pattern used everywhere else in this project
     (Stat Condition, Combine Item, Drop To Self, etc.).

---

## Open Questions (not yet decided)

- Exact tab/page navigation mechanics for potentially 60+ category pages —
  a simple list of tabs won't scale visually that far. Needs its own
  design pass once we have a real sense of how many categories exist.
- Should picking a "target" (which specific NPC, which specific faction)
  be its own nested picker step, separate from picking the attribute? Or
  bundled into one dialog per condition?
- Does every category page share one common layout, or do some categories
  need a genuinely different authoring UI (e.g. a stat comparison looks
  different from a boolean flag check)?

---

## Category & Condition Backlog

The actual growing list. Add an entry here only once a real need is
identified — this is not a wishlist to pad preemptively.

### Switches, Variables & Stats — real today
- [ ] Switch is On / Off
- [ ] Variable comparison (==, !=, >, >=, <, <=)
- [ ] Stat comparison (references a Stat definition)

*(Everything else: to be added here as real needs are identified in
future sessions.)*

---

## Session Log

- **This session:** Paused implementation to write this plan, right after
  finishing real nested-If indentation (DEBUG-BUILD-63, confirmed
  working). No Advance If code exists anywhere yet.
