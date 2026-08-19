#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Reads a strict schema 1 B3DK stream into an immutable runtime tag catalog.</summary>
    public static class TagCatalogBinary
    {
        private const int HeaderSize = 78;
        private const int FingerprintOffset = 14;
        private const int ChecksumOffset = 46;
        private const int HashLength = 32;
        private const ushort SupportedSchema = 1;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Reads B3DK from the current position to the end, validating format and runtime expectations.</summary>
        /// <param name="input">Readable B3DK stream; seeking is not required.</param>
        /// <param name="expectations">Catalog identity required by the running target.</param>
        /// <returns>The validated immutable tag catalog.</returns>
        /// <exception cref="ArgumentNullException">The input or expectations is null.</exception>
        /// <exception cref="ArgumentException">The input stream is not readable.</exception>
        /// <exception cref="TagCatalogFormatException">The B3DK format, checksum, or payload structure is invalid.</exception>
        /// <exception cref="TagCatalogCompatibilityException">The ID, version, or external fingerprint expectation differs.</exception>
        public static TagCatalog Load(Stream input, TagCatalogExpectations expectations)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (expectations is null) throw new ArgumentNullException(nameof(expectations));
            if (!input.CanRead) throw new ArgumentException("A readable stream is required.", nameof(input));

            var bytes = ReadToEnd(input);
            ValidateMagic(bytes);
            ValidateSchema(bytes);
            if (bytes.Length < HeaderSize)
            {
                throw Format("B3DK header is truncated.");
            }

            var catalogIdLength = ReadUInt16(bytes, 6);
            var catalogVersionLength = ReadUInt16(bytes, 8);
            var payloadLength = ReadUInt32(bytes, 10);
            var expectedLength = (ulong)HeaderSize + catalogIdLength + catalogVersionLength + payloadLength;
            if (expectedLength != (ulong)bytes.Length)
            {
                throw Format("B3DK length fields do not match the actual file length.");
            }

            var versionOffset = HeaderSize + catalogIdLength;
            ValidateChecksum(bytes);

            var catalogId = Decode(bytes, HeaderSize, catalogIdLength, "Catalog ID");
            var catalogVersion = Decode(bytes, versionOffset, catalogVersionLength, "Catalog version");
            if (catalogId.Length == 0 || catalogVersion.Length == 0)
            {
                throw Format("Catalog ID and version must not be empty.");
            }

            ValidateExpectations(bytes, catalogId, catalogVersion, expectations);

            var payloadOffset = versionOffset + catalogVersionLength;
            var catalog = ReadPayload(bytes, payloadOffset, checked((int)payloadLength), catalogId, catalogVersion);
            if (!catalog.MatchesFingerprint(bytes.AsSpan(FingerprintOffset, HashLength)))
            {
                throw Format("Payload semantics do not match the semantic fingerprint.");
            }

            return catalog;
        }

        // Max tag count (65,535) x max name length (255) + redirect/header headroom — larger input
        // cannot be a valid catalog, so fail as a format error instead of dying on OOM.
        private const int MaximumCatalogBytes = 64 * 1024 * 1024;

        private static byte[] ReadToEnd(Stream input)
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) return output.ToArray();
                if (output.Length + read > MaximumCatalogBytes)
                {
                    throw Format("B3DK input exceeds the allowed size.");
                }

                output.Write(buffer, 0, read);
            }
        }

        private static void ValidateMagic(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes[0] != (byte)'B' || bytes[1] != (byte)'3'
                || bytes[2] != (byte)'D' || bytes[3] != (byte)'K')
            {
                throw Format("B3DK magic is missing.");
            }
        }

        private static void ValidateSchema(byte[] bytes)
        {
            if (bytes.Length < 6)
            {
                throw Format("B3DK schema field is truncated.");
            }

            if (ReadUInt16(bytes, 4) != SupportedSchema)
            {
                throw Format("Unsupported B3DK schema.");
            }
        }

        private static void ValidateChecksum(byte[] bytes)
        {
            var stored = bytes.AsSpan(ChecksumOffset, HashLength).ToArray();
            bytes.AsSpan(ChecksumOffset, HashLength).Clear();
            byte[] actual;
            using (var sha256 = SHA256.Create())
            {
                actual = sha256.ComputeHash(bytes);
            }

            stored.CopyTo(bytes, ChecksumOffset);
            if (!actual.AsSpan().SequenceEqual(stored))
            {
                throw Format("B3DK content checksum mismatch.");
            }
        }

        private static void ValidateExpectations(
            byte[] bytes,
            string catalogId,
            string catalogVersion,
            TagCatalogExpectations expectations)
        {
            if (!string.Equals(catalogId, expectations.CatalogId, StringComparison.Ordinal))
            {
                throw new TagCatalogCompatibilityException("Catalog ID does not match the runtime expectation.");
            }

            if (!string.Equals(catalogVersion, expectations.CatalogVersion, StringComparison.Ordinal))
            {
                throw new TagCatalogCompatibilityException("Catalog version does not match the runtime expectation.");
            }

            if (expectations.RequiresFingerprint
                && !bytes.AsSpan(FingerprintOffset, HashLength).SequenceEqual(expectations.ExpectedFingerprint))
            {
                throw new TagCatalogCompatibilityException("Catalog semantic fingerprint does not match the runtime expectation.");
            }
        }

        private static TagCatalog ReadPayload(
            byte[] bytes,
            int payloadOffset,
            int payloadLength,
            string catalogId,
            string catalogVersion)
        {
            var reader = new PayloadReader(bytes, payloadOffset, payloadLength);
            var tagCountValue = reader.ReadUInt32("tag count");
            var remainingAfterCount = reader.Remaining;
            if (tagCountValue > ushort.MaxValue || tagCountValue > (uint)(remainingAfterCount / 8))
            {
                throw Format("Tag count exceeds the file length or runtime limit.");
            }

            var tagCount = checked((int)tagCountValue);
            var canonicalNames = new string[tagCount + 1];
            var parents = new ushort[tagCount + 1];
            var subtreeEnds = new ushort[tagCount + 1];
            canonicalNames[0] = string.Empty;
            var indices = new Dictionary<string, ushort>(tagCount, StringComparer.Ordinal);
            var previousName = string.Empty;
            for (var ordinal = 1; ordinal <= tagCount; ordinal++)
            {
                var index = reader.ReadUInt16("tag index");
                if (index != ordinal)
                {
                    throw Format("Tag indices must ascend from 1 without duplicates.");
                }

                var name = reader.ReadString("tag name");
                ValidateCanonicalName(name, "tag name");
                if (ordinal > 1 && StringComparer.Ordinal.Compare(previousName, name) >= 0)
                {
                    throw Format("Tag names must be in canonical order without duplicates.");
                }

                var parent = reader.ReadUInt16("parent index");
                var subtreeEnd = reader.ReadUInt16("subtree end index");
                var lastDot = name.LastIndexOf('.');
                var expectedParent = (ushort)0;
                if (lastDot >= 0 && !indices.TryGetValue(name.Substring(0, lastDot), out expectedParent))
                {
                    throw Format("Tag's canonical parent is missing at an earlier index.");
                }

                if (parent != expectedParent)
                {
                    throw Format("Tag parent index does not match the canonical hierarchy.");
                }

                canonicalNames[ordinal] = name;
                parents[ordinal] = parent;
                subtreeEnds[ordinal] = subtreeEnd;
                indices.Add(name, index);
                previousName = name;
            }

            ValidateSubtreeEnds(parents, subtreeEnds);

            var redirectCountValue = reader.ReadUInt32("redirect count");
            if (redirectCountValue > (uint)(reader.Remaining / 5) || redirectCountValue > int.MaxValue)
            {
                throw Format("Redirect count exceeds the file length limit.");
            }

            var redirectCount = checked((int)redirectCountValue);
            var redirects = new CompiledRedirect[redirectCount];
            previousName = string.Empty;
            for (var ordinal = 0; ordinal < redirectCount; ordinal++)
            {
                var from = reader.ReadString("redirect source");
                ValidateCanonicalName(from, "redirect source");
                if (ordinal > 0 && StringComparer.Ordinal.Compare(previousName, from) >= 0)
                {
                    throw Format("Redirect sources must be in canonical order without duplicates.");
                }

                var target = reader.ReadUInt16("redirect target");
                if (target == 0 || target > tagCount)
                {
                    throw Format("Redirect target index is outside the active tag range.");
                }

                redirects[ordinal] = new CompiledRedirect(from, canonicalNames[target]);
                previousName = from;
            }

            if (reader.Remaining != 0)
            {
                throw Format("Unparsed bytes remain after the payload.");
            }

            return TagCatalog.CreateCompiled(
                new TagCatalogIdentity(catalogId, catalogVersion),
                canonicalNames,
                parents,
                subtreeEnds,
                redirects);
        }

        private static void ValidateCanonicalName(string name, string label)
        {
            if (!TagName.TryFold(name, out var canonical)
                || !string.Equals(name, canonical, StringComparison.Ordinal))
            {
                throw Format(label + " is not a canonical lowercase tag path.");
            }
        }

        private static void ValidateSubtreeEnds(ushort[] parents, ushort[] actual)
        {
            var expected = new ushort[parents.Length];
            for (var index = 1; index < expected.Length; index++) expected[index] = checked((ushort)index);
            for (var index = expected.Length - 1; index > 0; index--)
            {
                var parent = parents[index];
                if (parent != 0 && expected[index] > expected[parent]) expected[parent] = expected[index];
            }

            for (var index = 1; index < expected.Length; index++)
            {
                if (actual[index] != expected[index])
                {
                    throw Format("Tag subtree end index does not match the canonical hierarchy.");
                }
            }
        }

        private static string Decode(byte[] bytes, int offset, int length, string label)
        {
            try
            {
                return StrictUtf8.GetString(bytes, offset, length);
            }
            catch (DecoderFallbackException exception)
            {
                throw new TagCatalogFormatException(label + " is not strict UTF-8.", exception);
            }
        }

        private static ushort ReadUInt16(byte[] bytes, int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

        private static uint ReadUInt32(byte[] bytes, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

        private static TagCatalogFormatException Format(string message) => new TagCatalogFormatException(message);

        private sealed class PayloadReader
        {
            private readonly byte[] _bytes;
            private readonly int _end;
            private int _offset;

            internal PayloadReader(byte[] bytes, int offset, int length)
            {
                _bytes = bytes;
                _offset = offset;
                _end = checked(offset + length);
            }

            internal int Remaining => _end - _offset;

            internal ushort ReadUInt16(string field)
            {
                Require(2, field);
                var value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(_offset, 2));
                _offset += 2;
                return value;
            }

            internal uint ReadUInt32(string field)
            {
                Require(4, field);
                var value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(_offset, 4));
                _offset += 4;
                return value;
            }

            internal string ReadString(string field)
            {
                var length = ReadUInt16(field + " length");
                if (length == 0) throw Format(field + " length must not be 0.");
                Require(length, field);
                var value = Decode(_bytes, _offset, length, field);
                _offset += length;
                return value;
            }

            private void Require(int length, string field)
            {
                if (length < 0 || length > Remaining)
                {
                    throw Format(field + " extends past the payload boundary.");
                }
            }
        }
    }
}
