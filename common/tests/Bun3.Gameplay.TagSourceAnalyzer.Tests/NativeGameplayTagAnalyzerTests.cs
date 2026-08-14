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
        public async Task Enum_member_declaration_reports_B3TAG0002()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]

public enum InputTag
{
    [NativeGameplayTag]
    Jump,
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0002");
        }

        [Test]
        public async Task Field_targeted_auto_property_reports_B3TAG0002()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]

public sealed class InputTags
{
    [field: NativeGameplayTag]
    public string Jump { get; } = ""input.jump"";
}");

            AssertDiagnosticIds(diagnostics, "B3TAG0002");
        }

        [Test]
        public async Task Field_targeted_record_parameter_reports_B3TAG0002()
        {
            var diagnostics = await AnalyzeAsync(@"
using Bun3.Gameplay.Tags;
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]

public record InputTags([field: NativeGameplayTag] string Jump);
");

            AssertDiagnosticIds(diagnostics, "B3TAG0002");
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

        [Test]
        public async Task Mixed_invalid_fields_report_in_compilation_syntax_tree_order()
        {
            const string source = @"
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]
";
            const string invalidField = @"
using Bun3.Gameplay.Tags;

public static class PrivateTags
{
    [NativeGameplayTag]
    private const string Private = ""input.private"";
}";
            const string invalidPath = @"
using Bun3.Gameplay.Tags;

public static class InvalidTags
{
    [NativeGameplayTag]
    public const string Invalid = ""input..invalid"";
}";

            for (var iteration = 0; iteration < 3; iteration++)
            {
                AssertDiagnosticIds(await AnalyzeAsync(source, invalidField, invalidPath), "B3TAG0002", "B3TAG0003");
                AssertDiagnosticIds(await AnalyzeAsync(source, invalidPath, invalidField), "B3TAG0003", "B3TAG0002");
            }
        }

        [Test]
        public async Task Duplicate_and_invalid_field_diagnostics_share_global_syntax_tree_order()
        {
            const string source = @"
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""bun3.input"", ""Bun3 Input"")]
";
            const string firstDuplicate = @"
using Bun3.Gameplay.Tags;

public static class FirstTags
{
    [NativeGameplayTag]
    public const string First = ""input.duplicate"";
}";
            const string invalidField = @"
using Bun3.Gameplay.Tags;

public static class InvalidTags
{
    [NativeGameplayTag]
    private const string Invalid = ""input.invalid"";
}";
            const string secondDuplicate = @"
using Bun3.Gameplay.Tags;

public static class SecondTags
{
    [NativeGameplayTag]
    public const string Second = ""Input.Duplicate"";
}";

            AssertDiagnosticIds(
                await AnalyzeAsync(source, firstDuplicate, invalidField, secondDuplicate),
                "B3TAG0004",
                "B3TAG0002",
                "B3TAG0004");
        }

        [Test]
        public async Task Invalid_source_and_field_diagnostics_share_global_syntax_tree_order()
        {
            const string invalidField = @"
using Bun3.Gameplay.Tags;

public static class InvalidTags
{
    [NativeGameplayTag]
    private const string Invalid = ""input.invalid"";
}";
            const string invalidSource = @"
[assembly: Bun3.Gameplay.Tags.GameplayTagSource(""Bun3.Input"", ""Bun3 Input"")]
";

            AssertDiagnosticIds(
                await AnalyzeAsync(invalidField, invalidSource),
                "B3TAG0002",
                "B3TAG0005");
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(params string[] sources)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9);
            var compilation = CSharpCompilation.Create(
                assemblyName: "NativeGameplayTagAnalyzerTests",
                syntaxTrees: new[] { CSharpSyntaxTree.ParseText(AttributeDefinitions, parseOptions, path: "attributes.cs") }
                    .Concat(sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, parseOptions, path: "source-" + index + ".cs"))),
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
