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

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
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
