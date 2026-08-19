#nullable enable
using System;
using System.IO;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>목적지 옆 임시 파일을 flush하고 readback 검증한 뒤 원자적으로 교체합니다.</summary>
    public static class AtomicFileWriter
    {
        /// <summary>임시 binary를 작성하고 검증한 뒤 목적지 파일을 교체합니다.</summary>
        /// <param name="destinationPath">교체할 최종 파일 경로입니다.</param>
        /// <param name="write">새 binary를 임시 스트림에 쓰는 작업입니다.</param>
        /// <param name="verify">flush된 임시 binary를 다시 읽어 검증하는 작업입니다.</param>
        /// <exception cref="ArgumentNullException">경로나 작업이 null인 경우입니다.</exception>
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
                        throw new InvalidDataException("검증 작업이 binary 전체를 읽지 않았습니다.");
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
