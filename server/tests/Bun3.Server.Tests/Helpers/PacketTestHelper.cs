using Google.Protobuf;

namespace Bun3.Server.Tests.Helpers;

/// <summary>[채널:1][protobuf] 테스트 패킷 조립 헬퍼.</summary>
internal static class PacketTestHelper
{
    public static byte[] Wrap(byte channel, IMessage message)
    {
        var body = message.ToByteArray();
        var packet = new byte[1 + body.Length];
        packet[0] = channel;
        body.CopyTo(packet, 1);
        return packet;
    }
}
