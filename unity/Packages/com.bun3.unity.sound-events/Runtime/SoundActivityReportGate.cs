using System;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Applies a session-local sequence watermark and a host-clock report interval.</summary>
    public sealed class SoundActivityReportGate
    {
        private readonly double _minimumInterval;
        private double _lastTime;
        private double _lastAdmittedTime = double.NegativeInfinity;
        private ulong _lastSequence;
        private bool _hasSequence;

        /// <summary>Gets whether the gate has consumed a report, including a rate-limited report.</summary>
        public bool HasSeenReport => _hasSequence;

        /// <summary>Gets the latest consumed sequence; check HasSeenReport before interpreting zero.</summary>
        public ulong LastSequence => _lastSequence;

        /// <summary>Creates a gate with a finite nonnegative minimum interval.</summary>
        public SoundActivityReportGate(double minimumInterval)
        {
            if (double.IsNaN(minimumInterval) || double.IsInfinity(minimumInterval) || minimumInterval < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumInterval));
            _minimumInterval = minimumInterval;
        }

        /// <summary>Consumes fresh sequences even when rate limited; bypass permits immediate cleanup reports.</summary>
        public bool TryAccept(ulong sequence, double now, bool bypassInterval = false)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < _lastTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            _lastTime = now;
            if (_hasSequence && sequence <= _lastSequence) return false;
            _hasSequence = true;
            _lastSequence = sequence;
            if (bypassInterval) return true;
            if (now - _lastAdmittedTime < _minimumInterval) return false;
            _lastAdmittedTime = now;
            return true;
        }
    }
}
