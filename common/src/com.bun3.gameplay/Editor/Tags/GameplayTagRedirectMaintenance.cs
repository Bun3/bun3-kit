#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Editor.Tags
{
    internal enum ReferencedRedirectDecision
    {
        OpenReferences,
        Cancel,
        RemoveAnyway
    }

    internal static class GameplayTagRedirectMaintenance
    {
        /// <summary>에디터가 검사하지 못한 데이터 범위를 알리는 경고 문구입니다.</summary>
        internal const string ExternalDataScopeWarning =
            "Save data, server configuration, external files, and already deployed builds " +
            "were not scanned. Directories the editor could not read are skipped silently.";

        /// <summary>프로젝트 참조가 없는 redirect를 제거하기 전에 보여 주는 경고 문구입니다.</summary>
        internal const string NoProjectReferencesWarning =
            "No project references were found. " + ExternalDataScopeWarning;

        /// <summary>완전한 검색 결과에서 프로젝트 참조가 없는 redirect old path만 순서대로 고릅니다.</summary>
        /// <param name="redirects">현재 세션의 redirect 행입니다.</param>
        /// <param name="result">완전하게 끝난 참조 검색 결과입니다.</param>
        internal static IReadOnlyList<string> GetUnreferencedSources(
            IReadOnlyList<EditableRedirectRow> redirects,
            GameplayTagReferenceSearchResult result)
        {
            if (redirects is null) throw new ArgumentNullException(nameof(redirects));
            if (!result.IsComplete)
            {
                throw new InvalidOperationException(
                    "An incomplete gameplay tag reference scan cannot produce cleanup candidates.");
            }

            var matches = result.Matches;
            var referenced = new HashSet<string>(matches.Count, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < matches.Count; index++)
            {
                referenced.Add(matches[index].RedirectSource);
            }

            var sources = new List<string>(redirects.Count);
            for (var index = 0; index < redirects.Count; index++)
            {
                var source = redirects[index].From;
                if (!referenced.Contains(source)) sources.Add(source);
            }

            return sources;
        }

        /// <summary>참조가 있는 redirect 확인 dialog의 버튼 index를 명시적 결정으로 변환합니다.</summary>
        /// <param name="result"><c>DisplayDialogComplex</c>가 반환한 버튼 index입니다.</param>
        internal static ReferencedRedirectDecision MapReferencedDialogResult(int result) => result switch
        {
            0 => ReferencedRedirectDecision.OpenReferences,
            1 => ReferencedRedirectDecision.Cancel,
            2 => ReferencedRedirectDecision.RemoveAnyway,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
    }
}
