using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Batch Windows build for playing on a PC: Unity.exe -batchmode -quit -projectPath unity
    ///   -executeMethod Oddity.EditorTools.BuildWindows.Build
    /// Writes unity/Build/pc/oddity-proto.exe (development build, Mono, 540x960 resizable window).
    /// Player settings touched here are restored before exit so ProjectSettings stays unchanged.
    /// </summary>
    public static class BuildWindows
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string OutputRelative = "Build/pc/oddity-proto.exe";
        const string DemoOutputRelative = "Build/pc-DEMO-private/oddity-proto.exe";

        public static void Build()
        {
            // saved player settings, restored in finally
            int w = PlayerSettings.defaultScreenWidth, h = PlayerSettings.defaultScreenHeight;
            FullScreenMode mode = PlayerSettings.fullScreenMode;
            bool resizable = PlayerSettings.resizableWindow;
            ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            int exit = 1;
            try
            {
                bool demo = BuildAndroid.IsDemo();
                string karina = BuildAndroid.FindKarinaVoice();
                if (karina != null && !demo)
                {
                    Debug.LogError("BUILD FAILED: voice files of a real person are in Resources/Voice (" + karina
                        + "). Move them out, or use python tools/dev.py build --pc --demo-voice for a private demo build.");
                    return;
                }
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", demo ? DemoOutputRelative : OutputRelative));
                string dir = Path.GetDirectoryName(output);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);

                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                {
                    Debug.LogError("BUILD FAILED: cannot switch active build target to Windows");
                    return;
                }
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                PlayerSettings.defaultScreenWidth = 540;
                PlayerSettings.defaultScreenHeight = 960;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.resizableWindow = true;

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                };
                BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("BUILD FAILED: build " + summary.result + ", errors " + summary.totalErrors);
                    return;
                }
                Debug.Log("BUILD ok " + output + " " + summary.totalSize + " bytes");
                exit = 0;
            }
            catch (Exception e)
            {
                Debug.LogError("BUILD FAILED: " + e);
            }
            finally
            {
                PlayerSettings.defaultScreenWidth = w;
                PlayerSettings.defaultScreenHeight = h;
                PlayerSettings.fullScreenMode = mode;
                PlayerSettings.resizableWindow = resizable;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(exit);
            }
        }
    }
}
