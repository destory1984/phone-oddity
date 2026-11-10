using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Oddity.RoomTest.Tests
{
    public class WorkoutTests
    {
        [Test]
        public void Workout_TimelineRuns120SecondsAndCountsEveryRep()
        {
            var clock = new WorkoutTimeline(17);
            clock.Start();
            var phases = new HashSet<string>();
            for (int i = 0; i < 12000; i++)
            {
                clock.Tick(.01f); phases.Add(clock.Phase);
                Assert.That(clock.Rep, Is.InRange(0, 6));
                Assert.That(clock.Weight, Is.InRange(0f, 1f));
            }
            Assert.That(clock.CompletedLoops, Is.EqualTo(2));
            Assert.That(clock.TotalReps, Is.EqualTo(39)); // 28 in two loops + 4 stretches + 6 squats + 1 lunge.
            CollectionAssert.AreEquivalent(new[] { "StretchLeft", "StretchRight", "Squat", "LungeLeft", "LungeRight", "Breathe", "Wipe" }, phases);
            clock.Stop(); clock.Tick(WorkoutRig.BlendSeconds);
            Assert.That(clock.Weight, Is.EqualTo(0f)); Assert.IsFalse(clock.Active);
            clock.Start(); clock.Tick(WorkoutRig.BlendSeconds);
            Assert.That(clock.Weight, Is.EqualTo(1f)); Assert.That(clock.TotalReps, Is.Zero);
        }

        [Test]
        public void Workout_LargeTicksPreserveStateAndRejectInvalidTime()
        {
            var a = new WorkoutTimeline(7); var b = new WorkoutTimeline(7);
            a.Start(); b.Start(); a.Tick(120f);
            for (int i = 0; i < 120; i++) b.Tick(1f);
            Assert.That(a.TotalReps, Is.EqualTo(b.TotalReps));
            Assert.That(a.Phase, Is.EqualTo(b.Phase));
            Assert.That(a.Time, Is.EqualTo(b.Time).Within(.001f));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Tick(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Tick(-1f));
        }

        static GameObject Model()
        {
            var dir = Path.Combine(Application.dataPath, "Models/test");
            var path = Directory.Exists(dir) ? Directory.GetFiles(dir, "harin-6T-O2-*.vrm")
                .OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(), out var n) ? n : -1)
                .FirstOrDefault() : null;
            var prefab = path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/test/" + Path.GetFileName(path));
            if (prefab == null) Assert.Ignore("no 6T-O2 default model installed");
            var model = UnityEngine.Object.Instantiate(prefab);
            var anim = model.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) { UnityEngine.Object.DestroyImmediate(model); Assert.Ignore("model has no humanoid avatar"); }
            return model;
        }

        static float SoleLow(GameObject model, Transform foot)
        {
            float lowest = float.PositiveInfinity;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                var weights = skin.sharedMesh.boneWeights; var bones = skin.bones;
                if (weights.Length != skin.sharedMesh.vertexCount) continue;
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh); var verts = mesh.vertices;
                    for (int v = 0; v < verts.Length; v++)
                    {
                        var w = weights[v];
                        var indices = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                        var values = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
                        float sum = 0f;
                        for (int j = 0; j < 4; j++)
                            if (indices[j] >= 0 && indices[j] < bones.Length && bones[indices[j]] != null &&
                                (bones[indices[j]] == foot || bones[indices[j]].IsChildOf(foot))) sum += values[j];
                        if (sum >= .25f) lowest = Mathf.Min(lowest, skin.transform.TransformPoint(verts[v]).y);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            return lowest;
        }

        [Test]
        public void Workout_DefaultModelKeepsBothSolesOnFloorForAnEntireLoop()
        {
            var model = Model(); WorkoutRig rig = null;
            try
            {
                model.transform.SetPositionAndRotation(new Vector3(1.7f, 0f, 1.8f), Quaternion.Euler(0f, 180f, 0f));
                var anim = model.GetComponent<Animator>(); rig = new WorkoutRig(anim, 17);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips); var original = hips.position;
                rig.Start(); rig.Tick(WorkoutRig.BlendSeconds);
                Assert.That(hips.position, Is.EqualTo(original), "Tick must not write bones");
                for (int i = 0; i < 900; i++)
                {
                    rig.Apply(.05f);
                    Assert.That(Mathf.Abs(rig.LeftContact.y), Is.LessThanOrEqualTo(.02f), rig.Phase + " left sole");
                    Assert.That(Mathf.Abs(rig.RightContact.y), Is.LessThanOrEqualTo(.02f), rig.Phase + " right sole");
                    Assert.That(rig.MaxFootError, Is.LessThan(.02f), rig.Phase + " leg IK reach");
                    if (i % 20 == 0)
                        foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                        {
                            float low = SoleLow(model, anim.GetBoneTransform(bone));
                            if (!float.IsPositiveInfinity(low)) Assert.That(low, Is.InRange(-.02f, .02f), rig.Phase + " skinned sole " + bone);
                        }
                    foreach (var bone in new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                        Assert.That(anim.GetBoneTransform(bone).position.y, Is.GreaterThanOrEqualTo(-.02f), rig.Phase + " " + bone);
                    var before = hips.position; rig.Apply(0f);
                    Assert.That(Vector3.Distance(before, hips.position), Is.LessThan(.00001f), "no additive drift");
                    rig.Tick(.05f);
                }
                Assert.That(rig.Mat.localScale, Is.EqualTo(new Vector3(WorkoutRig.MatWidth, WorkoutRig.MatThickness, WorkoutRig.MatLength)));
                rig.Stop(); rig.Tick(WorkoutRig.BlendSeconds); rig.Apply(0f);
                Assert.That(Vector3.Distance(hips.position, original), Is.LessThan(.0001f));
            }
            finally { rig?.Dispose(); UnityEngine.Object.DestroyImmediate(model); }
        }
    }
}
