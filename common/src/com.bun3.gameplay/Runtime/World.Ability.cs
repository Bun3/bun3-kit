// World partial — ability activation gates, cost payment and effect dispatch.
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay
{
    public sealed partial class World
    {
        /// <summary>Evaluates every activation gate without side effects — no cost is paid, no cooldown
        /// applied, no effect enqueued, no random number drawn. Safe to call outside <see cref="Tick"/>.</summary>
        /// <param name="unitId">Unit that would activate.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns>The result a queued activation would get if processed now.</returns>
        /// <remarks>Costs are checked against each attribute's Base, not its Current — a modifier on a
        /// cost attribute does not fund the spend.</remarks>
        public AbilityActivationResult CanActivateAbility(TargetId unitId, int abilityId) =>
            EvaluateActivation(unitId, abilityId, commit: false);

        // With commit, a passing activation pays its costs and applies its cooldown immediately, so a
        // second request processed in the same tick sees both.
        private AbilityActivationResult EvaluateActivation(TargetId unitId, int abilityId, bool commit)
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
                if (!unit.Attributes.Has(costs[i].AttributeId)) return AbilityActivationResult.CostInsufficient;
                amounts[i] = Pipeline.ComputeMagnitude(
                    costs[i].Magnitude, unit.Effects, unit.Effects, hasSource: true, level);
                if (unit.Attributes.GetBase(costs[i].AttributeId) < amounts[i])
                {
                    return AbilityActivationResult.CostInsufficient;
                }
            }

            if (!commit) return AbilityActivationResult.Success;

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
