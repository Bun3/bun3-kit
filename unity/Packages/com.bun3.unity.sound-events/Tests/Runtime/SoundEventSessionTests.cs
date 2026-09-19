using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.SoundEvents.Tests
{
    public sealed class SoundEventSessionTests
    {
        private sealed class LiveListener : ISoundEventSessionListener
        {
            internal readonly List<SoundEventPhase> Phases = new List<SoundEventPhase>();
            internal Vector3 Position;
            internal int Began;
            internal int Ended;
            public Vector3 SoundPosition => Position;
            public void BeginSoundTick() => Began++;
            public void EndSoundTick() => Ended++;
            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase) => Phases.Add(phase);
        }

        [Test]
        public void PulseCapturesRefreshedPositionAndTickHooksBracketDelivery()
        {
            using var session = new SoundEventSession(7, 1, 1);
            var listener = new LiveListener { Position = new Vector3(10, 0, 0) };
            session.Listen(listener.Position, 0, listener);
            listener.Position = Vector3.zero;
            Assert.That(session.EmitPulse(new SoundEventData(7, session.AllocateEventId(), 0, 1,
                Vector3.zero, 1, 1, 0, 0)), Is.True);
            listener.Position = new Vector3(10, 0, 0);
            session.Tick(0);
            Assert.That(listener.Phases, Is.EqualTo(new[] { SoundEventPhase.Pulse }));
            Assert.That(listener.Began, Is.EqualTo(1));
            Assert.That(listener.Ended, Is.EqualTo(1));
        }

        [Test]
        public void IdentitiesAreSessionLocalAndMutationsAreGuardedDuringCallbacks()
        {
            using var session = new SoundEventSession(7, 2, 1);
            Assert.That(session.AllocateEventId(), Is.EqualTo(1));
            Assert.That(session.AllocateEventId(), Is.EqualTo(2));
            var listener = new ReentrantListener(session);
            session.Listen(Vector3.zero, 0, listener);
            session.EmitPulse(new SoundEventData(7, session.AllocateEventId(), 0, 1, Vector3.zero, 1, 1, 0, 0));
            session.Tick(0);
            Assert.That(listener.Guarded, Is.True);
            Assert.Throws<ArgumentException>(() => session.EmitPulse(
                new SoundEventData(8, 1, 0, 1, Vector3.zero, 1, 1, 0, 0)));
        }

        private sealed class ReentrantListener : ISoundEventListener
        {
            private readonly SoundEventSession _session;
            internal bool Guarded;
            internal ReentrantListener(SoundEventSession session) { _session = session; }
            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase)
            {
                Guarded = Assert.Throws<InvalidOperationException>(() => _session.AllocateEventId()) != null;
            }
        }
    }
}
