// EffectPipeline partial — phases 3–5 per target: rebuild, Ongoing toggles, rebuild.
#nullable enable
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Effects
{
    public sealed partial class EffectPipeline
    {
        // Phases 3-5 per target in one pass: rebuild what phases 1-2 dirtied, evaluate Ongoing conditions,
        // rebuild what the toggles dirtied. Equivalent to running each phase across all targets because the
        // three read and write only the target's own state (captured modifier magnitudes, own clamp bounds,
        // source-less Ongoing conditions, own tags and ability grants).
        private void SettleAttributes()
        {
            var count = _resolver.TargetIds.Count;
            for (var t = 0; t < count; t++)
            {
                var target = _resolver.TargetAt(t);
                Track(target);
                target.Attributes.RebuildDirty();
                EvaluateOngoing(target);
                target.Attributes.RebuildDirty();
            }
        }

        // Phase 4: evaluates the target's Ongoing instances in Id order against the Current values rebuilt
        // in phase 3, once per tick. Toggles only when the result changes — OFF reclaims
        // GrantedTags and marks modifiers dirty, ON grants GrantedTags and marks dirty. The instance
        // itself is not removed (RebuildDirty's RebuildSlot skips modifiers of !Enabled instances, so
        // leaving them attached still excludes them from aggregation). GrantedTags is a counted container,
        // so Add/Remove are symmetric.
        private void EvaluateOngoing(EffectTarget target)
        {
            var active = target.OngoingEffects;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                var effect = _catalog.GetEffect(instance.EffectId);

                // Ongoing evaluation runs every tick with no triggering event — always zero. Build
                // already rejects EventMagnitude in OngoingConditions, so this value is unreachable.
                var satisfied = EvaluateConditions(effect.OngoingConditions, target, source: null, hasSource: false, BigNum.Zero);
                if (satisfied == instance.Enabled) continue;

                instance.Enabled = satisfied;
                if (satisfied) target.GrantTags(effect.GrantedTags);
                else target.RevokeTags(effect.GrantedTags);

                AbilityGrantHook?.Invoke(target, instance, satisfied);

                MarkDirtyForModifiers(target, effect);
            }
        }
    }
}
