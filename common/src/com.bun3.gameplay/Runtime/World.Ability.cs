// World partial — ability activation gates, cost payment and effect dispatch.
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

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
            if (IsOnCooldown(unit, ability)) return AbilityActivationResult.OnCooldown;

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

        /// <summary>Reports the longest remaining cooldown among active instances granting any tag of the
        /// ability's cooldown effect — the same tags the activation gate checks.</summary>
        /// <param name="unitId">Unit to query.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <param name="remainingTicks">Remaining ticks of that instance; -1 when an Infinite instance blocks
        /// (it lasts until removed).</param>
        /// <param name="durationTicks">Total ticks of that instance's current lifetime; -1 for Infinite.</param>
        /// <returns>False when the unit or ability is unknown, the ability has no cooldown effect, or no
        /// such instance is active.</returns>
        public bool TryGetCooldown(TargetId unitId, int abilityId, out int remainingTicks, out int durationTicks)
        {
            remainingTicks = 0;
            durationTicks = 0;
            if (!TryGetUnit(unitId, out var unit) || unit is null || !_abilities.Contains(abilityId)) return false;

            var cooldownEffectId = _abilities.GetAbility(abilityId).CooldownEffectId;
            if (cooldownEffectId < 0) return false;

            var cooldownTags = _effects.GetEffect(cooldownEffectId).GrantedTags;
            var tagCatalog = unit.Tags.Catalog;
            var active = unit.Effects.ActiveEffects;
            var found = false;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (!instance.Enabled) continue;
                if (!GrantsAny(_effects.GetEffect(instance.EffectId).GrantedTags, cooldownTags, tagCatalog)) continue;
                if (instance.RemainingTicks < 0)
                {
                    remainingTicks = -1;
                    durationTicks = -1;
                    return true;
                }

                if (instance.RemainingTicks <= remainingTicks) continue;
                remainingTicks = instance.RemainingTicks;
                durationTicks = instance.DurationTicks;
                found = true;
            }

            return found;
        }

        private bool IsOnCooldown(Unit unit, CompiledAbility ability)
        {
            if (ability.CooldownEffectId < 0) return false;
            var tags = _effects.GetEffect(ability.CooldownEffectId).GrantedTags;
            for (var i = 0; i < tags.Length; i++)
            {
                if (unit.Tags.Has(tags[i])) return true;
            }

            return false;
        }

        private static bool GrantsAny(GameplayTag[] granted, GameplayTag[] cooldownTags, TagCatalog tagCatalog)
        {
            for (var c = 0; c < cooldownTags.Length; c++)
            {
                for (var g = 0; g < granted.Length; g++)
                {
                    if (tagCatalog.IsAncestorOrSelf(cooldownTags[c], granted[g])) return true;
                }
            }

            return false;
        }

        private void ApplyAbilityEffects(CompiledAbility ability, TargetId unitId, int level)
        {
            var effects = ability.Effects;
            for (var i = 0; i < effects.Length; i++)
            {
                var entry = effects[i];
                var effectLevel = entry.LevelRule == ChainLevelRule.Inherit ? level : entry.FixedLevel;
                Pipeline.ApplyToSelection(
                    entry.Selector, entry.SelectorParams, entry.EffectId, unitId, unitId, effectLevel, default);
            }
        }
    }
}
