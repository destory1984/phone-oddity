using System;
using UnityEngine;

namespace Oddity.RoomTest
{
    public static class ZeroTwoMusic
    {
        // Downloaded source onset autocorrelation peaks at 127.9497 BPM (score .864).
        public const float Bpm = 128f;
        public const int Beats = 8;
        public const float Duration = Beats * 60f / Bpm;
        const int SampleRate = 44100;
        const double Tau = Math.PI * 2;

        public static AudioClip CreateClip()
        {
            int frames = (int)Math.Round(Duration * SampleRate);
            var samples = new float[frames];
            double beatSeconds = 60.0 / Bpm;
            // Original A-minor phrase; each entry lasts half a beat, -1 is a rest.
            int[] motif = { 76, -1, 79, 83, 81, 79, 74, -1, 76, 79, 84, 83, 79, 74, 71, -1 };
            int[] bass = { 33, 33, 36, 36, 31, 31, 28, 31 };
            uint random = 0x71a2c935;
            double previousNoise = 0;
            float peak = 0;
            for (int i = 0; i < frames; i++)
            {
                double t = (double)i / SampleRate;
                double beat = t / beatSeconds;
                int wholeBeat = Math.Min(Beats - 1, (int)beat);
                double kickTime = (beat - wholeBeat) * beatSeconds;
                // Integrating the falling pitch avoids discontinuous oscillator phase.
                double kickPhase = Tau * (48 * kickTime + 115 * .022 * (1 - Math.Exp(-kickTime / .022)));
                double kick = .52 * Math.Sin(kickPhase) * Envelope(kickTime, .002, .085, .31);

                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                double rawNoise = random / (double)uint.MaxValue * 2 - 1;
                // One-pole low-pass softens clap/hat transients before their envelopes.
                // Keep filter state local so repeated clip generation stays deterministic.
                double noise = previousNoise + .35 * (rawNoise - previousNoise);
                double highNoise = (noise - previousNoise) * .5;
                previousNoise = noise;
                double snare = 0;
                if ((wholeBeat & 1) == 1)
                {
                    double clap = Envelope(kickTime, .001, .036, .16);
                    if (kickTime >= .012) clap += .5 * Envelope(kickTime - .012, .001, .027, .12);
                    if (kickTime >= .024) clap += .3 * Envelope(kickTime - .024, .001, .026, .10);
                    snare = .15 * noise * clap + .09 * Math.Sin(Tau * 185 * kickTime)
                        * Envelope(kickTime, .001, .032, .15);
                }
                int halfBeat = Math.Min(15, (int)(beat * 2));
                double noteTime = (beat * 2 - halfBeat) * beatSeconds * .5;
                double hat = .085 * highNoise * Envelope(noteTime, .001, .019, .095);
                double bassTime = kickTime;
                double bassHz = Frequency(bass[wholeBeat]);
                double bassPhase = Tau * bassHz * bassTime;
                double bassTone = (Math.Sin(bassPhase) + .23 * Math.Sin(2 * bassPhase))
                    * .20 * Envelope(bassTime, .008, .20, beatSeconds * .88);
                // Duck the bass underneath the kick for a gentler mix.
                bassTone *= .35 + .65 * (1 - Math.Exp(-kickTime / .055));
                double synth = 0;
                if (motif[halfBeat] >= 0)
                {
                    double phase = Tau * Frequency(motif[halfBeat]) * noteTime;
                    synth = .12 * (Math.Sin(phase) + .22 * Math.Sin(2 * phase) + .10 * Math.Sin(3 * phase))
                        * Envelope(noteTime, .006, .13, beatSeconds * .46);
                }
                // Both loop endpoints reach zero; all individual hits also have attack/release ramps.
                double edge = Math.Min(1, Math.Min(t, (frames - 1 - i) / (double)SampleRate) / .003);
                samples[i] = (float)((kick + snare + hat + bassTone + synth) * edge);
                peak = Math.Max(peak, Math.Abs(samples[i]));
            }
            // Leave headroom, independent of summing coincidences in the deterministic phrase.
            float gain = peak > .78f ? .78f / peak : 1f;
            for (int i = 0; i < frames; i++) samples[i] *= gain;
            var clip = AudioClip.Create("Zero Two Original Dance", frames, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static double Frequency(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

        static double Envelope(double t, double attack, double decay, double length)
        {
            if (t < 0 || t >= length) return 0;
            return Math.Min(1, t / attack) * Math.Exp(-t / decay)
                * Math.Min(1, (length - t) / .008);
        }
    }

    public sealed class ZeroTwoDanceAudio : IDisposable
    {
        readonly AudioSource source;
        readonly AudioClip clip;
        readonly bool ownsClip;
        bool wasSelected, wasAdvancing, disposed;

        public ZeroTwoDanceAudio(AudioSource source, AudioClip music = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            this.source = source;
            ownsClip = music == null;
            clip = ownsClip ? ZeroTwoMusic.CreateClip() : music;
            source.Stop();
            source.clip = clip;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = .25f;
            source.playOnAwake = false;
            source.pitch = 1f;
        }

        public void SetState(bool selected, bool advancing, float phaseSeconds)
        {
            if (disposed || source == null || clip == null) return;
            if (!selected)
            {
                if (wasSelected)
                {
                    source.Stop();
                    source.timeSamples = 0;
                }
                wasSelected = wasAdvancing = false;
                return;
            }

            int target = PhaseSample(phaseSeconds);
            if (!advancing)
            {
                if (wasAdvancing) source.Pause();
                if (!wasSelected) source.timeSamples = target;
                wasSelected = true;
                wasAdvancing = false;
                return;
            }

            int distance = Math.Abs(source.timeSamples - target);
            distance = Math.Min(distance, clip.samples - distance);
            // Circular comparison prevents a false correction when the loop crosses zero.
            if (!wasSelected)
            {
                source.Play();
                source.timeSamples = target;
            }
            else if (!wasAdvancing)
            {
                source.UnPause();
                // A newly selected, paused clip has no playback to unpause yet.
                if (!source.isPlaying) source.Play();
                // Seek after Play/UnPause so restarting cannot reset the requested phase.
                source.timeSamples = target;
            }
            else if (distance > clip.frequency * .08f) source.timeSamples = target;
            wasSelected = wasAdvancing = true;
        }

        int PhaseSample(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return 0;
            double duration = clip.samples / (double)clip.frequency;
            double phase = seconds % duration;
            if (phase < 0) phase += duration;
            return Math.Min(clip.samples - 1, (int)(phase * clip.frequency));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (source != null && source.clip == clip)
            {
                source.Stop();
                source.clip = null;
            }
            if (!ownsClip || clip == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(clip);
            else UnityEngine.Object.DestroyImmediate(clip);
        }
    }
}
