using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oddity.RoomTest
{
    // Shortest free walk on the room floor. Pure geometry (no physics): a box of floor limits, blocker rects (x, z as Rect x, y) that are already grown
    // by the walker's radius, and optionally one temporary circle (Harin, for the cats). Grid A* on 0.05 m cells, 8-way, no corner cutting, then
    // line-of-sight smoothing so the result is a few straight segments hugging corners with a small clearance.
    public sealed class FloorPath
    {
        public const float Cell = .05f, Clear = .02f;
        readonly float x0, z0, x1, z1;
        readonly Rect[] rects; // grown by Clear
        readonly int nx, nz;
        readonly bool[] blocked;

        public FloorPath(float xMin, float xMax, float zMin, float zMax, IEnumerable<Rect> blockers)
        {
            x0 = xMin; x1 = xMax; z0 = zMin; z1 = zMax;
            var list = new List<Rect>();
            foreach (var r in blockers) list.Add(Rect.MinMaxRect(r.xMin - Clear, r.yMin - Clear, r.xMax + Clear, r.yMax + Clear));
            rects = list.ToArray();
            nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / Cell) + 1);
            nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / Cell) + 1);
            blocked = new bool[nx * nz];
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) blocked[j * nx + i] = PointBlocked(Center(i, j));
        }

        Vector2 Center(int i, int j) => new Vector2(Mathf.Min(x0 + i * Cell, x1), Mathf.Min(z0 + j * Cell, z1));
        bool InBox(Vector2 p) => p.x >= x0 - 1e-4f && p.x <= x1 + 1e-4f && p.y >= z0 - 1e-4f && p.y <= z1 + 1e-4f;
        bool PointBlocked(Vector2 p)
        {
            if (!InBox(p)) return true;
            foreach (var r in rects) if (p.x > r.xMin && p.x < r.xMax && p.y > r.yMin && p.y < r.yMax) return true;
            return false;
        }
        static bool InCircle(Vector2 p, Vector2? c, float r) => c.HasValue && (p - c.Value).sqrMagnitude < r * r;

        public bool Free(Vector2 p) => !PointBlocked(p);

        // true when p is a free point in the largest connected open area of the floor (not a sealed pocket such as a strip between a chair and the wall)
        int[] comp; int mainComp = -1;
        public bool InMainArea(Vector2 p)
        {
            if (PointBlocked(p)) return false;
            if (comp == null) Label();
            int i = Mathf.Clamp(Mathf.RoundToInt((p.x - x0) / Cell), 0, nx - 1), j = Mathf.Clamp(Mathf.RoundToInt((p.y - z0) / Cell), 0, nz - 1);
            if (blocked[j * nx + i]) { var n = NearestCell(i, j); if (n < 0) return false; i = n % nx; j = n / nx; }
            return comp[j * nx + i] == mainComp;
        }
        void Label()
        {
            comp = new int[nx * nz]; for (int k = 0; k < comp.Length; k++) comp[k] = -1;
            int id = 0, bestSize = -1; var q = new Queue<int>();
            for (int k = 0; k < comp.Length; k++)
            {
                if (blocked[k] || comp[k] >= 0) continue;
                int size = 0; comp[k] = id; q.Enqueue(k);
                while (q.Count > 0)
                {
                    int c = q.Dequeue(); size++; int ci = c % nx, cj = c / nx;
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int ni = ci + di, nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                        int n = nj * nx + ni;
                        if (blocked[n] || comp[n] >= 0) continue;
                        if (di != 0 && dj != 0 && (blocked[cj * nx + ni] || blocked[nj * nx + ci])) continue;
                        comp[n] = id; q.Enqueue(n);
                    }
                }
                if (size > bestSize) { bestSize = size; mainComp = id; }
                id++;
            }
        }

        // the nearest free point within maxR (the point itself when free); null when none
        public Vector2? NearestFree(Vector2 p, float maxR)
        {
            if (!PointBlocked(p)) return p;
            Vector2? best = null; float bd = float.MaxValue;
            int rad = Mathf.CeilToInt(maxR / Cell);
            int ci = Mathf.RoundToInt((p.x - x0) / Cell), cj = Mathf.RoundToInt((p.y - z0) / Cell);
            for (int j = cj - rad; j <= cj + rad; j++) for (int i = ci - rad; i <= ci + rad; i++)
            {
                if (i < 0 || j < 0 || i >= nx || j >= nz || blocked[j * nx + i]) continue;
                var c = Center(i, j); float d = (c - p).magnitude;
                if (d <= maxR && d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // the segment a-b stays inside the floor box and out of every rect (and the circle)
        public bool LineFree(Vector2 a, Vector2 b, Vector2? circle = null, float circleR = 0)
        {
            if (!InBox(a) || !InBox(b)) return false;
            foreach (var r in rects) if (SegHitsRect(a, b, r)) return false;
            if (circle.HasValue && SegDist(circle.Value, a, b) < circleR) return false;
            return true;
        }
        static bool SegHitsRect(Vector2 a, Vector2 b, Rect r)
        {
            float t0 = 0, t1 = 1, dx = b.x - a.x, dz = b.y - a.y;
            if (!Slab(a.x, dx, r.xMin, r.xMax, ref t0, ref t1)) return false;
            if (!Slab(a.y, dz, r.yMin, r.yMax, ref t0, ref t1)) return false;
            return t1 - t0 > 1e-5f || (t1 >= t0 && (b - a).sqrMagnitude < 1e-10f);
        }
        static bool Slab(float s, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-9f) return s > lo && s < hi;
            float ta = (lo - s) / d, tb = (hi - s) / d;
            if (ta > tb) { var t = ta; ta = tb; tb = t; }
            t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb);
            return t0 < t1;
        }
        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            float t = l < 1e-10f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l);
            return (a + ab * t - p).magnitude;
        }

        public static float Length(IList<Vector2> path)
        {
            float s = 0; for (int i = 1; i < path.Count; i++) s += (path[i] - path[i - 1]).magnitude; return s;
        }

        // the path from..to (first point from, last point the goal actually used), or null when unreachable. A goal in a blocker snaps to the nearest free
        // point within snapR (snapped true); a start inside a blocker (getting up from a seat) first steps to the nearest free point. The circle only
        // counts when neither end is inside it.
        public List<Vector2> Find(Vector2 from, Vector2 to, out bool snapped, Vector2? circle = null, float circleR = 0, float snapR = .6f)
        {
            snapped = false;
            to = new Vector2(Mathf.Clamp(to.x, x0, x1), Mathf.Clamp(to.y, z0, z1));
            if (PointBlocked(to))
            {
                var s = NearestFree(to, snapR); if (!s.HasValue) return null;
                to = s.Value; snapped = true;
            }
            var res = new List<Vector2> { from };
            var start = from;
            if (PointBlocked(from))
            {
                var s = NearestFree(from, 1f); if (!s.HasValue) return null;
                start = s.Value; res.Add(start);
            }
            if (InCircle(start, circle, circleR) || InCircle(to, circle, circleR)) circle = null;
            if (LineFree(start, to, circle, circleR)) { res.Add(to); return Dedup(res); }

            var path = AStar(start, to, circle, circleR);
            if (path == null) return null;
            // string pulling
            var pts = new List<Vector2> { start }; pts.AddRange(path); pts.Add(to);
            var outp = new List<Vector2> { start };
            int cur = 0;
            while (cur < pts.Count - 1)
            {
                int far = cur + 1;
                for (int k = pts.Count - 1; k > cur + 1; k--) if (LineFree(pts[cur], pts[k], circle, circleR)) { far = k; break; }
                outp.Add(pts[far]); cur = far;
            }
            outp.RemoveAt(0);
            Tighten(start, outp, circle, circleR);
            res.AddRange(outp);
            return Dedup(res);
        }
        // grid waypoints sit on cell centers a little off the corners they bend around: move each to the nearest blocker corner (just outside it) when both neighbours still see it
        void Tighten(Vector2 start, List<Vector2> pts, Vector2? circle, float circleR)
        {
            const float eps = .003f;
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var prev = i == 0 ? start : pts[i - 1]; var next = pts[i + 1];
                Vector2? best = null; float bd = .6f, cur = (prev - pts[i]).magnitude + (pts[i] - next).magnitude;
                foreach (var r in rects)
                    for (int cx = 0; cx < 2; cx++) for (int cz = 0; cz < 2; cz++)
                    {
                        var c = new Vector2(cx == 0 ? r.xMin - eps : r.xMax + eps, cz == 0 ? r.yMin - eps : r.yMax + eps);
                        float d = (c - pts[i]).magnitude;
                        if (d < bd && (prev - c).magnitude + (c - next).magnitude <= cur + 1e-4f && !PointBlocked(c) && LineFree(prev, c, circle, circleR) && LineFree(c, next, circle, circleR)) { bd = d; best = c; }
                    }
                if (best.HasValue) pts[i] = best.Value;
            }
        }
        static List<Vector2> Dedup(List<Vector2> l)
        {
            for (int i = l.Count - 1; i > 0; i--) if ((l[i] - l[i - 1]).sqrMagnitude < 1e-8f) l.RemoveAt(i);
            return l;
        }

        // cell centers from the cell nearest start to the cell nearest goal (exclusive of both ends); null when the goal cell is cut off
        List<Vector2> AStar(Vector2 start, Vector2 goal, Vector2? circle, float circleR)
        {
            int si = Mathf.Clamp(Mathf.RoundToInt((start.x - x0) / Cell), 0, nx - 1), sj = Mathf.Clamp(Mathf.RoundToInt((start.y - z0) / Cell), 0, nz - 1);
            int gi = Mathf.Clamp(Mathf.RoundToInt((goal.x - x0) / Cell), 0, nx - 1), gj = Mathf.Clamp(Mathf.RoundToInt((goal.y - z0) / Cell), 0, nz - 1);
            if (blocked[sj * nx + si]) { var n = NearestCell(si, sj); if (n < 0) return null; si = n % nx; sj = n / nx; }
            if (blocked[gj * nx + gi]) { var n = NearestCell(gi, gj); if (n < 0) return null; gi = n % nx; gj = n / nx; }
            int N = nx * nz;
            var g = new float[N]; var from = new int[N]; var closed = new bool[N];
            for (int i = 0; i < N; i++) { g[i] = float.MaxValue; from[i] = -1; }
            var open = new SortedSet<(float f, int id)>();
            int s0 = sj * nx + si, g0 = gj * nx + gi;
            g[s0] = 0; open.Add((H(si, sj, gi, gj), s0));
            const float D = 1.41421356f;
            while (open.Count > 0)
            {
                var top = open.Min; open.Remove(top);
                int id = top.id; if (closed[id]) continue; closed[id] = true;
                if (id == g0) break;
                int ci = id % nx, cj = id / nx;
                for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                {
                    if (di == 0 && dj == 0) continue;
                    int ni = ci + di, nj = cj + dj;
                    if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                    int nid = nj * nx + ni;
                    if (closed[nid] || Bad(ni, nj, circle, circleR)) continue;
                    if (di != 0 && dj != 0 && (Bad(ci + di, cj, circle, circleR) || Bad(ci, cj + dj, circle, circleR))) continue; // no corner cutting
                    float ng = g[id] + (di != 0 && dj != 0 ? D : 1f);
                    if (ng < g[nid]) { g[nid] = ng; from[nid] = id; open.Add((ng + H(ni, nj, gi, gj), nid)); }
                }
            }
            if (from[g0] < 0 && g0 != s0) return null;
            var rev = new List<Vector2>();
            for (int id = g0; id != -1; id = from[id]) rev.Add(Center(id % nx, id / nx));
            rev.Reverse();
            return rev;
        }
        bool Bad(int i, int j, Vector2? circle, float circleR) => blocked[j * nx + i] || InCircle(Center(i, j), circle, circleR);
        static float H(int i, int j, int gi, int gj)
        {
            float dx = Mathf.Abs(i - gi), dz = Mathf.Abs(j - gj);
            return (dx + dz) + (1.41421356f - 2f) * Mathf.Min(dx, dz);
        }
        int NearestCell(int i, int j)
        {
            for (int r = 1; r <= 20; r++)
                for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++)
                {
                    if (Mathf.Max(Mathf.Abs(di), Mathf.Abs(dj)) != r) continue;
                    int ni = i + di, nj = j + dj;
                    if (ni >= 0 && nj >= 0 && ni < nx && nj < nz && !blocked[nj * nx + ni]) return nj * nx + ni;
                }
            return -1;
        }
    }
}
