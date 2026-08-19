using System;
using System.Collections.Generic;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Achievement definition catalog — takes the definition list built by the game loader at
    /// startup, validates it in bulk, and freezes it (immutable). String ids and tags are interned
    /// here into dense int indices; the runtime identifier is the index — the game caches indices
    /// at startup via <see cref="GetIndex"/>/<see cref="GetTagIndex"/> and uses only indices on
    /// hot paths. Validation failure throws = startup failure.
    /// </summary>
    /// <typeparam name="TDef">Game achievement definition type — hooks and lookups receive this type without casting.</typeparam>
    public sealed class AchievementCatalog<TDef> where TDef : AchievementDefinition
    {
        /// <summary>Cap on the number of definitions the catalog accepts — a safety net that
        /// rejects accidentally huge inputs (runaway generators, etc.) at startup.</summary>
        public const int MaxDefinitions = 65_536;

        private readonly TDef[] _definitions;
        private readonly Dictionary<string, int> _indexById;
        private readonly Dictionary<string, int> _tagIndexByName;
        private readonly int[][] _indicesByTag;

        /// <summary>Number of definitions.</summary>
        public int Count => _definitions.Length;

        /// <summary>Number of interned tags.</summary>
        public int TagCount => _indicesByTag.Length;

        /// <summary>Validates the definition list and freezes it. After framework validation
        /// (empty/duplicate ids, Target ≤ 0, cap exceeded, availability range, empty/duplicate
        /// tags), <paramref name="validator"/> is called per definition — domain invariants
        /// (reward table exists, etc.) are enforced by the game throwing here.</summary>
        /// <exception cref="ArgumentException">When the definition list violates an invariant.</exception>
        public AchievementCatalog(IReadOnlyList<TDef> definitions, Action<TDef>? validator = null)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (definitions.Count > MaxDefinitions)
            {
                throw new ArgumentException($"Achievement definition count exceeds the cap ({definitions.Count} > {MaxDefinitions}).", nameof(definitions));
            }

            _definitions = new TDef[definitions.Count];
            _indexById = new Dictionary<string, int>(definitions.Count, StringComparer.Ordinal);
            _tagIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var indicesByTag = new List<List<int>>();

            for (var i = 0; i < definitions.Count; i++)
            {
                var def = definitions[i];
                if (def == null)
                {
                    throw new ArgumentException($"Achievement definition [{i}] is null.", nameof(definitions));
                }
                if (string.IsNullOrEmpty(def.Id))
                {
                    throw new ArgumentException($"Achievement definition [{i}] has an empty Id.", nameof(definitions));
                }
                if (def.Target <= 0)
                {
                    throw new ArgumentException($"Achievement '{def.Id}' has a non-positive Target ({def.Target}).", nameof(definitions));
                }
                if ((uint)def.InitialAvailability > (uint)AchievementStatus.Active)
                {
                    throw new ArgumentException($"Achievement '{def.Id}' InitialAvailability must be Locked/Ready/Active ({def.InitialAvailability}).", nameof(definitions));
                }
                if (_indexById.ContainsKey(def.Id))
                {
                    throw new ArgumentException($"Duplicate achievement Id '{def.Id}'.", nameof(definitions));
                }

                _indexById.Add(def.Id, i);

                for (var t = 0; t < def.Tags.Count; t++)
                {
                    var tag = def.Tags[t];
                    if (string.IsNullOrEmpty(tag))
                    {
                        throw new ArgumentException($"Achievement '{def.Id}' tag [{t}] is empty.", nameof(definitions));
                    }
                    if (!_tagIndexByName.TryGetValue(tag, out var tagIndex))
                    {
                        tagIndex = indicesByTag.Count;
                        _tagIndexByName.Add(tag, tagIndex);
                        indicesByTag.Add(new List<int>());
                    }

                    var members = indicesByTag[tagIndex];
                    if (members.Count > 0 && members[members.Count - 1] == i)
                    {
                        throw new ArgumentException($"Achievement '{def.Id}' has duplicate tag '{tag}'.", nameof(definitions));
                    }
                    members.Add(i);
                }

                validator?.Invoke(def);
                _definitions[i] = def;
            }

            _indicesByTag = new int[indicesByTag.Count][];
            for (var t = 0; t < indicesByTag.Count; t++)
            {
                _indicesByTag[t] = indicesByTag[t].ToArray();
            }
        }

        /// <summary>Returns the definition by index.</summary>
        public TDef GetDefinition(int index) => _definitions[index];

        /// <summary>Returns the index for an id — call once at startup and cache. Throws if absent.</summary>
        /// <exception cref="KeyNotFoundException">When the id is not in the catalog.</exception>
        public int GetIndex(string id)
        {
            if (!_indexById.TryGetValue(id, out var index))
            {
                throw new KeyNotFoundException($"Achievement Id '{id}' is not in the catalog.");
            }

            return index;
        }

        /// <summary>Returns the index for an id. False if absent.</summary>
        public bool TryGetIndex(string id, out int index) => _indexById.TryGetValue(id, out index);

        /// <summary>Returns the tag index for a tag name — call once at startup and cache.
        /// Typos surface here as startup failures.</summary>
        /// <exception cref="KeyNotFoundException">When the tag is not in the catalog.</exception>
        public int GetTagIndex(string tag)
        {
            if (!_tagIndexByName.TryGetValue(tag, out var index))
            {
                throw new KeyNotFoundException($"Achievement tag '{tag}' is not in the catalog.");
            }

            return index;
        }

        /// <summary>Returns the tag index for a tag name. False if absent.</summary>
        public bool TryGetTagIndex(string tag, out int index) => _tagIndexByName.TryGetValue(tag, out index);

        /// <summary>Achievement indices carrying the tag (frozen, in definition order). Beyond
        /// routing, also used for group sweeps such as reset sweeps and selection — allocation-free.</summary>
        public ReadOnlySpan<int> GetIndicesByTag(int tagIndex) => _indicesByTag[tagIndex];
    }
}
