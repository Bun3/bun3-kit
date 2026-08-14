#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;
using UnityEditor;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>Gameplay Tag Catalog ID Project Settings 계약을 검증합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagProjectSettingsTests
    {
        /// <summary>제품 이름과 사용자 입력을 안정적인 공개 Catalog ID로 정규화하는지 검증합니다.</summary>
        [TestCase("Jurassic Paradise", "jurassic-paradise")]
        [TestCase("Bun3.Game.Core", "bun3-game-core")]
        [TestCase("  GAME__SERVER  ", "game-server")]
        [TestCase("한국 게임", "")]
        public void Catalog_id_normalization_is_deterministic(string input, string expected)
        {
            Assert.That(GameplayTagCatalogId.Normalize(input), Is.EqualTo(expected));
        }

        /// <summary>유효하지 않은 ID가 persistence를 호출하기 전에 거부되는지 검증합니다.</summary>
        [Test]
        public void Empty_normalized_id_is_rejected_before_persistence()
        {
            var saveCount = 0;

            Assert.Throws<ArgumentException>(() =>
                GameplayTagProjectSettings.ApplyCatalogId("---", _ => saveCount++));

            Assert.That(saveCount, Is.Zero);
        }

        /// <summary>정규화한 ID를 정확히 한 번 persistence에 전달하고 반환하는지 검증합니다.</summary>
        [Test]
        public void Applying_catalog_id_persists_the_normalized_value_once()
        {
            var saveCount = 0;
            var persisted = string.Empty;

            var result = GameplayTagProjectSettings.ApplyCatalogId(" My Game ", value =>
            {
                saveCount++;
                persisted = value;
            });

            Assert.That(result, Is.EqualTo("my-game"));
            Assert.That(persisted, Is.EqualTo("my-game"));
            Assert.That(saveCount, Is.EqualTo(1));
        }

        /// <summary>설정 값을 읽는 동작이 Project Settings asset을 만들거나 수정하지 않는지 검증합니다.</summary>
        [Test]
        public void Reading_catalog_id_does_not_create_or_modify_the_settings_asset()
        {
            var path = Path.Combine(
                Directory.GetCurrentDirectory(),
                "ProjectSettings",
                "GameplayTagSettings.asset");
            var existed = File.Exists(path);
            var before = existed ? File.ReadAllBytes(path) : Array.Empty<byte>();

            _ = GameplayTagProjectSettings.ReadConfiguredCatalogId();

            Assert.That(File.Exists(path), Is.EqualTo(existed));
            if (existed) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        /// <summary>설정 Provider가 Unity Project Settings의 고정 경로로 등록되는지 검증합니다.</summary>
        [Test]
        public void Settings_provider_uses_the_project_gameplay_tags_path()
        {
            var provider = GameplayTagProjectSettingsProvider.CreateProvider();

            Assert.That(provider.settingsPath, Is.EqualTo("Project/Gameplay Tags"));
            Assert.That(provider.scope, Is.EqualTo(SettingsScope.Project));
        }

        /// <summary>Provider가 없으면 development fallback 안내를 반환하는지 검증합니다.</summary>
        [Test]
        public void Provider_status_reports_the_development_fallback_when_no_provider_exists()
        {
            var status = GameplayTagProjectSettingsProvider.GetProviderStatus(
                Array.Empty<Type>(), "configured-game");

            Assert.That(status.MessageType, Is.EqualTo(MessageType.Info));
            Assert.That(status.Message, Does.Contain("Development"));
        }

        /// <summary>하나의 Provider가 설정된 Catalog ID와 일치하면 전체 타입 이름과 성공 상태를 표시하는지 검증합니다.</summary>
        [Test]
        public void Provider_status_shows_the_full_name_for_a_matching_catalog_id()
        {
            var status = GameplayTagProjectSettingsProvider.GetProviderStatus(
                new[] { typeof(MatchingProvider) }, "configured-game");

            Assert.That(status.MessageType, Is.EqualTo(MessageType.Info));
            Assert.That(status.Message, Does.Contain(typeof(MatchingProvider).FullName));
            Assert.That(status.Message, Does.Contain("configured-game"));
            Assert.That(status.Message, Does.Contain("matches"));
        }

        /// <summary>하나의 Provider ID가 Project Settings와 다르면 오류 상태를 표시하는지 검증합니다.</summary>
        [Test]
        public void Provider_status_reports_an_error_for_a_mismatching_catalog_id()
        {
            var status = GameplayTagProjectSettingsProvider.GetProviderStatus(
                new[] { typeof(MismatchingProvider) }, "configured-game");

            Assert.That(status.MessageType, Is.EqualTo(MessageType.Error));
            Assert.That(status.Message, Does.Contain(typeof(MismatchingProvider).FullName));
            Assert.That(status.Message, Does.Contain("other-game"));
            Assert.That(status.Message, Does.Contain("configured-game"));
        }

        /// <summary>여러 Provider를 ordinal 전체 타입 이름 순서로 표시하고 오류 상태를 반환하는지 검증합니다.</summary>
        [Test]
        public void Provider_status_reports_multiple_providers_in_ordinal_full_name_order()
        {
            var status = GameplayTagProjectSettingsProvider.GetProviderStatus(
                new[] { typeof(ZebraProvider), typeof(AlphaProvider) }, "configured-game");
            var alpha = typeof(AlphaProvider).FullName!;
            var zebra = typeof(ZebraProvider).FullName!;

            Assert.That(status.MessageType, Is.EqualTo(MessageType.Error));
            Assert.That(status.Message, Does.Contain(alpha));
            Assert.That(status.Message, Does.Contain(zebra));
            Assert.That(status.Message.IndexOf(alpha, StringComparison.Ordinal),
                Is.LessThan(status.Message.IndexOf(zebra, StringComparison.Ordinal)));
        }

        private sealed class MatchingProvider : IGameplayTagBuildContextProvider
        {
            /// <inheritdoc />
            public string CatalogId => "configured-game";

            /// <inheritdoc />
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();

            /// <inheritdoc />
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                throw new InvalidOperationException();
        }

        private sealed class MismatchingProvider : IGameplayTagBuildContextProvider
        {
            /// <inheritdoc />
            public string CatalogId => "other-game";

            /// <inheritdoc />
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();

            /// <inheritdoc />
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                throw new InvalidOperationException();
        }

        private sealed class AlphaProvider : IGameplayTagBuildContextProvider
        {
            /// <inheritdoc />
            public string CatalogId => "configured-game";

            /// <inheritdoc />
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();

            /// <inheritdoc />
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                throw new InvalidOperationException();
        }

        private sealed class ZebraProvider : IGameplayTagBuildContextProvider
        {
            /// <inheritdoc />
            public string CatalogId => "configured-game";

            /// <inheritdoc />
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();

            /// <inheritdoc />
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                throw new InvalidOperationException();
        }
    }
}
