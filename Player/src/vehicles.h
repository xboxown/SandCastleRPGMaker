/* Vehicles: ships, carts, flying carpets (GameTestWindow.Vehicles / VehicleTravel, Exult's Barge_object). */
#ifndef SANDCASTLE_VEHICLES_H
#define SANDCASTLE_VEHICLES_H

int vehicles_use_part(int uid);                 /* a vehicle's controls or driver's seat double-clicked: 1 = handled */
int vehicles_drive(int dx, int dy, double seconds); /* the direction keys while driving: 1 = they drive it (the player doesn't walk) */
void vehicles_update(double ms);                /* autopilots, flying vehicles climbing */
void vehicles_stop_driving(void);
void vehicles_reset(void);

/* The event commands. */
int vehicles_drive_command(int templateId);
int vehicles_stop_command(void);
int vehicles_speed_command(int templateId, double speed);
int vehicles_travel_command(int templateId, int x, int y, int token);

#endif
