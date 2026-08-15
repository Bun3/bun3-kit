#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;

namespace Bun3.Gameplay.TagSource.Tasks
{
    /// <summary>Native GameplayTag C# 선언을 결정적인 Source 메타데이터로 추출합니다.</summary>
    public static class NativeTagMetadataExtractor
    {
        private const string SourceAttributeName = "Bun3.Gameplay.Tags.GameplayTagSourceAttribute";
        private const string TagAttributeName = "Bun3.Gameplay.Tags.NativeGameplayTagAttribute";

        /// <summary>평가된 C# 파일과 참조 어셈블리에서 Native Source 메타데이터를 추출합니다.</summary>
        /// <param name="sourceFiles">평가된 C# 소스 파일 경로입니다.</param>
        /// <param name="referencePaths">Roslyn compilation에 제공할 참조 어셈블리 경로입니다.</param>
        /// <param name="assemblyName">추출 대상의 어셈블리 이름입니다.</param>
        /// <returns>성공 시 JSON, 실패 시 결정적인 진단을 담은 결과입니다.</returns>
        public static NativeTagExtractionResult Extract(
            IReadOnlyList<string> sourceFiles,
            IReadOnlyList<string> referencePaths,
            string assemblyName)
        {
            if (sourceFiles is null) throw new ArgumentNullException(nameof(sourceFiles));
            if (referencePaths is null) throw new ArgumentNullException(nameof(referencePaths));
            if (string.IsNullOrWhiteSpace(assemblyName)) throw new ArgumentException("어셈블리 이름은 비어 있을 수 없습니다.", nameof(assemblyName));

            var diagnostics = new List<string>();
            var trees = new List<SyntaxTree>(sourceFiles.Count);
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.Latest,
                DocumentationMode.None,
                SourceCodeKind.Regular,
                new[] { "BUN3_GAMEPLAY_TAGS_AUTHORING" });
            foreach (var path in sourceFiles.OrderBy(value => value, StringComparer.Ordinal))
            {
                var text = File.ReadAllText(path, new UTF8Encoding(false, true));
                trees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, path, Encoding.UTF8));
            }

            var references = new List<MetadataReference>();
            var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in referencePaths.OrderBy(value => value, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var fullPath = Path.GetFullPath(path);
                if (seenReferences.Add(fullPath)) references.Add(MetadataReference.CreateFromFile(fullPath));
            }

            var compilation = CSharpCompilation.Create(
                assemblyName,
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
            foreach (var diagnostic in compilation.GetDiagnostics()
                         .Where(value => value.Severity == DiagnosticSeverity.Error)
                         .OrderBy(FormatDiagnostic, StringComparer.Ordinal))
            {
                diagnostics.Add(FormatDiagnostic(diagnostic));
            }

            var sourceAttributeType = compilation.GetTypeByMetadataName(SourceAttributeName);
            var tagAttributeType = compilation.GetTypeByMetadataName(TagAttributeName);
            if (sourceAttributeType is null) diagnostics.Add("B3TAG3001: GameplayTagSourceAttribute 참조를 찾을 수 없습니다.");
            if (tagAttributeType is null) diagnostics.Add("B3TAG3001: NativeGameplayTagAttribute 참조를 찾을 수 없습니다.");
            if (diagnostics.Count != 0) return NativeTagExtractionResult.Failure(diagnostics);

            var sourceAttributes = compilation.Assembly.GetAttributes()
                .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, sourceAttributeType))
                .ToArray();
            if (sourceAttributes.Length != 1)
            {
                diagnostics.Add("B3TAG3002: GameplayTagSource assembly 특성은 정확히 하나여야 합니다.");
                return NativeTagExtractionResult.Failure(diagnostics);
            }

            var sourceAttribute = sourceAttributes[0];
            if (!TryGetStringArgument(sourceAttribute, 0, out var sourceId)
                || !TryGetStringArgument(sourceAttribute, 1, out var displayName)
                || !IsValidSourceId(sourceId)
                || string.Equals(sourceId, "game", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(displayName))
            {
                diagnostics.Add("B3TAG3003: GameplayTagSource의 Source ID 또는 표시 이름이 올바르지 않습니다: " + sourceId);
                return NativeTagExtractionResult.Failure(diagnostics);
            }

            var rows = new List<TagRow>();
            CollectFields(compilation.Assembly.GlobalNamespace, tagAttributeType!, rows, diagnostics);
            rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            for (var index = 1; index < rows.Count; index++)
            {
                if (string.Equals(rows[index - 1].Name, rows[index].Name, StringComparison.Ordinal))
                {
                    diagnostics.Add("B3TAG3006: 중복된 Native GameplayTag입니다: " + rows[index].Name);
                }
            }

            if (diagnostics.Count != 0)
            {
                diagnostics.Sort(StringComparer.Ordinal);
                return NativeTagExtractionResult.Failure(diagnostics);
            }

            return NativeTagExtractionResult.Success(WriteJson(sourceId!, displayName!, rows));
        }

        private static void CollectFields(
            INamespaceSymbol namespaceSymbol,
            INamedTypeSymbol tagAttributeType,
            List<TagRow> rows,
            List<string> diagnostics)
        {
            foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                CollectFields(childNamespace, tagAttributeType, rows, diagnostics);
            }

            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                CollectFields(type, tagAttributeType, rows, diagnostics);
            }
        }

        private static void CollectFields(
            INamedTypeSymbol type,
            INamedTypeSymbol tagAttributeType,
            List<TagRow> rows,
            List<string> diagnostics)
        {
            foreach (var nested in type.GetTypeMembers()) CollectFields(nested, tagAttributeType, rows, diagnostics);
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                var attributes = field.GetAttributes()
                    .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, tagAttributeType))
                    .ToArray();
                if (attributes.Length == 0) continue;
                var location = FormatLocation(field.Locations.FirstOrDefault());
                if (attributes.Length != 1)
                {
                    diagnostics.Add(location + " B3TAG3004: NativeGameplayTag 특성은 필드마다 하나여야 합니다.");
                    continue;
                }

                if (!field.IsConst || field.DeclaredAccessibility != Accessibility.Public
                    || field.Type.SpecialType != SpecialType.System_String || field.ConstantValue is not string value)
                {
                    diagnostics.Add(location + " B3TAG3004: NativeGameplayTag may annotate only public const string fields.");
                    continue;
                }

                if (!TryFoldTagName(value, out var canonical))
                {
                    diagnostics.Add(location + " B3TAG3005: Native GameplayTag 경로가 올바르지 않습니다: " + value);
                    continue;
                }

                var comment = string.Empty;
                if (attributes[0].ConstructorArguments.Length == 1
                    && attributes[0].ConstructorArguments[0].Value is string providedComment)
                {
                    comment = providedComment;
                }

                rows.Add(new TagRow(canonical, comment));
            }
        }

        private static string WriteJson(string sourceId, string displayName, IReadOnlyList<TagRow> rows)
        {
            var output = new StringBuilder();
            using var textWriter = new StringWriter(output, System.Globalization.CultureInfo.InvariantCulture);
            using var writer = new JsonTextWriter(textWriter)
            {
                Formatting = Formatting.Indented,
                Indentation = 2,
                IndentChar = ' ',
            };
            writer.WriteStartObject();
            writer.WritePropertyName("schemaVersion");
            writer.WriteValue(1);
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            writer.WriteValue(sourceId);
            writer.WritePropertyName("displayName");
            writer.WriteValue(displayName);
            writer.WritePropertyName("kind");
            writer.WriteValue("native");
            writer.WriteEndObject();
            writer.WritePropertyName("tags");
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("name");
                writer.WriteValue(row.Name);
                writer.WritePropertyName("comment");
                writer.WriteValue(row.Comment);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("redirects");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
            output.Append('\n');
            return output.ToString();
        }

        private static bool TryGetStringArgument(AttributeData attribute, int index, out string? value)
        {
            value = null;
            if (attribute.ConstructorArguments.Length <= index) return false;
            value = attribute.ConstructorArguments[index].Value as string;
            return value is not null;
        }

        private static bool IsValidSourceId(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var separator = true;
            foreach (var character in value!)
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    separator = false;
                }
                else if ((character == '.' || character == '-') && !separator)
                {
                    separator = true;
                }
                else
                {
                    return false;
                }
            }

            return !separator;
        }

        private static bool TryFoldTagName(string value, out string canonical)
        {
            canonical = string.Empty;
            if (value.Length == 0 || value.Length > 255) return false;
            var characters = value.ToCharArray();
            var depth = 1;
            var segmentLength = 0;
            for (var index = 0; index < characters.Length; index++)
            {
                var character = characters[index];
                if (character == '.')
                {
                    if (segmentLength == 0 || ++depth > 16) return false;
                    segmentLength = 0;
                    continue;
                }

                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9'))) return false;
                if (character >= 'A' && character <= 'Z') characters[index] = (char)(character + 32);
                segmentLength++;
            }

            if (segmentLength == 0) return false;
            canonical = new string(characters);
            return true;
        }

        private static string FormatDiagnostic(Diagnostic diagnostic)
        {
            var location = FormatLocation(diagnostic.Location);
            return location + " " + diagnostic.Id + ": " + diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatLocation(Location? location)
        {
            if (location is null || !location.IsInSource) return "<unknown>";
            var span = location.GetLineSpan();
            return span.Path + "(" + (span.StartLinePosition.Line + 1) + "," + (span.StartLinePosition.Character + 1) + ")";
        }

        private sealed class TagRow
        {
            internal TagRow(string name, string comment)
            {
                Name = name;
                Comment = comment;
            }

            internal string Name { get; }
            internal string Comment { get; }
        }
    }

    /// <summary>Native GameplayTag 추출의 JSON 또는 진단 결과입니다.</summary>
    public sealed class NativeTagExtractionResult
    {
        private NativeTagExtractionResult(bool succeeded, string metadataJson, IReadOnlyList<string> diagnostics)
        {
            Succeeded = succeeded;
            MetadataJson = metadataJson;
            Diagnostics = diagnostics;
        }

        /// <summary>추출이 성공했는지 나타냅니다.</summary>
        public bool Succeeded { get; }

        /// <summary>성공한 경우의 엄격한 Source 메타데이터 JSON입니다.</summary>
        public string MetadataJson { get; }

        /// <summary>실패한 경우의 결정적인 진단 목록입니다.</summary>
        public IReadOnlyList<string> Diagnostics { get; }

        internal static NativeTagExtractionResult Success(string json) =>
            new NativeTagExtractionResult(true, json, Array.Empty<string>());

        internal static NativeTagExtractionResult Failure(IReadOnlyList<string> diagnostics) =>
            new NativeTagExtractionResult(false, string.Empty, diagnostics.ToArray());
    }
}
