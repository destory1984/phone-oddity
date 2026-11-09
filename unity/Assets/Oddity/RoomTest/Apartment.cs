using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oddity.RoomTest
{
    // The 17-pyeong flat (LH 36A, see shots/plans/apartment_36a_layout.md) as data plus a builder. Clear (interior) metres: x 0 -> 4.38 west to east, z 0 (the balcony's south face) -> 10.15 (the entrance's north
    // face), south = low z = the balcony, north = high z = the entrance. Outer walls 0.20 m, inner walls 0.10 m, ceilings 2.40 m. RoomPlay only calls into this file: Build() makes the shell, the doors and
    // the plain-box furniture; Walls()/Furniture() are pure data for FloorPath tests; View decides which room is shown and which walls are cut away.
    public static class Apartment
    {
        public const float H = 2.4f, X1 = 4.38f, ZN = 10.15f;
        // the walkable box: 0.2 m inside the outer walls (the old room kept the same margin)
        public const float LimX0 = .2f, LimX1 = X1 - .2f, LimZ0 = .2f, LimZ1 = ZN - .2f;
        static readonly Color Wall = new Color(.90f, .86f, .79f), CeilCol = new Color(.96f, .95f, .93f), BaseCol = new Color(.95f, .94f, .90f), Trim = new Color(.62f, .42f, .26f), Alu = new Color(.74f, .75f, .77f);

        public struct Opening { public float a, b, y0, y1; public bool door, window, slider; public string name; }
        public sealed class WallDef
        {
            public string name; public float x0, x1, z0, z1; public readonly List<Opening> openings = new List<Opening>();
            public bool AlongX => x1 - x0 >= z1 - z0;
            public float Thick => AlongX ? z1 - z0 : x1 - x0;
        }
        public sealed class RoomDef { public string name; public Rect floor; public float floorY; public Color color; public Vector3 eye, aim; public float fov; }
        public sealed class DoorDef { public string name, a, b; public Vector2 centre, axis; public float half; } // axis: the unit direction from room a to room b
        public sealed class FurnBox { public string name; public float x0, x1, y0, y1, z0, z1; public Color color; public bool block; public string perch; }

        static Opening Door(string n, float a, float b, bool slider = false) => new Opening { a = a, b = b, y0 = 0, y1 = 2.1f, door = true, slider = slider, name = n };
        static Opening Win(string n, float a, float b, float y0, float y1) => new Opening { a = a, b = b, y0 = y0, y1 = y1, window = true, name = n };
        static WallDef W(string name, float x0, float x1, float z0, float z1, params Opening[] ops) { var w = new WallDef { name = name, x0 = x0, x1 = x1, z0 = z0, z1 = z1 }; w.openings.AddRange(ops); return w; }

        // ---- doors and windows: bath door z 5.30..6.15 and bedroom door z 7.95..8.80 in the kitchen's west wall, kitchen <-> entrance arch x 2.5..3.6, kitchen <-> living sliding door 1.8 m, living <-> balcony sliding glass 2.3 m
        public const float BathDoorA = 5.30f, BathDoorB = 6.15f, BedDoorA = 7.95f, BedDoorB = 8.80f, ArchA = 2.5f, ArchB = 3.6f, KitSlideA = 2.45f, KitSlideB = 4.25f, GlassA = 1.95f, GlassB = 4.25f, FrontA = 3.0f, FrontB = 3.9f;
        public const float SillX0 = .6f, SillX1 = 1.7f, SillTop = .45f, SillWinTop = 2.0f; // the living room's south window above the deep sill, in the partition at z 1.20 -> 1.30
        public static List<WallDef> Walls() => new List<WallDef> {
            W("bal_S", -.2f, X1 + .2f, -.2f, 0, Win("balcony_window", .2f, X1 - .2f, .9f, 2.2f)),
            W("bal_W", -.2f, 0, 0, 1.25f), W("bal_E", X1, X1 + .2f, 0, 1.25f),
            W("part_BL", 0, X1, 1.20f, 1.30f, Win("living_window", SillX0, SillX1, SillTop, SillWinTop), Door("balcony_slider", GlassA, GlassB, true)),
            W("liv_W", -.2f, 0, 1.25f, 5.09f), W("liv_E", X1, X1 + .2f, 1.25f, 5.09f),
            W("liv_N", 0, X1, 5.04f, 5.14f, Door("kitchen_slider", KitSlideA, KitSlideB, true)),
            W("closet", -.2f, .71f, 5.09f, 7.34f), // the built-in storage and shaft beside the bath
            W("kit_W_bath", 2.21f, 2.31f, 5.14f, 7.34f, Door("bath_door", BathDoorA, BathDoorB)),
            W("kit_W_bed", 2.21f, 2.31f, 7.34f, ZN, Door("bedroom_door", BedDoorA, BedDoorB)),
            W("kit_E", X1, X1 + .2f, 5.09f, 8.90f), W("ent_E", X1, X1 + .2f, 8.90f, ZN + .2f),
            W("kit_ent", 2.31f, X1, 8.85f, 8.95f, Door("entrance_arch", ArchA, ArchB)),
            W("chase", 2.31f, 2.62f, 8.95f, ZN), // the utility shaft beside the entrance
            W("bed_W", -.2f, 0, 7.34f, ZN + .2f),
            W("bed_S", 0, 2.31f, 7.29f, 7.39f),
            W("bed_N", -.2f, 2.26f, ZN, ZN + .2f, Win("bedroom_window", .5f, 1.7f, .9f, 2.0f)),
            W("ent_N", 2.26f, X1 + .2f, ZN, ZN + .2f, Door("front_door", FrontA, FrontB)),
        };

        // one fixed camera per room, about 20-25 degrees down with the eye 2.4-2.5 m high, pulled back outside the cut-away walls (narrow lens) so a standing Harin's face is readable: living from the north-east, kitchen from the south-west (the living side), bedroom from the south-east,
        // bath and entrance from the south-west, balcony from the north-west. ODDITY_APTCAM="name=ex,ey,ez,ax,ay,az,fov;..." overrides for tuning
        public static List<RoomDef> Rooms()
        {
            var l = new List<RoomDef> {
                new RoomDef{name="balcony",floor=Rect.MinMaxRect(0,0,X1,1.20f),color=new Color(.64f,.62f,.58f),eye=new Vector3(-1.7f,2.5f,4.5f),aim=new Vector3(2.19f,.4f,.6f),fov=36},
                new RoomDef{name="living",floor=Rect.MinMaxRect(0,1.30f,X1,5.04f),color=new Color(.72f,.52f,.32f),eye=new Vector3(6.57f,2.5f,7.55f),aim=new Vector3(2.19f,.4f,3.17f),fov=36},
                new RoomDef{name="kitchen",floor=Rect.MinMaxRect(2.31f,5.14f,X1,8.85f),color=new Color(.80f,.68f,.50f),eye=new Vector3(-.73f,2.5f,2.97f),aim=new Vector3(3.3f,.4f,7.2f),fov=34},
                new RoomDef{name="bath",floor=Rect.MinMaxRect(.71f,5.14f,2.21f,7.29f),floorY=0,color=new Color(.80f,.85f,.87f),eye=new Vector3(-1.94f,2.4f,2.81f),aim=new Vector3(1.46f,.5f,6.21f),fov=30},
                new RoomDef{name="bedroom",floor=Rect.MinMaxRect(0,7.39f,2.21f,ZN),color=new Color(.70f,.52f,.34f),eye=new Vector3(4.78f,2.5f,5.09f),aim=new Vector3(1.1f,.5f,8.77f),fov=34},
                new RoomDef{name="entrance",floor=Rect.MinMaxRect(2.62f,8.95f,X1,ZN),floorY=-.05f,color=new Color(.58f,.58f,.57f),eye=new Vector3(-.04f,2.5f,6.01f),aim=new Vector3(3.5f,.6f,9.3f),fov=32},
            };
            var env = Environment.GetEnvironmentVariable("ODDITY_APTCAM");
            if (!string.IsNullOrWhiteSpace(env))
                foreach (var part in env.Split(';'))
                {
                    var kv = part.Split('='); if (kv.Length != 2) continue;
                    var v = kv[1].Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); var r = l.FirstOrDefault(q => q.name == kv[0]);
                    if (r != null && v.Length == 7) { r.eye = new Vector3(v[0], v[1], v[2]); r.aim = new Vector3(v[3], v[4], v[5]); r.fov = v[6]; }
                }
            return l;
        }

        public static List<DoorDef> Doors() => new List<DoorDef> {
            new DoorDef{name="kitchen_slider",a="living",b="kitchen",centre=new Vector2((KitSlideA+KitSlideB)/2,5.09f),axis=new Vector2(0,1),half=(KitSlideB-KitSlideA)/2},
            new DoorDef{name="balcony_slider",a="living",b="balcony",centre=new Vector2((GlassA+GlassB)/2,1.25f),axis=new Vector2(0,-1),half=(GlassB-GlassA)/2},
            new DoorDef{name="entrance_arch",a="kitchen",b="entrance",centre=new Vector2((ArchA+ArchB)/2,8.90f),axis=new Vector2(0,1),half=(ArchB-ArchA)/2},
            new DoorDef{name="bath_door",a="kitchen",b="bath",centre=new Vector2(2.26f,(BathDoorA+BathDoorB)/2),axis=new Vector2(-1,0),half=(BathDoorB-BathDoorA)/2},
            new DoorDef{name="bedroom_door",a="kitchen",b="bedroom",centre=new Vector2(2.26f,(BedDoorA+BedDoorB)/2),axis=new Vector2(-1,0),half=(BedDoorB-BedDoorA)/2},
        };

        // ---- the glb props' places (the sofa against the west wall facing east, the dining table on the kitchen's west wall with its two chairs along the wall)
        public const float TeaX0 = 1.96f, TeaX1 = 2.46f, TeaZ0 = 2.72f, TeaZ1 = 3.62f; // the tea table in front of the sofa (sofa front at x 0.82)
        public static readonly Vector3 SofaAt = new Vector3(.425f, 0, 3.17f), TableAt = new Vector3(2.69f, 0, 7.05f);
        public static readonly Vector3 SofaFace = Vector3.right, TableFace = Vector3.forward;
        public static readonly Vector2 HarinStart = new Vector2(3.05f, 3.0f), SomaliStart = new Vector2(1.7f, 4.1f), WhiteStart = new Vector2(3.1f, 2.15f), TabbyStart = new Vector2(3.4f, 4.3f);
        public static readonly Vector2 WindowSpot = new Vector2(1.15f, 1.95f), WindowLook = new Vector2(1.15f, 1.3f); // the floor in front of the sill, and what Harin faces there
        public const float WindowYaw = 180;
        public static readonly Vector3 SunThrough = new Vector3(2.2f, 1.5f, -.1f), SunOnto = new Vector3(.45f, .45f, 3.17f); // the balcony window's middle to the sofa seat
        public static readonly Vector3 LampAt = new Vector3(2.19f, 2.42f, 3.17f);
        // the counter run on the east wall of the kitchen (with the fridge at its north end), the sill, and the balcony ledge: where a cat lands and the floor it hops from
        public static readonly Vector2 CounterStand = new Vector2(3.45f, 6.7f), FridgeLand = new Vector2(4.03f, 8.5f);
        public static readonly Vector2[] CounterLand = { new Vector2(4.08f, 6.68f), new Vector2(4.08f, 7.9f) };
        public static readonly Vector2[] SillLand = { new Vector2(.9f, 1.45f), new Vector2(1.4f, 1.45f) };
        public static readonly Vector2[] LedgeLand = { new Vector2(.5f, .15f), new Vector2(1.1f, .15f) };

        // approximate footprints of the glb props for planner tests (the game measures the real meshes)
        public static List<Rect> PropFootprints() => new List<Rect> {
            Rect.MinMaxRect(.03f, 2.37f, .82f, 3.97f), Rect.MinMaxRect(2.31f, 6.675f, 3.065f, 7.425f), Rect.MinMaxRect(2.45f, 6.17f, 2.93f, 6.68f), Rect.MinMaxRect(2.45f, 7.42f, 2.93f, 7.93f) };

        static FurnBox Fb(string name, float x0, float x1, float y0, float y1, float z0, float z1, Color c, bool block = false, string perch = null)
            => new FurnBox { name = name, x0 = x0, x1 = x1, y0 = y0, y1 = y1, z0 = z0, z1 = z1, color = c, block = block, perch = perch };
        public static List<FurnBox> Furniture()
        {
            Color white = new Color(.93f, .93f, .91f), top = new Color(.30f, .30f, .32f), steel = new Color(.80f, .81f, .82f), dark = new Color(.12f, .12f, .13f), oak = new Color(.74f, .57f, .40f);
            var l = new List<FurnBox> {
                // kitchen: the counter run along the east wall (0.6 deep, z 5.25 -> 8.20), the fridge at its north end (0.7 x 0.6, z 8.20 -> 8.80); the washer went to the balcony
                Fb("counter",3.78f,X1,0,.82f,5.25f,8.20f,white,true,"counter"),
                Fb("worktop",3.77f,X1,.82f,.86f,5.24f,8.20f,top),
                Fb("sink",3.90f,4.28f,.84f,.865f,5.65f,6.35f,steel),
                Fb("tap",4.28f,4.31f,.86f,1.05f,5.97f,6.03f,steel),
                Fb("cooktop",3.88f,4.30f,.86f,.87f,7.0f,7.6f,dark),
                Fb("upper",4.03f,X1,1.48f,2.10f,5.25f,8.20f,white),
                Fb("hood",4.0f,4.34f,1.36f,1.48f,7.0f,7.6f,steel),
                Fb("fridge",3.68f,X1,0,1.78f,8.20f,8.80f,steel,true,"fridge"),
                Fb("fridge_split",3.665f,3.69f,1.05f,1.06f,8.20f,8.80f,dark),
                // living: the TV stand and TV on the east wall facing the sofa, a floor lamp at the sofa's north end, the rug between
                Fb("tv_stand",4.04f,X1,0,.40f,2.55f,3.75f,oak,true),
                Fb("tv",4.22f,4.25f,.44f,.96f,2.68f,3.62f,dark),
                Fb("tv_foot",4.28f,4.36f,.40f,.44f,3.05f,3.25f,dark),
                Fb("lamp_base",.15f,.35f,0,.03f,4.15f,4.35f,dark,true),
                Fb("lamp_pole",.24f,.26f,.03f,1.45f,4.24f,4.26f,dark),
                Fb("rug",1.35f,3.05f,0,.006f,2.45f,3.9f,new Color(.82f,.76f,.66f)),
                // living: the tea table in front of the sofa (0.90 long along z x 0.50 deep, top at 0.40 m, only the top blocks and is a cat perch)
                Fb("teatable_top",TeaX0,TeaX1,.37f,.40f,TeaZ0,TeaZ1,oak,true,"teatable"),
                Fb("teatable_leg1",TeaX0+.03f,TeaX0+.07f,0,.37f,TeaZ0+.03f,TeaZ0+.07f,oak),
                Fb("teatable_leg2",TeaX1-.07f,TeaX1-.03f,0,.37f,TeaZ0+.03f,TeaZ0+.07f,oak),
                Fb("teatable_leg3",TeaX0+.03f,TeaX0+.07f,0,.37f,TeaZ1-.07f,TeaZ1-.03f,oak),
                Fb("teatable_leg4",TeaX1-.07f,TeaX1-.03f,0,.37f,TeaZ1-.07f,TeaZ1-.03f,oak),
                // balcony: wood deck tiles (4 rows of 5, two tones), a drying rack with hung towels and shirts, a laundry basket and detergent by the washer, a 2-tier plant shelf
                // the living room's deep sill (a bench 0.30 deep, top at the window's bottom edge) and the balcony's window ledge, both for cats
                Fb("sill",SillX0,SillX1,0,SillTop,1.30f,1.60f,Trim,true,"sill"),
                Fb("ledge",.3f,1.4f,0,.9f,0,.30f,Trim,true,"ledge"),
                // balcony: the washer at the east end
                Fb("washer",3.78f,X1,0,.85f,.30f,.90f,white,true),
                Fb("washer_door",3.76f,3.78f,.2f,.6f,.45f,.75f,new Color(.55f,.58f,.62f)),
            };
            for (int row = 0; row < 4; row++) for (int i = 0; i < 5; i++)
            {
                float dx0 = i * X1 / 5 + .005f, dx1 = (i + 1) * X1 / 5 - .005f, dz0 = row * .30f + .005f, dz1 = (row + 1) * .30f - .005f;
                l.Add(Fb("deck", dx0, dx1, 0, .012f, dz0, dz1, (row + i) % 2 == 0 ? new Color(.66f, .50f, .34f) : new Color(.58f, .43f, .29f)));
            }
            Color metal = new Color(.78f, .79f, .80f), towelA = new Color(.85f, .35f, .35f), towelB = new Color(.35f, .55f, .80f), shirt = new Color(.92f, .90f, .80f), green = new Color(.55f, .75f, .90f);
            // drying rack x 2.6..3.3, z .10..0.45, 1.45 high: two end frames, a top bar, hung cloth
            l.Add(Fb("rack_frame1", 2.60f, 2.63f, 0, 1.45f, .10f, .45f, metal)); l.Add(Fb("rack_frame2", 3.27f, 3.30f, 0, 1.45f, .10f, .45f, metal));
            l.Add(Fb("rack_bar", 2.60f, 3.30f, 1.42f, 1.45f, .26f, .29f, metal, true)); l.Add(Fb("rack_base", 2.60f, 3.30f, 0, .03f, .26f, .29f, metal));
            l.Add(Fb("towel1", 2.66f, 2.84f, .85f, 1.42f, .22f, .24f, towelA)); l.Add(Fb("towel2", 2.88f, 3.04f, 1.0f, 1.42f, .30f, .32f, towelB));
            l.Add(Fb("shirt1", 3.08f, 3.24f, .90f, 1.42f, .22f, .24f, shirt)); l.Add(Fb("shirt2", 2.86f, 2.98f, .75f, 1.42f, .22f, .24f, green));
            l.Add(Fb("rack_block", 2.60f, 3.30f, 0, .05f, .10f, .45f, metal, true));
            l.Add(Fb("basket", 3.40f, 3.72f, 0, .40f, .30f, .62f, new Color(.80f, .72f, .55f), true)); l.Add(Fb("basket_cloth", 3.43f, 3.69f, .40f, .44f, .33f, .59f, towelB));
            l.Add(Fb("detergent", 4.00f, 4.12f, .85f, 1.08f, .38f, .48f, new Color(.25f, .50f, .85f))); l.Add(Fb("detergent_cap", 4.03f, 4.09f, 1.08f, 1.12f, .41f, .45f, white));
            // plant shelf on the west wall, x .02..0.32, z .35..0.85: posts, tiers at 0.35 and 0.75
            l.Add(Fb("pshelf_t1", .02f, .32f, .33f, .36f, .35f, .85f, Trim, true)); l.Add(Fb("pshelf_t2", .02f, .32f, .73f, .76f, .35f, .85f, Trim));
            l.Add(Fb("pshelf_p1", .03f, .06f, 0, .9f, .36f, .39f, Trim)); l.Add(Fb("pshelf_p2", .28f, .31f, 0, .9f, .81f, .84f, Trim));
            return l;
        }

        // wall pieces (axis-aligned boxes) of one wall around its openings
        public struct Piece { public float x0, x1, y0, y1, z0, z1; }
        public static List<Piece> Pieces(WallDef w)
        {
            var res = new List<Piece>();
            bool ax = w.AlongX;
            void Add(float a, float b, float y0, float y1)
            {
                if (b - a < 1e-3f || y1 - y0 < 1e-3f) return;
                res.Add(ax ? new Piece { x0 = a, x1 = b, y0 = y0, y1 = y1, z0 = w.z0, z1 = w.z1 } : new Piece { x0 = w.x0, x1 = w.x1, y0 = y0, y1 = y1, z0 = a, z1 = b });
            }
            float lo = ax ? w.x0 : w.z0, hi = ax ? w.x1 : w.z1, at = lo;
            foreach (var o in w.openings.OrderBy(o => o.a))
            {
                Add(at, o.a, 0, H);
                Add(o.a, o.b, 0, o.y0); Add(o.a, o.b, o.y1, H);
                at = o.b;
            }
            Add(at, hi, 0, H);
            return res;
        }
        // walking blockers of the walls (not grown): each wall's footprint cut at its door gaps (windows do not open the wall)
        public static List<Rect> WallBlockers()
        {
            var res = new List<Rect>();
            foreach (var w in Walls())
            {
                bool ax = w.AlongX; float lo = ax ? w.x0 : w.z0, hi = ax ? w.x1 : w.z1, at = lo;
                void Add(float a, float b) { if (b - a < 1e-3f) return; res.Add(ax ? Rect.MinMaxRect(a, w.z0, b, w.z1) : Rect.MinMaxRect(w.x0, a, w.x1, b)); }
                foreach (var o in w.openings.Where(o => o.door).OrderBy(o => o.a)) { Add(at, o.a); at = o.b; }
                Add(at, hi);
            }
            return res;
        }
        public static List<Rect> FurnitureBlockers() => Furniture().Where(f => f.block).Select(f => Rect.MinMaxRect(f.x0, f.z0, f.x1, f.z1)).ToList();


        // ---- finishing: door leaves, sliding panels, LED ceiling fixtures and the orbit shot's numbers (data, so the tests can read them)
        public sealed class LeafDef { public string name, wall; public Vector2 hinge, dir; public float w; public Color color; }
        const float LeafAngle = 95f, LeafThick = .04f;
        static LeafDef Leaf(string name, string wall, float hx, float hz, Vector2 closed, Vector2 into, float gap, Color c)
        {
            float a = LeafAngle * Mathf.Deg2Rad; var dir = closed * Mathf.Cos(a) + into * Mathf.Sin(a);
            return new LeafDef { name = name, wall = wall, hinge = new Vector2(hx, hz), dir = dir.normalized, w = gap - .08f, color = c };
        }
        // hinged leaves swung open about 95 degrees into their room, hinge on the low-coordinate jamb, so each gap keeps 0.4 m of clear floor beside the leaf
        public static List<LeafDef> Leaves() => new List<LeafDef> {
            Leaf("bath_door", "kit_W_bath", 2.21f, BathDoorA + .04f, new Vector2(0, 1), new Vector2(-1, 0), BathDoorB - BathDoorA, new Color(.94f, .93f, .90f)),
            Leaf("bedroom_door", "kit_W_bed", 2.21f, BedDoorA + .04f, new Vector2(0, 1), new Vector2(-1, 0), BedDoorB - BedDoorA, new Color(.94f, .93f, .90f)),
            Leaf("front_door", "ent_N", FrontA + .04f, ZN, new Vector2(1, 0), new Vector2(0, -1), FrontB - FrontA, new Color(.84f, .80f, .74f)),
        };
        static Rect LeafRect(LeafDef l)
        {
            var n = new Vector2(-l.dir.y, l.dir.x) * LeafThick / 2; var tip = l.hinge + l.dir * l.w;
            var pts = new[] { l.hinge + n, l.hinge - n, tip + n, tip - n };
            return Rect.MinMaxRect(pts.Min(q => q.x), pts.Min(q => q.y), pts.Max(q => q.x), pts.Max(q => q.y));
        }
        public static float PanelW(float a, float b) => (b - a) / 2 + .04f;
        // the two panels of a sliding door are stacked at its east end, the west part of the gap stays open
        public static List<Rect> SliderStacks()
        {
            var res = new List<Rect>();
            foreach (var w in Walls()) foreach (var o in w.openings.Where(o => o.slider))
            { float mid = (w.z0 + w.z1) / 2; res.Add(Rect.MinMaxRect(o.b - PanelW(o.a, o.b), mid - .03f, o.b, mid + .03f)); }
            return res;
        }
        public static List<Rect> LeafBlockers() => Leaves().Select(LeafRect).Concat(SliderStacks()).ToList();

        public struct Fixture { public string name, room; public float x, z, sx, sz; public bool round; public float power, range; }
        // Korean flush LED lights on the 2.40 m ceiling (0.04 thick): a 1.0 x 0.5 panel in the living room, a bar over the kitchen counter, small flushes elsewhere (sx, sz: size along x and z, round ones use sx as the diameter)
        public static List<Fixture> Fixtures() => new List<Fixture> {
            new Fixture{name="led_living",room="living",x=2.19f,z=3.17f,sx=1.0f,sz=.5f,power=2.4f,range=7f},
            new Fixture{name="led_kitchen",room="kitchen",x=3.80f,z=6.70f,sx=.12f,sz=1.4f,power=1.5f,range=5f},
            new Fixture{name="led_balcony",room="balcony",x=2.19f,z=.60f,sx=.24f,sz=.24f,round=true,power=.9f,range=4f},
            new Fixture{name="led_bath",room="bath",x=1.46f,z=6.21f,sx=.26f,sz=.26f,round=true,power=.9f,range=4f},
            new Fixture{name="led_bedroom",room="bedroom",x=1.10f,z=8.40f,sx=.36f,sz=.36f,power=1.2f,range=5f},
            new Fixture{name="led_entrance",room="entrance",x=3.50f,z=9.55f,sx=.24f,sz=.24f,round=true,power=.9f,range=4f},
        };

        // the orbit shot: Harin stands at OrbitCentre facing south-west, the camera goes round her at eye height inside the living room (0.25 m off every wall, out of anything tall enough to be in its way)
        public static readonly Vector2 OrbitCentre = new Vector2(2.95f, 3.17f);
        public const float OrbitMargin = .25f, OrbitCamY = 1.35f, OrbitAim = 1.25f, OrbitMaxR = 1.4f, OrbitFacingYaw = 225f;
        public static Rect OrbitBox() => Rect.MinMaxRect(OrbitMargin, 1.30f + OrbitMargin, X1 - OrbitMargin, 5.04f - OrbitMargin);
        // furniture the camera would hit at its height (tall, in the living room), grown by the margin
        public static List<Rect> OrbitObstacles()
        {
            var l = Furniture().Where(f => f.y1 > OrbitCamY - .15f && f.z0 > 1.3f && f.z1 < 5.04f).Select(f => Rect.MinMaxRect(f.x0, f.z0, f.x1, f.z1)).ToList();
            l.Add(Rect.MinMaxRect(.10f, 4.10f, .40f, 4.40f)); // the lamp shade
            return l.Select(r => Rect.MinMaxRect(r.xMin - OrbitMargin, r.yMin - OrbitMargin, r.xMax + OrbitMargin, r.yMax + OrbitMargin)).ToList();
        }
        static bool OrbitFree(Vector2 p, Rect box, List<Rect> obs) => box.Contains(p) && !obs.Any(r => p.x > r.xMin && p.x < r.xMax && p.y > r.yMin && p.y < r.yMax);
        static float OrbitRaw(float deg, Rect box, List<Rect> obs)
        {
            float a = deg * Mathf.Deg2Rad; var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            for (float r = OrbitMaxR; r > .3f; r -= .02f) if (OrbitFree(OrbitCentre + d * r, box, obs)) return r;
            return .3f;
        }
        // the camera's radius at an angle (0 = north of her, clockwise seen from above): the smallest free radius within 20 degrees either side, so the radius changes gently
        public static float OrbitRadius(float deg)
        {
            var box = OrbitBox(); var obs = OrbitObstacles(); float m = OrbitMaxR;
            for (float o = -20; o <= 20.01f; o += 4) m = Mathf.Min(m, OrbitRaw(deg + o, box, obs));
            return m;
        }
        public static Vector3 OrbitCamera(float deg) { float r = OrbitRadius(deg), a = deg * Mathf.Deg2Rad; return new Vector3(OrbitCentre.x + Mathf.Sin(a) * r, OrbitCamY, OrbitCentre.y + Mathf.Cos(a) * r); }

        // ---- the inside camera (key I): the camera stands in the room of the character it follows, at eye height, 0.25 m off every wall of that room and out of the furniture
        public const float InsideMargin = .25f, InsideCamY = 1.35f, InsideDist = 1.6f, InsideMinDist = .5f, InsideGoodDist = 1.0f;
        static List<Rect> insideObs; static Dictionary<string, Rect> insideBoxes; static List<Rect> insideSight;
        public static Rect InsideBox(string room)
        {
            if (insideBoxes == null) { insideBoxes = new Dictionary<string, Rect>(); foreach (var r in Rooms()) insideBoxes[r.name] = Rect.MinMaxRect(r.floor.xMin + InsideMargin, r.floor.yMin + InsideMargin, r.floor.xMax - InsideMargin, r.floor.yMax - InsideMargin); }
            return insideBoxes[room];
        }
        static Rect Grow(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);
        // everything above the floor coverings, the glb props' footprints, the door leaves and the sliding stacks, each grown by the margin
        public static List<Rect> InsideObstacles()
        {
            if (insideObs == null)
                insideObs = Furniture().Where(f => f.y1 > .2f).Select(f => Rect.MinMaxRect(f.x0, f.z0, f.x1, f.z1)).Concat(PropFootprints()).Concat(LeafBlockers()).Select(r => Grow(r, InsideMargin)).ToList();
            return insideObs;
        }
        // furniture and door leaves that stand in the way of a sight line at head height (not grown)
        static List<Rect> InsideSightBlockers()
        {
            if (insideSight == null)
                insideSight = Furniture().Where(f => f.y1 > 1.2f && f.y0 < 1.45f).Select(f => Rect.MinMaxRect(f.x0, f.z0, f.x1, f.z1)).Concat(LeafBlockers()).ToList();
            return insideSight;
        }
        public static bool InsideFree(string room, Vector2 p)
        {
            if (!InsideBox(room).Contains(p)) return false;
            foreach (var r in InsideObstacles()) if (p.x > r.xMin && p.x < r.xMax && p.y > r.yMin && p.y < r.yMax) return false;
            return true;
        }
        static bool SegHitsRect(Vector2 a, Vector2 b, Rect r)
        {
            float t0 = 0, t1 = 1; var d = b - a;
            for (int i = 0; i < 2; i++)
            {
                float s = a[i], dd = d[i], lo = i == 0 ? r.xMin : r.yMin, hi = i == 0 ? r.xMax : r.yMax;
                if (Mathf.Abs(dd) < 1e-9f) { if (s < lo || s > hi) return false; continue; }
                float ta = (lo - s) / dd, tb = (hi - s) / dd; if (ta > tb) { var t = ta; ta = tb; tb = t; }
                t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb); if (t0 > t1) return false;
            }
            return true;
        }
        // true when nothing at head height stands between the camera spot and the target (boxes the target itself stands in or on are ignored)
        public static bool InsideSight(Vector2 cam, Vector2 target)
        {
            foreach (var r in InsideSightBlockers()) { if (r.Contains(target) || r.Contains(cam)) continue; if (SegHitsRect(cam, target, r)) return false; }
            return true;
        }
        static Vector2 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }
        // the longest free distance (InsideDist down to InsideMinDist) at one angle round the target (0 = north, clockwise seen from above), 0 when none
        static float InsideReach(string room, Vector2 t, float deg)
        {
            var d = Dir(deg);
            for (float r = InsideDist; r >= InsideMinDist - 1e-3f; r -= .05f) { var p = t + d * r; if (InsideFree(room, p) && InsideSight(p, t)) return r; }
            return 0;
        }
        // the camera spot for a target at t standing in `room`: base + swing degrees round the target, shrunk toward 0.5 m where something is in the way. A blocked angle is skipped:
        // with no swing the farthest free direction is used (the one nearest the wanted angle among equals) and so is a facing direction that only has a cramped spot; with a swing the nearest free angle either side.
        // In a room too small for any ring spot the free spot farthest from the target is used. Returns false only when the room has no free spot at all
        public static bool InsideSpot(string room, Vector2 t, float baseDeg, float swingDeg, out Vector2 cam)
        {
            float want = baseDeg + swingDeg; float rr = InsideReach(room, t, want);
            if (swingDeg != 0)
            {
                if (rr > 0) { cam = t + Dir(want) * rr; return true; }
                for (float o = 5; o <= 180.01f; o += 5) foreach (float sgn in new[] { 1f, -1f }) { float a = want + o * sgn, r = InsideReach(room, t, a); if (r > 0) { cam = t + Dir(a) * r; return true; } }
            }
            else
            {
                if (rr >= InsideGoodDist) { cam = t + Dir(want) * rr; return true; }
                float best = rr, bestAt = want;
                for (float o = 5; o <= 180.01f; o += 5) foreach (float sgn in new[] { 1f, -1f }) { float a = want + o * sgn, r = InsideReach(room, t, a); if (r > best + 1e-4f) { best = r; bestAt = a; } }
                if (best > 0 && (rr <= 0 || best >= rr + .4f)) { cam = t + Dir(bestAt) * best; return true; }
                if (rr > 0) { cam = t + Dir(want) * rr; return true; }
            }
            // too cramped for a ring: the free spot farthest from the target, one with a clear sight line first
            var box = InsideBox(room); Vector2 pick = default; float far = -1; bool seen = false, any = false;
            for (int pass = 0; pass < 2 && !any; pass++) // first only spots within the ring distance, then anywhere
                for (float x = box.xMin; x <= box.xMax + 1e-4f; x += .05f)
                    for (float z = box.yMin; z <= box.yMax + 1e-4f; z += .05f)
                    {
                        var p = new Vector2(x, z); if (!InsideFree(room, p)) continue;
                        float dist = Vector2.Distance(p, t); if (pass == 0 && dist > InsideDist) continue;
                        bool s = InsideSight(p, t);
                        if ((s && !seen) || (s == seen && dist > far)) { seen = s; far = dist; pick = p; any = true; }
                    }
            cam = pick; return any;
        }
        // the default fov for a camera this far from the target: 60 from 1.2 m up, widening to 80 at 0.6 m and below
        public static float InsideFov(float dist) => Mathf.Lerp(80f, 60f, Mathf.InverseLerp(.6f, 1.2f, dist));
        // the room a floor point belongs to (the nearest floor)
        public static string RoomAt(Vector2 p)
        {
            string best = null; float gap = float.MaxValue;
            foreach (var r in Rooms()) { var f = r.floor; float dx = Mathf.Max(f.xMin - p.x, 0, p.x - f.xMax), dz = Mathf.Max(f.yMin - p.y, 0, p.y - f.yMax), g = dx * dx + dz * dz; if (g < gap) { gap = g; best = r.name; } }
            return best;
        }

        // ---- the builder
        public sealed class WallPiece { public string name, wall; public Renderer r; public Vector3 min, max; }
        static GameObject Box(Transform p, string name, float x0, float x1, float y0, float y1, float z0, float z1, Color c)
        {
            var g = RoomPlayRunner.Part(PrimitiveType.Cube, p, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3(x1 - x0, y1 - y0, z1 - z0), c);
            g.name = name; return g;
        }

        public sealed class View
        {
            public readonly List<RoomDef> rooms; public readonly List<DoorDef> doors; readonly List<WallPiece> pieces;
            readonly Dictionary<string, bool[]> hiddenOf = new Dictionary<string, bool[]>();
            public string current; public float lastCut = -10;
            Renderer ceilR; readonly List<Renderer> ceilParts = new List<Renderer>(); readonly List<(string room, Light l)> lamps = new List<(string, Light)>(); bool showAll, showCeil;
            public void SetCeiling(Renderer ceil, List<Renderer> parts) { ceilR = ceil; ceilParts.AddRange(parts); SetCeilingShown(false); }
            public void AddLamp(string room, Light l) { lamps.Add((room, l)); }
            void SetCeilingShown(bool on) { showCeil = on; if (ceilR != null) ceilR.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; foreach (var r in ceilParts) if (r != null) r.enabled = on; }
            public string LampInfo() => string.Join(" ", lamps.Select(q => q.room + "=" + q.l.intensity.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "/r" + q.l.range));
            public void SetLamps(bool on) { foreach (var (_, l) in lamps) l.enabled = on; }
            public View(List<RoomDef> r, List<DoorDef> d, List<WallPiece> p) { rooms = r; doors = d; pieces = p; }
            public RoomDef Room(string name) => rooms.First(r => r.name == name);
            static float Gap(Rect r, Vector2 p) { float dx = Mathf.Max(r.xMin - p.x, 0, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.y, 0, p.y - r.yMax); return Mathf.Sqrt(dx * dx + dz * dz); }
            public string Nearest(Vector2 p) => rooms.OrderBy(r => Gap(r.floor, p)).First().name;
            // true when this call changed the view: the character's centre went 0.2 m past a door's threshold (the band between -0.2 and 0.2 keeps the room it is in), at most one cut per 0.3 s
            public bool Tick(Vector2 p, float now, out string from)
            {
                from = current;
                if (current == null) { Show(Nearest(p)); lastCut = now; return true; }
                if (now - lastCut < .3f) return false;
                string target = null;
                foreach (var d in doors)
                {
                    if (current != d.a && current != d.b) continue;
                    var toward = current == d.a ? d.axis : -d.axis; var perp = new Vector2(-d.axis.y, d.axis.x);
                    float s = Vector2.Dot(p - d.centre, toward), lat = Mathf.Abs(Vector2.Dot(p - d.centre, perp));
                    if (s >= .2f && lat <= d.half + .15f) { target = current == d.a ? d.b : d.a; break; }
                }
                if (target == null && Gap(Room(current).floor, p) > .6f) { var n = Nearest(p); if (n != current) target = n; } // not through a door (a teleport or an odd path): the room it is in now
                if (target == null) return false;
                Show(target); lastCut = now; return true;
            }
            // the walls between the camera and the room: any wall piece a sight line from the camera to a grid of points on the room's floor (and a few at chest height) passes through is hidden
            // (kept as a shadow caster, so the sun still stops at the walls), the others are shown
            bool[] HiddenFor(RoomDef rd)
            {
                if (hiddenOf.TryGetValue(rd.name, out var h)) return h;
                var pts = new List<Vector3>();
                var f = rd.floor; const float inset = .12f;
                for (int i = 0; i < 6; i++) for (int j = 0; j < 6; j++) pts.Add(new Vector3(Mathf.Lerp(f.xMin + inset, f.xMax - inset, i / 5f), rd.floorY + .15f, Mathf.Lerp(f.yMin + inset, f.yMax - inset, j / 5f)));
                foreach (var (u, v) in new[] { (0f, 0f), (1f, 0f), (0f, 1f), (1f, 1f), (.5f, .5f) }) pts.Add(new Vector3(Mathf.Lerp(f.xMin + inset, f.xMax - inset, u), rd.floorY + 1.2f, Mathf.Lerp(f.yMin + inset, f.yMax - inset, v)));
                sightPts[rd.name] = pts; h = new bool[pieces.Count];
                for (int k = 0; k < pieces.Count; k++) foreach (var q in pts) if (SegHitsBox(rd.eye, q, pieces[k].min, pieces[k].max)) { h[k] = true; break; }
                // a wall is cut away whole: a piece left standing over a window or beside a door would float in the picture and hide the floor behind it
                var cut = new HashSet<string>(); for (int k = 0; k < pieces.Count; k++) if (h[k]) cut.Add(pieces[k].wall);
                for (int k = 0; k < pieces.Count; k++) h[k] = cut.Contains(pieces[k].wall);
                cutOf[rd.name] = cut; hiddenOf[rd.name] = h; return h;
            }
            // only the active room is in the picture, as if every room were its own film set: a renderer (floor, furniture, frame, prop) whose centre is outside the room's floor grown by 0.35 m is switched off,
            // wall pieces outside it only cast shadows. Cats outside it are switched off by Near() every frame
            readonly List<Renderer> statics = new List<Renderer>();
            public void AddStatic(Renderer r) { statics.Add(r); }
            readonly List<(string wall, Renderer r)> frames = new List<(string, Renderer)>(); readonly Dictionary<string, HashSet<string>> cutOf = new Dictionary<string, HashSet<string>>();
            public void AddFrame(string wall, Renderer r) { frames.Add((wall, r)); }
            public void Register(Transform t) { foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (!statics.Contains(r) && !ceilParts.Contains(r)) statics.Add(r); if (showAll) ShowAll(showCeil); else if (current != null) Apply(); }
            // the shown zones: the current room's floor grown by 0.35 m; the inside camera adds the rooms joined to it by a door, so they look finished through the openings
            // the outside camera shows the joined rooms too (through glass and open doors); ODDITY_NEIGHBOURS=0 brings back the old single-room picture
            static readonly bool Neighbours = Environment.GetEnvironmentVariable("ODDITY_NEIGHBOURS") != "0";
            readonly Dictionary<string, List<Vector3>> sightPts = new Dictionary<string, List<Vector3>>();
            List<Rect> Zones()
            {
                var l = new List<Rect>(); var names = new List<string> { current };
                if (Inside || Neighbours) foreach (var d in doors) { if (d.a == current) names.Add(d.b); else if (d.b == current) names.Add(d.a); }
                foreach (var n in names.Distinct()) { var f = Room(n).floor; l.Add(Rect.MinMaxRect(f.xMin - .35f, f.yMin - .35f, f.xMax + .35f, f.yMax + .35f)); }
                return l;
            }
            public bool Near(Vector2 p) { foreach (var g in Zones()) if (p.x > g.xMin && p.x < g.xMax && p.y > g.yMin && p.y < g.yMax) return true; return false; }
            bool SkyShown() { if (current == "living" || current == "balcony") return true; if (!Inside && !Neighbours) return false; foreach (var d in doors) if ((d.a == current && (d.b == "living" || d.b == "balcony")) || (d.b == current && (d.a == "living" || d.a == "balcony"))) return true; return false; }
            // the inside camera (key I): no wall of the room is cut away and its ceiling is shown; the rooms beyond the doors stay switched off as in the outside view
            public bool Inside { get; private set; }
            public void SetInside(bool on) { Inside = on; if (current != null) Apply(); }
            void Apply()
            {
                showAll = false; SetCeilingShown(Inside);
                var rd = Room(current); var h = Inside ? new bool[pieces.Count] : HiddenFor(rd); var zones = Zones();
                for (int k = 0; k < pieces.Count; k++)
                {
                    var r = pieces[k].r; if (r == null) continue;
                    var b = r.bounds; bool near = zones.Any(g => b.max.x > g.xMin && b.min.x < g.xMax && b.max.z > g.yMin && b.min.z < g.yMax);
                    r.shadowCastingMode = h[k] || !near ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
                }
                var cutWalls = Inside ? new HashSet<string>() : cutOf[rd.name];
                foreach (var (wn, r) in frames) { if (r == null) continue; var c = r.bounds.center; r.enabled = !cutWalls.Contains(wn) && zones.Any(g => g.Contains(new Vector2(c.x, c.z))); } // the frames of a cut-away wall go with it
                foreach (var r in statics)
                {
                    if (r == null || r.name == "ceiling") continue;
                    if (r.name == "Sky") { r.enabled = SkyShown(); continue; }
                    var c = r.bounds.center; r.enabled = r.name != "slab" && zones.Any(g => g.Contains(new Vector2(c.x, c.z)));
                    // a piece of a neighbouring room that stands between the fixed camera and the current room is hidden, so a neighbour never blocks the picture
                    if (r.enabled && !Inside && Neighbours && !zones[0].Contains(new Vector2(c.x, c.z)) && !r.name.StartsWith("floor_"))
                    {
                        var bb = r.bounds; foreach (var q in sightPts[rd.name]) if (SegHitsBox(rd.eye, q, bb.min, bb.max)) { r.enabled = false; break; }
                    }
                }
            }
            public void Show(string room)
            {
                current = room; Apply();
            }
            public void ShowAll(bool ceiling = false) { showAll = true; SetCeilingShown(ceiling); foreach (var pc in pieces) if (pc.r != null) pc.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; foreach (var r in statics) if (r != null && r.name != "ceiling") r.enabled = true; foreach (var (wn, r) in frames) if (r != null) r.enabled = true; }
            public string HiddenNames(string room) { var h = HiddenFor(Room(room)); return string.Join(" ", pieces.Where((q, k) => h[k]).Select(q => q.name)); }
            public int HiddenCount(string room) => HiddenFor(Room(room)).Count(b => b);
            public int PieceCount => pieces.Count;
            static bool SegHitsBox(Vector3 a, Vector3 b, Vector3 lo, Vector3 hi)
            {
                float t0 = 0, t1 = 1; var d = b - a;
                for (int i = 0; i < 3; i++)
                {
                    float s = a[i], dd = d[i];
                    if (Mathf.Abs(dd) < 1e-9f) { if (s < lo[i] || s > hi[i]) return false; continue; }
                    float ta = (lo[i] - s) / dd, tb = (hi[i] - s) / dd; if (ta > tb) { var t = ta; ta = tb; tb = t; }
                    t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb); if (t0 > t1) return false;
                }
                return true;
            }
        }

        // builds the shell under root (floors, walls with their openings, frames, thresholds, rails, the ceiling that only casts shadows, the sky card) and returns the view logic
        public static View Build(Transform root, Texture2D streetView, List<Rect> furnitureOut, Action<string, float, float, float, float, float, float> perchBox)
        {
            var rooms = Rooms(); var doors = Doors(); var pieces = new List<WallPiece>(); var frames = new List<(string, Renderer)>(); var ceilParts = new List<Renderer>();
            Box(root, "slab", -.4f, X1 + .4f, -.3f, -.07f, -.4f, ZN + .4f, new Color(.30f, .30f, .31f)); // under everything, so door gaps and the margin read as a dark edge
            foreach (var r in rooms)
            {
                var f = r.floor; float y = r.floorY;
                Box(root, "floor_" + r.name, f.xMin, f.xMax, y - .03f, y, f.yMin, f.yMax, r.color);
            }
            // the solid blocks (built-in storage, utility shaft) get a floor too, so cutting their walls away leaves floor and not a hole
            Box(root, "floor_closet", 0, .71f, -.03f, 0, 5.14f, 7.29f, new Color(.80f, .85f, .87f));
            Box(root, "floor_chase", 2.31f, 2.62f, -.08f, -.05f, 8.95f, ZN, new Color(.58f, .58f, .57f));
            foreach (var w in Walls())
            {
                var holder = new GameObject("wall_" + w.name).transform; holder.SetParent(root, false);
                foreach (var p in Pieces(w))
                {
                    var g = Box(holder, "piece", p.x0, p.x1, p.y0, p.y1, p.z0, p.z1, Wall);
                    pieces.Add(new WallPiece { wall = w.name, name = w.name + (p.y0 > 0 ? "^" : p.y1 < H ? "_" : ""), r = g.GetComponent<Renderer>(), min = new Vector3(p.x0, p.y0, p.z0), max = new Vector3(p.x1, p.y1, p.z1) });
                }
                Frames(holder, w); Baseboards(holder, w, rooms, ceilParts, root);
                foreach (Transform c in holder) if (c.name != "piece" && !c.name.EndsWith("_threshold") && !c.name.EndsWith("_rail1") && !c.name.EndsWith("_rail2")) frames.Add((w.name, c.GetComponent<Renderer>()));
            }
            // the entrance: a step 0.05 m down at the arch, the doorway floors are wood thresholds on the wall's thickness (frames builds them)
            var ceil = Box(root, "ceiling", -.3f, X1 + .3f, H, H + .05f, -.3f, ZN + .3f, CeilCol);
            ceil.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            var lampList = new List<(string, Light)>();
            foreach (var fx in Fixtures()) MakeFixture(root, fx, ceilParts, lampList);
            // the view out of the balcony window: a painted card 1.2 m outside, facing in
            var sky = GameObject.CreatePrimitive(PrimitiveType.Quad); UnityEngine.Object.Destroy(sky.GetComponent<Collider>()); sky.name = "Sky"; sky.transform.SetParent(root, false);
            float ww = (X1 - .4f), hh = 1.3f;
            sky.transform.SetPositionAndRotation(new Vector3(X1 / 2, 1.55f + .25f, -1.4f), Quaternion.Euler(0, 180, 0)); sky.transform.localScale = new Vector3(ww * 2.4f, hh * 2.4f, 1);
            var sr = sky.GetComponent<Renderer>(); sr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.white, mainTexture = streetView }; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // furniture boxes
            var fr = new GameObject("Furniture").transform; fr.SetParent(root, false);
            foreach (var b in Furniture())
            {
                Box(fr, b.name, b.x0, b.x1, b.y0, b.y1, b.z0, b.z1, b.color);
                if (b.block) furnitureOut.Add(Rect.MinMaxRect(b.x0, b.z0, b.x1, b.z1));
                if (b.perch != null) perchBox(b.perch, b.x0, b.x1, b.y0, b.y1, b.z0, b.z1);
            }
            // a lamp shade, a plant, the washer drum
            Part(fr, PrimitiveType.Cylinder, new Vector3(.25f, 1.52f, 4.25f), new Vector3(.30f, .11f, .30f), new Color(.95f, .90f, .80f));
            Part(fr, PrimitiveType.Cylinder, new Vector3(.40f, .17f, 1.62f), new Vector3(.28f, .17f, .28f), new Color(.80f, .78f, .74f)); furnitureOut.Add(Rect.MinMaxRect(.26f, 1.48f, .54f, 1.76f));
            foreach (var (dx, dy, dz, k) in new[] { (0f, .62f, 0f, .46f), (.08f, .82f, -.06f, .34f), (-.07f, .76f, .07f, .32f) }) Part(fr, PrimitiveType.Sphere, new Vector3(.40f + dx, dy, 1.62f + dz), Vector3.one * k, new Color(.30f, .48f, .28f));
            // balcony pots (cylinder pot + sphere leaves): a big one in the north-west corner, two on the shelf tiers, a small one by the rack, and one hanging from the ceiling
            void Pot(float px, float py, float pz, float r, float h, float leaf, bool blocks)
            {
                Part(fr, PrimitiveType.Cylinder, new Vector3(px, py + h / 2, pz), new Vector3(r * 2, h / 2, r * 2), new Color(.72f, .42f, .30f));
                Part(fr, PrimitiveType.Sphere, new Vector3(px, py + h + leaf * .35f, pz), Vector3.one * leaf, new Color(.28f + leaf * .1f, .52f, .30f));
                if (blocks) furnitureOut.Add(Rect.MinMaxRect(px - r, pz - r, px + r, pz + r));
            }
            Pot(.22f, 0, 1.05f, .16f, .34f, .55f, true); Pot(.17f, .36f, .55f, .08f, .13f, .24f, false); Pot(.17f, .76f, .70f, .07f, .11f, .22f, false); Pot(2.40f, 0, .17f, .09f, .20f, .26f, true);
            Part(fr, PrimitiveType.Cylinder, new Vector3(3.05f, 1.96f, 1.0f), new Vector3(.005f, .12f, .005f), Alu); Pot(3.05f, 1.62f, 1.0f, .09f, .12f, .30f, false);
            var drum = Part(fr, PrimitiveType.Cylinder, new Vector3(3.76f, .42f, .60f), new Vector3(.34f, .01f, .34f), new Color(.45f, .48f, .52f)); drum.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Finishing(fr);
            var view = new View(rooms, doors, pieces);
            foreach (var (rn, lt) in lampList) view.AddLamp(rn, lt);
            var wallRenderers = new HashSet<Renderer>(pieces.Select(q => q.r));
            var frameSet = new HashSet<Renderer>(frames.Select(q => q.Item2));
            var ceilSet = new HashSet<Renderer>(ceilParts);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (!wallRenderers.Contains(r) && !frameSet.Contains(r) && !ceilSet.Contains(r) && r != ceil.GetComponent<Renderer>()) view.AddStatic(r);
            view.SetCeiling(ceil.GetComponent<Renderer>(), ceilParts);
            foreach (var (wn, r) in frames) view.AddFrame(wn, r);
            return view;
        }
        static GameObject Part(Transform p, PrimitiveType t, Vector3 pos, Vector3 size, Color c) => RoomPlayRunner.Part(t, p, pos, size, c);


        // ---- finishing helpers
        static Material GlassMat(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000; m.SetColor("_BaseColor", c); m.color = c; m.SetFloat("_Smoothness", .85f);
            return m;
        }
        static GameObject Glass(GameObject g, Color c)
        {
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = GlassMat(c); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g;
        }
        static void NoShadow(GameObject g) { g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        // a rotated box (yaw about y) at a centre inside a holder
        static GameObject Rot(Transform p, string name, Vector3 centre, float yaw, Vector3 size, Color c)
        {
            var g = RoomPlayRunner.Part(PrimitiveType.Cube, p, centre, size, c); g.name = name; g.transform.localRotation = Quaternion.Euler(0, yaw, 0); return g;
        }
        static void HingedLeaf(Transform holder, LeafDef l)
        {
            float yaw = Mathf.Atan2(l.dir.x, l.dir.y) * Mathf.Rad2Deg;
            var mid = l.hinge + l.dir * (l.w / 2);
            Rot(holder, l.name + "_leaf", new Vector3(mid.x, 1.025f, mid.y), yaw, new Vector3(LeafThick, 2.05f, l.w), l.color);
            var hp = l.hinge + l.dir * (l.w - .07f);
            Rot(holder, l.name + "_lever", new Vector3(hp.x, 1.0f, hp.y), yaw, new Vector3(.10f, .02f, .12f), new Color(.72f, .73f, .75f));
            foreach (var hy in new[] { .25f, 1.0f, 1.75f }) Rot(holder, l.name + "_hinge", new Vector3(l.hinge.x, hy, l.hinge.y), yaw, new Vector3(.03f, .09f, .03f), new Color(.6f, .6f, .62f));
        }
        // two panels of a sliding door, stacked at the east end of the gap: a frame (white for the frosted kitchen door, aluminium for the balcony) round a glass pane
        static void SliderPanels(Transform holder, WallDef w, Opening o, Func<string, float, float, float, float, float, float, Color, GameObject> Bx)
        {
            bool frosted = o.name == "kitchen_slider"; var fc = frosted ? new Color(.95f, .95f, .93f) : Alu;
            var gc = frosted ? new Color(.90f, .93f, .95f, .55f) : new Color(.70f, .85f, .95f, .14f);
            float pw = PanelW(o.a, o.b), x0 = o.b - pw, x1 = o.b, top = o.y1 - .045f, bot = .02f;
            for (int i = 0; i < 2; i++)
            {
                float c0 = (i == 0 ? -.025f : .025f) - .0175f, c1 = c0 + .035f; string n = o.name + "_p" + i;
                NoShadow(Bx(n + "_l", x0, x0 + .04f, bot, top, c0, c1, fc)); NoShadow(Bx(n + "_r", x1 - .04f, x1, bot, top, c0, c1, fc));
                NoShadow(Bx(n + "_t", x0, x1, top - .05f, top, c0, c1, fc)); NoShadow(Bx(n + "_b", x0, x1, bot, bot + .07f, c0, c1, fc));
                Glass(Bx(n + "_glass", x0 + .04f, x1 - .04f, bot + .07f, top - .05f, (c0 + c1) / 2 - .004f, (c0 + c1) / 2 + .004f, Color.white), gc);
            }
        }
        // skirting boards (0.08 m, 15 mm) on every wall face that looks into a room, in chunks of at most 1 m so each is tested against the rooms, broken at door gaps (pieces that touch the floor only);
        // and a crown strip on the pieces that touch the ceiling (a ceiling part: shown only when the ceiling is)
        static void Baseboards(Transform holder, WallDef w, List<RoomDef> rooms, List<Renderer> ceilParts, Transform root)
        {
            bool ax = w.AlongX;
            bool InRoom(Vector2 q) => rooms.Any(r => r.floor.xMin - .02f <= q.x && q.x <= r.floor.xMax + .02f && r.floor.yMin - .02f <= q.y && q.y <= r.floor.yMax + .02f);
            foreach (var p in Pieces(w))
                foreach (var crown in new[] { false, true })
                {
                    if (crown ? p.y1 < H - 1e-3f : p.y0 > 1e-3f) continue;
                    float lo = ax ? p.x0 : p.z0, hi = ax ? p.x1 : p.z1; int chunks = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / 1f));
                    for (int k = 0; k < chunks; k++)
                    {
                        float a = Mathf.Lerp(lo, hi, k / (float)chunks), b = Mathf.Lerp(lo, hi, (k + 1) / (float)chunks), m = (a + b) / 2;
                        for (int side = 0; side < 2; side++)
                        {
                            float face = ax ? (side == 0 ? w.z0 : w.z1) : (side == 0 ? w.x0 : w.x1), dir = side == 0 ? -1 : 1;
                            if (!InRoom(ax ? new Vector2(m, face + dir * .05f) : new Vector2(face + dir * .05f, m))) continue;
                            float t0 = dir < 0 ? face - (crown ? .02f : .015f) : face, t1 = dir < 0 ? face : face + (crown ? .02f : .015f);
                            float y0 = crown ? H - .05f : 0, y1 = crown ? H : .08f; GameObject g;
                            if (crown) g = ax ? Box(root, "crown", a, b, y0, y1, t0, t1, CeilCol) : Box(root, "crown", t0, t1, y0, y1, a, b, CeilCol);
                            else g = ax ? Box(holder, "baseboard", a, b, y0, y1, t0, t1, BaseCol) : Box(holder, "baseboard", t0, t1, y0, y1, a, b, BaseCol);
                            NoShadow(g); if (crown) ceilParts.Add(g.GetComponent<Renderer>());
                        }
                    }
                }
        }
        // an LED fixture: a white frame 0.04 thick on the ceiling with an emissive face under it, and a soft point light (off until the lamp is on)
        static void MakeFixture(Transform root, Fixture f, List<Renderer> ceilParts, List<(string, Light)> lamps)
        {
            var grp = new GameObject(f.name).transform; grp.SetParent(root, false);
            var emit = new Color(1f, .97f, .90f);
            GameObject frame, face;
            if (f.round)
            {
                frame = RoomPlayRunner.Part(PrimitiveType.Cylinder, grp, new Vector3(f.x, H - .02f, f.z), new Vector3(f.sx, .02f, f.sz), new Color(.93f, .93f, .92f));
                face = RoomPlayRunner.Part(PrimitiveType.Cylinder, grp, new Vector3(f.x, H - .042f, f.z), new Vector3(f.sx - .04f, .003f, f.sz - .04f), emit);
            }
            else
            {
                frame = Box(grp, "frame", f.x - f.sx / 2, f.x + f.sx / 2, H - .04f, H, f.z - f.sz / 2, f.z + f.sz / 2, new Color(.93f, .93f, .92f));
                face = Box(grp, "face", f.x - f.sx / 2 + .02f, f.x + f.sx / 2 - .02f, H - .046f, H - .04f, f.z - f.sz / 2 + .02f, f.z + f.sz / 2 - .02f, emit);
            }
            var m = face.GetComponent<Renderer>().material; m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit * 2.2f);
            NoShadow(frame); NoShadow(face); ceilParts.Add(frame.GetComponent<Renderer>()); ceilParts.Add(face.GetComponent<Renderer>());
            var lg = new GameObject(f.name + "_light"); lg.transform.SetParent(grp, false); lg.transform.position = new Vector3(f.x, H - (f.room == "living" ? .45f : .2f), f.z);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, .96f, .90f); l.intensity = f.power; l.range = f.range; l.shadows = LightShadows.None; l.enabled = false;
            lamps.Add((f.room, l));
        }
        // a light switch plate by the kitchen door, a wall air conditioner and a curtain bunched at the east end of the balcony glass (the sun comes in over the west half)
        static void Finishing(Transform fr)
        {
            Box(fr, "switch_plate", 2.30f, 2.38f, 1.15f, 1.27f, 5.04f - .012f, 5.04f, new Color(.95f, .95f, .93f));
            Box(fr, "switch_btn", 2.32f, 2.36f, 1.19f, 1.23f, 5.04f - .018f, 5.04f - .012f, new Color(.88f, .88f, .86f));
            Box(fr, "ac_body", 1.00f, 1.85f, 1.95f, 2.30f, 5.04f - .21f, 5.04f, new Color(.94f, .94f, .93f));
            Box(fr, "ac_vent", 1.04f, 1.81f, 1.97f, 2.00f, 5.04f - .215f, 5.04f - .18f, new Color(.55f, .56f, .58f));
            Box(fr, "ac_light", 1.70f, 1.74f, 2.21f, 2.23f, 5.04f - .212f, 5.04f - .21f, new Color(.4f, .8f, .5f));
            var curtain = new Color(.78f, .80f, .84f);
            Part(fr, PrimitiveType.Cylinder, new Vector3(4.0f, 2.14f, 1.36f), new Vector3(.02f, .27f, .02f), Alu).transform.localRotation = Quaternion.Euler(0, 0, 90);
            for (int i = 0; i < 4; i++)
            {
                float cx = 3.95f + i * .075f, h = 2.06f; var g = Part(fr, PrimitiveType.Cylinder, new Vector3(cx, h / 2 + .02f, 1.36f + (i % 2) * .03f), new Vector3(.08f, h / 2, .08f), curtain * (i % 2 == 0 ? 1f : .92f)); NoShadow(g);
            }
        }

        // frames of a wall's openings: jambs, a lintel, a sill strip for windows, bars; thresholds and rails for doors (they stay when the wall is cut away)
        static void Frames(Transform holder, WallDef w)
        {
            bool ax = w.AlongX; float t = w.Thick + .02f, mid = ax ? (w.z0 + w.z1) / 2 : (w.x0 + w.x1) / 2;
            GameObject Bx(string n, float a, float b, float y0, float y1, float c0, float c1, Color col) // a..b along the wall, c0..c1 across it (relative to mid)
                => ax ? Box(holder, n, a, b, y0, y1, mid + c0, mid + c1, col) : Box(holder, n, mid + c0, mid + c1, y0, y1, a, b, col);
            foreach (var o in w.openings)
            {
                var col = o.slider ? Alu : Trim; float j = o.slider ? .035f : .04f;
                Bx(o.name + "_jamb_a", o.a, o.a + j, o.y0, o.y1, -t / 2, t / 2, col);
                Bx(o.name + "_jamb_b", o.b - j, o.b, o.y0, o.y1, -t / 2, t / 2, col);
                Bx(o.name + "_lintel", o.a, o.b, o.y1 - .04f, o.y1, -t / 2, t / 2, col);
                if (o.window)
                {
                    Bx(o.name + "_sill", o.a, o.b, o.y0, o.y0 + .04f, -t / 2 - .03f, t / 2 + .03f, col);
                    int n = Mathf.Max(1, Mathf.RoundToInt((o.b - o.a) / 1.1f));
                    for (int i = 1; i < n; i++) { float c = Mathf.Lerp(o.a, o.b, i / (float)n); Bx(o.name + "_bar" + i, c - .015f, c + .015f, o.y0, o.y1, -.02f, .02f, col); }
                    float yc = (o.y0 + o.y1) / 2; Bx(o.name + "_rail", o.a, o.b, yc - .015f, yc + .015f, -.02f, .02f, col);
                    Glass(Bx(o.name + "_glass", o.a + .04f, o.b - .04f, o.y0 + .04f, o.y1 - .04f, -.004f, .004f, Color.white), new Color(.70f, .85f, .95f, .14f));
                }
                if (o.door)
                {
                    Bx(o.name + "_threshold", o.a, o.b, -.07f, .012f, -t / 2, t / 2, new Color(.40f, .28f, .18f));
                    if (o.slider)
                    {
                        Bx(o.name + "_rail1", o.a, o.b, .012f, .02f, -.03f, -.02f, Alu); Bx(o.name + "_rail2", o.a, o.b, .012f, .02f, .02f, .03f, Alu);
                        SliderPanels(holder, w, o, Bx);
                    }
                    else
                    {
                        var lf = Leaves().FirstOrDefault(q => q.name == o.name);
                        if (lf != null) HingedLeaf(holder, lf);
                    }
                }
            }
        }
    }
}
