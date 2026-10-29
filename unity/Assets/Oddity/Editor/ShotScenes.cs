using System;
using System.Collections.Generic;
using System.Linq;
using Oddity.Game;
using UnityEngine;

namespace Oddity.EditorTools
{
    /// <summary>
    /// One screenshot target: which scene to open, which camera draws it, and how to set the
    /// fixed starting state. Setup runs in play mode, after the scene's Awake/Start have run.
    /// Keep ShotScenes.txt in sync (tools/dev.py scenes reads it).
    /// </summary>
    public sealed class ShotScene
    {
        public string Name;
        public string ScenePath;
        public string CameraName;
        public Action Setup;
    }

    public static class ShotScenes
    {
        const string PrototypeScene = "Assets/Scenes/Prototype.unity";

        // New game as "민수", then tap through the morning until `stop` says to stay.
        static ChatView PlayMorning(Func<ChatView, bool> stop)
        {
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            if (chat == null) throw new Exception("no ChatView in the scene");
            chat.SubmitName("민수");
            for (int i = 0; i < 30 && !stop(chat); i++) chat.Tap();
            return chat;
        }

        static void FitPhoneCamera()
        {
            var cam = GameObject.Find("MainCamera").GetComponent<Camera>();
            cam.aspect = 9f / 16f; // batch mode has no game view, so fix the phone's aspect
        }

        public static readonly IReadOnlyList<ShotScene> All = new List<ShotScene>
        {
            new ShotScene
            {
                Name = "appstage",
                ScenePath = "Assets/Scenes/Prototype.unity",
                CameraName = "AppStageCamera",
                Setup = () =>
                {
                    var harin = UnityEngine.Object.FindFirstObjectByType<HarinPuppet>();
                    if (harin == null) throw new Exception("no HarinPuppet in the scene");
                    harin.SetFace("neutral");
                    harin.SetGlitch(0f);
                    harin.SetFade(1f);
                },
            },
            new ShotScene
            {
                Name = "chat_morning",
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () =>
                {
                    FitPhoneCamera();
                    int taps = 0;
                    PlayMorning(c => taps++ >= 1); // second line of the morning
                },
            },
            new ShotScene
            {
                Name = "chat_choices",
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () =>
                {
                    FitPhoneCamera();
                    PlayMorning(c => c.VisibleButtonTexts.Count > 0);
                },
            },
            new ShotScene
            {
                Name = "dev_panel", // morning chat with the dev panel open
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () =>
                {
                    FitPhoneCamera();
                    int taps = 0;
                    PlayMorning(c => taps++ >= 1);
                    var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                    if (boot.Dev == null) throw new Exception("no DevPanel (editor only)");
                    boot.Dev.Toggle();
                },
            },
            new ShotScene
            {
                Name = "room_overview",
                ScenePath = PrototypeScene,
                CameraName = "RoomOverviewCamera",
                Setup = () =>
                {
                    var cam = GameObject.Find("MainCamera").GetComponent<Camera>();
                    cam.enabled = false;
                    var overview = FindInactive("RoomOverviewCamera");
                    overview.aspect = 1f;
                    overview.enabled = true;
                },
            },
            new ShotScene
            {
                Name = "emergence_hand", // storyboard cut 10
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () => HoldEmergence(2.75f),
            },
            new ShotScene
            {
                Name = "emergence_pullback", // storyboard cut 11, mid pull-back
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () => HoldEmergence(7f),
            },
            new ShotScene
            {
                Name = "room_after", // storyboard cut 12, first act 2 line
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () => HoldEmergence(10f),
            },
        };

        // New game as "민수", then hold the emergence at time t (Speed 0 keeps it there).
        static EmergenceSequence HoldEmergence(float t)
        {
            FitPhoneCamera();
            PlayMorning(c => true);
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            var seq = boot.Emergence;
            seq.Speed = 0f;
            seq.Play();
            seq.Seek(t);
            return seq;
        }

        static Camera FindInactive(string name) => UnityEngine.Object
            .FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .First(c => c.name == name);

        public static ShotScene Find(string name) => All.FirstOrDefault(s => s.Name == name);
    }
}
