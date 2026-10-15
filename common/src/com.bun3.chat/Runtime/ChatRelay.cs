#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Bun3.Chat
{
    /// <summary>Why <see cref="ChatRelay.Submit"/> rejected a message.</summary>
    public enum ChatRejectReason : byte
    {
        /// <summary>Accepted.</summary>
        None,
        /// <summary>The text, or the <see cref="ChatRelayOptions.TextFilter"/> result, failed <see cref="ChatText.TryNormalize"/>.</summary>
        InvalidText,
        /// <summary>The policy has no usable identity for the sender.</summary>
        UnknownSender,
        /// <summary>The policy does not allow the sender to chat right now.</summary>
        NotAllowed,
        /// <summary>The sender exceeded the burst limit.</summary>
        RateLimited,
    }

    /// <summary>Relay tuning, validated when the relay is constructed.</summary>
    public sealed class ChatRelayOptions
    {
        /// <summary>Maximum text elements per message. At least 1.</summary>
        public int MaxLength { get; set; } = 100;

        /// <summary>Messages a sender may send within <see cref="BurstWindowMs"/>. At least 1.</summary>
        public int Burst { get; set; } = 5;

        /// <summary>Burst window in milliseconds. At least 1.</summary>
        public long BurstWindowMs { get; set; } = 5000;

        /// <summary>Messages the host keeps in memory; 0 keeps none.</summary>
        public int HistoryCapacity { get; set; }

        /// <summary>History messages replayed to a newly connected peer; 0 replays none. At most <see cref="HistoryCapacity"/>.</summary>
        public int HistoryReplayCount { get; set; }

        /// <summary>
        /// Rewrites normalized text once the policy allows the sender, before the burst limit, for example to mask bad words; null keeps the text.
        /// The result is normalized again, and a result that fails normalization rejects the message as <see cref="ChatRejectReason.InvalidText"/>.
        /// </summary>
        public Func<string, string>? TextFilter { get; set; }
    }

    /// <summary>
    /// Host-side chat pipeline: identifies the sender, normalizes the text, asks the policy for a channel,
    /// optionally rewrites the text, applies the burst limit, and optionally records accepted messages for replay.
    /// </summary>
    public sealed class ChatRelay
    {
        readonly IChatPolicy policy;
        readonly ChatRateLimiter limiter;
        readonly Func<string, string>? textFilter;

        /// <summary>Creates a relay. Invalid <paramref name="options"/> throw <see cref="ArgumentOutOfRangeException"/>.</summary>
        public ChatRelay(IChatPolicy policy, ChatRelayOptions? options = null)
        {
            this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
            options ??= new ChatRelayOptions();
            if (options.MaxLength < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxLength must be at least 1.");
            if (options.Burst < 1) throw new ArgumentOutOfRangeException(nameof(options), "Burst must be at least 1.");
            if (options.BurstWindowMs < 1) throw new ArgumentOutOfRangeException(nameof(options), "BurstWindowMs must be at least 1.");
            if (options.HistoryCapacity < 0) throw new ArgumentOutOfRangeException(nameof(options), "HistoryCapacity must not be negative.");
            if (options.HistoryReplayCount < 0 || options.HistoryReplayCount > options.HistoryCapacity)
                throw new ArgumentOutOfRangeException(nameof(options), "HistoryReplayCount must be between 0 and HistoryCapacity.");
            MaxLength = options.MaxLength;
            HistoryReplayCount = options.HistoryReplayCount;
            limiter = new ChatRateLimiter(options.Burst, options.BurstWindowMs);
            textFilter = options.TextFilter;
            History = options.HistoryCapacity > 0 ? new ChatLog(options.HistoryCapacity) : null;
        }

        /// <summary>Maximum text elements per message.</summary>
        public int MaxLength { get; }

        /// <summary>History messages replayed to a newly connected peer.</summary>
        public int HistoryReplayCount { get; }

        /// <summary>Accepted messages kept by the host, or null when history is off. Shared across channels.</summary>
        public ChatLog? History { get; }

        /// <summary>Validates and stamps a message. On success the message is also added to <see cref="History"/>. On rejection <paramref name="message"/> is <c>default</c> (its <c>Text</c> is null).</summary>
        public ChatRejectReason Submit(ulong senderId, string? text, long nowMs, out ChatMessage message)
        {
            message = default;
            if (!policy.TryGetSender(senderId, out var playerId, out var name) || string.IsNullOrEmpty(playerId) || name == null ||
                Encoding.UTF8.GetByteCount(playerId) + Encoding.UTF8.GetByteCount(name) > ChatMessage.MaxIdentityBytes)
                return ChatRejectReason.UnknownSender;
            if (!ChatText.TryNormalize(text, MaxLength, out var normalized)) return ChatRejectReason.InvalidText;
            if (!policy.TryGetChannel(senderId, out var channel)) return ChatRejectReason.NotAllowed;
            if (textFilter != null && !ChatText.TryNormalize(textFilter(normalized), MaxLength, out normalized)) return ChatRejectReason.InvalidText;
            if (!limiter.TryAcquire(senderId, nowMs)) return ChatRejectReason.RateLimited;
            message = new ChatMessage(senderId, playerId, name, normalized, channel, nowMs);
            History?.Add(message);
            return ChatRejectReason.None;
        }

        /// <summary>Clears <paramref name="result"/> and fills it with the peers the policy lets receive <paramref name="message"/>.</summary>
        public void CollectRecipients(in ChatMessage message, IReadOnlyList<ulong> peers, List<ulong> result)
        {
            result.Clear();
            for (int i = 0; i < peers.Count; i++)
                if (policy.CanReceive(peers[i], message.SenderId, message.Channel)) result.Add(peers[i]);
        }

        /// <summary>
        /// Clears <paramref name="result"/> and fills it with up to <see cref="HistoryReplayCount"/> of the newest
        /// history messages <paramref name="recipientId"/> may receive, oldest first.
        /// </summary>
        public void CollectReplay(ulong recipientId, List<ChatMessage> result)
        {
            result.Clear();
            if (History == null || HistoryReplayCount == 0) return;
            for (int i = History.Count - 1; i >= 0 && result.Count < HistoryReplayCount; i--)
            {
                var message = History[i];
                if (policy.CanReceive(recipientId, message.SenderId, message.Channel)) result.Add(message);
            }
            result.Reverse();
        }

        /// <summary>
        /// Appends <paramref name="source"/> to <see cref="History"/> oldest first, keeping the newest on overflow.
        /// Used when the server role moves to a peer that has its own log. Does nothing when history is off.
        /// </summary>
        public void SeedHistory(ChatLog source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (History == null || ReferenceEquals(source, History)) return;
            for (int i = Math.Max(0, source.Count - History.Capacity); i < source.Count; i++) History.Add(source[i]);
        }

        /// <summary>Drops the sender's rate-limit state, for example on disconnect.</summary>
        public void Forget(ulong senderId) => limiter.Forget(senderId);

        /// <summary>Clears rate-limit state and history, for example when the server stops.</summary>
        public void Clear()
        {
            limiter.Clear();
            History?.Clear();
        }
    }
}
