#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagProjectSettingsProvider
    {
        private const string SettingsPath = "Project/Gameplay Tags";

        [SettingsProvider]
        internal static SettingsProvider CreateProvider()
        {
            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                guiHandler = _ => DrawGui()
            };
            return provider;
        }

        internal static GameplayTagProjectSettingsProviderStatus GetProviderStatus(
            IReadOnlyList<Type> providerTypes,
            string? configuredCatalogId)
        {
            if (providerTypes is null) throw new ArgumentNullException(nameof(providerTypes));
            var candidates = GameplayTagBuildContextProviderDiscovery.SelectCandidates(providerTypes);
            if (candidates.Count == 0)
            {
                return new GameplayTagProjectSettingsProviderStatus(
                    "No gameplay tag build context provider is configured. "
                    + "Development builds use the Game Source only.",
                    MessageType.Info);
            }

            if (candidates.Count > 1)
            {
                return new GameplayTagProjectSettingsProviderStatus(
                    "Multiple gameplay tag build context providers are configured; exactly one is required. "
                    + GameplayTagBuildContextProviderDiscovery.FormatCandidateCount(candidates),
                    MessageType.Error);
            }

            var providerType = candidates[0];
            var providerName = providerType.FullName ?? providerType.Name;
            IGameplayTagBuildContextProvider provider;
            try
            {
                provider = (IGameplayTagBuildContextProvider)Activator.CreateInstance(
                    providerType, nonPublic: true)!;
            }
            catch (Exception exception)
            {
                return new GameplayTagProjectSettingsProviderStatus(
                    "Failed to create gameplay tag build context provider " + providerName + ": "
                    + exception.GetBaseException().Message,
                    MessageType.Error);
            }

            string providerCatalogId;
            try
            {
                providerCatalogId = provider.CatalogId;
            }
            catch (Exception exception)
            {
                return new GameplayTagProjectSettingsProviderStatus(
                    "Failed to read Catalog ID from gameplay tag build context provider "
                    + providerName + ": " + exception.GetBaseException().Message,
                    MessageType.Error);
            }

            if (string.IsNullOrWhiteSpace(configuredCatalogId))
            {
                return new GameplayTagProjectSettingsProviderStatus(
                    "Gameplay tag build context provider " + providerName
                    + " is configured, but Project Settings Catalog ID is not set.",
                    MessageType.Error);
            }

            if (string.Equals(providerCatalogId, configuredCatalogId, StringComparison.Ordinal))
            {
                return new GameplayTagProjectSettingsProviderStatus(
                    "Gameplay tag build context provider " + providerName + " matches Catalog ID '"
                    + configuredCatalogId + "'.",
                    MessageType.Info);
            }

            return new GameplayTagProjectSettingsProviderStatus(
                "Gameplay tag build context provider " + providerName + " uses Catalog ID '"
                + providerCatalogId + "', which does not match Project Settings Catalog ID '"
                + configuredCatalogId + "'.",
                MessageType.Error);
        }

        private static void DrawGui()
        {
            var catalogId = GameplayTagProjectSettings.ReadConfiguredCatalogId()
                ?? GameplayTagProjectSettings.GetSuggestedCatalogId(PlayerSettings.productName);
            EditorGUILayout.HelpBox(
                "Catalog ID identifies this product's Gameplay Tag catalog. "
                + "Published builds require a gameplay tag build context provider.",
                MessageType.Info);
            var editedCatalogId = EditorGUILayout.TextField("Catalog ID", catalogId);
            if (GUILayout.Button("Apply"))
            {
                try
                {
                    GameplayTagProjectSettings.SaveCatalogId(editedCatalogId);
                }
                catch (Exception exception)
                {
                    GameplayTagDiagnosticsPanel.ShowWarning(
                        "Gameplay Tag Project Settings",
                        exception.Message);
                }
            }

            var status = GetProviderStatus(
                GameplayTagBuildContextProviderDiscovery.Discover(),
                GameplayTagProjectSettings.ReadConfiguredCatalogId());
            EditorGUILayout.HelpBox(status.Message, status.MessageType);
        }
    }

    internal readonly struct GameplayTagProjectSettingsProviderStatus
    {
        internal GameplayTagProjectSettingsProviderStatus(string message, MessageType messageType)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            MessageType = messageType;
        }

        internal string Message { get; }

        internal MessageType MessageType { get; }
    }
}
