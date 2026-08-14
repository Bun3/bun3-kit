#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>여러 태그 Source를 하나의 결정적인 런타임 Catalog와 provenance로 병합합니다.</summary>
    public static class TagCatalogCompiler
    {
        private const string DuplicateSourceCode = "B3TAG1001";
        private const string CapacityCode = "B3TAG1002";
        private const string ShadowedRedirectCode = "B3TAG2001";
        private const string ConflictingRedirectCode = "B3TAG2002";
        private const string RedirectCycleCode = "B3TAG2003";
        private const string MissingRedirectTargetCode = "B3TAG2004";

        /// <summary>Source 목록을 canonical runtime Catalog, provenance와 안정적인 진단으로 컴파일합니다.</summary>
        /// <param name="sources">제품 전체에서 resolve한 태그 Source 목록입니다.</param>
        /// <param name="identity">결과 Catalog의 제품 ID와 명시적인 Version입니다.</param>
        /// <returns>성공 결과 또는 작성 오류 진단입니다.</returns>
        /// <exception cref="ArgumentNullException">입력 목록, 목록 항목 또는 식별 정보가 null인 경우입니다.</exception>
        public static TagCatalogCompilation Compile(
            IReadOnlyList<TagSourceDocument> sources,
            TagCatalogIdentity identity)
        {
            if (sources is null) throw new ArgumentNullException(nameof(sources));
            if (identity is null) throw new ArgumentNullException(nameof(identity));

            var orderedSources = new TagSourceDocument[sources.Count];
            for (var i = 0; i < orderedSources.Length; i++)
            {
                orderedSources[i] = sources[i] ?? throw new ArgumentNullException(nameof(sources));
            }

            Array.Sort(orderedSources, CompareSources);
            var diagnostics = new List<TagCatalogDiagnostic>();
            ValidateUniqueSourceIds(orderedSources, diagnostics);
            if (HasErrors(diagnostics)) return Failure(diagnostics);

            var activeNames = new HashSet<string>(StringComparer.Ordinal);
            var provenanceLists = new Dictionary<string, List<TagSourceContribution>>(StringComparer.Ordinal);
            BuildTagUnion(orderedSources, activeNames, provenanceLists);
            if (activeNames.Count > ushort.MaxValue)
            {
                diagnostics.Add(new TagCatalogDiagnostic(
                    CapacityCode,
                    TagCatalogDiagnosticSeverity.Error,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "Runtime tag capacity exceeds 65,535"));
                return Failure(diagnostics);
            }

            var canonicalNames = BuildCanonicalNames(activeNames);
            var parents = BuildParents(canonicalNames);
            var subtreeEnds = BuildSubtreeEnds(parents);
            var redirectDeclarations = BuildRedirectDeclarations(orderedSources);
            var redirects = MergeAndFlattenRedirects(
                redirectDeclarations,
                activeNames,
                diagnostics);
            if (HasErrors(diagnostics)) return Failure(diagnostics);

            SortDiagnostics(diagnostics);
            var catalog = TagCatalog.CreateCompiled(identity, canonicalNames, parents, subtreeEnds, redirects);
            var provenance = CreateProvenance(provenanceLists);
            return new TagCatalogCompilation(catalog, provenance, diagnostics.ToArray());
        }

        private static int CompareSources(TagSourceDocument left, TagSourceDocument right)
        {
            var comparison = StringComparer.Ordinal.Compare(left.Descriptor.SourceId, right.Descriptor.SourceId);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(left.Origin, right.Origin);
            if (comparison != 0) return comparison;
            return StringComparer.Ordinal.Compare(left.Descriptor.DisplayName, right.Descriptor.DisplayName);
        }

        private static void ValidateUniqueSourceIds(
            TagSourceDocument[] sources,
            List<TagCatalogDiagnostic> diagnostics)
        {
            var index = 0;
            while (index < sources.Length)
            {
                var end = index + 1;
                var sourceId = sources[index].Descriptor.SourceId;
                while (end < sources.Length
                    && string.Equals(sourceId, sources[end].Descriptor.SourceId, StringComparison.Ordinal))
                {
                    end++;
                }

                if (end - index > 1)
                {
                    for (var duplicate = index; duplicate < end; duplicate++)
                    {
                        diagnostics.Add(new TagCatalogDiagnostic(
                            DuplicateSourceCode,
                            TagCatalogDiagnosticSeverity.Error,
                            sourceId,
                            sources[duplicate].Origin,
                            string.Empty,
                            "Duplicate Source ID"));
                    }
                }

                index = end;
            }
        }

        private static void BuildTagUnion(
            TagSourceDocument[] sources,
            HashSet<string> activeNames,
            Dictionary<string, List<TagSourceContribution>> provenanceLists)
        {
            for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                var perSource = new Dictionary<string, ContributionCandidate>(StringComparer.Ordinal);
                for (var tagIndex = 0; tagIndex < source.Tags.Count; tagIndex++)
                {
                    var tag = source.Tags[tagIndex];
                    AddTagAndParents(tag, activeNames, perSource);
                }

                var paths = new string[perSource.Count];
                perSource.Keys.CopyTo(paths, 0);
                Array.Sort(paths, StringComparer.Ordinal);
                for (var pathIndex = 0; pathIndex < paths.Length; pathIndex++)
                {
                    var path = paths[pathIndex];
                    var candidate = perSource[path];
                    if (!provenanceLists.TryGetValue(path, out var contributions))
                    {
                        contributions = new List<TagSourceContribution>();
                        provenanceLists.Add(path, contributions);
                    }

                    contributions.Add(new TagSourceContribution(
                        source.Descriptor.SourceId,
                        source.Descriptor.DisplayName,
                        source.Origin,
                        candidate.Comment,
                        candidate.IsExplicit,
                        source.Descriptor.IsReadOnly));
                }
            }
        }

        private static void AddTagAndParents(
            TagSourceTag tag,
            HashSet<string> activeNames,
            Dictionary<string, ContributionCandidate> perSource)
        {
            var start = 0;
            while (true)
            {
                var dot = tag.Name.IndexOf('.', start);
                var length = dot < 0 ? tag.Name.Length : dot;
                var path = tag.Name.Substring(0, length);
                activeNames.Add(path);
                if (dot < 0)
                {
                    if (!perSource.TryGetValue(path, out var existing)
                        || !existing.IsExplicit
                        || StringComparer.Ordinal.Compare(tag.Comment, existing.Comment) < 0)
                    {
                        perSource[path] = new ContributionCandidate(true, tag.Comment);
                    }

                    return;
                }

                if (!perSource.ContainsKey(path))
                {
                    perSource.Add(path, new ContributionCandidate(false, string.Empty));
                }

                start = dot + 1;
            }
        }

        private static string[] BuildCanonicalNames(HashSet<string> activeNames)
        {
            var sorted = new string[activeNames.Count];
            activeNames.CopyTo(sorted);
            Array.Sort(sorted, StringComparer.Ordinal);
            var canonicalNames = new string[sorted.Length + 1];
            canonicalNames[0] = string.Empty;
            Array.Copy(sorted, 0, canonicalNames, 1, sorted.Length);
            return canonicalNames;
        }

        private static ushort[] BuildParents(string[] canonicalNames)
        {
            var indices = new Dictionary<string, ushort>(canonicalNames.Length - 1, StringComparer.Ordinal);
            var parents = new ushort[canonicalNames.Length];
            for (var index = 1; index < canonicalNames.Length; index++)
            {
                indices.Add(canonicalNames[index], checked((ushort)index));
                var lastDot = canonicalNames[index].LastIndexOf('.');
                if (lastDot >= 0)
                {
                    parents[index] = indices[canonicalNames[index].Substring(0, lastDot)];
                }
            }

            return parents;
        }

        private static ushort[] BuildSubtreeEnds(ushort[] parents)
        {
            var subtreeEnds = new ushort[parents.Length];
            for (var index = 1; index < subtreeEnds.Length; index++)
            {
                subtreeEnds[index] = checked((ushort)index);
            }

            for (var index = subtreeEnds.Length - 1; index > 0; index--)
            {
                var parent = parents[index];
                if (parent != 0 && subtreeEnds[index] > subtreeEnds[parent])
                {
                    subtreeEnds[parent] = subtreeEnds[index];
                }
            }

            return subtreeEnds;
        }

        private static Dictionary<string, List<RedirectDeclaration>> BuildRedirectDeclarations(
            TagSourceDocument[] sources)
        {
            var byFrom = new Dictionary<string, List<RedirectDeclaration>>(StringComparer.Ordinal);
            for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                for (var redirectIndex = 0; redirectIndex < source.Redirects.Count; redirectIndex++)
                {
                    var redirect = source.Redirects[redirectIndex];
                    if (!byFrom.TryGetValue(redirect.From, out var declarations))
                    {
                        declarations = new List<RedirectDeclaration>();
                        byFrom.Add(redirect.From, declarations);
                    }

                    declarations.Add(new RedirectDeclaration(source, redirect.From, redirect.To));
                }
            }

            return byFrom;
        }

        private static CompiledRedirect[] MergeAndFlattenRedirects(
            Dictionary<string, List<RedirectDeclaration>> declarationsByFrom,
            HashSet<string> activeNames,
            List<TagCatalogDiagnostic> diagnostics)
        {
            var fromNames = new string[declarationsByFrom.Count];
            declarationsByFrom.Keys.CopyTo(fromNames, 0);
            Array.Sort(fromNames, StringComparer.Ordinal);
            var mergedTargets = new Dictionary<string, string>(fromNames.Length, StringComparer.Ordinal);

            for (var fromIndex = 0; fromIndex < fromNames.Length; fromIndex++)
            {
                var from = fromNames[fromIndex];
                var declarations = declarationsByFrom[from];
                var distinctTargets = new HashSet<string>(StringComparer.Ordinal);
                for (var declarationIndex = 0; declarationIndex < declarations.Count; declarationIndex++)
                {
                    distinctTargets.Add(declarations[declarationIndex].To);
                }

                if (distinctTargets.Count != 1)
                {
                    AddDiagnosticsForDeclarations(
                        diagnostics,
                        declarations,
                        ConflictingRedirectCode,
                        TagCatalogDiagnosticSeverity.Error,
                        "Conflicting targets for one redirect source");
                    continue;
                }

                foreach (var target in distinctTargets)
                {
                    mergedTargets.Add(from, target);
                }

                if (activeNames.Contains(from))
                {
                    AddDiagnosticsForDeclarations(
                        diagnostics,
                        declarations,
                        ShadowedRedirectCode,
                        TagCatalogDiagnosticSeverity.Warning,
                        "Redirect is shadowed by an active old name");
                }
            }

            if (HasErrors(diagnostics)) return Array.Empty<CompiledRedirect>();

            var resolvedTargets = new Dictionary<string, string>(fromNames.Length, StringComparer.Ordinal);
            for (var fromIndex = 0; fromIndex < fromNames.Length; fromIndex++)
            {
                var from = fromNames[fromIndex];
                if (TryResolveRedirect(from, mergedTargets, activeNames, resolvedTargets, out var finalTarget, out var cycle))
                {
                    resolvedTargets[from] = finalTarget;
                    continue;
                }

                AddDiagnosticsForDeclarations(
                    diagnostics,
                    declarationsByFrom[from],
                    cycle ? RedirectCycleCode : MissingRedirectTargetCode,
                    TagCatalogDiagnosticSeverity.Error,
                    cycle ? "Redirect self-reference or cycle" : "Redirect target is not active");
            }

            if (HasErrors(diagnostics)) return Array.Empty<CompiledRedirect>();

            var redirects = new CompiledRedirect[fromNames.Length];
            for (var index = 0; index < redirects.Length; index++)
            {
                redirects[index] = new CompiledRedirect(fromNames[index], resolvedTargets[fromNames[index]]);
            }

            return redirects;
        }

        private static bool TryResolveRedirect(
            string from,
            Dictionary<string, string> mergedTargets,
            HashSet<string> activeNames,
            Dictionary<string, string> resolvedTargets,
            out string finalTarget,
            out bool cycle)
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var current = from;
            while (true)
            {
                if (resolvedTargets.TryGetValue(current, out finalTarget))
                {
                    cycle = false;
                    CacheResolvedPath(path, finalTarget, resolvedTargets);
                    return true;
                }

                if (positions.ContainsKey(current))
                {
                    finalTarget = string.Empty;
                    cycle = true;
                    return false;
                }

                positions.Add(current, path.Count);
                path.Add(current);
                if (!mergedTargets.TryGetValue(current, out var target))
                {
                    finalTarget = string.Empty;
                    cycle = false;
                    return false;
                }

                if (activeNames.Contains(target))
                {
                    finalTarget = target;
                    cycle = false;
                    CacheResolvedPath(path, finalTarget, resolvedTargets);
                    return true;
                }

                current = target;
            }
        }

        private static void CacheResolvedPath(
            List<string> path,
            string finalTarget,
            Dictionary<string, string> resolvedTargets)
        {
            for (var index = 0; index < path.Count; index++)
            {
                resolvedTargets[path[index]] = finalTarget;
            }
        }

        private static void AddDiagnosticsForDeclarations(
            List<TagCatalogDiagnostic> diagnostics,
            List<RedirectDeclaration> declarations,
            string code,
            TagCatalogDiagnosticSeverity severity,
            string message)
        {
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < declarations.Count; index++)
            {
                var declaration = declarations[index];
                if (!sourceIds.Add(declaration.Source.Descriptor.SourceId)) continue;
                diagnostics.Add(new TagCatalogDiagnostic(
                    code,
                    severity,
                    declaration.Source.Descriptor.SourceId,
                    declaration.Source.Origin,
                    declaration.From,
                    message));
            }
        }

        private static TagCatalogProvenance CreateProvenance(
            Dictionary<string, List<TagSourceContribution>> lists)
        {
            var entries = new Dictionary<string, IReadOnlyList<TagSourceContribution>>(lists.Count, StringComparer.Ordinal);
            foreach (var pair in lists)
            {
                entries.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
            }

            return new TagCatalogProvenance(entries);
        }

        private static bool HasErrors(List<TagCatalogDiagnostic> diagnostics)
        {
            for (var i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == TagCatalogDiagnosticSeverity.Error) return true;
            }

            return false;
        }

        private static TagCatalogCompilation Failure(List<TagCatalogDiagnostic> diagnostics)
        {
            SortDiagnostics(diagnostics);
            return new TagCatalogCompilation(null, null, diagnostics.ToArray());
        }

        private static void SortDiagnostics(List<TagCatalogDiagnostic> diagnostics) =>
            diagnostics.Sort(CompareDiagnostics);

        private static int CompareDiagnostics(TagCatalogDiagnostic left, TagCatalogDiagnostic right)
        {
            var comparison = StringComparer.Ordinal.Compare(left.SourceId, right.SourceId);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(left.CanonicalPath, right.CanonicalPath);
            if (comparison != 0) return comparison;
            comparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
            if (comparison != 0) return comparison;
            return StringComparer.Ordinal.Compare(left.Origin, right.Origin);
        }

        private readonly struct ContributionCandidate
        {
            internal ContributionCandidate(bool isExplicit, string comment)
            {
                IsExplicit = isExplicit;
                Comment = comment;
            }

            internal bool IsExplicit { get; }
            internal string Comment { get; }
        }

        private sealed class RedirectDeclaration
        {
            internal RedirectDeclaration(TagSourceDocument source, string from, string to)
            {
                Source = source;
                From = from;
                To = to;
            }

            internal TagSourceDocument Source { get; }
            internal string From { get; }
            internal string To { get; }
        }
    }
}
