using System;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Tracks outgoing activity heartbeats, immediate stops, short activations, and ordered sequences.</summary>
    public sealed class OutgoingSoundActivityState
    {
        private ulong _sessionId;
        private ulong _sequence;
        private bool _pendingActivation;
        private bool _reportedActive;
        private double _nextReport;

        /// <summary>Gets the session identity currently associated with this state.</summary>
        public ulong SessionId => _sessionId;

        /// <summary>Gets whether an observed activation is waiting to be included in a report.</summary>
        public bool HasPendingActivation => _pendingActivation;

        /// <summary>Resets all report state for a new session identity.</summary>
        public void Reset(ulong sessionId)
        {
            _sessionId = sessionId;
            _sequence = 0;
            _pendingActivation = false;
            _reportedActive = false;
            _nextReport = 0;
        }

        /// <summary>Discards an activation that host policy does not permit reporting.</summary>
        public void ClearPendingActivation() => _pendingActivation = false;

        /// <summary>Builds the next ordered report when a heartbeat, stop, or short activation is due.</summary>
        public bool TryBuildReport(double now, double reportInterval, bool active, bool hadActivation,
            out OutgoingSoundActivityReport report)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (double.IsNaN(reportInterval) || double.IsInfinity(reportInterval) || reportInterval <= 0)
                throw new ArgumentOutOfRangeException(nameof(reportInterval));
            if (hadActivation) _pendingActivation = true;
            bool stop = _reportedActive && !active;
            if ((!stop && now < _nextReport) || (!active && !_pendingActivation && !_reportedActive) ||
                _sequence == ulong.MaxValue)
            {
                report = default;
                return false;
            }
            report = new OutgoingSoundActivityReport(_sessionId, ++_sequence, active, _pendingActivation);
            _reportedActive = active;
            _pendingActivation = false;
            _nextReport = now + reportInterval;
            return true;
        }
    }

    /// <summary>Contains one transport-independent outgoing activity report.</summary>
    public readonly struct OutgoingSoundActivityReport
    {
        /// <summary>Creates an ordered activity report.</summary>
        public OutgoingSoundActivityReport(ulong sessionId, ulong sequence, bool active, bool hadActivation)
        {
            SessionId = sessionId;
            Sequence = sequence;
            Active = active;
            HadActivation = hadActivation;
        }

        /// <summary>Gets the associated sound session identity.</summary>
        public ulong SessionId { get; }

        /// <summary>Gets the strictly increasing session-local report sequence.</summary>
        public ulong Sequence { get; }

        /// <summary>Gets whether activity remains sustained at report time.</summary>
        public bool Active { get; }

        /// <summary>Gets whether activity occurred since the previous report.</summary>
        public bool HadActivation { get; }
    }
}
