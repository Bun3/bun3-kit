#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay
{
    // Ability activation. Gate checks and cost payment are synchronous so that repeated activation
    // within one tick sees the spent resources; the ability's effects go through the normal apply
    // queue, and the cooldown effect is applied immediately so its granted tags gate the next call.
    public sealed partial class World
    {
        /// <summary>Attempts to activate a granted ability on a unit.</summary>
        /// <param name="unitId">Unit activating the ability.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns><see cref="AbilityActivationResult.Success"/> when every gate passed, otherwise the
        /// reason the activation was refused. Gate failures are results, not exceptions.</returns>
        /// <remarks>Costs are checked and paid against each attribute's Base, not its Current — a
        /// modifier on a cost attribute does not fund the spend.</remarks>
        public AbilityActivationResult TryActivateAbility(TargetId unitId, int abilityId)
        {
            if (!TryGetUnit(unitId, out var unit) || unit is null) return AbilityActivationResult.UnknownUnit;
            if (!_abilities.Contains(abilityId)) return AbilityActivationResult.UnknownAbility;

            var level = unit.Abilities.GetLevel(abilityId);
            if (level == 0) return AbilityActivationResult.NotGranted;

            var ability = _abilities.GetAbility(abilityId);

            var required = ability.RequiredTags;
            for (var i = 0; i < required.Length; i++)
            {
                if (!unit.Tags.Has(required[i])) return AbilityActivationResult.MissingTag;
            }

            var blocked = ability.BlockedTags;
            for (var i = 0; i < blocked.Length; i++)
            {
                if (unit.Tags.Has(blocked[i])) return AbilityActivationResult.BlockedByTag;
            }

            if (!Pipeline.CheckConditions(ability.ActivationConditions, unit.Effects, unit.Effects, hasSource: true))
            {
                return AbilityActivationResult.ConditionFailed;
            }

            var costs = ability.Costs;
            Span<BigNum> amounts = stackalloc BigNum[costs.Length];
            for (var i = 0; i < costs.Length; i++)
            {
                amounts[i] = Pipeline.ComputeMagnitude(
                    costs[i].Magnitude, unit.Effects, unit.Effects, hasSource: true, level);
                if (unit.Attributes.GetBase(costs[i].AttributeId) < amounts[i])
                {
                    return AbilityActivationResult.CostInsufficient;
                }
            }

            for (var i = 0; i < costs.Length; i++)
            {
                unit.Attributes.AddBase(costs[i].AttributeId, -amounts[i]);
            }

            if (ability.CooldownEffectId >= 0)
            {
                Pipeline.ApplyImmediate(ability.CooldownEffectId, unitId, unitId, level);
            }

            ApplyAbilityEffects(ability, unitId, level);
            return AbilityActivationResult.Success;
        }

        private void ApplyAbilityEffects(CompiledAbility ability, TargetId unitId, int level)
        {
            // ponytail: target counts are authoring-effect scale, so a 32-slot stack buffer suffices -- on overflow only the first 32 are applied.
            // Hoisted above the loop: stack memory is released at method exit, not per iteration, so
            // an alloc per effect entry would grow the frame with the entry count instead of staying fixed.
            Span<TargetId> targets = stackalloc TargetId[32];

            var effects = ability.Effects;
            for (var i = 0; i < effects.Length; i++)
            {
                var entry = effects[i];
                var effectLevel = entry.LevelRule == ChainLevelRule.Inherit ? level : entry.FixedLevel;

                if (entry.Selector is null)
                {
                    Pipeline.EnqueueApply(entry.EffectId, unitId, unitId, effectLevel);
                    continue;
                }

                var ctx = new SelectorContext(
                    unitId, unitId, entry.SelectorParams, Pipeline.Rng, this,
                    eventSource: default, eventTarget: default,
                    eventMagnitude: BigNum.Zero, eventNominalMagnitude: BigNum.Zero);
                var count = Pipeline.SelectTargets(entry.Selector, in ctx, targets);

                for (var k = 0; k < count; k++)
                {
                    Pipeline.EnqueueApply(entry.EffectId, unitId, targets[k], effectLevel);
                }
            }
        }
    }
}
