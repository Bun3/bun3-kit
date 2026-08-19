#nullable enable
using System;
using System.Linq;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    /// <summary>Verifies the tag catalog fingerprint contract.</summary>
    [TestFixture]
    public sealed class TagCatalogFingerprintTests
    {
        /// <summary>Verifies the BTAG big-endian canonical hash golden.</summary>
        [Test]
        public void Fingerprint_matches_BTAG_big_endian_golden()
        {
            var catalog = TagCatalogTestData.Load(
                """
                {
                  "schemaVersion": 1,
                  "tags": [
                    {"name":"State.Rooted"},
                    {"name":"Ability.Movement.Jump"},
                    {"name":"State.Dead.Ghost"}
                  ],
                  "redirects": [{"from":"State.Killed","to":"State.Dead"}]
                }
                """);

            Assert.That(ToHex(catalog.Fingerprint),
                Is.EqualTo("f41c48acaf18fc8d239fd042554f07a67a46e8a4170b792bbc5aeee0fd344ce5"));
        }

        /// <summary>Verifies representational differences do not change catalog identity.</summary>
        [Test]
        public void Formatting_order_comments_and_display_case_do_not_change_identity()
        {
            var left = TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"State.Dead","comment":"left"},{"name":"Ability.Jump"}]}""");
            var right = TagCatalogTestData.Load(
                """
                { "tags": [{"name":"ability.jump"},{"comment":"right","name":"state.dead"}],
                  "schemaVersion": 1, "redirects": [] }
                """);

            Assert.That(right.Fingerprint.ToArray(), Is.EqualTo(left.Fingerprint.ToArray()));
            Assert.That(right.GetRequired("STATE.DEAD").Index, Is.EqualTo(left.GetRequired("state.dead").Index));
        }

        /// <summary>Verifies path or redirect semantic changes change the fingerprint.</summary>
        [Test]
        public void Semantic_path_parent_or_redirect_change_changes_fingerprint()
        {
            var baseline = TagCatalogTestData.Load("""{"schemaVersion":1,"tags":[{"name":"State.Dead"}]}""");
            var renamed = TagCatalogTestData.Load("""{"schemaVersion":1,"tags":[{"name":"State.Gone"}]}""");
            var redirected = TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"State.Dead"}],"redirects":[{"from":"State.Killed","to":"State.Dead"}]}""");

            Assert.That(renamed.Fingerprint.ToArray(), Is.Not.EqualTo(baseline.Fingerprint.ToArray()));
            Assert.That(redirected.Fingerprint.ToArray(), Is.Not.EqualTo(baseline.Fingerprint.ToArray()));
            Assert.That(baseline.MatchesFingerprint(baseline.Fingerprint), Is.True);
            Assert.That(baseline.MatchesFingerprint(renamed.Fingerprint), Is.False);
        }

        /// <summary>Verifies implicit parents and redirect row order do not change identity.</summary>
        [Test]
        public void Implicit_parent_and_redirect_row_order_do_not_change_fingerprint()
        {
            var implicitParent = TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"State.Dead"}],"redirects":[{"from":"Old2","to":"State.Dead"},{"from":"Old1","to":"State"}]}""");
            var explicitParent = TagCatalogTestData.Load(
                """{"schemaVersion":1,"tags":[{"name":"State"},{"name":"State.Dead"}],"redirects":[{"from":"Old1","to":"State"},{"from":"Old2","to":"State.Dead"}]}""");

            Assert.That(explicitParent.Fingerprint.ToArray(), Is.EqualTo(implicitParent.Fingerprint.ToArray()));
        }

        /// <summary>Verifies the returned span's length and that internal bytes cannot be mutated.</summary>
        [Test]
        public void Fingerprint_is_fixed_length_and_cannot_be_mutated_by_callers()
        {
            var catalog = TagCatalogTestData.Load("""{"schemaVersion":1,"tags":[{"name":"A"}]}""");
            var fingerprint = catalog.Fingerprint.ToArray();
            fingerprint[0] ^= 0xFF;

            Assert.That(catalog.Fingerprint.Length, Is.EqualTo(32));
            Assert.That(catalog.MatchesFingerprint(fingerprint), Is.False);
        }

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0x0F];
            }

            return new string(chars);
        }
    }
}
