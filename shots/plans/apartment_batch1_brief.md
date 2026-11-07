# Worker brief: the 17-pyeong apartment, batch 1 (shell, doors, per-room cameras, moving the existing furniture)

Project: C:\_c\oddity-phone (read CLAUDE.md, TESTING.md). Do NOT commit, ship or push. One Unity at a time.
READ FIRST: `shots/plans/apartment_36a_layout.md` (the plan, the user's choices and GPT's advice). The plan is the spec; numbers below repeat its key parts.
Code: `unity/Assets/Oddity/RoomTest/RoomPlay.cs` (+ `FloorPath.cs`). Today it builds one 4 x 4 m room (`Room`, `Furnish`, `LeftWall`, window, sill, perches, rug, sofa/table/chairs glb props, `RoomEye`/`RoomAim` camera with zoom/follow). Harin, the two cats (SOMALI, WHITE), autonomy, perches, FloorPath, sounds, keys must keep working.

## Keep it switchable
Build the apartment behind `ODDITY_HOUSE` = `apt` (new default) or `room` (today's 4 x 4 room, unchanged, for comparison). Put the apartment geometry in a new file `unity/Assets/Oddity/RoomTest/Apartment.cs` (data + builder: rooms, walls, doors, windows, cameras, furniture spots), so RoomPlay.cs only calls into it.

## Shell (batch 1)
Use interior (clear) dimensions after walls: outer walls 0.2 m, inner walls 0.1 m. Orientation: south = balcony (+z or −z, pick one and keep it), north = entrance corridor. The unit is 4.58 m wide (x) by 9.0 m deep, plus a 1.31 m balcony on the south.
From north to south (centre-line metres, see the plan table): entrance (1.76 wide, about 1.2 deep, a 0.6 m utility shaft beside it); bedroom 2.21 x 2.76 in the north-west corner (window on the north wall to the corridor); bath about 1.5 x 2.4 south of the bedroom on the west side; kitchen/dining = the galley along the east side from the entrance to the living room (counter on the east wall); living room 4.58 x 3.84 across the full width; balcony 1.31 deep with the big south window (sliding glass between living room and balcony, and the outer balcony window).
- Floors per room (living/bed/kitchen wood, bath tile, entrance tile a step 0.05 m lower, balcony tile), walls 2.4 m high with door openings (0.8–0.9 m wide, 2.1 m high): front door (north, entrance), bedroom door, bath door, kitchen↔living sliding door opening (1.8 m wide, keep a visible frame and floor rail), living↔balcony sliding glass door. Doors can be open frames for now (no swinging leaves), but show frames and thresholds.
- Light: sun through the south windows into the living room (move today's window sun setup there), ambient as today. The window view card goes outside the balcony window.
- FloorPath: blockers = walls with door gaps + furniture footprints, so walks route through doors. Check the grid covers the whole unit.

## Cameras (user choice: one angled camera per room)
- One fixed camera per room, 40–50° down, FOV chosen to show the whole room, positions as in the plan (living: from the north-east corner looking south-west; kitchen: from the south-west/living side looking north-east; bedroom: from the south-east looking north-west; entrance and bath: from the south-west; balcony: from the north-west looking south-east).
- Cutaway: walls between the camera and the room are hidden (or cut down to 0.3 m) while that room is the active view; door frames, thresholds and floor edges stay.
- Active room = the room containing the controlled character (Harin or the controlled cat). Cut instantly when its centre is 0.2 m past a door threshold; hysteresis band at thresholds; no re-switch within 0.3 s; uncontrolled cats never switch the camera. Zoom keys/wheel still zoom within the room camera (towards the character) like today.
- Log `ROOMPLAY_ROOM view=<living|kitchen|bedroom|bath|entrance|balcony>` on each cut.

## Furniture moved into the new rooms (batch 1 uses the existing props only)
- Living: sofa against the west wall facing east (sofa glb), TV stand + TV on the east wall, rug between, floor lamp, plant in the south-west window corner. 0.9 m free path from the kitchen door to the balcony door.
- Kitchen: counter run (sink, cooktop, washer front removed: the washer goes to the balcony) on the east wall, depth 0.6, fridge at the north end of that run; the dining table becomes a wall table on the west side (keep the table glb if it fits 0.8 x 0.5-ish by scaling, else a simple box table) with the laptop and one or two chairs; keep the chair slide logic working or simplify it if the geometry no longer fits (say what you did).
- Balcony: washer at the east end.
- Bedroom, bath, entrance: empty floors in batch 1 (furniture comes in batch 2), but the bedroom keeps its window and the entrance its step.
- Harin spawns in the living room. Cats spawn in the living room/kitchen on free floor.
- Cat perches: keep sofa, table, counter, fridge (via counter), and move the "sill" perch to a deep sill under the living-room south window (inside the room, 0.3 m deep, about 0.45 m high) plus the balcony window ledge if easy. Spot reservation must keep working.
- Click targets: floor (any room, path through doors), sofa, table, counter, fridge, sill, window (the living-room south window).

## Verify
1. `python tools/dev.py check`; `python tools/dev.py test FloorPath,ZeroTwoDance` (add a FloorPath test that a path from the entrance to the balcony goes through the door gaps).
2. Scripted play with `ODDITY_PLAYSHOTS=1`: screenshot each room's camera view (walk Harin room to room with clicks: living → kitchen → entrance → back → bedroom door → bath door → balcony), and cats doing a few perches. Copy key shots to `shots/room_test/keep/apartment_1/`. Look at them yourself: walls not blocking the view, rooms read clearly, furniture against the right walls, no one walking through walls, camera cuts at doors.
3. `ODDITY_HOUSE=room` still gives today's room.
4. Do not run the full `test`.

## Report (40 lines max)
Files changed, the coordinates/dimensions you used per room, camera positions, how cutaways work, what happened to the chair-slide logic and perches, ROOM log lines, screenshot paths, anything off. Update the CLAUDE.md play row (keep it short: the apartment, ODDITY_HOUSE, per-room cameras).
