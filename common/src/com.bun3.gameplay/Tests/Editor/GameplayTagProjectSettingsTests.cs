#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;

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

        /// <summary>설정 Provider 활성화 뒤 편집 버퍼가 연속 GUI 이벤트 사이에 유지되는지 검증합니다.</summary>
        [Test]
        public void Settings_provider_keeps_the_edited_catalog_id_between_gui_events()
        {
            var initialized = 0;
            var observedValues = new List<string>();
            var provider = GameplayTagProjectSettingsProvider.CreateProvider(
                () =>
                {
                    initialized++;
                    return "saved-game";
                },
                value =>
                {
                    observedValues.Add(value);
                    return observedValues.Count == 1 ? "edited-game" : value;
                });

            provider.activateHandler!(string.Empty, null!);
            provider.guiHandler!(string.Empty);
            provider.guiHandler!(string.Empty);

            Assert.That(initialized, Is.EqualTo(1));
            Assert.That(observedValues, Is.EqualTo(new[] { "saved-game", "edited-game" }));
        }

        /// <summary>정규화 결과가 저장된 raw 값과 같으면 저장과 editor 갱신 알림을 생략하는지 검증합니다.</summary>
        [Test]
        public void Applying_an_unchanged_normalized_id_skips_persistence_and_refresh()
        {
            var saveCount = 0;
            var refreshCount = 0;

            var result = GameplayTagProjectSettingsProvider.ApplyCatalogId(
                "  CONFIGURED_GAME  ",
                "configured-game",
                value =>
                {
                    saveCount++;
                    return value;
                },
                () => refreshCount++);

            Assert.That(result, Is.EqualTo("configured-game"));
            Assert.That(saveCount, Is.Zero);
            Assert.That(refreshCount, Is.Zero);
        }

        /// <summary>변경된 입력은 canonical 값으로 한 번 저장한 뒤 editor 갱신을 한 번 알리는지 검증합니다.</summary>
        [Test]
        public void Applying_a_changed_id_persists_canonical_value_before_refresh()
        {
            var persisted = string.Empty;
            var refreshCount = 0;

            var result = GameplayTagProjectSettingsProvider.ApplyCatalogId(
                " Next Game ",
                "configured-game",
                value =>
                {
                    persisted = value;
                    return value;
                },
                () => refreshCount++);

            Assert.That(result, Is.EqualTo("next-game"));
            Assert.That(persisted, Is.EqualTo("next-game"));
            Assert.That(refreshCount, Is.EqualTo(1));
        }

        /// <summary>저장 예외가 나면 알림 없이 예외를 그대로 전달하는지 검증합니다.</summary>
        [Test]
        public void Failed_apply_does_not_notify_open_editors()
        {
            var refreshCount = 0;

            Assert.Throws<InvalidOperationException>(() =>
                GameplayTagProjectSettingsProvider.ApplyCatalogId(
                    "next-game",
                    "configured-game",
                    _ => throw new InvalidOperationException("save failed"),
                    () => refreshCount++));

            Assert.That(refreshCount, Is.Zero);
        }

        /// <summary>persistence가 대입 뒤 실패하면 singleton의 이전 raw 값을 복원하는지 검증합니다.</summary>
        [Test]
        public void Save_failure_restores_the_previous_in_memory_raw_value()
        {
            WithRawCatalogId("previous--raw", () =>
            {
                var error = Assert.Throws<InvalidOperationException>(() =>
                    GameplayTagProjectSettings.SaveCatalogId("next-game", () =>
                    {
                        Assert.That(
                            GameplayTagProjectSettings.ReadConfiguredCatalogId(),
                            Is.EqualTo("next-game"));
                        throw new InvalidOperationException("persistence failed");
                    }));

                Assert.That(error!.Message, Is.EqualTo("persistence failed"));
                Assert.That(
                    GameplayTagProjectSettings.ReadConfiguredCatalogId(),
                    Is.EqualTo("previous--raw"));
            });
        }

        /// <summary>production development resolver가 malformed raw 설정을 정규화하지 않고 거부하는지 검증합니다.</summary>
        [TestCase("test--game")]
        [TestCase(" TEST-GAME ")]
        public void Production_development_resolution_rejects_noncanonical_raw_settings(
            string rawCatalogId)
        {
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-project-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            var gameSourcePath = Path.Combine(temporaryDirectory, "GameplayTags.json");
            GameplayTagCatalogFileAdapter.CreateGameSource(gameSourcePath);
            try
            {
                WithRawCatalogId(rawCatalogId, () =>
                {
                    var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(gameSourcePath);

                    Assert.That(resolution.HasCompleteContext, Is.False);
                    Assert.That(resolution.Diagnostics, Has.Count.EqualTo(1));
                    Assert.That(resolution.Diagnostics[0], Does.Contain("Project Settings"));
                    Assert.That(resolution.Diagnostics[0], Does.Contain("canonical"));
                });
            }
            finally
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }

        /// <summary>production Published 진입점도 malformed raw 설정을 artifact 접근 전에 거부하는지 검증합니다.</summary>
        [Test]
        public void Production_published_resolution_rejects_noncanonical_raw_settings()
        {
            WithRawCatalogId("RELEASE GAME", () =>
            {
                var error = Assert.Throws<BuildFailedException>(() =>
                    GameplayTagPublishedCatalogValidator.ResolvePublishedCatalog());

                Assert.That(error!.Message, Does.Contain("Project Settings"));
                Assert.That(error.Message, Does.Contain("canonical"));
            });
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

        /// <summary>Project Settings가 없어도 단일 Provider를 활성 상태로 안내하는지 검증합니다.</summary>
        [Test]
        public void Provider_status_treats_a_provider_without_project_settings_as_active()
        {
            var status = GameplayTagProjectSettingsProvider.GetProviderStatus(
                new[] { typeof(MatchingProvider) }, null);

            Assert.That(status.MessageType, Is.EqualTo(MessageType.Info));
            Assert.That(status.Message, Does.Contain(typeof(MatchingProvider).FullName));
            Assert.That(status.Message, Does.Contain("active"));
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

        private static void WithRawCatalogId(string rawCatalogId, Action action)
        {
            if (rawCatalogId is null) throw new ArgumentNullException(nameof(rawCatalogId));
            if (action is null) throw new ArgumentNullException(nameof(action));
            var field = typeof(GameplayTagProjectSettings).GetField(
                "_catalogId",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var settings = GameplayTagProjectSettings.instance;
            var previous = (string?)field.GetValue(settings);
            field.SetValue(settings, rawCatalogId);
            try
            {
                action();
            }
            finally
            {
                field.SetValue(settings, previous);
            }
        }
    }
}
