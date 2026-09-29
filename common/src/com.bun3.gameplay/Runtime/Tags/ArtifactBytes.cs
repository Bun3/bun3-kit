// Whole-input reading and self-checksum validation shared by the binary artifact readers.
#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;

namespace Bun3.Gameplay.Tags
{
    internal static class ArtifactBytes
    {
        private const int HashLength = 32;

        // Larger input cannot be a valid artifact, so it fails as a format error instead of dying on OOM.
        internal static byte[] ReadToEnd(Stream input, int maximumBytes, string tooLargeMessage)
        {
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) return output.ToArray();
                if (output.Length + read > maximumBytes)
                {
                    throw new TagCatalogFormatException(tooLargeMessage);
                }

                output.Write(buffer, 0, read);
            }
        }

        // The checksum field holds SHA-256 of the whole artifact computed with that field zeroed.
        internal static void ValidateChecksum(byte[] bytes, int checksumOffset, string mismatchMessage)
        {
            var stored = bytes.AsSpan(checksumOffset, HashLength).ToArray();
            bytes.AsSpan(checksumOffset, HashLength).Clear();
            byte[] actual;
            using (var sha256 = SHA256.Create())
            {
                actual = sha256.ComputeHash(bytes);
            }

            stored.CopyTo(bytes, checksumOffset);
            if (!actual.AsSpan().SequenceEqual(stored))
            {
                throw new TagCatalogFormatException(mismatchMessage);
            }
        }
    }
}
