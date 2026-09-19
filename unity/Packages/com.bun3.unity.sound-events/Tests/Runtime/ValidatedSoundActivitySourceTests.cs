using System.Collections.Generic;
using System;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.SoundEvents.Tests
{
    public sealed class ValidatedSoundActivitySourceTests
    {
        private static readonly SoundActivityData Data = new SoundActivityData(2, Vector3.zero, 1, 10, 9);

        private sealed class Listener : ISoundEventListener
        {
            internal readonly List<SoundEventPhase> Phases = new List<SoundEventPhase>();
            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase) => Phases.Add(phase);
        }

        private sealed class CallbackListener : ISoundEventListener
        {
            internal Action Callback;
            internal readonly List<SoundEventPhase> Phases = new List<SoundEventPhase>();
            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase)
            {
                Phases.Add(phase);
                Callback?.Invoke();
            }
        }

        [Test]
        public void IdentityMismatchDoesNotConsumeSequenceAndFreshStopBypassesThrottle()
        {
            using var session = new SoundEventSession(7, 4, 1);
            using var source = new ValidatedSoundActivitySource(session, 8, 1.25, .1);
            var listener = new Listener(); session.Listen(Vector3.zero, 0, listener);
            Assert.That(source.Report(9, 7, 100, true, false, 0, true, Data), Is.False);
            Assert.That(source.Report(8, 9, 100, true, false, 0, true, Data), Is.False);
            Assert.That(source.Report(8, 7, 1, true, false, 0, true, Data), Is.True);
            session.Tick(0);
            Assert.That(source.Report(8, 7, 2, false, false, .01, true, Data), Is.True);
            Assert.That(listener.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.End }));
        }

        [Test]
        public void ShortActivationPulsesOnceAndPolicyLossEndsSustainedActivity()
        {
            using var session = new SoundEventSession(7, 4, 1);
            using var source = new ValidatedSoundActivitySource(session, 8, 1.25, .1);
            var listener = new Listener(); session.Listen(Vector3.zero, 0, listener);
            Assert.That(source.Report(8, 7, 1, false, true, 0, true, Data), Is.True);
            session.Tick(0);
            Assert.That(source.Report(8, 7, 2, true, false, .2, true, Data), Is.True);
            session.Tick(.2);
            source.Tick(.21, false);
            Assert.That(listener.Phases, Is.EqualTo(new[]
                { SoundEventPhase.Pulse, SoundEventPhase.Enter, SoundEventPhase.End }));
        }

        [Test]
        public void DisposeRejectedDuringDeliveryCanBeRetriedAfterCallback()
        {
            using var session = new SoundEventSession(7, 2, 1);
            var source = new ValidatedSoundActivitySource(session, 8, 1.25, .1);
            var listener = new CallbackListener();
            session.Listen(Vector3.zero, 0, listener);
            Assert.That(source.Report(8, 7, 1, true, false, 0, true, Data), Is.True);
            listener.Callback = () => Assert.Throws<InvalidOperationException>(() => source.Dispose());
            session.Tick(0);
            listener.Callback = null;
            Assert.DoesNotThrow(() => source.Dispose());
            Assert.That(listener.Phases, Is.EqualTo(new[] { SoundEventPhase.Enter, SoundEventPhase.End }));
            Assert.That(source.Report(8, 7, 2, true, false, 1, true, Data), Is.False);
        }

        [Test]
        public void ReportRejectedDuringDeliveryDoesNotConsumeSequence()
        {
            using var session = new SoundEventSession(7, 2, 1);
            using var source = new ValidatedSoundActivitySource(session, 8, 1.25, .1);
            var listener = new CallbackListener();
            session.Listen(Vector3.zero, 0, listener);
            listener.Callback = () => Assert.Throws<InvalidOperationException>(() =>
                source.Report(8, 7, 1, true, false, 0, true, Data));
            session.EmitPulse(new SoundEventData(7, session.AllocateEventId(), 9, 1,
                Vector3.zero, 1, 10, 0, 0));
            session.Tick(0);
            listener.Callback = null;
            Assert.That(source.Report(8, 7, 1, true, false, 0, true, Data), Is.True);
            session.Tick(0);
            Assert.That(listener.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse, SoundEventPhase.Enter }));
        }
    }
}
