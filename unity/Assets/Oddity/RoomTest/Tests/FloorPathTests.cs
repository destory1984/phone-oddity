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
    }
}
