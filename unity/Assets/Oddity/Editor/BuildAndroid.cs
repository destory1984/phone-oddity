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
        const string DemoOutputRelative = "Build/oddity-proto-DEMO-private.apk";
        const string VoiceDir = "Audio/Placeholder/Resources/Voice";
        const string DemoVoiceEnv = "ODDITY_DEMO_VOICE";

        public static void Build()
        {
            try
            {
                // Files of a real person's voice must not ship by accident: only ODDITY_DEMO_VOICE=1 lets them through.
                bool demo = IsDemo();
                string karina = FindKarinaVoice();
                if (karina != null && !demo)
                {
                    Fail("voice files of a real person are in Resources/Voice (" + karina
                        + "). Move them out, or set " + DemoVoiceEnv + "=1 for a private demo build (python tools/dev.py build --demo-voice).");
                    return;
                }
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", demo ? DemoOutputRelative : OutputRelative));
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

        internal static bool IsDemo()
        {
            return Environment.GetEnvironmentVariable(DemoVoiceEnv) == "1";
        }

        /// <summary>File name of the first karina voice file in Resources/Voice, or null.</summary>
        internal static string FindKarinaVoice()
        {
            string voiceDir = Path.Combine(Application.dataPath, VoiceDir);
            if (!Directory.Exists(voiceDir)) return null;
            foreach (string f in Directory.GetFiles(voiceDir))
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(f).IndexOf("karina", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Path.GetFileName(f);
            }
            return null;
        }

        static void Fail(string message)
        {
            Debug.LogError("BUILD FAILED: " + message);
            EditorApplication.Exit(1);
        }
    }
}
