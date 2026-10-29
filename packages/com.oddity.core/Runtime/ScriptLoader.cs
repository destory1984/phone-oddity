using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Oddity.Core
{
    public static class ScriptLoader
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static DayScript Parse(string json)
        {
            if (json == null) throw new ScriptFormatException("Script JSON is null.");
            json = json.TrimStart('\uFEFF');

            DayScript script;
            try
            {
                script = JsonConvert.DeserializeObject<DayScript>(json, Settings);
            }
            catch (JsonException e)
            {
                throw new ScriptFormatException("Script JSON is invalid: " + e.Message, e);
            }
            if (script == null) throw new ScriptFormatException("Script JSON is empty.");

            Normalize(script);
            Validate(script);
            return script;
        }

        // JSON null / omitted collections come back as null or replace the defaults.
        private static void Normalize(DayScript script)
        {
            script.Scenes = script.Scenes ?? new List<Scene>();
            script.Scenes.RemoveAll(s => s == null);
            foreach (Scene scene in script.Scenes)
            {
                scene.Type = scene.Type ?? "talk";
                scene.Lines = scene.Lines ?? new List<Line>();
                scene.Choices = scene.Choices ?? new List<Choice>();
                scene.Lines.RemoveAll(l => l == null);
                scene.Choices.RemoveAll(c => c == null);
                foreach (Line line in scene.Lines)
                {
                    line.Face = line.Face ?? "neutral";
                }
                foreach (Choice choice in scene.Choices)
                {
                    choice.Effects = choice.Effects ?? new Effects();
                }
            }
        }

        private static void Validate(DayScript script)
        {
            var ids = new HashSet<string>();
            foreach (Scene scene in script.Scenes)
            {
                if (string.IsNullOrEmpty(scene.Id))
                    throw new ScriptFormatException("Scene without id in day " + script.Day + ".");
                if (!ids.Add(scene.Id))
                    throw new ScriptFormatException("Duplicate scene id \"" + scene.Id + "\".");
            }

            foreach (Scene scene in script.Scenes)
            {
                foreach (Choice choice in scene.Choices)
                {
                    // No next means the slot ends here; only a dangling target is an error.
                    if (!string.IsNullOrEmpty(choice.Next) && !ids.Contains(choice.Next))
                        throw new ScriptFormatException(
                            "Scene \"" + scene.Id + "\" has a choice whose next \"" + choice.Next + "\" does not exist.");
                }
            }
        }
    }
}
