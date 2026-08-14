#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    [TestFixture]
    public sealed class TagCatalogConformanceTests
    {
        private const string Json =
            "{\"schemaVersion\":1,\"tags\":[" +
            "{\"name\":\"State.Rooted\"}," +
            "{\"name\":\"Ability.Movement.Jump\"}," +
            "{\"name\":\"State.Dead.Ghost\"}]," +
            "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}";

        [Test]
        public void Runtime_indices_hierarchy_redirect_and_fingerprint_match_golden()
        {
            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(Json));
#pragma warning disable CS0618 // 레거시 JSON conformance fixture입니다.
            var catalog = TagCatalog.Load(stream);
#pragma warning restore CS0618

            Assert.That(catalog.Count, Is.EqualTo(7));
            Assert.That(catalog.TryGetByIndex(GameplayTag.None.Index, out var none), Is.True);
            Assert.That(none, Is.EqualTo(GameplayTag.None));
            Assert.That(none.Index, Is.Zero);
            Assert.That(catalog.GetRequiredByIndex(none.Index), Is.EqualTo(GameplayTag.None));
            Assert.That(catalog.GetRequired("ability").Index, Is.EqualTo(1));
            Assert.That(catalog.GetRequired("ability.movement.jump").Index, Is.EqualTo(3));
            Assert.That(catalog.GetRequired("state.dead").Index, Is.EqualTo(5));
            Assert.That(catalog.GetRequired("state.dead.ghost").Index, Is.EqualTo(6));
            Assert.That(catalog.GetRequired("state.rooted").Index, Is.EqualTo(7));
            Assert.That(catalog.GetParent(catalog.GetRequired("state.dead.ghost")),
                Is.EqualTo(catalog.GetRequired("state.dead")));
            Assert.That(catalog.GetSubtreeEnd(catalog.GetRequired("state")), Is.EqualTo(7));
            Assert.That(catalog.GetSubtreeEnd(catalog.GetRequired("state.dead")), Is.EqualTo(6));
            Assert.That(catalog.GetSubtreeEnd(catalog.GetRequired("state.dead.ghost")), Is.EqualTo(6));
            Assert.That(catalog.IsAncestorOrSelf(
                catalog.GetRequired("state.dead"),
                catalog.GetRequired("state.dead.ghost")), Is.True);
            Assert.That(catalog.IsAncestorOrSelf(
                catalog.GetRequired("state.dead"),
                catalog.GetRequired("state.rooted")), Is.False);
            Assert.That(catalog.GetRequired("STATE.KILLED"),
                Is.EqualTo(catalog.GetRequired("state.dead")));
            Assert.That(ToHex(catalog.Fingerprint),
                Is.EqualTo("f41c48acaf18fc8d239fd042554f07a67a46e8a4170b792bbc5aeee0fd344ce5"));
        }

        [Test]
        public void Containers_match_the_same_hierarchy_contract()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
#pragma warning disable CS0618 // 레거시 JSON conformance fixture입니다.
            var catalog = TagCatalog.Load(stream);
#pragma warning restore CS0618
            var ghost = catalog.GetRequired("State.Dead.Ghost");
            var dead = catalog.GetRequired("State.Dead");

            var tags = catalog.CreateContainer(1);
            tags.Add(ghost);
            var counts = catalog.CreateCountContainer(1);
            counts.Add(ghost, 2);

            Assert.That(tags.Has(dead), Is.True);
            Assert.That(tags.HasExact(dead), Is.False);
            Assert.That(counts.Count(dead), Is.EqualTo(2));
            Assert.That(counts.ExactCount(dead), Is.Zero);
        }

        [Test]
        public void Fingerprint_gate_rejects_mismatched_peer_before_simulation_starts()
        {
            using var localStream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
#pragma warning disable CS0618 // 레거시 JSON conformance fixture입니다.
            var local = TagCatalog.Load(localStream);
#pragma warning restore CS0618
            const string changedRedirectJson =
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Rooted\"}," +
                "{\"name\":\"Ability.Movement.Jump\"}," +
                "{\"name\":\"State.Dead.Ghost\"}]," +
                "\"redirects\":[{\"from\":\"State.Gone\",\"to\":\"State.Dead\"}]}";
            using var peerStream = new MemoryStream(Encoding.UTF8.GetBytes(changedRedirectJson));
#pragma warning disable CS0618 // 레거시 JSON conformance fixture입니다.
            var peer = TagCatalog.Load(peerStream);
#pragma warning restore CS0618
            var simulationStarts = 0;
            Action onSimulationStart = () => simulationStarts++;

            Assert.That(TryStartSimulation(local, peer.Fingerprint, onSimulationStart), Is.False);
            Assert.That(simulationStarts, Is.Zero);
            var matchingFingerprint = local.Fingerprint.ToArray();
            Assert.That(TryStartSimulation(local, matchingFingerprint, onSimulationStart), Is.True);
            Assert.That(simulationStarts, Is.EqualTo(1));
        }

        [Test]
        public void Ability_effect_and_equipment_contributions_survive_until_the_last_source_is_removed()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
#pragma warning disable CS0618 // 레거시 JSON conformance fixture입니다.
            var catalog = TagCatalog.Load(stream);
#pragma warning restore CS0618
            var state = catalog.GetRequired("State");
            var dead = catalog.GetRequired("State.Dead");
            var subject = catalog.CreateCountContainer(1);
            var abilityGranted = dead;
            var effectGranted = dead;
            var equipmentGranted = dead;

            subject.Add(abilityGranted);
            subject.Add(effectGranted);
            subject.Add(equipmentGranted);
            Assert.That(subject.ExactCount(dead), Is.EqualTo(3));
            Assert.That(subject.Count(state), Is.EqualTo(3));

            Assert.That(subject.Remove(abilityGranted), Is.EqualTo(1));
            Assert.That(subject.ExactCount(dead), Is.EqualTo(2));
            Assert.That(subject.Count(state), Is.EqualTo(2));
            Assert.That(subject.Has(state), Is.True);
            Assert.That(subject.Remove(effectGranted), Is.EqualTo(1));
            Assert.That(subject.ExactCount(dead), Is.EqualTo(1));
            Assert.That(subject.Count(state), Is.EqualTo(1));
            Assert.That(subject.Has(state), Is.True);
            Assert.That(subject.Remove(equipmentGranted), Is.EqualTo(1));
            Assert.That(subject.ExactCount(dead), Is.Zero);
            Assert.That(subject.Count(state), Is.Zero);
            Assert.That(subject.Has(state), Is.False);
        }

        private static bool TryStartSimulation(
            TagCatalog local,
            System.ReadOnlySpan<byte> peerFingerprint,
            Action onSimulationStart)
        {
            if (!local.MatchesFingerprint(peerFingerprint))
                return false;
            onSimulationStart();
            return true;
        }

        private static string ToHex(System.ReadOnlySpan<byte> bytes)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 15];
            }
            return new string(chars);
        }
    }
}
