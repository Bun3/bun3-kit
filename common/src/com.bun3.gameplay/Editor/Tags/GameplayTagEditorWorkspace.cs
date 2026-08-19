#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Editor authoring state of the fixed game source and resolved external sources.</summary>
    public sealed class GameplayTagEditorWorkspace
    {
        private readonly IReadOnlyList<string> _diagnostics;
        private readonly IReadOnlyList<GameplayTagWorkspaceDiagnostic> _diagnosticEntries;
        private readonly GameplayTagBuildContextResolution _resolution;

        private GameplayTagEditorWorkspace(
            GameplayTagBuildContextResolution resolution,
            GameplayTagWorkspaceSnapshot? snapshot,
            GameplayTagCatalogEditSession? gameSession,
            GameplayTagWorkspaceDiagnostic[] diagnostics,
            bool canCreateGameSource,
            bool canEditGameSource,
            bool canBuildCatalog)
        {
            _resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
            Snapshot = snapshot;
            GameSession = gameSession;
            var entries = (GameplayTagWorkspaceDiagnostic[])diagnostics.Clone();
            _diagnosticEntries = Array.AsReadOnly(entries);
            var messages = new string[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                messages[index] = entries[index].Message;
            }

            _diagnostics = Array.AsReadOnly(messages);
            CanCreateGameSource = canCreateGameSource;
            CanEditGameSource = canEditGameSource;
            CanBuildCatalog = canBuildCatalog;
        }

        /// <summary>Merged preview built from a complete valid source set; null otherwise.</summary>
        public GameplayTagWorkspaceSnapshot? Snapshot { get; }

        /// <summary>Edit session that passed syntax and game-only semantic validation; null if the file is missing or invalid.</summary>
        internal GameplayTagCatalogEditSession? GameSession { get; }

        /// <summary>Configuration and source diagnostics that made the workspace incomplete or invalid.</summary>
        public IReadOnlyList<string> Diagnostics => _diagnostics;

        internal IReadOnlyList<GameplayTagWorkspaceDiagnostic> DiagnosticEntries =>
            _diagnosticEntries;

        /// <summary>Whether the fixed game source is absent so a new empty source can be created.</summary>
        public bool CanCreateGameSource { get; }

        /// <summary>Whether authoring mutations can safely be applied to the current game source.</summary>
        public bool CanEditGameSource { get; }

        /// <summary>Whether a runtime catalog can be built from the product-wide sources.</summary>
        public bool CanBuildCatalog { get; }

        internal bool RequiresCatalogConfiguration => _resolution.RequiresCatalogConfiguration;

        /// <summary>Opens a new editor workspace from the build context resolution and the fixed game source.</summary>
        /// <param name="resolution">Provider and external source resolution result.</param>
        /// <param name="gameSourcePath">Fixed absolute path of the game source.</param>
        /// <returns>Workspace that explicitly represents missing and error states.</returns>
        public static GameplayTagEditorWorkspace Open(
            GameplayTagBuildContextResolution resolution,
            string gameSourcePath)
        {
            if (resolution is null) throw new ArgumentNullException(nameof(resolution));
            if (gameSourcePath is null) throw new ArgumentNullException(nameof(gameSourcePath));

            var diagnostics = new List<GameplayTagWorkspaceDiagnostic>(resolution.DiagnosticEntries);
            if (!File.Exists(gameSourcePath))
            {
                diagnostics.Add(new GameplayTagWorkspaceDiagnostic(
                    "B3TAG3101: Game Source is missing: " + gameSourcePath,
                    gameSourcePath));
                return Invalid(
                    resolution,
                    diagnostics,
                    gameSession: null,
                    canCreateGameSource: true);
            }

            TagSourceDocument gameSource;
            try
            {
                gameSource = GameplayTagCatalogFileAdapter.LoadGameSourceDocument(gameSourcePath);
            }
            catch (Exception exception) when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is TagCatalogException)
            {
                diagnostics.Add(new GameplayTagWorkspaceDiagnostic(
                    "B3TAG3102: Invalid Game Source '" + gameSourcePath
                    + "': " + exception.Message,
                    gameSourcePath));
                return Invalid(
                    resolution,
                    diagnostics,
                    gameSession: null,
                    canCreateGameSource: false);
            }

            return Open(resolution, gameSource);
        }

        internal static GameplayTagEditorWorkspace Open(
            GameplayTagBuildContextResolution resolution,
            TagSourceDocument gameSource)
        {
            if (resolution is null) throw new ArgumentNullException(nameof(resolution));
            if (gameSource is null) throw new ArgumentNullException(nameof(gameSource));
            var compileCandidate = CreateCandidateCompiler(resolution);
            return Open(
                resolution,
                gameSource,
                GameplayTagCatalogEditSession.Open(gameSource, compileCandidate));
        }

        internal GameplayTagEditorWorkspace WithGameSession(
            GameplayTagCatalogEditSession gameSession)
        {
            if (gameSession is null) throw new ArgumentNullException(nameof(gameSession));
            return Open(_resolution, gameSession.GameSource, gameSession);
        }

        private static GameplayTagEditorWorkspace Open(
            GameplayTagBuildContextResolution resolution,
            TagSourceDocument gameSource,
            GameplayTagCatalogEditSession gameSession)
        {
            var diagnostics = new List<GameplayTagWorkspaceDiagnostic>(resolution.DiagnosticEntries);

            if (!resolution.HasCompleteContext)
            {
                if (!resolution.PermitsGameOnlyValidation)
                {
                    return Invalid(
                        resolution,
                        diagnostics,
                        gameSession,
                        canCreateGameSource: false);
                }

                var gameOnly = TagCatalogCompiler.Compile(
                    new[] { gameSource },
                    new TagCatalogIdentity("game", TagCatalogVersions.Development));
                AddCompilationDiagnostics(gameOnly.Diagnostics, diagnostics);
                return new GameplayTagEditorWorkspace(
                    resolution,
                    snapshot: null,
                    gameSession,
                    diagnostics.ToArray(),
                    canCreateGameSource: false,
                    canEditGameSource: gameOnly.Succeeded,
                    canBuildCatalog: false);
            }

            var sources = ReplaceGameSource(resolution.Context!.Sources, gameSource);
            var compilation = TagCatalogCompiler.Compile(sources, resolution.Context.Identity);
            AddCompilationDiagnostics(compilation.Diagnostics, diagnostics);
            if (!compilation.Succeeded)
            {
                return Invalid(
                    resolution,
                    diagnostics,
                    gameSession,
                    canCreateGameSource: false);
            }

            var snapshot = new GameplayTagWorkspaceSnapshot(
                compilation.Catalog!,
                compilation.Provenance!,
                sources);
            return new GameplayTagEditorWorkspace(
                resolution,
                snapshot,
                gameSession,
                diagnostics.ToArray(),
                canCreateGameSource: false,
                canEditGameSource: true,
                canBuildCatalog: true);
        }

        private static TagSourceDocument[] ReplaceGameSource(
            IReadOnlyList<TagSourceDocument> sources,
            TagSourceDocument gameSource)
        {
            var result = new TagSourceDocument[sources.Count];
            var replaced = false;
            for (var index = 0; index < result.Length; index++)
            {
                if (sources[index].Descriptor.Kind == TagSourceKind.GameJson)
                {
                    result[index] = gameSource;
                    replaced = true;
                }
                else
                {
                    result[index] = sources[index];
                }
            }

            if (!replaced)
            {
                throw new InvalidOperationException("Complete build context does not contain the Game Source.");
            }

            return result;
        }

        private static Func<TagSourceDocument, TagCatalogCompilation> CreateCandidateCompiler(
            GameplayTagBuildContextResolution resolution)
        {
            if (resolution.HasCompleteContext)
            {
                return candidate => TagCatalogCompiler.Compile(
                    ReplaceGameSource(resolution.Context!.Sources, candidate),
                    resolution.Context.Identity);
            }

            return candidate => TagCatalogCompiler.Compile(
                new[] { candidate },
                new TagCatalogIdentity("game", TagCatalogVersions.Development));
        }

        private static void AddCompilationDiagnostics(
            IReadOnlyList<TagCatalogDiagnostic> source,
            List<GameplayTagWorkspaceDiagnostic> destination)
        {
            for (var index = 0; index < source.Count; index++)
            {
                var diagnostic = source[index];
                var message = diagnostic.Code + ": " + diagnostic.Message
                    + (diagnostic.Origin.Length == 0 ? string.Empty : " [" + diagnostic.Origin + "]");
                destination.Add(new GameplayTagWorkspaceDiagnostic(
                    message,
                    GetKnownLocalSourcePath(diagnostic.Origin)));
            }
        }

        private static string? GetKnownLocalSourcePath(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin) || !Path.IsPathFullyQualified(origin)) return null;
            try
            {
                return Path.GetFullPath(origin);
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is NotSupportedException
                || exception is IOException)
            {
                return null;
            }
        }

        private static GameplayTagEditorWorkspace Invalid(
            GameplayTagBuildContextResolution resolution,
            List<GameplayTagWorkspaceDiagnostic> diagnostics,
            GameplayTagCatalogEditSession? gameSession,
            bool canCreateGameSource) =>
            new GameplayTagEditorWorkspace(
                resolution,
                snapshot: null,
                gameSession,
                diagnostics.ToArray(),
                canCreateGameSource,
                canEditGameSource: false,
                canBuildCatalog: false);
    }
}
