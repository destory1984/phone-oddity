using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Oddity.Core;
using Oddity.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Oddity.PlayTests
{
    /// <summary>The emergence (Harin comes out of the phone) and the act 2 cut, run 5x faster.</summary>
    public class EmergenceTests
    {
        const string SceneName = "Prototype";
        const float Speed = 5f;

        string _savePath;
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _savePath = Path.Combine(Path.GetTempPath(), "oddity_playtest_" + Guid.NewGuid().ToString("N") + ".json");
            _now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            GameBootstrap.SavePathOverride = _savePath;
            GameBootstrap.SkipPrologue = true; // these tests start at day 1
            ChatView.TypingEnabled = false;
            GameBootstrap.ClockOverride = () => _now;
            var state = new GameState { PlayerName = "민수", Day = CoreInfo.EmergenceDay, Slot = Slot.Night, NextOpenUtc = _now.AddMinutes(-1) };
            new SaveStore(_savePath).Save(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameBootstrap.SavePathOverride = null;
            GameBootstrap.SkipPrologue = false;
            ChatView.TypingEnabled = true;
            GameBootstrap.ClockOverride = null;
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }

        static GameBootstrap Boot() => UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();

        IEnumerator LoadAndPlay()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var seq = Boot().Emergence;
            seq.Speed = Speed;
            seq.Play();
        }

        IEnumerator WaitFinished(EmergenceSequence seq)
        {
            float limit = Time.realtimeSinceStartup + seq.Duration / Speed + 5f;
            while (!seq.HasFinished && Time.realtimeSinceStartup < limit) yield return null;
            Assert.IsTrue(seq.HasFinished, "emergence did not finish");
        }

        [UnityTest]
        public IEnumerator Emergence_HarinNeverHidden()
        {
            yield return LoadAndPlay();
            var seq = Boot().Emergence;
            var harin = HarinWardrobe.ActivePuppet();
            int id = harin.GetInstanceID();
            var renderers = harin.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0);

            int bad = 0, samples = 0;
            float nextSample = 0f;
            float limit = Time.realtimeSinceStartup + seq.Duration / Speed + 5f;
            while (!seq.HasFinished && Time.realtimeSinceStartup < limit)
            {
                // every frame is checked; samples counts the 0.1 s (sequence time) marks passed
                var all = UnityEngine.Object.FindObjectsByType<HarinPuppet>(FindObjectsSortMode.None);
                bool ok = all.Length == 1 && all[0].GetInstanceID() == id && harin.gameObject.activeInHierarchy;
                foreach (var r in renderers) ok &= r != null && r.enabled && r.gameObject.activeInHierarchy;
                if (!ok) bad++;
                if (seq.Time >= nextSample) { samples++; nextSample += 0.1f; }
                yield return null;
            }
            Assert.IsTrue(seq.HasFinished);
            Assert.AreEqual(0, bad, "frames where Harin was hidden or replaced");
            Assert.GreaterOrEqual(samples, 50);
        }

        [UnityTest]
        public IEnumerator Emergence_EndsInRoom()
        {
            yield return LoadAndPlay();
            var seq = Boot().Emergence;
            yield return WaitFinished(seq);
            yield return null;

            var harin = HarinWardrobe.ActivePuppet();
            Assert.Less(Vector3.Distance(harin.transform.position, seq.RoomAnchor.position), 0.05f);

            var cam = seq.MainCamera.transform;
            var phone = GameObject.Find("Phone").transform;
            Assert.Greater(Vector3.Distance(cam.position, phone.position), 2f, "camera left the phone");
            Assert.That(cam.position.x, Is.InRange(-2f, 2f));
            Assert.That(cam.position.z, Is.InRange(-2.5f, 2.5f));
            var toHarin = (harin.transform.position + Vector3.up * 0.9f - cam.position).normalized;
            Assert.Greater(Vector3.Dot(cam.forward, toHarin), 0.95f, "camera looks at Harin in the room");
            Assert.IsFalse(UnityEngine.Object.FindFirstObjectByType<ChatView>().IsVisible, "phone UI hidden");
        }

        [UnityTest]
        public IEnumerator Emergence_FinishedStartsAfterScene()
        {
            // play the real night to its end: the bootstrap starts the emergence by itself
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var boot = Boot();
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            boot.Emergence.Speed = Speed;
            for (int i = 0; i < 80 && !boot.Emergence.IsPlaying; i++)
            {
                if (chat.VisibleButtonTexts.Count > 0) chat.PickChoice(0);
                else chat.Tap();
                yield return null;
            }
            Assert.IsTrue(boot.Emergence.IsPlaying, "night end did not play the emergence");
            Assert.AreNotEqual(GameBootstrap.AfterSceneId, boot.CurrentSceneId);

            yield return WaitFinished(boot.Emergence);
            Assert.AreEqual(GameBootstrap.AfterSceneId, boot.CurrentSceneId);
            Assert.IsTrue(boot.InRoom);
            Assert.IsTrue(boot.Subtitle.IsVisible);
            Assert.IsNotEmpty(boot.Subtitle.CurrentText);
        }

        [UnityTest]
        public IEnumerator After_FadeDipsAndRecovers()
        {
            yield return LoadAndPlay();
            var boot = Boot();
            yield return WaitFinished(boot.Emergence);
            var harin = HarinWardrobe.ActivePuppet();
            Assert.AreEqual(1f, harin.Fade, 0.001f);

            for (int i = 0; i < 10 && Mathf.Abs(harin.Glitch - 0.6f) > 0.01f; i++)
            {
                boot.Subtitle.Tap();
                yield return null;
            }
            Assert.AreEqual(0.6f, harin.Glitch, 0.01f, "reached the line before the name");
            Assert.AreEqual(0.4f, harin.Fade, 0.05f);

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0.4f, harin.Fade, 0.05f, "still dimmed half way");
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(1f, harin.Fade, 0.001f, "back to opaque after 1 s");
        }

        IEnumerator LoadAndHold()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var seq = Boot().Emergence;
            seq.Speed = 0f;
            seq.Play();
        }

        static bool InsideQuad(Vector2 p, Vector2[] q)
        {
            float sign = 0f;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i];
                var b = q[(i + 1) % 4];
                float c = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (Mathf.Abs(c) < 1e-7f) continue;
                if (sign == 0f) sign = Mathf.Sign(c);
                else if (Mathf.Sign(c) != sign) return false;
            }
            return true;
        }

        // Every 0.5 s of cut 10: only the right forearm and hand are above the desk plane, the arm comes
        // up through the phone screen quad (in Cut10Camera's view), and the hand never goes above the
        // top of the phone screen in the picture.
        [UnityTest]
        public IEnumerator Cut10_ArmStaysInsideScreenRect()
        {
            yield return LoadAndHold();
            var seq = Boot().Emergence;
            var harin = HarinWardrobe.ActivePuppet();
            var cam = seq.Cut10Camera;
            Assert.IsNotNull(cam, "no Cut10Camera");
            cam.aspect = 9f / 16f;
            float plane = seq.DeskPlaneY;

            var hidden = new[]
            {
                HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            };
            var chain = new[] { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleDistal };
            int crossings = 0;
            for (float t = 0.5f; t < 4f; t += 0.5f)
            {
                seq.Seek(t);
                yield return null;
                Assert.IsTrue(cam.enabled, $"Cut10Camera off at {t}");
                // after the frame, so the camera's fit to the 9:16 aspect has run
                var quad = seq.ScreenCorners.Select(c => (Vector2)cam.WorldToViewportPoint(c)).ToArray();
                float top = quad.Max(q => q.y);
                foreach (var b in hidden)
                    Assert.Less(harin.BonePosition(b).y, plane, $"{b} above the desk at {t:0.0}s");

                var pts = chain.Select(harin.BonePosition).ToArray();
                for (int i = 1; i < pts.Length; i++)
                {
                    if (pts[i].y <= plane) continue;
                    var vp = cam.WorldToViewportPoint(pts[i]);
                    Assert.LessOrEqual(vp.y, top, $"{chain[i]} above the phone screen in the picture at {t:0.0}s: {vp} top {top:0.000} height {pts[i].y - plane:0.000}");
                }
                for (int i = 1; i < pts.Length; i++)
                {
                    if (pts[i - 1].y < plane && pts[i].y >= plane)
                    {
                        float k = (plane - pts[i - 1].y) / (pts[i].y - pts[i - 1].y);
                        var cross = Vector3.Lerp(pts[i - 1], pts[i], k);
                        Vector2 cv = cam.WorldToViewportPoint(cross);
                        Assert.IsTrue(InsideQuad(cv, quad),
                            $"arm crosses the desk outside the phone screen at {t:0.0}s ({chain[i - 1]} → {chain[i]}) at {cv} quad {string.Join(" ", quad)}");
                        crossings++;
                        break;
                    }
                }
            }
            Assert.GreaterOrEqual(crossings, 5, "the arm should be out of the screen for most of cut 10");
            var wrist = harin.BonePosition(HumanBodyBones.RightHand);
            Assert.Greater(wrist.y, plane, "wrist out of the screen by 3.5 s");
        }

        // Cut 12: the seat contact rests on the mattress and both soles on the floor.
        [UnityTest]
        public IEnumerator Cut12_SeatedOnBed()
        {
            yield return LoadAndHold();
            var seq = Boot().Emergence;
            var harin = HarinWardrobe.ActivePuppet();
            seq.Seek(9f);
            yield return null;
            var seat = harin.SeatContactWorld;
            var l = harin.SolePosition(false);
            var r = harin.SolePosition(true);
            var knee = harin.BonePosition(HumanBodyBones.LeftLowerLeg);
            var hip = harin.BonePosition(HumanBodyBones.LeftUpperLeg);
            var ankle = harin.BonePosition(HumanBodyBones.LeftFoot);
            float hipFlex = Vector3.Angle(Vector3.down, knee - hip);
            float kneeFlex = Vector3.Angle(knee - hip, ankle - knee);
            string info = $"seat y {seat.y:0.000} mattress {seq.MattressTop:0.000} soles {l.y:0.000}/{r.y:0.000} "
                + $"hip {hipFlex:0} knee {kneeFlex:0} in from edge {seq.MattressFrontX - seat.x:0.000} "
                + $"ankleH {harin.AnkleHeight:0.000} seatLocal {harin.SeatContactLocal}";
            Debug.Log("CUT12 " + info);
            Assert.AreEqual(seq.MattressTop, seat.y, 0.03f, "pelvis on the mattress: " + info);
            Assert.AreEqual(0f, l.y, 0.03f, "left sole on the floor: " + info);
            Assert.AreEqual(0f, r.y, 0.03f, "right sole on the floor: " + info);
            Assert.That(seq.MattressFrontX - seat.x, Is.InRange(0.12f, 0.18f), "seat in from the bed edge: " + info);
            Assert.That(hipFlex, Is.InRange(75f, 100f), "hip flexion: " + info);
            Assert.That(kneeFlex, Is.InRange(80f, 105f), "knee flexion: " + info);
        }
    }
}
