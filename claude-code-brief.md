# Sandcastle RPG Maker — Interaction Subsystem Rewrite Brief

## Project
C#/.NET 8 WPF editor + runtime for a Windows RPG construction set (Ultima 7 /
Exult-style games). Solution file: `SandcastleRpgMaker.sln` in this folder.
Runtime/engine logic for Game Test lives mainly in `Editor/GameTestWindow.xaml.cs`
(a large file). Container editing UI is in `Editor/ContainerEditorWindow.xaml(.cs)`.
Shared data models are in the `Engine/` project (e.g. `ContainerModel.cs`,
`InventoryModel.cs`, `CharacterModel.cs`).

## Why this rewrite
Over many incremental patches, the interaction/drag-drop system accumulated a
scattered web of loosely-related mutable fields representing "what's being
dragged and from where" — `_dragItem`, `_dragObject`, `_dragVisual`,
`_dragOriginGump`, `_dragOriginSlot`, `_dragOriginContainerGump`,
`_pendingItemDragGump`, `_pendingSlotDragGump`, `_movedDuringDrag`, etc. — each
one added to fix one bug. The same *category* of bug (stale drag state from an
abandoned interaction silently winning over the current one — an item
vanishing, reappearing inside the wrong container, or replacing an unrelated
drop) kept resurfacing in new forms even after real, verified fixes. The
diagnosis: the architecture itself needs consolidating, not another patch.

## Scope — rewrite ONLY this
- Single-click (identify) vs. double-click (use/open) detection, for items on
  the map, in the paperdoll, and inside open containers.
- Drag-and-drop: picking an item up (from the map, a paperdoll slot, the
  backpack list, or an open container), moving it, and dropping it onto any
  valid target (map tile, open container gump, paperdoll gump, player avatar,
  nested container icon).
- Container interaction: opening/closing container gumps, containers nested
  inside containers, a container equipped in a paperdoll slot (including
  custom slots), moving items between two open containers, dropping an item
  from a container onto the map and vice versa.
- The revert-on-failed-drop logic (what happens when a drop target rejects an
  item, or the drag never moved far enough to count).

## Explicitly OUT of scope — do not touch
- Camera following / viewport scrolling.
- Player movement/walking, input handling for movement.
- Viewport scaling (`ViewportScale`, window-resize magnification).
- Climbing/falling/elevation (`_playerZ`, `_playerVisualLiftZ`,
  `ComputePlayerStandingZ`, `IsTileWalkable`, `MaxStepUpHeight`,
  `TryFindReachableClusterAt`).
- Collision detection and the footprint-overlap stacking system
  (`SettleCell`, `ClusterByFootprintOverlap`, `FootprintsOverlap`,
  `StackEntry`).
These are considered stable for now ("okay with it so far") and are a
separate future task — leave them exactly as they are.

## Design direction
1. **One consolidated drag-state object**, not a dozen scattered fields.
   Something like a single `DragState` class holding: what's being dragged
   (a discriminated union/tagged reference — map item, map object, or an
   inventory/container item instance), where it visually lives right now,
   and **one real origin record** (an enum/tag for "map", "paperdoll slot",
   "backpack", "container", plus the one reference needed to revert to that
   origin) — not three separate nullable origin fields that have to be kept
   in sync by hand at every call site.
2. **One subscribe, one unsubscribe, guaranteed.** The current bug class came
   from `+=` event-handler subscriptions never being guaranteed to pair with
   a corresponding `-=` before the next drag starts. The rewrite should make
   double-subscription structurally impossible — e.g. subscribe once at
   startup and gate behavior entirely on drag-state content, rather than
   subscribing/unsubscribing per-drag at all.
3. **Build the actual *rules* from Exult's real design**, verified against
   real source/discussion where possible, not guessed:
   - Confirmed from real Exult source (`actors.cc`, `Shape_manager::paint_shape`
     in `shapeid.h`) and real developer discussion: the height/"lift" model
     (an object's height = how tall it is, lift = where it sits), gump
     hit-testing is checked before falling back to the map for a drop, and
     shapes/gumps are logically unscaled individually — the whole screen is
     one scaled unit.
   - If you can get real network/source access in this environment, actual
     Exult source for `gumps.cc`, `drag.cc`, `objstack.cc`, `contain.cc` is
     the authoritative reference for exact interaction rules (what a single
     click vs. double click does, what makes a valid drop target, container
     volume/fit checks). Where you can't verify directly, say so explicitly
     rather than presenting a guess as confirmed.
4. **Single source of truth for container volume.** A recurring, separate
   bug class was volume/fit checks being reimplemented ad hoc per call site
   (e.g. a dragged container's volume resolving via the wrong lookup table
   and silently defaulting to zero). There should be exactly one method that
   resolves an item's volume by category, used everywhere.
5. Don't break what already works on top of this: existing container data
   (`ContainerData.Volume`, `MaxVolume`, `StartingContents`,
   `PreferredEquipSlot`/`PreferredCustomSlotName`), the paperdoll's built-in
   AND custom slots (`CustomEquipmentSlot`, `CharacterDefinition.
   CustomEquippedSlots`), and the editor-side UI for all of these should
   keep working against the new interaction code without changes to their
   own data model.

## Practical notes for this session
- No compiler was available in the environment this was previously built in
  — every change was verified by brace-balance checking only. You have a
  real `dotnet build` here: use it after every meaningful change.
- Read `Editor/GameTestWindow.xaml.cs` in full before deleting anything —
  it's large, and the interaction code is interleaved with code that must
  NOT be touched (see "explicitly out of scope" above).
- Ask before deleting large blocks if you're not fully sure a block is
  interaction-only vs. shared with something in scope above.
