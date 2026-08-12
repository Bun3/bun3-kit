#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Editor.Tags
{
    internal readonly struct GameplayTagTreeRowModel
    {
        internal GameplayTagTreeRowModel(
            ushort index, ushort parentIndex, string path, string comment, bool directMatch)
        {
            Index = index;
            ParentIndex = parentIndex;
            Path = path;
            Comment = comment;
            IsDirectMatch = directMatch;
        }

        internal ushort Index { get; }
        internal ushort ParentIndex { get; }
        internal string Path { get; }
        internal string Comment { get; }
        internal bool IsDirectMatch { get; }
    }

    internal sealed class GameplayTagCatalogViewModel
    {
        private readonly GameplayTagTreeRowModel[] _rows;

        internal GameplayTagCatalogViewModel(GameplayTagCatalogEditSession session)
        {
            if (session is null) throw new ArgumentNullException(nameof(session));

            var comments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < session.Tags.Count; i++)
            {
                var row = session.Tags[i];
                comments[row.Name] = row.Comment;
            }

            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(session.Serialize()));
            var catalog = TagCatalog.Load(stream);
            _rows = new GameplayTagTreeRowModel[catalog.Count];
            for (var index = 1; index <= catalog.Count; index++)
            {
                var tag = catalog.GetRequiredByIndex(checked((ushort)index));
                var path = catalog.GetDisplayName(tag);
                var parent = catalog.GetParent(tag);
                comments.TryGetValue(path, out var comment);
                _rows[index - 1] = new GameplayTagTreeRowModel(
                    checked((ushort)index), parent.Index, path, comment ?? string.Empty, directMatch: false);
            }

            ActiveCount = catalog.Count;
            FingerprintPrefix = FormatFingerprintPrefix(catalog.Fingerprint);
        }

        internal int ActiveCount { get; }

        internal string FingerprintPrefix { get; }

        internal IReadOnlyList<GameplayTagTreeRowModel> Filter(string search)
        {
            if (search is null) throw new ArgumentNullException(nameof(search));
            if (search.Length == 0) return _rows;

            var included = new bool[_rows.Length + 1];
            var directMatches = new bool[_rows.Length];
            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (row.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && row.Comment.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                directMatches[rowIndex] = true;
                var current = row.Index;
                while (current != 0)
                {
                    included[current] = true;
                    current = _rows[current - 1].ParentIndex;
                }
            }

            var results = new List<GameplayTagTreeRowModel>();
            for (var rowIndex = 0; rowIndex < _rows.Length; rowIndex++)
            {
                var row = _rows[rowIndex];
                if (included[row.Index])
                {
                    results.Add(new GameplayTagTreeRowModel(
                        row.Index, row.ParentIndex, row.Path, row.Comment, directMatches[rowIndex]));
                }
            }

            return results;
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
