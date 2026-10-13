using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bun3.State.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace Bun3.State.Generator.Tests
{
    internal static class GeneratorCompilation
    {
        private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(ITrackedState).Assembly.Location)).ToArray();

        internal static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Generate(params (string Source, string Path)[] sources)
            => GenerateCore(sources, References);

        internal static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) GenerateWithReference(string source, MetadataReference reference)
            => GenerateCore(new[] { (source, "Schema.cs") }, References.Append(reference).ToArray());

        private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) GenerateCore(
            (string Source, string Path)[] sources, MetadataReference[] references)
        {
            var options = new CSharpParseOptions(LanguageVersion.CSharp9);
            var compilation = CSharpCompilation.Create("Generated_" + Guid.NewGuid().ToString("N"),
                sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, options, s.Path)), references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new TrackedStateGenerator().AsSourceGenerator() }, parseOptions: options);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            return (output, diagnostics);
        }

        internal static async Task<Diagnostic[]> Diagnose(string source, string path = "Schema.cs")
        {
            var generated = Generate((source, path));
            var analyzer = await Analyze(generated.Compilation);
            return generated.Diagnostics.Concat(analyzer).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        }

        internal static Task<ImmutableArray<Diagnostic>> Analyze(Compilation compilation) =>
            compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TrackedStateAnalyzer())).GetAnalyzerDiagnosticsAsync();

        internal static Assembly Emit(string source)
        {
            var result = Generate((source, "Schema.cs"));
            Assert.That(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
            using var stream = new MemoryStream();
            var emitted = result.Compilation.Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            return Assembly.Load(stream.ToArray());
        }
    }
}
