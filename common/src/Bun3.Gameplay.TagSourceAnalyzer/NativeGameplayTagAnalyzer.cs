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
    /// Validates Native GameplayTag declarations at compile time without reading external state.
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
                    var attributeList = (AttributeListSyntax)syntaxNodeContext.Node;
                    var attribute = FindAttribute(attributeList.Attributes, syntaxNodeContext.SemanticModel, nativeTagAttribute);
                    if (attribute is null)
                    {
                        return;
                    }

                    foreach (var field in GetAnnotatedFields(attributeList, syntaxNodeContext.SemanticModel))
                    {
                        fields.Enqueue(new AnnotatedField(field, attribute));
                    }
                }, SyntaxKind.AttributeList);

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

            var annotatedFieldComparer = new AnnotatedFieldComparer(context.Compilation);
            Array.Sort(annotatedFields, annotatedFieldComparer);
            var diagnostics = new List<DiagnosticCandidate>();

            var sourceAttributes = FindAttributes(context.Compilation.Assembly.GetAttributes(), sourceAttribute);
            if (sourceAttributes.Count != 1)
            {
                diagnostics.Add(new DiagnosticCandidate(
                    NativeGameplayTagDiagnostics.MissingOrMultipleSource,
                    Location.None,
                    NativeGameplayTagDiagnostics.MissingOrMultipleSource.Id));
            }
            else if (!IsValidSource(sourceAttributes[0]))
            {
                diagnostics.Add(new DiagnosticCandidate(
                    NativeGameplayTagDiagnostics.InvalidSource,
                    GetLocation(sourceAttributes[0]),
                    NativeGameplayTagDiagnostics.InvalidSource.Id));
            }

            var declarations = new List<TagDeclaration>();
            foreach (var annotatedField in annotatedFields)
            {
                var field = annotatedField.Field;
                var attribute = annotatedField.Attribute;

                if (field.DeclaredAccessibility != Accessibility.Public || !field.IsConst || field.Type.SpecialType != SpecialType.System_String)
                {
                    diagnostics.Add(new DiagnosticCandidate(
                        NativeGameplayTagDiagnostics.InvalidField,
                        attribute.GetLocation(),
                        GetFieldIdentity(field)));
                    continue;
                }

                if (field.ConstantValue is not string value || !TryFoldTagName(value, out var canonicalName))
                {
                    diagnostics.Add(new DiagnosticCandidate(
                        NativeGameplayTagDiagnostics.InvalidTagName,
                        attribute.GetLocation(),
                        GetFieldIdentity(field)));
                    continue;
                }

                declarations.Add(new TagDeclaration(canonicalName, annotatedField));
            }

            declarations.Sort(new TagDeclarationComparer(annotatedFieldComparer));
            CollectDuplicateDiagnostics(diagnostics, declarations);
            diagnostics.Sort(new DiagnosticCandidateComparer(context.Compilation));
            foreach (var diagnostic in diagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(diagnostic.Descriptor, diagnostic.Location));
            }
        }

        private static void CollectDuplicateDiagnostics(List<DiagnosticCandidate> diagnostics, List<TagDeclaration> declarations)
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
                        var annotatedField = declarations[duplicate].AnnotatedField;
                        diagnostics.Add(new DiagnosticCandidate(
                            NativeGameplayTagDiagnostics.DuplicateTagName,
                            annotatedField.Attribute.GetLocation(),
                            GetFieldIdentity(annotatedField.Field)));
                    }
                }

                index = end;
            }
        }

        private static string GetFieldIdentity(IFieldSymbol field) =>
            field.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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

        private static IEnumerable<IFieldSymbol> GetAnnotatedFields(AttributeListSyntax attributeList, SemanticModel semanticModel)
        {
            switch (attributeList.Parent)
            {
                case FieldDeclarationSyntax fieldDeclaration:
                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        if (semanticModel.GetDeclaredSymbol(variable) is IFieldSymbol field)
                        {
                            yield return field;
                        }
                    }

                    yield break;

                case EnumMemberDeclarationSyntax enumMember:
                    if (semanticModel.GetDeclaredSymbol(enumMember) is IFieldSymbol enumField)
                    {
                        yield return enumField;
                    }

                    yield break;

                case PropertyDeclarationSyntax propertyDeclaration when IsFieldTarget(attributeList):
                    if (semanticModel.GetDeclaredSymbol(propertyDeclaration) is IPropertySymbol property
                        && FindAssociatedField(property.ContainingType, property) is IFieldSymbol backingField)
                    {
                        yield return backingField;
                    }

                    yield break;

                case ParameterSyntax parameterSyntax when IsFieldTarget(attributeList):
                    if (semanticModel.GetDeclaredSymbol(parameterSyntax) is IParameterSymbol parameter
                        && FindRecordParameterBackingField(parameter) is IFieldSymbol recordBackingField)
                    {
                        yield return recordBackingField;
                    }

                    yield break;

                case EventFieldDeclarationSyntax eventFieldDeclaration when IsFieldTarget(attributeList):
                    foreach (var variable in eventFieldDeclaration.Declaration.Variables)
                    {
                        if (semanticModel.GetDeclaredSymbol(variable) is IEventSymbol eventSymbol
                            && FindAssociatedField(eventSymbol.ContainingType, eventSymbol) is IFieldSymbol eventBackingField)
                        {
                            yield return eventBackingField;
                        }
                    }

                    yield break;
            }
        }

        private static bool IsFieldTarget(AttributeListSyntax attributeList) =>
            attributeList.Target?.Identifier.IsKind(SyntaxKind.FieldKeyword) == true;

        private static IFieldSymbol? FindRecordParameterBackingField(IParameterSymbol parameter)
        {
            foreach (var member in parameter.ContainingType.GetMembers(parameter.Name))
            {
                if (member is IPropertySymbol property
                    && FindAssociatedField(parameter.ContainingType, property) is IFieldSymbol backingField)
                {
                    return backingField;
                }
            }

            return null;
        }

        private static IFieldSymbol? FindAssociatedField(INamedTypeSymbol containingType, ISymbol associatedSymbol)
        {
            foreach (var member in containingType.GetMembers())
            {
                if (member is IFieldSymbol field
                    && SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, associatedSymbol))
                {
                    return field;
                }
            }

            return null;
        }

        private static AttributeSyntax? FindAttribute(
            SeparatedSyntaxList<AttributeSyntax> attributes,
            SemanticModel semanticModel,
            INamedTypeSymbol attributeType)
        {
            foreach (var attribute in attributes)
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
            internal TagDeclaration(string canonicalName, AnnotatedField annotatedField)
            {
                CanonicalName = canonicalName;
                AnnotatedField = annotatedField;
            }

            internal string CanonicalName { get; }

            internal AnnotatedField AnnotatedField { get; }
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

        private sealed class DiagnosticCandidate
        {
            internal DiagnosticCandidate(DiagnosticDescriptor descriptor, Location location, string finalKey)
            {
                Descriptor = descriptor;
                Location = location;
                FinalKey = finalKey;
            }

            internal DiagnosticDescriptor Descriptor { get; }

            internal Location Location { get; }

            internal string FinalKey { get; }
        }

        private sealed class AnnotatedFieldComparer : IComparer<AnnotatedField>
        {
            private readonly Dictionary<SyntaxTree, int> _syntaxTreeOrdinals = new Dictionary<SyntaxTree, int>();

            internal AnnotatedFieldComparer(Compilation compilation)
            {
                var index = 0;
                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    _syntaxTreeOrdinals.Add(syntaxTree, index++);
                }
            }

            public int Compare(AnnotatedField? left, AnnotatedField? right)
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

                var leftLocation = left.Attribute.GetLocation();
                var rightLocation = right.Attribute.GetLocation();
                var comparison = GetSyntaxTreeOrdinal(leftLocation.SourceTree).CompareTo(GetSyntaxTreeOrdinal(rightLocation.SourceTree));
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = leftLocation.SourceSpan.Start.CompareTo(rightLocation.SourceSpan.Start);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = leftLocation.SourceSpan.Length.CompareTo(rightLocation.SourceSpan.Length);
                if (comparison != 0)
                {
                    return comparison;
                }

                return StringComparer.Ordinal.Compare(
                    left.Field.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    right.Field.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            private int GetSyntaxTreeOrdinal(SyntaxTree? syntaxTree) =>
                syntaxTree is not null && _syntaxTreeOrdinals.TryGetValue(syntaxTree, out var ordinal) ? ordinal : int.MaxValue;
        }

        private sealed class TagDeclarationComparer : IComparer<TagDeclaration>
        {
            private readonly IComparer<AnnotatedField> _annotatedFieldComparer;

            internal TagDeclarationComparer(IComparer<AnnotatedField> annotatedFieldComparer)
            {
                _annotatedFieldComparer = annotatedFieldComparer;
            }

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
                return comparison != 0
                    ? comparison
                    : _annotatedFieldComparer.Compare(left.AnnotatedField, right.AnnotatedField);
            }
        }

        private sealed class DiagnosticCandidateComparer : IComparer<DiagnosticCandidate>
        {
            private readonly Dictionary<SyntaxTree, int> _syntaxTreeOrdinals = new Dictionary<SyntaxTree, int>();

            internal DiagnosticCandidateComparer(Compilation compilation)
            {
                var index = 0;
                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    _syntaxTreeOrdinals.Add(syntaxTree, index++);
                }
            }

            public int Compare(DiagnosticCandidate? left, DiagnosticCandidate? right)
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

                var comparison = GetSyntaxTreeOrdinal(left.Location.SourceTree).CompareTo(GetSyntaxTreeOrdinal(right.Location.SourceTree));
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = left.Location.SourceSpan.Length.CompareTo(right.Location.SourceSpan.Length);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = StringComparer.Ordinal.Compare(left.FinalKey, right.FinalKey);
                return comparison != 0
                    ? comparison
                    : StringComparer.Ordinal.Compare(left.Descriptor.Id, right.Descriptor.Id);
            }

            private int GetSyntaxTreeOrdinal(SyntaxTree? syntaxTree) =>
                syntaxTree is not null && _syntaxTreeOrdinals.TryGetValue(syntaxTree, out var ordinal) ? ordinal : int.MaxValue;
        }
    }
}
