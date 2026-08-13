#nullable enable
using System.Linq;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>게임플레이 태그 공용 트리 모델의 행 구성과 검색 동작을 검증합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagTreeModelTests
    {
        /// <summary>명시 태그와 암시 부모가 행에서 구분되는지 검증합니다.</summary>
        [Test]
        public void Rows_distinguish_explicit_tags_from_implicit_parents()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\",\"comment\":\"사망\"}]}");
            var model = new GameplayTagTreeModel(session);

            Assert.That(model.Rows.Single(row => row.Path == "State").IsExplicit, Is.False);
            var dead = model.Rows.Single(row => row.Path == "State.Dead");
            Assert.That(dead.IsExplicit, Is.True);
            Assert.That(dead.Comment, Is.EqualTo("사망"));
        }

        /// <summary>검색이 전체 경로만 대상으로 하고 조상 문맥을 유지하는지 검증합니다.</summary>
        [Test]
        public void Search_matches_full_path_only_and_keeps_ancestor_context()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Dead.Ghost\",\"comment\":\"spectral marker\"}," +
                "{\"name\":\"Ability.GhostWalk\"}]}");
            var model = new GameplayTagTreeModel(session);

            Assert.That(model.Filter("gHoSt").Select(row => row.Path),
                Is.EqualTo(new[] { "Ability", "Ability.GhostWalk", "State", "State.Dead", "State.Dead.Ghost" }));
            Assert.That(model.Filter("spectral"), Is.Empty);
        }

        /// <summary>검색 일치 행과 그 조상 행이 함께 유지되는지 검증합니다.</summary>
        [Test]
        public void Search_keeps_matching_rows_and_their_ancestor_context()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Dead.Ghost\",\"comment\":\"유령 상태\"}," +
                "{\"name\":\"State.Alive\"},{\"name\":\"Ability.Jump\"}]}");
            var model = new GameplayTagTreeModel(session);

            var rows = model.Filter("gHoSt");

            Assert.That(rows.Select(row => row.Path),
                Is.EqualTo(new[] { "State", "State.Dead", "State.Dead.Ghost" }));
            Assert.That(rows[2].Comment, Is.EqualTo("유령 상태"));
            Assert.That(rows[2].IsDirectMatch, Is.True);
            Assert.That(rows[0].IsDirectMatch, Is.False);
        }

        /// <summary>빈 검색이 결정적인 전위 순회 행을 반환하는지 검증합니다.</summary>
        [Test]
        public void Empty_search_returns_deterministic_preorder()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"},{\"name\":\"Ability.Jump\"}]}");
            var model = new GameplayTagTreeModel(session);

            Assert.That(model.Filter("").Select(row => row.Path),
                Is.EqualTo(new[] { "Ability", "Ability.Jump", "State", "State.Dead" }));
        }
    }
}
