using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bun3.State.Generator
{
    /// <summary>Rejects mutation bypasses and omitted mutable storage in tracked schemas.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TrackedStateAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>Gets the diagnostics reported by this analyzer.</summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(StateDiagnostics.Mutation, StateDiagnostics.Omission);

        /// <summary>Registers semantic storage and mutation analysis, including user generated-looking files.</summary>
        /// <param name="context">The analyzer initialization context.</param>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeStorage, SymbolKind.Field, SymbolKind.Property);
            context.RegisterOperationAction(AnalyzeReference, OperationKind.FieldReference);
        }

        private static void AnalyzeStorage(SymbolAnalysisContext context)
        {
            var symbol = context.Symbol;
            if (symbol.IsImplicitlyDeclared || symbol.IsStatic || !TrackedStateModel.HasAttribute(symbol.ContainingType, TrackedStateModel.StateAttribute) ||
                symbol.DeclaringSyntaxReferences.All(r => TrackedStateEmitter.IsGenerated(r.SyntaxTree))) return;
            bool omitted;
            if (symbol is IFieldSymbol field)
                omitted = !field.IsConst && !TrackedStateModel.HasAttribute(field, TrackedStateModel.FieldAttribute) &&
                    (!field.IsReadOnly || !TrackedStateModel.IsScalar(field.Type));
            else if (symbol is IPropertySymbol property)
                omitted = property.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is PropertyDeclarationSyntax p &&
                    p.AccessorList != null && p.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null)) &&
                    (property.SetMethod != null || !TrackedStateModel.IsScalar(property.Type));
            else return;
            if (omitted) context.ReportDiagnostic(Diagnostic.Create(StateDiagnostics.Omission, symbol.Locations.FirstOrDefault(), symbol.Name));
        }

        private static void AnalyzeReference(OperationAnalysisContext context)
        {
            var reference = (IFieldReferenceOperation)context.Operation;
            if (!TrackedStateModel.HasAttribute(reference.Field, TrackedStateModel.FieldAttribute) ||
                !TrackedStateModel.HasAttribute(reference.Field.ContainingType, TrackedStateModel.StateAttribute) ||
                TrackedStateEmitter.IsGenerated(reference.Syntax.SyntaxTree)) return;
            IOperation current = reference;
            while (current.Parent is ITupleOperation || current.Parent is IConversionOperation || current.Parent is IParenthesizedOperation)
                current = current.Parent;
            var parent = current.Parent;
            var storage = current.Syntax;
            while (storage.Parent is ParenthesizedExpressionSyntax parentheses)
                storage = parentheses;
            var writes = parent is IAssignmentOperation assignment && assignment.Target == current ||
                parent is IIncrementOrDecrementOperation ||
                parent is IArgumentOperation argument && (argument.Parameter?.RefKind == RefKind.Ref || argument.Parameter?.RefKind == RefKind.Out) ||
                storage.Parent is RefExpressionSyntax ||
                storage.Parent is ArgumentSyntax syntaxArgument &&
                    (syntaxArgument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || syntaxArgument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword));
            if (writes) context.ReportDiagnostic(Diagnostic.Create(StateDiagnostics.Mutation, reference.Syntax.GetLocation(), reference.Field.Name));
        }
    }
}
