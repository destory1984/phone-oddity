using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// Batch Android build: Unity.exe -batchmode -quit -projectPath unity
    ///   -executeMethod Oddity.EditorTools.BuildAndroid.Build
    /// Writes unity/Build/oddity-proto.apk (development build, debug key, ARM64 only).
    /// IL2CPP is required: Mono has no ARM64 on Android ("Target architecture not specified").
    /// </summary>
    public static class BuildAndroid
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string OutputRelative = "Build/oddity-proto.apk";

        public static void Build()
        {
            try
            {
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputRelative));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                if (File.Exists(output)) File.Delete(output);

                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Fail("cannot switch active build target to Android");
                    return;
                }

                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.oddityphone.proto");
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
                PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto; // highest installed
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android,
                    ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                EditorUserBuildSettings.buildAppBundle = false;

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development, // no AllowDebugging / ConnectWithProfiler
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Fail("build " + summary.result + ", errors " + summary.totalErrors);
                    return;
                }
                Debug.Log("BUILD ok " + output + " " + summary.totalSize + " bytes");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Fail(e.ToString());
            }
        }

        static void Fail(string message)
        {
            Debug.LogError("BUILD FAILED: " + message);
            EditorApplication.Exit(1);
        }
    }
}
