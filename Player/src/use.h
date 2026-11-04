/* Double-clicking in the world: talk to a character, use a thing (GameTestWindow HandleObjectClick). */
#ifndef SANDCASTLE_USE_H
#define SANDCASTLE_USE_H

void use_at(int screenX, int screenY);
void use_actor(int actor);   /* talk (in combat mode: attack) */
void use_thing(int uid);     /* a thing on the map double-clicked */
struct cJSON;
void use_put_out_light(int uid, struct cJSON *def, int withSound); /* a placed light goes out, whatever did it */

#endif
