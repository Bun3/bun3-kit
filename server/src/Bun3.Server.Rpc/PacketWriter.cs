using System;
using Google.Protobuf;

namespace Bun3.Server.Rpc
{
    /// <summary>Assembles packets as channel byte + serialized message body.</summary>
    internal static class PacketWriter
    {
        /// <summary>Serializes the message with a 1-byte channel prefix.
        /// Serializes directly into the final array — no temporary array or copy.</summary>
        public static byte[] Wrap(byte channel, IMessage message)
        {
            var packet = new byte[1 + message.CalculateSize()];
            packet[0] = channel;
            message.WriteTo(packet.AsSpan(1));
            return packet;
        }
    }
}
