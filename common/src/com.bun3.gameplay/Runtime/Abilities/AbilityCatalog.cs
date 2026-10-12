#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>One compiled cost row.</summary>
    internal readonly struct CompiledCost
    {
        internal CompiledCost(ushort attributeId, CompiledMagnitude magnitude)
        {
            AttributeId = attributeId;
            Magnitude = magnitude;
        }

        internal ushort AttributeId { get; }

        internal CompiledMagnitude Magnitude { get; }
    }

    /// <summary>One compiled effect application of an ability.</summary>
    internal sealed class CompiledAbilityEffect
    {
        internal CompiledAbilityEffect(
            int effectId, AbilityEffectOrigin origin, ITargetSelector? selector, GameplayTag selectorTag,
            BigNum[] selectorParams, ChainLevelRule levelRule, int fixedLevel)
        {
            EffectId = effectId;
            Origin = origin;
            Selector = selector;
            SelectorTag = selectorTag;
            SelectorParams = selectorParams;
            LevelRule = levelRule;
            FixedLevel = fixedLevel;
        }

        internal int EffectId { get; }

        internal AbilityEffectOrigin Origin { get; }

        internal ITargetSelector? Selector { get; }

        /// <summary>Tag <see cref="Selector"/> was resolved from; <see cref="GameplayTag.None"/> when
        /// Selector is null. Kept so a compiled catalog can be persisted and the seam re-resolved on load.</summary>
        internal GameplayTag SelectorTag { get; }

        internal BigNum[] SelectorParams { get; }

        internal ChainLevelRule LevelRule { get; }

        internal int FixedLevel { get; }
    }

    /// <summary>One compiled ability.</summary>
    internal sealed class CompiledAbility
    {
        internal CompiledAbility(
            string name, int maxLevel, GameplayTag[] assetTags, GameplayTag[] requiredTags,
            GameplayTag[] blockedTags, CompiledCondition[] activationConditions, AbilityTargetMode targetMode,
            GameplayTag[] targetRequiredTags, GameplayTag[] targetBlockedTags, CompiledCondition[] targetConditions,
            CompiledCost[] costs, int cooldownEffectId, CompiledAbilityEffect[] effects)
        {
            Name = name;
            MaxLevel = maxLevel;
            AssetTags = assetTags;
            RequiredTags = requiredTags;
            BlockedTags = blockedTags;
            ActivationConditions = activationConditions;
            TargetMode = targetMode;
            TargetRequiredTags = targetRequiredTags;
            TargetBlockedTags = targetBlockedTags;
            TargetConditions = targetConditions;
            Costs = costs;
            CooldownEffectId = cooldownEffectId;
            Effects = effects;
        }

        internal string Name { get; }

        internal int MaxLevel { get; }

        internal GameplayTag[] AssetTags { get; }

        internal GameplayTag[] RequiredTags { get; }

        internal GameplayTag[] BlockedTags { get; }

        internal CompiledCondition[] ActivationConditions { get; }

        internal AbilityTargetMode TargetMode { get; }

        internal GameplayTag[] TargetRequiredTags { get; }

        internal GameplayTag[] TargetBlockedTags { get; }

        internal CompiledCondition[] TargetConditions { get; }

        internal CompiledCost[] Costs { get; }

        /// <summary>Compiled cooldown effect id, or -1 when the ability has no cooldown.</summary>
        internal int CooldownEffectId { get; }

        internal CompiledAbilityEffect[] Effects { get; }
    }

    /// <summary>Immutable compiled ability catalog produced by <see cref="AbilityCatalogBuilder"/>.</summary>
    public sealed class AbilityCatalog
    {
        private readonly CompiledAbility[] _abilities;
        private readonly Dictionary<string, int> _nameToId;

        internal AbilityCatalog(CompiledAbility[] abilities, Dictionary<string, int> nameToId)
        {
            _abilities = abilities;
            _nameToId = nameToId;
        }

        /// <summary>Number of compiled abilities.</summary>
        public int Count => _abilities.Length;

        /// <summary>Resolves an ability name to its dense id.</summary>
        /// <param name="name">Ability name.</param>
        /// <returns>Dense ability id.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the name is not in the catalog.</exception>
        public int GetRequiredId(string name)
        {
            if (_nameToId.TryGetValue(name, out var id)) return id;
            throw new KeyNotFoundException($"Ability '{name}' is not in the catalog.");
        }

        /// <summary>Tries to resolve an ability name to its dense id.</summary>
        /// <param name="name">Ability name.</param>
        /// <param name="id">Resolved dense id.</param>
        /// <returns>True when the name is in the catalog.</returns>
        public bool TryGetId(string name, out int id) => _nameToId.TryGetValue(name, out id);

        /// <summary>Returns whether the id addresses a compiled ability.</summary>
        /// <param name="id">Dense ability id.</param>
        /// <returns>True when the id is in range.</returns>
        public bool Contains(int id) => id >= 0 && id < _abilities.Length;

        internal CompiledAbility GetAbility(int id) => _abilities[id];

        /// <summary>Authored name of an ability. The string is built once at catalog build, so this does not
        /// allocate.</summary>
        /// <param name="id">Compiled ability id.</param>
        /// <returns>Ability name.</returns>
        public string GetName(int id) => _abilities[id].Name;
    }
}
