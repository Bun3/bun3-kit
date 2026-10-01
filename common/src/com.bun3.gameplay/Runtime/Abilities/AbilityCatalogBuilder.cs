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

            ValidateGrantedAbilitiesResolve(effects, nameToId);

            return new AbilityCatalog(compiled, nameToId);
        }

        // Every EffectDef.GrantedAbilities entry must name an ability in this catalog.
        private static void ValidateGrantedAbilitiesResolve(EffectCatalog effects, Dictionary<string, int> nameToId)
        {
            for (var effectId = 0; effectId < effects.Count; effectId++)
            {
                var effect = effects.GetEffect(effectId);
                var grantedNames = effect.GrantedAbilityNames;
                for (var i = 0; i < grantedNames.Length; i++)
                {
                    if (!nameToId.ContainsKey(grantedNames[i]))
                    {
                        throw new InvalidOperationException(
                            $"Effect '{effect.Name}': grants unknown ability '{grantedNames[i]}'.");
                    }
                }
            }
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

                RejectEventMagnitude(cost.Magnitude, owner, "a cost magnitude");

                var (magBase, magPerLevel, magCalc, magCalcTag, magByLevel, magTail, magIncrement) =
                    MagnitudeCompiler.Resolve(cost.Magnitude, owner, def.MaxLevel, tags, seams, attributes);
                costs[i] = new CompiledCost(
                    cost.AttributeId,
                    new CompiledMagnitude(
                        magBase, magPerLevel, magCalc, magCalcTag, magByLevel, magTail, magIncrement));
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

                    var targetMaxLevel = effects.GetEffect(effectId).MaxLevel;
                    if (targetMaxLevel > 0 && entry.FixedLevel > targetMaxLevel)
                    {
                        throw new InvalidOperationException(
                            $"{owner}: FixedLevel ({entry.FixedLevel}) for effect '{entry.EffectName}' "
                            + $"exceeds MaxLevel ({targetMaxLevel}).");
                    }
                }

                ITargetSelector? selector = null;
                var selectorTag = GameplayTag.None;
                if (!string.IsNullOrEmpty(entry.SelectorTag))
                {
                    selector = MagnitudeCompiler.ResolveTargetSelector(
                        entry.SelectorTag!, owner, tags, seams, out selectorTag);
                }

                if (entry.SelectorTag == BuiltInSelectors.EventSourceTag || entry.SelectorTag == BuiltInSelectors.EventTargetTag)
                {
                    throw new InvalidOperationException(
                        $"{owner}: selector '{entry.SelectorTag}' is only meaningful on an OnEvent chain edge, "
                        + "but ability activation has no event context.");
                }

                if (entry.Origin == AbilityEffectOrigin.Target && def.Target != AbilityTargetMode.Required)
                {
                    throw new InvalidOperationException(
                        $"{owner}: effect '{entry.EffectName}' starts from the target, but the ability's Target is "
                        + $"{def.Target}. Set Target to Required.");
                }

                abilityEffects[i] = new CompiledAbilityEffect(
                    effectId, entry.Origin, selector, selectorTag, entry.SelectorParams.ToArray(), entry.LevelRule,
                    entry.FixedLevel);
            }

            var cooldownEffectId = -1;
            if (!string.IsNullOrEmpty(def.CooldownEffect))
            {
                if (!effects.TryGetId(def.CooldownEffect!, out cooldownEffectId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: references unknown cooldown effect '{def.CooldownEffect}'.");
                }

                if (effects.GetEffect(cooldownEffectId).GrantedTags.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"{owner}: cooldown effect '{def.CooldownEffect}' grants no tag, so it can never gate "
                        + "activation. Add at least one GrantedTags entry to the cooldown effect.");
                }
            }

            if (def.Target != AbilityTargetMode.Required
                && (def.TargetRequiredTags.Count > 0 || def.TargetBlockedTags.Count > 0 || def.TargetConditions.Count > 0))
            {
                throw new InvalidOperationException(
                    $"{owner}: target tags or conditions are set, but the ability's Target is {def.Target}. "
                    + "Set Target to Required.");
            }

            return new CompiledAbility(
                def.Name,
                def.MaxLevel,
                MagnitudeCompiler.ResolveTags(def.AssetTags, owner, tags, "AssetTags"),
                MagnitudeCompiler.ResolveTags(def.RequiredTags, owner, tags, "RequiredTags"),
                MagnitudeCompiler.ResolveTags(def.BlockedTags, owner, tags, "BlockedTags"),
                CompileConditions(def.ActivationConditions, owner, "ActivationConditions", attributes),
                def.Target,
                MagnitudeCompiler.ResolveTags(def.TargetRequiredTags, owner, tags, "TargetRequiredTags"),
                MagnitudeCompiler.ResolveTags(def.TargetBlockedTags, owner, tags, "TargetBlockedTags"),
                CompileConditions(def.TargetConditions, owner, "TargetConditions", attributes),
                costs,
                cooldownEffectId,
                abilityEffects);
        }

        private static CompiledCondition[] CompileConditions(
            List<ConditionDef> defs, string owner, string where, AttributeRegistry attributes)
        {
            var conditions = new CompiledCondition[defs.Count];
            for (var i = 0; i < defs.Count; i++)
            {
                var condition = defs[i];
                RejectEventMagnitude(condition.Left, owner, where);
                RejectEventMagnitude(condition.Right, owner, where);
                MagnitudeCompiler.ValidateOperandAttribute(condition.Left, owner, attributes);
                MagnitudeCompiler.ValidateOperandAttribute(condition.Right, owner, attributes);
                conditions[i] = new CompiledCondition(condition.Left, condition.Op, condition.Right);
            }

            return conditions;
        }

        // An ability activation is never an event path -- there is no GameplayEvent to carry a
        // magnitude, so EventMagnitude here would always resolve to BigNum.Zero and silently zero
        // out whatever it authored (a cost of zero, a condition that always compares against zero).
        private static void RejectEventMagnitude(MagnitudeDef magnitude, string owner, string where)
        {
            if ((magnitude.Base.HasValue && magnitude.Base.Value.Kind == OperandKind.EventMagnitude)
                || (magnitude.PerLevel.HasValue && magnitude.PerLevel.Value.Kind == OperandKind.EventMagnitude))
            {
                throw new InvalidOperationException($"{owner}: EventMagnitude cannot be used in {where}.");
            }
        }

        private static void RejectEventMagnitude(Operand operand, string owner, string where)
        {
            if (operand.Kind == OperandKind.EventMagnitude)
            {
                throw new InvalidOperationException($"{owner}: EventMagnitude cannot be used in {where}.");
            }
        }
    }
}
