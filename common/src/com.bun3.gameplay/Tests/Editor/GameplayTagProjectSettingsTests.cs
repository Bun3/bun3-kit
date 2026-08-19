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
    /// <summary>Verifies the Gameplay Tag catalog ID project settings contract.</summary>
    [TestFixture]
    public sealed class GameplayTagProjectSettingsTests
    {
        /// <summary>Verifies product names and user input normalize to a stable public catalog ID.</summary>
        [TestCase("Jurassic Paradise", "jurassic-paradise")]
        [TestCase("Bun3.Game.Core", "bun3-game-core")]
        [TestCase("  GAME__SERVER  ", "game-server")]
        [TestCase("£¥€", "")]
        public void Catalog_id_normalization_is_deterministic(string input, string expected)
        {
            Assert.That(GameplayTagCatalogId.Normalize(input), Is.EqualTo(expected));
        }

        /// <summary>Verifies an invalid ID is rejected before persistence is called.</summary>
        [Test]
        public void Empty_normalized_id_is_rejected_before_persistence()
        {
            var saveCount = 0;

            Assert.Throws<ArgumentException>(() =>
                GameplayTagProjectSettings.ApplyCatalogId("---", _ => saveCount++));

            Assert.That(saveCount, Is.Zero);
        }

        /// <summary>Verifies the normalized ID is passed to persistence exactly once and returned.</summary>
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

        /// <summary>Verifies reading the setting neither creates nor modifies the project settings asset.</summary>
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

        /// <summary>Verifies the settings provider registers at the fixed Unity project settings path.</summary>
        [Test]
        public void Settings_provider_uses_the_project_gameplay_tags_path()
        {
            var provider = GameplayTagProjectSettingsProvider.CreateProvider();

            Assert.That(provider.settingsPath, Is.EqualTo("Project/Gameplay Tags"));
            Assert.That(provider.scope, Is.EqualTo(SettingsScope.Project));
        }

        /// <summary>Verifies the edit buffer persists across consecutive GUI events after provider activation.</summary>
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

        /// <summary>Verifies save and editor refresh notification are skipped when the normalized result equals the stored raw value.</summary>
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

        /// <summary>Verifies changed input is saved once as the canonical value and then notifies editor refresh once.</summary>
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

        /// <summary>Verifies a save exception propagates unchanged without notification.</summary>
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

        /// <summary>Verifies the singleton's previous raw value is restored when persistence fails after assignment.</summary>
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

        /// <summary>Verifies the production development resolver rejects a malformed raw setting instead of normalizing it.</summary>
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

        /// <summary>Verifies the production published entry point also rejects a malformed raw setting before artifact access.</summary>
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

        /// <summary>Verifies the development fallback guidance is returned when no provider exists.</summary>
        [Test]
        public void Provider_status_reports_the_development_fallback_when_no_provider_exists()
        {
            var status = GameplayTagProjectSettingsProvider.GetProviderStatus(
                Array.Empty<Type>(), "configured-game");

            Assert.That(status.MessageType, Is.EqualTo(MessageType.Info));
            Assert.That(status.Message, Does.Contain("Development"));
        }

        /// <summary>Verifies a single provider matching the configured catalog ID shows its full type name and success state.</summary>
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

        /// <summary>Verifies a single provider is reported active even without project settings.</summary>
        [Test]
        public void Provider_status_treats_a_provider_without_project_settings_as_active()
        {
            var status = GameplayTagProjectSettingsProvider.GetProviderStatus(
                new[] { typeof(MatchingProvider) }, null);

            Assert.That(status.MessageType, Is.EqualTo(MessageType.Info));
            Assert.That(status.Message, Does.Contain(typeof(MatchingProvider).FullName));
            Assert.That(status.Message, Does.Contain("active"));
        }

        /// <summary>Verifies a single provider ID differing from project settings shows an error state.</summary>
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

        /// <summary>Verifies multiple providers are listed in ordinal full-type-name order with an error state.</summary>
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
