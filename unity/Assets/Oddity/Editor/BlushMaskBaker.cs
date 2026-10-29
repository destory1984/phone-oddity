using System.IO;
using Oddity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Bakes Harin's cheek mask (<see cref="BlushMask"/>) from the face mesh of the model in Prototype.unity into
    /// Assets/Oddity/Art/harin_blush_mask.png and sets it on the HarinPuppet, so the phone does not have to read
    /// the mesh. Run it again after a model swap (SceneBuilder.SwapHarinModel does). Menu Oddity/Bake Blush Mask, or
    /// Unity -batchmode -executeMethod Oddity.EditorTools.BlushMaskBaker.BakeBatch -quit
    /// </summary>
    public static class BlushMaskBaker
    {
        public const string MaskPath = "Assets/Oddity/Art/harin_blush_mask.png";
        const string ScenePath = "Assets/Scenes/Prototype.unity";

        [MenuItem("Oddity/Bake Blush Mask")]
        public static void BakeMenu() => Bake(true);

        public static void BakeBatch() => Bake(true);

        /// <summary>Bakes from the scene's puppet; <paramref name="openScene"/> false uses the scene already open.</summary>
        public static void Bake(bool openScene)
        {
            var scene = openScene ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single) : EditorSceneManager.GetActiveScene();
            var puppet = Object.FindFirstObjectByType<HarinPuppet>(FindObjectsInactive.Include);
            if (puppet == null) throw new System.Exception("no HarinPuppet in " + ScenePath);
            var root = puppet.gameObject;
            var head = root.GetComponent<Animator>()?.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) throw new System.Exception("no Head bone on " + root.name);
            var face = BlushMask.Find(root, Quaternion.Inverse(root.transform.rotation) * head.rotation);
            if (face == null) throw new System.Exception("no readable face SKIN mesh on " + root.name);

            var tex = BlushMask.Bake(face);
            // grey PNG (R = G = B = mask) so it can be looked at; imported as a single linear channel
            var px = tex.GetPixelData<byte>(0);
            var rgb = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false, true);
            var cols = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++) cols[i] = new Color32(px[i], px[i], px[i], 255);
            rgb.SetPixels32(cols);
            Directory.CreateDirectory(Path.GetDirectoryName(MaskPath));
            File.WriteAllBytes(MaskPath, rgb.EncodeToPNG());
            Object.DestroyImmediate(rgb);
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(MaskPath);
            imp.textureType = TextureImporterType.SingleChannel;
            imp.sRGBTexture = false;
            imp.mipmapEnabled = false;
            imp.isReadable = true; // the tests sample it
            imp.wrapMode = TextureWrapMode.Clamp;
            var ts = new TextureImporterSettings();
            imp.ReadTextureSettings(ts);
            ts.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            imp.SetTextureSettings(ts);
            imp.textureCompression = TextureImporterCompression.Uncompressed; // R8
            imp.SaveAndReimport();

            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            var so = new SerializedObject(puppet);
            so.FindProperty("blushMask").objectReferenceValue = mask;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"BLUSH mask baked {MaskPath} from {face.Renderer.name} (eyeY {face.EyeY:F3}, forward {face.Forward})");
        }
    }
}
