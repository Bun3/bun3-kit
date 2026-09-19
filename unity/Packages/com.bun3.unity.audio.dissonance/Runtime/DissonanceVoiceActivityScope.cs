using System;
using System.Threading;
using global::Dissonance;
using global::Dissonance.VAD;

namespace Bun3.Unity.Audio.Dissonance
{
    /// <summary>Owns a Dissonance VAD subscription and publishes worker callbacks for main-thread polling.</summary>
    /// <remarks>
    /// Construct, poll and dispose on the main-thread owner. Detection does not imply an open channel,
    /// successful encoding, delivered packets or permission to transmit. Playback volume has no effect.
    /// </remarks>
    public sealed class DissonanceVoiceActivityScope : IDisposable, IVoiceActivationListener
    {
        private const int ActivationCountShift = 2;
        private const long Active = 1;
        private const long Disposed = 2;
        private const long CountIncrement = 4;
        private const long MaximumCount = long.MaxValue >> ActivationCountShift;
        private readonly Action<IVoiceActivationListener> _unsubscribe;
        private long _state;

        /// <summary>Subscribes explicitly to the supplied SDK component without creating or starting capture objects.</summary>
        public DissonanceVoiceActivityScope(DissonanceComms comms)
        {
            if (comms == null) throw new ArgumentNullException(nameof(comms));
            _unsubscribe = comms.UnsubscribeFromVoiceActivation;
            comms.SubscribeToVoiceActivation(this);
        }

        internal DissonanceVoiceActivityScope(Action<IVoiceActivationListener> subscribe,
            Action<IVoiceActivationListener> unsubscribe)
        {
            if (subscribe == null) throw new ArgumentNullException(nameof(subscribe));
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
            subscribe(this);
        }

        /// <summary>Returns one consistent observation. The activation count survives both polling and disposal.</summary>
        public DissonanceVoiceActivitySnapshot Poll()
        {
            var state = Interlocked.Read(ref _state);
            return new DissonanceVoiceActivitySnapshot((state & Active) != 0, state >> ActivationCountShift);
        }

        /// <summary>Unsubscribes once and prevents synchronous or late callbacks from publishing more activity.</summary>
        public void Dispose()
        {
            while (true)
            {
                var state = Interlocked.Read(ref _state);
                if ((state & Disposed) != 0) return;
                if (Interlocked.CompareExchange(ref _state, (state | Disposed) & ~Active, state) == state)
                    break;
            }
            _unsubscribe(this);
        }

        void IVoiceActivationListener.VoiceActivationStart()
        {
            while (true)
            {
                var state = Interlocked.Read(ref _state);
                if ((state & (Active | Disposed)) != 0) return;
                long next = WithNewActivation(state);
                if (Interlocked.CompareExchange(ref _state, next, state) == state) return;
            }
        }

        private static long WithNewActivation(long state)
        {
            long count = state >> ActivationCountShift;
            long incremented = count == MaximumCount ? state : state + CountIncrement;
            return incremented | Active;
        }

        void IVoiceActivationListener.VoiceActivationStop()
        {
            while (true)
            {
                var state = Interlocked.Read(ref _state);
                if ((state & Disposed) != 0 || (state & Active) == 0) return;
                if (Interlocked.CompareExchange(ref _state, state & ~Active, state) == state) return;
            }
        }
    }
}
