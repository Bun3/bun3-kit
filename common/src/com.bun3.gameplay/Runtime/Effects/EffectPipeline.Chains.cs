// EffectPipeline partial — chain edges, target selection and OnEvent routing.
#nullable enable
using System;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    // Events are never queued: a raise walks the listeners immediately and enqueues applications, so
    // cycles are bounded by the same per-tick apply budget as any other chain and no re-entry is possible.
    public sealed partial class EffectPipeline
    {
        // Chain-firing helper. For each edge matching trigger: evaluate the edge conditions against the
        // origin target's current state plus the firing instance's Source → without a Selector use the
        // origin target as-is, otherwise pick targets via SelectorContext (inheriting the firing
        // instance's Source) → EnqueueApply for each target (source inherited; level is the firing level
        // when LevelRule is Inherit, or the edge's FixedLevel when Fixed).
        private void FireChain(
            CompiledChainEdge[] chains, ChainTrigger trigger, TargetId source, int level,
            TargetId originTargetId, EffectTarget originTarget)
        {
            for (var i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                if (chain.Trigger != trigger) continue;

                var hasSource = _resolver.TryResolve(source, out var sourceTarget) && sourceTarget is not null;
                // Lifecycle chains (OnApplication/OnCompleteNormal/OnCompletePrematurely/OnStackOverflow)
                // have no triggering event; Build also rejects EventMagnitude in a non-OnEvent edge's
                // Conditions, so this value is provably unreachable here.
                if (!EvaluateConditions(chain.Conditions, originTarget, sourceTarget, hasSource, BigNum.Zero)) continue;

                var chainLevel = chain.LevelRule == ChainLevelRule.Inherit ? level : chain.FixedLevel;
                ApplyToSelection(
                    chain.Selector, chain.SelectorParams, chain.EffectId, source, originTargetId, chainLevel, default);
            }
        }

        // The dispatch step shared by chain edges, event chains and ability effects: without a selector the
        // effect goes to the origin, otherwise to every selected target. evt is default outside an event
        // path; its magnitude rides along into the applied effect (one hop only).
        internal void ApplyToSelection(
            ITargetSelector? selector, ReadOnlySpan<BigNum> selectorParams, int effectId, TargetId source,
            TargetId origin, int level, in GameplayEvent evt)
        {
            if (selector is null)
            {
                EnqueueApply(effectId, source, origin, level, evt.Magnitude);
                return;
            }

            // ponytail: target counts are authoring-effect scale, so a 32-slot stack buffer suffices — on overflow only the first 32 are applied.
            Span<TargetId> targets = stackalloc TargetId[32];
            var ctx = new SelectorContext(source, origin, selectorParams, _rng, _resolver, in evt);
            var count = SelectTargets(selector, in ctx, targets);
            for (var k = 0; k < count; k++)
            {
                EnqueueApply(effectId, source, targets[k], level, evt.Magnitude);
            }
        }

        // Counts truncation when a selector's reported total exceeds the buffer and returns the clamped
        // count actually written into results.
        private int SelectTargets(ITargetSelector selector, in SelectorContext ctx, Span<TargetId> results)
        {
            var selected = selector.Select(in ctx, results);
            if (selected <= results.Length) return selected;

            SelectorTruncationCount++;
            return results.Length;
        }

        /// <summary>Number of OnEvent chain edges that fired.</summary>
        internal long EventChainFireCount { get; private set; }

        private void DispatchEvent(TargetId listenerId, ChainListen listen, in GameplayEvent evt)
        {
            if (!_resolver.TryResolve(listenerId, out var listener) || listener is null) return;
            if (listener.EventListenerCount == 0) return;

            var tagCatalog = listener.Tags.Catalog;
            var active = listener.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (!instance.Enabled) continue;

                var effect = _catalog.GetEffect(instance.EffectId);
                if (!effect.HasEventChains) continue;

                var chains = effect.Chains;
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
            CompiledChainEdge chain, EffectInstance instance, TargetId listenerId, EffectTarget listener,
            in GameplayEvent evt)
        {
            var source = instance.Source;
            var hasSource = _resolver.TryResolve(source, out var sourceTarget) && sourceTarget is not null;
            // An OnEvent edge's own conditions may reference EventMagnitude directly (Build rejects it on
            // any non-OnEvent edge), so this is the one place the real, unclamped-to-zero value is used.
            if (!EvaluateConditions(chain.Conditions, listener, sourceTarget, hasSource, evt.Magnitude)) return;

            var chainLevel = chain.LevelRule == ChainLevelRule.Inherit ? instance.Level : chain.FixedLevel;
            EventChainFireCount++;

            // The event magnitude travels exactly one hop: into the effect this chain applies directly.
            // That effect's own chains go through FireChain with no event, so they enqueue zero.
            ApplyToSelection(chain.Selector, chain.SelectorParams, chain.EffectId, source, listenerId, chainLevel, in evt);
        }
    }
}
