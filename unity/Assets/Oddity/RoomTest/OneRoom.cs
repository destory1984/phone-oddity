using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // The 5-pyeong one-room studio (원룸) of the peeping game: seen from across the street through its big south window. Clear (interior) metres: x 0 -> 3.0 west to east,
    // z 0 (the window wall's inner face) -> 5.5 (the entrance wall's inner face), ceiling 2.30. Outer walls 0.20, inner walls 0.10. Same data shapes as Apartment (it is one Apartment.Layout).
    // Rooms: main (x 0..3, z 0..3.8), bath (x 0..1.5, z 3.9..5.5), entrance (x 1.6..3.0, z 3.8..5.5: the tiled front hall with the kitchenette along its east side and the shoe cabinet).
    public static class OneRoom
    {
        public const float H = 2.3f, X1 = 3.0f, ZN = 5.5f;
        // the window (통창) in the south wall and its sill, the bath door and the front door
        public const float WinA = .3f, WinB = 2.7f, WinY0 = .4f, WinY1 = 2.2f, BathDoorA = 4.0f, BathDoorB = 4.85f, FrontA = 2.0f, FrontB = 2.9f;
        // the peeping camera across the street: x, eye height, z, aim, default lens (fov 30 -> 6 by zoom)
        public static readonly Vector3 PeepEye = new Vector3(1.5f, 2.4f, -9f), PeepAim = new Vector3(1.5f, .9f, 2.75f);
        public const float PeepFov = 16f;
        static Apartment.Layout layout;
        public static Apartment.Layout Layout => layout ?? (layout = MakeLayout());

        static Apartment.Opening WinOpening() { var o = Apartment.Win("south_window", WinA, WinB, WinY0, WinY1); o.alu = true; return o; }
        static List<Apartment.WallDef> Walls() => new List<Apartment.WallDef> {
            Apartment.W("south", -.2f, X1 + .2f, -.2f, 0, WinOpening()),
            Apartment.W("west", -.2f, 0, 0, ZN), Apartment.W("east", X1, X1 + .2f, 0, ZN),
            Apartment.W("north", -.2f, X1 + .2f, ZN, ZN + .2f, Apartment.Door("front_door", FrontA, FrontB)),
            Apartment.W("bath_S", 0, 1.5f, 3.8f, 3.9f),
            Apartment.W("bath_E", 1.5f, 1.6f, 3.8f, ZN, Apartment.Door("bath_door", BathDoorA, BathDoorB)),
        };
        static List<Apartment.RoomDef> Rooms()
        {
            Vector3 eye = PeepEye, aim = PeepAim;
            return new List<Apartment.RoomDef> {
                new Apartment.RoomDef { name = "main", floor = Rect.MinMaxRect(0, 0, X1, 3.8f), color = new Color(.72f, .52f, .32f), eye = eye, aim = aim, fov = PeepFov },
                new Apartment.RoomDef { name = "bath", floor = Rect.MinMaxRect(0, 3.9f, 1.5f, ZN), color = new Color(.80f, .85f, .87f), eye = eye, aim = aim, fov = PeepFov },
                new Apartment.RoomDef { name = "entrance", floor = Rect.MinMaxRect(1.6f, 3.8f, X1, ZN), color = new Color(.58f, .58f, .57f), eye = eye, aim = aim, fov = PeepFov },
            };
        }
        static List<Apartment.DoorDef> Doors() => new List<Apartment.DoorDef> {
            new Apartment.DoorDef { name = "bath_door", a = "entrance", b = "bath", centre = new Vector2(1.55f, (BathDoorA + BathDoorB) / 2), axis = new Vector2(-1, 0), half = (BathDoorB - BathDoorA) / 2 },
        };
        // the bath door swings 95 degrees into the bath on its south jamb, so it lies along the bath's south wall and leaves the doorway free; the front door is shown closed (a box in Furniture)
        static List<Apartment.LeafDef> Leaves() => new List<Apartment.LeafDef> {
            Apartment.Leaf("bath_door", "bath_E", 1.5f, BathDoorA + .04f, new Vector2(0, 1), new Vector2(-1, 0), BathDoorB - BathDoorA, new Color(.94f, .93f, .90f)),
        };
        static List<Rect> Props() => new List<Rect> { Rect.MinMaxRect(2.23f, .70f, 2.97f, 2.20f) }; // the sofa (the glb scaled to 1.5 m long)

        static Apartment.FurnBox f(string name, float x0, float x1, float y0, float y1, float z0, float z1, Color c, bool block = false, string perch = null) => Apartment.Fb(name, x0, x1, y0, y1, z0, z1, c, block, perch);
        static List<Apartment.FurnBox> Furniture()
        {
            Color white = new Color(.93f, .93f, .91f), top = new Color(.30f, .30f, .32f), steel = new Color(.80f, .81f, .82f), dark = new Color(.12f, .12f, .13f), oak = new Color(.74f, .57f, .40f);
            Color wood = new Color(.68f, .52f, .38f), linen = new Color(.93f, .92f, .90f), duvet = new Color(.62f, .70f, .78f), door = new Color(.84f, .80f, .74f), alu = new Color(.74f, .75f, .77f);
            return new List<Apartment.FurnBox> {
                // bed: west wall, head at the north end
                f("bed_frame", 0, 1.0f, 0, .28f, .5f, 2.5f, wood, true),
                f("bed_mattress", .02f, .98f, .28f, .42f, .52f, 2.48f, linen, false, "bed"),
                f("bed_duvet", 0, 1.0f, .42f, .46f, .5f, 1.95f, duvet),
                f("bed_head", 0, 1.0f, 0, .85f, 2.5f, 2.56f, wood),
                f("rug", 1.25f, 2.15f, 0, .006f, 1.0f, 2.6f, new Color(.82f, .76f, .66f)),
                // built-in wardrobe: west wall, two doors facing east
                f("wardrobe", 0, .6f, 0, 2.2f, 2.7f, 3.8f, new Color(.90f, .88f, .84f), true),
                f("wardrobe_door1", .6f, .615f, .05f, 2.15f, 2.72f, 3.24f, new Color(.86f, .84f, .80f)),
                f("wardrobe_door2", .6f, .615f, .05f, 2.15f, 3.26f, 3.78f, new Color(.86f, .84f, .80f)),
                f("wardrobe_handle1", .615f, .63f, .9f, 1.2f, 3.17f, 3.21f, steel), f("wardrobe_handle2", .615f, .63f, .9f, 1.2f, 3.29f, 3.33f, steel),
                // kitchenette: east wall, counter with a sink and one burner, a small fridge under it, an upper cabinet; the counter is a cat perch
                f("counter", 2.45f, X1, 0, .85f, 3.0f, 4.5f, white, true, "counter"),
                f("worktop", 2.44f, X1, .85f, .89f, 2.99f, 4.5f, top),
                f("fridge_door", 2.43f, 2.45f, .05f, .80f, 3.02f, 3.58f, steel), f("fridge_handle", 2.41f, 2.43f, .55f, .75f, 3.52f, 3.55f, dark),
                f("sink", 2.58f, 2.92f, .87f, .895f, 3.7f, 4.1f, steel), f("tap", 2.93f, 2.96f, .89f, 1.05f, 3.88f, 3.92f, steel),
                f("cooktop", 2.62f, 2.92f, .89f, .9f, 4.15f, 4.40f, dark),
                f("upper", 2.7f, X1, 1.45f, 2.1f, 3.0f, 4.5f, white),
                // bath: toilet on the north wall, small washbasin on the west wall, a shower curtain in the south-west corner (cloth, Harin can brush through it)
                f("toilet", .95f, 1.35f, 0, .40f, 4.9f, 5.5f, white, true), f("toilet_tank", .95f, 1.35f, .40f, .80f, 5.25f, 5.5f, white),
                f("basin", 0, .4f, 0, .80f, 4.45f, 4.95f, white, true), f("basin_top", 0, .4f, .80f, .85f, 4.45f, 4.95f, steel),
                f("curtain_a", .64f, .66f, 0, 1.95f, 3.9f, 4.3f, new Color(.85f, .90f, .95f)), f("curtain_b", 0, .66f, 0, 1.95f, 4.28f, 4.3f, new Color(.85f, .90f, .95f)),
                // entrance: shoe cabinet on the east wall, the front door closed in its opening
                f("shoe_cabinet", 2.7f, X1, 0, 1.0f, 4.8f, 5.45f, oak, true),
                f("front_door_closed", FrontA + .04f, FrontB - .04f, .012f, 2.06f, 5.58f, 5.62f, door), f("front_door_handle", 2.12f, 2.16f, .95f, 1.0f, 5.52f, 5.58f, alu),
                // the window sill inside (a thin board, a cat perch) and a little plant on it
                f("sill", WinA, WinB, .30f, .40f, 0, .28f, new Color(.92f, .91f, .89f), true, "sill"),
            };
        }
        static List<Apartment.Fixture> Fixtures() => new List<Apartment.Fixture> {
            new Apartment.Fixture { name = "led_main", room = "main", x = 1.5f, z = 1.45f, sx = 1.0f, sz = .5f, power = 3.2f, range = 7f },
            new Apartment.Fixture { name = "led_bath", room = "bath", x = .75f, z = 4.7f, sx = .26f, sz = .26f, round = true, power = .9f, range = 4f },
            new Apartment.Fixture { name = "led_entrance", room = "entrance", x = 2.2f, z = 4.8f, sx = .24f, sz = .24f, round = true, power = .9f, range = 4f },
        };

        // the building's face outside the south wall (concrete and tile, the window reveal, neighbouring windows, floor bands): what the peeper sees round the window
        static void Facade(Transform root)
        {
            var g = new GameObject("Facade").transform; g.SetParent(root, false);
            Color concrete = new Color(.62f, .60f, .57f), tile = new Color(.72f, .66f, .58f), band = new Color(.52f, .50f, .48f), glassC = new Color(.24f, .30f, .36f), alu = new Color(.74f, .75f, .77f);
            const float z0 = -.30f, z1 = -.20f;
            Apartment.Box(g, "facade_left", -4.2f, WinA, -2f, 7f, z0, z1, concrete);
            Apartment.Box(g, "facade_right", WinB, 6.2f, -2f, 7f, z0, z1, concrete);
            Apartment.Box(g, "facade_below", WinA, WinB, -2f, WinY0, z0, z1, tile);
            Apartment.Box(g, "facade_above", WinA, WinB, WinY1, 7f, z0, z1, concrete);
            Apartment.Box(g, "facade_sill", WinA - .05f, WinB + .05f, WinY0 - .05f, WinY0, z0 - .12f, z1, band);
            Apartment.Box(g, "facade_band_a", -4.2f, 6.2f, 2.32f, 2.48f, z0 - .06f, z1, band);
            Apartment.Box(g, "facade_band_b", -4.2f, 6.2f, -.20f, -.02f, z0 - .06f, z1, band);
            for (int i = 0; i < 6; i++) Apartment.Box(g, "facade_tile_row", -4.2f, WinA, -1.9f + i * .5f, -1.88f + i * .5f, z0 - .005f, z0, band); // tile joints under the neighbour
            foreach (var (a, b) in new[] { (-2.9f, -1.1f), (3.9f, 5.7f) })
                foreach (var (y0, y1) in new[] { (.4f, 2.2f), (3.0f, 4.8f) })
                {
                    Apartment.Box(g, "facade_win", a, b, y0, y1, z0 - .01f, z0, glassC);
                    Apartment.Box(g, "facade_win_frame_t", a - .04f, b + .04f, y1, y1 + .04f, z0 - .02f, z0, alu); Apartment.Box(g, "facade_win_frame_b", a - .04f, b + .04f, y0 - .04f, y0, z0 - .02f, z0, alu);
                    Apartment.Box(g, "facade_win_bar", (a + b) / 2 - .02f, (a + b) / 2 + .02f, y0, y1, z0 - .02f, z0, alu);
                }
            Apartment.Box(g, "facade_win_up", WinA, WinB, 3.0f, 4.8f, z0 - .01f, z0, glassC); // the flat above
            Apartment.Box(g, "facade_win_up_bar", 1.48f, 1.52f, 3.0f, 4.8f, z0 - .02f, z0, alu);
        }
        // the curtain bunched at the window's east end, the rod, a plant on the sill
        static void Window(Transform fr, List<Rect> furnitureOut)
        {
            Color sheer = new Color(.90f, .90f, .92f), pot = new Color(.80f, .78f, .74f);
            Apartment.NoShadow(Apartment.Part(fr, PrimitiveType.Cylinder, new Vector3(1.5f, 2.26f, .12f), new Vector3(.02f, 1.22f, .02f), new Color(.74f, .75f, .77f)));
            fr.GetChild(fr.childCount - 1).localRotation = Quaternion.Euler(0, 0, 90);
            for (int i = 0; i < 3; i++)
            {
                float cx = 2.56f + i * .055f; var c = Apartment.Part(fr, PrimitiveType.Cylinder, new Vector3(cx, 1.30f, .12f + (i % 2) * .03f), new Vector3(.06f, .9f, .06f), sheer * (i % 2 == 0 ? 1f : .94f)); Apartment.NoShadow(c);
            }
            // the pillow: an ellipsoid whose lower half is inside the mattress, so the visible part is a soft dome (pillowSize.y high at the centre)
            var pl = Layout; var pg = Apartment.Part(fr, PrimitiveType.Sphere, pl.pillowAt, new Vector3(pl.pillowSize.x, pl.pillowSize.y * 2, pl.pillowSize.z), new Color(.95f, .94f, .92f)); pg.name = "bed_pillow";
            Apartment.Part(fr, PrimitiveType.Cylinder, new Vector3(.55f, .45f, .14f), new Vector3(.14f, .05f, .14f), pot);
            foreach (var (dx, dy, k) in new[] { (0f, .62f, .26f), (.05f, .74f, .2f), (-.04f, .70f, .18f) }) Apartment.Part(fr, PrimitiveType.Sphere, new Vector3(.55f + dx, dy, .14f), Vector3.one * k, new Color(.30f, .48f, .28f));
        }

        static Apartment.Layout MakeLayout() => new Apartment.Layout {
            name = "oneroom", H = H, X1 = X1, ZN = ZN, limX0 = .2f, limX1 = X1 - .2f, limZ0 = .2f, limZ1 = ZN - .2f,
            walls = Walls, rooms = Rooms, doors = Doors, furniture = Furniture, leaves = Leaves, fixtures = Fixtures, props = Props,
            // the sofa: the props glb scaled to 1.5 m long on the east wall facing west; the bed seat mark for K (and C when it is the nearest), lying along the bed with the head to the north
            sofaAt = new Vector3(2.60f, 0, 1.45f), sofaFace = Vector3.left, sofaScale = .94f, hasTable = false, hasTea = false, hasFridge = false, hasLedge = false, hasBed = true,
            bedSeat = new Vector3(.5f, .43f, 1.5f), bedFront = Vector3.right, bedSize = new Vector3(1.0f, .43f, 2.0f), bedStand = new Vector3(1.25f, 0, 1.5f), bedLieTop = .46f, pillowAt = new Vector3(.5f, .42f, 2.30f), pillowSize = new Vector3(.50f, .14f, .32f),
            // a 32 inch TV on the west wall over the foot of the bed, facing the sofa, centred on the sofa's z
            hasTv = true, tvAt = new Vector3(.02f, 1.25f, 1.45f), tvFace = Vector3.right, tvSize = new Vector3(.72f, .42f, .04f),
            harinStart = new Vector2(1.6f, 2.0f), somaliStart = new Vector2(1.2f, 3.2f), whiteStart = new Vector2(1.9f, 1.1f), tabbyStart = new Vector2(2.0f, 3.3f),
            startYaw = 180, somaliYaw = 150, whiteYaw = 200, tabbyYaw = 170,
            windowSpot = new Vector2(1.5f, .62f), windowLook = new Vector2(1.5f, 0), windowRangeX = new Vector2(.8f, 2.2f), windowYaw = 180,
            sunThrough = new Vector3(1.5f, 1.4f, -.1f), sunOnto = new Vector3(1.4f, .1f, 2.6f), lampAt = new Vector3(1.5f, 2.2f, 1.9f),
            counterStand = new Vector2(2.1f, 3.35f), fridgeLand = new Vector2(0, 0), counterLand = new[] { new Vector2(2.72f, 3.35f) }, sillLand = new[] { new Vector2(1.0f, .14f), new Vector2(2.0f, .14f) },
            rugBox = Rect.MinMaxRect(1.25f, 1.0f, 2.15f, 2.6f),
            wander = new[] { Rect.MinMaxRect(.9f, .9f, 2.1f, 3.4f), Rect.MinMaxRect(1.7f, 3.9f, 2.4f, 5.2f) }, wanderWeight = new[] { 80f, 20f },
            winPlaneZ = 0, winX0 = WinA, winX1 = WinB, winY0 = WinY0, winY1 = WinY1,
            sky = false, peep = true, floorExtras = Facade, furnExtras = Window,
        };
    }
}
