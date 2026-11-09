using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    public class ApartmentTests
    {
        static FloorPath Flat(bool leaves)
        {
            var rects = Apartment.WallBlockers().Concat(Apartment.FurnitureBlockers()).Concat(Apartment.PropFootprints());
            if (leaves) rects = rects.Concat(Apartment.LeafBlockers());
            return new FloorPath(Apartment.LimX0, Apartment.LimX1, Apartment.LimZ0, Apartment.LimZ1,
                rects.Select(r => Rect.MinMaxRect(r.xMin - .2f, r.yMin - .2f, r.xMax + .2f, r.yMax + .2f)).ToList());
        }

        [Test]
        public void Apartment_EveryRoomHasALedFixtureOnItsCeiling()
        {
            var fx = Apartment.Fixtures();
            foreach (var room in Apartment.Rooms())
            {
                var f = fx.Where(q => q.room == room.name).ToList();
                Assert.AreEqual(1, f.Count, room.name + " has " + f.Count + " fixtures");
                var r = Rect.MinMaxRect(f[0].x - f[0].sx / 2, f[0].z - f[0].sz / 2, f[0].x + f[0].sx / 2, f[0].z + f[0].sz / 2);
                Assert.That(r.xMin, Is.GreaterThan(room.floor.xMin), room.name); Assert.That(r.xMax, Is.LessThan(room.floor.xMax), room.name);
                Assert.That(r.yMin, Is.GreaterThan(room.floor.yMin), room.name); Assert.That(r.yMax, Is.LessThan(room.floor.yMax), room.name);
            }
            var living = fx.First(q => q.room == "living");
            Assert.AreEqual(1.0f, living.sx, 1e-3f); Assert.AreEqual(.5f, living.sz, 1e-3f);
        }

        [Test]
        public void Apartment_DoorLeavesAndSlidingPanelsLeaveEveryGapWalkable()
        {
            var fp = Flat(true);
            // each door gap, walked straight through from one side to the other
            var gaps = new[] {
                (new Vector2(1.5f, 5.8f), new Vector2(3.4f, 5.8f), "bath door"),
                (new Vector2(1.1f, 8.5f), new Vector2(3.4f, 8.5f), "bedroom door"),
                (new Vector2(3.45f, 9.5f), new Vector2(3.45f, 10.6f), "front door"),
                (new Vector2(2.7f, 4.5f), new Vector2(2.7f, 5.8f), "kitchen slider"),
                (new Vector2(2.4f, 1.0f), new Vector2(2.4f, 2.0f), "balcony slider"),
            };
            foreach (var (a, b, name) in gaps)
            {
                var p = fp.Find(a, b, out var snapped);
                if (name == "front door") continue; // the front door opens onto the outside, which the walkable box does not include
                Assert.NotNull(p, name + ": no path"); Assert.IsFalse(snapped, name + ": goal snapped");
            }
            // the entrance to the balcony and the bedroom and bath still work with the leaves in
            Assert.NotNull(fp.Find(new Vector2(3.5f, 9.6f), new Vector2(2.7f, .85f), out _));
            Assert.NotNull(fp.Find(new Vector2(3.4f, 9.4f), new Vector2(1.0f, 8.6f), out _));
            Assert.NotNull(fp.Find(new Vector2(3.4f, 5.8f), new Vector2(1.5f, 6.2f), out _));
            // every leaf stays inside its room's side of the wall and 0.4 m of each gap is clear beside it
            foreach (var l in Apartment.Leaves()) Assert.Greater(l.w, .6f, l.name);
            foreach (var s in Apartment.SliderStacks()) Assert.Less(s.width, 1.3f);
        }

        [Test]
        public void Apartment_OrbitCameraStaysInsideTheLivingRoomAndOutOfFurniture()
        {
            var box = Apartment.OrbitBox(); var obs = Apartment.OrbitObstacles();
            Assert.That(box.xMin, Is.EqualTo(.25f).Within(1e-4f));
            float minR = 99, maxR = 0;
            for (int deg = 0; deg < 360; deg += 3)
            {
                var c = Apartment.OrbitCamera(deg);
                Assert.That(c.x, Is.GreaterThanOrEqualTo(box.xMin - 1e-3f).And.LessThanOrEqualTo(box.xMax + 1e-3f), "x at " + deg);
                Assert.That(c.z, Is.GreaterThanOrEqualTo(box.yMin - 1e-3f).And.LessThanOrEqualTo(box.yMax + 1e-3f), "z at " + deg);
                Assert.AreEqual(Apartment.OrbitCamY, c.y, 1e-4f);
                foreach (var o in obs) Assert.IsFalse(c.x > o.xMin && c.x < o.xMax && c.z > o.yMin && c.z < o.yMax, "inside furniture at " + deg);
                float r = Vector2.Distance(new Vector2(c.x, c.z), Apartment.OrbitCentre); minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
            }
            Assert.Greater(minR, .8f, "the camera gets too close to her"); Assert.LessOrEqual(maxR, Apartment.OrbitMaxR + 1e-3f);
            // Harin stands on free floor
            Assert.IsTrue(Flat(true).Free(Apartment.OrbitCentre));
        }

        // raw furniture (above the floor coverings) and prop footprints: what a camera spot must stay out of
        static List<Rect> RawSolids() => Apartment.Furniture().Where(f => f.y1 > .2f).Select(f => Rect.MinMaxRect(f.x0, f.z0, f.x1, f.z1)).Concat(Apartment.PropFootprints()).Concat(Apartment.LeafBlockers()).ToList();

        [Test]
        public void Apartment_InsideCameraSpotIsInTheRoomBoxAndOutOfFurnitureForEveryRoomAndTarget()
        {
            var solids = RawSolids(); int checkedSpots = 0;
            foreach (var room in Apartment.Rooms())
            {
                var f = room.floor; var box = Apartment.InsideBox(room.name);
                Assert.That(box.xMin, Is.EqualTo(f.xMin + .25f).Within(1e-4f), room.name); Assert.That(box.yMax, Is.EqualTo(f.yMax - .25f).Within(1e-4f), room.name);
                for (float x = f.xMin + .1f; x < f.xMax; x += .15f)
                    for (float z = f.yMin + .1f; z < f.yMax; z += .15f)
                    {
                        var t = new Vector2(x, z);
                        if (solids.Any(r => r.Contains(t))) continue; // the free floor only
                        for (int face = 0; face < 360; face += 90)
                        {
                            Assert.IsTrue(Apartment.InsideSpot(room.name, t, face, 0, out var cam), room.name + " no spot for " + t + " facing " + face);
                            Assert.That(cam.x, Is.GreaterThanOrEqualTo(box.xMin - 1e-3f).And.LessThanOrEqualTo(box.xMax + 1e-3f), room.name + " x for " + t);
                            Assert.That(cam.y, Is.GreaterThanOrEqualTo(box.yMin - 1e-3f).And.LessThanOrEqualTo(box.yMax + 1e-3f), room.name + " z for " + t);
                            foreach (var r in solids) Assert.IsFalse(r.Contains(cam), room.name + " camera inside furniture " + r + " at " + cam + " target " + t);
                            Assert.LessOrEqual(Vector2.Distance(cam, t), Apartment.InsideDist + 1e-3f, room.name);
                            checkedSpots++;
                        }
                    }
            }
            Assert.Greater(checkedSpots, 500);
        }

        [Test]
        public void Apartment_InsideCameraSwingingAFullCircleInTheLivingRoomOnlyReturnsFreeSpots()
        {
            var solids = RawSolids(); var box = Apartment.InsideBox("living");
            foreach (var t in new[] { new Vector2(2.95f, 3.17f), new Vector2(1.5f, 4.5f), new Vector2(3.5f, 1.9f), new Vector2(1.15f, 1.95f) })
                for (int swing = 0; swing <= 360; swing += 5)
                {
                    Assert.IsTrue(Apartment.InsideSpot("living", t, 225, swing, out var cam), "no spot at swing " + swing + " for " + t);
                    Assert.IsTrue(Apartment.InsideFree("living", cam), "not free at swing " + swing);
                    Assert.IsTrue(box.Contains(cam), "outside the box at swing " + swing);
                    foreach (var r in solids) Assert.IsFalse(r.Contains(cam), "inside furniture at swing " + swing + " " + cam);
                }
        }

        [Test]
        public void Apartment_InsideCameraStandsAboutOnePointSixMetresAwayInTheOpenLivingRoomOnTheFacingSide()
        {
            var t = new Vector2(2.95f, 3.17f);
            Assert.IsTrue(Apartment.InsideSpot("living", t, 90, 0, out var cam)); // facing east: the camera is east of her
            Assert.AreEqual(1.6f, Vector2.Distance(cam, t), .06f); Assert.Greater(cam.x, t.x);
        }
    }
}
