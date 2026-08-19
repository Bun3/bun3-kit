#nullable enable
using System;
using System.IO;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Builds a valid editor workspace into an atomic local development catalog.</summary>
    public static class GameplayTagDevelopmentCatalogBuilder
    {
        private const string MenuPath = "Gameplay/Build Local Tag Catalog";

        /// <summary>Writes the workspace's immutable preview to the development cache and reloads it with the real binary reader.</summary>
        /// <param name="workspace">Complete and valid product-wide source workspace.</param>
        /// <returns>Immutable catalog reloaded from the verified development binary.</returns>
        /// <exception cref="InvalidOperationException">The workspace cannot build a development catalog.</exception>
        public static TagCatalog Build(GameplayTagEditorWorkspace workspace) =>
            Build(workspace, localApplicationDataOverride: null);

        internal static TagCatalog Build(
            GameplayTagEditorWorkspace workspace,
            string? localApplicationDataOverride) =>
            Build(workspace, localApplicationDataOverride, TagCatalogBinary.Load);

        internal static TagCatalog Build(
            GameplayTagEditorWorkspace workspace,
            string? localApplicationDataOverride,
            Func<Stream, TagCatalogExpectations, TagCatalog> load)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));
            if (load is null) throw new ArgumentNullException(nameof(load));
            if (!workspace.CanBuildCatalog || workspace.Snapshot is null)
            {
                throw new InvalidOperationException(
                    "Local Development Catalog cannot be built from the current Workspace."
                    + FormatDiagnostics(workspace.Diagnostics));
            }

            var preview = workspace.Snapshot.Catalog;
            var destination = TagCatalogDevelopmentPath.Get(
                preview.CatalogId, localApplicationDataOverride);
            TagCatalog? reloaded = null;
            AtomicFileWriter.WriteVerified(
                destination,
                output => TagCatalogBinaryWriter.Write(output, preview),
                input => reloaded = load(
                    input,
                    TagCatalogExpectations.ForDevelopment(preview.CatalogId)));
            return reloaded
                ?? throw new InvalidOperationException("Development Catalog binary readback did not return a Catalog.");
        }

        [MenuItem(MenuPath)]
        private static void BuildFromMenu()
        {
            var sourcePath = GameplayTagGameSourcePath.Get(Application.dataPath);
            try
            {
                var workspace = GameplayTagEditorWorkspace.Open(
                    GameplayTagBuildContextResolver.ResolveDevelopment(sourcePath),
                    sourcePath);
                var catalog = Build(workspace);
                EditorUtility.DisplayDialog(
                    "Local GameplayTag Catalog Built",
                    TagCatalogDevelopmentPath.Get(catalog.CatalogId),
                    "OK");
            }
            catch (Exception exception)
            {
                GameplayTagDiagnosticsPanel.ShowWarning(
                    "Local GameplayTag Catalog Build Failed",
                    sourcePath + Environment.NewLine + exception.Message);
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateBuildFromMenu()
        {
            var sourcePath = GameplayTagGameSourcePath.Get(Application.dataPath);
            try
            {
                return GameplayTagEditorWorkspace.Open(
                    GameplayTagBuildContextResolver.ResolveDevelopment(sourcePath),
                    sourcePath).CanBuildCatalog;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string FormatDiagnostics(System.Collections.Generic.IReadOnlyList<string> diagnostics)
        {
            if (diagnostics.Count == 0) return string.Empty;
            return Environment.NewLine + string.Join(Environment.NewLine, diagnostics);
        }
    }
}
