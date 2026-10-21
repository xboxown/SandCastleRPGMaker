/*
 * The Show Text dialog box, as the Windows engine draws it
 * (DialogBoxBuilder, GameTestWindow.Talk/Replies): the DialogFrame sprite
 * (Nine Slice corners kept), the speaker's face on its side, a title, the
 * words in the DialogFont four lines a page (Exult's NPC box), and the
 * replies left to right, row under row, More / Previous past five rows of
 * five. While a box is up the game is paused (an Exult conversation).
 */
#ifndef SANDCASTLE_DIALOG_H
#define SANDCASTLE_DIALOG_H

#include <stdint.h>
#include "pictures.h"
#include "platform.h"

#define DIALOG_MAX_REPLIES 64

typedef struct DialogBox
{
    const char *title, *text;      /* copied when queued */
    const Sprite *face;            /* the speaker's face, or NULL */
    int faceRight;
    int position;                  /* 0 top, 1 middle, 2 bottom */
    int dim;                       /* darken the game behind */
    const Sprite *frame;           /* the DialogFrame sprite, or NULL for none */
    int replyCount;
    int replyIds[DIALOG_MAX_REPLIES];
    const char *replies[DIALOG_MAX_REPLIES];
    /* A reply picked: called with its id (the box is gone by then). */
    void (*chosen)(void *context, int replyId);
    void *context;
} DialogBox;

/* System Settings > Conversations: the replies in a box of their own with the main character's face (as Exult). */
void dialog_set_replies_apart(int apart, const Sprite *mainCharacterFace);

/* Queues a box (shown when those before it are read). */
void dialog_queue(const DialogBox *box);
/* Stop Conversation: goOn is called once every box queued now is read. */
void dialog_after_read(void (*goOn)(void *context), void *context);

/* A box is up, or waiting to come up: the game is paused. */
int dialog_showing(void);

void dialog_key(enum PlatformKey key);
void dialog_mouse_move(int x, int y);
void dialog_click(int x, int y);

void dialog_draw(uint32_t *pixels, uint32_t ms);

#endif
