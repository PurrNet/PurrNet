using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PurrNet.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RpcUnusedParameterSuppressor : DiagnosticSuppressor
    {
        private const string Justification =
            "Parameters of PurrNet RPC methods are consumed by the generated networking code.";

        private static readonly SuppressionDescriptor[] descriptors =
        {
            new SuppressionDescriptor("PNSP0001", "IDE0060", Justification),
            new SuppressionDescriptor("PNSP0002", "CA1801", Justification),
        };

        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
            ImmutableArray.Create(descriptors);

        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            PurrNetSymbols? symbols = null;

            foreach (var diagnostic in context.ReportedDiagnostics)
            {
                var tree = diagnostic.Location.SourceTree;
                if (tree == null)
                    continue;

                var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);
                var methodSyntax = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (methodSyntax == null)
                    continue;

                symbols ??= PurrNetSymbols.Create(context.Compilation);
                if (!symbols.HasPurrNet)
                    return;

                var model = context.GetSemanticModel(tree);
                if (model.GetDeclaredSymbol(methodSyntax, context.CancellationToken) is not IMethodSymbol method)
                    continue;

                if (!HasRpcAttribute(method, symbols))
                    continue;

                foreach (var descriptor in descriptors)
                {
                    if (descriptor.SuppressedDiagnosticId == diagnostic.Id)
                        context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
                }
            }
        }

        private static bool HasRpcAttribute(IMethodSymbol method, PurrNetSymbols symbols)
        {
            var attributes = method.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                if (symbols.IsRpcAttribute(attributes[i].AttributeClass))
                    return true;
            }

            return false;
        }
    }
}
