---
name: u8-exclusive-features
description: "b511-512 (2026-11-05) U8-exclusive features from Pentagram, user's replies in issues.odt; open questions on bursting, level cap, items 15-21; user's rule to prefer commands over editor changes"
metadata:
  node_type: memory
  type: project
  originSessionId: 6db52882-69e2-4bac-8341-a1d83cc1d49d
  modified: 2026-11-05T21:07:26.745Z
---

Compared Pentagram (ScummVM engines/ultima/ultima8) with our U8 engine; user answered in issues.odt 2026-11-05.

**User's rule:** missing features go in as commands where a command can do it, else update the editor. Feign Death, Withstand Death, Changeling and the ritual commands are in BOTH U7 and U8 engines by the user's later messages. Files: {GameTestWindow,U8GameWindow}.Toughness.cs, U8GameWindow.U8Reactions.cs.

Done: b511 both engines (damage types with per-character response, getting back up, Feign/Withstand as commands + spell effects, Changeling, Find/Delete Objects In Area, Items Inside, Item Charges, Hurt In Area, 5 Advance IF conditions incl. In the party; Character killed bug: dead NPC who left a body wasn't found). b512 U8 only (Stats grow by use, eating, stumble, knocked down, hang, keep balance, airwalk jump, Build Up Stat).

Run proved: every command and condition above, feign stops attacks on both engines, stumble on U8, stat gains and caps, healing. NOT run: hang, keep balance, knocked down, airwalk (need heights + art), eating's HP back, a stat gain from a real climb/swing, the editor controls by hand, save/load of a rising monster.

b515 (2026-11-05): user's design for bursting: the MAIN BODY stays as always (looted, too heavy or not); the pieces are Object type JUNK that really fly and bounce and can be picked up (both engines, Burst.cs). Items 15-21 and Crusader features in U8 mode (U8Extras.cs): Move Platform, Weight On, Flip Screen (U8's inverter is a vertical flip), Speak, Reading Screen, Keypad, Computer, Recharge, Alarm; energy shield, surrender, difficulty by a variable; triggers ByLook/ByGotHit/ByCast/ByEnterView/ByLeaveView/ByStolen. Crusader's controlling/moves/reticle were added in b521 (below). Bomb runs and turrets are doable with existing commands and missile eggs.

Run proved b515: pieces on both engines, platform, weight + condition, flip, speak, reading screen, keypad, computer, recharge, alarm, shield, surrender. NOT run: knock an object flying by a real hit, the six new triggers, difficulty, the new editor controls by hand.

b517-519 done: platforms stop at ground (Guide 27); real water/lava/falls for every character (U8Liquids.cs); locked groups Water Splash, Lava Flare; picker tabs Areas + Crusader; Guides 28-30 with 37 pictures (made by a WPF tool rendering editor windows to PNG, scratchpad shots/). b520 DONE: pets in BOTH engines (the user's explicit exception to U8-only): Call Character, Order On A Target, Sniff And Lead ({engine}.Pets.cs); orders otherwise are existing commands on the pet's Talk replies (Follow, Attack, Attack Object, Pickup Item, Play Pose); Guide 31. Run proved call + sniff + waiting on both; Order On A Target needs a real click (not driven). C port of pets: Saturday list.

b516 (user's decision 2026-11-05): under Stats grow by use, experience stays on: kills give EXP and raise Level by the graph, up to Most Level (LevelLock; events can't pass it either), no training points. Graph stays on the Level tab under the caps. Max level is 100 now. Run proved on U8.

Test tips: a test project's character own Event pages keep it busy (clear them). C: drive was at 0 free: set TEMP/TMP to a folder on H: for runs. Debug Log also writes to SANDCASTLE_PERF_LOG. Related: [[u8-prefab-rule]], [[saturday-c-port-list]], [[u8-tiptoe-mouse-only]].

b521 DONE (2026-11-05, U8 only, U8GameWindow.U8Crusader.cs + .U8Party.cs): Tab/Home keys switch control between JOINED party members only; Control Character (any NPC, Lock: keys can't switch away) / Return Control commands; the player-swap puts the old one on the map as a MapCharacter, saves are written with the main controlled + ControlledCharacterIndex; death of controlled or main returns control; Max party members setting + Verbose "Party is full"; party climbs after the player (leaps recorded on climb/jump/drop, follower walks to the foot, waits till the player is off the landing, climbs); rolls (no hits), side-steps, kneel (shots over 1 in 2); crosshair (locked Crosshair group, CrosshairGuideWindow), Attack/NextTarget keys; condition state "Controlled by the player". Guide 32. Run proved all of it incl. a real climb onto tables with Peter following; NOT run: rolls (Ctrl+key can't be posted), drop-down follow, load of a save made while controlling, crosshair sprite art. Found and fixed: CharacterDefinitionFor(id) never found the player (Turn To Face / Walk To Location by the main's number failed) - fixed in U8 (b521) and U7 (b522, user asked).
b523 (2026-11-05): U8 talk stays a bark, never pauses; portrait OFF by default (System Settings > Ultima 8 > Conversations: None / Beside the words / In a corner - the main's Face while answering), Face frames animate (talking head); per-character BarkColor/BarkFont on the Character Editor's TALK tab (user's choice, not Verbose; tab opens for the main in U8 for this only); Talk pose while speaking. Fixed: U8 IsInSight used the U7 camera test, so U8 speakers' lines were dropped ("isn't in sight"). User asked 2026-11-05 whether U8 is complete to move on to U6: answered yes for now. Guide 33.
