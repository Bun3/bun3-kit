#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.TagSource.Tasks;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Tags.Cli
{
    internal static class ExtractNativeCommand
    {
        private static readonly string[] OutputOption = { "--output" };

        internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (!CliArguments.TryParse(args, 1, Array.Empty<string>(), OutputOption, false, true, out var parsed)
                || parsed.Get("--output") is not string outputPath || parsed.Positionals.Count == 0)
            {
                return Program.Usage(stderr);
            }

            try
            {
                var references = GetReferences();
                var result = NativeTagMetadataExtractor.Extract(parsed.Positionals, references, "Bun3.Gameplay.NativeExtraction");
                if (!result.Succeeded)
                {
                    foreach (var diagnostic in result.Diagnostics) stderr.WriteLine(diagnostic);
                    return 2;
                }

                var bytes = new UTF8Encoding(false).GetBytes(result.MetadataJson);
                AtomicFileWriter.WriteVerified(
                    outputPath,
                    stream => stream.Write(bytes, 0, bytes.Length),
                    stream => TagSourceJson.LoadMetadata(stream, Path.GetFullPath(outputPath)));
                stdout.WriteLine(Path.GetFullPath(outputPath));
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
            catch (Exception exception) when (exception is ArgumentException or TagCatalogException or InvalidDataException)
            {
                stderr.WriteLine(exception.Message);
                return 2;
            }
        }

        private static IReadOnlyList<string> GetReferences()
        {
            var result = new List<string>();
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            {
                result.AddRange(trusted.Split(Path.PathSeparator));
            }

            result.Add(typeof(GameplayTagSourceAttribute).Assembly.Location);
            return result;
        }
    }
}
