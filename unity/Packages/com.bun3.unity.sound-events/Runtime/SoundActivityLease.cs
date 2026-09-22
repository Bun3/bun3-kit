using System;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Owns one source's finite sustained activity in one session.</summary>
    public sealed class SoundActivityLease : IDisposable
    {
        private readonly SoundEventWorld _world;
        private readonly ulong _sessionId;
        private readonly ulong _sourceId;
        private readonly double _leaseSeconds;
        private readonly Func<ulong> _allocateEventId;
        private SoundEventHandle _handle;
        private ulong _eventId;
        private uint _revision;
        private double _expiresAt;
        private double _now;
        private bool _busy;
        private bool _disposed;

        /// <summary>Creates a lease with a finite positive duration and a session-unique identity allocator.</summary>
        public SoundActivityLease(SoundEventWorld world, ulong sessionId, ulong sourceId, double leaseSeconds, Func<ulong> allocateEventId)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _allocateEventId = allocateEventId ?? throw new ArgumentNullException(nameof(allocateEventId));
            if (double.IsNaN(leaseSeconds) || double.IsInfinity(leaseSeconds) || leaseSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(leaseSeconds));
            world.EnsureMutable();
            _sessionId = sessionId; _sourceId = sourceId; _leaseSeconds = leaseSeconds;
            _now = world.CurrentTime;
        }
        /// <summary>Gets whether the current event remains active.</summary>
        public bool IsActive => !_disposed && _handle.IsValid;
        /// <summary>Gets whether any sequence has been accepted.</summary>
        public bool HasAcceptedReport { get; private set; }
        /// <summary>Gets the latest consumed report sequence.</summary>
        public ulong LastSequence { get; private set; }
        /// <summary>Gets the active event identity, or zero when inactive.</summary>
        public ulong CurrentEventId => IsActive ? _eventId : 0;
        /// <summary>
        /// Accepts a strictly increasing report with host-derived payload. Host policy denial ends activity even for stale reports.
        /// A fresh denied, inactive, or capacity-rejected report consumes its sequence. Invalid input consumes nothing.
        /// </summary>
        public SoundActivityReportResult Report(ulong sequence, bool active, bool allowed, double now, SoundActivityData hostData)
            => Report(sequence, active, allowed, now, hostData, preserveUnevaluated: false);

        internal SoundActivityReportResult Report(ulong sequence, bool active, bool allowed, double now,
            SoundActivityData hostData, bool preserveUnevaluated)
        {
            EnsureMutable(now);
            var validatedData = CreatePayload(hostData, 0, 0);
            SoundEventWorld.ValidateData(validatedData);
            bool fresh = !HasAcceptedReport || sequence > LastSequence;
            if (!fresh && allowed) return SoundActivityReportResult.Stale;
            double expiresAt = now + _leaseSeconds;
            if (active && allowed && (double.IsInfinity(expiresAt) || expiresAt <= now))
                throw new ArgumentOutOfRangeException(nameof(now), "Expiry must be finite and strictly greater than report time.");
            _busy = true;
            try
            {
                _now = now;
                if (fresh)
                {
                    HasAcceptedReport = true;
                    LastSequence = sequence;
                }
                if (!allowed)
                {
                    EndCurrent();
                    return SoundActivityReportResult.Denied;
                }
                if (!active)
                {
                    EndCurrent(preserveUnevaluated && now < _expiresAt);
                    return SoundActivityReportResult.Stopped;
                }
                if (IsActive && (now >= _expiresAt || _revision == uint.MaxValue)) EndCurrent();
                return IsActive
                    ? RenewCurrent(hostData, expiresAt)
                    : StartCurrent(hostData, expiresAt);
            }
            finally { _busy = false; }
        }
        /// <summary>Expires activity or revokes policy without requiring another report.</summary>
        public bool Tick(double now, bool allowed = true)
        {
            EnsureMutable(now);
            _now = now;
            if (!IsActive || (allowed && now < _expiresAt)) return false;
            _busy = true;
            try { return EndCurrent(); }
            finally { _busy = false; }
        }
        /// <summary>Ends the current event and permanently disconnects this lease.</summary>
        public void Dispose()
        {
            EnsureNotBusy();
            if (_disposed) return;
            _world.EnsureNotBusy();
            _busy = true;
            try { _disposed = true; EndCurrent(); }
            finally { _busy = false; }
        }

        private SoundActivityReportResult RenewCurrent(SoundActivityData hostData, double expiresAt)
        {
            uint revision = _revision + 1;
            _handle.Update(CreatePayload(hostData, _eventId, revision), expiresAt);
            _revision = revision;
            _expiresAt = expiresAt;
            return SoundActivityReportResult.Renewed;
        }

        private SoundActivityReportResult StartCurrent(SoundActivityData hostData, double expiresAt)
        {
            _handle = default;
            ulong eventId = _allocateEventId();
            if (!_world.TryStart(CreatePayload(hostData, eventId, 0), expiresAt, out _handle))
                return SoundActivityReportResult.CapacityUnavailable;
            _eventId = eventId;
            _revision = 0;
            _expiresAt = expiresAt;
            return SoundActivityReportResult.Started;
        }

        private SoundEventData CreatePayload(SoundActivityData data, ulong eventId, uint revision) =>
            new SoundEventData(_sessionId, eventId, _sourceId, data.KindId, data.Position, data.Intensity, data.Radius, data.HostTick, revision);

        private bool EndCurrent(bool preserveUnevaluated = false)
        {
            var handle = _handle;
            _handle = default;
            _eventId = 0;
            return preserveUnevaluated ? handle.Complete() : handle.Stop();
        }

        private void EnsureNotBusy()
        {
            if (_busy) throw new InvalidOperationException("Activity lease mutation is forbidden during callbacks or identity allocation.");
        }

        private void EnsureMutable(double now)
        {
            EnsureNotBusy();
            if (_disposed) throw new ObjectDisposedException(nameof(SoundActivityLease));
            _world.EnsureMutable();
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < _now || now < _world.CurrentTime)
                throw new ArgumentOutOfRangeException(nameof(now));
        }
    }

    /// <summary>Result of submitting an activity report.</summary>
    public enum SoundActivityReportResult
    {
        /// <summary>The sequence was previously consumed or reordered.</summary>
        Stale,
        /// <summary>Host policy denied activity and ended any current event.</summary>
        Denied,
        /// <summary>An accepted inactive report ended any current event.</summary>
        Stopped,
        /// <summary>A fresh sustained identity was created.</summary>
        Started,
        /// <summary>The current identity's lease was renewed.</summary>
        Renewed,
        /// <summary>The sequence was consumed but event storage was full.</summary>
        CapacityUnavailable
    }
}
