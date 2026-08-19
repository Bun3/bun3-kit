#nullable enable
using System;
using System.IO;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>Flushes a temp file next to the destination, verifies it by readback, then swaps it in atomically.</summary>
    public static class AtomicFileWriter
    {
        /// <summary>Writes and verifies a temp binary, then replaces the destination file.</summary>
        /// <param name="destinationPath">Final file path to replace.</param>
        /// <param name="write">Action that writes the new binary to the temp stream.</param>
        /// <param name="verify">Action that re-reads and verifies the flushed temp binary.</param>
        /// <exception cref="ArgumentNullException">A path or action is null.</exception>
        public static void WriteVerified(
            string destinationPath,
            Action<Stream> write,
            Action<Stream> verify)
        {
            if (destinationPath is null) throw new ArgumentNullException(nameof(destinationPath));
            if (write is null) throw new ArgumentNullException(nameof(write));
            if (verify is null) throw new ArgumentNullException(nameof(verify));
            var fullPath = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(output);
                    output.Flush(true);
                }

                using (var input = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    verify(input);
                    if (input.ReadByte() != -1)
                    {
                        throw new InvalidDataException("Verification did not read the entire binary.");
                    }
                }

                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
