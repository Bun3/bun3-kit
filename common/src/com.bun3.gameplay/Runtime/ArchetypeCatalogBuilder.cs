#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay
{
    /// <summary>Collects archetype definitions and validates them all at Build.</summary>
    public sealed class ArchetypeCatalogBuilder
    {
        private readonly List<ArchetypeDef> _defs = new List<ArchetypeDef>();
        private bool _built;

        /// <summary>Adds an archetype definition. Validation happens at Build.</summary>
        /// <param name="def">Archetype definition.</param>
        public void Add(ArchetypeDef def)
        {
            if (def is null) throw new ArgumentNullException(nameof(def));
            if (_built) throw new InvalidOperationException("Cannot add after Build.");
            _defs.Add(def);
        }

        /// <summary>Validates every definition against the attribute registry and ability catalog and builds
        /// the immutable catalog. Ids are assigned densely in insertion order.</summary>
        /// <param name="attributes">Registry the declared attributes must be registered in.</param>
        /// <param name="abilities">Catalog the granted abilities must exist in.</param>
        /// <returns>The compiled archetype catalog.</returns>
        /// <exception cref="InvalidOperationException">Thrown on the first invalid definition.</exception>
        public ArchetypeCatalog Build(AttributeRegistry attributes, AbilityCatalog abilities)
        {
            if (attributes is null) throw new ArgumentNullException(nameof(attributes));
            if (abilities is null) throw new ArgumentNullException(nameof(abilities));
            _built = true;

            var nameToId = new Dictionary<string, int>(StringComparer.Ordinal);
            var compiled = new CompiledArchetype[_defs.Count];
            for (var i = 0; i < _defs.Count; i++)
            {
                var def = _defs[i];
                if (string.IsNullOrEmpty(def.Name))
                    throw new InvalidOperationException($"Archetype at index {i} has an empty name.");
                if (nameToId.ContainsKey(def.Name))
                    throw new InvalidOperationException($"Archetype '{def.Name}' is defined more than once.");

                compiled[i] = Compile(def, attributes, abilities);
                nameToId.Add(def.Name, i);
            }

            return new ArchetypeCatalog(compiled, nameToId);
        }

        private static CompiledArchetype Compile(ArchetypeDef def, AttributeRegistry attributes, AbilityCatalog abilities)
        {
            var owner = $"Archetype '{def.Name}'";

            var bases = new Dictionary<ushort, BigNum>();
            foreach (var attribute in def.Attributes)
            {
                if (!attributes.Contains(attribute.AttributeId))
                    throw new InvalidOperationException($"{owner}: references unregistered attribute {attribute.AttributeId}.");
                if (bases.ContainsKey(attribute.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: declares attribute '{attributes.GetName(attribute.AttributeId)}' more than once.");
                }

                bases.Add(attribute.AttributeId, attribute.Base);
            }

            // Clamp evaluation order puts a bound (MaxHp) before the value it clamps (Hp), whatever the
            // authoring order, so initial values are never clamped against a not-yet-set bound.
            var attributeIds = new ushort[bases.Count];
            var baseValues = new BigNum[bases.Count];
            var written = 0;
            foreach (var id in attributes.EvaluationOrder)
            {
                if (!bases.TryGetValue(id, out var value)) continue;
                attributeIds[written] = id;
                baseValues[written] = value;
                written++;
            }

            AttributeLayout layout;
            try
            {
                layout = new AttributeLayout(attributes, attributeIds);
            }
            catch (ArgumentException e)
            {
                throw new InvalidOperationException($"{owner}: {e.Message}", e);
            }

            var abilityIds = new int[def.Abilities.Count];
            var abilityLevels = new int[def.Abilities.Count];
            for (var i = 0; i < def.Abilities.Count; i++)
            {
                var entry = def.Abilities[i];
                if (!abilities.TryGetId(entry.AbilityName, out var abilityId))
                    throw new InvalidOperationException($"{owner}: grants unknown ability '{entry.AbilityName}'.");
                if (Array.IndexOf(abilityIds, abilityId, 0, i) >= 0)
                    throw new InvalidOperationException($"{owner}: grants ability '{entry.AbilityName}' more than once.");
                if (entry.Level < 1)
                {
                    throw new InvalidOperationException(
                        $"{owner}: ability '{entry.AbilityName}' level {entry.Level} must be at least 1.");
                }

                var maxLevel = abilities.GetAbility(abilityId).MaxLevel;
                if (maxLevel > 0 && entry.Level > maxLevel)
                {
                    throw new InvalidOperationException(
                        $"{owner}: ability '{entry.AbilityName}' level {entry.Level} exceeds MaxLevel ({maxLevel}).");
                }

                abilityIds[i] = abilityId;
                abilityLevels[i] = entry.Level;
            }

            return new CompiledArchetype(def.Name, layout, attributeIds, baseValues, abilityIds, abilityLevels);
        }
    }
}
