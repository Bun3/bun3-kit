using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Transport.Tcp
{
    /// <summary>
    /// 4바이트 리틀엔디언 길이 프리픽스 프레이밍.
    /// 와이어 형식: [length:4(LE)][body:length]
    /// </summary>
    public static class PacketFormat
    {
        /// <summary>길이 프리픽스의 바이트 크기(4).</summary>
        public const int HeaderSize = 4;

        /// <summary>패킷 하나를 길이 프리픽스와 함께 스트림에 쓴다.</summary>
        public static async ValueTask WritePacketAsync(
            Stream stream, ReadOnlyMemory<byte> packet, CancellationToken ct = default)
        {
            var length = packet.Length;
            var header = new byte[HeaderSize];
            header[0] = (byte)length;
            header[1] = (byte)(length >> 8);
            header[2] = (byte)(length >> 16);
            header[3] = (byte)(length >> 24);
            await stream.WriteAsync(header.AsMemory(), ct).ConfigureAwait(false);
            if (length > 0)
            {
                await stream.WriteAsync(packet, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 패킷 하나를 읽는다. 패킷 경계에서의 깨끗한 EOF는 null을 반환한다.
        /// 패킷 도중 EOF는 <see cref="EndOfStreamException"/>,
        /// 길이가 음수이거나 maxPacketSize 초과면 <see cref="InvalidDataException"/>.
        /// </summary>
        public static async ValueTask<byte[]?> ReadPacketAsync(
            Stream stream, int maxPacketSize, CancellationToken ct = default)
        {
            var header = new byte[HeaderSize];
            var got = await ReadExactAsync(stream, header, HeaderSize, allowCleanEof: true, ct).ConfigureAwait(false);
            if (got == 0)
            {
                return null;
            }

            var length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (length < 0 || length > maxPacketSize)
            {
                throw new InvalidDataException($"Packet length {length} is out of range (max {maxPacketSize}).");
            }

            var body = new byte[length];
            if (length > 0)
            {
                await ReadExactAsync(stream, body, length, allowCleanEof: false, ct).ConfigureAwait(false);
            }

            return body;
        }

        private static async ValueTask<int> ReadExactAsync(
            Stream stream, byte[] buffer, int count, bool allowCleanEof, CancellationToken ct)
        {
            var total = 0;
            while (total < count)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    if (allowCleanEof && total == 0)
                    {
                        return 0;
                    }

                    throw new EndOfStreamException("Stream ended mid-packet.");
                }

                total += n;
            }

            return total;
        }
    }
}
