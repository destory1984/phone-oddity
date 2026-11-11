using System;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Shared finger helpers for the prop rigs. All of them rotate bones in world space after the hand has been posed,
    // so call them straight after the hand rotation is written.
    public static class HandPose
    {
        const float MinLength = .0001f;

        // Brings index, middle, ring and little finger together: each proximal bone swings about the palm normal until its direction
        // (inside the palm plane) matches the middle finger's. amount 0 = untouched, 1 = parallel.
        public static void Adduct(Transform[] proximal, Transform[] intermediate, Vector3 palmNormal, float amount)
        {
            if (amount <= 0 || proximal == null || intermediate == null || proximal.Length < 4 || intermediate.Length < 4) return;
            if (proximal[1] == null || intermediate[1] == null) return;
            var n = palmNormal.normalized;
            var m = Vector3.ProjectOnPlane(intermediate[1].position - proximal[1].position, n);
            if (m.sqrMagnitude < MinLength * MinLength) return;
            for (int f = 0; f < 4; f++)
            {
                if (f == 1 || proximal[f] == null || intermediate[f] == null) continue;
                var d = Vector3.ProjectOnPlane(intermediate[f].position - proximal[f].position, n);
                if (d.sqrMagnitude < MinLength * MinLength) continue;
                proximal[f].rotation = Quaternion.AngleAxis(Vector3.SignedAngle(d, m, n) * amount, n) * proximal[f].rotation;
            }
        }

        // Finger tip pad: the distal joint plus a short extension along the last bone.
        public static Vector3 Tip(Transform proximal, Transform intermediate, Transform distal, float extension)
        {
            if (distal == null) return intermediate != null ? intermediate.position : proximal.position;
            var from = intermediate != null ? intermediate : proximal;
            return distal.position + (distal.position - from.position).normalized * extension;
        }

        // Curls one chain (3 joints about one world axis) a little more each step until the tip is within `reach` of the target surface
        // (distanceToSurface returns the tip's distance to the thing being held, negative = inside it). Returns the final tip distance.
        public static float Wrap(Transform a, Transform b, Transform c, Vector3 axis, float maxA, float maxB, float maxC, float extension,
            Func<Vector3, float> distanceToSurface, float reach)
        {
            if (a == null || b == null || c == null) return float.NaN;
            var la = a.localRotation; var lb = b.localRotation; var lc = c.localRotation;
            float result = float.NaN;
            const int steps = 40;
            for (int i = 0; i <= steps; i++)
            {
                a.localRotation = la; b.localRotation = lb; c.localRotation = lc;
                float s = (float)i / steps;
                Rot(a, axis, maxA * s); Rot(b, axis, maxB * s); Rot(c, axis, maxC * s);
                result = distanceToSurface(c.position + (c.position - b.position).normalized * extension);
                if (result <= reach) break;
            }
            return result;
        }

        static void Rot(Transform t, Vector3 axis, float degrees) { t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation; }
    }
}
