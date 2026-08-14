#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagPickerModel
    {
        private readonly GameplayTagPickerRow[] _rows;

        internal GameplayTagPickerModel(GameplayTagWorkspaceSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _rows = CreateRows(snapshot);
        }

        internal GameplayTagWorkspaceSnapshot Snapshot { get; }
        internal IReadOnlyList<GameplayTagPickerRow> Rows => _rows;

        internal IReadOnlyList<GameplayTagPickerRow> Filter(string search)
        {
            if (search is null) throw new ArgumentNullException(nameof(search));
            if (search.Length == 0) return _rows;

            var included = new bool[_rows.Length + 1];
            var directMatches = new bool[_rows.Length + 1];
            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (row.CanonicalPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                directMatches[row.Id] = true;
                for (var current = row.Id; current != 0; current = _rows[current - 1].ParentId)
                {
                    included[current] = true;
                }
            }

            var results = new List<GameplayTagPickerRow>();
            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (included[row.Id]) results.Add(row.WithDirectMatch(directMatches[row.Id]));
            }

            return results;
        }

        private static GameplayTagPickerRow[] CreateRows(GameplayTagWorkspaceSnapshot snapshot)
        {
            var rows = new GameplayTagPickerRow[snapshot.Catalog.Count];
            for (var runtimeIndex = 1; runtimeIndex <= snapshot.Catalog.Count; runtimeIndex++)
            {
                var tag = snapshot.Catalog.GetRequiredByIndex(checked((ushort)runtimeIndex));
                var path = snapshot.Catalog.GetDisplayName(tag);
                var parent = snapshot.Catalog.GetParent(tag);
                var contributions = snapshot.Provenance.GetContributions(path);
                rows[runtimeIndex - 1] = new GameplayTagPickerRow(
                    runtimeIndex,
                    parent.Index,
                    path,
                    GetDisplaySegment(path),
                    contributions.Count,
                    FormatSourceDetails(contributions),
                    isDirectMatch: false);
            }

            return rows;
        }

        private static string GetDisplaySegment(string path)
        {
            var lastDot = path.LastIndexOf('.');
            return path.Substring(lastDot + 1);
        }

        private static string FormatSourceDetails(IReadOnlyList<TagSourceContribution> contributions)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < contributions.Count; index++)
            {
                if (index > 0) builder.Append('\n');
                var contribution = contributions[index];
                builder.Append(contribution.SourceId)
                    .Append(" (")
                    .Append(contribution.DisplayName)
                    .Append("): ")
                    .Append(contribution.Comment.Length == 0 ? "implicit" : contribution.Comment);
            }

            return builder.ToString();
        }
    }
}
