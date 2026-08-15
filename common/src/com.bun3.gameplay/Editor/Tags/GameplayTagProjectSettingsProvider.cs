#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagProjectSettingsProvider
    {
        private const string SettingsPath = "Project/Gameplay Tags";

        [SettingsProvider]
        internal static SettingsProvider CreateProvider() =>
            CreateProvider(GetInitialCatalogId, DrawGui);

        internal static SettingsProvider CreateProvider(
            Func<string> initializeEditorBuffer,
            Func<string, string> drawGui)
        {
            if (initializeEditorBuffer is null)
            {
                throw new ArgumentNullException(nameof(initializeEditorBuffer));
            }

            if (drawGui is null) throw new ArgumentNullException(nameof(drawGui));
            var editorBuffer = string.Empty;
            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                activateHandler = (_, _) => editorBuffer = initializeEditorBuffer(),
                guiHandler = _ => editorBuffer = drawGui(editorBuffer)
            };
            return provider;
        }

        internal static string ApplyCatalogId(
            string editedCatalogId,
            string? configuredCatalogId,
            Func<string, string> saveCatalogId,
            Action notifyOpenEditors)
        {
            if (saveCatalogId is null) throw new ArgumentNullException(nameof(saveCatalogId));
            if (notifyOpenEditors is null) throw new ArgumentNullException(nameof(notifyOpenEditors));
            var normalized = GameplayTagCatalogId.Require(editedCatalogId, nameof(editedCatalogId));
            if (string.Equals(normalized, configuredCatalogId, StringComparison.Ordinal))
            {
                return normalized;
            }

            var saved = saveCatalogId(normalized);
            notifyOpenEditors();
            return saved;
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
                    + " is active. Project Settings Catalog ID is not set, so the provider ID '"
                    + providerCatalogId + "' is used.",
                    MessageType.Info);
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

        private static string GetInitialCatalogId() =>
            GameplayTagProjectSettings.ReadConfiguredCatalogId()
            ?? GameplayTagProjectSettings.GetSuggestedCatalogId(PlayerSettings.productName);

        private static string DrawGui(string editedCatalogId)
        {
            EditorGUILayout.HelpBox(
                "Catalog ID identifies this product's Gameplay Tag catalog. "
                + "Published builds require a gameplay tag build context provider.",
                MessageType.Info);
            editedCatalogId = EditorGUILayout.TextField("Catalog ID", editedCatalogId);
            if (GUILayout.Button("Apply"))
            {
                try
                {
                    editedCatalogId = ApplyCatalogId(
                        editedCatalogId,
                        GameplayTagProjectSettings.ReadConfiguredCatalogId(),
                        GameplayTagProjectSettings.SaveCatalogId,
                        NotifyOpenEditors);
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
            return editedCatalogId;
        }

        private static void NotifyOpenEditors()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            InternalEditorUtility.RepaintAllViews();
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
