#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Bun3.Gameplay.Tags
{
    /// <summary>엄격한 schema 1 B3DK 스트림을 불변 런타임 태그 카탈로그로 읽습니다.</summary>
    public static class TagCatalogBinary
    {
        private const int HeaderSize = 78;
        private const int FingerprintOffset = 14;
        private const int ChecksumOffset = 46;
        private const int HashLength = 32;
        private const ushort SupportedSchema = 1;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>현재 위치부터 끝까지 B3DK를 읽고 형식과 실행 기대 조건을 검증합니다.</summary>
        /// <param name="input">읽을 수 있는 B3DK 스트림이며 seek를 지원하지 않아도 됩니다.</param>
        /// <param name="expectations">실행 대상이 요구하는 Catalog 식별 정보입니다.</param>
        /// <returns>검증된 불변 태그 카탈로그입니다.</returns>
        /// <exception cref="ArgumentNullException">입력 또는 기대 조건이 null인 경우입니다.</exception>
        /// <exception cref="ArgumentException">입력 스트림을 읽을 수 없는 경우입니다.</exception>
        /// <exception cref="TagCatalogFormatException">B3DK 형식, checksum 또는 payload 구조가 유효하지 않은 경우입니다.</exception>
        /// <exception cref="TagCatalogCompatibilityException">ID, Version 또는 외부 fingerprint 기대가 다른 경우입니다.</exception>
        public static TagCatalog Load(Stream input, TagCatalogExpectations expectations)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (expectations is null) throw new ArgumentNullException(nameof(expectations));
            if (!input.CanRead) throw new ArgumentException("읽을 수 있는 스트림이 필요합니다.", nameof(input));

            var bytes = ReadToEnd(input);
            ValidateMagic(bytes);
            ValidateSchema(bytes);
            if (bytes.Length < HeaderSize)
            {
                throw Format("B3DK header가 잘렸습니다.");
            }

            var catalogIdLength = ReadUInt16(bytes, 6);
            var catalogVersionLength = ReadUInt16(bytes, 8);
            var payloadLength = ReadUInt32(bytes, 10);
            var expectedLength = (ulong)HeaderSize + catalogIdLength + catalogVersionLength + payloadLength;
            if (expectedLength != (ulong)bytes.Length)
            {
                throw Format("B3DK 길이 필드와 실제 파일 길이가 다릅니다.");
            }

            var versionOffset = HeaderSize + catalogIdLength;
            ValidateChecksum(bytes);

            var catalogId = Decode(bytes, HeaderSize, catalogIdLength, "Catalog ID");
            var catalogVersion = Decode(bytes, versionOffset, catalogVersionLength, "Catalog Version");
            if (catalogId.Length == 0 || catalogVersion.Length == 0)
            {
                throw Format("Catalog ID와 Version은 비어 있을 수 없습니다.");
            }

            ValidateExpectations(bytes, catalogId, catalogVersion, expectations);

            var payloadOffset = versionOffset + catalogVersionLength;
            var catalog = ReadPayload(bytes, payloadOffset, checked((int)payloadLength), catalogId, catalogVersion);
            if (!catalog.MatchesFingerprint(bytes.AsSpan(FingerprintOffset, HashLength)))
            {
                throw Format("payload 의미와 semantic fingerprint가 다릅니다.");
            }

            return catalog;
        }

        private static byte[] ReadToEnd(Stream input)
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) return output.ToArray();
                output.Write(buffer, 0, read);
            }
        }

        private static void ValidateMagic(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes[0] != (byte)'B' || bytes[1] != (byte)'3'
                || bytes[2] != (byte)'D' || bytes[3] != (byte)'K')
            {
                throw Format("B3DK magic이 없습니다.");
            }
        }

        private static void ValidateSchema(byte[] bytes)
        {
            if (bytes.Length < 6)
            {
                throw Format("B3DK schema field가 잘렸습니다.");
            }

            if (ReadUInt16(bytes, 4) != SupportedSchema)
            {
                throw Format("지원하지 않는 B3DK schema입니다.");
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
                throw Format("B3DK content checksum이 일치하지 않습니다.");
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
                throw new TagCatalogCompatibilityException("Catalog ID가 실행 기대와 다릅니다.");
            }

            if (!string.Equals(catalogVersion, expectations.CatalogVersion, StringComparison.Ordinal))
            {
                throw new TagCatalogCompatibilityException("Catalog Version이 실행 기대와 다릅니다.");
            }

            if (expectations.RequiresFingerprint
                && !bytes.AsSpan(FingerprintOffset, HashLength).SequenceEqual(expectations.ExpectedFingerprint))
            {
                throw new TagCatalogCompatibilityException("Catalog semantic fingerprint가 실행 기대와 다릅니다.");
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
                throw Format("tag count가 파일 길이 또는 runtime 한계를 넘습니다.");
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
                    throw Format("tag index는 1부터 중복 없이 오름차순이어야 합니다.");
                }

                var name = reader.ReadString("tag name");
                ValidateCanonicalName(name, "tag name");
                if (ordinal > 1 && StringComparer.Ordinal.Compare(previousName, name) >= 0)
                {
                    throw Format("tag name은 중복 없이 canonical 순서여야 합니다.");
                }

                var parent = reader.ReadUInt16("parent index");
                var subtreeEnd = reader.ReadUInt16("subtree end index");
                var lastDot = name.LastIndexOf('.');
                var expectedParent = (ushort)0;
                if (lastDot >= 0 && !indices.TryGetValue(name.Substring(0, lastDot), out expectedParent))
                {
                    throw Format("tag의 canonical 부모가 앞선 index에 없습니다.");
                }

                if (parent != expectedParent)
                {
                    throw Format("tag parent index가 canonical hierarchy와 다릅니다.");
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
                throw Format("redirect count가 파일 길이 한계를 넘습니다.");
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
                    throw Format("redirect source는 중복 없이 canonical 순서여야 합니다.");
                }

                var target = reader.ReadUInt16("redirect target");
                if (target == 0 || target > tagCount)
                {
                    throw Format("redirect target index가 활성 tag 범위를 벗어났습니다.");
                }

                redirects[ordinal] = new CompiledRedirect(from, canonicalNames[target]);
                previousName = from;
            }

            if (reader.Remaining != 0)
            {
                throw Format("payload 뒤에 해석되지 않은 byte가 있습니다.");
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
                throw Format(label + "이 canonical 소문자 태그 경로가 아닙니다.");
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
                    throw Format("tag subtree end index가 canonical hierarchy와 다릅니다.");
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
                throw new TagCatalogFormatException(label + "이 strict UTF-8이 아닙니다.", exception);
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
                if (length == 0) throw Format(field + " length는 0일 수 없습니다.");
                Require(length, field);
                var value = Decode(_bytes, _offset, length, field);
                _offset += length;
                return value;
            }

            private void Require(int length, string field)
            {
                if (length < 0 || length > Remaining)
                {
                    throw Format(field + "이 payload 경계를 벗어났습니다.");
                }
            }
        }
    }
}
