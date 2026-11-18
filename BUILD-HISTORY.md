# Build history

One line per build, newest first. Added with every build's commit (see CLAUDE.md). Older builds: `git log`.

- **b617** (2026-11-17) - Magic weapons and armor, U6/U7/U8 engines, Windows and the C player: Object Editor > Weapon / Armor > Magic (reach in cells, defense bonus, stat bonuses + or - on any stat incl. custom ones - made on equip, taken back exactly on unequip, kept on the character in saves - never-ending light, immunities, Cursed with its own words, On Equip / On Unequip common events) and 8 commands on the new Equipment tab: Curse Item, Lift Curse, Is Item Cursed, Enchant Item, Remove Enchantments, Get Magic Bonus, Get Equipped Item, Combine Items. Cursed worn items refuse drag / Unequip / Drop / Give / Remove. CommandList.docx Guide 40 + test list. Rebuilt: Amiga 68k, Vampire, MorphOS, AmigaOS 4, PS2, PS3, Mac (CI); Ubuntu needs a root build. Rules unit-tested; not yet play-tested.
- **b616** (2026-11-17) - Amiga Vampire: the CRT effect (System Settings > Game Window) made while doubling to SAGA's 640 x 400, as Windows makes it (crt_double: each game row a bright line and one 45% darker, pixels softened) - real 400-line scanlines; before, the Amiga's in-place CRT darkened every other game row at 320 x 200. Built, not yet run on a Vampire.
- **b615** (2026-11-17) - Amiga Vampire speed: it ran at 8-9 fps on the user's V4 Firebird because it drew the whole game at 640 x 400 (four times the 68k build's pixels). Now drawn at 320 x 200 like the Amiga 68k and doubled on the way to SAGA's own 640 x 400 screen (AMMX makes each row 5-6-5, each pixel written twice, the row copied below); fonts for 320 x 200. Built, not yet run on a Vampire.
- **b614** (2026-11-17) - Amiga Vampire: crashed at once with guru 8000000A on the user's V4 Firebird - gcc's -m68080 filled the program with the 68080's extra A-line instructions (8642 of them), which that core doesn't run. Now compiled as 68060 code (as the Amiga 68k build); AMMX, SAGA's display and sound unchanged. A-line instructions checked gone by disassembly.
- **b613** (2026-11-17) - AmigaOS 4.1 port: the Sandcastle player built for OS4 (PowerPC, GCC 11 adtools, newlib, SDL2 built in - nothing to install), Player/build_amigaos4.sh; AmigaOS 4.x switched on in the Build dialog (a drawer: Game, Game.info, Game_Data; fonts drawn fine as on MorphOS); Game.log started fresh and a $STACK cookie on OS4. Built only - not yet run on an OS4 machine.
- **b612** (2026-11-17) - Amiga Vampire only: the hand is hidden by its picture alone (SAGA's 16 lines at $DFF800 made see-through each frame while the game's window is active); b611's parking it off-screen removed, so the pointer's position stays the system's and nothing is left to put back - the system redraws its own pointer on leaving the game. Not yet tried on a Vampire.
- **b611** (2026-11-17) - Amiga Vampire only: SAGA's own hardware pointer (the Firebird/V4 core's hand, drawn over the game's pointer) hidden while the game's window is active - its 16 lines blanked at $DFF800 and parked at $DFF1D0/$DFF1D2 every frame, as AROS's SAGA driver does; the system's pointer back on leaving. Built, not yet tried on a Vampire. The editor itself wasn't rebuilt (it was running): only Runtimes/AmigaVampire/Game was installed.
- **b560-b610** (2026-11-10 to 11-17) - checkpoint 8c90808. Ports: PlayStation 2 (ISO with ESR, max 8 GB, controller; U6 needs USB keyboard and mouse), PlayStation 3 (.pkg), Ubuntu 24.04+ (.deb), Amiga Vampire (SAGA/AMMX); the U6 and U8 engines in the C player; CRT effect and Fill the screen; U6 debug notes; U6 parts of the Book and Container editors. The session that made these was lost, so this is all that's known.
- **b559** (2026-11-07) - all engines: Light Source command (light / put out / switch / burn out / make fresh / set or read minutes left / read lit, burnt out - this object or a thing on the map, placed or carried); Paperdoll... > Open this object's own paperdoll; By Burnt Out now runs for carried lights too, with the object's paperdoll at hand
- **b558** (2026-11-07) - all engines: a paperdoll slot can show what it holds on its object in the world (Show on the object, X/Y on the object's picture, steps for several, a look per item there, Place it on the object... picker); each item clickable on its own (click names it, double click uses it), the object stays one object; changes in the paperdoll show at once
- **b557** (2026-11-06) - Ultima 6 only: Conversation Style command - answers typed / picked from a list (arrows, 1-9, click) / either; keywords coloured or not, any colour; portrait in the HUD, in the words' box or none; words in the text area or a box of their own (place, size, colour, picture, font, colour); back to Ultima 6's; kept in saves
- **b556** (2026-11-06) - Ultima 6 only: a line before Scene Type Text / a text-area Scene Choice counts as read - the next Show Text comes straight after the typing (it waited for an unseen extra Enter)
- **b555** (2026-11-06) - Ultima 6 only: one cross - while a verb waits the mouse moves the map cursor's crosshairs (no second pointer), centred on its square; Look at someone shows their portrait in the panel till the next action
- **b554** (2026-11-06) - Ultima 6 only: Show Text's words take {Variable} (Nice to meet you, {heroName}!); a conversation stays one conversation (portrait, music) while its event asks for typing or a choice between its Show Texts
- **b553** (2026-11-06) - Ultima 6 only: the map cursor is the crosshairs sprite (arrows move it, Enter picks); Shift+8 is * too, and a click on the member's name or portrait switches portrait / inventory
- **b552** (2026-11-06) - Ultima 6 only: party size 0-9 (more: 9), enforced by Join Party; verbose lines in the text area, food lines hidden unless Ultima 7 food; System Settings: no Body Container, Combat, Save/Load, Slider, Weapon in hand tabs; Ultima 6's mouse pointers (arrow, crosshairs, eight direction arrows); Conversations = the keywords' colour; Sounds without windows/paperdolls; Title Scr
- **b551** (2026-11-06) - Ultima 6 only: food is for camping (no hunger, no starving, no healing by the hour; Rest's mealtime feeds and the fed heal) unless System Settings > Ultima 6 inherits Ultima 7's food (then the Health tab's Hunger shows); no volume - gone from the Object, Container and Book editors, containers never full
- **b550** (2026-11-06) - Ultima 6 only: four directions and five poses (Idle, Walking, Sleeping, Unconscious, Body) in the Character Editor; no attack poses - a hit plays the Hit Spark sprite group's spark; no weapon hand point in the Sprite Editor; damage not from a blow told with its cause (Andrew lightly damaged by poison); errors and the Quit question in the text area - all words editable
- **b549** (2026-11-06) - Ultima 6 only: Look on bare ground names its tile (Tile Properties > Name) - with L and with a click; unnamed or unseen: nothing
- **b547** (2026-11-06) - Ultima 6 only: character making commands - Scene Type Text, Scene Pick Picture, Set Gender, Scene Distribute Points (points from a formula/global variable, stats by name or built-in kind); a long speech waits for a key a screenful at a time; the text area's cursor can be a sprite (HUD Editor), one frame blinks
- **b546** (2026-11-06) - Ultima 6 only: Change Portrait (Ultima 6 tab > Party): someone's Face changes in play, kept in saves; Back to its own
- **b544** (2026-11-06) - a new drawn frame suggests its sprite's frame size (a tileset's tiles match), else 16x16 in Ultima 6 projects; an Ultima 6 project opening with a non-16 tile grid is told the map's grid is its tileset's frame size
- **b541** (2026-11-06) - Ultima 6 only: Tools > Leveling Editor - leveling systems of the maker's own (level table + events made of commands: when someone earns a level, after Rest, called by Run Leveling Event), one in use; templates Ultima 6 shrines and Savage Empire / Martian Dreams dreams; Rise A Level, Run Leveling Event; Scene Choice asks in the text area with no scene
- **b540** (2026-11-06) - Ultima 6 only: no upper floors or roofs; what the avatar can't see is black; bags open in the panel, books read in the text area, bodies searched; HUD command buttons do Rest/Cast/Combat; cutscenes (Begin Scene, Scene Picture, Scene Text, Scene Wait For Key, Scene Choice, Scene Remove, End Scene) and the title menu's Introduction / Create New Character scenes
- **b539** (2026-11-06) - Ultima 6 only: Text area notes - rules change or remove any of the engine's own notes (a spell's "Light on Andrew." and the rest), * for any words, {1} {2} put back; a switch for notes no rule fits
- **b538** (2026-11-06) - Ultima 6 only: Cast (spellbook in hand, the book in the panel, typed syllables, Ultima 6's checks), solo mode moves the member, combat modes and each Command member's own turn, Ultima 6's title menu in place of the paperdoll title and Save/Load screen; CommandList Guide 34
- **b537** (2026-11-06) - Ultima 6 only: the mouse as Ultima 6 (click looks, double click talks/uses from the next square, chests searched), Ultima 6 levels (shrine or dream), Rest; Ultima 7 leveling locked off
- **b532** (2026-11-05) - the close button's "At its own size" right under its sprite in the Character Editor; first in the paperdoll layout
2026-11-05|b529 (U6-6): combat turn by turn on the combat key, every blow told; Change HUD Frame (Ultima 6 projects)
2026-11-05|b528 (U6-5): conversations as Ultima 6 - portrait, typed keywords (Ultima 6 projects)
2026-11-05|b527 (U6-4): Ultima 6's keys and verbs, the inventory panel, members (Ultima 6 projects)
2026-11-05|b526 (U6-3): the HUD in play (Ultima 6 engine)
2026-11-05|b525 (U6-2): the HUD Editor in place of the PaperDoll Editor (Ultima 6 projects); System Settings > Ultima 6
2026-11-05|b524 (U6-1): project Style Ultima 6; the Ultima 6 engine starts as a copy of Ultima 7's
- **b523** (2026-11-05) - Ultima 8 talk: portrait while talking (System Settings > Ultima 8 > Conversations: None by default, Beside the words, In a corner), a Face with frames plays as a talking head; each character's bark colour and font on the Character Editor's Talk tab (the main character's colours the answers); the Talk pose while speaking; still a bark, never pauses. Fixed: a U8 speaker on 
- **b522** (2026-11-05) - Ultima 7 engine: a command naming the main character by its own number (Turn To Face, Walk To Location...) finds them, as the Ultima 8 engine does since b521
- **b521** (2026-11-05) - Crusader in Ultima 8 by the user's request: Control Character / Return Control (any character, lock), Tab / Home switch between party members, party size limit (System Settings > Ultima 8) with Party is full words (Verbose), party climbs after the main character, rolls / side-steps / kneeling, the crosshair (locked Crosshair sprite group with its guide), Attack and Next T
- **b520** (2026-11-05) - pets, in both Ultima 7 and Ultima 8 by the user's request: Call Character (a whistle or a bell: comes from off the screen, follows without joining), Order On A Target (the player clicks: attack, fetch, go to), Sniff And Lead (to where an object lies, its owner, or a character, waiting when the player falls behind); CommandList Guide 31 with 8 pictures
2026-11-03|b487: baseline of the Oct 29-30 work (stand points, doll links, disk progress, U8 reach and movement, editor diamond view) before the Ultima 8 keys
2026-10-27|b480: opening or starting another project closes the one open first (no data mixed between projects)
2026-10-27|b479 (U8-8): Ultima 8 magic - schools, foci with charges, the Cast pose, no pause
2026-10-27|b478 (U8-7): Ultima 8 conversations - words over the speaker's head, answers over the Avatar, no pause
2026-10-27|b477 (U8-6): Ultima 8 paperdoll stats; System Settings > Ultima 8
2026-10-27|b476 (U8-5): Ultima 8 combat - swing, kick, block; Pentagram's to-hit, damage, block and armour
2026-10-27|b475 (U8-4): Ultima 8 movement - screen-relative 8 directions, right-mouse walking, running, jumping, climbing, falling
2026-10-27|b474 (U8-3): Ultima 8 engine - no window pauses the game (paperdoll, inventory, containers, stats, PaperDoll Editor windows, books)
2026-10-27|b473 (U8-2): the Ultima 8 view - the map drawn in Ultima 8's diamond projection (Pentagram ItemSorter)
2026-10-27|b472 (U8-1): project Style Ultima 7 / Ultima 8; the Ultima 8 engine starts as a copy of Ultima 7's
2026-10-27|b471: a chair's back goes the moment its sitter stands up (it hung on while the chair was dragged)
2026-10-27|b470: chairs always under their sitter; an optional chair back per facing drawn over them
2026-10-27|b469: the chair's seat preview draws with the game's depth (issues.odt: sitting facing north)
2026-10-27|b468: How many? - the quantity slider when a stack changes hands (Exult drag.cc)
2026-10-27|b467: "Stays where it's put" - keeps its height like a hung thing, but can be picked up
2026-10-27|b466: Shop command (talk tree, goods, buying back, Serpent Isle haggle on the slider); C slider from System Settings
2026-10-27|b465: Resurrect any character - the main character too
2026-10-27|b464: eggs - everything Exult's eggs do (objs/egg.cc)
2026-10-27|b463: fix sprite pictures lost on save; editor speed and memory on big maps
2026-10-27|b462: Undo / Redo in the Map Editor and in container contents
2026-10-27|b461: Serpent Isle paperdolls - what's worn drawn on the body
2026-10-27|b460: fix the Windows build (spawn reach rounding)
2026-10-27|b460: Spawn mode - spawn points (Ultima VII's monster eggs)
2026-10-27|b459: Format Painter (objects, characters, inventory paperdoll, stats paperdoll, PaperDoll designs)
2026-10-27|b458: Sprite Editor folders - identities, counts, cut/copy/paste/rename/delete; Expand sprite browser
2026-10-27|b457: facing of things inside containers (right-click in the contents), carried facing in C
2026-10-27|b456: a ground tile can have a tile under it (see-through tiles)
2026-10-27|b455: chairs facing north or west drawn over their sitter (Exult's order)
2026-10-27|b454: non-human characters - no paperdoll, can't be given things, death drops
2026-10-27|b453: spells Destroy Field, Common Event, Levitate, Translate; languages
2026-10-27|b452: level curves per character (Character Editor > Level, graph colour), Earthquake command
2026-10-27|b451: sprite previews in the Sprite Editor list, Object Editor and Houses/Vehicles lists; stats in columns (custom stats green, side by side)
2026-10-27|b450: Stationary characters, Teleport to a map/floor, palette pick for day/night, autosave, Cave zones
2026-10-27|b449: sounds heard from where they are (Exult's sound distance), all platforms
2026-10-27|b448: Amiga/MorphOS port bugs (issues.odt A/B) and walkers through 8px doorways
2026-10-27|b447: floors are apart in combat - characters only see, fight and hear enemies on their own floor
2026-10-22|b446: party members sit when the main character sits; Amiga pointer hidden with a 16x16 blank sprite
2026-10-22|b445: C player - Windows-like pause/keys/Escape, missing commands, Amiga speed, pointer and mouse lock
2026-10-22|b444: MorphOS build of the player; Build > MorphOS makes the game drawer
2026-10-22|b443: Build > Amiga 68k makes the whole game drawer
2026-10-22|b442: C player - things on a tall table under the floor above stay visible
2026-10-22|b441: C player - upper-floor paint order, NPCs breaking doors down
2026-10-22|b440: C player - maps that wrap around
2026-10-22|b439: C player - the engine's own sprite mouse pointer
2026-10-22|b438: C player - move aside / swap places, sea and air characters
2026-10-22|b437: C player - tiles, traps, fires, projectiles, status outlines, bleeding
2026-10-22|b436: C player meals, inns, waiters, When rules, zones, house doors
2026-10-22|b435: C player work schedules - Bake, Sew, Blacksmith, Lab, Farm, Miner
2026-10-22|b434: C player By Combine Item, display names
2026-10-22|b433: C player Input Number/Text, Select Item, gumps pause the game, Ctrl+Q
2026-10-22|b432: C player paperdoll buttons and the stats paperdoll
2026-10-22|b431: C player reading books, Change Book Content / Settings
2026-10-22|b430: C player Play Video and the title's intro video
2026-10-21|b429: Windows engine - each world keeps its own events' fired pages on Change Map
2026-10-21|b428: C player Change Map and worlds
2026-10-21|b427: C player vehicles - drive, turn, autopilot, flying
2026-10-21|b426: C player plants, tools, crafting stations, sleeping, liquids
2026-10-21|b425: C player mechanisms - keys, lockpicks, keychains, windows, levers, stairs, ladders, sextant, map, clock, instruments
2026-10-21|b424: C player magic - spells, spellbook, potions, food, useable objects
2026-10-21|b423 Player: Return To Title (a fresh game from game.sdbx, the Title Screen over it) and New Game during play (a fresh game) - one rebuild shared with loading a save
2026-10-21|b422 Player: loading - the game built anew from a save (every module reset, the world built from the save's data, then the clock, the player's exact spot, script data, vitals, combat mode, game conditions, weather and fired pages put back; the dead lie in their bodies with their belongings); Load button, double-click a save, Continue on the title (the newest save; greyed with none); bed
2026-10-21|b421 Player: saving - the Save/Load screen (S; save slots, save list, screenshot, details, Save / Load / Delete, typing the name, write-over and delete asked first, the Saved paperdoll, errors), saves beside the game in Saves/ (info.json, game.sav encrypted with the game's key - AES encryption added, read by the Windows tools too - shot.png), the whole game written back: map things and 
2026-10-21|b420 Player: the Title Screen - fills a black view before play (the game waits behind it), its music, New Game starts the game, Continue greyed with no save, Quit asks with the Quit prompt; disabled paperdoll parts drawn greyed (45%); --notitle for testing
2026-10-21|b419 Player: PaperDoll Editor paperdolls - objects' own (state on each copy, travels with it), forms (Open Paperdoll: modal, wait, answer variable, fresh), the error box (Show Error waits for it); every part kind and form control (buttons, switches, lids, slots and item areas with drag in/out and combining, sliders, displays, text, text boxes with typing, check boxes, radio buttons, dro
2026-10-21|b418 Player (Amiga 68k): Paula through audio.device is the default sound (the user's choice: its 8-bit sound); AHI only with "ahi"; the Vampire build will have its own
2026-10-21|b417 Player (Amiga): the sound route chosen by argument or a file beside the game - paula (audio.device only), ahi (AHI only), else AHI then Paula; Game.log notes the archive read before the sound task starts; BeginIO declared (alib_protos)
2026-10-21|b416 Player: Game.log notes each start-up step (sound and what it plays through, events, characters, running); nosound (argument, or a file called nosound beside the game) starts without sound
2026-10-21|b415 Player (Amiga freeze): sound task stack 16 KB -> 128 KB (the MP3 decoder's 17 KB scratch plus the mixer's 8 KB overflowed it), game stack 64 KB -> 256 KB; Game.log notes the first frame and the 100th
2026-10-21|b414 Player (Amiga freeze): the sound task at the game's own priority, stepping aside when behind (at priority 10 it froze the whole Amiga); sounds decrypted a block at a time as they play instead of whole first (a 9.5 MB MP3 took minutes on a 68060); table-driven AES-256 replaces tiny-AES (many times faster); checked byte for byte against whole-file decryption
2026-10-21|b413 Player: inventory windows and the mouse - one pipeline (drag after 2 px, identify after the 500 ms double-click window, double-click uses), character paperdolls (body, slots, weight, labels, exit button), backpack list, container windows (art, contents at their spots, Exult row layout), drag and drop between map, windows, slots and characters (closest free slot, add rules, containe
2026-10-21|b412 Player: Change Stat (temporary changes wear off), Reset Stat, Train, Exchange Currency, Grid From Map / Grid To Map, Pause Here, Allow Saving, Allow Controls, Quit Game, Game Over, Show Error (errors.c: the maker's words else the engine's)
2026-10-21|b411 Player: Attack Object and things in combat - blows at objects and containers (hit points, defense, no effect/no damage), breaking (destroyed sound, By Destroy Item, window glass broken in place, glass shards, a container's contents fall out), owned things a crime before witnesses, combat-mode double-click on things, explosives (fuse by double-click, struck kegs, blast damage, chain
2026-10-21|b410 Player: combat - factions (bands, relations, incidents), NPC Combat_schedule (find foe by attack mode, approach, dex points, strike, flee, give up), defending allies, player combat mode (C) and targets, roll to win, weapon damage, armor and immunities, weapon powers, shooting/throwing with ammo, charges and thrown weapons in flight, fight back and attack_avatar, theft witnesses and
2026-10-21|b409 Player: vitals - health (Hits/Strength), hourly mending and food, hunger, poison and ailments with timers, tile effects, knock-outs and mending, death with a body holding the belongings, the main character's death, hit outline / red flash, spoken lines (Verbose); Change HP, Kill Character, Change Food Level, Modify Ailments, Change State, heal fully
2026-10-21|b408 Player: Open Door (keys, keyrings), Picklock Door (Dexterity vs lock, lockpicks break), Pick Item From Container, Equip / Unequip Equipment (stow in bag or drop), Play Animation (on someone or a spot, waited or not)
2026-10-21|b407 Player: Join Party (followers), Follow, Change Schedule for hours, Place Character (remove / bring on), Turn To Face, Change Name, Change Look, Set Attack Mode, heal fully, Play Pose, Create/Delete/Move Object, Change Object Look, Set Door State, Count Items, Set Container Contents, Change Tile
2026-10-21|b406 Player game data encrypted as the Windows game's: game.sdbx, sprites.sdbx, pictures.sdbx, sounds.sdbx (ProjectContainer AES-256, project id key; SRPK archives decrypted per file on demand); player decrypts with SHA-256 + tiny-AES-c (AES-256-CBC); world commands groundwork (place/remove characters, change look, move object, door state, change tile)
2026-10-21|b405 Player: screen commands - Fade Screen, Flash Screen, Shake Screen, Show/Erase Picture, Move Camera (eased, back to the player), Camera Follows
2026-10-21|b404 Player: game conditions (Advance IF, Complex Game Condition, Until Condition Met) and the game's history (events and pages run, used, lit, put out, burnt out, equip/unequip, sounds played/stopped, last command result)
2026-10-21|b403 Player: Map Editor container contents (OwnContents) kept; full SettleAll gravity (elevating, surfaces, put underneath, floors, fixed pieces) at the start and whenever things are taken or put down, the player falls too
2026-10-21|b402 Player: lights burn down by the clock and are spent (look, sound, By Stay), lit state carried with a light, lit lights in party hands lift the dark
2026-10-21|b401 Player: weather - rain, snow, hail, storms (lightning, thunder), cloud shadows, rain sound, hourly changes, hidden under roofs, overcast darkening the sky, Change Weather
2026-10-21|b400 Player: sound - mixer (WAV/u7o, MP3 via dr_libs, IFF 8SVX), Play/Stop Sound, Play/Stop/Fade Music, Set Volume, Resume Map Music, map ambient by time of day, sprite frame sounds, door/light sounds; SDL audio and Amiga AHI / Paula (audio.device) sound task; the world's time stands still while a box is up; sounds in the game pack
2026-10-21|b399 Player: chairs and beds (seats per facing, Sit/Sleep schedules, walk-up-and-sit, covers, sitters drawn over), double-click use (talk, doors, lights, chairs, beds, covers), object event pages per placed copy, Sit/Stand/Sleep On Bed commands, thing barks
2026-10-21|b398 Player: NPC schedules (Exult update_schedule, Walk_to_schedule, Stand/Wait/Loiter/Wander/Pace/Dance/Sleep/Custom, dormant off screen), characters held by their event till it ends, Change Behavior
2026-10-21|b397 Player: the game clock (Exult ticks, System Settings start hour and time rate), Set Time, Wait in game minutes/hours, hour()/minute()/day(); day and night lighting (dawn/dusk/night colours, lit lights lift the dark), Change Day/Night, Apply Palette Color; barks drawn
2026-10-20|b396 Player: the script language - formulas (ScriptExpression), texts, arrays, grids, data commands, loops, Select Case, Wait Until, Common Events with Return, self switches, Get Game Data, Remove Item, game functions
2026-10-20|b395 Player: items - what characters carry (slots, hands, backpack/pouch, weight 2 x STR, stacking, containers with contents), Pickup Item, Drop Item, Give Item (party helps, at their feet), Show Bark, item triggers (By Pickup/Drop, Having/Not Having, Equip/UnEquip); walkers open closed unlocked doors and close them behind; open doors drawn at their offset
2026-10-20|b394 Player: events and dialog boxes - map events (enter/stay/exit), talking (double-click), Always pages, page conditions and cycles, Show Text with pages/replies/pinned/Stop Conversation, switches, variables, branches, labels, Wait, Cinema Mode, Teleport, Change Pose, Walk To (Floor) Location with Exult A*; Nine Slice frames in the pack; Build dialog: separate Amiga Vampire build
2026-10-20|b381: Sound Mixer Compose - the mix becomes a new internal sound (named, in the Sound list, embedded at the next Save) and the mixer is cleared; Preview Mix; per-sound Starts after
2026-10-19|b380: reagents with amounts (4 garlic, 2 spider silk); circles of your own after Exult's nine (name, level needed; deletable)
2026-10-19|b379: land, sea and air characters - sea creatures swim only on Water tiles; fliers fly at floor 10 in the open (out of melee reach), under ceilings indoors, in through doors, fall when knocked out; shots and spell missiles rise to a flier
2026-10-19|b378: mages cast in combat (heal when badly hurt, harmful or summoning spells at the foe)
2026-10-19|b377: spells - missiles, reach (everyone/foes/friends), animation; Teleport, Mark, Recall, Summon, Create Item/Field, Magic Unlock/Lock, Douse/Ignite, Reveal/Destroy Traps, Telekinesis, Charm, Fear, Dance, Resurrect, Clone, Reveal, Time Stop, Tremor, Locate; spell kills give experience
2026-10-19|b376: Exult-style spellbook (pages by circle, icons, casts left, bookmark, page corners), who can read it (Can cast magic + book's list), Cast Spell from a spellbook's own paperdoll
2026-10-19|b375: workers keep in-between work (thread, dough) in the workshop and do later steps first
2026-10-19|b374: real work schedules - Bake, Sew, Blacksmith, Lab work stations by recipe (fetch ingredients and tools, hands rules, deliver, tidy up); Farm and Miner harvest and sow; Lab reads between brews; Plant seed/soil sowing
2026-10-19|b373: Belongs to on each placed copy (Map Editor right-click), carried with the item; theft uses it
2026-10-19|b372: Paperdoll > Turn the object into another object works from any object's own events
2026-10-19|b371: fires that burn while lit (Light Source), solid only while lit; pouring between liquid containers and from a liquid on its own
2026-10-19|b370: stats paperdolls pause the game like every gump; clearer 'Starts the game in the party' checkbox
2026-10-19|b369: experience for kills, level curve editor (System Settings > Levels), per-character Level Lock
2026-10-19|b368: map ambient silenced when the Title Screen (or its intro video) opens
2026-10-19|b367: title re-centres after maximize/restore; no map ambient on the Title Screen
2026-10-19|b366: Title Screen paperdoll is fixed in place and opens without the paperdoll-open sound
2026-10-19|b365: replies under the main character (Exult) - System Settings > Conversations
2026-10-19|b364: Wall Designer - wall sets (a wall with a window, a doorway): pieces glued once (any objects at their own spot and lift, west-east and north-south), a Wall object built from a set, placed as one wall in the Map Editor (right-click > Runs), split into its real pieces in play (the wall a wall, the window a window, the door a door), pieces keep their designed height
2026-10-19|b363: an NPC sitting on after a Sit event (its event over) no longer stops saving
2026-10-19|b362: game errors - numbered errors with editable words (System Settings > Errors), an error paperdoll (%errnum, %errdescrp, %errdetail), disk full / read-only / missing told apart, damaged and too-new saves, Show Error command, plain numbered box for errors before the game can draw, error 900 for bugs during play
2026-10-19|b361: dragging a modal paperdoll (a prompt) no longer drops it under its own blocker (it froze: no buttons, no close, no moving)
2026-10-18|b360: Too Heavy for containers (Container Editor > Behavior) - can't be picked up or moved, only opened where it stands
2026-10-18|b359: worlds in play - Change Map has a World choice; going to another world (its Start Position or a picked spot) takes the party there, remembers the world left as it was, carries worlds in saves; a new game starts in the starting world
2026-10-18|b358: Maps tab - a project's worlds (one map = one world): add (name and size asked), click to edit, right-click rename / resize (what a smaller size cuts off listed first) / make starting world / delete (asked); the toolbar's tile size and map boxes, New / Save / Load Map gone
2026-10-18|b357: a party member carrying out an event's command isn't pulled back by following (Peter, In Party from the start, walked to the player instead of the pumpkin); bodies with no width or depth are in the area grid; measuring trace of scripted characters
2026-10-18|b356: path searches go their full length again - b354's limit (40x the straight way) cut off the way out of a house to its door, so Pickup Item walked in circles
2026-10-18|b355: I and Z open the next party member's inventory / stats (Exult ActionInventory / ActionStats); party members on the map take dropped items (Exult Actor::drop); Words / Formula switch for Show Bark, Set Part Text, Change Name
2026-10-18|b354: speed - frames decoded once (FrameCache) on one animation clock, definitions by id, world bodies kept by area, walls by screen area, doors restack locally, ground in chunks, only near things on the canvas, Exult's dormant NPCs, game work every drawn frame; F10 frame rate, SANDCASTLE_PERF_LOG measuring. Stress map (4367 things, 128 NPCs): 7 -> 60 fps
2026-10-18|b353: shipping - only the .msrp inside the game's .exe, the .sdbx media in <game>_Data beside it (size + fingerprint checked: married to that game); System Settings > Game Window (window size or 100% full screen); a built game has no testing bar
2026-10-18|b352: Title Screen - a PaperDoll Editor paperdoll before the game (intro video, music), New Game / Continue / Load Game / Quit parts, Return To Title and New Game start fresh games; Quit prompt and Ctrl+Q; System Settings > Title Screen, Game > Test the Title Screen
2026-10-18|b351: screen, time, sound and game script commands - fades, flashes, camera, pictures, set time, change map, music fades and volumes, save/load by slot, allow/forbid saving and controls, game over, return to title, quit, learn/cast spell; Play Music can loop
2026-10-18|b350: character and object script commands - party, schedule, place/remove, face, follow, name, look, attack mode, heal/resurrect, bark; create/delete/move/re-skin things, doors, count items, container contents; the party follows the main character
2026-10-18|b349: paperdoll script commands - close, show/hide, look, text, value, enable, move parts; lists from arrays; items in slots; move paperdoll
2026-10-18|b348: script commands - formulas, text variables, arrays, grids, While/Repeat/For Each/Break/Next, Select Case, Wait Until, Common Events, Debug Log, Pause Here; the 7 greyed-out commands
2026-10-18|b347: a character asleep on its Sleep schedule can't be talked to (Exult Actor::activate)
2026-10-18|b346: combat music - danger, battle, run away, escaped, victory, battle over (Exult), near death and death; ambient silent in battle; dead: double-click body for Save/Load, no Save button
2026-10-18|b345: dead, the Save/Load screen can be clicked; dying ends combat mode
2026-10-18|b344: upper-floor tiles catch clicks - nothing under a floor is identified or used through it
2026-10-18|b343: blows always land on the motionless - knocked out, asleep, paralyzed, sleeping in bed
2026-10-18|b342: the player can finish off a knocked-out foe with a weapon
2026-10-18|b341: PaperDoll Editor form controls, prompt paperdolls, Open Paperdoll, fixed sounds, ambient sounds
2026-10-18|b340: Lockpick/Keychain groups red; non-empty object folders purple and undeletable; Load shows Pressed first
2026-10-17|b339: Save/Load screen stays open after saving, as Exult
2026-10-17|b338: Save/Load screen says why on the paperdoll; closes after saving
2026-10-17|b337: Save slots - Save name part becomes an Exult-style numbered save slot
2026-10-17|b336: in-game Save/Load as a PaperDoll Editor paperdoll
2026-10-17|b335: Video Library + Play Video command
2026-10-17|b334: Walk To Floor Location gets down/up stairs
2026-10-17|b333: Walk To Floor Location command (floor + cell; walks up/down stairs)
2026-10-17|b332: inventory/custom-slot id counters never reuse an existing id after load
