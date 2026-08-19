#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// Immutable gameplay tag catalog built once from validated input.
    /// </summary>
    /// <remarks>Partial layout: this file (queries and lookup) / Build (construction) / Fingerprint (fingerprint computation).</remarks>
    public sealed partial class TagCatalog
    {
        private readonly Dictionary<string, ushort> _byCanonicalName;
        private readonly Dictionary<string, ushort> _redirects;
        private readonly string[] _displayNames;
        private readonly ushort[] _parents;
        private readonly ushort[] _subtreeEnds;
        private readonly byte[] _fingerprint;
        private readonly string _catalogId;
        private readonly string _catalogVersion;

        private TagCatalog(
            Dictionary<string, ushort> byCanonicalName,
            Dictionary<string, ushort> redirects,
            string[] displayNames,
            ushort[] parents,
            ushort[] subtreeEnds,
            byte[] fingerprint,
            string catalogId,
            string catalogVersion)
        {
            _byCanonicalName = byCanonicalName;
            _redirects = redirects;
            _displayNames = displayNames;
            _parents = parents;
            _subtreeEnds = subtreeEnds;
            _fingerprint = fingerprint;
            _catalogId = catalogId;
            _catalogVersion = catalogVersion;
            Count = displayNames.Length - 1;
        }

        /// <summary>
        /// Assembles a catalog from canonical tag names and redirect definitions — the format-agnostic entry point.
        /// </summary>
        internal static TagCatalog Create(List<string> canonicalTagNames, List<RedirectDefinition> definitions)
        {
            var build = Build(canonicalTagNames);
            var redirects = BuildRedirects(definitions, build.ByCanonicalName, out var fingerprintRedirects);
            var canonicalNames = CreateCanonicalNames(build.ByCanonicalName, build.Parents.Length);
            var fingerprint = ComputeFingerprint(
                2,
                canonicalNames,
                build.Parents,
                build.SubtreeEnds,
                fingerprintRedirects);
            return new TagCatalog(
                build.ByCanonicalName,
                redirects,
                canonicalNames,
                build.Parents,
                build.SubtreeEnds,
                fingerprint,
                string.Empty,
                string.Empty);
        }

        internal static TagCatalog CreateCompiled(
            TagCatalogIdentity identity,
            string[] canonicalNames,
            ushort[] parents,
            ushort[] subtreeEnds,
            IReadOnlyList<CompiledRedirect> compiledRedirects)
        {
            if (identity is null) throw new ArgumentNullException(nameof(identity));
            if (canonicalNames is null) throw new ArgumentNullException(nameof(canonicalNames));
            if (parents is null) throw new ArgumentNullException(nameof(parents));
            if (subtreeEnds is null) throw new ArgumentNullException(nameof(subtreeEnds));
            if (compiledRedirects is null) throw new ArgumentNullException(nameof(compiledRedirects));
            if (canonicalNames.Length != parents.Length || canonicalNames.Length != subtreeEnds.Length)
            {
                throw new ArgumentException("Compiled tag arrays must have equal lengths.", nameof(canonicalNames));
            }

            var names = (string[])canonicalNames.Clone();
            var parentCopy = (ushort[])parents.Clone();
            var subtreeEndCopy = (ushort[])subtreeEnds.Clone();
            var byCanonicalName = new Dictionary<string, ushort>(names.Length - 1, StringComparer.Ordinal);
            for (var index = 1; index < names.Length; index++)
            {
                byCanonicalName.Add(names[index], checked((ushort)index));
            }

            var redirectEntries = new RedirectEntry[compiledRedirects.Count];
            for (var index = 0; index < redirectEntries.Length; index++)
            {
                var redirect = compiledRedirects[index];
                redirectEntries[index] = new RedirectEntry(redirect.From, redirect.To);
            }

            Array.Sort(
                redirectEntries,
                (left, right) => StringComparer.Ordinal.Compare(left.From, right.From));
            var redirects = new Dictionary<string, ushort>(redirectEntries.Length, StringComparer.Ordinal);
            for (var index = 0; index < redirectEntries.Length; index++)
            {
                redirects.Add(redirectEntries[index].From, byCanonicalName[redirectEntries[index].To]);
            }

            var fingerprint = ComputeFingerprint(
                2,
                names,
                parentCopy,
                subtreeEndCopy,
                redirectEntries);
            return new TagCatalog(
                byCanonicalName,
                redirects,
                names,
                parentCopy,
                subtreeEndCopy,
                fingerprint,
                identity.CatalogId,
                identity.CatalogVersion);
        }

        private static Dictionary<string, ushort> BuildRedirects(
            List<RedirectDefinition> definitions,
            Dictionary<string, ushort> byCanonicalName,
            out RedirectEntry[] fingerprintRedirects)
        {
            var redirects = new Dictionary<string, ushort>(definitions.Count, StringComparer.Ordinal);
            var entries = new List<RedirectEntry>(definitions.Count);
            foreach (var definition in definitions)
            {
                if (byCanonicalName.ContainsKey(definition.From))
                {
                    throw new TagCatalogException(
                        "Redirect source must not collide with an active tag.",
                        definition.FromJsonPath,
                        definition.FromLineNumber,
                        definition.FromLinePosition);
                }

                if (!byCanonicalName.TryGetValue(definition.To, out var target))
                {
                    throw new TagCatalogException(
                        "Redirect target must be an active tag.",
                        definition.ToJsonPath,
                        definition.ToLineNumber,
                        definition.ToLinePosition);
                }

                if (!redirects.TryAdd(definition.From, target))
                {
                    throw new TagCatalogException(
                        "Duplicate redirect source (case-insensitive).",
                        definition.FromJsonPath,
                        definition.FromLineNumber,
                        definition.FromLinePosition);
                }

                entries.Add(new RedirectEntry(definition.From, definition.To));
            }

            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.From, right.From));
            fingerprintRedirects = entries.ToArray();
            return redirects;
        }

        private static string[] CreateCanonicalNames(Dictionary<string, ushort> byCanonicalName, int length)
        {
            var canonicalNames = new string[length];
            foreach (var pair in byCanonicalName)
            {
                canonicalNames[pair.Value] = pair.Key;
            }

            return canonicalNames;
        }

        /// <summary>Number of tags in the catalog, excluding None.</summary>
        public int Count { get; }

        /// <summary>Game product ID of a compiled catalog; empty for a legacy JSON catalog.</summary>
        public string CatalogId => _catalogId;

        /// <summary>Version of a compiled catalog; empty for a legacy JSON catalog.</summary>
        public string CatalogVersion => _catalogVersion;

        /// <summary>Creates an empty container bound to this catalog.</summary>
        /// <param name="expectedExactKinds">Expected number of exact tag kinds; 0 to 64.</param>
        /// <returns>An empty tag container.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedExactKinds"/> is outside the 0 to 64 range.</exception>
        public TagContainer CreateContainer(int expectedExactKinds = 0) => new TagContainer(this, expectedExactKinds);

        /// <summary>Creates an empty count container bound to this catalog.</summary>
        /// <param name="expectedExactKinds">Expected number of exact tag kinds; 0 to 64.</param>
        /// <returns>An empty tag count container.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedExactKinds"/> is outside the 0 to 64 range.</exception>
        public TagCountContainer CreateCountContainer(int expectedExactKinds = 0) => new TagCountContainer(this, expectedExactKinds);

        /// <summary>Gets the canonical SHA-256 fingerprint of the catalog.</summary>
        public ReadOnlySpan<byte> Fingerprint => _fingerprint;

        /// <summary>Checks whether the given fingerprint equals this catalog's fingerprint.</summary>
        /// <param name="other">Fingerprint bytes to compare.</param>
        /// <returns>True if the fingerprints are equal.</returns>
        public bool MatchesFingerprint(ReadOnlySpan<byte> other) => other.SequenceEqual(_fingerprint);

        /// <summary>Finds the registered tag for a path.</summary>
        /// <param name="path">Path of ASCII alphanumeric segments.</param>
        /// <param name="tag">The found tag, or None.</param>
        /// <returns>True if the syntactically valid path is registered.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> syntax is invalid.</exception>
        public bool TryGet(string path, out GameplayTag tag)
        {
            if (!TagName.TryFold(path, out var canonical))
            {
                throw new ArgumentException("Invalid tag path syntax.", nameof(path));
            }

            if (_byCanonicalName.TryGetValue(canonical, out var index))
            {
                tag = new GameplayTag(index);
                return true;
            }

            if (_redirects.TryGetValue(canonical, out index))
            {
                tag = new GameplayTag(index);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>Finds the registered tag for a path, or throws.</summary>
        /// <param name="path">Path of ASCII alphanumeric segments.</param>
        /// <returns>The found tag.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> syntax is invalid.</exception>
        /// <exception cref="KeyNotFoundException">The valid path is not registered.</exception>
        public GameplayTag GetRequired(string path)
        {
            if (TryGet(path, out var tag))
            {
                return tag;
            }

            throw new KeyNotFoundException($"Unregistered tag path: {path}");
        }

        /// <summary>Restores a wire index within catalog range to a tag.</summary>
        /// <param name="index">Wire index to restore.</param>
        /// <param name="tag">The restored tag, or None.</param>
        /// <returns>True if the index is within catalog range.</returns>
        public bool TryGetByIndex(ushort index, out GameplayTag tag)
        {
            if (index <= Count)
            {
                tag = new GameplayTag(index);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>Restores a wire index within catalog range to a tag.</summary>
        /// <param name="index">Wire index to restore.</param>
        /// <returns>The restored tag.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside catalog range.</exception>
        public GameplayTag GetRequiredByIndex(ushort index)
        {
            if (TryGetByIndex(index, out var tag))
            {
                return tag;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        /// <summary>Gets the canonical lowercase name of a tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns>The registered canonical lowercase name, or an empty string.</returns>
        public string GetDisplayName(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? _displayNames[tag.Index] : string.Empty;

        /// <summary>Gets the direct parent of a tag; None for a root or invalid tag.</summary>
        /// <param name="tag">Tag to look up.</param>
        /// <returns>The direct parent, or None.</returns>
        public GameplayTag GetParent(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? new GameplayTag(_parents[tag.Index]) : GameplayTag.None;

        /// <summary>Checks whether ancestor is the tag itself or one of its ancestors.</summary>
        /// <param name="ancestor">Candidate ancestor tag.</param>
        /// <param name="tag">Candidate descendant tag.</param>
        /// <returns>True if ancestor is the tag itself or one of its ancestors.</returns>
        public bool IsAncestorOrSelf(GameplayTag ancestor, GameplayTag tag)
        {
            if (!ancestor.IsValid || !tag.IsValid || ancestor.Index > Count || tag.Index > Count)
            {
                return false;
            }

            return ancestor.Index <= tag.Index && tag.Index <= _subtreeEnds[ancestor.Index];
        }

        internal ushort GetSubtreeEnd(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? _subtreeEnds[tag.Index] : (ushort)0;

        internal CompiledRedirect[] CopyCompiledRedirects()
        {
            var redirects = new CompiledRedirect[_redirects.Count];
            var index = 0;
            foreach (var pair in _redirects)
            {
                redirects[index++] = new CompiledRedirect(pair.Key, _displayNames[pair.Value]);
            }

            Array.Sort(redirects, (left, right) => StringComparer.Ordinal.Compare(left.From, right.From));
            return redirects;
        }

        /// <summary>One redirect's canonical name pair and its source JSON positions.</summary>
        internal readonly struct RedirectDefinition
        {
            internal RedirectDefinition(
                string from,
                string to,
                string fromJsonPath,
                int fromLineNumber,
                int fromLinePosition,
                string toJsonPath,
                int toLineNumber,
                int toLinePosition)
            {
                From = from;
                To = to;
                FromJsonPath = fromJsonPath;
                FromLineNumber = fromLineNumber;
                FromLinePosition = fromLinePosition;
                ToJsonPath = toJsonPath;
                ToLineNumber = toLineNumber;
                ToLinePosition = toLinePosition;
            }

            internal string From { get; }
            internal string To { get; }
            internal string FromJsonPath { get; }
            internal int FromLineNumber { get; }
            internal int FromLinePosition { get; }
            internal string ToJsonPath { get; }
            internal int ToLineNumber { get; }
            internal int ToLinePosition { get; }
        }

        private readonly struct RedirectEntry
        {
            internal RedirectEntry(string from, string to)
            {
                From = from;
                To = to;
            }

            internal string From { get; }
            internal string To { get; }
        }
    }
}
