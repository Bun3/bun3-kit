#nullable enable
using System;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// Stable identifier and display info of a tag source.
    /// </summary>
    public sealed class TagSourceDescriptor
    {
        /// <summary>Tag source identifier.</summary>
        public string SourceId { get; }

        /// <summary>Source name shown to users.</summary>
        public string DisplayName { get; }

        /// <summary>Delivery kind of the source.</summary>
        public TagSourceKind Kind { get; }

        /// <summary>Whether the source is read-only.</summary>
        public bool IsReadOnly { get; }

        /// <summary>Creates a validated tag source descriptor.</summary>
        public TagSourceDescriptor(string sourceId, string displayName, TagSourceKind kind, bool isReadOnly)
        {
            if (!IsValidSourceId(sourceId))
            {
                throw new ArgumentException("A source ID must be lowercase alphanumeric segments joined by dots or hyphens.", nameof(sourceId));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
            }

            if (kind == TagSourceKind.GameJson)
            {
                if (!string.Equals(sourceId, "game", StringComparison.Ordinal) || isReadOnly)
                {
                    throw new ArgumentException("A GameJson source must have the ID \"game\" and be editable.", nameof(sourceId));
                }
            }
            else
            {
                if (string.Equals(sourceId, "game", StringComparison.Ordinal) || !isReadOnly)
                {
                    throw new ArgumentException("Sources other than the \"game\" ID must be read-only.", nameof(sourceId));
                }

                if (kind != TagSourceKind.PackageJson && kind != TagSourceKind.Native)
                {
                    throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }

            SourceId = sourceId;
            DisplayName = displayName;
            Kind = kind;
            IsReadOnly = isReadOnly;
        }

        private static bool IsValidSourceId(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var sourceId = value!;
            var previousWasSeparator = true;
            for (var i = 0; i < sourceId.Length; i++)
            {
                var character = sourceId[i];
                var alphaNumeric = (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9');
                if (alphaNumeric)
                {
                    previousWasSeparator = false;
                    continue;
                }

                if ((character == '.' || character == '-') && !previousWasSeparator)
                {
                    previousWasSeparator = true;
                    continue;
                }

                return false;
            }

            return !previousWasSeparator;
        }
    }
}
