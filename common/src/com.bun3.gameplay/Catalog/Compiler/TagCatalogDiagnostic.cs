#nullable enable
using System;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>태그 카탈로그 컴파일 중 발견한 안정적인 진단입니다.</summary>
    public sealed class TagCatalogDiagnostic
    {
        /// <summary>기계가 판별할 수 있는 안정적인 진단 코드입니다.</summary>
        public string Code { get; }

        /// <summary>진단의 심각도입니다.</summary>
        public TagCatalogDiagnosticSeverity Severity { get; }

        /// <summary>진단과 관련된 Source 식별자이며 전체 Catalog 진단이면 빈 문자열입니다.</summary>
        public string SourceId { get; }

        /// <summary>진단과 관련된 원본 경로 또는 선언 레이블입니다.</summary>
        public string Origin { get; }

        /// <summary>진단과 관련된 canonical 태그 경로입니다.</summary>
        public string CanonicalPath { get; }

        /// <summary>사람이 읽을 수 있는 진단 설명입니다.</summary>
        public string Message { get; }

        internal TagCatalogDiagnostic(
            string code,
            TagCatalogDiagnosticSeverity severity,
            string sourceId,
            string origin,
            string canonicalPath,
            string message)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            Severity = severity;
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            CanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }
    }
}
