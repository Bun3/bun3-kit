#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Unity Editor의 Game Catalog build context 발견 결과입니다.</summary>
    public sealed class GameplayTagBuildContextResolution
    {
        private readonly IReadOnlyList<string> _diagnostics;

        internal GameplayTagBuildContextResolution(
            GameCatalogBuildContext? context,
            string[] diagnostics,
            bool permitsGameOnlyValidation)
        {
            Context = context;
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics = Array.AsReadOnly((string[])diagnostics.Clone());
            PermitsGameOnlyValidation = permitsGameOnlyValidation;
        }

        /// <summary>완전하게 resolve된 경우 제품 전체 개발 build context입니다.</summary>
        public GameCatalogBuildContext? Context { get; }

        /// <summary>발견, 설정 또는 Source 읽기 실패의 안정적인 진단입니다.</summary>
        public IReadOnlyList<string> Diagnostics => _diagnostics;

        /// <summary>제품 전체 Source를 포함한 context를 사용할 수 있는지 나타냅니다.</summary>
        public bool HasCompleteContext => Context is not null && _diagnostics.Count == 0;

        internal bool PermitsGameOnlyValidation { get; }
    }
}
