#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Writes a compiled immutable tag catalog as a deterministic schema 1 B3DK file.</summary>
    public static class TagCatalogBinaryWriter
    {
        private const int HeaderSize = 78;
        private const int ChecksumOffset = 46;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Writes a compilation result with identity as a single B3DK file at the current position.</summary>
        /// <param name="output">Writable output stream.</param>
        /// <param name="catalog">Immutable catalog produced by a successful source compilation.</param>
        /// <exception cref="ArgumentNullException">The output or catalog is null.</exception>
        /// <exception cref="ArgumentException">The output stream is not writable or a string exceeds a format limit.</exception>
        /// <exception cref="InvalidOperationException">No explicit catalog identity is present (e.g. legacy JSON).</exception>
        public static void Write(Stream output, TagCatalog catalog)
        {
            if (output is null) throw new ArgumentNullException(nameof(output));
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (!output.CanWrite) throw new ArgumentException("A writable stream is required.", nameof(output));
            if (catalog.CatalogId.Length == 0 || catalog.CatalogVersion.Length == 0)
            {
                throw new InvalidOperationException("Only compilation results with an explicit catalog ID and version can be written as B3DK.");
            }

            var catalogId = Encode(catalog.CatalogId, "Catalog ID");
            var catalogVersion = Encode(catalog.CatalogVersion, "Catalog Version");
            var writer = new BufferWriter(HeaderSize + catalogId.Length + catalogVersion.Length + 128);
            writer.WriteAscii("B3DK");
            writer.WriteUInt16(1);
            writer.WriteUInt16(checked((ushort)catalogId.Length));
            writer.WriteUInt16(checked((ushort)catalogVersion.Length));
            writer.WriteUInt32(0);
            writer.WriteBytes(catalog.Fingerprint);
            writer.WriteZeroes(32);
            writer.WriteBytes(catalogId);
            writer.WriteBytes(catalogVersion);

            var payloadOffset = writer.Length;
            writer.WriteUInt32(checked((uint)catalog.Count));
            for (var index = 1; index <= catalog.Count; index++)
            {
                var tag = catalog.GetRequiredByIndex(checked((ushort)index));
                writer.WriteUInt16(tag.Index);
                writer.WriteString(catalog.GetDisplayName(tag));
                writer.WriteUInt16(catalog.GetParent(tag).Index);
                writer.WriteUInt16(catalog.GetSubtreeEnd(tag));
            }

            var redirects = catalog.CopyCompiledRedirects();
            writer.WriteUInt32(checked((uint)redirects.Length));
            for (var index = 0; index < redirects.Length; index++)
            {
                writer.WriteString(redirects[index].From);
                writer.WriteUInt16(catalog.GetRequired(redirects[index].To).Index);
            }

            writer.PatchUInt32(10, checked((uint)(writer.Length - payloadOffset)));
            var bytes = writer.ToArray();
            using (var sha256 = SHA256.Create())
            {
                var checksum = sha256.ComputeHash(bytes);
                checksum.CopyTo(bytes, ChecksumOffset);
            }

            output.Write(bytes, 0, bytes.Length);
        }

        private static byte[] Encode(string value, string label)
        {
            var byteCount = StrictUtf8.GetByteCount(value);
            if (byteCount > ushort.MaxValue)
            {
                throw new ArgumentException(label + " must not exceed 65,535 bytes in UTF-8.");
            }

            return StrictUtf8.GetBytes(value);
        }

        private sealed class BufferWriter
        {
            private byte[] _buffer;

            internal BufferWriter(int capacity) => _buffer = new byte[capacity];
            internal int Length { get; private set; }

            internal void WriteAscii(string value)
            {
                Ensure(value.Length);
                for (var index = 0; index < value.Length; index++)
                {
                    _buffer[Length++] = checked((byte)value[index]);
                }
            }

            internal void WriteUInt16(ushort value)
            {
                Ensure(2);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(Length, 2), value);
                Length += 2;
            }

            internal void WriteUInt32(uint value)
            {
                Ensure(4);
                BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(Length, 4), value);
                Length += 4;
            }

            internal void WriteString(string value)
            {
                var bytes = Encode(value, "tag name");
                WriteUInt16(checked((ushort)bytes.Length));
                WriteBytes(bytes);
            }

            internal void WriteBytes(ReadOnlySpan<byte> value)
            {
                Ensure(value.Length);
                value.CopyTo(_buffer.AsSpan(Length));
                Length += value.Length;
            }

            internal void WriteZeroes(int count)
            {
                Ensure(count);
                _buffer.AsSpan(Length, count).Clear();
                Length += count;
            }

            internal void PatchUInt32(int offset, uint value) =>
                BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(offset, 4), value);

            internal byte[] ToArray()
            {
                var result = new byte[Length];
                Array.Copy(_buffer, result, Length);
                return result;
            }

            private void Ensure(int additional)
            {
                var required = checked(Length + additional);
                if (required <= _buffer.Length) return;
                var capacity = _buffer.Length;
                while (capacity < required) capacity = checked(capacity * 2);
                Array.Resize(ref _buffer, capacity);
            }
        }
    }
}
