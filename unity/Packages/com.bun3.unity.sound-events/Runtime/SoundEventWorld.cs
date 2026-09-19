using System;
using UnityEngine;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Bounded synchronous event world driven by an explicit monotonic clock. Not thread safe.</summary>
    public sealed class SoundEventWorld : IDisposable
    {
        private struct EventSlot
        {
            internal bool Active;
            internal ulong Generation;
            internal SoundEventSnapshot Snapshot;
            internal bool CapturedPulse;
            internal bool Evaluated;
        }
        private struct ListenerSlot
        {
            internal ISoundEventListener Receiver;
            internal ulong Generation;
            internal Vector3 Position;
            internal float Radius;
        }
        private readonly EventSlot[] _events;
        private readonly ListenerSlot[] _listeners;
        private readonly bool[] _reached;
        private readonly ISoundEventReachPolicy _policy;
        private bool _busy;
        private bool _disposed;
        private double _now;
        private Exception _callbackError;

        /// <summary>Creates fixed storage. A null policy selects inclusive sphere contact.</summary>
        public SoundEventWorld(int eventCapacity, int listenerCapacity, ISoundEventReachPolicy reachPolicy = null)
        {
            if (eventCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(eventCapacity));
            if (listenerCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(listenerCapacity));
            _reached = new bool[checked(eventCapacity * listenerCapacity)];
            _events = new EventSlot[eventCapacity];
            _listeners = new ListenerSlot[listenerCapacity];
            _policy = reachPolicy ?? new SphereSoundEventReachPolicy();
        }

        /// <summary>Queues a pulse for the next tick. Optionally captures reach now for existing listener registrations.</summary>
        public bool TryEmitPulse(SoundEventData data, out SoundEventHandle handle, bool captureReach = false)
        {
            EnsureMutable(); ValidateData(data);
            if (!Add(data, false, 0, out handle, out int slot)) return false;
            if (!captureReach) return true;
            _events[slot].CapturedPulse = true;
            _busy = true;
            try
            {
                for (int l = 0; l < _listeners.Length; l++)
                {
                    ref var listener = ref _listeners[l];
                    if (listener.Receiver != null)
                        _reached[slot * _listeners.Length + l] = _policy.CanReach(in _events[slot].Snapshot, listener.Position, listener.Radius);
                }
                return true;
            }
            catch { End(slot); handle = default; throw; }
            finally { _busy = false; }
        }

        /// <summary>Starts a sustained event with a finite, nonnegative absolute expiry.</summary>
        public bool TryStart(SoundEventData data, double expiresAt, out SoundEventHandle handle)
        {
            EnsureMutable(); ValidateData(data); ValidateNonnegative(expiresAt, nameof(expiresAt));
            return Add(data, true, expiresAt, out handle, out _);
        }

        private bool Add(SoundEventData data, bool sustained, double expiresAt, out SoundEventHandle handle, out int slot)
        {
            for (int i = 0; i < _events.Length; i++)
            {
                ref var entry = ref _events[i];
                if (entry.Active || entry.Generation == ulong.MaxValue) continue;
                entry.Generation++;
                entry.Active = true;
                entry.Snapshot = new SoundEventSnapshot(data, sustained, expiresAt);
                entry.CapturedPulse = false;
                entry.Evaluated = false;
                handle = new SoundEventHandle(this, i, entry.Generation);
                slot = i;
                return true;
            }
            handle = default;
            slot = -1;
            return false;
        }

        /// <summary>Registers a listener or throws when listener capacity is exhausted.</summary>
        public SoundEventListenerHandle RegisterListener(Vector3 position, float radius, ISoundEventListener receiver)
        {
            if (TryRegisterListener(position, radius, receiver, out var handle)) return handle;
            throw new InvalidOperationException("Listener capacity exhausted.");
        }

        /// <summary>Registers a listener without throwing when fixed storage is full.</summary>
        public bool TryRegisterListener(Vector3 position, float radius, ISoundEventListener receiver, out SoundEventListenerHandle handle)
        {
            EnsureMutable(); ValidatePosition(position); ValidateNonnegative(radius, nameof(radius));
            if (receiver == null) throw new ArgumentNullException(nameof(receiver));
            for (int i = 0; i < _listeners.Length; i++)
            {
                ref var entry = ref _listeners[i];
                if (entry.Receiver != null || entry.Generation == ulong.MaxValue) continue;
                entry.Generation++;
                entry.Receiver = receiver; entry.Position = position; entry.Radius = radius;
                handle = new SoundEventListenerHandle(this, i, entry.Generation);
                return true;
            }
            handle = default;
            return false;
        }

        /// <summary>Delivers events in slot order at a finite, nonnegative monotonic time. Expiry precedes reach.</summary>
        public void Tick(double now)
        {
            EnsureMutable(); ValidateNonnegative(now, nameof(now));
            if (now < _now) throw new ArgumentOutOfRangeException(nameof(now));
            _now = now;
            _busy = true;
            try
            {
                for (int eventSlot = 0; eventSlot < _events.Length; eventSlot++)
                    DeliverEvent(eventSlot, now);
            }
            finally { _busy = false; }
            ThrowCallbackError();
        }

        private void DeliverEvent(int eventSlot, double now)
        {
            ref var entry = ref _events[eventSlot];
            if (!entry.Active) return;
            if (entry.Snapshot.IsSustained && entry.Snapshot.ExpiresAt <= now)
            {
                End(eventSlot);
                return;
            }

            entry.Evaluated = true;
            for (int listenerSlot = 0; listenerSlot < _listeners.Length; listenerSlot++)
            {
                if (_listeners[listenerSlot].Receiver != null)
                    DeliverToListener(eventSlot, listenerSlot);
            }
            if (!entry.Snapshot.IsSustained) entry.Active = false;
        }

        private void DeliverToListener(int eventSlot, int listenerSlot)
        {
            ref var entry = ref _events[eventSlot];
            ref var listener = ref _listeners[listenerSlot];
            int reachIndex = eventSlot * _listeners.Length + listenerSlot;
            if (entry.CapturedPulse)
            {
                bool captured = _reached[reachIndex];
                _reached[reachIndex] = false;
                if (captured) Notify(listenerSlot, in entry.Snapshot, SoundEventPhase.Pulse);
                return;
            }

            bool reaches;
            try { reaches = _policy.CanReach(in entry.Snapshot, listener.Position, listener.Radius); }
            catch (Exception error)
            {
                _callbackError ??= error;
                return;
            }
            if (!entry.Snapshot.IsSustained)
            {
                if (reaches) Notify(listenerSlot, in entry.Snapshot, SoundEventPhase.Pulse);
                return;
            }

            bool previouslyReached = _reached[reachIndex];
            _reached[reachIndex] = reaches;
            if (reaches)
                Notify(listenerSlot, in entry.Snapshot, previouslyReached ? SoundEventPhase.Update : SoundEventPhase.Enter);
            else if (previouslyReached)
                Notify(listenerSlot, in entry.Snapshot, SoundEventPhase.Exit);
        }

        /// <summary>Ends all events from a source, returning the number removed.</summary>
        public int RemoveSource(ulong sourceId)
        {
            EnsureMutable();
            int count = 0;
            _busy = true;
            try
            {
                for (int i = 0; i < _events.Length; i++)
                    if (_events[i].Active && _events[i].Snapshot.Data.SourceId == sourceId)
                    { End(i); count++; }
            }
            finally { _busy = false; }
            ThrowCallbackError();
            return count;
        }

        /// <summary>Ends all active events and invalidates all registrations. Repeated disposal is harmless.</summary>
        public void Dispose()
        {
            EnsureNotBusy();
            if (_disposed) return;
            _busy = true;
            try
            {
                for (int i = 0; i < _events.Length; i++) if (_events[i].Active) End(i);
                Array.Clear(_listeners, 0, _listeners.Length);
                _disposed = true;
            }
            finally { _busy = false; }
            ThrowCallbackError();
        }

        internal bool IsEventValid(int slot, ulong generation) => !_disposed && _events[slot].Active && _events[slot].Generation == generation;
        internal bool IsListenerValid(int slot, ulong generation) => !_disposed && _listeners[slot].Receiver != null && _listeners[slot].Generation == generation;

        internal bool Stop(int slot, ulong generation, bool preserveUnevaluated = false)
        {
            EnsureNotBusy();
            if (!IsEventValid(slot, generation)) return false;
            ref var entry = ref _events[slot];
            if (preserveUnevaluated && entry.Snapshot.IsSustained && !entry.Evaluated)
            {
                entry.Snapshot = new SoundEventSnapshot(entry.Snapshot.Data, false, 0);
                return true;
            }
            _busy = true;
            try { End(slot); }
            finally { _busy = false; }
            ThrowCallbackError();
            return true;
        }

        internal bool UpdateEvent(int slot, ulong generation, SoundEventData data, double expiresAt)
        {
            EnsureNotBusy();
            if (!IsEventValid(slot, generation) || !_events[slot].Snapshot.IsSustained) return false;
            ValidateData(data); ValidateNonnegative(expiresAt, nameof(expiresAt));
            var previous = _events[slot].Snapshot.Data;
            if (data.SessionId != previous.SessionId || data.EventId != previous.EventId || data.SourceId != previous.SourceId)
                throw new ArgumentException("Event identity cannot change.", nameof(data));
            _events[slot].Snapshot = new SoundEventSnapshot(data, true, expiresAt);
            return true;
        }

        internal bool UpdateListener(int slot, ulong generation, Vector3 position, float radius)
        {
            EnsureNotBusy();
            if (!IsListenerValid(slot, generation)) return false;
            ValidatePosition(position); ValidateNonnegative(radius, nameof(radius));
            _listeners[slot].Position = position; _listeners[slot].Radius = radius;
            return true;
        }

        internal bool ReleaseListener(int slot, ulong generation)
        {
            EnsureNotBusy();
            if (!IsListenerValid(slot, generation)) return false;
            _busy = true;
            try
            {
                for (int e = 0; e < _events.Length; e++)
                {
                    int index = e * _listeners.Length + slot;
                    if (!_reached[index]) continue;
                    _reached[index] = false;
                    if (_events[e].Snapshot.IsSustained) Notify(slot, in _events[e].Snapshot, SoundEventPhase.Exit);
                }
                _listeners[slot].Receiver = null;
            }
            finally { _busy = false; }
            ThrowCallbackError();
            return true;
        }

        private void End(int slot)
        {
            ref var entry = ref _events[slot];
            entry.Active = false;
            for (int l = 0; l < _listeners.Length; l++)
            {
                int index = slot * _listeners.Length + l;
                if (!_reached[index]) continue;
                _reached[index] = false;
                if (entry.Snapshot.IsSustained) Notify(l, in entry.Snapshot, SoundEventPhase.End);
            }
        }

        private void Notify(int listener, in SoundEventSnapshot snapshot, SoundEventPhase phase)
        {
            try { _listeners[listener].Receiver.OnSoundEvent(in snapshot, phase); }
            catch (Exception error) { _callbackError ??= error; }
        }

        private void ThrowCallbackError()
        {
            var error = _callbackError;
            _callbackError = null;
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        internal double CurrentTime => _now;

        internal void EnsureNotBusy()
        {
            if (_busy) throw new InvalidOperationException("World mutation is forbidden during delivery and reach queries.");
        }
        internal void EnsureMutable()
        {
            EnsureNotBusy();
            if (_disposed) throw new ObjectDisposedException(nameof(SoundEventWorld));
        }
        internal static void ValidateData(SoundEventData data)
        {
            ValidatePosition(data.Position);
            ValidateNonnegative(data.Intensity, nameof(data.Intensity));
            ValidateNonnegative(data.Radius, nameof(data.Radius));
        }
        private static void ValidatePosition(Vector3 position)
        {
            if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                float.IsNaN(position.y) || float.IsInfinity(position.y) ||
                float.IsNaN(position.z) || float.IsInfinity(position.z))
                throw new ArgumentOutOfRangeException(nameof(position));
        }
        private static void ValidateNonnegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
