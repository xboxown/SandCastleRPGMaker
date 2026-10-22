/* The work schedules: Bake, Sew, Blacksmith, Lab at their stations; Farm and Miner in their fields (GameTestWindow.Work). */
#ifndef SANDCASTLE_WORK_H
#define SANDCASTLE_WORK_H
#include "../third_party/cJSON.h"

int work_is_work(int activity);
int work_now_what(int actor, int activity, cJSON *entry, int destX, int destY); /* 1 = at work */
int work_busy(int actor);                                                       /* in a round: its schedule waits */
void work_schedule_changed(int actor, int newActivity);                         /* the round stops; borrowed tools go back */
int work_station_in_use(int uid);
void work_update(double ms);
void work_reset(void);

#endif
