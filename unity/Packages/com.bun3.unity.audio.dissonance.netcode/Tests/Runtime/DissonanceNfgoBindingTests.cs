using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using System.Collections;
using System.Reflection;
using global::Dissonance;
using global::Dissonance.Integrations.Unity_NFGO;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.Netcode.Tests
{
    public sealed class DissonanceNfgoBindingTests
    {
        GameObject root;
        NetworkManager manager;
        DissonanceComms comms;
        NfgoCommsNetwork transport;
        static Type Find(string name)
        {
            var type = Type.GetType("Bun3.Unity.Audio.Dissonance.Netcode." + name + ", Bun3.Unity.Audio.Dissonance.Netcode");
            Assert.That(type, Is.Not.Null, "Explicit NGO binding and stable tracker ownership must exist.");
            return type;
        }
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("NGO voice binding fixture"); root.SetActive(false);
            manager = root.AddComponent<NetworkManager>();
            comms = root.AddComponent<DissonanceComms>();
            transport = root.AddComponent<NfgoCommsNetwork>();
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        [Test]
        public void SessionBindingDoesNotStartNetworkAndDuplicateBindingIsRejected()
        {
            var type = Find("DissonanceNfgoSessionScope");
            using var scope = (IDisposable)Activator.CreateInstance(type, manager, comms, transport);
            Assert.That(manager.IsListening, Is.False);
            Assert.That(type.GetProperty("IsReady").GetValue(scope), Is.False);
            var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type, manager, comms, transport));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            scope.Dispose();
            using var replacement = (IDisposable)Activator.CreateInstance(type, manager, comms, transport);
        }

        [Test]
        public void TransportMustBeOnTheSameObjectAsComms()
        {
            var type = Find("DissonanceNfgoSessionScope");
            var other = new GameObject("Other SDK transport"); other.SetActive(false);
            try
            {
                var otherTransport = other.AddComponent<NfgoCommsNetwork>();
                var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type, manager, comms, otherTransport));
                Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
            }
            finally { UnityEngine.Object.DestroyImmediate(other); }
        }

        [Test]
        public void RegistrationRejectsDuplicateIdentityAndDisposesExactOldId()
        {
            var type = Find("DissonancePlayerRegistrationScope");
            object[] Args(string id) => new object[] { comms, id, root.transform, NetworkPlayerType.Local };
            using var old = (IDisposable)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Args("first"), null);
            var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Args("first"), null));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            old.Dispose();
            using var next = (IDisposable)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Args("second"), null);
            var managerField = comms.GetType().GetField("_playerTrackers", BindingFlags.NonPublic | BindingFlags.Instance);
            var trackers = managerField.GetValue(comms);
            var unlinked = (IDictionary)trackers.GetType().GetField("_unlinkedPlayerTrackers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(trackers);
            Assert.That(unlinked.Contains("first"), Is.False);
            Assert.That(unlinked.Contains("second"), Is.True);
            old.Dispose();
            Assert.That(unlinked.Contains("second"), Is.True);
        }

        [Test]
        public void ReplacementTrackerUsesRealNetworkBehaviourAndOverridesDespawnAndDestroy()
        {
            var type = Find("DissonanceNfgoPlayer");
            Assert.That(typeof(NetworkBehaviour).IsAssignableFrom(type), Is.True);
            Assert.That(typeof(IDissonancePlayer).IsAssignableFrom(type), Is.True);
            Assert.That(type.GetMethod("OnNetworkDespawn").DeclaringType, Is.EqualTo(type));
            Assert.That(type.GetMethod("OnDestroy").DeclaringType, Is.EqualTo(type));
        }

        [Test]
        public void RepeatedDuplicateRegistrationChecksDoNotAllocate()
        {
            var type = Find("DissonancePlayerRegistrationScope");
            var method = type.GetMethod("CanRegister", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Duplicate identities need a nonthrowing path before allocating a registration.");
            var canRegister = (Func<DissonanceComms, string, bool>)Delegate.CreateDelegate(typeof(Func<DissonanceComms, string, bool>), method);
            using var registration = (IDisposable)Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { comms, "collision", root.transform, NetworkPlayerType.Local }, null);
            for (int i = 0; i < 8; i++) canRegister(comms, "collision");
            bool rejected = true;
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            TestDelegate warmLookup = () =>
            {
                for (int i = 0; i < 1000; i++) rejected &= !canRegister(comms, "collision");
            };
            warmLookup();
            Assert.That(warmLookup, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.That(rejected, Is.True);
        }

        [Test]
        public void InvalidAndOversizeIdentityCannotReachFixedStringConstruction()
        {
            var type = Find("DissonanceNfgoPlayer");
            var method = type.GetMethod("IsValidIdentity", BindingFlags.Static | BindingFlags.NonPublic);
            var valid = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), method);
            Assert.That(valid(null), Is.False);
            Assert.That(valid(" "), Is.False);
            Assert.That(valid("\uD800"), Is.False);
            Assert.That(valid(new string('a', 126)), Is.False);
            Assert.That(valid(new string('\uD55C', 42)), Is.False);
            Assert.That(valid(new string('a', 125)), Is.True);
            Assert.That(valid(new string('\uD55C', 41)), Is.True);
        }

        [Test]
        public void AtomicIdentityDoesNotMatchADifferentOwnerEvenBeforeReplicationCatchesUp()
        {
            var type = Find("DissonancePlayerIdentity");
            var identity = Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { 12UL, "owner-twelve" }, null);
            var match = type.GetMethod("IsForOwner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(match.Invoke(identity, new object[] { 12UL }), Is.True);
            Assert.That(match.Invoke(identity, new object[] { 13UL }), Is.False);
            var empty = Activator.CreateInstance(type);
            Assert.That(match.Invoke(empty, new object[] { 0UL }), Is.False);
        }
    }
}
