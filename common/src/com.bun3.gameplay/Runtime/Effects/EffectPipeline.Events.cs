// EffectPipeline partial — observation buffers (gameplay events, effect lifecycle events, attribute changes).
#nullable enable
using System;
using Bun3.Gameplay.Attributes;

namespace Bun3.Gameplay.Effects
{
    public sealed partial class EffectPipeline
    {
        internal void ClearObservationBuffers()
        {
            _gameplayEventCount = 0;
            _effectEventCount = 0;
            _attributeChanges.Clear();
        }

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

        private void RaiseGameplayEvent(in GameplayEvent evt)
        {
            if (_gameplayEventCount == _gameplayEvents.Length) Array.Resize(ref _gameplayEvents, _gameplayEvents.Length * 2);
            _gameplayEvents[_gameplayEventCount++] = evt;
            DispatchEvent(evt.Target, ChainListen.AsTarget, in evt);
            DispatchEvent(evt.Source, ChainListen.AsSource, in evt);
        }
    }
}
