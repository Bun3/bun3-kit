#nullable enable
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Bun3.Gameplay.Tags.Cli
{
    internal static class InspectCommand
    {
        internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length != 2 || args[1].StartsWith("--", StringComparison.Ordinal)) return Program.Usage(stderr);
            try
            {
                var bytes = File.ReadAllBytes(args[1]);
                var info = ReadInfo(bytes);
                using var input = new MemoryStream(bytes, false);
                var expectations = string.Equals(info.Version, "0.0.0-dev", StringComparison.Ordinal)
                    ? TagCatalogExpectations.ForDevelopment(info.CatalogId)
                    : TagCatalogExpectations.ForPublished(info.CatalogId, info.Version, info.Fingerprint);
                TagCatalogBinary.Load(input, expectations);
                stdout.WriteLine("Catalog ID: " + info.CatalogId);
                stdout.WriteLine("Version: " + info.Version);
                stdout.WriteLine("Fingerprint: " + Convert.ToHexString(info.Fingerprint).ToLowerInvariant());
                stdout.WriteLine("Tags: " + info.TagCount);
                stdout.WriteLine("Redirects: " + info.RedirectCount);
                return 0;
            }
            catch (IOException exception)
            {
                stderr.WriteLine(exception.Message);
                return 3;
            }
            catch (UnauthorizedAccessException exception)
            {
                stderr.WriteLine(exception.Message);
                return 3;
            }
            catch (Exception exception) when (exception is ArgumentException
                or TagCatalogFormatException or TagCatalogCompatibilityException or InvalidDataException or OverflowException)
            {
                stderr.WriteLine(exception.Message);
                return 2;
            }
        }

        internal static CatalogFileInfo ReadInfo(byte[] bytes)
        {
            if (bytes.Length < 78 || bytes[0] != (byte)'B' || bytes[1] != (byte)'3'
                || bytes[2] != (byte)'D' || bytes[3] != (byte)'K') throw new InvalidDataException("B3DK header가 올바르지 않습니다.");
            var idLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
            var versionLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
            var payloadOffset = checked(78 + idLength + versionLength);
            if (payloadOffset + 4 > bytes.Length) throw new InvalidDataException("B3DK identity 또는 payload가 잘렸습니다.");
            var strictUtf8 = new UTF8Encoding(false, true);
            var catalogId = strictUtf8.GetString(bytes, 78, idLength);
            var version = strictUtf8.GetString(bytes, 78 + idLength, versionLength);
            var fingerprint = bytes.AsSpan(14, 32).ToArray();
            var tagCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(payloadOffset, 4));
            var cursor = payloadOffset + 4;
            for (uint index = 0; index < tagCount; index++)
            {
                if (cursor + 4 > bytes.Length) throw new InvalidDataException("B3DK tag entry가 잘렸습니다.");
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cursor + 2, 2));
                cursor = checked(cursor + 8 + nameLength);
                if (cursor > bytes.Length) throw new InvalidDataException("B3DK tag entry가 잘렸습니다.");
            }

            if (cursor + 4 > bytes.Length) throw new InvalidDataException("B3DK redirect count가 잘렸습니다.");
            var redirectCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4));
            return new CatalogFileInfo(catalogId, version, fingerprint, tagCount, redirectCount);
        }
    }

    internal sealed class CatalogFileInfo
    {
        internal CatalogFileInfo(string catalogId, string version, byte[] fingerprint, uint tagCount, uint redirectCount)
        {
            CatalogId = catalogId;
            Version = version;
            Fingerprint = fingerprint;
            TagCount = tagCount;
            RedirectCount = redirectCount;
        }

        internal string CatalogId { get; }
        internal string Version { get; }
        internal byte[] Fingerprint { get; }
        internal uint TagCount { get; }
        internal uint RedirectCount { get; }
    }
}
