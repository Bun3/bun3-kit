using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using NUnit.Framework;

namespace Bun3.Unity.SoundEvents.Tests
{
    public sealed class OutgoingSoundActivityStateTests
    {
        [Test]
        public void HeartbeatIsThrottledButStopIsImmediate()
        {
            var state = new OutgoingSoundActivityState();
            state.Reset(7);
            Assert.That(state.TryBuildReport(0, 1, true, true, out var start), Is.True);
            Assert.That(start.Sequence, Is.EqualTo(1));
            Assert.That(start.HadActivation, Is.True);
            Assert.That(state.TryBuildReport(.5, 1, true, false, out _), Is.False);
            Assert.That(state.TryBuildReport(.5, 1, false, false, out var stop), Is.True);
            Assert.That(stop.Sequence, Is.EqualTo(2));
            Assert.That(stop.Active, Is.False);
        }

        [Test]
        public void ShortActivationWaitsForHeartbeatAndSessionResetRestartsSequence()
        {
            var state = new OutgoingSoundActivityState();
            state.Reset(7);
            state.TryBuildReport(0, 1, true, false, out _);
            Assert.That(state.TryBuildReport(.5, 1, false, true, out var shortReport), Is.True,
                "Stopping sustained activity remains immediate and carries the observed activation.");
            Assert.That(shortReport.HadActivation, Is.True);
            state.Reset(8);
            Assert.That(state.TryBuildReport(0, 1, false, true, out var reset), Is.True);
            Assert.That(reset.SessionId, Is.EqualTo(8));
            Assert.That(reset.Sequence, Is.EqualTo(1));
        }

        [Test]
        public void IdleShortActivationRespectsExistingHeartbeatDeadline()
        {
            var state = new OutgoingSoundActivityState();
            state.Reset(7);
            state.TryBuildReport(0, 1, false, true, out _);
            Assert.That(state.TryBuildReport(.5, 1, false, true, out _), Is.False);
            Assert.That(state.HasPendingActivation, Is.True);
            Assert.That(state.TryBuildReport(1, 1, false, false, out var report), Is.True);
            Assert.That(report.HadActivation, Is.True);
            Assert.That(report.Sequence, Is.EqualTo(2));
        }

        [Test]
        public void IdlePollingDoesNotAllocate()
        {
            var state = new OutgoingSoundActivityState();
            state.Reset(7);
            state.TryBuildReport(0, 1, true, false, out _);
            for (int i = 0; i < 100; i++) state.TryBuildReport(.5, 1, true, false, out _);
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++) state.TryBuildReport(.5, 1, true, false, out _);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
