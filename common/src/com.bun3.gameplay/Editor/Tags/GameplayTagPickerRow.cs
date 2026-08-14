#nullable enable
using System;

namespace Bun3.Gameplay.Editor.Tags
{
    internal readonly struct GameplayTagPickerRow : IGameplayTagProjectionRow
    {
        internal GameplayTagPickerRow(
            int id,
            int parentId,
            string canonicalPath,
            string displaySegment,
            int sourceCount,
            string sourceDetails,
            bool isDirectMatch)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (parentId < 0) throw new ArgumentOutOfRangeException(nameof(parentId));
            if (sourceCount <= 0) throw new ArgumentOutOfRangeException(nameof(sourceCount));
            Id = id;
            ParentId = parentId;
            CanonicalPath = canonicalPath ?? throw new ArgumentNullException(nameof(canonicalPath));
            DisplaySegment = displaySegment ?? throw new ArgumentNullException(nameof(displaySegment));
            SourceCount = sourceCount;
            SourceDetails = sourceDetails ?? throw new ArgumentNullException(nameof(sourceDetails));
            IsDirectMatch = isDirectMatch;
        }

        internal int Id { get; }
        internal int ParentId { get; }
        internal string CanonicalPath { get; }
        internal string DisplaySegment { get; }
        internal int SourceCount { get; }
        internal string SourceDetails { get; }
        internal bool IsDirectMatch { get; }
        int IGameplayTagProjectionRow.Id => Id;
        int IGameplayTagProjectionRow.ParentId => ParentId;
        string IGameplayTagProjectionRow.DisplaySegment => DisplaySegment;

        internal GameplayTagPickerRow WithDirectMatch(bool isDirectMatch) =>
            new GameplayTagPickerRow(
                Id,
                ParentId,
                CanonicalPath,
                DisplaySegment,
                SourceCount,
                SourceDetails,
                isDirectMatch);
    }
}
