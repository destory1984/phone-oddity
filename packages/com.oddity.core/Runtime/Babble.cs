using System;
using System.Collections.Generic;

namespace Oddity.Core
{
    /// <summary>Pitch of the babble voice used for the player's lines.</summary>
    public enum BabbleVoice
    {
        Low = 0,
        Mid = 1,
        High = 2,
    }

    /// <summary>
    /// Synthesizes meaningless chatter for a line of text, one short sound per letter (the way a character in
    /// a village-life game "talks"). Pure C#, no engine: the same text always gives the same samples.
    /// A Hangul syllable is split into initial, vowel and final. The vowel sets the pitch and the two formants,
    /// the initial sets a short head sound (burst, hiss or soft hum), a final shortens the syllable. The body
    /// is a sine with weak overtones shaped by the vowel formants (additive synthesis, no samples are used).
    /// </summary>
    public static class Babble
    {
        public const int DefaultSampleRate = 44100;
        public const float SyllableSeconds = 0.070f;
        public const float ShortSyllableSeconds = 0.055f;
        public const float FinalFactor = 0.8f;
        public const float GapSeconds = 0.015f;
        public const float FadeSeconds = 0.005f;
        public const float SpaceSeconds = 0.060f;
        public const float CommaSeconds = 0.120f;
        public const float StopSeconds = 0.180f;
        public const float QuestionPitchFactor = 1.15f;
        const float Peak = 0.6f;

        /// <summary>Base pitch in Hz of a voice.</summary>
        public static float BaseHz(BabbleVoice voice)
        {
            switch (voice)
            {
                case BabbleVoice.Low: return 110f;
                case BabbleVoice.High: return 195f;
                default: return 150f;
            }
        }

        // Head sound of an initial consonant.
        enum Head { None, Burst, Hiss, Hum }

        // Initials in unicode order: g gg n d dd r m b bb s ss (silent) j jj ch k t p h
        static readonly Head[] InitialHead =
        {
            Head.Burst, Head.Burst, Head.Hum, Head.Burst, Head.Burst, Head.Hum, Head.Hum, Head.Burst, Head.Burst, Head.Hiss,
            Head.Hiss, Head.None, Head.Burst, Head.Burst, Head.Burst, Head.Burst, Head.Burst, Head.Burst, Head.Hiss,
        };

        // Vowels in unicode order: a ae ya yae eo e yeo ye o wa wae oe yo u wo we wi yu eu ui i
        // F1, F2 in Hz and a pitch offset in semitones (bright vowels sit higher).
        static readonly float[] VowelF1 =
            { 800, 620, 800, 620, 600, 550, 600, 550, 450, 700, 600, 500, 450, 350, 450, 450, 320, 350, 400, 380, 300 };
        static readonly float[] VowelF2 =
            { 1300, 1800, 1400, 1850, 1000, 1900, 1150, 1950, 800, 1100, 1600, 1700, 900, 800, 1000, 1700, 2100, 1000, 1300, 1700, 2300 };
        static readonly float[] VowelSemitone =
            { 0, 1, 1, 2, -1, 1, 0, 2, -2, -1, 1, 1, -1, -3, -2, 0, 3, -2, -3, 0, 4 };

        static float FormantScale(BabbleVoice voice)
        {
            switch (voice)
            {
                case BabbleVoice.Low: return 0.94f;
                case BabbleVoice.High: return 1.08f;
                default: return 1f;
            }
        }

        enum Kind { Syllable, Pause, Skip }

        struct Token
        {
            public Kind Kind;
            public int Initial;     // -1 none
            public int Vowel;       // index into the vowel tables
            public bool HasFinal;
            public bool Short;      // a non-Hangul letter, digit or symbol
            public float Seconds;   // for a pause
            public bool Raise;      // pitch +15% (two syllables before a question mark)
            public bool Question;   // a pause made by a question mark
            public uint Seed;
        }

        /// <summary>Renders the text as mono samples in -1..1. Null or empty text gives an empty array.</summary>
        public static float[] Render(string text, BabbleVoice voice, int sampleRate = DefaultSampleRate)
        {
            if (string.IsNullOrEmpty(text) || sampleRate <= 0) return new float[0];
            var tokens = Tokenize(text);
            MarkQuestions(tokens);

            var samples = new List<float>(tokens.Count * (int)(0.09f * sampleRate) + 16);
            foreach (var t in tokens)
            {
                if (t.Kind == Kind.Skip) continue;
                if (t.Kind == Kind.Pause)
                {
                    AppendSilence(samples, t.Seconds, sampleRate);
                    continue;
                }
                AppendSyllable(samples, t, voice, sampleRate);
                AppendSilence(samples, GapSeconds, sampleRate);
            }
            return samples.ToArray();
        }

        /// <summary>Length in seconds the text would render to.</summary>
        public static float Seconds(string text, BabbleVoice voice, int sampleRate = DefaultSampleRate)
        {
            return Render(text, voice, sampleRate).Length / (float)sampleRate;
        }

        static List<Token> Tokenize(string text)
        {
            var list = new List<Token>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                uint seed = Hash(c, i);
                if (c >= 0xAC00 && c <= 0xD7A3)
                {
                    int code = c - 0xAC00;
                    list.Add(new Token { Kind = Kind.Syllable, Initial = code / 588, Vowel = code % 588 / 28, HasFinal = code % 28 != 0, Seed = seed });
                }
                else if (char.IsWhiteSpace(c)) list.Add(Pause(SpaceSeconds));
                else if (c == ',') list.Add(Pause(CommaSeconds));
                else if (c == '?') { var q = Pause(StopSeconds); q.Question = true; list.Add(q); }
                else if (c == '.' || c == '!' || c == '…') list.Add(Pause(StopSeconds));
                else if (IsQuote(c)) list.Add(new Token { Kind = Kind.Skip });
                else if (char.IsHighSurrogate(c) || char.IsLowSurrogate(c)) list.Add(new Token { Kind = Kind.Skip });
                else
                {
                    // other letters, digits and symbols: one short syllable, the vowel picked by the character
                    list.Add(new Token { Kind = Kind.Syllable, Initial = -1, Vowel = (int)(seed % (uint)VowelF1.Length), Short = true, Seed = seed });
                }
            }
            return list;
        }

        static bool IsQuote(char c)
        {
            return c == '"' || c == '\'' || c == '(' || c == ')' || c == '[' || c == ']' || c == '“' || c == '”'
                || c == '‘' || c == '’' || c == '「' || c == '」' || c == '`';
        }

        static Token Pause(float seconds) => new Token { Kind = Kind.Pause, Seconds = seconds };

        // The two syllables before each question mark go up 15%. The walk back stops at any sentence mark.
        static void MarkQuestions(List<Token> tokens)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].Question) continue;
                int marked = 0;
                for (int j = i - 1; j >= 0 && marked < 2; j--)
                {
                    var t = tokens[j];
                    if (t.Kind == Kind.Pause && t.Seconds > SpaceSeconds) break;
                    if (t.Kind != Kind.Syllable) continue;
                    t.Raise = true;
                    tokens[j] = t;
                    marked++;
                }
            }
        }

        static uint Hash(char c, int index)
        {
            uint h = 2166136261u;
            h = (h ^ c) * 16777619u;
            h = (h ^ (uint)(index & 0xFF)) * 16777619u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return h;
        }

        static void AppendSilence(List<float> samples, float seconds, int sampleRate)
        {
            int n = (int)Math.Round(seconds * sampleRate);
            for (int i = 0; i < n; i++) samples.Add(0f);
        }

        static void AppendSyllable(List<float> samples, Token t, BabbleVoice voice, int sampleRate)
        {
            float seconds = t.Short ? ShortSyllableSeconds : SyllableSeconds;
            if (t.HasFinal) seconds *= FinalFactor;
            int n = Math.Max(2, (int)Math.Round(seconds * sampleRate));
            int fade = Math.Max(1, (int)Math.Round(FadeSeconds * sampleRate));
            if (fade * 2 > n) fade = n / 2;

            int v = Math.Max(0, Math.Min(VowelF1.Length - 1, t.Vowel));
            float scale = FormantScale(voice);
            float f1 = VowelF1[v] * scale, f2 = VowelF2[v] * scale;
            float jitter = ((t.Seed & 0xFF) / 255f - 0.5f) * 0.06f;               // +-3 %
            float glide = (((t.Seed >> 8) & 0xFF) / 255f - 0.5f) * 0.10f;          // -5..+5 % over the syllable
            float hz = BaseHz(voice) * (float)Math.Pow(2.0, VowelSemitone[v] / 12.0) * (1f + jitter);
            if (t.Raise) hz *= QuestionPitchFactor;

            // harmonic weights: 1/k roll-off lifted near the two formants
            int maxK = Math.Max(1, (int)(4200f / hz));
            var weights = new float[maxK + 1];
            float sum = 0f;
            for (int k = 1; k <= maxK; k++)
            {
                float f = k * hz;
                float w = 1f / k;
                float bump = 1f + 5f * Bump(f, f1, 220f) + 3f * Bump(f, f2, 320f);
                // keep the fundamental dominant (sine body), the overtones weak
                weights[k] = k == 1 ? 1f : w * 0.6f * bump;
                sum += weights[k];
            }

            Head head = t.Initial >= 0 && t.Initial < InitialHead.Length ? InitialHead[t.Initial] : Head.None;
            int headLen = head == Head.Burst ? (int)(0.012f * sampleRate) : head == Head.Hiss ? (int)(0.030f * sampleRate)
                : head == Head.Hum ? (int)(0.016f * sampleRate) : 0;
            uint rng = t.Seed | 1u;
            double phase = 0.0;
            float lastNoise = 0f;

            for (int i = 0; i < n; i++)
            {
                float pos = i / (float)n;
                double f0 = hz * (1f + glide * (pos - 0.5f));
                phase += 2.0 * Math.PI * f0 / sampleRate;
                float body = 0f;
                for (int k = 1; k <= maxK; k++) body += weights[k] * (float)Math.Sin(k * phase);
                body /= sum;

                // body swell: quick rise, slow settle
                float env = 0.75f + 0.25f * (float)Math.Sin(Math.PI * Math.Min(1f, pos * 1.6f));
                float s = body * env;

                if (i < headLen)
                {
                    rng = rng * 1664525u + 1013904223u;
                    float noise = ((rng >> 8) / (float)(1 << 24)) * 2f - 1f;
                    float hp = noise - lastNoise * 0.3f; // high-passed: a softer hiss, no rumble
                    lastNoise = noise;
                    float hpos = i / (float)headLen;
                    if (head == Head.Burst) s = s * 0.6f + hp * 0.3f * (1f - hpos);
                    else if (head == Head.Hiss) s = s * 0.6f + hp * 0.16f * (float)Math.Sin(Math.PI * hpos);
                    else s *= 0.35f + 0.65f * hpos; // hum: the vowel fades in
                }
                else if (head == Head.Burst && i < headLen * 3) s *= 0.85f + 0.15f * (i - headLen) / (headLen * 2f);

                float edge = 1f;
                if (i < fade) edge = i / (float)fade;
                else if (i >= n - fade) edge = (n - 1 - i) / (float)fade;
                // raised-cosine fades: no clicks at the edges
                edge = 0.5f - 0.5f * (float)Math.Cos(Math.PI * Math.Max(0f, Math.Min(1f, edge)));
                samples.Add(s * edge * Peak);
            }
        }

        static float Bump(float f, float center, float width)
        {
            float d = (f - center) / width;
            return (float)Math.Exp(-d * d);
        }
    }
}
