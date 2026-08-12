#nullable enable
using System;
using Bun3.Gameplay.Editor.Tags;
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
    }
}
