using Google.Protobuf;

namespace Bun3.Server.Rpc
{
    /// <summary>채널 바이트 + 직렬화된 메시지 본문으로 패킷을 조립한다.</summary>
    internal static class PacketWriter
    {
        /// <summary>message를 직렬화해 앞에 channel 1바이트를 붙인 패킷을 만든다.</summary>
        public static byte[] Wrap(byte channel, IMessage message)
        {
            var body = message.ToByteArray();
            var packet = new byte[1 + body.Length];
            packet[0] = channel;
            body.CopyTo(packet, 1);
            return packet;
        }
    }
}
