#nullable enable
using System;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>redirect 정리 정책이 완전한 검색 결과만 신뢰하는지 검증합니다.</summary>
    [TestFixture]
    internal sealed class GameplayTagRedirectMaintenanceTests
    {
        /// <summary>프로젝트 match가 없는 redirect만 일괄 후보가 되는지 검증합니다.</summary>
        [Test]
        public void Bulk_cleanup_returns_only_sources_without_project_matches()
        {
            var redirects = new[]
            {
                new EditableRedirectRow("State.Killed", "State.Dead"),
                new EditableRedirectRow("Ability.Old", "Ability.New")
            };
            var result = GameplayTagReferenceSearchResult.Complete(new[]
            {
                new GameplayTagReferenceMatch(
                    "State.Killed", "C:/Project/Assets/A.prefab", "Assets/A.prefab", 7, "State.Killed")
            });

            Assert.That(GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, result),
                Is.EqualTo(new[] { "Ability.Old" }));
        }

        /// <summary>대소문자만 다른 match도 후보에서 제외하고 redirect 순서를 유지하는지 검증합니다.</summary>
        [Test]
        public void Bulk_cleanup_ignores_case_and_preserves_redirect_order()
        {
            var redirects = new[]
            {
                new EditableRedirectRow("State.Killed", "State.Dead"),
                new EditableRedirectRow("Ability.Old", "Ability.New"),
                new EditableRedirectRow("Item.Old", "Item.New")
            };
            var result = GameplayTagReferenceSearchResult.Complete(new[]
            {
                new GameplayTagReferenceMatch(
                    "ability.old", "C:/Project/Assets/A.prefab", "Assets/A.prefab", 3, "ability.old")
            });

            Assert.That(GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, result),
                Is.EqualTo(new[] { "State.Killed", "Item.Old" }));
        }

        /// <summary>불완전한 검색이 정리 후보를 만들지 못하는지 검증합니다.</summary>
        [Test]
        public void Incomplete_scan_cannot_produce_cleanup_candidates()
        {
            var redirects = new[] { new EditableRedirectRow("State.Killed", "State.Dead") };
            var incomplete = GameplayTagReferenceSearchResult.Incomplete(
                cancelled: true, errors: Array.Empty<string>());

            Assert.Throws<InvalidOperationException>(
                () => GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, incomplete));
        }

        /// <summary>참조된 redirect dialog 결과가 명시적 결정으로 변환되는지 검증합니다.</summary>
        /// <param name="result">DisplayDialogComplex가 반환한 버튼 index입니다.</param>
        /// <param name="expected">기대하는 결정입니다.</param>
        [TestCase(0, ReferencedRedirectDecision.OpenReferences)]
        [TestCase(1, ReferencedRedirectDecision.Cancel)]
        [TestCase(2, ReferencedRedirectDecision.RemoveAnyway)]
        public void Referenced_redirect_dialog_result_maps_to_an_explicit_decision(
            int result, ReferencedRedirectDecision expected)
        {
            Assert.That(GameplayTagRedirectMaintenance.MapReferencedDialogResult(result), Is.EqualTo(expected));
        }

        /// <summary>정의되지 않은 dialog 결과를 거부하는지 검증합니다.</summary>
        [Test]
        public void Unknown_dialog_result_is_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => GameplayTagRedirectMaintenance.MapReferencedDialogResult(3));
        }

        /// <summary>정리 경고가 검사하지 못한 외부 데이터를 항상 알리는지 검증합니다.</summary>
        [Test]
        public void Cleanup_warning_always_names_the_unscanned_external_data()
        {
            Assert.That(GameplayTagRedirectMaintenance.NoProjectReferencesWarning,
                Does.StartWith("No project references were found."));
            Assert.That(GameplayTagRedirectMaintenance.ExternalDataScopeWarning,
                Does.Contain("Save data").And.Contain("server configuration")
                    .And.Contain("external files").And.Contain("deployed builds"));
        }
    }
}
