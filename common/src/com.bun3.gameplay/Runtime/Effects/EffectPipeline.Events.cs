// EffectPipeline partial — observation buffers (gameplay events, effect lifecycle events, attribute changes) and OnEvent chain routing.
#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    // Events are never queued: a raise walks the listeners immediately and enqueues applications, so
    // cycles are bounded by the same per-tick apply budget as any other chain and no re-entry is possible.
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

        private EffectLifecycleEvent[] _effectEvents = new EffectLifecycleEvent[16];
        private int _effectEventCount;
        private readonly AttributeChangeLog _attributeChanges = new AttributeChangeLog();

        /// <summary>Effect lifecycle events of every target since the tick started, in occurrence order.</summary>
        public ReadOnlySpan<EffectLifecycleEvent> PendingEffectEvents => _effectEvents.AsSpan(0, _effectEventCount);

        /// <summary>Attribute Current changes of every attached target since the tick started, in occurrence
        /// order. A target is attached once this pipeline changes its values; a World attaches its units at spawn.</summary>
        public ReadOnlySpan<AttributeChange> PendingAttributeChanges => _attributeChanges.Pending;

        private void RaiseEffectEvent(in EffectLifecycleEvent evt)
        {
            if (_effectEventCount == _effectEvents.Length) Array.Resize(ref _effectEvents, _effectEvents.Length * 2);
            _effectEvents[_effectEventCount++] = evt;
        }

        /// <summary>Makes this pipeline the target's owner: it advances the target's time and records its
        /// attribute changes. Time owed under a previous owner is settled first and the baseline restarts here;
        /// instances created on another clock count as already existing, so they advance on the next time step.</summary>
        internal void Track(EffectTarget target)
        {
            if (target.Owner == this) return;
            target.Owner?.CatchUp(target);
            target.Owner = this;
            target.AdvancedThrough = EffectTarget.UnknownTick;
            target.NextDueTick = long.MinValue;
            target.Attributes.AttachChangeLog(_attributeChanges, target.Id);
            var active = target.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].CreatedTick >= CurrentTick) active[i].CreatedTick = CurrentTick - 1;
            }
        }

        /// <summary>Releases a target this pipeline owns, settling its time first. Time stops until it is tracked again.</summary>
        internal void Untrack(EffectTarget target)
        {
            if (target.Owner != this) return;
            CatchUp(target);
            target.Owner = null;
            target.AdvancedThrough = EffectTarget.UnknownTick;
            target.NextDueTick = long.MinValue;
            target.Attributes.AttachChangeLog(null, default);
        }

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

            // The event magnitude travels exactly one hop: into the effect this chain applies directly.
            // That effect's own chains go through FireChain with no event, so they enqueue zero.
            ApplyToSelection(chain.Selector, chain.SelectorParams, chain.EffectId, source, listenerId, chainLevel, in evt);
        }
    }
}
