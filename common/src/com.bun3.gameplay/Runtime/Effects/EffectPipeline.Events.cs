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

        private GameplayEvent[] _gameplayEvents = new GameplayEvent[16];
        private int _gameplayEventCount;

        /// <summary>Gameplay events raised since the buffer was last cleared, in raise order — the single
        /// source for damage numbers, combat logs and network broadcast.</summary>
        public ReadOnlySpan<GameplayEvent> PendingGameplayEvents => _gameplayEvents.AsSpan(0, _gameplayEventCount);

        /// <summary>Clears <see cref="PendingGameplayEvents"/>.</summary>
        public void ClearGameplayEvents() => _gameplayEventCount = 0;

        private void RaiseGameplayEvent(in GameplayEvent evt)
        {
            if (_gameplayEventCount == _gameplayEvents.Length) Array.Resize(ref _gameplayEvents, _gameplayEvents.Length * 2);
            _gameplayEvents[_gameplayEventCount++] = evt;
            DispatchEvent(evt.Target, ChainListen.AsTarget, in evt);
            DispatchEvent(evt.Source, ChainListen.AsSource, in evt);
        }

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

            if (chain.Selector is null)
            {
                EnqueueApplyWithEvent(chain.EffectId, source, listenerId, chainLevel, in evt);
                return;
            }

            // ponytail: target counts are authoring-effect scale, so a 32-slot stack buffer suffices -- on overflow only the first 32 are applied.
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

        // The event magnitude travels exactly one hop: into the effect this OnEvent chain applies
        // directly. Anything that effect's own chains apply in turn goes through FireChain, which always
        // enqueues BigNum.Zero.
        private void EnqueueApplyWithEvent(
            int effectId, TargetId source, TargetId target, int level, in GameplayEvent evt) =>
            EnqueueApply(effectId, source, target, level, evt.Magnitude);
    }
}
