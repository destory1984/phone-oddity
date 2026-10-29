using System.IO;
using Oddity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Builds the room, phone and messenger objects in Prototype.unity. Safe to run again: it deletes
    /// what it built before and rebuilds. Run: Unity -batchmode -executeMethod Oddity.EditorTools.SceneBuilder.Build
    /// (or the menu Oddity/Build Prototype Scene). Art files that are missing yet are replaced by flat colours;
    /// run it again once window_night.png, chat_wallpaper.png or app_icon.png exist.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string ArtDir = "Assets/Oddity/Art";
        const string MatDir = "Assets/Oddity/Materials";
        const string AppStageLayerName = "AppStage";
        const int AppStageLayerIndex = 8;
        const uint AppStageRenderingLayer = 2; // bit 1; the room keeps layer 1 (bit 0)

        // phone: 9:16 screen, lying on the desk
        const float ScreenW = 0.072f;
        const float ScreenH = 0.128f;
        const float DeskTop = 0.75f;
        static readonly Vector3 PhoneCenter = new Vector3(0.25f, DeskTop, 2.05f);

        static readonly string[] Built =
        {
            "Room", "Phone", "PhoneCanvas", "MainCamera", "RoomOverviewCamera", "EventSystem", "GameBootstrap",
        };

        [MenuItem("Oddity/Build Prototype Scene")]
        public static void Build()
        {
            EnsureLayer();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (string name in Built)
            {
                GameObject old;
                while ((old = GameObject.Find(name)) != null) Object.DestroyImmediate(old);
            }
            Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            SetupAppStage();
            BuildRoom();
            BuildLights();
            var phone = BuildPhone();
            var main = BuildMainCamera(phone);
            BuildOverviewCamera();
            var chat = BuildCanvas(main);
            BuildEventSystem();
            BuildBootstrap(chat);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.26f, 0.26f, 0.34f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("SCENE built " + ScenePath);
        }

        // ------------------------------------------------------------ Harin model swap

        const string ModelDir = "Assets/Models";

        /// <summary>
        /// Replaces one of Harin's VRMs in Prototype.unity with another VRoid export. <paramref name="outfit"/> picks the
        /// wardrobe slot: Summer (outfit B, the phone, the emergence and act 2) or Indoor (outfit B2, the prologue).
        /// <paramref name="vrmPath"/> is a project asset path ("Assets/Models/harin-summer-B.vrm") or a file on
        /// disk, which is copied into Assets/Models first. The new model takes the old one's parent, transform,
        /// layer and rendering layers; HarinPuppet (with HarinPose, HarinExpression) and any other Oddity
        /// components are copied over, and scene references to the old puppet, object or transform are pointed
        /// at the new one. When the slot is empty (the first indoor model) the summer model is the template, the new
        /// one starts inactive and nothing else is re-pointed. Expression names are the VRM presets and the room look
        /// finds the materials by their VRoid names (SKIN, HAIR), so they keep working. The cheek masks are baked again.
        /// Batch mode:
        /// Unity -batchmode -executeMethod Oddity.EditorTools.SceneBuilder.SwapHarinModelFromArgs -vrm path [-outfit indoor]
        /// </summary>
        public static void SwapHarinModel(string vrmPath, HarinWardrobe.Outfit outfit = HarinWardrobe.Outfit.Summer)
        {
            string assetPath = ImportVrm(vrmPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) throw new System.Exception("no model imported from " + assetPath);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var wardrobe = EnsureWardrobe();
            var oldPuppet = wardrobe.Get(outfit);
            bool replace = oldPuppet != null;
            var template = replace ? oldPuppet : wardrobe.Summer;
            if (template == null) throw new System.Exception("no HarinPuppet in " + ScenePath);
            var old = template.gameObject;

            var fresh = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            fresh.name = replace ? old.name : (outfit == HarinWardrobe.Outfit.Indoor ? "HarinIndoor" : old.name);
            fresh.transform.SetParent(old.transform.parent, false);
            fresh.transform.SetSiblingIndex(old.transform.GetSiblingIndex() + (replace ? 0 : 1));
            fresh.transform.localPosition = old.transform.localPosition;
            fresh.transform.localRotation = old.transform.localRotation;
            fresh.transform.localScale = old.transform.localScale;

            uint renderingLayers = 0;
            foreach (var r in old.GetComponentsInChildren<Renderer>(true)) renderingLayers |= r.renderingLayerMask;
            foreach (var t in fresh.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = old.layer;
            if (renderingLayers != 0)
                foreach (var r in fresh.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = renderingLayers;

            // our components (HarinPuppet and anything else from Oddity.*), with their serialized settings
            var map = new System.Collections.Generic.Dictionary<Object, Object>
            {
                { old, fresh }, { old.transform, fresh.transform },
            };
            foreach (var c in old.GetComponents<MonoBehaviour>())
            {
                if (c == null || c.GetType().Namespace == null || !c.GetType().Namespace.StartsWith("Oddity")) continue;
                var copy = fresh.GetComponent(c.GetType()) ?? fresh.AddComponent(c.GetType());
                EditorUtility.CopySerialized(c, copy);
                map[c] = copy;
            }
            var oldAnimator = old.GetComponent<Animator>();
            var newAnimator = fresh.GetComponent<Animator>();
            if (oldAnimator != null && newAnimator != null)
            {
                newAnimator.runtimeAnimatorController = oldAnimator.runtimeAnimatorController;
                newAnimator.applyRootMotion = oldAnimator.applyRootMotion;
                newAnimator.cullingMode = oldAnimator.cullingMode;
                map[oldAnimator] = newAnimator;
            }

            if (replace)
            {
                fresh.SetActive(old.activeSelf);
                // point scene references at the new model
                foreach (var root in scene.GetRootGameObjects())
                foreach (var comp in root.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null || comp.transform.IsChildOf(old.transform)) continue;
                    var so = new SerializedObject(comp);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                        if (map.TryGetValue(it.objectReferenceValue, out var to))
                        {
                            it.objectReferenceValue = to;
                            changed = true;
                        }
                    }
                    if (changed) so.ApplyModifiedPropertiesWithoutUndo();
                }
                Object.DestroyImmediate(old);
            }
            else
            {
                fresh.SetActive(false); // the wardrobe turns it on when it is worn
                var w = new SerializedObject(wardrobe);
                w.FindProperty(outfit == HarinWardrobe.Outfit.Indoor ? "indoor" : "summer").objectReferenceValue = fresh.GetComponent<HarinPuppet>();
                w.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            BlushMaskBaker.Bake(false); // the copied cheek mask belongs to the old face
            Debug.Log("HARIN " + outfit + " model swapped to " + assetPath);
        }

        // The wardrobe sits on the GameBootstrap object; a scene with one model gets one with that model as Summer.
        static HarinWardrobe EnsureWardrobe()
        {
            var wardrobe = Object.FindFirstObjectByType<HarinWardrobe>(FindObjectsInactive.Include);
            if (wardrobe == null)
            {
                var boot = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
                if (boot == null) throw new System.Exception("no GameBootstrap in " + ScenePath);
                wardrobe = boot.gameObject.AddComponent<HarinWardrobe>();
                var b = new SerializedObject(boot);
                var summer = b.FindProperty("harin").objectReferenceValue as HarinPuppet;
                if (summer == null) summer = Object.FindFirstObjectByType<HarinPuppet>(FindObjectsInactive.Include);
                var w = new SerializedObject(wardrobe);
                w.FindProperty("summer").objectReferenceValue = summer;
                w.ApplyModifiedPropertiesWithoutUndo();
                b.FindProperty("wardrobe").objectReferenceValue = wardrobe;
                b.ApplyModifiedPropertiesWithoutUndo();
            }
            return wardrobe;
        }

        /// <summary>Batch-mode entry: -vrm &lt;path&gt; [-outfit summer|indoor] on the command line.</summary>
        public static void SwapHarinModelFromArgs()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-vrm");
            if (i < 0 || i + 1 >= args.Length) throw new System.Exception("usage: -vrm <path to .vrm> [-outfit summer|indoor]");
            var outfit = HarinWardrobe.Outfit.Summer;
            int o = System.Array.IndexOf(args, "-outfit");
            if (o >= 0 && o + 1 < args.Length && args[o + 1].ToLowerInvariant() == "indoor") outfit = HarinWardrobe.Outfit.Indoor;
            SwapHarinModel(args[i + 1], outfit);
        }

        static string ImportVrm(string vrmPath)
        {
            string p = vrmPath.Replace('\\', '/');
            if (p.StartsWith("Assets/") && File.Exists(p)) return p;
            if (!File.Exists(p)) throw new FileNotFoundException("no VRM at " + vrmPath);
            Directory.CreateDirectory(ModelDir);
            string dest = ModelDir + "/" + Path.GetFileName(p);
            File.Copy(p, dest, true);
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            return dest;
        }

        // ------------------------------------------------------------ layers

        static void EnsureLayer()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            var slot = layers.GetArrayElementAtIndex(AppStageLayerIndex);
            if (slot.stringValue != AppStageLayerName)
            {
                slot.stringValue = AppStageLayerName;
                tags.ApplyModifiedProperties();
            }
        }

        static void SetupAppStage()
        {
            int layer = LayerMask.NameToLayer(AppStageLayerName);
            var stage = GameObject.Find("AppStage");
            if (stage == null) throw new System.Exception("AppStage not found in the scene");
            foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in stage.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = AppStageRenderingLayer;

            var cam = stage.GetComponentInChildren<Camera>(true);
            cam.cullingMask = 1 << layer;

            foreach (var light in stage.GetComponentsInChildren<Light>(true)) SetLightLayers(light, AppStageRenderingLayer);
        }

        static void SetLightLayers(Light light, uint renderingLayers)
        {
            var data = light.GetUniversalAdditionalLightData();
            data.renderingLayers = renderingLayers;
            data.customShadowLayers = true;
            data.shadowRenderingLayers = renderingLayers;
        }

        // ------------------------------------------------------------ materials

        static Material Mat(string name, Color color, string shader = "Universal Render Pipeline/Lit", float smoothness = 0.15f)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material WindowMaterial()
        {
            var m = Mat("WindowNight", new Color(0.07f, 0.1f, 0.26f), "Universal Render Pipeline/Unlit");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/window_night.png");
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetColor("_BaseColor", Color.white);
            }
            else m.SetTexture("_BaseMap", null);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Sprite LoadSprite(string file)
        {
            string path = ArtDir + "/" + file;
            if (!File.Exists(path)) return null;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------ room

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void BuildRoom()
        {
            var room = new GameObject("Room").transform;
            var floorMat = Mat("RoomFloor", new Color(0.22f, 0.17f, 0.14f));
            var wallMat = Mat("RoomWall", new Color(0.3f, 0.32f, 0.4f));
            var bedMat = Mat("RoomBed", new Color(0.3f, 0.34f, 0.5f));
            var pillowMat = Mat("RoomPillow", new Color(0.8f, 0.78f, 0.74f));
            var woodMat = Mat("RoomWood", new Color(0.36f, 0.25f, 0.17f));
            var frameMat = Mat("RoomFrame", new Color(0.12f, 0.1f, 0.1f));

            // room x -2..2, z -2.5..2.5, height 2.6
            Box("Floor", room, new Vector3(0f, -0.05f, 0f), new Vector3(4.4f, 0.1f, 5.4f), floorMat);
            Box("Ceiling", room, new Vector3(0f, 2.65f, 0f), new Vector3(4.4f, 0.1f, 5.4f), wallMat);
            Box("WallNorth", room, new Vector3(0f, 1.3f, 2.55f), new Vector3(4.4f, 2.6f, 0.1f), wallMat);
            Box("WallSouth", room, new Vector3(0f, 1.3f, -2.55f), new Vector3(4.4f, 2.6f, 0.1f), wallMat);
            Box("WallEast", room, new Vector3(2.05f, 1.3f, 0f), new Vector3(0.1f, 2.6f, 5.4f), wallMat);
            Box("WallWest", room, new Vector3(-2.05f, 1.3f, 0f), new Vector3(0.1f, 2.6f, 5.4f), wallMat);

            // bed against the west wall; mattress top at 0.45 m so she can sit with both feet on the floor
            Box("BedFrame", room, new Vector3(-1.4f, 0.115f, -0.9f), new Vector3(1.0f, 0.23f, 2.0f), woodMat);
            Box("Mattress", room, new Vector3(-1.4f, 0.34f, -0.9f), new Vector3(0.96f, 0.22f, 1.9f), bedMat);
            Box("Pillow", room, new Vector3(-1.4f, 0.5f, -1.65f), new Vector3(0.6f, 0.1f, 0.3f), pillowMat);

            // desk under the window on the north wall
            Box("DeskTop", room, new Vector3(0.2f, DeskTop - 0.025f, 2.1f), new Vector3(1.4f, 0.05f, 0.7f), woodMat);
            Box("DeskLegL", room, new Vector3(-0.45f, (DeskTop - 0.05f) / 2f, 2.1f), new Vector3(0.05f, DeskTop - 0.05f, 0.6f), woodMat);
            Box("DeskLegR", room, new Vector3(0.85f, (DeskTop - 0.05f) / 2f, 2.1f), new Vector3(0.05f, DeskTop - 0.05f, 0.6f), woodMat);

            // window spot: dark frame + the night picture on the inner face of the north wall
            Box("WindowFrame", room, new Vector3(0.2f, 1.65f, 2.49f), new Vector3(1.5f, 1.1f, 0.02f), frameMat);
            var win = GameObject.CreatePrimitive(PrimitiveType.Quad);
            win.name = "WindowNight";
            win.transform.SetParent(room, false);
            win.transform.localPosition = new Vector3(0.2f, 1.65f, 2.475f);
            win.transform.localScale = new Vector3(1.4f, 1.0f, 1f);
            win.GetComponent<Renderer>().sharedMaterial = WindowMaterial();
            Object.DestroyImmediate(win.GetComponent<Collider>());

            // the GPT wall picture (curtained window, shelf, plant, photos) on the inner face of the west wall,
            // the wall behind the bed that cuts 11 and 12 look at. It has its own window, and the old night window
            // above stays on the north wall (the desk wall), so the two never overlap.
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "WallBackdrop";
            backdrop.transform.SetParent(room, false);
            backdrop.transform.localPosition = new Vector3(BackdropX, BackdropHeight * 0.5f, BackdropZ);
            backdrop.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); // faces +X, into the room
            backdrop.transform.localScale = new Vector3(BackdropHeight * 1080f / 1920f * BackdropStretch, BackdropHeight, 1f);
            backdrop.GetComponent<Renderer>().sharedMaterial = BackdropMaterial();
            Object.DestroyImmediate(backdrop.GetComponent<Collider>());
        }

        // floor to ceiling (2.6 m); the blank lower wall of the picture meets the bed (top 0.45 m) behind its pillow end
        const float BackdropX = -1.995f, BackdropZ = -1.25f, BackdropHeight = 2.6f;
        // a little wider than the picture's own shape so the cut 11 camera, which looks along the wall, does not see its side edges
        const float BackdropStretch = 1.5f;
        const float BackdropTint = 0.9f; // unlit, so this is the whole brightness (0.8 to 1.0)

        static Material BackdropMaterial()
        {
            var m = Mat("RoomWallBackdrop", new Color(BackdropTint, BackdropTint, BackdropTint), "Universal Render Pipeline/Unlit");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/room_wall_backdrop.png");
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildLights()
        {
            var room = GameObject.Find("Room").transform;

            var lamp = new GameObject("RoomLamp");
            lamp.transform.SetParent(room, false);
            lamp.transform.localPosition = new Vector3(0.1f, 2.35f, 0.4f);
            var l = lamp.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.72f, 0.42f);
            l.intensity = 6f;
            l.range = 7f;
            l.shadows = LightShadows.None;
            SetLightLayers(l, 1);

            var moon = new GameObject("WindowMoonLight");
            moon.transform.SetParent(room, false);
            moon.transform.localPosition = new Vector3(0.2f, 1.8f, 2.3f);
            moon.transform.localRotation = Quaternion.Euler(25f, 180f, 0f);
            var m = moon.AddComponent<Light>();
            m.type = LightType.Spot;
            m.color = new Color(0.45f, 0.58f, 1f);
            m.intensity = 20f;
            m.range = 6f;
            m.spotAngle = 85f;
            m.innerSpotAngle = 40f;
            m.shadows = LightShadows.None;
            SetLightLayers(m, 1);
        }

        // ------------------------------------------------------------ phone

        static Transform BuildPhone()
        {
            var phone = new GameObject("Phone").transform;
            phone.position = new Vector3(PhoneCenter.x, PhoneCenter.y + 0.004f, PhoneCenter.z);
            Box("Body", phone, Vector3.zero, new Vector3(ScreenW + 0.006f, 0.008f, ScreenH + 0.008f),
                Mat("PhoneBody", new Color(0.05f, 0.05f, 0.06f), smoothness: 0.6f));

            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(phone, false);
            screen.transform.localPosition = new Vector3(0f, 0.0041f, 0f);
            screen.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            screen.transform.localScale = new Vector3(ScreenW, ScreenH, 1f);
            screen.GetComponent<Renderer>().sharedMaterial = Mat("PhoneScreen", new Color(0.1f, 0.1f, 0.18f), "Universal Render Pipeline/Unlit");
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            return phone;
        }

        static Camera BuildMainCamera(Transform phone)
        {
            const float distance = 0.2389f;
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            go.transform.position = phone.position + new Vector3(0f, 0.0042f + distance, 0f);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f);
            cam.fieldOfView = 30f;
            go.AddComponent<AudioListener>();
            go.AddComponent<PhoneCameraFit>();
            return cam;
        }

        static void BuildOverviewCamera()
        {
            var go = new GameObject("RoomOverviewCamera");
            go.transform.position = new Vector3(1.7f, 2.3f, -2.3f);
            go.transform.LookAt(new Vector3(-0.3f, 0.7f, 1.4f));
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            cam.fieldOfView = 75f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.05f);
            cam.enabled = false; // test camera; act 1 does not use it
        }

        // ------------------------------------------------------------ UI

        static ChatView BuildCanvas(Camera worldCamera)
        {
            var go = new GameObject("PhoneCanvas", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = worldCamera;
            go.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(ChatView.CanvasWidth, ChatView.CanvasHeight);
            rt.rotation = Quaternion.Euler(90f, 0f, 0f);
            float s = ScreenH / ChatView.CanvasHeight;
            rt.localScale = new Vector3(s, s, s);
            rt.anchoredPosition3D = new Vector3(PhoneCenter.x, PhoneCenter.y + 0.0085f + 0.0005f, PhoneCenter.z);

            var chat = go.AddComponent<ChatView>();
            var so = new SerializedObject(chat);
            so.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>("Assets/Oddity/Fonts/Pretendard/PretendardVariable.ttf");
            so.FindProperty("harinTexture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/Oddity/RenderTextures/AppStage.renderTexture");
            so.FindProperty("glitchMaterial").objectReferenceValue = GlitchMaterial();
            so.FindProperty("wallpaper").objectReferenceValue = LoadSprite("chat_wallpaper.png");
            so.FindProperty("appIcon").objectReferenceValue = LoadSprite("app_icon.png");
            so.ApplyModifiedPropertiesWithoutUndo();
            return chat;
        }

        static Material GlitchMaterial()
        {
            string path = MatDir + "/UIGlitch.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Oddity/UIGlitch"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static void BuildEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        static void BuildBootstrap(ChatView chat)
        {
            var go = new GameObject("GameBootstrap");
            var boot = go.AddComponent<GameBootstrap>();
            var so = new SerializedObject(boot);
            so.FindProperty("chat").objectReferenceValue = chat;
            so.FindProperty("harin").objectReferenceValue = Object.FindFirstObjectByType<HarinPuppet>();
            so.FindProperty("cut10DeskPhone").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/cut10_desk_phone.png");
            so.FindProperty("screenNoise").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/screen_noise.png");
            so.FindProperty("portalRing").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/portal_ring.png");
            so.FindProperty("wristSwirl").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/wrist_swirl.png");
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
