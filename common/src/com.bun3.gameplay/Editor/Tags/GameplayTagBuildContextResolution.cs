#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagWorkspaceDiagnostic
    {
        internal GameplayTagWorkspaceDiagnostic(string message, string? localSourcePath)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            LocalSourcePath = localSourcePath;
        }

        internal string Message { get; }
        internal string? LocalSourcePath { get; }
    }

    /// <summary>Result of discovering the game catalog build context in the Unity editor.</summary>
    public sealed class GameplayTagBuildContextResolution
    {
        private readonly IReadOnlyList<string> _diagnostics;
        private readonly IReadOnlyList<GameplayTagWorkspaceDiagnostic> _diagnosticEntries;

        internal GameplayTagBuildContextResolution(
            GameCatalogBuildContext? context,
            string[] diagnostics,
            bool permitsGameOnlyValidation)
            : this(
                context,
                diagnostics,
                permitsGameOnlyValidation,
                requiresCatalogConfiguration: false)
        {
        }

        internal GameplayTagBuildContextResolution(
            GameCatalogBuildContext? context,
            string[] diagnostics,
            bool permitsGameOnlyValidation,
            bool requiresCatalogConfiguration)
            : this(
                context,
                CreateEntries(diagnostics),
                permitsGameOnlyValidation,
                requiresCatalogConfiguration)
        {
        }

        internal GameplayTagBuildContextResolution(
            GameCatalogBuildContext? context,
            GameplayTagWorkspaceDiagnostic[] diagnostics,
            bool permitsGameOnlyValidation)
            : this(
                context,
                diagnostics,
                permitsGameOnlyValidation,
                requiresCatalogConfiguration: false)
        {
        }

        internal GameplayTagBuildContextResolution(
            GameCatalogBuildContext? context,
            GameplayTagWorkspaceDiagnostic[] diagnostics,
            bool permitsGameOnlyValidation,
            bool requiresCatalogConfiguration)
        {
            Context = context;
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            var entries = (GameplayTagWorkspaceDiagnostic[])diagnostics.Clone();
            _diagnosticEntries = Array.AsReadOnly(entries);
            var messages = new string[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                messages[index] = entries[index].Message;
            }

            _diagnostics = Array.AsReadOnly(messages);
            PermitsGameOnlyValidation = permitsGameOnlyValidation;
            RequiresCatalogConfiguration = requiresCatalogConfiguration;
        }

        /// <summary>Product-wide development build context when fully resolved.</summary>
        public GameCatalogBuildContext? Context { get; }

        /// <summary>Stable diagnostics for discovery, configuration, or source read failures.</summary>
        public IReadOnlyList<string> Diagnostics => _diagnostics;

        internal IReadOnlyList<GameplayTagWorkspaceDiagnostic> DiagnosticEntries =>
            _diagnosticEntries;

        /// <summary>Whether a context with product-wide sources is available.</summary>
        public bool HasCompleteContext => Context is not null && _diagnostics.Count == 0;

        internal bool PermitsGameOnlyValidation { get; }

        internal bool RequiresCatalogConfiguration { get; }

        private static GameplayTagWorkspaceDiagnostic[] CreateEntries(string[] diagnostics)
        {
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            var entries = new GameplayTagWorkspaceDiagnostic[diagnostics.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                entries[index] = new GameplayTagWorkspaceDiagnostic(diagnostics[index], null);
            }

            return entries;
        }
    }
}
