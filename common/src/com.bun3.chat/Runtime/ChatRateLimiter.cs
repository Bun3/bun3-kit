#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Chat
{
    /// <summary>
    /// Allows each sender at most <c>burst</c> messages within any <c>windowMs</c> span.
    /// A per-sender ring is allocated on the sender's first message and reused afterwards.
    /// </summary>
    public sealed class ChatRateLimiter
    {
        sealed class Ring
        {
            public readonly long[] Times;
            public int Next;
            public int Count;
            public Ring(int size) => Times = new long[size];
        }

        readonly int burst;
        readonly long windowMs;
        readonly Dictionary<ulong, Ring> rings = new Dictionary<ulong, Ring>();

        /// <summary>Creates a limiter. Both arguments must be positive.</summary>
        public ChatRateLimiter(int burst, long windowMs)
        {
            if (burst < 1) throw new ArgumentOutOfRangeException(nameof(burst));
            if (windowMs < 1) throw new ArgumentOutOfRangeException(nameof(windowMs));
            this.burst = burst;
            this.windowMs = windowMs;
        }

        /// <summary>
        /// Records a message at <paramref name="nowMs"/> and returns true, or returns false without recording
        /// when the sender already sent <c>burst</c> messages in the window. A clock that moved backwards
        /// counts as the window having passed.
        /// </summary>
        public bool TryAcquire(ulong senderId, long nowMs)
        {
            if (!rings.TryGetValue(senderId, out var ring))
            {
                ring = new Ring(burst);
                rings.Add(senderId, ring);
            }
            if (ring.Count == burst)
            {
                long elapsed = nowMs - ring.Times[ring.Next];
                if (elapsed >= 0 && elapsed < windowMs) return false;
            }
            ring.Times[ring.Next] = nowMs;
            ring.Next = (ring.Next + 1) % burst;
            if (ring.Count < burst) ring.Count++;
            return true;
        }

        /// <summary>Drops the sender's history, for example on disconnect.</summary>
        public void Forget(ulong senderId) => rings.Remove(senderId);

        /// <summary>Drops every sender's history.</summary>
        public void Clear() => rings.Clear();
    }
}
