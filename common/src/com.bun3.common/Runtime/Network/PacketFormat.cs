#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Common.Network
{
    /// <summary>
    /// 4-byte little-endian length-prefix framing.
    /// Wire format: [length:4(LE)][body:length]
    /// </summary>
    public static class PacketFormat
    {
        /// <summary>Byte size of the length prefix (4).</summary>
        public const int HeaderSize = 4;

        /// <summary>Writes one packet to the stream with a length prefix.</summary>
        public static ValueTask WritePacketAsync(
            Stream stream, ReadOnlyMemory<byte> packet, CancellationToken ct = default) =>
            WritePacketAsync(stream, packet, new byte[HeaderSize], ct);

        /// <summary>Overload reusing a header scratch buffer (length &#8805; 4) to avoid a per-packet header allocation.
        /// The caller must ensure no concurrent calls share the scratch (e.g. serialize sends per connection).</summary>
        public static async ValueTask WritePacketAsync(
            Stream stream, ReadOnlyMemory<byte> packet, byte[] headerScratch, CancellationToken ct = default)
        {
            var length = packet.Length;
            var header = headerScratch;
            header[0] = (byte)length;
            header[1] = (byte)(length >> 8);
            header[2] = (byte)(length >> 16);
            header[3] = (byte)(length >> 24);
            await stream.WriteAsync(header.AsMemory(0, HeaderSize), ct).ConfigureAwait(false);
            if (length > 0)
            {
                await stream.WriteAsync(packet, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reads one packet. A clean EOF on a packet boundary returns null.
        /// EOF mid-packet throws <see cref="EndOfStreamException"/>;
        /// a negative length or one above maxPacketSize throws <see cref="InvalidDataException"/>.
        /// </summary>
        public static ValueTask<byte[]?> ReadPacketAsync(
            Stream stream, int maxPacketSize, CancellationToken ct = default) =>
            ReadPacketAsync(stream, maxPacketSize, new byte[HeaderSize], ct);

        /// <summary>Overload reusing a header scratch buffer (length &#8805; 4) to avoid a per-packet header allocation.
        /// The caller must ensure no concurrent calls share the scratch (e.g. a single receive loop per connection).
        /// The caller owns the returned body array.</summary>
        public static async ValueTask<byte[]?> ReadPacketAsync(
            Stream stream, int maxPacketSize, byte[] headerScratch, CancellationToken ct = default)
        {
            var header = headerScratch;
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
