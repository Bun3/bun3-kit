#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagCatalogEditSessionTests
    {
        [Test]
        public void Add_and_comment_serialize_in_case_insensitive_path_order()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[]}");

            session.Add("State.Dead", "사망");
            session.Add("ability.Jump", "점프");
            session.SetComment("STATE.DEAD", "전투 불능");

            var json = session.Serialize();
            Assert.That(json, Does.Contain("\"name\": \"ability.Jump\""));
            Assert.That(json, Does.Contain("\"comment\": \"전투 불능\""));
            Assert.That(json.IndexOf("ability.Jump", StringComparison.Ordinal),
                Is.LessThan(json.IndexOf("State.Dead", StringComparison.Ordinal)));
            Assert.That(json.EndsWith("\n", StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void Commenting_an_implicit_parent_promotes_only_its_authoring_row()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");

            session.SetComment("State", "상태 루트");

            var json = session.Serialize();
            Assert.That(json, Does.Contain("\"name\": \"State\""));
            Assert.That(json, Does.Contain("\"comment\": \"상태 루트\""));
        }

        [Test]
        public void Commenting_an_implicit_parent_uses_catalog_display_name_when_caller_casing_differs()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Dead\"},{\"name\":\"State.Dead.Ghost\"}]}");

            session.SetComment("STATE", "root");

            var json = session.Serialize();
            Assert.That(json, Does.Contain("\"name\": \"State\""));
            Assert.That(json, Does.Not.Contain("\"name\": \"STATE\""));

            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(json));
            var catalog = TagCatalog.Load(stream);
            Assert.That(catalog.GetDisplayName(catalog.GetRequired("State")), Is.EqualTo("State"));
            Assert.That(catalog.GetDisplayName(catalog.GetRequired("State.Dead")), Is.EqualTo("State.Dead"));
            Assert.That(catalog.GetDisplayName(catalog.GetRequired("State.Dead.Ghost")),
                Is.EqualTo("State.Dead.Ghost"));
        }

        [Test]
        public void Relocate_subtree_creates_direct_redirects_and_rewrites_old_targets()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Dead\"},{\"name\":\"State.Dead.Ghost\"}," +
                "{\"name\":\"State.Dead.Ghost.Spirit\"}]," +
                "\"redirects\":[{\"from\":\"Legacy.Dead\",\"to\":\"State.Dead\"}]}");

            session.RelocateSubtree("State.Dead", "Condition.Deceased");
            var json = session.Serialize();

            Assert.That(json, Does.Contain("Condition.Deceased.Ghost.Spirit"));
            Assert.That(json, Does.Contain("\"from\": \"State.Dead\""));
            Assert.That(json, Does.Contain("\"from\": \"State.Dead.Ghost\""));
            Assert.That(json, Does.Contain("\"from\": \"State.Dead.Ghost.Spirit\""));
            Assert.That(json, Does.Contain("\"from\": \"Legacy.Dead\""));
            Assert.That(json, Does.Not.Contain("\"to\": \"State.Dead\""));
            Assert.That(session.Redirects.Count, Is.EqualTo(4));
            Assert.That(GetRedirectTarget(session, "State.Dead"), Is.EqualTo("Condition.Deceased"));
            Assert.That(GetRedirectTarget(session, "State.Dead.Ghost"),
                Is.EqualTo("Condition.Deceased.Ghost"));
            Assert.That(GetRedirectTarget(session, "State.Dead.Ghost.Spirit"),
                Is.EqualTo("Condition.Deceased.Ghost.Spirit"));
            Assert.That(GetRedirectTarget(session, "Legacy.Dead"), Is.EqualTo("Condition.Deceased"));
        }

        [Test]
        public void Collision_keeps_the_previous_document_byte_for_byte()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"},{\"name\":\"State.Alive\"}]}");
            var before = session.Serialize();

            Assert.Throws<InvalidOperationException>(
                () => session.RelocateSubtree("State.Dead", "State.Alive"));
            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        [Test]
        public void Case_only_relocate_changes_display_case_without_a_redirect()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");

            session.RelocateSubtree("State.Dead", "state.dead");
            var json = session.Serialize();

            Assert.That(json, Does.Contain("\"name\": \"state.dead\""));
            Assert.That(json, Does.Not.Contain("\"from\""));
        }

        [Test]
        public void Delete_requires_subtree_authorization_and_removes_dangling_redirects()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead.Ghost\"}]," +
                "\"redirects\":[{\"from\":\"Old.Ghost\",\"to\":\"State.Dead.Ghost\"}]}");

            Assert.Throws<InvalidOperationException>(() => session.Delete("State.Dead", false));
            session.Delete("State.Dead", true);
            var json = session.Serialize();
            Assert.That(json, Does.Not.Contain("State.Dead"));
            Assert.That(json, Does.Not.Contain("Old.Ghost"));
        }

        [Test]
        [Timeout(600_000)]
        public void Relocate_subtree_at_maximum_catalog_count_includes_the_last_active_path()
        {
            var session = GameplayTagCatalogEditSession.Open(CreateMaximumCatalogJson());

            Assert.DoesNotThrow(() => session.RelocateSubtree("State", "Condition"));

            Assert.That(session.Redirects.Count, Is.EqualTo(65_535));
            Assert.That(GetRedirectTarget(session, "State"), Is.EqualTo("Condition"));
            Assert.That(GetRedirectTarget(session, "State.X65533"), Is.EqualTo("Condition.X65533"));
        }

        private static string GetRedirectTarget(GameplayTagCatalogEditSession session, string from)
        {
            for (var i = 0; i < session.Redirects.Count; i++)
            {
                var redirect = session.Redirects[i];
                if (redirect.From == from)
                {
                    return redirect.To;
                }
            }

            Assert.Fail($"Expected redirect source was not found: {from}");
            return string.Empty;
        }

        private static string CreateMaximumCatalogJson()
        {
            const int descendantCount = 65_534;
            var json = new StringBuilder(descendantCount * 24);
            json.Append("{\"schemaVersion\":1,\"tags\":[");
            for (var i = 0; i < descendantCount; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"name\":\"State.X");
                json.Append(i.ToString("D5", CultureInfo.InvariantCulture));
                json.Append("\"}");
            }

            json.Append("]}");
            return json.ToString();
        }
    }
}
