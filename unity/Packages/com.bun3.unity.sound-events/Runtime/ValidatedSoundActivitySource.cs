using System;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Validates one owner's ordered activity reports and translates accepted reports into events.</summary>
    public sealed class ValidatedSoundActivitySource : IDisposable
    {
        private readonly SoundEventSession _session;
        private readonly ulong _ownerId;
        private readonly SoundActivityLease _lease;
        private readonly SoundActivityReportGate _gate;
        private bool _disposed;
        private bool _busy;

        /// <summary>Creates an activity source bound to one session and owner identity.</summary>
        public ValidatedSoundActivitySource(SoundEventSession session, ulong ownerId, double leaseSeconds, double minimumReportInterval)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _ownerId = ownerId;
            _gate = new SoundActivityReportGate(minimumReportInterval);
            _lease = session.CreateActivityLease(ownerId, leaseSeconds);
        }

        /// <summary>
        /// Validates transport identity, ordering, throttling, and host policy before updating activity.
        /// Accepted stops preserve speech that has not reached a world tick as a single pulse.
        /// </summary>
        public bool Report(ulong senderId, ulong sessionId, ulong sequence, bool active,
            bool hadActivation, double now, bool allowed, SoundActivityData hostData)
        {
            EnsureNotBusy();
            if (_disposed || _session.IsDisposed || senderId != _ownerId || sessionId != _session.SessionId)
                return false;
            _session.EnsureMutable();
            _busy = true;
            try { return ReportValidated(sequence, active, hadActivation, now, allowed, hostData); }
            finally { _busy = false; }
        }

        /// <summary>Expires activity or revokes host policy without requiring another report.</summary>
        public void Tick(double now, bool allowed)
        {
            EnsureNotBusy();
            if (_disposed || _session.IsDisposed) return;
            _busy = true;
            try { _lease.Tick(now, allowed); }
            finally { _busy = false; }
        }

        /// <summary>Ends activity while retaining no further connection to the event session.</summary>
        public void Dispose()
        {
            EnsureNotBusy();
            if (_disposed) return;
            _busy = true;
            try
            {
                _lease.Dispose();
                _disposed = true;
            }
            finally { _busy = false; }
        }

        private bool ReportValidated(ulong sequence, bool active, bool hadActivation, double now,
            bool allowed, SoundActivityData hostData)
        {
            bool fresh = !_gate.HasSeenReport || sequence > _gate.LastSequence;
            bool requiresImmediateCleanup = !allowed || (!active && !hadActivation);
            bool accepted = _gate.TryAccept(sequence, now, requiresImmediateCleanup);
            if (!allowed) _lease.Tick(now, false);
            if (!fresh) return false;
            if (!active || !allowed)
            {
                bool preserveUnevaluated = allowed && (!accepted || !hadActivation);
                _lease.Report(sequence, false, allowed, now, hostData, preserveUnevaluated);
            }
            if (!accepted || !allowed) return false;
            if (active)
            {
                var result = _lease.Report(sequence, true, true, now, hostData);
                return result == SoundActivityReportResult.Started || result == SoundActivityReportResult.Renewed;
            }
            if (!hadActivation) return true;
            return EmitShortActivation(hostData);
        }

        private bool EmitShortActivation(SoundActivityData hostData)
        {
            return _session.EmitPulse(new SoundEventData(_session.SessionId, _session.AllocateEventId(),
                _ownerId, hostData.KindId, hostData.Position, hostData.Intensity, hostData.Radius,
                hostData.HostTick, 0));
        }

        private void EnsureNotBusy()
        {
            if (_busy) throw new InvalidOperationException("Activity source mutation is forbidden during event callbacks.");
        }
    }
}
