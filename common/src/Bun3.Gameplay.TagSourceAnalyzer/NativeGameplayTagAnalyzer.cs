#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bun3.Gameplay.TagSourceAnalyzer
{
    /// <summary>
    /// 외부 상태를 읽지 않고 컴파일 시점 Native GameplayTag 선언을 검증합니다.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NativeGameplayTagAnalyzer : DiagnosticAnalyzer
    {
        private const string GameplayTagSourceAttributeName = "Bun3.Gameplay.Tags.GameplayTagSourceAttribute";
        private const string NativeGameplayTagAttributeName = "Bun3.Gameplay.Tags.NativeGameplayTagAttribute";
        private const int MaximumTagLength = 255;
        private const int MaximumTagDepth = 16;

        /// <inheritdoc />
        public override System.Collections.Immutable.ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => NativeGameplayTagDiagnostics.All;

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(compilationStartContext =>
            {
                var sourceAttribute = compilationStartContext.Compilation.GetTypeByMetadataName(GameplayTagSourceAttributeName);
                var nativeTagAttribute = compilationStartContext.Compilation.GetTypeByMetadataName(NativeGameplayTagAttributeName);
                if (sourceAttribute is null || nativeTagAttribute is null)
                {
                    return;
                }

                var fields = new ConcurrentQueue<AnnotatedField>();
                compilationStartContext.RegisterSyntaxNodeAction(syntaxNodeContext =>
                {
                    var declaration = (FieldDeclarationSyntax)syntaxNodeContext.Node;
                    var attribute = FindAttribute(declaration.AttributeLists, syntaxNodeContext.SemanticModel, nativeTagAttribute);
                    if (attribute is null)
                    {
                        return;
                    }

                    foreach (var variable in declaration.Declaration.Variables)
                    {
                        if (syntaxNodeContext.SemanticModel.GetDeclaredSymbol(variable) is IFieldSymbol field)
                        {
                            fields.Enqueue(new AnnotatedField(field, attribute));
                        }
                    }
                }, SyntaxKind.FieldDeclaration);

                compilationStartContext.RegisterCompilationEndAction(compilationEndContext =>
                {
                    AnalyzeCompilation(compilationEndContext, sourceAttribute, fields);
                });
            });
        }

        private static void AnalyzeCompilation(
            CompilationAnalysisContext context,
            INamedTypeSymbol sourceAttribute,
            ConcurrentQueue<AnnotatedField> fields)
        {
            var annotatedFields = fields.ToArray();
            if (annotatedFields.Length == 0)
            {
                return;
            }

            var sourceAttributes = FindAttributes(context.Compilation.Assembly.GetAttributes(), sourceAttribute);
            if (sourceAttributes.Count != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(NativeGameplayTagDiagnostics.MissingOrMultipleSource, Location.None));
            }
            else if (!IsValidSource(sourceAttributes[0]))
            {
                context.ReportDiagnostic(Diagnostic.Create(NativeGameplayTagDiagnostics.InvalidSource, GetLocation(sourceAttributes[0])));
            }

            var declarations = new List<TagDeclaration>();
            foreach (var annotatedField in annotatedFields)
            {
                var field = annotatedField.Field;
                var attribute = annotatedField.Attribute;

                if (field.DeclaredAccessibility != Accessibility.Public || !field.IsConst || field.Type.SpecialType != SpecialType.System_String)
                {
                    context.ReportDiagnostic(Diagnostic.Create(NativeGameplayTagDiagnostics.InvalidField, attribute.GetLocation()));
                    continue;
                }

                if (field.ConstantValue is not string value || !TryFoldTagName(value, out var canonicalName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(NativeGameplayTagDiagnostics.InvalidTagName, attribute.GetLocation()));
                    continue;
                }

                declarations.Add(new TagDeclaration(canonicalName, attribute));
            }

            declarations.Sort(TagDeclarationComparer.Instance);
            ReportDuplicates(context, declarations);
        }

        private static void ReportDuplicates(CompilationAnalysisContext context, List<TagDeclaration> declarations)
        {
            var index = 0;
            while (index < declarations.Count)
            {
                var end = index + 1;
                while (end < declarations.Count
                    && string.Equals(declarations[index].CanonicalName, declarations[end].CanonicalName, StringComparison.Ordinal))
                {
                    end++;
                }

                if (end - index > 1)
                {
                    for (var duplicate = index; duplicate < end; duplicate++)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(NativeGameplayTagDiagnostics.DuplicateTagName, declarations[duplicate].Attribute.GetLocation()));
                    }
                }

                index = end;
            }
        }

        private static bool IsValidSource(AttributeData attribute)
        {
            if (attribute.ConstructorArguments.Length != 2)
            {
                return false;
            }

            return attribute.ConstructorArguments[0].Value is string sourceId
                && attribute.ConstructorArguments[1].Value is string displayName
                && IsValidSourceId(sourceId)
                && !string.Equals(sourceId, "game", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(displayName);
        }

        private static bool IsValidSourceId(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            var previousWasSeparator = true;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    previousWasSeparator = false;
                    continue;
                }

                if ((character == '.' || character == '-') && !previousWasSeparator)
                {
                    previousWasSeparator = true;
                    continue;
                }

                return false;
            }

            return !previousWasSeparator;
        }

        private static bool TryFoldTagName(string value, out string canonicalName)
        {
            canonicalName = string.Empty;
            if (value.Length == 0 || value.Length > MaximumTagLength)
            {
                return false;
            }

            var characters = value.ToCharArray();
            var depth = 1;
            var segmentLength = 0;
            for (var index = 0; index < characters.Length; index++)
            {
                var character = characters[index];
                if (character == '.')
                {
                    if (segmentLength == 0 || ++depth > MaximumTagDepth)
                    {
                        return false;
                    }

                    segmentLength = 0;
                    continue;
                }

                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9')))
                {
                    return false;
                }

                if (character >= 'A' && character <= 'Z')
                {
                    characters[index] = (char)(character + ('a' - 'A'));
                }

                segmentLength++;
            }

            if (segmentLength == 0)
            {
                return false;
            }

            canonicalName = new string(characters);
            return true;
        }

        private static List<AttributeData> FindAttributes(ImmutableArray<AttributeData> attributes, INamedTypeSymbol attributeType)
        {
            var matches = new List<AttributeData>();
            for (var index = 0; index < attributes.Length; index++)
            {
                if (SymbolEqualityComparer.Default.Equals(attributes[index].AttributeClass, attributeType))
                {
                    matches.Add(attributes[index]);
                }
            }

            return matches;
        }

        private static AttributeSyntax? FindAttribute(
            SyntaxList<AttributeListSyntax> attributeLists,
            SemanticModel semanticModel,
            INamedTypeSymbol attributeType)
        {
            foreach (var attributeList in attributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeClass = semanticModel.GetTypeInfo(attribute.Name).Type
                        ?? semanticModel.GetTypeInfo(attribute).Type;
                    if (attributeClass is null)
                    {
                        var attributeSymbol = semanticModel.GetSymbolInfo(attribute.Name).Symbol;
                        if (attributeSymbol is IMethodSymbol constructor)
                        {
                            attributeClass = constructor.ContainingType;
                        }
                        else if (attributeSymbol is INamedTypeSymbol namedType)
                        {
                            attributeClass = namedType;
                        }
                    }

                    if (SymbolEqualityComparer.Default.Equals(attributeClass, attributeType)
                        || (attributeClass is null && HasExpectedAttributeName(attribute.Name, attributeType.Name)))
                    {
                        return attribute;
                    }
                }
            }

            return null;
        }

        private static bool HasExpectedAttributeName(NameSyntax name, string attributeTypeName)
        {
            var shortName = name.ToString();
            return string.Equals(shortName, attributeTypeName, StringComparison.Ordinal)
                || string.Equals(shortName, "NativeGameplayTag", StringComparison.Ordinal)
                || shortName.EndsWith("." + attributeTypeName, StringComparison.Ordinal)
                || shortName.EndsWith(".NativeGameplayTag", StringComparison.Ordinal);
        }

        private static Location GetLocation(AttributeData attribute) =>
            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;

        private sealed class TagDeclaration
        {
            internal TagDeclaration(string canonicalName, AttributeSyntax attribute)
            {
                CanonicalName = canonicalName;
                Attribute = attribute;
            }

            internal string CanonicalName { get; }

            internal AttributeSyntax Attribute { get; }
        }

        private sealed class AnnotatedField
        {
            internal AnnotatedField(IFieldSymbol field, AttributeSyntax attribute)
            {
                Field = field;
                Attribute = attribute;
            }

            internal IFieldSymbol Field { get; }

            internal AttributeSyntax Attribute { get; }
        }

        private sealed class TagDeclarationComparer : IComparer<TagDeclaration>
        {
            internal static readonly TagDeclarationComparer Instance = new TagDeclarationComparer();

            public int Compare(TagDeclaration? left, TagDeclaration? right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }

                if (left is null)
                {
                    return -1;
                }

                if (right is null)
                {
                    return 1;
                }

                var comparison = StringComparer.Ordinal.Compare(left.CanonicalName, right.CanonicalName);
                if (comparison != 0)
                {
                    return comparison;
                }

                var leftLocation = left.Attribute.GetLocation();
                var rightLocation = right.Attribute.GetLocation();
                comparison = StringComparer.Ordinal.Compare(leftLocation.SourceTree?.FilePath, rightLocation.SourceTree?.FilePath);
                if (comparison != 0)
                {
                    return comparison;
                }

                return leftLocation.SourceSpan.Start.CompareTo(rightLocation.SourceSpan.Start);
            }
        }
    }
}
