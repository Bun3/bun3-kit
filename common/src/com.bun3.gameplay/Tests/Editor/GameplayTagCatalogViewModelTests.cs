#nullable enable
using System.Linq;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>게임플레이 태그 카탈로그 뷰 모델의 검색 동작을 검증합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagCatalogViewModelTests
    {
        /// <summary>검색 일치 행과 그 조상 행이 함께 유지되는지 검증합니다.</summary>
        [Test]
        public void Search_keeps_matching_rows_and_their_ancestor_context()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Dead.Ghost\",\"comment\":\"유령 상태\"}," +
                "{\"name\":\"State.Alive\"},{\"name\":\"Ability.Jump\"}]}");
            var model = new GameplayTagCatalogViewModel(session);

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
            var model = new GameplayTagCatalogViewModel(session);

            Assert.That(model.Filter("").Select(row => row.Path),
                Is.EqualTo(new[] { "Ability", "Ability.Jump", "State", "State.Dead" }));
        }
    }
}
