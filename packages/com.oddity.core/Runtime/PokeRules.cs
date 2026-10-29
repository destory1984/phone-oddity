using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Oddity.Core
{
    /// <summary>Where Harin's window was touched (see shots/plans/poke_reactions.md).</summary>
    public enum PokeZone
    {
        Head,
        Cheek,
        Window
    }

    /// <summary>One reaction line of poke.json.</summary>
    public class PokeLine
    {
        public string Zone = "";
        public int Stage = 1;
        public string Text = "";
        public string Face = "neutral";
        public string Motion = "";

        /// <summary>Stable id for the line (zone, stage and text), so a voice file can be attached later.</summary>
        public string Id => Zone + "_" + Stage + "_" + Text;
    }

    /// <summary>The 18 lines of poke.json.</summary>
    public class PokeTable
    {
        public List<PokeLine> Lines = new List<PokeLine>();

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static PokeTable Parse(string json)
        {
            if (json == null) throw new ScriptFormatException("Poke JSON is null.");
            PokeTable table;
            try
            {
                table = JsonConvert.DeserializeObject<PokeTable>(json.TrimStart('\uFEFF'), Settings);
            }
            catch (JsonException e)
            {
                throw new ScriptFormatException("Poke JSON is invalid: " + e.Message, e);
            }
            if (table == null) throw new ScriptFormatException("Poke JSON is empty.");
            table.Lines = table.Lines ?? new List<PokeLine>();
            table.Lines.RemoveAll(l => l == null);
            return table;
        }

        public static string ZoneName(PokeZone zone)
        {
            return zone == PokeZone.Head ? "head" : zone == PokeZone.Cheek ? "cheek" : "window";
        }

        public List<PokeLine> For(PokeZone zone, int stage)
        {
            string name = ZoneName(zone);
            var list = new List<PokeLine>();
            foreach (PokeLine l in Lines)
            {
                if (l.Zone == name && l.Stage == stage) list.Add(l);
            }
            return list;
        }
    }

    public enum PokeOutcome
    {
        /// <summary>Dropped: inside the 2 s dead time, resting after stage 3, or no line to show.</summary>
        Ignored,
        /// <summary>Only a short eye contact, no line (daily cap reached).</summary>
        EyeContact,
        /// <summary>A reaction line to show.</summary>
        Reaction
    }

    public struct PokeResult
    {
        public PokeOutcome Outcome;
        public int Stage;
        public PokeLine Line;
    }

    /// <summary>
    /// Rules for touching Harin in the phone: stages shared by all zones, 2 s of dead time after a reaction,
    /// 8 s to reach the next stage, 30 s rest after stage 3, 9 line reactions per game day. No hidden value changes.
    /// Time is passed in (seconds on any steady clock) so tests need no real clock.
    /// </summary>
    public class PokeRules
    {
        public const double DeadSeconds = 2.0;
        public const double StageWindowSeconds = 8.0;
        public const double RestSeconds = 30.0;
        public const int DailyCap = 9;
        public const int LastStage = 3;

        private readonly GameState _state;
        private readonly PokeTable _table;
        private readonly Random _random;
        private readonly HashSet<string> _usedToday = new HashSet<string>();
        private double _lastValid = double.NegativeInfinity;
        private double _restUntil = double.NegativeInfinity;
        private int _stage;
        private int _usedDay = int.MinValue;

        public PokeRules(GameState state, PokeTable table, int seed = 0)
        {
            _state = state;
            _table = table;
            _random = new Random(seed);
        }

        /// <summary>Stage of the last reaction (0 = none yet or reset).</summary>
        public int Stage => _stage;
        /// <summary>Reactions with a line shown on the current game day.</summary>
        public int PokesToday => _state.PokeDay == _state.Day ? _state.PokesToday : 0;

        /// <summary>A touch on the window at <paramref name="now"/> seconds.</summary>
        public PokeResult Poke(PokeZone zone, double now)
        {
            NewDayCheck();
            var none = new PokeResult { Outcome = PokeOutcome.Ignored };
            if (now < _restUntil) return none;
            if (now - _lastValid < DeadSeconds) return none;
            _lastValid = now;

            if (PokesToday >= DailyCap) return new PokeResult { Outcome = PokeOutcome.EyeContact };

            int stage = (_stage == 0 || now - _lastValidReaction > StageWindowSeconds) ? 1 : _stage + 1;
            if (stage > LastStage) stage = LastStage;
            List<PokeLine> options = _table.For(zone, stage);
            if (options.Count == 0) return none;

            var fresh = new List<PokeLine>();
            foreach (PokeLine l in options)
            {
                if (!_usedToday.Contains(l.Id)) fresh.Add(l);
            }
            List<PokeLine> pool = fresh.Count > 0 ? fresh : options;
            PokeLine pick = pool[_random.Next(pool.Count)];
            _usedToday.Add(pick.Id);

            _stage = stage;
            _lastValidReaction = now;
            _state.PokeDay = _state.Day;
            _state.PokesToday = PokesToday + 1;
            if (stage >= LastStage)
            {
                _restUntil = now + RestSeconds;
                _stage = 0;
            }
            return new PokeResult { Outcome = PokeOutcome.Reaction, Stage = stage, Line = pick };
        }

        private double _lastValidReaction = double.NegativeInfinity;

        // A new game day starts the count and the used lines again.
        private void NewDayCheck()
        {
            if (_usedDay == _state.Day) return;
            _usedDay = _state.Day;
            _usedToday.Clear();
            if (_state.PokeDay != _state.Day)
            {
                _state.PokeDay = _state.Day;
                _state.PokesToday = 0;
            }
        }
    }
}
