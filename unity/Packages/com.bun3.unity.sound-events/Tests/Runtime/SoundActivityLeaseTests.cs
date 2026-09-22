using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.SoundEvents.Tests
{
    public sealed class SoundActivityLeaseTests
    {
        private sealed class Receiver : ISoundEventListener
        {
            internal readonly List<SoundEventPhase> Phases = new List<SoundEventPhase>();
            internal SoundEventSnapshot Last;
            internal Action Callback;
            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase)
            { Phases.Add(phase); Last = snapshot; Callback?.Invoke(); }
        }
        private static SoundActivityData Data => new SoundActivityData(3, Vector3.zero, 2, 4, 5);

        [Test]
        public void BeginRenewAndStopPreserveIdentityAndHostPayload()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver(); world.RegisterListener(Vector3.zero, 0, receiver);
            using var lease = new SoundActivityLease(world, 7, 9, 2, () => 11);
            Assert.That(lease.Report(0, true, true, 0, Data), Is.EqualTo(SoundActivityReportResult.Started));
            world.Tick(0);
            Assert.That(receiver.Last.Data.EventId, Is.EqualTo(11));
            Assert.That(receiver.Last.Data.SessionId, Is.EqualTo(7));
            Assert.That(receiver.Last.Data.SourceId, Is.EqualTo(9));
            Assert.That(receiver.Last.Data.KindId, Is.EqualTo(3));
            Assert.That(receiver.Last.Data.Intensity, Is.EqualTo(2));
            Assert.That(receiver.Last.Data.Radius, Is.EqualTo(4));
            Assert.That(receiver.Last.Data.HostTick, Is.EqualTo(5));
            Assert.That(lease.Report(1, true, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Renewed));
            world.Tick(2);
            Assert.That(receiver.Last.ExpiresAt, Is.EqualTo(3));
            Assert.That(receiver.Last.Data.Revision, Is.EqualTo(1));
            Assert.That(lease.CurrentEventId, Is.EqualTo(11));
            Assert.That(lease.Report(2, false, true, 2, Data), Is.EqualTo(SoundActivityReportResult.Stopped));
            Assert.That(lease.IsActive, Is.False);
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Update, SoundEventPhase.End }));
        }

        [Test]
        public void ReorderedAndDuplicateReportsCannotRenewOrStop()
        {
            using var world = new SoundEventWorld(1, 1);
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => 3);
            lease.Report(10, true, true, 0, Data);
            Assert.That(lease.Report(10, false, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Stale));
            Assert.That(lease.Report(9, true, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Stale));
            Assert.That(lease.IsActive, Is.True);
            Assert.That(lease.Tick(2), Is.True);
            Assert.That(lease.IsActive, Is.False);
            Assert.That(lease.LastSequence, Is.EqualTo(10));
        }

        [Test]
        public void PolicyLossAndDeniedReportEndImmediatelyAndConsumeSequence()
        {
            using var world = new SoundEventWorld(1, 1);
            ulong id = 0;
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => ++id);
            lease.Report(1, true, true, 0, Data);
            Assert.That(lease.Report(0, true, false, 0.5, Data), Is.EqualTo(SoundActivityReportResult.Denied));
            Assert.That(lease.IsActive, Is.False);
            Assert.That(lease.Report(1, true, true, 0.5, Data), Is.EqualTo(SoundActivityReportResult.Stale));
            Assert.That(lease.Report(2, true, false, 1, Data), Is.EqualTo(SoundActivityReportResult.Denied));
            Assert.That(lease.Report(2, true, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Stale));
            Assert.That(lease.Report(3, true, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Started));
            Assert.That(lease.CurrentEventId, Is.EqualTo(2));
            Assert.That(lease.Tick(1.5, false), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExpiryRequiresFreshIdentityWhetherWorldOrLeaseTicksFirst(bool worldFirst)
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver(); world.RegisterListener(Vector3.zero, 0, receiver);
            ulong id = 0;
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => ++id);
            lease.Report(1, true, true, 0, Data); world.Tick(0);
            if (worldFirst) world.Tick(2);
            Assert.That(lease.Report(2, true, true, 2, Data), Is.EqualTo(SoundActivityReportResult.Started));
            Assert.That(lease.CurrentEventId, Is.EqualTo(2));
            world.Tick(2);
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.End, SoundEventPhase.Enter }));
        }

        [Test]
        public void SourceRemovalAndReusedSlotCannotBeAffectedByOldLease()
        {
            using var world = new SoundEventWorld(1, 1);
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => 3);
            lease.Report(1, true, true, 0, Data);
            world.RemoveSource(2);
            world.TryStart(new SoundEventData(1, 4, 5, 0, Vector3.zero, 0, 0, 0, 0), 10, out var other);
            lease.Dispose();
            Assert.That(other.IsValid, Is.True);
            Assert.Throws<ObjectDisposedException>(() => lease.Report(2, true, true, 1, Data));
        }

        [Test]
        public void CapacityFailureConsumesSequenceAndOnlyFreshReportRetries()
        {
            using var world = new SoundEventWorld(1, 1);
            world.TryStart(new SoundEventData(), 10, out var blocker);
            ulong id = 0;
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => ++id);
            Assert.That(lease.Report(1, true, true, 0, Data), Is.EqualTo(SoundActivityReportResult.CapacityUnavailable));
            blocker.Stop();
            lease.Tick(1);
            Assert.That(lease.IsActive, Is.False);
            Assert.That(lease.Report(1, true, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Stale));
            Assert.That(lease.Report(2, true, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Started));
            Assert.That(id, Is.EqualTo(2));
        }

        [Test]
        public void InvalidInputDoesNotConsumeSequenceAndClockCannotMoveBackward()
        {
            using var world = new SoundEventWorld(1, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoundActivityLease(world, 1, 2, 0, () => 3));
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => lease.Report(1, true, true, double.NaN, Data));
            Assert.Throws<ArgumentOutOfRangeException>(() => lease.Report(1, true, true, 0,
                new SoundActivityData(0, Vector3.zero, -1, 1, 0)));
            Assert.That(lease.HasAcceptedReport, Is.False);
            lease.Report(1, true, true, 1, Data);
            Assert.Throws<ArgumentOutOfRangeException>(() => lease.Tick(0));
            world.Tick(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => lease.Report(2, true, true, 1, Data));
            Assert.That(lease.LastSequence, Is.EqualTo(1));
        }

        [Test]
        public void HeartbeatsDoNotAllocateOrCallEventIdAllocator()
        {
            using var world = new SoundEventWorld(1, 1);
            int allocations = 0;
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => { allocations++; return 3; });
            lease.Report(0, true, true, 0, Data);
            for (ulong i = 1; i < 100; i++) lease.Report(i, true, true, i * 0.01, Data);
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (ulong i = 100; i < 1100; i++) lease.Report(i, true, true, i * 0.01, Data);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That(allocations, Is.EqualTo(1));
        }

        [Test]
        public void DenialAndStopNeedNoRepresentableFutureExpiry()
        {
            using var world = new SoundEventWorld(1, 1);
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => 3);
            lease.Report(1, true, true, 0, Data);
            Assert.That(lease.Report(1, true, false, double.MaxValue, Data), Is.EqualTo(SoundActivityReportResult.Denied));
            Assert.That(lease.IsActive, Is.False);
            Assert.That(lease.Report(2, false, true, double.MaxValue, Data), Is.EqualTo(SoundActivityReportResult.Stopped));
            Assert.Throws<ArgumentOutOfRangeException>(() => lease.Report(3, true, true, double.MaxValue, Data));
            Assert.That(lease.LastSequence, Is.EqualTo(2));
        }

        [Test]
        public void CallbackCannotMutateLeaseAndExceptionDoesNotLeakItsEvent()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver(); world.RegisterListener(Vector3.zero, 0, receiver);
            ulong id = 0;
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => ++id);
            lease.Report(1, true, true, 0, Data); world.Tick(0);
            receiver.Callback = () =>
            {
                Assert.Throws<InvalidOperationException>(() => lease.Report(3, true, true, 1, Data));
                Assert.Throws<InvalidOperationException>(() => lease.Dispose());
                throw new InvalidOperationException("receiver");
            };
            Assert.Throws<InvalidOperationException>(() => lease.Report(2, false, true, 1, Data));
            Assert.That(lease.LastSequence, Is.EqualTo(2));
            Assert.That(lease.IsActive, Is.False);
            receiver.Callback = null;
            Assert.That(lease.Report(3, true, true, 1, Data), Is.EqualTo(SoundActivityReportResult.Started));
        }

        [Test]
        public void DisposalEndsActivityOnceAndWorksAfterWorldDisposal()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver(); world.RegisterListener(Vector3.zero, 0, receiver);
            using var lease = new SoundActivityLease(world, 1, 2, 2, () => 3);
            lease.Report(ulong.MaxValue, true, true, 0, Data); world.Tick(0);
            Assert.That(lease.Report(0, true, true, 0, Data), Is.EqualTo(SoundActivityReportResult.Stale));
            world.Dispose();
            Assert.DoesNotThrow(() => lease.Dispose());
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.End }));
            Assert.Throws<ObjectDisposedException>(() => lease.Tick(1));
        }
    }
}
