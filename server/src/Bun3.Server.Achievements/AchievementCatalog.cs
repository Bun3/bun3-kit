using System;
using System.Collections.Generic;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// Achievement definition catalog — takes the definition list built by the game loader at
    /// startup, validates it in bulk, and freezes it (immutable). String ids and tags are interned
    /// here into dense identifiers; the runtime identifier is <see cref="AchievementId"/>/
    /// <see cref="AchievementTag"/> — the game caches them at startup via
    /// <see cref="GetRequired"/>/<see cref="GetRequiredTag"/> and uses only identifiers on hot
    /// paths. Validation failure throws = startup failure.
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
        private readonly AchievementId[][] _indicesByTag;

        /// <summary>Number of definitions.</summary>
        public int Count => _definitions.Length;

        /// <summary>Number of interned tags.</summary>
        public int TagCount => _indicesByTag.Length;

        /// <summary>Validates the definition list and freezes it. After framework validation
        /// (empty/duplicate ids, Target ≤ 0, cap exceeded, availability range, empty/duplicate
        /// tags), <paramref name="validator"/> is called per definition — domain invariants
        /// (reward table exists, etc.) are enforced by the game throwing here.</summary>
        /// <exception cref="ArgumentNullException">When <paramref name="definitions"/> is null.</exception>
        /// <exception cref="AchievementCatalogException">When the definition list violates an invariant.</exception>
        public AchievementCatalog(IReadOnlyList<TDef> definitions, Action<TDef>? validator = null)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (definitions.Count > MaxDefinitions)
            {
                throw new AchievementCatalogException($"Achievement definition count exceeds the cap ({definitions.Count} > {MaxDefinitions}).");
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
                    throw new AchievementCatalogException($"Achievement definition [{i}] is null.");
                }
                if (string.IsNullOrEmpty(def.Id))
                {
                    throw new AchievementCatalogException($"Achievement definition [{i}] has an empty Id.");
                }
                if (def.Target <= 0)
                {
                    throw new AchievementCatalogException($"Achievement '{def.Id}' has a non-positive Target ({def.Target}).");
                }
                if ((uint)def.InitialAvailability > (uint)AchievementStatus.Active)
                {
                    throw new AchievementCatalogException($"Achievement '{def.Id}' InitialAvailability must be Locked/Ready/Active ({def.InitialAvailability}).");
                }
                if (_indexById.ContainsKey(def.Id))
                {
                    throw new AchievementCatalogException($"Duplicate achievement Id '{def.Id}'.");
                }

                _indexById.Add(def.Id, i);

                for (var t = 0; t < def.Tags.Count; t++)
                {
                    var tag = def.Tags[t];
                    if (string.IsNullOrEmpty(tag))
                    {
                        throw new AchievementCatalogException($"Achievement '{def.Id}' tag [{t}] is empty.");
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
                        throw new AchievementCatalogException($"Achievement '{def.Id}' has duplicate tag '{tag}'.");
                    }
                    members.Add(i);
                }

                validator?.Invoke(def);
                _definitions[i] = def;
            }

            _indicesByTag = new AchievementId[indicesByTag.Count][];
            for (var t = 0; t < indicesByTag.Count; t++)
            {
                var members = indicesByTag[t];
                var ids = new AchievementId[members.Count];
                for (var m = 0; m < members.Count; m++)
                {
                    ids[m] = new AchievementId(members[m]);
                }
                _indicesByTag[t] = ids;
            }
        }

        /// <summary>Returns the definition. Throws on an identifier from another catalog or None.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When the identifier is not valid here.</exception>
        public TDef GetDefinition(AchievementId id)
        {
            if (!Contains(id))
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Not an identifier of this catalog.");
            }

            return _definitions[id.Index];
        }

        /// <summary>Whether the identifier is valid for this catalog (None or out of range is false).</summary>
        public bool Contains(AchievementId id) => (uint)id.Index < (uint)_definitions.Length;

        /// <summary>Looks up by string id. Returns false if absent; <paramref name="achievement"/> is None.</summary>
        public bool TryGet(string id, out AchievementId achievement)
        {
            if (id != null && _indexById.TryGetValue(id, out var index))
            {
                achievement = new AchievementId(index);
                return true;
            }

            achievement = AchievementId.None;
            return false;
        }

        /// <summary>Looks up by string id — call once at startup and cache.</summary>
        /// <exception cref="AchievementCatalogException">When the id is not in the catalog.</exception>
        public AchievementId GetRequired(string id)
        {
            if (!TryGet(id, out var achievement))
            {
                throw new AchievementCatalogException($"Achievement id not in catalog: '{id}'");
            }

            return achievement;
        }

        /// <summary>Looks up a tag by name. Returns false if absent; <paramref name="tag"/> is None.</summary>
        public bool TryGetTag(string tag, out AchievementTag achievementTag)
        {
            if (tag != null && _tagIndexByName.TryGetValue(tag, out var index))
            {
                achievementTag = new AchievementTag(index);
                return true;
            }

            achievementTag = AchievementTag.None;
            return false;
        }

        /// <summary>Looks up a tag by name — call once at startup and cache. Typos surface here as
        /// startup failures.</summary>
        /// <exception cref="AchievementCatalogException">When the tag is not in the catalog.</exception>
        public AchievementTag GetRequiredTag(string tag)
        {
            if (!TryGetTag(tag, out var achievementTag))
            {
                throw new AchievementCatalogException($"Achievement tag not in catalog: '{tag}'");
            }

            return achievementTag;
        }

        /// <summary>Achievement identifiers carrying the tag (frozen, in definition order). Beyond
        /// routing, also used for group sweeps such as reset sweeps and selection — allocation-free.</summary>
        /// <exception cref="ArgumentOutOfRangeException">When the identifier is not valid here.</exception>
        public ReadOnlySpan<AchievementId> GetIndicesByTag(AchievementTag tag)
        {
            if ((uint)tag.Index >= (uint)_indicesByTag.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(tag), "Not a tag of this catalog.");
            }

            return _indicesByTag[tag.Index];
        }
    }
}
