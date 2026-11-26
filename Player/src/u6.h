/*
 * The Ultima 6 engine's own part of the player (Editor/U6/U6GameWindow*.cs, the Windows reference): an Ultima 6 project
 * (its manifest's Style "Ultima6") is played on the HUD Editor's screen - the frame, the world inside the Game View with the
 * avatar in its middle, the text area telling what happens, the panel showing one member, the buttons, date, compass, sun.
 * Ultima 7 projects never reach any of this.
 */
#ifndef SANDCASTLE_U6_H
#define SANDCASTLE_U6_H

#include <stdint.h>

/* After the game is read: an Ultima 6 project's HUD set up (the world's view placed in it). */
void u6_load(void);
/* 1 for an Ultima 6 project. */
int u6_on(void);
void u6_show_character(int characterId);
/* 1 when it has a HUD with a Game View (the world inside it); 0: the world fills the screen, as before. */
int u6_hud(void);
/* Whether a point of the screen is inside the Game View (the world's part of the screen). */
int u6_in_view(int x, int y);

/* The HUD over the drawn world: its frame round the Game View, its pieces, the text area, the panel, the date. */
void u6_draw(uint32_t *pixels, uint32_t ms);

/* Ultima 6: what happens, told in the text area ("You see a wall."); 0 if this game has no text area (say it another way). */
int u6_say(const char *text);
const char *u6_word(const char *id);    /* an Ultima 6 word (System Settings > Ultima 6 > Words) */
/* The line being typed under the text ("Move-"); "" none. */
void u6_prompt(const char *text);

/* An engine's note (a refusal: "That's too heavy to lift."): into the text area through the maker's rules (Text area notes). */
void u6_note(const char *text);

/* ---- Input (U6GameWindow.U6Verbs / U6Keys) ---- */
struct PlatformEvent;
/* A key: 1 if Ultima 6 took it (every key not Ultima 6's is swallowed - Escape and F11 still reach the engine). */
int u6_key(const struct PlatformEvent *e);
/* A left click: 1 if Ultima 6 took it (a button, the panel, a verb's target); 0 - the engine's own clicks (look, use, walk). */
int u6_mouse_down(int x, int y);
/* The walking keys held now (the last pressed wins; diagonals too). */
void u6_walk(int *dx, int *dy);
/* The engine's clicks on the map, as Ultima 6 has them: one click looks (the text area), a double click talks / uses / attacks. */
void u6_click_look(int actor, int uid);
void u6_multi_use(int actor, int uid);
/* Ultima 6 drags only from the next square. */
int u6_can_drag(int cellX, int cellY);

/* ---- Talking (U6GameWindow.U6Talk): the words in the text area, the portrait in the panel, the answer typed ---- */
struct Sprite;
int u6_talking(void);   /* this game talks the Ultima 6 way (a HUD with a text area) */
void u6_talk_show(int speakerCharacter, const char *text, const struct Sprite *face, const char *const *replies, int replyCount);
/* While a talk's words are up: its keys (Enter, Escape, Backspace, Space) and letters typed. 1 taken. */
int u6_talk_key(const struct PlatformEvent *e);
int u6_talk_text(int ch);
int u6_talk_click(int x, int y);   /* a listed reply clicked (Conversation Style): 1 taken */

/* A book read the Ultima 6 way: its words in the text area, a screenful at a time ("<more>": any key, Escape stops). 1 shown. */
int u6_read_book(int bookId);
/* Ctrl+Q: "Quit the game? (Y/N)" in the text area; 0 when this game has none (the engine's own prompt). */
int u6_ask_to_quit(void);

/* ---- Combat (U6GameWindow.U6Combat / HitFlash) ---- */
void u6_hit_spark(int actor);                                       /* the Hit Spark sprite on whoever is struck */
void u6_tell_wound(int characterId, int actor, int hpLeft, int maxHp, int dying, const char *cause);
void u6_tell_attack(int attacker, int defender, int hit);           /* "Andrew attacks rat." / "Missed!" */
int u6_says_line(int actor, int characterId, const char *line);     /* a Verbose line in the text area; 0: no text area */
int u6_error(int number, const char *words);                        /* an error in the text area; 0: no text area */
int u6_food(void);                                                  /* Ultima 6's food: no hunger, starving or mending by the hour */
int u6_party_full(int characterId);                                 /* Join Party refused (told): the party is full */

/* Combat turn by turn: 1 while the world waits for the avatar's turn (call each frame, not while paused); the avatar strikes
   only on its Attack turn. */
int u6_turn_frozen(double ms);
int u6_player_may_strike(void);

/* The pointer, Ultima 6's (arrows from the avatar, the crosshairs): drawn - 1; 0 the engine's own. */
int u6_pointer(uint32_t *pixels, int x, int y, uint32_t ms);
/* Solo mode: the member under the keys steered a square at a time (1: the walking keys were its - the avatar stands). */
int u6_solo_step(int dx, int dy);
int u6_waits_for_command(int actor);   /* turn by turn: a Command member waits for the player's turn */

/* ---- The Ultima 6 tab's event commands (events.c hands them over) ----
   whoCharacter: the command's Who (-1 none); activator: whoever set the event off (-1 the player); token: answered with
   events_doll_answered(token, 1) when a waiting command is done; formula: a parameter's formula's number.
   1 done, -1 failed (the result false), 0 waiting for the token. */
struct cJSON;
int u6_command(int type, struct cJSON *params, int whoCharacter, int activator, int token, double (*formula)(const char *key));
/* A scene up (Begin Scene ... End Scene): the world waits; its pictures and words drawn over everything; its keys and clicks. */
int u6_scene_on(void);
void u6_draw_scene(uint32_t *pixels, uint32_t ms);

/* A save: the HUD frame in play and the conversations' style kept; a load: put back (after u6_load). */
void u6_save(struct cJSON *runtime);
void u6_restore(struct cJSON *runtime);

/* ---- The title menu (U6GameWindow.U6Title) ---- */
void u6_show_title(void);               /* the game's start: the intro video, then the menu */
int u6_title_on(void);                  /* the menu is up: the game waits */
void u6_draw_title(uint32_t *pixels, uint32_t ms);
int u6_open_save_screen(void);          /* Ultima 6 has no Save/Load screen: the menu (in play); 0 - the engine's own */

/* ---- Levels (U6Leveling) ---- */
int u6_levels_on(void);                 /* Ultima 6's levels (System Settings > Ultima 6: not Ultima 7 leveling) */
int u6_max_hp(int characterId);         /* 30 a level (the system's); -1: not Ultima 6's levels */
void u6_init_levels(void);              /* at the start: more hit points than the level allows - a higher level */
int u6_experience_gained(int characterId); /* after experience is added: 1 handled (Ultima 6's way) */

#endif
