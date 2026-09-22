using System;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Coordinates one bounded event world, session-local identities, and live listener ticks.</summary>
    public sealed class SoundEventSession : IDisposable
    {
        private readonly Func<ulong> _allocateEventId;
        private readonly Receiver[] _receivers;
        private ulong _nextEventId;
        private bool _ticking;
        private bool _refreshing;
        private int _callbackDepth;

        /// <summary>Creates a session with fixed event and listener capacities.</summary>
        public SoundEventSession(ulong sessionId, int eventCapacity, int listenerCapacity)
        {
            if (sessionId == 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            SessionId = sessionId;
            World = new SoundEventWorld(eventCapacity, listenerCapacity);
            _allocateEventId = AllocateEventId;
            _receivers = new Receiver[listenerCapacity];
            for (int i = 0; i < _receivers.Length; i++) _receivers[i] = new Receiver(this);
        }

        /// <summary>Gets the bounded event world owned by this session.</summary>
        public SoundEventWorld World { get; }

        /// <summary>Gets the nonzero identity of this session.</summary>
        public ulong SessionId { get; }

        /// <summary>Gets whether this session has been disposed.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>Allocates the next nonzero event identity in this session.</summary>
        public ulong AllocateEventId()
        {
            EnsureMutable();
            return checked(++_nextEventId);
        }

        /// <summary>Creates a sustained activity lease with a session-local event identity.</summary>
        public SoundActivityLease CreateActivityLease(ulong sourceId, double leaseSeconds)
        {
            EnsureMutable();
            return new SoundActivityLease(World, SessionId, sourceId, leaseSeconds, _allocateEventId);
        }

        /// <summary>Registers a listener or throws when the fixed listener capacity is exhausted.</summary>
        public SoundEventListenerHandle Listen(Vector3 position, float radius, ISoundEventListener listener)
        {
            if (TryListen(position, radius, listener, out var handle)) return handle;
            throw new InvalidOperationException("Sound listener capacity exhausted.");
        }

        /// <summary>Attempts to register a listener without allocating when capacity is exhausted.</summary>
        public bool TryListen(Vector3 position, float radius, ISoundEventListener listener, out SoundEventListenerHandle handle)
        {
            EnsureMutable();
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            for (int i = 0; i < _receivers.Length; i++)
            {
                var receiver = _receivers[i];
                if (receiver.Handle.IsValid) continue;
                if (!World.TryRegisterListener(position, radius, receiver, out handle)) return false;
                receiver.Handle = handle;
                receiver.Target = listener;
                receiver.Live = listener as ISoundEventSessionListener;
                receiver.Radius = radius;
                return true;
            }
            handle = default;
            return false;
        }

        /// <summary>Emits a pulse after refreshing live positions and captures its reached listeners immediately.</summary>
        public bool EmitPulse(SoundEventData data)
        {
            EnsureMutable();
            if (data.SessionId != SessionId)
                throw new ArgumentException("Event belongs to another sound session.", nameof(data));
            RefreshPositions();
            return World.TryEmitPulse(data, out _, captureReach: true);
        }

        /// <summary>Refreshes listener positions and delivers one complete event tick.</summary>
        public void Tick(double now)
        {
            EnsureMutable();
            RefreshPositions();
            _ticking = true;
            Exception error = null;
            try
            {
                BeginListenerTicks(ref error);
                try { World.Tick(now); }
                catch (Exception caught) { error ??= caught; }
            }
            finally
            {
                try
                {
                    EndListenerTicks(ref error);
                }
                finally { _ticking = false; }
            }
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        private void BeginListenerTicks(ref Exception error)
        {
            for (int i = 0; i < _receivers.Length; i++)
            {
                var receiver = _receivers[i];
                if (!receiver.Handle.IsValid || receiver.Live == null) continue;
                try { receiver.Live.BeginSoundTick(); receiver.Began = true; }
                catch (Exception caught) { error ??= caught; }
            }
        }

        private void EndListenerTicks(ref Exception error)
        {
            for (int i = 0; i < _receivers.Length; i++)
            {
                var receiver = _receivers[i];
                if (!receiver.Began) continue;
                receiver.Began = false;
                try { receiver.Live.EndSoundTick(); }
                catch (Exception caught) { error ??= caught; }
            }
        }

        /// <summary>Ends all events and invalidates all listeners.</summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            EnsureMutable();
            IsDisposed = true;
            try { World.Dispose(); }
            finally
            {
                for (int i = 0; i < _receivers.Length; i++)
                {
                    _receivers[i].Target = null;
                    _receivers[i].Live = null;
                }
            }
        }

        private void RefreshPositions()
        {
            _refreshing = true;
            try
            {
                for (int i = 0; i < _receivers.Length; i++)
                {
                    var receiver = _receivers[i];
                    if (!receiver.Handle.IsValid)
                    {
                        receiver.Target = null;
                        receiver.Live = null;
                        continue;
                    }
                    if (receiver.Live != null) receiver.Handle.Update(receiver.Live.SoundPosition, receiver.Radius);
                }
            }
            finally { _refreshing = false; }
        }

        internal void EnsureMutable()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(SoundEventSession));
            if (_ticking || _refreshing || _callbackDepth != 0)
                throw new InvalidOperationException("Sound session mutation is forbidden during delivery.");
        }

        private sealed class Receiver : ISoundEventListener
        {
            private readonly SoundEventSession _session;
            internal ISoundEventListener Target;
            internal ISoundEventSessionListener Live;
            internal SoundEventListenerHandle Handle;
            internal float Radius;
            internal bool Began;

            internal Receiver(SoundEventSession session) { _session = session; }

            public void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase)
            {
                _session._callbackDepth++;
                try { Target.OnSoundEvent(in snapshot, phase); }
                finally { _session._callbackDepth--; }
            }
        }
    }
}
