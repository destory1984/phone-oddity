using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oddity.RoomTest.Tests
{
    public class ZeroTwoDanceMusicTests
    {
        static float[] ReadPcm(AudioClip clip)
        {
            var pcm = new float[clip.samples * clip.channels];
            Assert.IsTrue(clip.GetData(pcm, 0), "Read actual generated clip PCM");
            return pcm;
        }

        [Test]
        public void ClipHasExpectedDurationAndFormat()
        {
            var clip = ZeroTwoMusic.CreateClip();
            try
            {
                Assert.AreEqual(44100, clip.frequency);
                Assert.AreEqual(1, clip.channels);
                Assert.That(Math.Abs(clip.samples / (double)clip.frequency - ZeroTwoMusic.Duration),
                    Is.LessThanOrEqualTo(1.0 / clip.frequency));
                // Unity's length property is millisecond-quantized; PCM above remains sample-accurate.
                Assert.That(Math.Abs(clip.length - ZeroTwoMusic.Duration),
                    Is.LessThanOrEqualTo(.001f));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Test]
        public void ActualPcmIsFiniteAudibleAndBelowPeakLimit()
        {
            var clip = ZeroTwoMusic.CreateClip();
            try
            {
                var pcm = ReadPcm(clip);
                double energy = 0;
                float peak = 0, maxJump = 0;
                for (int i = 0; i < pcm.Length; i++)
                {
                    if (float.IsNaN(pcm[i]) || float.IsInfinity(pcm[i]))
                        Assert.Fail("Nonfinite PCM sample at " + i);
                    energy += (double)pcm[i] * pcm[i];
                    peak = Math.Max(peak, Math.Abs(pcm[i]));
                    if (i > 0) maxJump = Math.Max(maxJump, Math.Abs(pcm[i] - pcm[i - 1]));
                }
                double rms = Math.Sqrt(energy / pcm.Length);
                TestContext.WriteLine("frames={0}, seconds={1:F9}, RMS={2:F6}, peak={3:F6}, maxJump={4:F6}",
                    clip.samples, clip.samples / (double)clip.frequency, rms, peak, maxJump);
                Assert.That(rms, Is.GreaterThan(.01), "Audible signal, not silence or numerical noise");
                Assert.That(peak, Is.LessThanOrEqualTo(.8f));
                Assert.That(Math.Abs(pcm[0]), Is.LessThanOrEqualTo(1e-5f));
                Assert.That(Math.Abs(pcm[pcm.Length - 1]), Is.LessThanOrEqualTo(1e-5f));
                Assert.That(maxJump, Is.LessThan(.35f));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Test]
        public void RepeatedGenerationProducesIdenticalActualPcm()
        {
            AudioClip first = null, second = null;
            try
            {
                first = ZeroTwoMusic.CreateClip();
                second = ZeroTwoMusic.CreateClip();
                CollectionAssert.AreEqual(ReadPcm(first), ReadPcm(second));
            }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void DisposeTwiceDestroysOwnedClipAndClearsSource()
        {
            var owner = new GameObject("ZeroTwoDanceMusicTests");
            ZeroTwoDanceAudio audio = null;
            try
            {
                var source = owner.AddComponent<AudioSource>();
                audio = new ZeroTwoDanceAudio(source);
                var owned = source.clip;
                Assert.IsNotNull(owned);
                Assert.IsTrue(source.loop);
                Assert.IsFalse(source.playOnAwake);
                Assert.AreEqual(0f, source.spatialBlend);
                Assert.That(source.volume, Is.InRange(0f, .25f));
                audio.Dispose();
                Assert.IsTrue(owned == null, "Owned native clip destroyed in EditMode");
                Assert.IsTrue(source.clip == null);
                Assert.DoesNotThrow(() => audio.Dispose());
                Assert.DoesNotThrow(() => audio.SetState(true, true, 1f));
            }
            finally
            {
                audio?.Dispose();
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void DisposePreservesReplacementClipAndDestroysOnlyOwnedClip()
        {
            var owner = new GameObject("ZeroTwoDanceMusicTestsReplacement");
            var replacement = AudioClip.Create("Unowned replacement", 441, 1, 44100, false);
            ZeroTwoDanceAudio audio = null;
            try
            {
                var source = owner.AddComponent<AudioSource>();
                audio = new ZeroTwoDanceAudio(source);
                var owned = source.clip;
                source.clip = replacement;
                audio.Dispose();
                audio.Dispose();
                Assert.IsTrue(owned == null);
                Assert.IsTrue(replacement != null, "Replacement remains alive");
                Assert.AreSame(replacement, source.clip, "Replacement remains attached");
            }
            finally
            {
                audio?.Dispose();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void DisposeAfterSourceDestroyedStillDestroysOwnedClip()
        {
            var owner = new GameObject("ZeroTwoDanceMusicTestsDestroyedSource");
            var source = owner.AddComponent<AudioSource>();
            var audio = new ZeroTwoDanceAudio(source);
            var owned = source.clip;
            try
            {
                Object.DestroyImmediate(owner);
                Assert.DoesNotThrow(() => audio.Dispose());
                Assert.IsTrue(owned == null);
                Assert.DoesNotThrow(() => audio.Dispose());
            }
            finally
            {
                audio.Dispose();
                if (owner != null) Object.DestroyImmediate(owner);
            }
        }

        static int SeekTarget(ZeroTwoDanceAudio audio, float seconds)
        {
            // Inspect the target without depending on an audio device in an EditMode test.
            var method = typeof(ZeroTwoDanceAudio).GetMethod("PhaseSample", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (int)method.Invoke(audio, new object[] { seconds });
        }

        [TestCase(10)]
        [TestCase(20)]
        public void InjectedTrackUsesItsFullDurationAndIsNotDestroyed(int seconds)
        {
            var owner = new GameObject("ZeroTwoDanceMusicTestsInjected");
            var track = AudioClip.Create("Injected long track", seconds * 44100, 1, 44100, false);
            ZeroTwoDanceAudio audio = null;
            try
            {
                var source = owner.AddComponent<AudioSource>();
                audio = new ZeroTwoDanceAudio(source, track);
                Assert.AreSame(track, source.clip);
                Assert.AreEqual(5 * track.frequency, SeekTarget(audio, 5f), "Five seconds must not wrap at the dance period");
                Assert.AreEqual(5 * track.frequency, SeekTarget(audio, seconds + 5f), "Wrap at the actual track end");
                Assert.AreEqual((seconds - 2) * track.frequency, SeekTarget(audio, -2f));
                audio.SetState(true, false, 5f);
                Assert.AreEqual(5 * track.frequency, source.timeSamples, "Paused selection seeks to the requested full-track phase");
                audio.SetState(false, false, 0f);
                Assert.AreEqual(0, source.timeSamples, "Leaving the dance rewinds the song");
                audio.Dispose();
                audio.Dispose();
                Assert.IsTrue(source.clip == null);
                Assert.IsTrue(track != null, "Injected clip is not owned by the helper");
            }
            finally
            {
                audio?.Dispose();
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(track);
            }
        }

        [Test]
        public void InstalledImportedTrackIsLongAndTargetsBeyondDanceLoop()
        {
            var track = Resources.Load<AudioClip>("Sfx/ZeroTwoTrack");
            if (track == null) Assert.Ignore("Optional downloaded ZeroTwoTrack is not installed");
            var owner = new GameObject("ZeroTwoDanceMusicTestsImported");
            ZeroTwoDanceAudio audio = null;
            try
            {
                var source = owner.AddComponent<AudioSource>();
                audio = new ZeroTwoDanceAudio(source, track);
                Assert.AreSame(track, source.clip);
                Assert.That(track.samples / (double)track.frequency, Is.GreaterThan(70));
                int target = SeekTarget(audio, 10f);
                Assert.AreEqual(10 * track.frequency, target);
                Assert.That(target / (double)track.frequency, Is.GreaterThan(ZeroTwoMusic.Duration));
                TestContext.WriteLine("Imported track={0}, frames={1}, seconds={2:F6}, targetSeconds={3:F6}",
                    track.name, track.samples, track.samples / (double)track.frequency, target / (double)track.frequency);
                audio.Dispose();
                Assert.IsTrue(track != null, "Imported asset must survive helper disposal");
                Assert.IsTrue(source.clip == null);
            }
            finally
            {
                audio?.Dispose();
                Object.DestroyImmediate(owner);
            }
        }

        [Test, Explicit("Writes actual generated PCM to ignored shots/room_test/zero_two/beat.wav for listening")]
        public void ExportActualClipPcmToReviewWav()
        {
            var clip = ZeroTwoMusic.CreateClip();
            try
            {
                var pcm = ReadPcm(clip);
                // Application.dataPath is unity/Assets; no machine-specific path is stored.
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string directory = Path.Combine(root, "shots", "room_test", "zero_two");
                Directory.CreateDirectory(directory);
                using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "beat.wav"))))
                {
                    int bytes = pcm.Length * 2;
                    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
                    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)clip.channels);
                    writer.Write(clip.frequency); writer.Write(clip.frequency * clip.channels * 2);
                    writer.Write((short)(clip.channels * 2)); writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                    foreach (float sample in pcm)
                        writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, sample)) * 32767));
                }
            }
            finally { Object.DestroyImmediate(clip); }
        }
    }
}
