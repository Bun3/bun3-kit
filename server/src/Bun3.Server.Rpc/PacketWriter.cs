using System;
using Google.Protobuf;

namespace Bun3.Server.Rpc
{
    /// <summary>채널 바이트 + 직렬화된 메시지 본문으로 패킷을 조립한다.</summary>
    internal static class PacketWriter
    {
        /// <summary>message를 직렬화해 앞에 channel 1바이트를 붙인 패킷을 만든다.
        /// 최종 배열에 바로 직렬화한다 — 임시 배열/복사 없음.</summary>
        public static byte[] Wrap(byte channel, IMessage message)
        {
            var packet = new byte[1 + message.CalculateSize()];
            packet[0] = channel;
            message.WriteTo(packet.AsSpan(1));
            return packet;
        }
    }
}
