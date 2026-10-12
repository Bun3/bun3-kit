#nullable enable
namespace Bun3.Chat
{
    /// <summary>
    /// Game rules the relay consults. Implementations run on the relay host.
    /// </summary>
    public interface IChatPolicy
    {
        /// <summary>
        /// Resolves the stable player id and display name for a connection. Return false for unknown senders.
        /// The id must be non-empty, and id plus name must fit <see cref="ChatMessage.MaxIdentityBytes"/> UTF-8 bytes.
        /// </summary>
        bool TryGetSender(ulong senderId, out string playerId, out string name);

        /// <summary>Returns false when the sender may not chat right now; otherwise the channel to stamp.</summary>
        bool TryGetChannel(ulong senderId, out byte channel);

        /// <summary>
        /// Decides delivery. Replayed history is judged with current state, so decide by
        /// <paramref name="channel"/>, not by the sender's current state; the sender id may be stale.
        /// </summary>
        bool CanReceive(ulong recipientId, ulong senderId, byte channel);
    }
}
