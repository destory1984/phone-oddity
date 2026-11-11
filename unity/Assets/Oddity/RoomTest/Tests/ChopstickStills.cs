#if UNITY_EDITOR
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oddity.RoomTest.Tests
{
    // Renders close-ups of the chopstick hand into shots/room_test/keep/chopsticks (set ODDITY_STILLS=1; otherwise the test is ignored).
    public class ChopstickStills
    {
        const int W = 560, H = 420;

        static GameObject LoadModel()
        {
            string dir = Path.Combine(Application.dataPath, "Models/test");
            string file = Directory.GetFiles(dir, "harin-6T-O2-*.vrm")
                .OrderByDescending(p => int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(), out int n) ? n : -1).First();
            return Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/test/" + Path.GetFileName(file)));
        }

        static Texture2D Shoot(Camera cam, Vector3 eye, Vector3 aim, Vector3 up, float fov)
        {
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye, up)); cam.fieldOfView = fov;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render();
            var old = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = old; cam.targetTexture = null; Object.DestroyImmediate(rt);
            return tex;
        }

        static void Sheet(string name, Texture2D[] tiles, int columns)
        {
            int rows = (tiles.Length + columns - 1) / columns;
            var sheet = new Texture2D(W * columns, H * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Length; i++) sheet.SetPixels(W * (i % columns), H * (rows - 1 - i / columns), W, H, tiles[i].GetPixels());
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../shots/room_test/keep/chopsticks")); Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), sheet.EncodeToPNG());
        }

        [Test]
        public void ChopstickStills_GripSheet()
        {
            if (System.Environment.GetEnvironmentVariable("ODDITY_STILLS") != "1") Assert.Ignore("set ODDITY_STILLS=1");
            var model = LoadModel(); FloorMealRig rig = null; var lightObj = new GameObject("StillLight"); var camObj = new GameObject("StillCam");
            try
            {
                var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
                var cam = camObj.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f); cam.nearClipPlane = .02f; cam.farClipPlane = 5;
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start(); rig.Tick(FloorMealTimeline.BlendSeconds);
                foreach (var r in rig.Props.GetComponentsInChildren<Renderer>()) r.enabled = false; // only the hand and the sticks
                foreach (var r in rig.Noodles.GetComponentsInChildren<Renderer>()) r.enabled = false;
                var tiles = new System.Collections.Generic.List<Texture2D>();
                foreach (float open in new[] { 0f, 1f })
                {
                    rig.GripOverride = open; rig.Apply(0);
                    anim.GetBoneTransform(HumanBodyBones.Head).localScale = Vector3.zero; anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).localScale = Vector3.zero; // nothing in front of the hand
                    var g = rig.Grip;
                    var centre = g.ToWorld(new Vector3(.015f, .075f, -.04f)); var frame = g.HFrame;
                    // thumb side, back of the hand, palm side, from the tips looking back (directions in the hand frame)
                    var dirs = new[] { new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1), new Vector3(0, 1, 0) };
                    var ups = new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
                    for (int v = 0; v < 4; v++)
                    {
                        var eye = centre + frame * dirs[v] * .34f;
                        tiles.Add(Shoot(cam, eye, centre, frame * ups[v], 26));
                    }
                }
                Sheet("A2_grip_sheet.png", tiles.ToArray(), 4);
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); Object.DestroyImmediate(lightObj); Object.DestroyImmediate(camObj); }
        }

        [Test]
        public void ChopstickStills_PickupSheet()
        {
            if (System.Environment.GetEnvironmentVariable("ODDITY_STILLS") != "1") Assert.Ignore("set ODDITY_STILLS=1");
            var model = LoadModel(); FloorMealRig rig = null; var lightObj = new GameObject("StillLight"); var camObj = new GameObject("StillCam");
            try
            {
                var light = lightObj.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
                var cam = camObj.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f); cam.nearClipPlane = .02f; cam.farClipPlane = 8;
                var anim = model.GetComponent<Animator>();
                rig = new FloorMealRig(anim, 104);
                model.transform.rotation = rig.Props.rotation;
                rig.Start();
                var front = new System.Collections.Generic.List<Texture2D>(); var side = new System.Collections.Generic.List<Texture2D>();
                // (drink bite?, phase, progress): the first normal bite, then the first bite that drinks the broth
                var marks = new[] { (false, "Gathering", .45f), (false, "Gathering", .9f), (false, "Lifting", .7f), (false, "Slurping", .2f), (false, "Slurping", .55f), (false, "Slurping", .97f), (true, "Lifting", .75f), (true, "Chewing", .5f) };
                int next = 0;
                for (int i = 0; i < 9000 && next < marks.Length; i++)
                {
                    rig.Tick(1f / 60);
                    if (rig.Weight < 1) continue;
                    rig.Apply(1f / 60);
                    var m = marks[next];
                    if (rig.BiteIsDrink != m.Item1 || rig.Phase != m.Item2 || rig.Progress < m.Item3) continue;
                    if (!m.Item1 && rig.BiteNumber != 1) continue;
                    var bowl = rig.BowlRimCentre; var f = rig.Props.forward; var r = rig.Props.right;
                    var aim = Vector3.Lerp(bowl, rig.MouthPoint, .5f) + Vector3.up * .03f;
                    front.Add(Shoot(cam, bowl + f * .85f + r * .35f + Vector3.up * .25f, aim, Vector3.up, 36));
                    side.Add(Shoot(cam, bowl + r * .85f + Vector3.up * .22f, aim, Vector3.up, 36));
                    Debug.Log("PICKUP " + m + " " + rig.ArmNumbers() + " wrist " + rig.WristAngles + " mouthOverRim " + (rig.MouthPoint.y - rig.BowlRimCentre.y).ToString("0.00"));
                    next++;
                }
                var all = new System.Collections.Generic.List<Texture2D>(front); all.AddRange(side);
                Sheet("B2_pickup_sheet.png", all.ToArray(), 4);
            }
            finally { rig?.Dispose(); Object.DestroyImmediate(model); Object.DestroyImmediate(lightObj); Object.DestroyImmediate(camObj); }
        }
    }
}
#endif
