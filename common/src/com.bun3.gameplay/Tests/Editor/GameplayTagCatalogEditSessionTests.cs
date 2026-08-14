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
            Assert.That(catalog.GetDisplayName(catalog.GetRequired("State")), Is.EqualTo("state"));
            Assert.That(catalog.GetDisplayName(catalog.GetRequired("State.Dead")), Is.EqualTo("state.dead"));
            Assert.That(catalog.GetDisplayName(catalog.GetRequired("State.Dead.Ghost")),
                Is.EqualTo("state.dead.ghost"));
        }

        /// <summary>마지막 세그먼트만 바꾸고 기존 활성 경로와 redirect target을 함께 갱신하는지 검증합니다.</summary>
        [Test]
        public void Rename_subtree_changes_only_the_last_segment_and_redirects_every_active_old_path()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Movement.Run.Fast\"}]," +
                "\"redirects\":[{\"from\":\"Legacy.Run\",\"to\":\"State.Movement.Run\"}]}");

            var renamedPath = session.RenameSubtree("STATE.MOVEMENT.RUN", "Sprint");

            Assert.That(renamedPath, Is.EqualTo("State.Movement.Sprint"));
            Assert.That(session.Serialize(), Does.Contain("State.Movement.Sprint.Fast"));
            Assert.That(GetRedirectTarget(session, "State.Movement.Run"),
                Is.EqualTo("State.Movement.Sprint"));
            Assert.That(GetRedirectTarget(session, "State.Movement.Run.Fast"),
                Is.EqualTo("State.Movement.Sprint.Fast"));
            Assert.That(GetRedirectTarget(session, "Legacy.Run"),
                Is.EqualTo("State.Movement.Sprint"));
        }

        /// <summary>암시 부모의 마지막 세그먼트 변경이 명시 자손을 함께 옮기는지 검증합니다.</summary>
        [Test]
        public void Renaming_an_implicit_parent_moves_its_explicit_descendants()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Movement.Run\"}]}");

            Assert.That(session.RenameSubtree("State.Movement", "Motion"),
                Is.EqualTo("State.Motion"));
            Assert.That(session.Serialize(), Does.Contain("State.Motion.Run"));
            Assert.That(GetRedirectTarget(session, "State.Movement"), Is.EqualTo("State.Motion"));
            Assert.That(GetRedirectTarget(session, "State.Movement.Run"), Is.EqualTo("State.Motion.Run"));
        }

        /// <summary>전체 경로나 무효 문자가 새 세그먼트로 전달되면 문서를 보존하는지 검증합니다.</summary>
        /// <param name="newSegment">검증할 새 세그먼트입니다.</param>
        [TestCase("Other.Parent")]
        [TestCase("Bad_Name")]
        [TestCase("")]
        public void Rename_rejects_a_non_segment_and_preserves_the_document(string newSegment)
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");
            var before = session.Serialize();

            Assert.Throws<ArgumentException>(() => session.RenameSubtree("State.Dead", newSegment));
            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        /// <summary>목적 경로 충돌 시 직렬화 문서를 바이트 단위로 보존하는지 검증합니다.</summary>
        [Test]
        public void Collision_keeps_the_previous_document_byte_for_byte()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"},{\"name\":\"State.Alive\"}]}");
            var before = session.Serialize();

            Assert.Throws<InvalidOperationException>(
                () => session.RenameSubtree("State.Dead", "Alive"));
            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        /// <summary>명시 자손이 만든 암시 목적 경로와 충돌하면 문서를 보존하는지 검증합니다.</summary>
        [Test]
        public void Rename_rejects_an_implicit_destination_collision_and_preserves_the_document()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Dead\"},{\"name\":\"State.Alive.Child\"}]}");
            var before = session.Serialize();

            Assert.Throws<InvalidOperationException>(
                () => session.RenameSubtree("State.Dead", "Alive"));
            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        /// <summary>대소문자만 바꾼 이름 변경이 redirect 없이 표시 casing만 갱신하는지 검증합니다.</summary>
        [Test]
        public void Case_only_rename_changes_display_case_without_a_redirect()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");

            Assert.That(session.RenameSubtree("State.Dead", "dead"), Is.EqualTo("State.dead"));
            Assert.That(session.Serialize(), Does.Contain("\"name\": \"State.dead\""));
            Assert.That(session.Redirects, Is.Empty);
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

        /// <summary>암시 부모 rename 뒤 유일한 leaf를 지우면 조상 redirect까지 사라지는지 검증합니다.</summary>
        [Test]
        public void Delete_removes_every_redirect_whose_target_is_no_longer_active()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Movement.Run\"}]}");
            Assert.That(session.RenameSubtree("State.Movement", "Motion"), Is.EqualTo("State.Motion"));
            Assert.That(session.Redirects, Has.Count.EqualTo(2));

            session.Delete("State.Motion.Run", false);

            Assert.That(session.Redirects, Is.Empty);
            Assert.That(session.Serialize(), Does.Not.Contain("State.Movement"));
        }

        /// <summary>여러 redirect source를 대소문자 무시로 한 트랜잭션에서 제거하는지 검증합니다.</summary>
        [Test]
        public void Remove_redirects_matches_sources_case_insensitively_in_one_transaction()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
                "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}," +
                "{\"from\":\"State.Gone\",\"to\":\"State.Dead\"}]}");

            var removed = session.RemoveRedirects(new[] { "state.killed", "STATE.GONE" });

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(session.Redirects, Is.Empty);
        }

        /// <summary>존재하지 않는 redirect source가 문서를 그대로 보존하는지 검증합니다.</summary>
        [Test]
        public void Removing_an_unknown_redirect_preserves_the_document()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
                "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}");
            var before = session.Serialize();

            Assert.Throws<InvalidOperationException>(
                () => session.RemoveRedirects(new[] { "State.Killed", "Missing.Old" }));
            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        /// <summary>빈 입력이 no-op이고 중복 입력이 무해하며 null 항목이 거부되는지 검증합니다.</summary>
        [Test]
        public void Remove_redirects_tolerates_duplicates_and_rejects_null_sources()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
                "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}");
            var before = session.Serialize();

            Assert.That(session.RemoveRedirects(Array.Empty<string>()), Is.Zero);
            Assert.That(session.Serialize(), Is.EqualTo(before));
            Assert.Throws<ArgumentNullException>(() => session.RemoveRedirects(null!));
            Assert.Throws<ArgumentException>(() => session.RemoveRedirects(new string[] { null! }));
            Assert.That(session.Serialize(), Is.EqualTo(before));

            Assert.That(session.RemoveRedirects(new[] { "State.Killed", "state.killed" }), Is.EqualTo(1));
            Assert.That(session.Redirects, Is.Empty);
        }

        /// <summary>최대 크기 카탈로그에서도 마지막 활성 경로까지 direct redirect를 만드는지 검증합니다.</summary>
        [Test]
        [Timeout(600_000)]
        public void Rename_subtree_at_maximum_catalog_count_includes_the_last_active_path()
        {
            var session = GameplayTagCatalogEditSession.Open(CreateMaximumCatalogJson());

            Assert.DoesNotThrow(() => session.RenameSubtree("State", "Condition"));

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
