#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>완전한 Editor Workspace Source를 컴파일한 불변 미리보기입니다.</summary>
    public sealed class GameplayTagWorkspaceSnapshot
    {
        private readonly IReadOnlyList<TagSourceDocument> _sources;

        internal GameplayTagWorkspaceSnapshot(
            TagCatalog catalog,
            TagCatalogProvenance provenance,
            IReadOnlyList<TagSourceDocument> sources)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            if (sources is null) throw new ArgumentNullException(nameof(sources));
            var copy = new TagSourceDocument[sources.Count];
            for (var index = 0; index < copy.Length; index++)
            {
                copy[index] = sources[index] ?? throw new ArgumentNullException(nameof(sources));
            }

            _sources = Array.AsReadOnly(copy);
        }

        /// <summary>모든 Source를 병합한 불변 Runtime Catalog입니다.</summary>
        public TagCatalog Catalog { get; }

        /// <summary>병합된 태그의 Source별 작성 정보를 제공하는 색인입니다.</summary>
        public TagCatalogProvenance Provenance { get; }

        /// <summary>미리보기를 만든 Game 및 읽기 전용 Source 문서입니다.</summary>
        public IReadOnlyList<TagSourceDocument> Sources => _sources;
    }
}
