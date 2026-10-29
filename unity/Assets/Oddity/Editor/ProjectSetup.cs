using UnityEditor;
using UnityEngine;

namespace Oddity.EditorTools
{
    public static class ProjectSetup
    {
        [MenuItem("Oddity/Apply Project Setup")]
        public static void Apply()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            AssetDatabase.SaveAssets();
            Debug.Log("Oddity: project setup applied");
        }
    }
}
