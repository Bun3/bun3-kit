#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    internal enum ReferencedRedirectDecision
    {
        OpenReferences,
        Cancel,
        RemoveAnyway
    }

    internal readonly struct GameplayTagRedirectRowModel
    {
        internal GameplayTagRedirectRowModel(
            string sourceId,
            string sourceDisplayName,
            string from,
            string to,
            bool isReadOnly,
            bool isShadowed)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            SourceDisplayName = sourceDisplayName ?? throw new ArgumentNullException(nameof(sourceDisplayName));
            From = from ?? throw new ArgumentNullException(nameof(from));
            To = to ?? throw new ArgumentNullException(nameof(to));
            IsReadOnly = isReadOnly;
            IsShadowed = isShadowed;
        }

        internal string SourceId { get; }
        internal string SourceDisplayName { get; }
        internal string From { get; }
        internal string To { get; }
        internal bool IsReadOnly { get; }
        internal bool IsShadowed { get; }
    }

    internal static class GameplayTagRedirectMaintenance
    {
        /// <summary>Warning text describing the data scope the editor could not inspect.</summary>
        internal const string ExternalDataScopeWarning =
            "Save data, server configuration, external files, and already deployed builds " +
            "were not scanned. Directories the editor could not read are skipped silently.";

        /// <summary>Warning text shown before removing redirects without project references.</summary>
        internal const string NoProjectReferencesWarning =
            "No project references were found. " + ExternalDataScopeWarning;

        internal const string ShadowedRedirectWarning =
            "This redirect is shadowed. During lookup, the active tag takes priority over the redirect.";

        /// <summary>Projects workspace redirects into authoring rows ordered by source ID and old path.</summary>
        internal static IReadOnlyList<GameplayTagRedirectRowModel> CreateRows(
            GameplayTagWorkspaceSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            var sources = new TagSourceDocument[snapshot.Sources.Count];
            for (var index = 0; index < sources.Length; index++) sources[index] = snapshot.Sources[index];
            Array.Sort(
                sources,
                (left, right) => StringComparer.Ordinal.Compare(
                    left.Descriptor.SourceId,
                    right.Descriptor.SourceId));

            var rows = new List<GameplayTagRedirectRowModel>();
            for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var source = sources[sourceIndex];
                var redirects = new TagSourceRedirect[source.Redirects.Count];
                for (var index = 0; index < redirects.Length; index++) redirects[index] = source.Redirects[index];
                Array.Sort(redirects, CompareRedirects);
                for (var redirectIndex = 0; redirectIndex < redirects.Length; redirectIndex++)
                {
                    var redirect = redirects[redirectIndex];
                    rows.Add(new GameplayTagRedirectRowModel(
                        source.Descriptor.SourceId,
                        source.Descriptor.DisplayName,
                        redirect.From,
                        redirect.To,
                        source.Descriptor.IsReadOnly,
                        snapshot.Provenance.GetContributions(redirect.From).Count > 0));
                }
            }

            return rows;
        }

        /// <summary>Picks, in order, only redirect old paths without project references from a complete search result.</summary>
        /// <param name="redirects">Redirect rows of the current session.</param>
        /// <param name="result">Reference search result that completed fully.</param>
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

        /// <summary>Picks only unreferenced redirect old paths of editable sources from a complete search result.</summary>
        internal static IReadOnlyList<string> GetUnreferencedSources(
            IReadOnlyList<GameplayTagRedirectRowModel> redirects,
            GameplayTagReferenceSearchResult result)
        {
            if (redirects is null) throw new ArgumentNullException(nameof(redirects));
            if (!result.IsComplete)
            {
                throw new InvalidOperationException(
                    "An incomplete gameplay tag reference scan cannot produce cleanup candidates.");
            }

            var referenced = new HashSet<string>(result.Matches.Count, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < result.Matches.Count; index++)
            {
                referenced.Add(result.Matches[index].RedirectSource);
            }

            var sources = new List<string>(redirects.Count);
            for (var index = 0; index < redirects.Count; index++)
            {
                var redirect = redirects[index];
                if (!redirect.IsReadOnly && !referenced.Contains(redirect.From)) sources.Add(redirect.From);
            }

            return sources;
        }

        /// <summary>Converts the referenced-redirect confirmation dialog's button index into an explicit decision.</summary>
        /// <param name="result">Button index returned by <c>DisplayDialogComplex</c>.</param>
        internal static ReferencedRedirectDecision MapReferencedDialogResult(int result) => result switch
        {
            0 => ReferencedRedirectDecision.OpenReferences,
            1 => ReferencedRedirectDecision.Cancel,
            2 => ReferencedRedirectDecision.RemoveAnyway,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

        private static int CompareRedirects(TagSourceRedirect left, TagSourceRedirect right)
        {
            var from = StringComparer.Ordinal.Compare(left.From, right.From);
            return from != 0 ? from : StringComparer.Ordinal.Compare(left.To, right.To);
        }
    }
}
