#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>태그 Source 병합의 런타임 결과, provenance와 진단을 묶습니다.</summary>
    public sealed class TagCatalogCompilation
    {
        private readonly IReadOnlyList<TagCatalogDiagnostic> _diagnostics;

        internal TagCatalogCompilation(
            TagCatalog? catalog,
            TagCatalogProvenance? provenance,
            TagCatalogDiagnostic[] diagnostics)
        {
            Catalog = catalog;
            Provenance = provenance;
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            _diagnostics = Array.AsReadOnly((TagCatalogDiagnostic[])diagnostics.Clone());
            Succeeded = catalog is not null && provenance is not null;
        }

        /// <summary>오류 없이 런타임 Catalog와 provenance가 생성되었는지 나타냅니다.</summary>
        public bool Succeeded { get; }

        /// <summary>성공한 경우 생성된 불변 런타임 Catalog입니다.</summary>
        public TagCatalog? Catalog { get; }

        /// <summary>성공한 경우 생성된 불변 Source provenance 색인입니다.</summary>
        public TagCatalogProvenance? Provenance { get; }

        /// <summary>Source와 canonical 경로 순으로 정렬된 컴파일 진단입니다.</summary>
        public IReadOnlyList<TagCatalogDiagnostic> Diagnostics => _diagnostics;
    }
}
