#nullable enable
using System;
using System.IO;

namespace Bun3.Gameplay.Tags.Cli
{
    internal static class Program
    {
        private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

        internal static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (args is null) throw new ArgumentNullException(nameof(args));
            if (stdout is null) throw new ArgumentNullException(nameof(stdout));
            if (stderr is null) throw new ArgumentNullException(nameof(stderr));
            if (args.Length == 0) return Usage(stderr);

            return args[0] switch
            {
                "compile" => CompileCommand.Run(args, stdout, stderr),
                "extract-native" => ExtractNativeCommand.Run(args, stdout, stderr),
                "inspect" => InspectCommand.Run(args, stdout, stderr),
                _ => Usage(stderr),
            };
        }

        internal static int Usage(TextWriter stderr)
        {
            stderr.WriteLine("Usage:");
            stderr.WriteLine("  bun3-tags compile --development --catalog-id <id> --project-root <dir> [--source <metadata>]...");
            stderr.WriteLine("  bun3-tags compile --published --catalog-id <id> --catalog-version <version> --project-root <dir> --output <file> [--source <metadata>]...");
            stderr.WriteLine("  bun3-tags extract-native --output <metadata.json> <source.cs> [<source.cs>]...");
            stderr.WriteLine("  bun3-tags inspect <GameplayTags.catalog>");
            return 1;
        }
    }
}
