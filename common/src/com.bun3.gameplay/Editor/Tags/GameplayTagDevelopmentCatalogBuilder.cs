#nullable enable
using System;
using System.IO;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>유효한 Editor Workspace를 원자적인 Local Development Catalog로 만듭니다.</summary>
    public static class GameplayTagDevelopmentCatalogBuilder
    {
        private const string MenuPath = "Gameplay/Build Local Tag Catalog";

        /// <summary>Workspace의 불변 미리보기를 개발 cache에 쓰고 실제 binary reader로 재로드합니다.</summary>
        /// <param name="workspace">완전하고 유효한 제품 전체 Source Workspace입니다.</param>
        /// <returns>검증된 개발 binary에서 재로드한 불변 Catalog입니다.</returns>
        /// <exception cref="InvalidOperationException">Workspace가 개발 Catalog를 만들 수 없는 경우입니다.</exception>
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
