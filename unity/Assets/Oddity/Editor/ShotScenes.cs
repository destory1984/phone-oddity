using System;
using System.Collections.Generic;
using System.Linq;
using Oddity.Core;
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
        /// <summary>True: start like a first launch, with the prologue. False: skip it (day 1 as before).</summary>
        public bool Prologue;
    }

    public static class ShotScenes
    {
        const string PrototypeScene = "Assets/Scenes/Prototype.unity";

        // New game as "민수", then tap through the morning until `stop` says to stay.
        static ChatView PlayMorning(Func<ChatView, bool> stop)
        {
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            if (chat == null) throw new Exception("no ChatView in the scene");
            ChatView.TypingEnabled = false; // shots tap through lines without waiting
            chat.SubmitName("민수");
            for (int i = 0; i < 30 && !stop(chat); i++) chat.Tap();
            return chat;
        }

        // New game as "민수", jump to the day and slot, tap `taps` times.
        static void PlayDay(int day, Slot slot, int taps)
        {
            FitPhoneCamera();
            var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (chat == null || boot == null) throw new Exception("no ChatView or GameBootstrap in the scene");
            ChatView.TypingEnabled = false;
            chat.SubmitName("민수");
            boot.JumpTo(day, slot);
            for (int i = 0; i < taps; i++) chat.Tap();
        }

        static void FitPhoneCamera()
        {
            var cam = GameObject.Find("MainCamera").GetComponent<Camera>();
            cam.aspect = 9f / 16f; // batch mode has no game view, so fix the phone's aspect
        }

        // First launch: tap (or pick the first choice) through the prologue until `stop` says to stay.
        static GameBootstrap PlayPrologue(Func<GameBootstrap, bool> stop)
        {
            FitPhoneCamera();
            ChatView.TypingEnabled = false; // no breath pause, so taps land at once
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            if (boot == null || !boot.InPrologue) throw new Exception("the prologue did not start");
            for (int i = 0; i < 80 && !stop(boot); i++)
            {
                if (boot.Subtitle.VisibleChoiceTexts.Count > 0) boot.Subtitle.PickChoice(0);
                else boot.Subtitle.Tap();
            }
            if (!stop(boot)) throw new Exception("prologue line not reached");
            boot.Voice.Stop(); // the still shows the expression, not a random lip-sync frame
            return boot;
        }

        static bool AtLine(GameBootstrap boot, string scene, string beat) =>
            boot.PrologueSceneId == scene && boot.Prologue != null && boot.Prologue.CurrentBeat == beat;

        public static readonly IReadOnlyList<ShotScene> All = new List<ShotScene>
        {
            new ShotScene
            {
                Name = "prologue_dinner", // p_dinner line 3 ("teases"), before any close-up
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Prologue = true,
                Setup = () => PlayPrologue(b => AtLine(b, "p_dinner", "teases")).Prologue.SeekBeat(0.8f),
            },
            new ShotScene
            {
                Name = "prologue_closeup", // p_side_by_side line 6 ("quiet_confession"), the closest shot
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Prologue = true,
                Setup = () => PlayPrologue(b => AtLine(b, "p_side_by_side", "quiet_confession")).Prologue.SeekBeat(1f),
            },
            new ShotScene
            {
                Name = "prologue_notice", // the lock-screen notice "19일 후 삭제됩니다"
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Prologue = true,
                Setup = () => PlayPrologue(b => AtLine(b, "p_rewind", PrologueDirector.NoticeBeat)).Prologue.SeekBeat(0.6f),
            },
            new ShotScene
            {
                Name = "prologue_rewind", // 1.2 s into the rewind
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Prologue = true,
                Setup = () =>
                {
                    var boot = PlayPrologue(b => b.Prologue != null && b.Prologue.IsRewinding);
                    boot.Prologue.RewindSpeed = 0f;
                    boot.Prologue.SeekRewind(1.2f);
                },
            },
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
                Name = "chat_poke_cheek", // the cheek touched twice: the stage 2 reaction
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () =>
                {
                    FitPhoneCamera();
                    int taps = 0;
                    var chat = PlayMorning(c => taps++ >= 1);
                    var boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
                    var harin = UnityEngine.Object.FindFirstObjectByType<HarinPuppet>();
                    var cam = GameObject.Find("AppStageCamera").GetComponent<Camera>();
                    double clock = 100;
                    PokeController.ClockOverride = () => clock;
                    boot.Voice.Stop();
                    var cheek = PokeZones.ViewportOf(cam, harin, 0f, -0.04f);
                    chat.PokeWindow(cheek);
                    clock = 103;
                    chat.PokeWindow(cheek);
                },
            },
            new ShotScene
            {
                Name = "chat_day2_morning", // day 2 morning, third line
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () => PlayDay(2, Slot.Morning, 2),
            },
            new ShotScene
            {
                Name = "chat_day3_night", // day 3 night, fourth line
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () => PlayDay(3, Slot.Night, 3),
            },
            new ShotScene
            {
                Name = "chat_typing", // the typing bubble before the first morning line
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () =>
                {
                    FitPhoneCamera();
                    var chat = UnityEngine.Object.FindFirstObjectByType<ChatView>();
                    if (chat == null) throw new Exception("no ChatView in the scene");
                    ChatView.TypingEnabled = true;
                    chat.SubmitName("민수");
                    for (int i = 0; i < 5 && !chat.IsTyping; i++) chat.Tap(); // past the system lines
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
                Name = "emergence_hand", // storyboard cut 10, finger tips reach the desk
                ScenePath = PrototypeScene,
                CameraName = "Cut10Camera",
                Setup = () =>
                {
                    var seq = HoldEmergence(3f);
                    seq.Cut10Camera.aspect = 9f / 16f;
                },
            },
            new ShotScene
            {
                Name = "emergence_pullback", // storyboard cut 11, looking at her hands
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () => HoldEmergence(5f),
            },
            new ShotScene
            {
                Name = "emergence_seated", // storyboard cut 12, eyes to the camera
                ScenePath = PrototypeScene,
                CameraName = "MainCamera",
                Setup = () => HoldEmergence(8f),
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
