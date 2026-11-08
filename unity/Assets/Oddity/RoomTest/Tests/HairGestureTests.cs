using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    public class HairGestureTests
    {
        static AnimationClip Idle()
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Oddity/DevMotion/Resources/Motion/CatwalkIdleSkin.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            return null;
        }
        static float Muscle(AnimationClip clip, string name, float t)
        {
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (b.propertyName == name) return AnimationUtility.GetEditorCurve(clip, b).Evaluate(t);
            return float.NaN;
        }

        [Test]
        public void HairGesture_ClipLengthsAreTossAndSweep()
        {
            var toss = HairGestures.CreateClip("t", HairGestures.TossLength, HairGestures.SampleToss, null);
            var sweep = HairGestures.CreateClip("s", HairGestures.SweepLength, HairGestures.SampleSweep, null);
            Assert.That(toss.length, Is.EqualTo(1.4f).Within(.05f));
            Assert.That(sweep.length, Is.EqualTo(2.2f).Within(.05f));
        }

        [Test]
        public void HairGesture_EveryMuscleValueStaysWithinMinusOneAndOne()
        {
            var idle = Idle();
            var baseValues = idle != null ? HairGestures.ReadBase(idle) : new Dictionary<string, float>();
            foreach (var (length, sample) in new (float, System.Action<float, float[]>)[] { (HairGestures.TossLength, HairGestures.SampleToss), (HairGestures.SweepLength, HairGestures.SampleSweep) })
            {
                var clip = HairGestures.CreateClip("x", length, sample, baseValues);
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!HairGestures.IsMuscle(b.propertyName)) continue;
                    foreach (var k in AnimationUtility.GetEditorCurve(clip, b).keys)
                        Assert.That(k.value, Is.InRange(-1f, 1f), b.propertyName + " at " + k.time);
                }
            }
        }

        [Test]
        public void HairGesture_FirstAndLastFrameOffsetsAreZero()
        {
            var m = new float[HumanTrait.MuscleCount];
            foreach (var (length, sample) in new (float, System.Action<float, float[]>)[] { (HairGestures.TossLength, HairGestures.SampleToss), (HairGestures.SweepLength, HairGestures.SampleSweep) })
                foreach (float t in new[] { 0f, length })
                {
                    sample(t, m);
                    Assert.That(m.Max(v => Mathf.Abs(v)), Is.LessThan(1e-3f), "t=" + t);
                }
        }

        [Test]
        public void HairGesture_ClipStartsAndEndsOnTheIdlePose()
        {
            var idle = Idle();
            if (idle == null) Assert.Ignore("no CatwalkIdleSkin.fbx (git-ignored Mixamo folder)");
            var baseValues = HairGestures.ReadBase(idle);
            Assert.That(baseValues.Count, Is.GreaterThan(50), "the idle clip should expose its Humanoid curves");
            foreach (var (length, sample) in new (float, System.Action<float, float[]>)[] { (HairGestures.TossLength, HairGestures.SampleToss), (HairGestures.SweepLength, HairGestures.SampleSweep) })
            {
                var clip = HairGestures.CreateClip("x", length, sample, baseValues);
                foreach (var kv in baseValues)
                {
                    float expect = Mathf.Clamp(kv.Value, -1f, 1f);
                    if (!HairGestures.IsMuscle(kv.Key)) expect = kv.Value;
                    Assert.That(Muscle(clip, kv.Key, 0), Is.EqualTo(expect).Within(1e-3f), kv.Key + " first");
                    Assert.That(Muscle(clip, kv.Key, clip.length), Is.EqualTo(expect).Within(1e-3f), kv.Key + " last");
                }
            }
        }

        [Test]
        public void HairGesture_TossHasTwoFlicksTheSecondSmaller()
        {
            var m = new float[HumanTrait.MuscleCount];
            int tilt = System.Array.IndexOf(HumanTrait.MuscleName, "Head Tilt Left-Right");
            var values = new List<float>();
            const float step = .002f;
            for (float t = .26f; t <= .94f; t += step) { HairGestures.SampleToss(t, m); values.Add(m[tilt]); }
            var extremes = new List<float>();
            float previous = values[1] - values[0];
            for (int i = 2; i < values.Count; i++)
            {
                float slope = values[i] - values[i - 1];
                if (Mathf.Abs(slope) < 1e-7f) continue;
                if (Mathf.Sign(slope) != Mathf.Sign(previous)) extremes.Add(Mathf.Abs(values[i - 1]));
                previous = slope;
            }
            Assert.That(extremes.Count, Is.EqualTo(2), "direction changes of the head tilt between 0.25 and 0.95 s");
            Assert.That(extremes[1], Is.LessThan(extremes[0]));
        }
    }
}
