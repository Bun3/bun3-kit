#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>Collects <see cref="AbilityDef"/> declarations and compiles them into an
    /// <see cref="AbilityCatalog"/>. Runs after the effect catalog — abilities reference effects by name.</summary>
    public sealed class AbilityCatalogBuilder
    {
        private readonly List<AbilityDef> _defs = new List<AbilityDef>();

        /// <summary>Adds an ability declaration.</summary>
        /// <param name="def">Declaration to add.</param>
        public void Add(AbilityDef def)
        {
            if (def is null) throw new ArgumentNullException(nameof(def));
            _defs.Add(def);
        }

        /// <summary>Validates and compiles every added declaration.</summary>
        /// <param name="tags">Tag catalog resolving tag paths.</param>
        /// <param name="seams">Seam registry resolving selector tags.</param>
        /// <param name="attributes">Attribute registry validating cost and operand references.</param>
        /// <param name="effects">Effect catalog resolving referenced effect names.</param>
        /// <returns>Compiled catalog.</returns>
        /// <exception cref="InvalidOperationException">Thrown on any validation failure.</exception>
        public AbilityCatalog Build(
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, EffectCatalog effects)
        {
            if (tags is null) throw new ArgumentNullException(nameof(tags));
            if (seams is null) throw new ArgumentNullException(nameof(seams));
            if (attributes is null) throw new ArgumentNullException(nameof(attributes));
            if (effects is null) throw new ArgumentNullException(nameof(effects));

            var nameToId = new Dictionary<string, int>(_defs.Count, StringComparer.Ordinal);
            for (var i = 0; i < _defs.Count; i++)
            {
                var name = _defs[i].Name;
                if (string.IsNullOrEmpty(name))
                    throw new InvalidOperationException($"Ability at index {i} has an empty name.");
                if (nameToId.ContainsKey(name))
                    throw new InvalidOperationException($"Ability '{name}' is declared more than once.");
                nameToId.Add(name, i);
            }

            var compiled = new CompiledAbility[_defs.Count];
            for (var i = 0; i < _defs.Count; i++)
            {
                compiled[i] = Compile(_defs[i], tags, seams, attributes, effects);
            }

            return new AbilityCatalog(compiled, nameToId);
        }

        private static CompiledAbility Compile(
            AbilityDef def, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, EffectCatalog effects)
        {
            var owner = $"Ability '{def.Name}'";

            var costs = new CompiledCost[def.Costs.Count];
            for (var i = 0; i < def.Costs.Count; i++)
            {
                var cost = def.Costs[i];
                if (!attributes.Contains(cost.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: cost references unregistered attribute {cost.AttributeId}.");
                }

                var (magBase, magPerLevel, magCalc, magByLevel, magTail, magIncrement) =
                    MagnitudeCompiler.Resolve(cost.Magnitude, owner, def.MaxLevel, tags, seams, attributes);
                costs[i] = new CompiledCost(
                    cost.AttributeId,
                    new CompiledMagnitude(magBase, magPerLevel, magCalc, magByLevel, magTail, magIncrement));
            }

            var abilityEffects = new CompiledAbilityEffect[def.Effects.Count];
            for (var i = 0; i < def.Effects.Count; i++)
            {
                var entry = def.Effects[i];
                if (!effects.TryGetId(entry.EffectName, out var effectId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: references unknown effect '{entry.EffectName}'.");
                }

                if (entry.LevelRule == ChainLevelRule.Fixed)
                {
                    if (entry.FixedLevel < 1)
                    {
                        throw new InvalidOperationException(
                            $"{owner}: FixedLevel {entry.FixedLevel} for effect '{entry.EffectName}' must be at least 1.");
                    }

                    var targetMaxLevel = effects.GetSpec(effectId).MaxLevel;
                    if (targetMaxLevel > 0 && entry.FixedLevel > targetMaxLevel)
                    {
                        throw new InvalidOperationException(
                            $"{owner}: FixedLevel ({entry.FixedLevel}) for effect '{entry.EffectName}' "
                            + $"exceeds MaxLevel ({targetMaxLevel}).");
                    }
                }

                var selector = string.IsNullOrEmpty(entry.SelectorTag)
                    ? null
                    : MagnitudeCompiler.ResolveTargetSelector(entry.SelectorTag!, owner, tags, seams);
                if (entry.SelectorTag == BuiltInSelectors.EventSourceTag || entry.SelectorTag == BuiltInSelectors.EventTargetTag)
                {
                    throw new InvalidOperationException(
                        $"{owner}: selector '{entry.SelectorTag}' is only meaningful on an OnEvent chain edge, "
                        + "but ability activation has no event context.");
                }

                abilityEffects[i] = new CompiledAbilityEffect(
                    effectId, selector, entry.SelectorParams.ToArray(), entry.LevelRule, entry.FixedLevel);
            }

            var cooldownEffectId = -1;
            if (!string.IsNullOrEmpty(def.CooldownEffect))
            {
                if (!effects.TryGetId(def.CooldownEffect!, out cooldownEffectId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: references unknown cooldown effect '{def.CooldownEffect}'.");
                }
            }

            var conditions = new CompiledCondition[def.ActivationConditions.Count];
            for (var i = 0; i < def.ActivationConditions.Count; i++)
            {
                var condition = def.ActivationConditions[i];
                MagnitudeCompiler.ValidateOperandAttribute(condition.Left, owner, attributes);
                MagnitudeCompiler.ValidateOperandAttribute(condition.Right, owner, attributes);
                conditions[i] = new CompiledCondition(condition.Left, condition.Op, condition.Right);
            }

            return new CompiledAbility(
                def.Name,
                def.MaxLevel,
                MagnitudeCompiler.ResolveTags(def.AssetTags, owner, tags, "AssetTags"),
                MagnitudeCompiler.ResolveTags(def.RequiredTags, owner, tags, "RequiredTags"),
                MagnitudeCompiler.ResolveTags(def.BlockedTags, owner, tags, "BlockedTags"),
                conditions,
                costs,
                cooldownEffectId,
                abilityEffects);
        }
    }
}
