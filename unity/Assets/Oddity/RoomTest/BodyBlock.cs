using UnityEngine;

namespace Oddity.RoomTest
{
    // Harin and the cats are solid to each other: a body may not enter another's circle, it slides round it. Pure geometry so the tests can run it.
    public static class BodyBlock
    {
        // Harin's circle seen by a cat (and a cat's foot centre seen by Harin): the same 0.35 m the cats' path planner already keeps from her
        public const float HarinCircle = .35f;

        // the least distance between Harin's centre and a cat's foot centre: a cat's body radius plus Harin's 0.2 m, never under the planner's 0.35
        public static float Need(float catRadius) => Mathf.Max(HarinCircle, catRadius + .2f);

        // the position to move to when stepping from `from` to `next` with another body's circle (centre `other`, radius `need`) in the way:
        // a step that stays outside is kept; one that ends inside is pushed out along the line from the other's centre (a slide round the circle);
        // a body that already stands inside (a spawn, a seat it just left) may only step away
        public static Vector2 Slide(Vector2 from, Vector2 next, Vector2 other, float need)
        {
            var d = next - other;
            if (d.magnitude >= need) return next;
            var f = from - other;
            if (f.magnitude < need) return d.magnitude > f.magnitude ? next : from; // already inside: only steps that lead out
            if (d.sqrMagnitude < 1e-8f) d = f;
            return other + d.normalized * need;
        }
    }
}
