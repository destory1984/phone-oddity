using UnityEditor;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Fixes the import settings of the Mixamo motion files in Assets/Oddity/DevMotion (task 51): Humanoid rig built from
    /// the file itself, one non-looping clip. The folder is git-ignored (the files are not redistributed), so the
    /// settings live here and not in the .meta files.
    /// </summary>
    public class MotionImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Oddity/DevMotion/";
        // task 62: the skin-true export carries a T-pose bind, so its Avatar is symmetric; the skin-false files build an Avatar from the
        // clip's first frame (the left leg comes out bent). These files borrow the skinned Avatar instead (ODDITY_SKINAVATAR=0 turns it off).
        const string SkinFile = Folder + "Resources/Motion/WalkStandardSkin.fbx";
        static readonly string[] UseSkinAvatar = { "WalkStandard" };

        public override uint GetVersion() => 6;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            string stem = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (System.Array.IndexOf(UseSkinAvatar, stem) >= 0 && System.Environment.GetEnvironmentVariable("ODDITY_SKINAVATAR") != "0")
            {
                var skin = AssetDatabase.LoadAssetAtPath<Avatar>(SkinFile);
                if (skin == null) foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SkinFile)) if (o is Avatar av) skin = av;
                if (skin != null) { importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = skin; }
                else UnityEngine.Debug.LogWarning("MotionImport: no skinned Avatar yet for " + assetPath);
            }
            importer.importAnimation = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = false;
                c.loopPose = false;
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
