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

    /// <summary>Unity Editor의 Game Catalog build context 발견 결과입니다.</summary>
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
                CreateEntries(diagnostics),
                permitsGameOnlyValidation)
        {
        }

        internal GameplayTagBuildContextResolution(
            GameCatalogBuildContext? context,
            GameplayTagWorkspaceDiagnostic[] diagnostics,
            bool permitsGameOnlyValidation)
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
        }

        /// <summary>완전하게 resolve된 경우 제품 전체 개발 build context입니다.</summary>
        public GameCatalogBuildContext? Context { get; }

        /// <summary>발견, 설정 또는 Source 읽기 실패의 안정적인 진단입니다.</summary>
        public IReadOnlyList<string> Diagnostics => _diagnostics;

        internal IReadOnlyList<GameplayTagWorkspaceDiagnostic> DiagnosticEntries =>
            _diagnosticEntries;

        /// <summary>제품 전체 Source를 포함한 context를 사용할 수 있는지 나타냅니다.</summary>
        public bool HasCompleteContext => Context is not null && _diagnostics.Count == 0;

        internal bool PermitsGameOnlyValidation { get; }

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
