using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PurrNet.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RegisterNetworkTypeAnalyzer : DiagnosticAnalyzer
    {
        // Mirrors PostProcessor.IsTypeInOwnModule / HandledGenericTypes: these containers are
        // registered by any assembly using them, regardless of where they are declared.
        private static readonly ImmutableHashSet<string> CrossAssemblyContainers = ImmutableHashSet.Create(
            "System.Collections.Generic.List`1",
            "System.Collections.Generic.Dictionary`2",
            "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.Queue`1",
            "System.Collections.Generic.Stack`1",
            "System.Nullable`1",
            "PurrNet.Pooling.DisposableList`1",
            "PurrNet.Pooling.DisposableArray`1",
            "PurrNet.Pooling.DisposableHashSet`1",
            "PurrNet.Pooling.DisposableDictionary`2",
            "Unity.Collections.NativeArray`1",
            "Unity.Collections.NativeList`1");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(PurrNetDiagnostics.RegisterNetworkTypeForeignType);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(startContext =>
            {
                var attributeType =
                    startContext.Compilation.GetTypeByMetadataName("PurrNet.RegisterNetworkTypeAttribute");
                if (attributeType == null)
                    return;

                startContext.RegisterSymbolAction(
                    symbolContext => AnalyzeType(symbolContext, attributeType), SymbolKind.NamedType);
            });
        }

        private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol attributeType)
        {
            var type = (INamedTypeSymbol)context.Symbol;

            foreach (var attribute in type.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                    continue;

                if (attribute.ConstructorArguments.Length == 0 ||
                    attribute.ConstructorArguments[0].Value is not ITypeSymbol target)
                {
                    continue;
                }

                var foreign = FindForeignType(target, context.Compilation.Assembly);
                if (foreign == null)
                    continue;

                var location =
                    attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? type.Locations.FirstOrDefault();

                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.RegisterNetworkTypeForeignType,
                    location,
                    foreign.ToDisplayString(),
                    foreign.ContainingAssembly?.Name ?? "<unknown>"));
            }
        }

        private static INamedTypeSymbol? FindForeignType(ITypeSymbol type, IAssemblySymbol ownAssembly)
        {
            while (type is IArrayTypeSymbol array)
                type = array.ElementType;

            if (type is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
                return null;

            if (named.IsUnboundGenericType)
                return null;

            bool isContainer = CrossAssemblyContainers.Contains(SymbolNames.FullName(named.OriginalDefinition));

            if (!isContainer &&
                named.SpecialType == SpecialType.None &&
                named.ContainingAssembly != null &&
                !SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, ownAssembly))
            {
                return named;
            }

            foreach (var argument in named.TypeArguments)
            {
                var foreign = FindForeignType(argument, ownAssembly);
                if (foreign != null)
                    return foreign;
            }

            return null;
        }
    }
}
