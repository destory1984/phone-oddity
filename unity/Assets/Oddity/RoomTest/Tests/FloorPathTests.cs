using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    public class FloorPathTests
    {
        static FloorPath Room(params Rect[] blockers) => new FloorPath(-3, 3, -3, 3, blockers);

        [Test]
        public void FloorPath_StraightWhenClear()
        {
            var fp = Room(Rect.MinMaxRect(1, 1, 2, 2));
            var p = fp.Find(new Vector2(-2, -2), new Vector2(2, -1), out var snapped);
            Assert.NotNull(p);
            Assert.IsFalse(snapped);
            Assert.AreEqual(2, p.Count);
            Assert.AreEqual(new Vector2(2, -1), p[p.Count - 1]);
            Assert.That(FloorPath.Length(p), Is.EqualTo(Vector2.Distance(new Vector2(-2, -2), new Vector2(2, -1))).Within(1e-3f));
        }

        [Test]
        public void FloorPath_GoesAroundABox()
        {
            var box = Rect.MinMaxRect(-.5f, -.5f, .5f, .5f);
            var fp = Room(box);
            var from = new Vector2(-1.5f, 0); var to = new Vector2(1.5f, 0);
            var p = fp.Find(from, to, out _);
            Assert.NotNull(p);
            float c = .5f + FloorPath.Clear; // the planner keeps a 2 cm clearance
            float analytic = 2 * Mathf.Sqrt((1.5f - c) * (1.5f - c) + c * c) + 2 * c;
            Assert.That(FloorPath.Length(p), Is.EqualTo(analytic).Within(.08f), "detour length " + FloorPath.Length(p) + " vs " + analytic + " pts " + string.Join(" ", p));
            Assert.Greater(FloorPath.Length(p), 3f);
            Assert.LessOrEqual(p.Count, 5);
            // no point and no sampled point of any segment inside the rect
            for (int i = 1; i < p.Count; i++)
                for (int k = 0; k <= 100; k++)
                {
                    var q = Vector2.Lerp(p[i - 1], p[i], k / 100f);
                    Assert.IsFalse(q.x > box.xMin && q.x < box.xMax && q.y > box.yMin && q.y < box.yMax, "inside the box at " + q);
                }
        }

        [Test]
        public void FloorPath_SnapsGoalInsideBlocker()
        {
            var box = Rect.MinMaxRect(-.5f, -.5f, .5f, .5f);
            var fp = Room(box);
            var p = fp.Find(new Vector2(-2, 0), new Vector2(.4f, 0), out var snapped);
            Assert.NotNull(p);
            Assert.IsTrue(snapped);
            var end = p[p.Count - 1];
            Assert.IsTrue(fp.Free(end));
            Assert.That(Vector2.Distance(end, new Vector2(.4f, 0)), Is.LessThanOrEqualTo(.6f));
            // a goal deeper than the snap radius has no free point near
            var big = Room(Rect.MinMaxRect(-2, -2, 2, 2));
            Assert.IsNull(big.Find(new Vector2(-2.8f, 0), new Vector2(0, 0), out _));
        }

        [Test]
        public void FloorPath_UnreachableReturnsNull()
        {
            // a closed ring of walls around the goal
            var fp = Room(
                Rect.MinMaxRect(-1, 1, 1, 1.2f), Rect.MinMaxRect(-1, -1.2f, 1, -1), Rect.MinMaxRect(-1.2f, -1, -1, 1), Rect.MinMaxRect(1, -1, 1.2f, 1));
            Assert.IsNull(fp.Find(new Vector2(-2.5f, 0), new Vector2(0, 0), out _));
            // an opening makes it reachable
            var open = Room(Rect.MinMaxRect(-1, 1, 1, 1.2f), Rect.MinMaxRect(-1, -1.2f, 1, -1), Rect.MinMaxRect(-1.2f, -1, -1, 1));
            Assert.NotNull(open.Find(new Vector2(-2.5f, 0), new Vector2(0, 0), out _));
        }

        // ---- the apartment: the planner on the real walls (door gaps only), the furniture footprints and the props, every rect grown by the body radius 0.2 like RoomPlay does
        static FloorPath Flat()
        {
            var rects = Apartment.WallBlockers().Concat(Apartment.FurnitureBlockers()).Concat(Apartment.PropFootprints())
                .Select(r => Rect.MinMaxRect(r.xMin - .2f, r.yMin - .2f, r.xMax + .2f, r.yMax + .2f)).ToList();
            return new FloorPath(Apartment.LimX0, Apartment.LimX1, Apartment.LimZ0, Apartment.LimZ1, rects);
        }
        // where the path crosses a wall line (axis 0: the line z = at, axis 1: x = at); null when it never does
        static float? CrossAt(List<Vector2> p, int axis, float at)
        {
            for (int i = 1; i < p.Count; i++)
            {
                float a = axis == 0 ? p[i - 1].y : p[i - 1].x, b = axis == 0 ? p[i].y : p[i].x;
                if ((a - at) * (b - at) > 0 || Mathf.Approximately(a, b)) continue;
                float t = (at - a) / (b - a); var q = Vector2.Lerp(p[i - 1], p[i], t);
                return axis == 0 ? q.x : q.y;
            }
            return null;
        }

        [Test]
        public void FloorPath_ApartmentEntranceToBalconyGoesThroughTheDoorGaps()
        {
            var fp = Flat();
            var p = fp.Find(new Vector2(3.5f, 9.6f), new Vector2(2.7f, .85f), out var snapped);
            Assert.NotNull(p, "no path from the entrance to the balcony");
            Assert.IsFalse(snapped);
            var arch = CrossAt(p, 0, 8.90f); var slider = CrossAt(p, 0, 5.09f); var glass = CrossAt(p, 0, 1.25f);
            Assert.NotNull(arch, "does not cross the kitchen entrance wall"); Assert.NotNull(slider, "does not cross the living room wall"); Assert.NotNull(glass, "does not cross the balcony partition");
            Assert.That(arch.Value, Is.InRange(Apartment.ArchA, Apartment.ArchB), "outside the arch at " + arch);
            Assert.That(slider.Value, Is.InRange(Apartment.KitSlideA, Apartment.KitSlideB), "outside the kitchen sliding door at " + slider);
            Assert.That(glass.Value, Is.InRange(Apartment.GlassA, Apartment.GlassB), "outside the balcony glass door at " + glass);
            // never inside a wall's footprint
            foreach (var w in Apartment.WallBlockers())
                for (int i = 1; i < p.Count; i++) for (int k = 0; k <= 50; k++)
                {
                    var q = Vector2.Lerp(p[i - 1], p[i], k / 50f);
                    Assert.IsFalse(q.x > w.xMin && q.x < w.xMax && q.y > w.yMin && q.y < w.yMax, "inside a wall at " + q);
                }
        }

        [Test]
        public void FloorPath_ApartmentBedroomAndBathAreReachedByTheirDoors()
        {
            var fp = Flat();
            var bed = fp.Find(new Vector2(3.4f, 9.4f), new Vector2(1.0f, 8.6f), out _);
            Assert.NotNull(bed, "no path to the bedroom");
            Assert.That(CrossAt(bed, 1, 2.26f).Value, Is.InRange(Apartment.BedDoorA, Apartment.BedDoorB));
            var bath = fp.Find(new Vector2(3.4f, 5.8f), new Vector2(1.5f, 6.2f), out _);
            Assert.NotNull(bath, "no path to the bath");
            Assert.That(CrossAt(bath, 1, 2.26f).Value, Is.InRange(Apartment.BathDoorA, Apartment.BathDoorB));
        }

        [Test]
        public void FloorPath_TeaTableBlocksWalking()
        {
            var fp = Flat();
            float mz = (Apartment.TeaZ0 + Apartment.TeaZ1) / 2;
            // a goal on the table top is not free and snaps out to the floor beside it
            Assert.IsFalse(fp.Free(new Vector2((Apartment.TeaX0 + Apartment.TeaX1) / 2, mz)));
            // the straight line from sofa side to the far side of the table is longer than the direct one
            var from = new Vector2(Apartment.TeaX0 - .45f, mz); var to = new Vector2(Apartment.TeaX1 + .45f, mz);
            var p = fp.Find(from, to, out _);
            Assert.NotNull(p);
            Assert.Greater(FloorPath.Length(p), Vector2.Distance(from, to) + .3f, "walks straight through the tea table");
            // the balcony slider to the kitchen slider and the washer front stay open
            Assert.NotNull(fp.Find(new Vector2(3.0f, .9f), new Vector2(3.4f, 4.6f), out _));
            Assert.NotNull(fp.Find(new Vector2(3.0f, 1.15f), new Vector2(3.95f, 1.15f), out _));
        }
    }
}
