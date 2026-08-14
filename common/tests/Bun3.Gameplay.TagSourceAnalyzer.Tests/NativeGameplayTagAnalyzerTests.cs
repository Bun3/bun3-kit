#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Bun3.Gameplay.TagSourceAnalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace Bun3.Gameplay.TagSourceAnalyzer.Tests
{
    [TestFixture]
    public sealed class NativeGameplayTagAnalyzerTests
    {
        [Test]
        public async Task Valid_public_const_string_declaration_has_no_diagnostics()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]

public static class InputTags
{
    [NativeGameplayTag(""Jump input"")]
    public const string Jump = ""input.jump"";
}");

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task Declaration_without_assembly_source_reports_B3TAG0001()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;

public static class InputTags
{
    [NativeGameplayTag]
    public const string Jump = ""input.jump"";
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0001");
        }

        [Test]
        public async Task Compilation_without_native_declarations_has_no_diagnostics()
        {
            var diagnostics = await AnalyzeAsync(@"
public static class InputTags
{
    public const string Jump = ""input.jump"";
}");

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task Non_public_non_const_or_non_string_fields_report_B3TAG0002()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]

public static class InputTags
{
    [NativeGameplayTag]
    private const string Private = ""input.private"";

    [NativeGameplayTag]
    public static readonly string Readonly = ""input.readonly"";

    [NativeGameplayTag]
    public const int Number = 1;
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0002", "B3TAG0002", "B3TAG0002");
        }

        [Test]
        public async Task Invalid_tag_path_reports_B3TAG0003()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]

public static class InputTags
{
    [NativeGameplayTag]
    public const string Jump = ""input..jump"";
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0003");
        }

        [Test]
        public async Task Canonically_duplicate_tag_paths_report_B3TAG0004_for_each_declaration()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]

public static class InputTags
{
    [NativeGameplayTag]
    public const string Jump = ""Input.Jump"";

    [NativeGameplayTag]
    public const string JumpAlias = ""input.jump"";
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0004", "B3TAG0004");
        }

        [Test]
        public async Task Multiple_assembly_sources_report_B3TAG0001()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.combat"", ""Bun3 Combat"")]

public static class InputTags
{
    [NativeGameplayTag]
    public const string Jump = ""input.jump"";
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0001");
        }

        [Test]
        public async Task Invalid_source_id_or_display_name_reports_B3TAG0005()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""Bun3.Input"", "" "")]

public static class InputTags
{
    [NativeGameplayTag]
    public const string Jump = ""input.jump"";
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0005");
        }

        [Test]
        public async Task Reserved_game_source_id_reports_B3TAG0005()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""game"", ""Game"")]

public static class InputTags
{
    [NativeGameplayTag]
    public const string Jump = ""input.jump"";
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0005");
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var compilation = CSharpCompilation.Create(
                assemblyName: "NativeGameplayTagAnalyzerTests",
                syntaxTrees: new[]
                {
                    CSharpSyntaxTree.ParseText(AttributeDefinitions, parseOptions),
                    CSharpSyntaxTree.ParseText(source, parseOptions),
                },
                references: ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assert.That(compilation.GetDiagnostics(), Is.Empty);

            var analyzer = new NativeGameplayTagAnalyzer();
            var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static void AssertDiagnosticIds(ImmutableArray<Diagnostic> diagnostics, params string[] expectedIds)
        {
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(expectedIds));
            for (var index = 0; index < diagnostics.Length; index++)
            {
                Assert.That(diagnostics[index].GetMessage(), Is.EqualTo(GetExpectedMessage(expectedIds[index])));
            }
        }

        private static string GetExpectedMessage(string diagnosticId) => diagnosticId switch
        {
            "B3TAG0001" => "Native tag declaration requires exactly one assembly GameplayTagSource attribute",
            "B3TAG0002" => "NativeGameplayTag may annotate only const string fields",
            "B3TAG0003" => "Native gameplay tag name is invalid",
            "B3TAG0004" => "Native gameplay tag is duplicated after canonicalization",
            "B3TAG0005" => "GameplayTag Source ID or display name is invalid",
            _ => throw new ArgumentOutOfRangeException(nameof(diagnosticId)),
        };

private const string AttributeDefinitions = @"
using System;
using System.Diagnostics;

namespace Bun3.Gameplay.Tags
{
    [Conditional(""BUN3_GAMEPLAY_TAGS_AUTHORING"")]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GameplayTagSourceAttribute : Attribute
    {
        public GameplayTagSourceAttribute(string sourceId, string displayName) { }
    }

    [Conditional(""BUN3_GAMEPLAY_TAGS_AUTHORING"")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NativeGameplayTagAttribute : Attribute
    {
        public NativeGameplayTagAttribute(string comment = """") { }
    }
}
";
    }
}
