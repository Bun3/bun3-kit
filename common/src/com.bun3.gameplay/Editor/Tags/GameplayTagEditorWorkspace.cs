#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>고정 Game Source와 resolve된 외부 Source의 Editor 작성 상태입니다.</summary>
    public sealed class GameplayTagEditorWorkspace
    {
        private readonly IReadOnlyList<string> _diagnostics;
        private readonly GameplayTagBuildContextResolution _resolution;

        private GameplayTagEditorWorkspace(
            GameplayTagBuildContextResolution resolution,
            GameplayTagWorkspaceSnapshot? snapshot,
            GameplayTagCatalogEditSession? gameSession,
            string[] diagnostics,
            bool canCreateGameSource,
            bool canEditGameSource,
            bool canBuildCatalog)
        {
            _resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
            Snapshot = snapshot;
            GameSession = gameSession;
            _diagnostics = Array.AsReadOnly((string[])diagnostics.Clone());
            CanCreateGameSource = canCreateGameSource;
            CanEditGameSource = canEditGameSource;
            CanBuildCatalog = canBuildCatalog;
        }

        /// <summary>완전하고 유효한 Source set에서 만든 병합 미리보기이며 아니면 null입니다.</summary>
        public GameplayTagWorkspaceSnapshot? Snapshot { get; }

        /// <summary>구문과 Game-only 의미 검증을 통과한 편집 session이며 파일이 없거나 잘못되면 null입니다.</summary>
        internal GameplayTagCatalogEditSession? GameSession { get; }

        /// <summary>Workspace를 불완전하거나 잘못되게 만든 설정과 Source 진단입니다.</summary>
        public IReadOnlyList<string> Diagnostics => _diagnostics;

        /// <summary>고정 Game Source가 없어 새 빈 Source를 만들 수 있는지 나타냅니다.</summary>
        public bool CanCreateGameSource { get; }

        /// <summary>현재 Game Source에 안전하게 authoring mutation을 적용할 수 있는지 나타냅니다.</summary>
        public bool CanEditGameSource { get; }

        /// <summary>제품 전체 Source로 Runtime Catalog를 만들 수 있는지 나타냅니다.</summary>
        public bool CanBuildCatalog { get; }

        /// <summary>build context resolution과 고정 Game Source에서 새 Editor Workspace를 엽니다.</summary>
        /// <param name="resolution">provider 및 외부 Source resolve 결과입니다.</param>
        /// <param name="gameSourcePath">고정 Game Source 절대 경로입니다.</param>
        /// <returns>누락 또는 오류 상태까지 명시적으로 표현하는 Workspace입니다.</returns>
        public static GameplayTagEditorWorkspace Open(
            GameplayTagBuildContextResolution resolution,
            string gameSourcePath)
        {
            if (resolution is null) throw new ArgumentNullException(nameof(resolution));
            if (gameSourcePath is null) throw new ArgumentNullException(nameof(gameSourcePath));

            var diagnostics = new List<string>(resolution.Diagnostics);
            if (!File.Exists(gameSourcePath))
            {
                diagnostics.Add("B3TAG3101: Game Source is missing: " + gameSourcePath);
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
                diagnostics.Add("B3TAG3102: Invalid Game Source '" + gameSourcePath
                    + "': " + exception.Message);
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
            var diagnostics = new List<string>(resolution.Diagnostics);

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
                    new TagCatalogIdentity("game", "0.0.0-dev"));
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
                new TagCatalogIdentity("game", "0.0.0-dev"));
        }

        private static void AddCompilationDiagnostics(
            IReadOnlyList<TagCatalogDiagnostic> source,
            List<string> destination)
        {
            for (var index = 0; index < source.Count; index++)
            {
                var diagnostic = source[index];
                destination.Add(diagnostic.Code + ": " + diagnostic.Message
                    + (diagnostic.Origin.Length == 0 ? string.Empty : " [" + diagnostic.Origin + "]"));
            }
        }

        private static GameplayTagEditorWorkspace Invalid(
            GameplayTagBuildContextResolution resolution,
            List<string> diagnostics,
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
