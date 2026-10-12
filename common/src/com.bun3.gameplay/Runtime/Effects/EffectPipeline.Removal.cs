// EffectPipeline partial — premature removal by tag or id, RemoveOnApplyTags, and the shared complete-removal path.
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    public sealed partial class EffectPipeline
    {
        /// <summary>
        /// Removes every active instance on the target whose effect asset tags have any query tag as
        /// self-or-ancestor (in ascending Id order). Modifier detachment, GrantedTags reclamation, and
        /// removal from the active list happen immediately in this call, but the resulting attribute
        /// Current values are not recomputed until the next tick's rebuild phase. Removal takes the same
        /// cleanup path as expiry but fires the <see cref="EffectLifecycleKind.RemovedPrematurely"/> event
        /// and <see cref="ChainTrigger.OnCompletePrematurely"/> chains (never OnCompleteNormal).
        /// </summary>
        /// <param name="target">Target identifier.</param>
        /// <param name="query">Dispel query tag container.</param>
        /// <returns>Number of instances removed; 0 if the target does not resolve.</returns>
        public int RemoveByTags(TargetId target, TagContainer query)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));

            Span<GameplayTag> queryTags = stackalloc GameplayTag[query.ExactKindCount];
            query.CopyExactTags(queryTags);
            return RemoveByTagSpan(target, queryTags);
        }

        internal int RemoveByTagSpan(TargetId target, ReadOnlySpan<GameplayTag> queryTags)
        {
            if (!_resolver.TryResolve(target, out var effectTarget) || effectTarget is null) return 0;

            var tagCatalog = effectTarget.Tags.Catalog;
            var active = effectTarget.ActiveEffects;
            _removalScratch.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (MatchesDispelQuery(_catalog.GetEffect(instance.EffectId).AssetTags, queryTags, tagCatalog))
                    _removalScratch.Add(instance);
            }

            var removed = _removalScratch.Count;
            for (var i = 0; i < removed; i++)
            {
                RemoveInstancePrematurely(effectTarget, _removalScratch[i]);
            }

            _removalScratch.Clear();
            return removed;
        }

        /// <summary>Removes one active instance on the target by id. Modifiers detach immediately in this
        /// call, but attribute Current values recompute on the next tick's rebuild phase. Removal path,
        /// events, and chains match <see cref="RemoveByTags"/> (<see cref="ChainTrigger.OnCompletePrematurely"/>).</summary>
        /// <param name="target">Target identifier.</param>
        /// <param name="instanceId">Instance id to remove.</param>
        /// <returns><see langword="true"/> if the instance was found and removed; <see langword="false"/>
        /// if the target does not resolve or the id does not exist.</returns>
        public bool RemoveById(TargetId target, ulong instanceId)
        {
            if (!_resolver.TryResolve(target, out var effectTarget) || effectTarget is null) return false;

            var active = effectTarget.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].Id != instanceId) continue;
                RemoveInstancePrematurely(effectTarget, active[i]);
                return true;
            }

            return false;
        }

        // Matches when any explicit query tag is self-or-ancestor of any effect asset tag.
        private static bool MatchesDispelQuery(GameplayTag[] assetTags, ReadOnlySpan<GameplayTag> queryTags, TagCatalog tagCatalog)
        {
            for (var q = 0; q < queryTags.Length; q++)
            {
                for (var a = 0; a < assetTags.Length; a++)
                {
                    if (tagCatalog.IsAncestorOrSelf(queryTags[q], assetTags[a])) return true;
                }
            }

            return false;
        }

        private void RemoveInstancePrematurely(EffectTarget target, EffectInstance instance) =>
            RemoveInstanceCompletely(
                target, instance, _catalog.GetEffect(instance.EffectId),
                EffectLifecycleKind.RemovedPrematurely, ChainTrigger.OnCompletePrematurely);

        // RemoveOnApplyTags — just before applying, prematurely removes every active instance whose effect
        // AssetTags hierarchically match these tags. The same effect (about to merge) is excluded.
        private void RemoveOnApply(EffectTarget target, CompiledEffect effect, int incomingEffectId)
        {
            if (effect.RemoveOnApplyTags.Length == 0) return;
            var active = target.ActiveEffects;
            if (active.Count == 0) return;

            var tagCatalog = target.Tags.Catalog;
            _removalScratch.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (instance.EffectId == incomingEffectId) continue;   // merge takes precedence — exclude
                var otherAssetTags = _catalog.GetEffect(instance.EffectId).AssetTags;
                if (MatchesDispelQuery(otherAssetTags, effect.RemoveOnApplyTags, tagCatalog))
                    _removalScratch.Add(instance);
            }

            for (var i = 0; i < _removalScratch.Count; i++)
            {
                RemoveInstancePrematurely(target, _removalScratch[i]);
            }

            _removalScratch.Clear();
        }

        // Shared complete-removal path — used by normal expiry (ExpireInstance) and premature removal
        // (RemoveByTags/RemoveById). Detaches modifiers, reclaims GrantedTags, removes from the active
        // list, returns the instance to the pool, then raises the eventKind lifecycle event and fires the
        // chainTrigger chains. The firing instance's Source and Level are inherited as-is.
        private void RemoveInstanceCompletely(
            EffectTarget target, EffectInstance instance, CompiledEffect effect,
            EffectLifecycleKind eventKind, ChainTrigger chainTrigger)
        {
            var source = instance.Source;
            var level = instance.Level;

            target.Attributes.DetachModifiers(instance);
            target.RevokeTags(effect.GrantedTags);

            AbilityGrantHook?.Invoke(target, instance, false);

            target.RemoveActive(instance, effect);
            RaiseEffectEvent(new EffectLifecycleEvent(eventKind, target.Id, instance.Id, instance.EffectId, instance.Stack, instance.Source, instance.Level));
            EffectInstance.Return(_instancePool, instance);

            FireChain(effect.Chains, chainTrigger, source, level, target.Id, target);
        }
    }
}
