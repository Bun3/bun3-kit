#nullable enable
using System;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    // Gameplay event emission and OnEvent chain routing. Events are never queued: a raise walks the
    // listeners immediately and enqueues applications, so cycles are bounded by the same per-tick
    // apply budget as any other chain and no re-entry is possible.
    public sealed partial class EffectPipeline
    {
        /// <summary>Number of OnEvent chain edges that fired.</summary>
        internal long EventChainFireCount { get; private set; }

        private void RaiseGameplayEvent(in GameplayEvent evt)
        {
            DispatchEvent(evt.Target, ChainListen.AsTarget, in evt);
            DispatchEvent(evt.Source, ChainListen.AsSource, in evt);
        }

        private void DispatchEvent(TargetId listenerId, ChainListen listen, in GameplayEvent evt)
        {
            if (!_resolver.TryResolve(listenerId, out var listener) || listener is null) return;

            var tagCatalog = listener.Tags.Catalog;
            var active = listener.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (!instance.Enabled) continue;

                var spec = _catalog.GetSpec(instance.SpecId);
                if (!spec.HasEventChains) continue;

                var chains = spec.Chains;
                for (var c = 0; c < chains.Length; c++)
                {
                    var chain = chains[c];
                    if (chain.Trigger != ChainTrigger.OnEvent || chain.Listen != listen) continue;
                    if (!tagCatalog.IsAncestorOrSelf(chain.EventTag, evt.Tag)) continue;

                    FireEventChain(chain, instance, listenerId, listener, in evt);
                }
            }
        }

        // An event chain preserves the listening instance's attribution — the enqueued application's
        // caster (and the source side of condition evaluation) is instance.Source, exactly like
        // FireChain. The event's two parties reach a selector separately, through
        // SelectorContext.EventSource/EventTarget.
        private void FireEventChain(
            CompiledChain chain, EffectInstance instance, TargetId listenerId, EffectTarget listener,
            in GameplayEvent evt)
        {
            var source = instance.Source;
            var hasSource = _resolver.TryResolve(source, out var sourceTarget) && sourceTarget is not null;
            if (!EvaluateConditions(chain.Conditions, listener, sourceTarget, hasSource)) return;

            var chainLevel = chain.LevelRule == ChainLevelRule.Inherit ? instance.Level : chain.FixedLevel;
            EventChainFireCount++;

            if (chain.Selector is null)
            {
                EnqueueApplyWithEvent(chain.EffectId, source, listenerId, chainLevel, in evt);
                return;
            }

            Span<TargetId> targets = stackalloc TargetId[32];
            var selectorCtx = new SelectorContext(
                source, listenerId, chain.SelectorParams, _rng, _resolver,
                evt.Source, evt.Target, evt.Magnitude, evt.NominalMagnitude);
            var count = SelectTargets(chain.Selector, in selectorCtx, targets);

            for (var k = 0; k < count; k++)
            {
                EnqueueApplyWithEvent(chain.EffectId, source, targets[k], chainLevel, in evt);
            }
        }

        // Task 10 widens the pending-apply record to carry the event magnitude; until then this is a
        // thin pass-through that ignores it.
        private void EnqueueApplyWithEvent(
            int specId, TargetId source, TargetId target, int level, in GameplayEvent evt) =>
            EnqueueApply(specId, source, target, level);
    }
}
