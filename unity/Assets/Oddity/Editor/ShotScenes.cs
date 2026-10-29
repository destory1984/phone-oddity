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
        };

        public static ShotScene Find(string name) => All.FirstOrDefault(s => s.Name == name);
    }
}
