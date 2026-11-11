using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Oddity.RoomTest
{
    // The seven sleeping poses of the shared cat clips (Sleep<Pose>Down / Idle / Up, 21 clips per cat). Pure data and rules, no Unity objects:
    // the clip names, the Korean names, which surfaces a pose may be used on, whether its footprint fits a perch, and the weighted choice for a resting cat.
    public static class CatSleep
    {
        public const int Count = 7;
        // pose order is the order of the K key's cycle
        public static readonly string[] Id = { "LoafDoze", "Donut", "SideSprawl", "BellyUp", "PawCover", "Superman", "CurlC" };
        public static readonly string[] Korean = { "식빵 졸기", "둥글게 말기", "느슨하게 뻗기", "배 보이기", "앞발로 얼굴 가리기", "쭉 뻗기", "옆으로 말고 자기" };
        public static readonly string[] ScriptKey = { "catsleep1", "catsleep2", "catsleep3", "catsleep4", "catsleep5", "catsleep6", "catsleep7" };
        public const int LoafDoze = 0, Donut = 1, SideSprawl = 2, BellyUp = 3, PawCover = 4, Superman = 5, CurlC = 6;
        // the autonomous weights (percent-like): CurlC 25, Donut 20, LoafDoze 20, SideSprawl 10, PawCover 10, Superman 10, BellyUp 5
        public static readonly int[] Weight = { 20, 20, 10, 5, 10, 10, 25 };

        public enum Place { Floor, Bed, Tower, Sill, Other }

        // phase 0 Down, 1 Idle (loops), 2 Up
        public static string ClipName(int pose, int phase) => "Sleep" + Id[pose] + (phase == 0 ? "Down" : phase == 1 ? "Idle" : "Up");
        public static IEnumerable<string> AllClipNames() { for (int p = 0; p < Count; p++) for (int ph = 0; ph < 3; ph++) yield return ClipName(p, ph); }

        // SideSprawl, BellyUp and Superman (the full side stretch, longer than a tower top) need room: only the floor or the bed
        public static bool Allowed(int pose, Place place) => place == Place.Floor || place == Place.Bed || !(pose == SideSprawl || pose == BellyUp || pose == Superman);

        // the pose's footprint (metres, long and short side, tail included) against a perch top of w x d metres: the short side may overhang 5 cm, the long one 15 cm
        // (the tail and a paw are thin; the tower has a rim). Floor and bed pass a huge perch
        public static bool Fits(float fpLong, float fpShort, float w, float d)
        {
            float pl = Math.Max(w, d), ps = Math.Min(w, d);
            return fpShort <= ps + .05f && fpLong <= pl + .15f;
        }

        // the weight of a pose in one place; 0 when it is not allowed there or does not fit. fits(pose) says whether the footprint fits the perch
        public static float WeightOf(int pose, Place place, bool lampOn, Func<int, bool> fits)
        {
            if (!Allowed(pose, place) || !fits(pose)) return 0;
            float w = Weight[pose];
            bool small = pose == Donut || pose == CurlC || pose == LoafDoze;
            if (small && (place == Place.Tower || place == Place.Bed || place == Place.Sill)) w *= 3; // preferred up high and on the bed
            if (pose == PawCover && lampOn) w *= 2.5f; // lights on: it covers its face
            return w;
        }

        // the pose for a resting cat; -1 when none is allowed and fits
        public static int Choose(Place place, bool lampOn, Func<int, bool> fits, System.Random rng)
        {
            var w = new float[Count];float total = 0;
            for (int i = 0; i < Count; i++) { w[i] = WeightOf(i, place, lampOn, fits); total += w[i]; }
            if (total <= 0) return -1;
            double r = rng.NextDouble() * total;
            for (int i = 0; i < Count; i++) { if (w[i] <= 0) continue; if (r < w[i]) return i; r -= w[i]; }
            for (int i = Count - 1; i >= 0; i--) if (w[i] > 0) return i;
            return -1;
        }

        // the footprints of the Idle clips from the sidecar json (model units): pose -> (long, short). JsonUtility cannot read the dictionary, so a pattern finds the numbers
        public static (float lng, float sht)[] ParseFootprints(string json)
        {
            var fp = new (float, float)[Count];
            for (int p = 0; p < Count; p++)
            {
                var m = Regex.Match(json, "\"" + ClipName(p, 1) + "\"\\s*:\\s*\\{[^}]*?\"footprint_x\"\\s*:\\s*([-0-9.eE]+)\\s*,\\s*\"footprint_y\"\\s*:\\s*([-0-9.eE]+)");
                if (!m.Success) continue;
                float x = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), y = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                fp[p] = (Math.Max(x, y), Math.Min(x, y));
            }
            return fp;
        }
    }
}
