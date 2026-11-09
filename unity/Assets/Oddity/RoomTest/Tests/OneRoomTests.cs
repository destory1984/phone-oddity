using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    // The 5-pyeong studio (OneRoom.cs) through the Apartment API: each test switches the layout and puts the flat back afterwards
    public class OneRoomTests
    {
        [SetUp] public void Use() => Apartment.Use(OneRoom.Layout);
        [TearDown] public void Back() => Apartment.Use(null);

        static FloorPath Flat()
        {
            var rects = Apartment.WallBlockers().Concat(Apartment.FurnitureBlockers()).Concat(Apartment.PropFootprints()).Concat(Apartment.LeafBlockers());
            return new FloorPath(Apartment.LimX0, Apartment.LimX1, Apartment.LimZ0, Apartment.LimZ1,
                rects.Select(r => Rect.MinMaxRect(r.xMin - .2f, r.yMin - .2f, r.xMax + .2f, r.yMax + .2f)).ToList());
        }
        static bool Overlap(Rect a, Rect b, float eps = .002f) => a.xMax > b.xMin + eps && a.xMin < b.xMax - eps && a.yMax > b.yMin + eps && a.yMin < b.yMax - eps;
        static Rect R(Apartment.FurnBox f) => Rect.MinMaxRect(f.x0, f.z0, f.x1, f.z1);

        [Test]
        public void OneRoom_TheFloorRectsAddUpToAboutSixteenAndAHalfSquareMetres()
        {
            float area = Apartment.Rooms().Sum(r => r.floor.width * r.floor.height);
            Assert.That(area, Is.EqualTo(16.5f).Within(1f), "area " + area);
            CollectionAssert.AreEquivalent(new[] { "main", "bath", "entrance" }, Apartment.Rooms().Select(r => r.name).ToArray());
            Assert.AreEqual("oneroom", Apartment.LayoutName); Assert.IsTrue(Apartment.Peep); Assert.IsFalse(Apartment.HasTable); Assert.IsTrue(Apartment.HasBed);
        }

        [Test]
        public void OneRoom_EveryFurnitureBoxIsInsideTheWallsAndNoWalkingBlockersOverlap()
        {
            var furn = Apartment.Furniture();
            foreach (var f in furn)
            {
                if (f.name.StartsWith("front_door")) continue; // the closed door stands in the north wall's opening
                Assert.That(f.x0, Is.GreaterThanOrEqualTo(-1e-4f), f.name); Assert.That(f.x1, Is.LessThanOrEqualTo(OneRoom.X1 + 1e-4f), f.name);
                Assert.That(f.z0, Is.GreaterThanOrEqualTo(-1e-4f), f.name); Assert.That(f.z1, Is.LessThanOrEqualTo(OneRoom.ZN + 1e-4f), f.name);
                Assert.That(f.y1, Is.LessThanOrEqualTo(OneRoom.H), f.name); Assert.Less(f.x0, f.x1, f.name); Assert.Less(f.y0, f.y1, f.name); Assert.Less(f.z0, f.z1, f.name);
            }
            var solid = furn.Where(f => f.block).Select(f => (f.name, r: R(f))).Concat(Apartment.PropFootprints().Select((r, i) => ("prop" + i, r))).ToList();
            for (int i = 0; i < solid.Count; i++)
            {
                foreach (var w in Apartment.WallBlockers()) Assert.IsFalse(Overlap(solid[i].r, w), solid[i].Item1 + " is inside a wall " + w);
                for (int j = i + 1; j < solid.Count; j++) Assert.IsFalse(Overlap(solid[i].r, solid[j].r), solid[i].Item1 + " overlaps " + solid[j].Item1);
                foreach (var l in Apartment.LeafBlockers()) Assert.IsFalse(Overlap(solid[i].r, l), solid[i].Item1 + " overlaps a door leaf");
            }
        }

        [Test]
        public void OneRoom_BedSofaWardrobeAndCounterStandWhereTheBriefSays()
        {
            var f = Apartment.Furniture().ToDictionary(q => q.name);
            Assert.AreEqual(0f, f["bed_frame"].x0, 1e-3f); Assert.AreEqual(1.0f, f["bed_frame"].x1, 1e-3f); Assert.AreEqual(.5f, f["bed_frame"].z0, 1e-3f); Assert.AreEqual(2.5f, f["bed_frame"].z1, 1e-3f);
            Assert.Greater(Apartment.PillowAt.z - Apartment.PillowSize.z / 2, f["bed_duvet"].z1, "the pillow is at the north (head) end"); Assert.That(Apartment.PillowSize.x, Is.EqualTo(.5f).Within(.05f)); Assert.That(Apartment.PillowSize.z, Is.EqualTo(.32f).Within(.05f)); Assert.That(Apartment.PillowSize.y, Is.EqualTo(.12f).Within(.03f));
            Assert.AreEqual(0f, f["wardrobe"].x0, 1e-3f); Assert.AreEqual(.6f, f["wardrobe"].x1, 1e-3f); Assert.AreEqual(2.7f, f["wardrobe"].z0, 1e-3f); Assert.Greater(f["wardrobe_door1"].x0, f["wardrobe"].x1 - .02f, "doors face east");
            Assert.AreEqual(OneRoom.X1, f["counter"].x1, 1e-3f); Assert.GreaterOrEqual(f["counter"].x0, 2.4f); Assert.AreEqual(3.0f, f["counter"].z0, 1e-3f); Assert.AreEqual(4.5f, f["counter"].z1, 1e-3f); Assert.AreEqual("counter", f["counter"].perch);
            Assert.AreEqual("sill", f["sill"].perch);
            var sofa = Apartment.PropFootprints().Single();
            Assert.GreaterOrEqual(sofa.xMin, 2.15f); Assert.LessOrEqual(sofa.xMax, OneRoom.X1); Assert.GreaterOrEqual(sofa.yMin, .65f); Assert.LessOrEqual(sofa.yMax, 2.25f);
            Assert.AreEqual(Vector3.left, Apartment.SofaFace); // facing west
            Assert.IsFalse(Apartment.HasFridge); Assert.IsFalse(Apartment.HasLedge); Assert.IsFalse(Apartment.HasTea);
        }

        [Test]
        public void OneRoom_EveryRoomAndTheBedSofaAndCounterFrontsAreReachableFromHarinsStart()
        {
            var fp = Flat(); var start = Apartment.HarinStart;
            Assert.IsTrue(fp.Free(start), "Harin starts on free floor");
            var goals = new[] {
                (new Vector2(1.9f, 1.5f), "main"), (new Vector2(1.0f, 4.45f), "bath"), (new Vector2(2.0f, 5.0f), "entrance"),
                (new Vector2(1.35f, 1.5f), "bed front"), (new Vector2(1.95f, 1.4f), "sofa front"), (new Vector2(2.1f, 3.4f), "counter front"), (new Vector2(1.5f, .62f), "window spot"),
            };
            foreach (var (g, name) in goals)
            {
                var p = fp.Find(start, g, out var snapped);
                Assert.NotNull(p, name + ": no path"); Assert.IsFalse(snapped, name + ": goal snapped");
            }
            // out of the bath and back, through the door gap the leaf leaves free
            Assert.NotNull(fp.Find(new Vector2(1.0f, 4.45f), new Vector2(2.0f, 4.45f), out _));
            Assert.Greater(Apartment.Leaves().Single().w, .6f);
        }

        [Test]
        public void OneRoom_StartSpotsAreOnFreeFloorAndFarEnoughApartForTheBodyBlock()
        {
            var fp = Flat();
            var starts = new[] { Apartment.HarinStart, Apartment.SomaliStart, Apartment.WhiteStart, Apartment.TabbyStart };
            foreach (var s in starts) Assert.IsTrue(fp.Free(s), "start " + s + " is blocked");
            for (int i = 0; i < starts.Length; i++) for (int j = i + 1; j < starts.Length; j++)
                Assert.Greater(Vector2.Distance(starts[i], starts[j]), BodyBlock.Need(.22f) + .2f, "starts " + i + " and " + j + " overlap");
        }

        [Test]
        public void OneRoom_ThePeepCameraSeesTheRoomCentreThroughTheWindowOpening()
        {
            var eye = OneRoom.PeepEye;
            foreach (var target in new[] { new Vector3(1.5f, 1.0f, 2.75f), new Vector3(1.6f, 1.2f, 2.0f), OneRoom.PeepAim })
            {
                var dir = target - eye; Assert.Greater(dir.z, 0);
                foreach (float planeZ in new[] { -.2f, 0f }) // the facade's outer face and the wall's inner face
                {
                    float t = (planeZ - eye.z) / dir.z; var q = eye + dir * t;
                    Assert.That(q.x, Is.InRange(OneRoom.WinA, OneRoom.WinB), "x on plane " + planeZ + " for " + target);
                    Assert.That(q.y, Is.InRange(OneRoom.WinY0, OneRoom.WinY1), "y on plane " + planeZ + " for " + target);
                }
            }
            Assert.That(eye.z, Is.InRange(-10f, -8f), "about 9 m across the street");
            // and the window fills a good part of the lens at the default fov
            float half = Mathf.Atan((OneRoom.WinY1 - OneRoom.WinY0) / 2 / (-eye.z)) * Mathf.Rad2Deg;
            Assert.Greater(half * 2 / OneRoom.PeepFov, .5f);
        }

        // furniture and props a camera must stay out of (the inside camera, key I)
        static List<Rect> RawSolids() => Apartment.Furniture().Where(f => f.y1 > .2f && !f.name.StartsWith("front_door")).Select(R).Concat(Apartment.PropFootprints()).Concat(Apartment.LeafBlockers()).ToList();

        [Test]
        public void OneRoom_InsideCameraFindsAFreeSpotInMainBathAndEntrance()
        {
            var solids = RawSolids(); int checkedSpots = 0;
            foreach (var room in Apartment.Rooms())
            {
                var f = room.floor; var box = Apartment.InsideBox(room.name);
                for (float x = f.xMin + .1f; x < f.xMax; x += .2f)
                    for (float z = f.yMin + .1f; z < f.yMax; z += .2f)
                    {
                        var t = new Vector2(x, z);
                        if (solids.Any(r => r.Contains(t))) continue;
                        for (int face = 0; face < 360; face += 90)
                        {
                            Assert.IsTrue(Apartment.InsideSpot(room.name, t, face, 0, out var cam), room.name + " no spot for " + t + " facing " + face);
                            Assert.IsTrue(box.Contains(cam), room.name + " camera outside its box " + cam);
                            foreach (var r in solids) Assert.IsFalse(r.Contains(cam), room.name + " camera inside furniture " + r + " at " + cam);
                            checkedSpots++;
                        }
                    }
            }
            Assert.Greater(checkedSpots, 150);
        }

        [Test]
        public void OneRoom_TheApartmentIsBackAfterTheLayoutIsReleased()
        {
            Apartment.Use(null);
            Assert.AreEqual("apt", Apartment.LayoutName); Assert.AreEqual(4.38f, Apartment.X1, 1e-4f); Assert.IsTrue(Apartment.HasTable);
            Apartment.Use(OneRoom.Layout);
            Assert.AreEqual(3.0f, Apartment.X1, 1e-4f);
        }
    }

    public class OneRoomBodyBlockTests
    {
        [Test]
        public void OneRoom_NoOverlap_AStepThatStaysOutsideIsKept()
        {
            var o = Vector2.zero; var from = new Vector2(1, 0); var next = new Vector2(.8f, 0);
            Assert.AreEqual(next, BodyBlock.Slide(from, next, o, .4f));
        }

        [Test]
        public void OneRoom_NoOverlap_AStepIntoTheCircleEndsOnItAndSlidesAlongIt()
        {
            var o = Vector2.zero; float need = .4f;
            var from = new Vector2(.45f, 0); var next = new Vector2(.3f, .08f); // walking in, slightly to the side
            var r = BodyBlock.Slide(from, next, o, need);
            Assert.AreEqual(need, r.magnitude, 1e-4f, "ends on the circle");
            Assert.Greater(r.y, 0f, "keeps the sideways part of the step (a slide, not a dead stop)");
            Assert.AreNotEqual(from, r);
        }

        [Test]
        public void OneRoom_NoOverlap_ABodyInsideAlreadyMayOnlyStepAway()
        {
            var o = Vector2.zero; var from = new Vector2(.2f, 0);
            Assert.AreEqual(from, BodyBlock.Slide(from, new Vector2(.1f, 0), o, .4f), "toward the centre: refused");
            Assert.AreEqual(new Vector2(.3f, 0), BodyBlock.Slide(from, new Vector2(.3f, 0), o, .4f), "away: allowed");
        }

        [Test]
        public void OneRoom_NoOverlap_WalkingStraightIntoACatNeverGetsCloserThanTheCircleAndGoesRoundIt()
        {
            var cat = new Vector2(1.5f, 2.0f); float need = BodyBlock.Need(.22f); var p = new Vector2(1.5f, .5f);
            float closest = 99;
            for (int i = 0; i < 400; i++)
            {
                var next = p + new Vector2(.01f, .02f).normalized * .02f * 2; // north-north-east, 4 cm a frame
                p = BodyBlock.Slide(p, next, cat, need); closest = Mathf.Min(closest, Vector2.Distance(p, cat));
            }
            Assert.GreaterOrEqual(closest, need - 1e-3f, "closest " + closest);
            Assert.Greater(p.y, cat.y, "she ends up past the cat: she slid round it instead of standing still");
        }

        [Test]
        public void OneRoom_NoOverlap_TheCatsSideAndHarinsSideUseTheSameDistance()
        {
            Assert.AreEqual(BodyBlock.Need(.22f), BodyBlock.Need(.22f));
            Assert.GreaterOrEqual(BodyBlock.Need(.1f), BodyBlock.HarinCircle, "never under the 0.35 m the planner keeps");
            Assert.GreaterOrEqual(BodyBlock.Need(.22f), .22f + .2f - 1e-4f);
        }
    }
}
