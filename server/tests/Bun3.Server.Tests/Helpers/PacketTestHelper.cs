using Google.Protobuf;

namespace Bun3.Server.Tests.Helpers;

/// <summary>Helper for assembling [channel:1][protobuf] test packets.</summary>
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
