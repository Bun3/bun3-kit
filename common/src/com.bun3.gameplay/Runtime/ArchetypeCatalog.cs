#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay
{
    /// <summary>One compiled archetype. Attribute ids and bases are in clamp evaluation order.</summary>
    internal sealed class CompiledArchetype
    {
        internal CompiledArchetype(
            string name, ushort[] attributeIds, BigNum[] bases, int[] abilityIds, int[] abilityLevels)
        {
            Name = name;
            AttributeIds = attributeIds;
            Bases = bases;
            AbilityIds = abilityIds;
            AbilityLevels = abilityLevels;
        }

        internal string Name { get; }

        internal ushort[] AttributeIds { get; }

        internal BigNum[] Bases { get; }

        internal int[] AbilityIds { get; }

        internal int[] AbilityLevels { get; }
    }

    /// <summary>Immutable catalog of compiled unit archetypes, addressed by dense id.</summary>
    public sealed class ArchetypeCatalog
    {
        private readonly CompiledArchetype[] _archetypes;
        private readonly Dictionary<string, int> _nameToId;

        internal ArchetypeCatalog(CompiledArchetype[] archetypes, Dictionary<string, int> nameToId)
        {
            _archetypes = archetypes;
            _nameToId = nameToId;
        }

        /// <summary>A catalog with no archetypes.</summary>
        public static ArchetypeCatalog Empty { get; } =
            new ArchetypeCatalog(Array.Empty<CompiledArchetype>(), new Dictionary<string, int>());

        /// <summary>Number of compiled archetypes.</summary>
        public int Count => _archetypes.Length;

        /// <summary>Resolves an archetype name to its dense id.</summary>
        /// <param name="name">Archetype name.</param>
        /// <returns>Dense archetype id.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the name is not in the catalog.</exception>
        public int GetRequiredId(string name)
        {
            if (_nameToId.TryGetValue(name, out var id)) return id;
            throw new KeyNotFoundException($"Archetype '{name}' is not in the catalog.");
        }

        /// <summary>Tries to resolve an archetype name to its dense id.</summary>
        /// <param name="name">Archetype name.</param>
        /// <param name="id">Resolved dense id.</param>
        /// <returns>True when the name is in the catalog.</returns>
        public bool TryGetId(string name, out int id) => _nameToId.TryGetValue(name, out id);

        /// <summary>Authored name of an archetype.</summary>
        /// <param name="id">Dense archetype id.</param>
        /// <returns>Archetype name.</returns>
        public string GetName(int id) => _archetypes[id].Name;

        internal CompiledArchetype GetArchetype(int id) => _archetypes[id];
    }
}
