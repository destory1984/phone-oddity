using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Oddity.Core
{
    public class GameState
    {
        public const int CurrentVersion = 1;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.DateTime,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Formatting = Formatting.Indented
        };

        public int Version = CurrentVersion;
        public int Day = 1;
        public Slot Slot = Slot.Morning;
        public int Stability = 50;
        public int Deviation = 0;
        public int Heart = 0;
        public int DaysLeft = 30;
        public string PlayerName = "";
        public DateTime NextOpenUtc;

        public void Apply(Effects e)
        {
            if (e == null) return;
            Stability = Clamp(Stability + e.Stability);
            Deviation = Clamp(Deviation + e.Deviation);
            Heart = Clamp(Heart + e.Heart);
        }

        private static int Clamp(int v)
        {
            return v < 0 ? 0 : (v > 100 ? 100 : v);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Settings);
        }

        public static GameState FromJson(string json, out bool recovered)
        {
            try
            {
                GameState s = JsonConvert.DeserializeObject<GameState>((json ?? "").TrimStart('\uFEFF'), Settings);
                if (s != null && s.Version == CurrentVersion)
                {
                    s.PlayerName = s.PlayerName ?? "";
                    s.NextOpenUtc = DateTime.SpecifyKind(s.NextOpenUtc, DateTimeKind.Utc);
                    recovered = false;
                    return s;
                }
            }
            catch (JsonException)
            {
            }
            recovered = true;
            return new GameState();
        }
    }
}
