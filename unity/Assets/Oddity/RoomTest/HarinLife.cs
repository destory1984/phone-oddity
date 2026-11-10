#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Oddity.RoomTest
{
    // Everything Harin can do on her own in the studio. The names follow the keys: P phone, T tv, Shift+T lying tv, B prone phone, K nap, Q coffee,
    // E call, comma wake stretch, period workout, semicolon ramen, quote floor meal, slash laundry, lbracket mirror, rbracket pet, Shift+rbracket wand,
    // X / Shift+X curtain, then the small idles.
    public enum LifeAct { None, Phone, Tv, LieTv, Bed, Nap, Coffee, Call, Wake, Workout, Ramen, Meal, Laundry, Mirror, Pet, Wand, CurtainClose, CurtainOpen, WindowStand, RandomWalk, Yawn, HairSweep, HairToss }

    public struct LifeContext
    {
        public LifeAct Last;          // the activity just finished (None at the start)
        public bool Night;            // the lamp is on / night look
        public bool CurtainOpen;      // the curtain is mostly open (Openness above one half)
        public bool CatOnFloor;       // a cat that is awake, on the floor, not steered with Tab
    }

    public readonly struct LifePick
    {
        public readonly LifeAct Act; public readonly float Seconds; // Seconds 0: it ends by itself (Cap is the safety limit)
        public LifePick(LifeAct act, float seconds) { Act = act; Seconds = seconds; }
    }

    // The weighted choice. Pure C#: the same seed gives the same sequence.
    public static class HarinLife
    {
        public const float WakeAfterNap = .7f, MealAfterRamen = .75f;

        // (min, max) seconds of the timed part; (0, 0) ends by itself
        public static (float min, float max) Range(LifeAct a)
        {
            switch (a)
            {
                case LifeAct.Phone: return (40, 90);
                case LifeAct.Tv: return (60, 150);
                case LifeAct.LieTv: return (60, 150);
                case LifeAct.Bed: return (40, 90);
                case LifeAct.Nap: return (60, 180);
                case LifeAct.Coffee: return (30, 60);
                case LifeAct.Call: return (30, 70);
                case LifeAct.Workout: return (45, 90);
                case LifeAct.Ramen: return (40, 40);
                case LifeAct.Meal: return (40, 80);
                case LifeAct.Mirror: return (25, 50);
                case LifeAct.Pet: return (15, 30);
                case LifeAct.Wand: return (15, 30);
                case LifeAct.WindowStand: return (5, 10);
                default: return (0, 0); // wake, laundry, curtain, walk, yawn, hair: they end by themselves
            }
        }

        // the longest an act that ends by itself may take before she gives up on it
        public static float Cap(LifeAct a)
        {
            switch (a)
            {
                case LifeAct.Laundry: return 160;
                case LifeAct.Wake: return 25;
                case LifeAct.CurtainClose: case LifeAct.CurtainOpen: return 40;
                case LifeAct.RandomWalk: return 30;
                case LifeAct.Yawn: case LifeAct.HairSweep: case LifeAct.HairToss: return 8;
                default: return 400;
            }
        }

        static float Base(LifeAct a)
        {
            switch (a)
            {
                case LifeAct.Phone: return 8;
                case LifeAct.Tv: return 8;
                case LifeAct.LieTv: return 5;
                case LifeAct.Bed: return 7;
                case LifeAct.Nap: return 5;
                case LifeAct.Coffee: return 7;
                case LifeAct.Call: return 5;
                case LifeAct.Wake: return .3f;
                case LifeAct.Workout: return 4;
                case LifeAct.Ramen: return 5;
                case LifeAct.Meal: return .3f;
                case LifeAct.Laundry: return 4;
                case LifeAct.Mirror: return 5;
                case LifeAct.Pet: return 5;
                case LifeAct.Wand: return 4;
                case LifeAct.CurtainClose: return 2;
                case LifeAct.CurtainOpen: return 2;
                case LifeAct.WindowStand: return 4;
                case LifeAct.RandomWalk: return 4;
                case LifeAct.Yawn: return 3;
                case LifeAct.HairSweep: return 2;
                case LifeAct.HairToss: return 2;
                default: return 0;
            }
        }

        public static float Weight(LifeAct a, in LifeContext c)
        {
            if (a == LifeAct.None || a == c.Last) return 0; // never the same twice in a row
            if ((a == LifeAct.Pet || a == LifeAct.Wand) && !c.CatOnFloor) return 0;
            // the window view must stay open most of the time: closing only at night with the curtain open, never by day; opening only if it is shut
            if (a == LifeAct.CurtainClose) return c.Night && c.CurtainOpen ? Base(a) : 0;
            if (a == LifeAct.CurtainOpen) return !c.CurtainOpen ? (c.Night ? Base(a) * .5f : Base(a) * 4) : 0;
            return Base(a);
        }

        public static LifePick Pick(Random rng, in LifeContext c)
        {
            // a meal follows the ramen most of the time, a wake stretch follows a nap
            if (c.Last == LifeAct.Ramen && rng.NextDouble() < MealAfterRamen) return Make(rng, LifeAct.Meal);
            if (c.Last == LifeAct.Nap && rng.NextDouble() < WakeAfterNap) return Make(rng, LifeAct.Wake);
            double total = 0;
            var all = (LifeAct[])Enum.GetValues(typeof(LifeAct));
            foreach (var a in all) total += Weight(a, c);
            double r = rng.NextDouble() * total;
            foreach (var a in all)
            {
                double w = Weight(a, c);
                if (w <= 0) continue;
                if (r < w) return Make(rng, a);
                r -= w;
            }
            return Make(rng, LifeAct.WindowStand);
        }

        static LifePick Make(Random rng, LifeAct a)
        {
            var (min, max) = Range(a);
            float s = max <= min ? min : min + (float)rng.NextDouble() * (max - min);
            return new LifePick(a, s);
        }
    }

    // Who runs Harin: HarinLife or the player. Any Harin input hands her over at once; 20 s without Harin input hands her back.
    public sealed class LifeControl
    {
        public readonly float IdleLimit;
        float idle;
        public bool Auto { get; private set; }
        public LifeControl(float idleLimit, bool auto) { IdleLimit = idleLimit; Auto = auto; }

        // returns true when this input is what switched her off
        public bool Input()
        {
            idle = 0;
            if (!Auto) return false;
            Auto = false; return true;
        }

        // returns true on the tick she goes back to living on her own
        public bool Tick(float dt)
        {
            if (Auto) return false;
            idle += dt;
            if (idle >= IdleLimit) { Auto = true; idle = 0; return true; }
            return false;
        }

        // keys that are not Harin input: camera, cat control, model and test-script plumbing
        public static bool IsHarinKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            switch (key)
            {
                case "tab": case "o": case "i": case "escape": case "m": case "realtab": case "wait": case "freeze": case "unfreeze":
                case "wheelup": case "wheeldown": case "tiltup": case "tiltdown": case "swingleft": case "swingright": case "liftup": case "liftdown":
                case "shiftleft": case "shiftright": case "shiftup": case "shiftdown": case "up": case "down":
                    return false;
            }
            if (key.StartsWith("cam") || key.StartsWith("view") || key.StartsWith("orbit") || key.StartsWith("insideswing")) return false;
            if (key.Length > 2 && (key.StartsWith("gi") || key.StartsWith("shade"))) { int n; if (int.TryParse(key.Substring(key.StartsWith("gi") ? 2 : 5), out n)) return false; }
            return true;
        }
    }
}
#endif
