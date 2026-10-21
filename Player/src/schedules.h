/* Characters' daily schedules (GameTestWindow.Schedules, from Exult). */
#ifndef SANDCASTLE_SCHEDULES_H
#define SANDCASTLE_SCHEDULES_H

void schedules_load(void);
void schedules_start(void);
void schedules_update(double ms);           /* not while the game is paused */
void schedules_let_go(int actor);           /* an event that held them is over */
int schedules_change_behavior(int actor, const char *behavior);
void schedules_join(int actor, int join);
void schedules_follow(int actor, int leader);     /* leader -1: stop following */
void schedules_until(int actor, long minute);
int schedules_sleeping(int actor);

#endif
