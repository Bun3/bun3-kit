using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.SoundEvents.Tests
{
    public sealed class SoundEventWorldTests
    {
        private sealed class Receiver : ISoundEventListener
        {
            internal readonly List<SoundEventPhase> Phases = new List<SoundEventPhase>();
            internal Action Callback;
            internal SoundEventSnapshot Last;
            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase)
            {
                Phases.Add(phase);
                Last = snapshot;
                Callback?.Invoke();
            }
        }

        private static SoundEventData Data(float radius = 2, ulong source = 1) =>
            new SoundEventData(1, 2, source, 3, Vector3.zero, 1, radius, 0, 0);

        [TestCase(3f, 1)]
        [TestCase(3.01f, 0)]
        public void PulseUsesInclusiveSphereContactAndDispatchesOnce(float distance, int count)
        {
            using var world = new SoundEventWorld(2, 2);
            var receiver = new Receiver();
            world.RegisterListener(new Vector3(distance, 0, 0), 1, receiver);
            Assert.That(world.TryEmitPulse(Data(), out var handle), Is.True);
            world.Tick(0);
            world.Tick(1);
            Assert.That(receiver.Phases.Count, Is.EqualTo(count));
            Assert.That(handle.IsValid, Is.False);
        }

        [Test]
        public void SustainedEntersUpdatesExitsReentersAndEndsOnce()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver();
            var listener = world.RegisterListener(Vector3.zero, 1, receiver);
            world.TryStart(Data(), 10, out var handle);
            world.Tick(0);
            world.Tick(1);
            listener.Update(new Vector3(4, 0, 0), 1);
            world.Tick(2);
            listener.Update(Vector3.zero, 1);
            world.Tick(3);
            world.Tick(10);
            Assert.That(handle.Stop(), Is.False);
            world.Tick(11);
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Update,
                SoundEventPhase.Exit, SoundEventPhase.Enter, SoundEventPhase.End }));
        }

        [Test]
        public void PriorExitDoesNotReceiveEndAndStaleHandleCannotStopReusedSlot()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver();
            var listener = world.RegisterListener(Vector3.zero, 0, receiver);
            world.TryStart(Data(), 10, out var old);
            world.Tick(0);
            listener.Update(new Vector3(10, 0, 0), 0);
            world.Tick(1);
            Assert.That(old.Stop(), Is.True);
            Assert.That(world.TryStart(Data(), 20, out var current), Is.True);
            Assert.That(old.Stop(), Is.False);
            Assert.That(old.Update(Data(), 30), Is.False);
            Assert.That(current.IsValid, Is.True);
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Exit }));
        }

        [Test]
        public void SourceAndListenerRemovalClearTheirRelationships()
        {
            using var world = new SoundEventWorld(2, 1);
            var first = new Receiver();
            var listener = world.RegisterListener(Vector3.zero, 0, first);
            world.TryStart(Data(), 10, out var handle);
            world.Tick(0);
            Assert.That(listener.Release(), Is.True);
            Assert.That(listener.Release(), Is.False);
            var second = new Receiver();
            world.RegisterListener(Vector3.zero, 0, second);
            Assert.That(listener.Update(Vector3.zero, 0), Is.False);
            world.Tick(1);
            Assert.That(world.RemoveSource(1), Is.EqualTo(1));
            Assert.That(handle.IsValid, Is.False);
            Assert.That(first.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Exit }));
            Assert.That(second.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.End }));
        }

        [Test]
        public void CapacityIsBoundedAndRecoveredAfterStop()
        {
            using var world = new SoundEventWorld(1, 1);
            world.RegisterListener(Vector3.zero, 0, new Receiver());
            Assert.Throws<InvalidOperationException>(() => world.RegisterListener(Vector3.zero, 0, new Receiver()));
            Assert.That(world.TryStart(Data(), 1, out var handle), Is.True);
            Assert.That(world.TryEmitPulse(Data(), out _), Is.False);
            handle.Stop();
            Assert.That(world.TryEmitPulse(Data(), out _), Is.True);
        }

        [Test]
        public void UpdateExtendsLifetimeAndChangesSnapshot()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver();
            world.RegisterListener(Vector3.zero, 0, receiver);
            world.TryStart(Data(), 1, out var handle);
            world.Tick(0);
            Assert.That(handle.Update(Data(), 3), Is.True);
            world.Tick(1);
            Assert.That(handle.IsValid, Is.True);
            world.Tick(3);
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Update, SoundEventPhase.End }));
        }

        [Test]
        public void InvalidNumbersAndBackwardClockAreRejected()
        {
            using var world = new SoundEventWorld(1, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => world.TryStart(Data(), double.NaN, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.TryEmitPulse(Data(-1), out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.RegisterListener(new Vector3(float.NaN, 0, 0), 0, new Receiver()));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Tick(double.PositiveInfinity));
            world.Tick(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Tick(1));
        }

        [Test]
        public void CallbacksCannotReenterOrMutateWorld()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver();
            var listener = world.RegisterListener(Vector3.zero, 0, receiver);
            world.TryStart(Data(), 10, out var handle);
            receiver.Callback = () =>
            {
                Assert.Throws<InvalidOperationException>(() => world.Tick(0));
                Assert.Throws<InvalidOperationException>(() => handle.Stop());
                Assert.Throws<InvalidOperationException>(() => listener.Release());
                Assert.Throws<InvalidOperationException>(() => world.Dispose());
            };
            world.Tick(0);
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter }));
            receiver.Callback = null;
        }

        private sealed class NoReach : ISoundEventReachPolicy
        {
            internal int Calls;
            public bool CanReach(in SoundEventSnapshot snapshot, Vector3 position, float radius) { Calls++; return false; }
        }

        [Test]
        public void CustomPolicyReplacesDefaultReach()
        {
            var policy = new NoReach();
            using var world = new SoundEventWorld(1, 1, policy);
            var receiver = new Receiver();
            world.RegisterListener(Vector3.zero, 0, receiver);
            world.TryEmitPulse(Data(), out _);
            world.Tick(0);
            Assert.That(receiver.Phases, Is.Empty);
            Assert.That(policy.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ListenerExceptionDoesNotPreventOtherDeliveryOrReplayPulse()
        {
            using var world = new SoundEventWorld(1, 2);
            var first = new Receiver { Callback = () => throw new InvalidOperationException("listener") };
            var second = new Receiver();
            world.RegisterListener(Vector3.zero, 0, first);
            world.RegisterListener(Vector3.zero, 0, second);
            world.TryEmitPulse(Data(), out var handle);
            Assert.Throws<InvalidOperationException>(() => world.Tick(0));
            Assert.That(handle.IsValid, Is.False);
            first.Callback = null;
            world.Tick(1);
            Assert.That(first.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse }));
            Assert.That(second.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse }));
        }

        [Test]
        public void DisposeEndsActiveEventsEvenWhenListenerThrows()
        {
            var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver();
            var listener = world.RegisterListener(Vector3.zero, 0, receiver);
            world.TryStart(Data(), 10, out var handle);
            world.Tick(0);
            receiver.Callback = () => throw new InvalidOperationException("listener");
            Assert.Throws<InvalidOperationException>(() => world.Dispose());
            Assert.That(handle.IsValid, Is.False);
            Assert.That(listener.IsValid, Is.False);
            Assert.That(handle.Stop(), Is.False);
            Assert.That(listener.Release(), Is.False);
            Assert.DoesNotThrow(() => world.Dispose());
            Assert.Throws<ObjectDisposedException>(() => world.Tick(1));
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.End }));
        }

        [Test]
        public void SnapshotPreservesPayloadAndUpdateMovesSource()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver();
            world.RegisterListener(Vector3.zero, 0, receiver);
            var data = new SoundEventData(10, 20, 30, 40, Vector3.zero, 5, 6, 70, 8);
            world.TryStart(data, 10, out var handle);
            world.Tick(0);
            Assert.That(receiver.Last.Data.SessionId, Is.EqualTo(10));
            Assert.That(receiver.Last.Data.EventId, Is.EqualTo(20));
            Assert.That(receiver.Last.Data.SourceId, Is.EqualTo(30));
            Assert.That(receiver.Last.Data.KindId, Is.EqualTo(40));
            Assert.That(receiver.Last.Data.Intensity, Is.EqualTo(5));
            Assert.That(receiver.Last.Data.Radius, Is.EqualTo(6));
            Assert.That(receiver.Last.Data.HostTick, Is.EqualTo(70));
            Assert.That(receiver.Last.Data.Revision, Is.EqualTo(8));
            Assert.That(receiver.Last.IsSustained, Is.True);
            Assert.That(receiver.Last.ExpiresAt, Is.EqualTo(10));
            handle.Update(new SoundEventData(10, 20, 30, 40, new Vector3(20, 0, 0), 5, 6, 70, 9), 20);
            world.Tick(1);
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Exit }));
            Assert.That(receiver.Last.Data.Revision, Is.EqualTo(9));
            Assert.That(receiver.Last.ExpiresAt, Is.EqualTo(20));
            Assert.Throws<ArgumentException>(() => handle.Update(Data(), 20));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void InvalidGeometryAndIntensityNeverConsumeCapacity(float value)
        {
            using var world = new SoundEventWorld(1, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => world.TryEmitPulse(Data(value), out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.TryEmitPulse(
                new SoundEventData(0, 0, 0, 0, Vector3.zero, value, 0, 0, 0), out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.RegisterListener(Vector3.zero, value, new Receiver()));
            Assert.That(world.TryEmitPulse(Data(), out _), Is.True);
            Assert.That(world.RegisterListener(Vector3.zero, 0, new Receiver()).IsValid, Is.True);
        }

        [Test]
        public void ZeroDurationExpiresBeforeFirstReachAndPendingPulseCanBeStopped()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Receiver();
            world.RegisterListener(Vector3.zero, 0, receiver);
            world.TryStart(Data(), 0, out var sustained);
            world.Tick(0);
            Assert.That(sustained.IsValid, Is.False);
            world.TryEmitPulse(Data(), out var pulse);
            Assert.That(pulse.Update(Data(), 1), Is.False);
            Assert.That(pulse.Stop(), Is.True);
            world.Tick(1);
            Assert.That(receiver.Phases, Is.Empty);
        }

        private sealed class Counter : ISoundEventListener
        {
            internal int Count;
            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase) { Count++; }
        }

        [Test]
        public void CapturedPulseUsesEmissionReachAndRejectsReplacementListener()
        {
            using var world = new SoundEventWorld(2, 2);
            var inside = new Receiver(); var outside = new Receiver();
            var a = world.RegisterListener(Vector3.zero, 0, inside);
            var b = world.RegisterListener(new Vector3(10, 0, 0), 0, outside);
            world.TryEmitPulse(Data(), out _, captureReach: true);
            a.Update(new Vector3(10, 0, 0), 0); b.Update(Vector3.zero, 0);
            world.Tick(0);
            Assert.That(inside.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse }));
            Assert.That(outside.Phases, Is.Empty);
            a.Update(Vector3.zero, 0);
            world.TryEmitPulse(Data(), out _, captureReach: true);
            a.Release();
            var replacement = new Receiver(); world.RegisterListener(Vector3.zero, 0, replacement);
            world.Tick(1);
            Assert.That(replacement.Phases, Is.Empty);
            Assert.That(inside.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse }), "Pending pulses emit no Exit or End on listener removal.");
        }

        [Test]
        public void FullListenerTryRegistrationDoesNotThrowOrAllocate()
        {
            using var world = new SoundEventWorld(1, 1);
            var receiver = new Counter();
            var registered = world.RegisterListener(Vector3.zero, 0, receiver);
            Assert.That(world.TryRegisterListener(Vector3.zero, 0, receiver, out _), Is.False);
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++) world.TryRegisterListener(Vector3.zero, 0, receiver, out _);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            registered.Release();
            Assert.That(world.TryRegisterListener(Vector3.zero, 0, receiver, out var next), Is.True);
            Assert.That(next.IsValid, Is.True);
        }

        private sealed class CaptureReach : ISoundEventReachPolicy
        {
            internal int Calls;
            internal bool ThrowSecond;
            internal SoundEventWorld World;
            public bool CanReach(in SoundEventSnapshot snapshot, Vector3 position, float radius)
            {
                Calls++;
                if (ThrowSecond && Calls == 2) throw new InvalidOperationException("capture");
                if (World != null)
                {
                    Assert.Throws<InvalidOperationException>(() => World.Tick(0));
                    Assert.Throws<InvalidOperationException>(() => World.TryEmitPulse(Data(), out _));
                    Assert.Throws<InvalidOperationException>(() => World.Dispose());
                }
                return true;
            }
        }

        [Test]
        public void FailedReachCaptureRollsBackPartialRecipientsAndCapacity()
        {
            var policy = new CaptureReach { ThrowSecond = true };
            using var world = new SoundEventWorld(1, 2, policy);
            var first = new Receiver(); var second = new Receiver();
            var listener = world.RegisterListener(Vector3.zero, 0, first);
            world.RegisterListener(Vector3.zero, 0, second);
            Assert.Throws<InvalidOperationException>(() => world.TryEmitPulse(Data(), out _, captureReach: true));
            world.Tick(0); listener.Release();
            Assert.That(first.Phases, Is.Empty);
            Assert.That(second.Phases, Is.Empty);
            Assert.That(world.TryEmitPulse(Data(), out _, captureReach: true), Is.True);
            world.Tick(1);
            Assert.That(second.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse }));
        }

        [Test]
        public void ReachCaptureRejectsReentrantWorldMutation()
        {
            var policy = new CaptureReach();
            using var world = new SoundEventWorld(1, 1, policy);
            policy.World = world;
            var receiver = new Receiver(); world.RegisterListener(Vector3.zero, 0, receiver);
            Assert.That(world.TryEmitPulse(Data(), out _, captureReach: true), Is.True);
            Assert.That(policy.Calls, Is.EqualTo(1));
            world.Tick(0);
            Assert.That(policy.Calls, Is.EqualTo(1), "Captured reach is not queried again during delivery.");
            Assert.That(receiver.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse }));
        }

        private sealed class ThrowingReach : ISoundEventReachPolicy
        {
            internal bool ThrowNext;
            public bool CanReach(in SoundEventSnapshot snapshot, Vector3 position, float radius)
            {
                if (ThrowNext) { ThrowNext = false; throw new InvalidOperationException("reach"); }
                return true;
            }
        }

        [Test]
        public void ReachPolicyExceptionPreservesRelationshipAndOtherDelivery()
        {
            var policy = new ThrowingReach();
            using var world = new SoundEventWorld(1, 2, policy);
            var first = new Receiver(); var second = new Receiver();
            world.RegisterListener(Vector3.zero, 0, first);
            world.RegisterListener(Vector3.zero, 0, second);
            world.TryStart(Data(), 10, out var handle);
            world.Tick(0);
            policy.ThrowNext = true;
            Assert.Throws<InvalidOperationException>(() => world.Tick(1));
            Assert.That(first.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter }));
            Assert.That(second.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Update }));
            world.Tick(2);
            handle.Stop();
            Assert.That(first.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Update, SoundEventPhase.End }));
            Assert.That(second.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.Update, SoundEventPhase.Update, SoundEventPhase.End }));
        }

        [Test]
        public void SteadyStateTicksDoNotAllocate()
        {
            using var world = new SoundEventWorld(4, 4);
            var receiver = new Counter();
            world.RegisterListener(Vector3.zero, 0, receiver);
            world.TryStart(Data(), 10000, out _);
            for (int i = 0; i < 100; i++) world.Tick(i);
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (int i = 100; i < 1100; i++) world.Tick(i);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That(receiver.Count, Is.EqualTo(1100));
        }
    }
}
