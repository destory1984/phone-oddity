using System;
using System.Collections.Generic;

namespace Oddity.Core
{
    public enum Slot
    {
        Morning,
        Evening,
        Night
    }

    public class Effects
    {
        public int Stability;
        public int Deviation;
        public int Heart;
    }

    public class Line
    {
        public string Who;
        public string Text;
        public string Face = "neutral";
        public float Glitch;
    }

    public class Choice
    {
        public string Text;
        public Effects Effects = new Effects();
        public string Next;
    }

    public class Scene
    {
        public string Id;
        public Slot Slot;
        /// <summary>"talk" (default), "feeling" or "task".</summary>
        public string Type = "talk";
        public List<Line> Lines = new List<Line>();
        public List<Choice> Choices = new List<Choice>();
    }

    public class DayScript
    {
        public int Day;
        public List<Scene> Scenes = new List<Scene>();

        public Scene Get(string id)
        {
            foreach (Scene scene in Scenes)
            {
                if (scene.Id == id) return scene;
            }
            return null;
        }

        /// <summary>First scene of the slot in file order, or null if none.</summary>
        public Scene EntryFor(Slot slot)
        {
            foreach (Scene scene in Scenes)
            {
                if (scene.Slot == slot) return scene;
            }
            return null;
        }
    }

    public class ScriptFormatException : Exception
    {
        public ScriptFormatException(string message) : base(message) { }

        public ScriptFormatException(string message, Exception inner) : base(message, inner) { }
    }
}
