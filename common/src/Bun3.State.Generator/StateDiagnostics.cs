using Microsoft.CodeAnalysis;

namespace Bun3.State.Generator
{
    internal static class StateDiagnostics
    {
        internal static readonly DiagnosticDescriptor Declaration = Create("B3STATE0001", "Unsupported state declaration", "State '{0}' must be a namespace-level, non-generic, sealed partial class without tracked inheritance");
        internal static readonly DiagnosticDescriptor Field = Create("B3STATE0002", "Unsupported tracked field", "Tracked field '{0}' must be private, non-static, and a supported scalar or child, or a readonly tracked list");
        internal static readonly DiagnosticDescriptor Name = Create("B3STATE0003", "Generated member name collision", "State member '{0}' has an invalid or conflicting generated name");
        internal static readonly DiagnosticDescriptor Mutation = Create("B3STATE0004", "Tracked field mutation bypass", "Use the generated property instead of directly writing or exposing tracked field '{0}' by reference");
        internal static readonly DiagnosticDescriptor Omission = Create("B3STATE0005", "Mutable state is not tracked", "Mutable storage '{0}' declared in a tracked schema must use a supported tracked field");

        private static DiagnosticDescriptor Create(string id, string title, string message) =>
            new DiagnosticDescriptor(id, title, message, "Bun3.State", DiagnosticSeverity.Error, true);
    }
}
