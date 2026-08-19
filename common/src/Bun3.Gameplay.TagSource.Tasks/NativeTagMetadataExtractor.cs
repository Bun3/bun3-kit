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
    /// <summary>Extracts Native GameplayTag C# declarations into deterministic source metadata.</summary>
    public static class NativeTagMetadataExtractor
    {
        private const string SourceAttributeName = "Bun3.Gameplay.Tags.GameplayTagSourceAttribute";
        private const string TagAttributeName = "Bun3.Gameplay.Tags.NativeGameplayTagAttribute";

        /// <summary>Extracts native source metadata from evaluated C# files and reference assemblies.</summary>
        /// <param name="sourceFiles">Evaluated C# source file paths.</param>
        /// <param name="referencePaths">Reference assembly paths supplied to the Roslyn compilation.</param>
        /// <param name="assemblyName">Assembly name of the extraction target.</param>
        /// <returns>Result holding JSON on success or deterministic diagnostics on failure.</returns>
        public static NativeTagExtractionResult Extract(
            IReadOnlyList<string> sourceFiles,
            IReadOnlyList<string> referencePaths,
            string assemblyName)
        {
            if (sourceFiles is null) throw new ArgumentNullException(nameof(sourceFiles));
            if (referencePaths is null) throw new ArgumentNullException(nameof(referencePaths));
            if (string.IsNullOrWhiteSpace(assemblyName)) throw new ArgumentException("Assembly name cannot be empty.", nameof(assemblyName));

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
            if (sourceAttributeType is null) diagnostics.Add("B3TAG3001: GameplayTagSourceAttribute reference not found.");
            if (tagAttributeType is null) diagnostics.Add("B3TAG3001: NativeGameplayTagAttribute reference not found.");
            if (diagnostics.Count != 0) return NativeTagExtractionResult.Failure(diagnostics);

            var sourceAttributes = compilation.Assembly.GetAttributes()
                .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, sourceAttributeType))
                .ToArray();
            if (sourceAttributes.Length != 1)
            {
                diagnostics.Add("B3TAG3002: Exactly one GameplayTagSource assembly attribute is required.");
                return NativeTagExtractionResult.Failure(diagnostics);
            }

            var sourceAttribute = sourceAttributes[0];
            if (!TryGetStringArgument(sourceAttribute, 0, out var sourceId)
                || !TryGetStringArgument(sourceAttribute, 1, out var displayName)
                || !IsValidSourceId(sourceId)
                || string.Equals(sourceId, "game", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(displayName))
            {
                diagnostics.Add("B3TAG3003: Invalid GameplayTagSource source ID or display name: " + sourceId);
                return NativeTagExtractionResult.Failure(diagnostics);
            }

            var rows = new List<TagRow>();
            CollectFields(compilation.Assembly.GlobalNamespace, tagAttributeType!, rows, diagnostics);
            rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            for (var index = 1; index < rows.Count; index++)
            {
                if (string.Equals(rows[index - 1].Name, rows[index].Name, StringComparison.Ordinal))
                {
                    diagnostics.Add("B3TAG3006: Duplicate Native GameplayTag: " + rows[index].Name);
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
                    diagnostics.Add(location + " B3TAG3004: Exactly one NativeGameplayTag attribute per field is required.");
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
                    diagnostics.Add(location + " B3TAG3005: Invalid Native GameplayTag path: " + value);
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

    /// <summary>JSON or diagnostic result of Native GameplayTag extraction.</summary>
    public sealed class NativeTagExtractionResult
    {
        private NativeTagExtractionResult(bool succeeded, string metadataJson, IReadOnlyList<string> diagnostics)
        {
            Succeeded = succeeded;
            MetadataJson = metadataJson;
            Diagnostics = diagnostics;
        }

        /// <summary>Whether extraction succeeded.</summary>
        public bool Succeeded { get; }

        /// <summary>Strict source metadata JSON when successful.</summary>
        public string MetadataJson { get; }

        /// <summary>Deterministic diagnostics when failed.</summary>
        public IReadOnlyList<string> Diagnostics { get; }

        internal static NativeTagExtractionResult Success(string json) =>
            new NativeTagExtractionResult(true, json, Array.Empty<string>());

        internal static NativeTagExtractionResult Failure(IReadOnlyList<string> diagnostics) =>
            new NativeTagExtractionResult(false, string.Empty, diagnostics.ToArray());
    }
}
