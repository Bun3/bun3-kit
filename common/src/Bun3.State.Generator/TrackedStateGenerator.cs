using System.Text;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Bun3.State.Generator
{
    /// <summary>Generates tracked state properties and explicit runtime callbacks.</summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class TrackedStateGenerator : IIncrementalGenerator
    {
        /// <summary>Registers schema discovery and source generation.</summary>
        /// <param name="context">The compiler initialization context.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var states = context.SyntaxProvider.CreateSyntaxProvider(
                (node, _) => node is TypeDeclarationSyntax declaration && declaration.AttributeLists.Count != 0,
                (syntax, cancellation) =>
                {
                    var symbol = syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, cancellation) as INamedTypeSymbol;
                    var attribute = symbol?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TrackedStateModel.StateAttribute);
                    var reference = attribute?.ApplicationSyntaxReference;
                    return reference != null && reference.SyntaxTree == syntax.Node.SyntaxTree && syntax.Node.Span.Contains(reference.Span) ? symbol : null;
                }).Where(symbol => symbol != null);
            context.RegisterSourceOutput(states.Combine(context.CompilationProvider), (production, input) =>
            {
                var symbol = input.Left!;
                var model = new TrackedStateModel(symbol, input.Right);
                foreach (var diagnostic in model.Diagnostics) production.ReportDiagnostic(diagnostic);
                if (model.Diagnostics.Count != 0) return;
                var hint = symbol!.ToDisplayString() + ".TrackedState.g.cs";
                production.AddSource(hint, SourceText.From(TrackedStateEmitter.Emit(model), Encoding.UTF8));
            });
        }
    }
}
