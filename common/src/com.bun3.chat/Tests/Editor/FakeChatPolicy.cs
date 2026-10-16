#nullable enable
using System.Collections.Generic;

namespace Bun3.Chat.Tests
{
    internal sealed class FakeChatPolicy : IChatPolicy
    {
        public readonly Dictionary<ulong, (string PlayerId, string Name)> Senders = new Dictionary<ulong, (string, string)>();
        public readonly Dictionary<ulong, byte> Channels = new Dictionary<ulong, byte>();
        public readonly HashSet<(ulong Recipient, byte Channel)> Blocked = new HashSet<(ulong, byte)>();

        public bool TryGetSender(ulong senderId, out string playerId, out string name)
        {
            if (Senders.TryGetValue(senderId, out var sender))
            {
                playerId = sender.PlayerId;
                name = sender.Name;
                return true;
            }
            playerId = name = string.Empty;
            return false;
        }

        public bool TryGetChannel(ulong senderId, out byte channel) => Channels.TryGetValue(senderId, out channel);

        public bool CanReceive(ulong recipientId, ulong senderId, byte channel) => !Blocked.Contains((recipientId, channel));
    }
}
