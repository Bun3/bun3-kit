using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using global::Dissonance;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.Tests
{
    public class DissonanceRoomScopeTests
    {
        private GameObject _object;
        private DissonanceComms _comms;

        private sealed class Scope : IDisposable
        {
            private readonly object _instance;
            private readonly Type _type;

            internal Scope(DissonanceComms comms)
            {
                _type = Type.GetType("Bun3.Unity.Audio.Dissonance.DissonanceRoomScope, Bun3.Unity.Audio.Dissonance");
                Assert.That(_type, Is.Not.Null, "The room scope must own only its membership and channel.");
                _instance = Activator.CreateInstance(_type, new object[] { comms });
            }

            internal void SetRoom(string room, bool transmit, bool positional = false)
                => Invoke("SetRoom", new object[] { room, transmit, positional });
            internal void Clear() => Invoke("Clear", null);
            public void Dispose() => ((IDisposable)_instance).Dispose();

            private void Invoke(string method, object[] arguments)
            {
                try { _type.GetMethod(method).Invoke(_instance, arguments); }
                catch (TargetInvocationException error) when (error.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("Room ownership test");
            _object.SetActive(false);
            _comms = _object.AddComponent<DissonanceComms>();
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_object);

        [Test]
        public void NewScopeDoesNotChooseARoomOrTransmit()
        {
            using var scope = new Scope(_comms);
            Assert.That(_comms.Rooms.Count, Is.Zero);
            Assert.That(_comms.RoomChannels.Count, Is.Zero);
        }

        [Test]
        public void ClearPreservesExternalMembershipAndDuplicateChannels()
        {
            using var scope = new Scope(_comms);
            var membership = _comms.Rooms.Join("scope-external");
            var externalA = _comms.RoomChannels.Open("scope-external");
            var externalB = _comms.RoomChannels.Open("scope-external");
            scope.SetRoom("scope-external", true);
            Assert.That(_comms.RoomChannels.Count, Is.EqualTo(3));
            scope.Clear();
            Assert.That(_comms.RoomChannels.Count, Is.EqualTo(2));
            Assert.That(externalA.IsOpen && externalB.IsOpen, Is.True);
            Assert.That(_comms.Rooms.Contains("scope-external"), Is.True);
            _comms.Rooms.Leave(membership);
            Assert.That(_comms.Rooms.Contains("scope-external"), Is.False);
            externalA.Dispose(); externalB.Dispose();
        }

        [Test]
        public void SameRoomDoesNotDuplicateMembershipAndTransmitCanToggle()
        {
            using var scope = new Scope(_comms);
            int joined = 0, opened = 0;
            _comms.Rooms.JoinedRoom += _ => joined++;
            _comms.RoomChannels.OpenedChannel += (_, __) => opened++;
            scope.SetRoom("scope-repeat", false);
            scope.SetRoom("scope-repeat", false);
            Assert.That(_comms.RoomChannels.Count, Is.Zero);
            scope.SetRoom("scope-repeat", true);
            scope.SetRoom("scope-repeat", true);
            Assert.That(_comms.RoomChannels.Count, Is.EqualTo(1));
            Assert.That(opened, Is.EqualTo(1));
            scope.SetRoom("scope-repeat", false);
            Assert.That(_comms.Rooms.Contains("scope-repeat"), Is.True);
            Assert.That(_comms.RoomChannels.Count, Is.Zero);
            scope.SetRoom("scope-repeat", true);
            Assert.That(opened, Is.EqualTo(2));
            Assert.That(joined, Is.EqualTo(1));
            scope.Clear();
            Assert.That(_comms.Rooms.Count, Is.Zero);
        }

        [Test]
        public void SwitchingRoomsReleasesOldOutputBeforeJoiningNewRoom()
        {
            using var scope = new Scope(_comms);
            scope.SetRoom("scope-old", true);
            bool oldReleasedAtJoin = false;
            _comms.Rooms.JoinedRoom += room =>
            {
                if (room == "scope-new")
                    oldReleasedAtJoin = !_comms.Rooms.Contains("scope-old") && _comms.RoomChannels.Count == 0;
            };
            scope.SetRoom("scope-new", true);
            Assert.That(oldReleasedAtJoin, Is.True);
            Assert.That(_comms.Rooms.Contains("scope-new"), Is.True);
            Assert.That(_comms.RoomChannels.Count, Is.EqualTo(1));
        }

        [Test]
        public void SameRoomPositionalChangeAffectsOnlyOwnedChannel()
        {
            using var scope = new Scope(_comms);
            var external = _comms.RoomChannels.Open("scope-position", positional: false);
            scope.SetRoom("scope-position", true, false);
            scope.SetRoom("scope-position", true, true);
            int positionalCount = 0;
            foreach (var entry in _comms.RoomChannels)
                if (entry.Value.Positional) positionalCount++;
            Assert.That(positionalCount, Is.EqualTo(1));
            Assert.That(external.Positional, Is.False);
            external.Dispose();
        }

        [Test]
        public void DisposeReleasesOnceAndRejectsFurtherMutation()
        {
            using var scope = new Scope(_comms);
            scope.SetRoom("scope-dispose", true);
            scope.Dispose(); scope.Dispose();
            Assert.That(_comms.Rooms.Count, Is.Zero);
            Assert.That(_comms.RoomChannels.Count, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => scope.SetRoom("scope-after", true));
            Assert.Throws<ObjectDisposedException>(() => scope.Clear());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  \t")]
        public void InvalidNameDoesNotReleaseCurrentRoom(string name)
        {
            using var scope = new Scope(_comms);
            scope.SetRoom("scope-valid", true);
            Assert.That(() => scope.SetRoom(name, true), Throws.InstanceOf<ArgumentException>());
            Assert.That(_comms.Rooms.Contains("scope-valid"), Is.True);
            Assert.That(_comms.RoomChannels.Count, Is.EqualTo(1));
        }
    }
}
