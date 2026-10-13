using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bun3.State.Generator
{
    internal sealed class TrackedStateModel
    {
        internal const string StateAttribute = "Bun3.State.TrackedStateAttribute";
        internal const string FieldAttribute = "Bun3.State.TrackedAttribute";
        internal const string MetadataName = "__bun3Tracking";
        internal INamedTypeSymbol Symbol { get; }
        internal List<TrackedField> Fields { get; } = new List<TrackedField>();
        internal List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

        internal TrackedStateModel(INamedTypeSymbol symbol, Compilation compilation)
        {
            Symbol = symbol;
            if (!ValidDeclaration(symbol))
            {
                Report(StateDiagnostics.Declaration, symbol);
                return;
            }

            var names = new HashSet<string>(symbol.GetMembers().Select(m => m.Name), StringComparer.Ordinal);
            for (var parent = symbol.BaseType; parent != null; parent = parent.BaseType)
                foreach (var member in parent.GetMembers())
                    if (compilation.IsSymbolAccessibleWithin(member, symbol)) names.Add(member.Name);
            names.Add(symbol.Name);
            if (!names.Add(MetadataName)) Report(StateDiagnostics.Name, symbol);
            foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>().Where(f => HasAttribute(f, FieldAttribute)).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                var kind = GetKind(field.Type);
                if (field.IsStatic || field.IsConst || field.DeclaredAccessibility != Accessibility.Private || kind == FieldKind.Unsupported ||
                    (kind == FieldKind.List ? !field.IsReadOnly : field.IsReadOnly))
                {
                    Report(StateDiagnostics.Field, field);
                    continue;
                }
                var trimmed = field.Name.TrimStart('_');
                var name = trimmed.Length == 0 ? "" : char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
                if (!SyntaxFacts.IsValidIdentifier(name) || !names.Add(name))
                {
                    Report(StateDiagnostics.Name, field);
                    continue;
                }
                Fields.Add(new TrackedField(field, name, Fields.Count, kind));
            }
        }

        internal static bool HasAttribute(ISymbol symbol, string name) => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == name);

        private static bool ValidDeclaration(INamedTypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Class || type.IsRecord || !type.IsSealed || type.IsStatic || type.ContainingType != null ||
                type.Arity != 0 || type.ContainingNamespace.IsGlobalNamespace ||
                type.DeclaringSyntaxReferences.Any(r => !(r.GetSyntax() is ClassDeclarationSyntax c) || !c.Modifiers.Any(SyntaxKind.PartialKeyword))) return false;
            for (var parent = type.BaseType; parent != null; parent = parent.BaseType)
                if (HasAttribute(parent, StateAttribute) || parent.AllInterfaces.Any(i => i.ToDisplayString() == "Bun3.State.ITrackedNode")) return false;
            return true;
        }

        internal static FieldKind GetKind(ITypeSymbol type)
        {
            if (IsScalar(type)) return FieldKind.Scalar;
            if (type is INamedTypeSymbol named)
            {
                if (HasAttribute(named, StateAttribute) && ValidDeclaration(named)) return FieldKind.Child;
                if (named.OriginalDefinition.ToDisplayString() == "Bun3.State.TrackedList<T>" &&
                    (IsScalar(named.TypeArguments[0]) || GetKind(named.TypeArguments[0]) == FieldKind.Child)) return FieldKind.List;
            }
            return FieldKind.Unsupported;
        }

        internal static bool IsScalar(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                return IsScalar(nullable.TypeArguments[0]);
            if (type.TypeKind == TypeKind.Enum) return true;
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: case SpecialType.System_Byte: case SpecialType.System_SByte:
                case SpecialType.System_Int16: case SpecialType.System_UInt16: case SpecialType.System_Int32:
                case SpecialType.System_UInt32: case SpecialType.System_Int64: case SpecialType.System_UInt64:
                case SpecialType.System_Char: case SpecialType.System_Single: case SpecialType.System_Double:
                case SpecialType.System_Decimal: case SpecialType.System_String: case SpecialType.System_DateTime:
                case SpecialType.System_IntPtr: case SpecialType.System_UIntPtr:
                    return true;
            }
            var name = type.ToDisplayString();
            return name == "System.DateTimeOffset" || name == "System.TimeSpan" || name == "System.Guid";
        }

        private void Report(DiagnosticDescriptor descriptor, ISymbol symbol) => Diagnostics.Add(Diagnostic.Create(descriptor, symbol.Locations.FirstOrDefault(), symbol.Name));
    }

    internal enum FieldKind { Unsupported, Scalar, Child, List }

    internal sealed class TrackedField
    {
        internal IFieldSymbol Symbol { get; }
        internal string PropertyName { get; }
        internal int Slot { get; }
        internal FieldKind Kind { get; }
        internal TrackedField(IFieldSymbol symbol, string propertyName, int slot, FieldKind kind)
        {
            Symbol = symbol;
            PropertyName = propertyName;
            Slot = slot;
            Kind = kind;
        }
    }
}
