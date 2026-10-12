#nullable enable
using System;

namespace Bun3.Chat
{
    /// <summary>
    /// One relayed chat message. The relay host stamps every field except the text, which it normalizes.
    /// </summary>
    public readonly struct ChatMessage
    {
        /// <summary>Upper bound on the UTF-8 bytes of <see cref="SenderPlayerId"/> plus <see cref="SenderName"/>.</summary>
        public const int MaxIdentityBytes = 448;

        /// <summary>Creates a message. Only the relay host should create messages that peers display.</summary>
        public ChatMessage(ulong senderId, string senderPlayerId, string senderName, string text, byte channel, long timestampMs)
        {
            SenderId = senderId;
            SenderPlayerId = senderPlayerId ?? throw new ArgumentNullException(nameof(senderPlayerId));
            SenderName = senderName ?? throw new ArgumentNullException(nameof(senderName));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Channel = channel;
            TimestampMs = timestampMs;
        }

        /// <summary>Transport connection id of the sender. Routing only: transports reassign it on reconnect.</summary>
        public ulong SenderId { get; }

        /// <summary>Stable player identity supplied by the policy. Use it for "mine", blocking and reporting.</summary>
        public string SenderPlayerId { get; }

        /// <summary>Display name supplied by the policy, never by the sending client.</summary>
        public string SenderName { get; }

        /// <summary>Normalized message text.</summary>
        public string Text { get; }

        /// <summary>Channel chosen by the policy. Its meaning is defined by the game.</summary>
        public byte Channel { get; }

        /// <summary>Unix time in milliseconds, stamped by the relay host.</summary>
        public long TimestampMs { get; }
    }
}
