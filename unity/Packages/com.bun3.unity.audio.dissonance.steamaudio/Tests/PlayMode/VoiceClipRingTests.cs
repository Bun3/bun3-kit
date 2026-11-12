using System;
using NUnit.Framework;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio.Tests
{
    public sealed class VoiceClipRingTests
    {
        static RingWrite[] Plan(VoiceClipRing ring, int play)
        {
            var writes = new RingWrite[4];
            int count = ring.Plan(play, writes);
            return writes.AsSpan(0, count).ToArray();
        }

        static void AssertWrite(RingWrite write, int start, int count, bool silence)
        {
            Assert.That(write.Start, Is.EqualTo(start));
            Assert.That(write.Count, Is.EqualTo(count));
            Assert.That(write.Silence, Is.EqualTo(silence));
        }

        [Test]
        public void FirstPlanWritesLeadThenSilence()
        {
            var ring = new VoiceClipRing(48000, lengthSeconds: 1f);
            Assert.That(ring.LengthFrames, Is.EqualTo(48000));
            Assert.That(ring.BlockFrames, Is.EqualTo(480));
            var writes = Plan(ring, 0);
            Assert.That(writes, Has.Length.EqualTo(2));
            AssertWrite(writes[0], 0, 2880, false);
            AssertWrite(writes[1], 2880, 44640, true);
        }

        [Test]
        public void SteadyPlanWritesOnlyTheAdvance()
        {
            var ring = new VoiceClipRing(48000, lengthSeconds: 1f);
            Plan(ring, 0);
            var writes = Plan(ring, 480);
            Assert.That(ring.AdvancedFrames, Is.EqualTo(480));
            Assert.That(writes, Has.Length.EqualTo(2));
            AssertWrite(writes[0], 2880, 480, false);
            AssertWrite(writes[1], 47520, 480, true);
            Assert.That(ring.Underruns, Is.Zero);
        }

        [Test]
        public void OverrunResyncsWithoutRepeatingAudio()
        {
            var ring = new VoiceClipRing(48000, lengthSeconds: 1f);
            Plan(ring, 0);
            var writes = Plan(ring, 10000);
            Assert.That(ring.Underruns, Is.EqualTo(1));
            AssertWrite(writes[0], 10080, 2880, false);
        }

        [Test]
        public void DefaultRingOutlastsLongStalls()
        {
            Assert.That(new VoiceClipRing(48000).LengthFrames, Is.EqualTo(4 * 48000));
        }

        [Test]
        public void SpeechFramesPlayedExcludeUnderrunGaps()
        {
            var ring = new VoiceClipRing(48000, lengthSeconds: 1f);
            Plan(ring, 0);
            Plan(ring, 1440);
            Assert.That(ring.SpeechFramesPlayed, Is.EqualTo(1440));
            Plan(ring, 10000);
            Assert.That(ring.Underruns, Is.EqualTo(1));
            Plan(ring, 10560);
            Assert.That(ring.SpeechFramesPlayed, Is.EqualTo(4320 + 480),
                "Only the written speech and the speech after the resync point count as delivered.");
        }

        [Test]
        public void WrapAroundSplitsWrites()
        {
            var ring = new VoiceClipRing(48000, lengthSeconds: 1f);
            Plan(ring, 0);
            var writes = Plan(ring, 45600);
            AssertWrite(writes[0], 45600, 2400, false);
            AssertWrite(writes[1], 0, 480, false);
        }
    }
}
