using UnityEditor;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Import settings of the Asset Store "Cartoon Cat" files in Assets/Oddity/DevMotion/Cat (git-ignored, not redistributed): a Generic rig
    /// (not Humanoid, not Legacy), one Avatar shared by every file (built from cat_Idle), clips that loop, no materials (RoomPlay builds a URP Lit
    /// one from Texture.png). Generic clips find their bones by path, so every file's clip plays on the one cat_Idle skeleton.
    /// </summary>
    public class CatImport : AssetPostprocessor
    {
        public const string Folder = "Assets/Oddity/DevMotion/Cat/";
        const string Master = Folder + "cat_Idle.fbx";

        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder) || !assetPath.EndsWith(".fbx")) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            if (assetPath == Master) importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            else
            {
                Avatar master = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Master)) if (o is Avatar av) master = av;
                if (master != null) { importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = master; }
                else importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; // first pass: the master has no Avatar yet
            }
            importer.importAnimation = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Folder) || !assetPath.EndsWith(".fbx")) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips) { c.loopTime = true; c.loopPose = true; }
            importer.clipAnimations = clips;
        }
    }
}
