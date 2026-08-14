#nullable enable
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Bun3.Gameplay.Tags
{
    public sealed partial class TagCatalog
    {
        private static byte[] ComputeFingerprint(
            int schemaVersion,
            string[] canonicalNames,
            ushort[] parents,
            ushort[] subtreeEnds,
            RedirectEntry[] redirects)
        {
            var writer = new CanonicalWriter();
            writer.WriteAscii("BTAG");
            writer.WriteUInt32(checked((uint)schemaVersion));
            writer.WriteUInt32(checked((uint)(canonicalNames.Length - 1)));
            for (var i = 1; i < canonicalNames.Length; i++)
            {
                writer.WriteUtf8(canonicalNames[i]);
                writer.WriteUInt16(parents[i]);
                writer.WriteUInt16(subtreeEnds[i]);
            }

            writer.WriteUInt32(checked((uint)redirects.Length));
            for (var i = 0; i < redirects.Length; i++)
            {
                writer.WriteUtf8(redirects[i].From);
                writer.WriteUtf8(redirects[i].To);
            }

            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(writer.ToArray());
        }

        private sealed class CanonicalWriter
        {
            private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
            private byte[] _buffer = new byte[128];
            private int _length;

            internal void WriteAscii(string value)
            {
                EnsureCapacity(value.Length);
                for (var i = 0; i < value.Length; i++)
                {
                    _buffer[_length++] = checked((byte)value[i]);
                }
            }

            internal void WriteUInt32(uint value)
            {
                EnsureCapacity(4);
                BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_length, 4), value);
                _length += 4;
            }

            internal void WriteUInt16(ushort value)
            {
                EnsureCapacity(2);
                BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_length, 2), value);
                _length += 2;
            }

            internal void WriteUtf8(string value)
            {
                var byteCount = Utf8.GetByteCount(value);
                WriteUInt32(checked((uint)byteCount));
                EnsureCapacity(byteCount);
                _length += Utf8.GetBytes(value, 0, value.Length, _buffer, _length);
            }

            internal byte[] ToArray()
            {
                var result = new byte[_length];
                Array.Copy(_buffer, result, _length);
                return result;
            }

            private void EnsureCapacity(int additional)
            {
                var required = checked(_length + additional);
                if (required <= _buffer.Length) return;
                var capacity = _buffer.Length;
                while (capacity < required) capacity = checked(capacity * 2);
                Array.Resize(ref _buffer, capacity);
            }
        }
    }
}
