#nullable enable
using System;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Bun3.Gameplay.TagSource.Tasks
{
    /// <summary>Extracts Native GameplayTag metadata from evaluated MSBuild items.</summary>
    public sealed class ExtractNativeGameplayTagsTask : Task
    {
        /// <summary>Evaluated Compile items.</summary>
        [Required]
        public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>Evaluated ReferencePath items.</summary>
        [Required]
        public ITaskItem[] References { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>Path of the built target assembly.</summary>
        [Required]
        public string TargetPath { get; set; } = string.Empty;

        /// <summary>Output path of the source metadata JSON.</summary>
        [Required]
        public string OutputPath { get; set; } = string.Empty;

        /// <inheritdoc />
        public override bool Execute()
        {
            try
            {
                var sourcePaths = Array.ConvertAll(Sources, item => item.GetMetadata("FullPath"));
                var referencePaths = Array.ConvertAll(References, item => item.GetMetadata("FullPath"));
                var assemblyName = Path.GetFileNameWithoutExtension(TargetPath);
                var result = NativeTagMetadataExtractor.Extract(sourcePaths, referencePaths, assemblyName);
                if (!result.Succeeded)
                {
                    foreach (var diagnostic in result.Diagnostics) Log.LogError(diagnostic);
                    return false;
                }

                WriteAtomically(OutputPath, result.MetadataJson);
                return true;
            }
            catch (Exception exception)
            {
                Log.LogErrorFromException(exception, true);
                return false;
            }
        }

        private static void WriteAtomically(string destination, string contents)
        {
            var fullPath = Path.GetFullPath(destination);
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(true);
                }

                using (var staged = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    NativeTagMetadataValidator.Validate(staged);
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
