using System;

namespace Bun3.Unity.Audio.Dissonance
{
    /// <summary>Bridges brief VAD gaps without delaying permission revocation.</summary>
    /// <remarks>Use on one owner thread with a monotonic unscaled clock. Reset when routing changes.</remarks>
    public sealed class DissonanceTransmissionGate
    {
        private double _releaseAt = double.NegativeInfinity;

        /// <summary>Returns transmission intent; raw speech observations remain unchanged.</summary>
        /// <param name="speech">Whether speech or a new activation was observed.</param>
        /// <param name="allowed">False revokes transmission immediately and clears pending release.</param>
        /// <param name="now">Finite monotonic time in seconds.</param>
        /// <param name="releaseSeconds">Finite nonnegative time to retain transmission after speech.</param>
        public bool Evaluate(bool speech, bool allowed, double now, double releaseSeconds)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
            if (double.IsNaN(releaseSeconds) || double.IsInfinity(releaseSeconds) || releaseSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(releaseSeconds));
            if (!allowed) { Reset(); return false; }
            if (speech) _releaseAt = now + releaseSeconds;
            return speech || now < _releaseAt;
        }

        /// <summary>Clears previous speech, for example after a room change, disable or disconnect.</summary>
        public void Reset() => _releaseAt = double.NegativeInfinity;
    }
}
