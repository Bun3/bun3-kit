using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace Bun3.State.Generator.Tests
{
    public class StateDiagnosticTests
    {
        private static string State(string members) => "using Bun3.State; namespace Fixtures { [TrackedState] public sealed partial class State { " + members + " } }";

        [TestCase("public partial class State")]
        [TestCase("public sealed class State")]
        [TestCase("public sealed partial class State<T>")]
        [TestCase("public partial struct State")]
        [TestCase("public sealed partial record State")]
        public async Task Unsupported_declarations_report_0001(string declaration)
        {
            AssertIds(await GeneratorCompilation.Diagnose("using Bun3.State; namespace Fixtures { [TrackedState] " + declaration + " { } }"), "B3STATE0001");
        }

        [TestCase("[TrackedState] public sealed partial class State { }")]
        [TestCase("namespace Fixtures { class Outer { [TrackedState] public sealed partial class State { } } }")]
        [TestCase("namespace Fixtures { [TrackedState] public partial class Base { } [TrackedState] public sealed partial class State : Base { } }")]
        public async Task Unsupported_scope_or_inheritance_reports_0001(string source)
        {
            var diagnostics = await GeneratorCompilation.Diagnose("using Bun3.State; " + source);
            Assert.That(diagnostics, Is.Not.Empty);
            Assert.That(diagnostics.All(d => d.Id == "B3STATE0001"), Is.True);
        }

        [TestCase("private int[] _value;")]
        [TestCase("private System.Collections.Generic.List<int> _value;")]
        [TestCase("private System.Collections.Generic.Dictionary<int,int> _value;")]
        [TestCase("private object _value;")]
        [TestCase("private CustomStruct _value;")]
        [TestCase("private Message _value;")]
        [TestCase("private readonly int _value;")]
        [TestCase("private TrackedList<int> _value;")]
        [TestCase("public int _value;")]
        [TestCase("private static int _value;")]
        [TestCase("private readonly TrackedList<object> _value;")]
        public async Task Unsupported_fields_report_0002_without_generated_compile_errors(string field)
        {
            var source = State("[Tracked] " + field) + " namespace Fixtures { public struct CustomStruct { } public class Message { } }";
            AssertIds(await GeneratorCompilation.Diagnose(source), "B3STATE0002");
            var generated = GeneratorCompilation.Generate((source, "Schema.cs"));
            Assert.That(generated.Compilation.SyntaxTrees.Count(), Is.EqualTo(1));
        }

        [TestCase("[Tracked] private int _;")]
        [TestCase("[Tracked] private int _x; [Tracked] private int x;")]
        [TestCase("[Tracked] private int _level; public int Level => 0;")]
        [TestCase("private readonly int __bun3Tracking = 1;")]
        public async Task Generated_name_collisions_report_0003(string members)
        {
            var diagnostics = await GeneratorCompilation.Diagnose(State(members));
            Assert.That(diagnostics, Is.Not.Empty);
            Assert.That(diagnostics.All(d => d.Id == "B3STATE0003"), Is.True, string.Join("\n", diagnostics.Select(d => d.ToString())));
        }

        [TestCase("_value = 1;")]
        [TestCase("_value += 1;")]
        [TestCase("++_value;")]
        [TestCase("_value--;")]
        [TestCase("(_value, _) = (1, 2);")]
        [TestCase("Take(ref _value);")]
        [TestCase("TakeOut(out _value);")]
        [TestCase("ref int alias = ref _value;")]
        [TestCase("dynamic target = this; target.Take(ref _value);")]
        public async Task Direct_writes_and_aliases_report_0004_even_in_user_g_cs(string operation)
        {
            var source = State("[Tracked] private int _value = 1; void Mutate() { " + operation + " } void Take(ref int v) { } void TakeOut(out int v) { v = 0; }");
            AssertIds(await GeneratorCompilation.Diagnose(source, "User.g.cs"), "B3STATE0004");
        }

        [TestCase("private int _value;")]
        [TestCase("private readonly System.Collections.Generic.List<int> _value = new System.Collections.Generic.List<int>();")]
        [TestCase("private readonly object _value = new object();")]
        [TestCase("public int Value { get; set; }")]
        [TestCase("public int Value { get; init; }")]
        [TestCase("public System.Collections.Generic.List<int> Values { get; } = new System.Collections.Generic.List<int>();")]
        public async Task Omitted_mutable_storage_reports_0005(string members)
        {
            AssertIds(await GeneratorCompilation.Diagnose(State(members)), "B3STATE0005");
        }

        [Test]
        public async Task Supported_types_aliases_partial_files_and_namespaces_compile_cleanly()
        {
            var types = new[] { "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "char", "float", "double", "decimal", "string?", "System.DateTime", "System.DateTimeOffset", "System.TimeSpan", "System.Guid", "System.DayOfWeek", "int?", "System.Guid?", "Child?", "Child", "nint", "nuint" };
            var members = string.Join("\n", types.Select((t, i) => "[T] private " + t + " _value" + i + ";"));
            var source = "using Bun3.State; using S = Bun3.State.TrackedStateAttribute; using T = Bun3.State.TrackedAttribute; namespace A { [S] public sealed partial class State { " + members + " [T] private readonly TrackedList<int?> _numbers = new TrackedList<int?>(); [T] private readonly TrackedList<Child> _children = new TrackedList<Child>(); } [S] public sealed partial class Child { } }";
            var second = "namespace A { public sealed partial class State { public int Read() => Value5; } } namespace B { [Bun3.State.TrackedState] public sealed partial class State { } }";
            var generated = GeneratorCompilation.Generate((source, "A.cs"), (second, "B.cs"));
            Assert.That(generated.Diagnostics, Is.Empty);
            Assert.That(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty, string.Join("\n", generated.Compilation.SyntaxTrees.Select(t => t.FilePath + "\n" + t.ToString())));
            Assert.That(await GeneratorCompilation.Diagnose(source), Is.Empty);
        }

        [Test]
        public async Task Generated_restore_code_and_immutable_or_inherited_storage_are_not_reported()
        {
            var source = "using Bun3.State; namespace Fixtures { public class Base { public int Untracked; } [TrackedState] public sealed partial class State : Base { [Tracked] private int _value = 1; private readonly int _constant = 3; private const string Name = \"state\"; public int Computed => Value + _constant; } }";
            Assert.That(await GeneratorCompilation.Diagnose(source), Is.Empty);
        }

        [Test]
        public async Task Containing_type_name_is_a_generated_member_collision()
        {
            AssertIds(await GeneratorCompilation.Diagnose(State("[Tracked] private int _state;")), "B3STATE0003");
        }

        [TestCase("public int Level => 42;")]
        [TestCase("protected int Level => 42;")]
        [TestCase("public abstract int Level { get; }")]
        [TestCase("public void Level() { }")]
        [TestCase("protected int Level;")]
        [TestCase("internal int Level => 42;")]
        [TestCase("private protected int Level => 42;")]
        [TestCase("protected internal int Level => 42;")]
        public void Accessible_inherited_names_report_0003_and_suppress_output(string member)
        {
            var source = "using Bun3.State; namespace Fixtures { public abstract class Base { " + member +
                " } public abstract class Middle : Base { } [TrackedState] public sealed partial class State : Middle { [Tracked] private int _level; } }";
            var generated = GeneratorCompilation.Generate((source, "Schema.cs"));
            AssertIds(generated.Diagnostics.ToArray(), "B3STATE0003");
            Assert.That(generated.Compilation.SyntaxTrees.Count(), Is.EqualTo(1));
        }

        [TestCase("[TrackedState] public sealed partial class __bun3Tracking { }")]
        [TestCase("public class Base { protected int __bun3Tracking; } [TrackedState] public sealed partial class State : Base { }")]
        public void Metadata_names_report_0003_and_suppress_output(string declaration)
        {
            var generated = GeneratorCompilation.Generate(("using Bun3.State; namespace Fixtures { " + declaration + " }", "Schema.cs"));
            AssertIds(generated.Diagnostics.ToArray(), "B3STATE0003");
            Assert.That(generated.Compilation.SyntaxTrees.Count(), Is.EqualTo(1));
        }

        [Test]
        public void Private_inherited_names_remain_supported()
        {
            var generated = GeneratorCompilation.Generate(("using Bun3.State; namespace Fixtures { public class Base { private int Level => 42; private int __bun3Tracking => 1; } [TrackedState] public sealed partial class State : Base { [Tracked] private int _level; } }", "Schema.cs"));
            Assert.That(generated.Diagnostics, Is.Empty);
            Assert.That(generated.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        }

        [TestCase("public", true)]
        [TestCase("protected", true)]
        [TestCase("protected internal", true)]
        [TestCase("internal", false)]
        [TestCase("private protected", false)]
        [TestCase("private", false)]
        public void External_base_names_respect_assembly_accessibility(string accessibility, bool collision)
        {
            var baseCompilation = GeneratorCompilation.Generate(("namespace Fixtures { public class Base { " + accessibility + " int Level => 42; } }", "Base.cs"));
            using var stream = new MemoryStream();
            Assert.That(baseCompilation.Compilation.Emit(stream).Success, Is.True);
            var reference = MetadataReference.CreateFromImage(stream.ToArray());
            var generated = GeneratorCompilation.GenerateWithReference("using Bun3.State; namespace Fixtures { [TrackedState] public sealed partial class State : Base { [Tracked] private int _level; } }", reference);
            if (collision)
            {
                AssertIds(generated.Diagnostics.ToArray(), "B3STATE0003");
                Assert.That(generated.Compilation.SyntaxTrees.Count(), Is.EqualTo(1));
            }
            else
            {
                Assert.That(generated.Diagnostics, Is.Empty);
                Assert.That(generated.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
            }
        }

        [Test]
        public async Task Filename_matching_generator_does_not_exempt_user_mutations()
        {
            var path = "Bun3.State.Generator/Bun3.State.Generator.TrackedStateGenerator/Fixtures.State.TrackedState.g.cs";
            AssertIds(await GeneratorCompilation.Diagnose(State("[Tracked] private int _value; void Write() { _value = 3; }"), path), "B3STATE0004");
        }

        [TestCase("E:/Projects/sample/server/artifacts/obj/Consumer/debug/")]
        [TestCase("/tmp/sample/obj/Debug/")]
        public async Task Rooted_compiler_generated_paths_exempt_generated_storage_only(string outputDirectory)
        {
            var generated = GeneratorCompilation.Generate((State("[Tracked] private int _value; void Write() { _value = 3; }"), "Schema.cs"));
            var compilation = generated.Compilation;
            foreach (var tree in compilation.SyntaxTrees.Where(t => t.FilePath != "Schema.cs").ToArray())
                compilation = compilation.ReplaceSyntaxTree(tree, tree.WithFilePath(outputDirectory + tree.FilePath));
            Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
            var diagnostics = await GeneratorCompilation.Analyze(compilation);
            AssertIds(diagnostics.ToArray(), "B3STATE0004");
            Assert.That(diagnostics[0].Location.SourceTree!.FilePath, Is.EqualTo("Schema.cs"));
        }

        [TestCase("E:/Projects/sample/obj/Bun3.State.Generator/Bun3.State.Generator.TrackedStateGenerator/Fixtures.State.TrackedState.g.cs", false)]
        [TestCase("/tmp/obj/Bun3.State.Generator/Bun3.State.Generator.TrackedStateGenerator/Fixtures.State.TrackedState.g.cs", false)]
        [TestCase("E:/Projects/sample/Schema.g.cs", true)]
        [TestCase("E:/Projects/sample/FakeBun3.State.Generator/Bun3.State.Generator.TrackedStateGenerator/Fixtures.State.TrackedState.g.cs", true)]
        [TestCase("E:/Projects/sample/Bun3.State.Generator/Bun3.State.Generator.TrackedStateGenerator/extra/Fixtures.State.TrackedState.g.cs", true)]
        public async Task Rooted_user_paths_or_headers_do_not_exempt_mutations(string path, bool generatorHeader)
        {
            var source = (generatorHeader ? "// <auto-generated by Bun3.State.Generator />\n" : "") +
                State("[Tracked] private int _value; void Write() { _value = 3; }");
            AssertIds(await GeneratorCompilation.Diagnose(source, path), "B3STATE0004");
        }

        [Test]
        public async Task Constructor_writes_and_ref_returns_are_rejected()
        {
            var source = State("[Tracked] private int _value; public State() { _value = 1; } public ref int Escape() => ref _value;");
            AssertIds(await GeneratorCompilation.Diagnose(source), "B3STATE0004", "B3STATE0004");
        }

        [Test]
        public async Task Ref_array_access_reads_tracked_index_without_exposing_it()
        {
            var source = State("[Tracked] private int _index; public ref int Pick(int[] values) => ref values[_index]; void Use(int[] values) { ref int selected = ref values[(_index)]; selected = 3; }");
            var generated = GeneratorCompilation.Generate((source, "Schema.cs"));
            Assert.That(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
            Assert.That(await GeneratorCompilation.Diagnose(source), Is.Empty);
        }

        [Test]
        public async Task Parenthesized_field_ref_exposures_remain_rejected()
        {
            var source = State("[Tracked] private int _value; public ref int Pick() => ref (_value); void Use() { ref int selected = ref (_value); }");
            AssertIds(await GeneratorCompilation.Diagnose(source), "B3STATE0004", "B3STATE0004");
        }

        [Test]
        public void Nullable_disabled_source_and_distinct_dotted_namespace_names_compile()
        {
            var source = "#nullable disable\nusing Bun3.State; namespace A_B { [TrackedState] public sealed partial class C { [Tracked] private string _value; } } namespace A { [TrackedState] public sealed partial class B_C { } }";
            var generated = GeneratorCompilation.Generate((source, "Schema.cs"));
            Assert.That(generated.Diagnostics, Is.Empty);
            Assert.That(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        }

        [Test]
        public async Task User_auto_generated_header_does_not_exempt_mutations()
        {
            var source = "// <auto-generated/>\n" + State("[Tracked] private int _value; void Write() { _value = 3; }");
            AssertIds(await GeneratorCompilation.Diagnose(source, "Schema.g.cs"), "B3STATE0004");
        }

        [Test]
        public async Task Other_partial_files_are_checked_for_omitted_storage_and_name_collisions()
        {
            var source = "using Bun3.State; namespace Fixtures { [TrackedState] public sealed partial class State { [Tracked] private int _level; } public sealed partial class State { public int Level => 1; private int _missing; } }";
            AssertIds(await GeneratorCompilation.Diagnose(source), "B3STATE0003", "B3STATE0005");
        }

        private static void AssertIds(Diagnostic[] diagnostics, params string[] ids)
        {
            Assert.That(diagnostics.Select(d => d.Id), Is.EquivalentTo(ids), string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
        }
    }
}
