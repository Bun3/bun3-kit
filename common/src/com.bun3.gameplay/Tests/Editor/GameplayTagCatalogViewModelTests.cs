#nullable enable
using System.Linq;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagCatalogViewModelTests
    {
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
