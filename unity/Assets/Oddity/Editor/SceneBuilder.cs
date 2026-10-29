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
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
