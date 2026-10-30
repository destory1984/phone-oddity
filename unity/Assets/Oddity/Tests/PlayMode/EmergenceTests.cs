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
        public IEnumerator Emergence_KnitHiddenInHandCutAndBackWhenBodyShows()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var seq = Boot().Emergence;
            seq.Speed = 0f;
            seq.Seek(1f); // cut 10: only the hand is in frame
            var harin = HarinWardrobe.ActivePuppet();
            Assert.IsTrue(harin.KnitHidden, "knit sleeve must not draw while only the hand shows");
            for (int i = 0; i < 20; i++) yield return null; // stays hidden while the sequence keeps evaluating
            Assert.IsTrue(harin.KnitHidden, "knit sleeve came back by itself in cut 10");
            seq.Seek(EmergenceSequence.RoomAt + 1f); // cut 11: her body is in frame
            Assert.IsFalse(harin.KnitHidden, "knit must be back when the pullback shows her body");
            seq.Seek(EmergenceSequence.Cut10At + 1f);
            Assert.IsTrue(harin.KnitHidden, "knit hidden again when cut 10 is re-entered");
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

        // Cut 10 at 2.5 s: drawn with and without Harin. A point of her arm or body under the desk whose screen ray
        // meets the desk plane outside the portal circle shows the same pixel as the picture without her (the desk
        // hides it); some forearm points under the plane inside the circle show her (the arm goes down into the portal).
        [UnityTest]
        public IEnumerator Cut10_ArmHiddenOutsidePortal()
        {
            yield return LoadAndHold();
            var seq = Boot().Emergence;
            var harin = HarinWardrobe.ActivePuppet();
            var cam = seq.Cut10Camera;
            cam.aspect = 9f / 16f;
            seq.Seek(2.5f);
            yield return null;

            const int W = 360, H = 640;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var renderers = harin.GetComponentsInChildren<Renderer>(true);
            Texture2D Draw(bool withHarin)
            {
                foreach (var r in renderers) r.forceRenderingOff = !withHarin;
                var prevTarget = cam.targetTexture;
                var prevActive = RenderTexture.active;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                foreach (var r in renderers) r.forceRenderingOff = false;
                return tex;
            }
            var with = Draw(true);
            var without = Draw(false);

            float plane = seq.DeskPlaneY;
            var centre = seq.PortalCentre;
            var camPos = cam.transform.position;
            float Diff(Vector3 world, out Vector2Int px)
            {
                var vp = cam.WorldToViewportPoint(world);
                px = new Vector2Int(Mathf.Clamp((int)(vp.x * W), 0, W - 1), Mathf.Clamp((int)(vp.y * H), 0, H - 1));
                Color a = with.GetPixel(px.x, px.y), b = without.GetPixel(px.x, px.y);
                return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
            }
            float OnPlaneFromCentre(Vector3 world)
            {
                var d = world - camPos;
                var p = camPos + d * ((plane - camPos.y) / d.y);
                return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(centre.x, centre.z));
            }

            var shoulder = harin.BonePosition(HumanBodyBones.RightUpperArm);
            var elbow = harin.BonePosition(HumanBodyBones.RightLowerArm);
            var wrist = harin.BonePosition(HumanBodyBones.RightHand);
            var points = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i <= 40; i++)
            {
                points.Add(Vector3.Lerp(shoulder, elbow, i / 40f));
                points.Add(Vector3.Lerp(elbow, wrist, i / 40f));
            }
            foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Chest, HumanBodyBones.Hips, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm })
                points.Add(harin.BonePosition(b));

            int outside = 0, outsideBad = 0, inside = 0, insideShown = 0;
            string bad = "";
            foreach (var p in points)
            {
                if (p.y > plane - 0.005f) continue;
                float r = OnPlaneFromCentre(p);
                float diff = Diff(p, out var px);
                if (r > EmergenceSequence.PortalRadius + 0.01f)
                {
                    outside++;
                    if (diff > 0.03f) { outsideBad++; if (bad.Length < 300) bad += $" {px} r {r:0.000} diff {diff:0.00} depth {plane - p.y:0.000};"; }
                }
                else if (r < EmergenceSequence.PortalRadius - 0.01f)
                {
                    inside++;
                    if (diff > 0.05f) insideShown++;
                }
            }
            float handDiff = Diff(wrist + (MiddleTip(harin) - wrist) * 0.5f, out _);
            string info = $"outside {outside} bad {outsideBad} inside {inside} shown {insideShown} hand diff {handDiff:0.00}";
            Debug.Log("CUT10PORTAL " + info + bad);
            UnityEngine.Object.Destroy(with);
            UnityEngine.Object.Destroy(without);
            rt.Release();
            Assert.Greater(handDiff, 0.05f, "the hand above the desk is drawn: " + info);
            Assert.GreaterOrEqual(outside, 10, "enough points outside the portal: " + info);
            Assert.AreEqual(0, outsideBad, "her body shows through the desk outside the portal:" + bad);
            Assert.GreaterOrEqual(insideShown, 3, "the forearm shows going down into the portal: " + info);
        }

        // Middle finger tip: the distal bone plus 2 cm along it (the bone sits at the last joint).
        static Vector3 MiddleTip(HarinPuppet h)
        {
            var d = h.BonePosition(HumanBodyBones.RightMiddleDistal);
            var m = h.BonePosition(HumanBodyBones.RightMiddleIntermediate);
            return d + (d - m).normalized * 0.02f * h.transform.lossyScale.x;
        }

        // Cut 10 at 3.0 s (finger tips on the desk): wrist to middle finger tip on screen is 1.0 to 1.4 times
        // the phone's long side on screen (a 17 to 18 cm hand next to a 14.6 cm phone).
        [UnityTest]
        public IEnumerator Cut10_HandRealSize()
        {
            yield return LoadAndHold();
            var seq = Boot().Emergence;
            var harin = HarinWardrobe.ActivePuppet();
            var cam = seq.Cut10Camera;
            cam.aspect = 9f / 16f;
            seq.Seek(3f);
            yield return null;
            Vector2 S(Vector3 w) { var v = cam.WorldToViewportPoint(w); return new Vector2(v.x * cam.aspect, v.y); }
            var c = seq.ScreenCorners;
            float phone = (Vector2.Distance(S(c[0]), S(c[3])) + Vector2.Distance(S(c[1]), S(c[2]))) * 0.5f;
            var wrist = harin.BonePosition(HumanBodyBones.RightHand);
            var tip = MiddleTip(harin);
            float hand = Vector2.Distance(S(wrist), S(tip));
            var phoneMid = (c[0] + c[1] + c[2] + c[3]) * 0.25f;
            string info = $"hand {hand:0.000} phone {phone:0.000} ratio {hand / phone:0.00} hand3d {Vector3.Distance(wrist, tip):0.000} "
                + $"phone3d {Vector3.Distance(c[0], c[3]):0.000} scale {harin.transform.lossyScale.x:0.000} "
                + $"cam→phone {Vector3.Distance(cam.transform.position, phoneMid):0.000} cam→hand {Vector3.Distance(cam.transform.position, (wrist + tip) * 0.5f):0.000} "
                + $"wristH {wrist.y - seq.DeskPlaneY:0.000} tipH {tip.y - seq.DeskPlaneY:0.000} fov {cam.fieldOfView:0.0}";
            Debug.Log("CUT10SIZE " + info);
            Assert.That(hand / phone, Is.InRange(1.0f, 1.4f), info);
        }

        // Cut 10: the first joint of index to little finger is bent at least 8° from the T-pose, little the most.
        [UnityTest]
        public IEnumerator Cut10_FingersCurled()
        {
            yield return LoadAndHold();
            var seq = Boot().Emergence;
            var harin = HarinWardrobe.ActivePuppet();
            var fingers = new[] { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal };
            foreach (float t in new[] { 3f, 3.5f })
            {
                seq.Seek(t);
                yield return null;
                var bends = fingers.Select(harin.FingerBend).ToArray();
                string info = $"at {t:0.0}s " + string.Join(" ", fingers.Select((f, i) => $"{f} {bends[i]:0.0}"));
                Debug.Log("CUT10FINGERS " + info);
                foreach (var b in bends) Assert.GreaterOrEqual(b, 8f, info);
                Assert.Greater(bends[3], bends[0], "little bends more than index: " + info);
            }
        }

        // Cut 10: once the finger tips are on the desk the contact shadow is on and at least 0.2 alpha.
        [UnityTest]
        public IEnumerator Cut10_ContactShadow()
        {
            yield return LoadAndHold();
            var seq = Boot().Emergence;
            seq.Seek(2f);
            yield return null;
            float before = seq.ContactShadowAlpha;
            foreach (float t in new[] { 3f, 3.5f, 3.9f })
            {
                seq.Seek(t);
                yield return null;
                var r = seq.ContactShadow;
                Assert.IsNotNull(r, "no contact shadow");
                Assert.IsTrue(r.enabled && r.gameObject.activeInHierarchy, $"contact shadow off at {t:0.0}s");
                Assert.Greater(seq.ContactShadowAlpha, 0.2f, $"contact shadow alpha at {t:0.0}s");
                Assert.Greater(seq.ContactShadowAlpha, before, "darker once the hand is down");
            }
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

        // Act 2: on a plain line the eyes follow the camera and the face points within 20 degrees of it.
        [UnityTest]
        public IEnumerator Act2_LooksAtCameraOnPlainLines()
        {
            yield return LoadAndPlay();
            var boot = Boot();
            yield return WaitFinished(boot.Emergence);
            var harin = HarinWardrobe.ActivePuppet();
            var cam = boot.Emergence.MainCamera.transform;
            yield return new WaitForSeconds(1.0f); // the 0.4 s ease is done
            Assert.AreSame(cam, harin.EyeTarget, "eyes follow the camera");
            var head = harin.BonePosition(HumanBodyBones.Head);
            float angle = Vector3.Angle(harin.HeadFacing, cam.position - head);
            Debug.Log($"ACT2 head-to-camera angle {angle:F1}");
            Assert.LessOrEqual(angle, 20f, $"face towards the camera: {angle:F1} deg");
        }

        // Every scene: the mouth inside is 15% darker than imported (both outfits).
        [UnityTest]
        public IEnumerator Mouth_DarkerEverywhere()
        {
            yield return LoadAndHold();
            var wardrobe = UnityEngine.Object.FindFirstObjectByType<HarinWardrobe>();
            foreach (var outfit in new[] { HarinWardrobe.Outfit.Summer, HarinWardrobe.Outfit.Indoor })
            {
                var puppet = wardrobe.Get(outfit);
                if (puppet == null) continue;
                Assert.AreEqual(0.85f, puppet.MouthInsideBrightness, 0.01f, outfit + " brightness");
                if (!puppet.TryGetMouthInsideColors(out var imported, out var now)) continue;
                Assert.AreEqual(imported.r * 0.85f, now.r, 0.01f, outfit + " FaceMouth red");
                Assert.AreEqual(imported.g * 0.85f, now.g, 0.01f, outfit + " FaceMouth green");
            }
        }
    }
}
