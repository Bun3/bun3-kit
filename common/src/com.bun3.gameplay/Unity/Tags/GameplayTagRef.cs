#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Authoring reference that stores a canonical tag path in a Unity asset.</summary>
    [Serializable]
    public struct GameplayTagRef : IEquatable<GameplayTagRef>
    {
        [SerializeField]
        private string? _path;

        /// <summary>Default value that references no tag.</summary>
        public static readonly GameplayTagRef None = default;

        /// <summary>Creates a reference, normalizing the input path to canonical lowercase.</summary>
        /// <param name="path">Tag path to store.</param>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        /// <exception cref="ArgumentException">The tag path syntax is invalid.</exception>
        public GameplayTagRef(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
            {
                _path = string.Empty;
                return;
            }

            if (!TagName.TryFold(path, out var canonical))
            {
                throw new ArgumentException("Invalid tag path syntax.", nameof(path));
            }

            _path = canonical;
        }

        /// <summary>Serialized tag path; an empty string means None.</summary>
        public string Path => _path ?? string.Empty;

        /// <summary>Indicates whether no tag is referenced.</summary>
        public bool IsEmpty => Path.Length == 0;

        /// <summary>Resolves this reference to a tag in the given runtime catalog.</summary>
        /// <param name="catalog">Runtime catalog used for resolution.</param>
        /// <param name="tag">Resolved tag, or None.</param>
        /// <returns>True if the reference is None or resolves to a registered path.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
        public bool TryResolve(TagCatalog catalog, out GameplayTag tag)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (IsEmpty)
            {
                tag = GameplayTag.None;
                return true;
            }

            if (!TagName.TryFold(Path, out _))
            {
                tag = GameplayTag.None;
                return false;
            }

            return catalog.TryGet(Path, out tag);
        }

        /// <summary>Resolves this reference to a tag in the given runtime catalog, throwing on failure.</summary>
        /// <param name="catalog">Runtime catalog used for resolution.</param>
        /// <returns>Resolved tag, or None for an empty reference.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
        /// <exception cref="ArgumentException">The serialized tag path syntax is invalid.</exception>
        /// <exception cref="KeyNotFoundException">The tag path is not in the current catalog.</exception>
        public GameplayTag ResolveRequired(TagCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (IsEmpty) return GameplayTag.None;
            if (!TagName.TryFold(Path, out _))
            {
                throw new ArgumentException("Invalid serialized tag path syntax.", nameof(Path));
            }

            return catalog.GetRequired(Path);
        }

        /// <summary>Checks whether the serialized paths of two references are ordinally equal.</summary>
        /// <param name="other">Reference to compare with.</param>
        /// <returns>True if the paths are equal.</returns>
        public bool Equals(GameplayTagRef other) =>
            string.Equals(Path, other.Path, StringComparison.Ordinal);

        /// <summary>Checks whether the given object is a reference with the same path.</summary>
        /// <param name="obj">Object to compare with.</param>
        /// <returns>True if it is the same reference.</returns>
        public override bool Equals(object? obj) => obj is GameplayTagRef other && Equals(other);

        /// <summary>Returns the ordinal hash code of the serialized path.</summary>
        /// <returns>Reference hash code.</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Path);

        /// <summary>Checks whether two references store the same path.</summary>
        public static bool operator ==(GameplayTagRef left, GameplayTagRef right) => left.Equals(right);

        /// <summary>Checks whether two references store different paths.</summary>
        public static bool operator !=(GameplayTagRef left, GameplayTagRef right) => !left.Equals(right);
    }
}
