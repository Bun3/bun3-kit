using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using NUnit.Framework;

namespace Bun3.Unity.SoundEvents.Tests
{
    public class SoundActivityReportGateTests
    {
        [Test]
        public void RateLimitedSequencesCannotBeReplayedAfterTheWindow()
        {
            var gate = new SoundActivityReportGate(1);
            Assert.That(gate.TryAccept(0, 0), Is.True);
            Assert.That(gate.TryAccept(1, 0.1), Is.False);
            Assert.That(gate.TryAccept(1, 2), Is.False);
            Assert.That(gate.TryAccept(2, 2), Is.True);
            Assert.That(gate.TryAccept(1, 3), Is.False);
        }

        [Test]
        public void CleanupBypassesRateLimitWithoutReopeningTheWindow()
        {
            var gate = new SoundActivityReportGate(1);
            Assert.That(gate.TryAccept(4, 0), Is.True);
            Assert.That(gate.TryAccept(5, 0.1, true), Is.True);
            Assert.That(gate.TryAccept(6, 0.2), Is.False);
            Assert.That(gate.TryAccept(7, 1), Is.True);
            Assert.That(gate.TryAccept(7, 1, true), Is.False);
        }

        [Test]
        public void InvalidTimesAndIntervalsDoNotConsumeSequence()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoundActivityReportGate(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoundActivityReportGate(-1));
            var gate = new SoundActivityReportGate(0.1);
            Assert.Throws<ArgumentOutOfRangeException>(() => gate.TryAccept(1, double.PositiveInfinity));
            Assert.That(gate.TryAccept(1, 1), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => gate.TryAccept(2, 0.9));
            Assert.That(gate.TryAccept(2, 2), Is.True);
        }

        [Test]
        public void HighestSequenceDoesNotWrapAndWarmReportsDoNotAllocate()
        {
            var gate = new SoundActivityReportGate(0);
            gate.TryAccept(0, 0);
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (ulong i = 1; i < 1000; i++) gate.TryAccept(i, i);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That(gate.TryAccept(ulong.MaxValue, 1000), Is.True);
            Assert.That(gate.TryAccept(0, 1001), Is.False);
        }
    }
}
