#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Tags.Cli
{
    internal static class CompileCommand
    {
        private static readonly string[] Flags = { "--development", "--published" };
        private static readonly string[] Singletons = { "--catalog-id", "--catalog-version", "--project-root", "--output" };

        internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (!CliArguments.TryParse(args, 1, Flags, Singletons, true, false, out var parsed)) return Program.Usage(stderr);
            var development = parsed.HasFlag("--development");
            var published = parsed.HasFlag("--published");
            var catalogId = parsed.Get("--catalog-id");
            var projectRoot = parsed.Get("--project-root");
            var version = parsed.Get("--catalog-version");
            var explicitOutput = parsed.Get("--output");
            if (development == published || catalogId is null || projectRoot is null) return Program.Usage(stderr);
            if (development && (version is not null || explicitOutput is not null)) return Program.Usage(stderr);
            if (published && (version is null || explicitOutput is null)) return Program.Usage(stderr);
            if (published && TagCatalogVersions.IsDevelopment(version))
            {
                stderr.WriteLine("Published Catalog Version cannot use the reserved development Version.");
                return 2;
            }

            try
            {
                var gamePath = Path.Combine(Path.GetFullPath(projectRoot), "ProjectSettings", "GameplayTags.json");
                var sources = new List<TagSourceDocument> { LoadGame(gamePath) };
                foreach (var sourcePath in parsed.Sources) sources.Add(LoadMetadata(sourcePath));
                var identity = new TagCatalogIdentity(
                    catalogId,
                    development ? TagCatalogVersions.Development : version!);
                var compilation = TagCatalogCompiler.Compile(sources, identity);
                foreach (var diagnostic in compilation.Diagnostics)
                {
                    var writer = diagnostic.Severity == TagCatalogDiagnosticSeverity.Error ? stderr : stdout;
                    writer.WriteLine($"{diagnostic.Code}: {diagnostic.Message} [{diagnostic.SourceId}:{diagnostic.CanonicalPath}] ({diagnostic.Origin})");
                }

                if (!compilation.Succeeded) return 2;
                var catalog = compilation.Catalog!;
                var outputPath = development ? TagCatalogDevelopmentPath.Get(catalogId) : Path.GetFullPath(explicitOutput!);
                if (published && File.Exists(outputPath))
                {
                    var existingBytes = File.ReadAllBytes(outputPath);
                    var existing = InspectCommand.ReadInfo(existingBytes);
                    if (string.Equals(existing.CatalogId, catalogId, StringComparison.Ordinal)
                        && string.Equals(existing.Version, version, StringComparison.Ordinal))
                    {
                        var candidate = WriteBytes(catalog);
                        if (existingBytes.SequenceEqual(candidate))
                        {
                            stdout.WriteLine(outputPath);
                            return 0;
                        }

                        stderr.WriteLine("Published Catalog identity is immutable: the same Catalog ID and Version already has a different checksum or fingerprint.");
                        return 2;
                    }
                }

                AtomicFileWriter.WriteVerified(
                    outputPath,
                    stream => TagCatalogBinaryWriter.Write(stream, catalog),
                    stream => TagCatalogBinary.Load(stream, development
                        ? TagCatalogExpectations.ForDevelopment(catalogId)
                        : TagCatalogExpectations.ForPublished(catalogId, version!, catalog.Fingerprint)));
                stdout.WriteLine(outputPath);
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
                or TagCatalogException or TagCatalogFormatException or TagCatalogCompatibilityException
                or InvalidDataException or InvalidOperationException or OverflowException)
            {
                stderr.WriteLine(exception.Message);
                return 2;
            }
        }

        private static TagSourceDocument LoadGame(string path)
        {
            using var input = File.OpenRead(path);
            return TagSourceJson.LoadGame(input, path);
        }

        private static TagSourceDocument LoadMetadata(string path)
        {
            var fullPath = Path.GetFullPath(path);
            using var input = File.OpenRead(fullPath);
            return TagSourceJson.LoadMetadata(input, fullPath);
        }

        private static byte[] WriteBytes(TagCatalog catalog)
        {
            using var output = new MemoryStream();
            TagCatalogBinaryWriter.Write(output, catalog);
            return output.ToArray();
        }
    }
}
