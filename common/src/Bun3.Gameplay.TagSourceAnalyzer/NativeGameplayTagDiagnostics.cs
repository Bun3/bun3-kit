#nullable enable
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Bun3.Gameplay.TagSourceAnalyzer
{
    internal static class NativeGameplayTagDiagnostics
    {
        internal static readonly DiagnosticDescriptor MissingOrMultipleSource = Create(
            "B3TAG0001",
            "Native GameplayTag source declaration is required",
            "Native tag declaration requires exactly one assembly GameplayTagSource attribute");

        internal static readonly DiagnosticDescriptor InvalidField = Create(
            "B3TAG0002",
            "Native GameplayTag field is invalid",
            "NativeGameplayTag may annotate only const string fields");

        internal static readonly DiagnosticDescriptor InvalidTagName = Create(
            "B3TAG0003",
            "Native GameplayTag name is invalid",
            "Native gameplay tag name is invalid");

        internal static readonly DiagnosticDescriptor DuplicateTagName = Create(
            "B3TAG0004",
            "Native GameplayTag name is duplicated",
            "Native gameplay tag is duplicated after canonicalization");

        internal static readonly DiagnosticDescriptor InvalidSource = Create(
            "B3TAG0005",
            "GameplayTag source is invalid",
            "GameplayTag Source ID or display name is invalid");

        internal static readonly ImmutableArray<DiagnosticDescriptor> All = ImmutableArray.Create(
            MissingOrMultipleSource,
            InvalidField,
            InvalidTagName,
            DuplicateTagName,
            InvalidSource);

        private static DiagnosticDescriptor Create(string id, string title, string messageFormat) =>
            new DiagnosticDescriptor(id, title, messageFormat, "GameplayTag", DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
