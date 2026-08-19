#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    internal readonly struct GameplayTagTreeSelectionKey : IEquatable<GameplayTagTreeSelectionKey>
    {
        internal GameplayTagTreeSelectionKey(string sourceId, string canonicalPath)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            CanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
        }

        internal string SourceId { get; }
        internal string CanonicalPath { get; }

        public bool Equals(GameplayTagTreeSelectionKey other) =>
            string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
            && string.Equals(CanonicalPath, other.CanonicalPath, StringComparison.Ordinal);

        public override bool Equals(object? obj) =>
            obj is GameplayTagTreeSelectionKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(SourceId) * 397)
                    ^ StringComparer.Ordinal.GetHashCode(CanonicalPath);
            }
        }
    }

    internal readonly struct GameplayTagTreeRowModel : IGameplayTagProjectionRow
    {
        internal GameplayTagTreeRowModel(
            int id,
            int parentId,
            ushort runtimeIndex,
            string sourceId,
            string displayName,
            string path,
            string comment,
            bool isSourceRoot,
            bool isExplicit,
            bool isReadOnly,
            bool directMatch)
        {
            Id = id;
            ParentId = parentId;
            RuntimeIndex = runtimeIndex;
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Comment = comment ?? throw new ArgumentNullException(nameof(comment));
            IsSourceRoot = isSourceRoot;
            IsExplicit = isExplicit;
            IsReadOnly = isReadOnly;
            IsDirectMatch = directMatch;
        }

        internal int Id { get; }
        internal int ParentId { get; }
        internal ushort RuntimeIndex { get; }
        internal string SourceId { get; }
        internal string DisplayName { get; }
        internal string Path { get; }
        internal string Comment { get; }
        internal bool IsSourceRoot { get; }
        internal bool IsExplicit { get; }
        internal bool IsReadOnly { get; }
        internal bool IsDirectMatch { get; }
        internal string DisplaySegment => DisplayName;
        int IGameplayTagProjectionRow.Id => Id;
        int IGameplayTagProjectionRow.ParentId => ParentId;
        string IGameplayTagProjectionRow.DisplaySegment => DisplaySegment;
        internal GameplayTagTreeSelectionKey SelectionKey =>
            new GameplayTagTreeSelectionKey(SourceId, Path);

        // Kept so the shared TreeView renderer can use the same shape for source and merged projections.
        internal int Index => Id;
        internal int ParentIndex => ParentId;
    }

    internal sealed class GameplayTagTreeModel
    {
        private readonly GameplayTagTreeRowModel[] _rows;

        internal GameplayTagTreeModel(GameplayTagWorkspaceSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            Snapshot = snapshot;
            _rows = CreateRows(snapshot);
            ActiveCount = snapshot.Catalog.Count;
            FingerprintPrefix = FormatFingerprintPrefix(snapshot.Catalog.Fingerprint);
        }

        internal GameplayTagTreeModel(GameplayTagCatalogEditSession session)
            : this(CreateGameOnlySnapshot(session))
        {
        }

        internal IReadOnlyList<GameplayTagTreeRowModel> Rows => _rows;
        internal GameplayTagWorkspaceSnapshot Snapshot { get; }
        internal int ActiveCount { get; }
        internal string FingerprintPrefix { get; }

        internal IReadOnlyList<GameplayTagTreeRowModel> Filter(string search)
        {
            if (search is null) throw new ArgumentNullException(nameof(search));
            if (search.Length == 0) return _rows;

            var included = new bool[_rows.Length + 1];
            var directMatches = new bool[_rows.Length + 1];
            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (row.IsSourceRoot
                    || row.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                directMatches[row.Id] = true;
                var current = row.Id;
                while (current != 0)
                {
                    included[current] = true;
                    current = _rows[current - 1].ParentId;
                }
            }

            var results = new List<GameplayTagTreeRowModel>();
            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (!included[row.Id]) continue;
                results.Add(new GameplayTagTreeRowModel(
                    row.Id,
                    row.ParentId,
                    row.RuntimeIndex,
                    row.SourceId,
                    row.DisplayName,
                    row.Path,
                    row.Comment,
                    row.IsSourceRoot,
                    row.IsExplicit,
                    row.IsReadOnly,
                    directMatches[row.Id]));
            }

            return results;
        }

        private static GameplayTagTreeRowModel[] CreateRows(GameplayTagWorkspaceSnapshot snapshot)
        {
            var sources = new TagSourceDocument[snapshot.Sources.Count];
            for (var index = 0; index < sources.Length; index++) sources[index] = snapshot.Sources[index];
            Array.Sort(
                sources,
                (left, right) => StringComparer.Ordinal.Compare(
                    left.Descriptor.SourceId,
                    right.Descriptor.SourceId));

            var rows = new List<GameplayTagTreeRowModel>(snapshot.Catalog.Count + sources.Length);
            for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                var descriptor = sources[sourceIndex].Descriptor;
                var rootId = rows.Count + 1;
                rows.Add(new GameplayTagTreeRowModel(
                    rootId,
                    parentId: 0,
                    runtimeIndex: 0,
                    descriptor.SourceId,
                    descriptor.DisplayName,
                    path: string.Empty,
                    comment: string.Empty,
                    isSourceRoot: true,
                    isExplicit: false,
                    descriptor.IsReadOnly,
                    directMatch: false));

                var idsByPath = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var runtimeIndex = 1; runtimeIndex <= snapshot.Catalog.Count; runtimeIndex++)
                {
                    var tag = snapshot.Catalog.GetRequiredByIndex(checked((ushort)runtimeIndex));
                    var path = snapshot.Catalog.GetDisplayName(tag);
                    var contributions = snapshot.Provenance.GetContributions(path);
                    TagSourceContribution? contribution = null;
                    for (var contributionIndex = 0;
                        contributionIndex < contributions.Count;
                        contributionIndex++)
                    {
                        if (string.Equals(
                                contributions[contributionIndex].SourceId,
                                descriptor.SourceId,
                                StringComparison.Ordinal))
                        {
                            contribution = contributions[contributionIndex];
                            break;
                        }
                    }

                    if (contribution is null) continue;
                    var lastDot = path.LastIndexOf('.');
                    var parentId = lastDot < 0
                        ? rootId
                        : idsByPath[path.Substring(0, lastDot)];
                    var id = rows.Count + 1;
                    idsByPath.Add(path, id);
                    rows.Add(new GameplayTagTreeRowModel(
                        id,
                        parentId,
                        tag.Index,
                        descriptor.SourceId,
                        path.Substring(lastDot + 1),
                        path,
                        contribution.Comment,
                        isSourceRoot: false,
                        contribution.IsExplicit,
                        contribution.IsReadOnly,
                        directMatch: false));
                }
            }

            return rows.ToArray();
        }

        private static GameplayTagWorkspaceSnapshot CreateGameOnlySnapshot(
            GameplayTagCatalogEditSession session)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));
            var sources = new[] { session.GameSource };
            var compilation = TagCatalogCompiler.Compile(
                sources,
                new TagCatalogIdentity("game", TagCatalogVersions.Development));
            if (!compilation.Succeeded)
            {
                throw new InvalidOperationException("The Game Source cannot be projected as a tree.");
            }

            return new GameplayTagWorkspaceSnapshot(
                compilation.Catalog!, compilation.Provenance!, sources);
        }

        private static string FormatFingerprintPrefix(ReadOnlySpan<byte> fingerprint)
        {
            var length = Math.Min(8, fingerprint.Length);
            var builder = new StringBuilder(length * 2);
            for (var index = 0; index < length; index++)
            {
                builder.Append(fingerprint[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
