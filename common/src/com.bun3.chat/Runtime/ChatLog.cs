#nullable enable
using System;

namespace Bun3.Chat
{
    /// <summary>
    /// Fixed-capacity ring of recent messages; the oldest message is overwritten when full.
    /// Not thread-safe: consume it from the main thread.
    /// </summary>
    public sealed class ChatLog
    {
        readonly ChatMessage[] items;
        int start;

        /// <summary>Creates a log that holds at most <paramref name="capacity"/> messages.</summary>
        public ChatLog(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            items = new ChatMessage[capacity];
        }

        /// <summary>Maximum number of messages kept.</summary>
        public int Capacity => items.Length;

        /// <summary>Number of messages currently kept.</summary>
        public int Count { get; private set; }

        /// <summary>Messages added since the last <see cref="MarkRead"/>, capped at <see cref="Capacity"/>.</summary>
        public int Unread { get; private set; }

        /// <summary>
        /// Total number of <see cref="Add"/> calls since construction; <see cref="Clear"/> does not reset it.
        /// Compare it with <see cref="Count"/> to tell how many messages were appended and how many fell off the front.
        /// </summary>
        public long TotalAdded { get; private set; }

        /// <summary>Raised after <see cref="Add"/>, after a <see cref="MarkRead"/> that changed <see cref="Unread"/>, and after <see cref="Clear"/>.</summary>
        public event Action? Changed;

        /// <summary>The message at <paramref name="index"/>; 0 is the oldest.</summary>
        public ChatMessage this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                return items[(start + index) % items.Length];
            }
        }

        /// <summary>Appends a message, overwriting the oldest when full.</summary>
        public void Add(in ChatMessage message)
        {
            if (Count < items.Length)
            {
                items[(start + Count) % items.Length] = message;
                Count++;
            }
            else
            {
                items[start] = message;
                start = (start + 1) % items.Length;
            }
            if (Unread < items.Length) Unread++;
            TotalAdded++;
            Changed?.Invoke();
        }

        /// <summary>Resets <see cref="Unread"/> to zero.</summary>
        public void MarkRead()
        {
            if (Unread == 0) return;
            Unread = 0;
            Changed?.Invoke();
        }

        /// <summary>Removes every message. <see cref="TotalAdded"/> is kept.</summary>
        public void Clear()
        {
            Array.Clear(items, 0, items.Length);
            start = 0;
            Count = 0;
            Unread = 0;
            Changed?.Invoke();
        }
    }
}
