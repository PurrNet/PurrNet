using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PurrNet.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RpcAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                PurrNetDiagnostics.MultipleRpcAttributes,
                PurrNetDiagnostics.BufferLastWithDeltaPacked,
                PurrNetDiagnostics.InstanceRpcOutsideNetworkType,
                PurrNetDiagnostics.InvalidRpcReturnType,
                PurrNetDiagnostics.ObserversRpcCoroutine,
                PurrNetDiagnostics.ObserversRpcAwaitable,
                PurrNetDiagnostics.AwaitableTargetRpcMultipleTargets,
                PurrNetDiagnostics.InvalidTargetRpcFirstParameter,
                PurrNetDiagnostics.RpcInfoMustBeLast,
                PurrNetDiagnostics.MtuOverrideOnSequencedChannel,
                PurrNetDiagnostics.ImmediateOnNonUnreliableChannel,
                PurrNetDiagnostics.OwnerRequiresNetworkIdentity,
                PurrNetDiagnostics.LocalModeNested,
                PurrNetDiagnostics.LocalModeExitWithoutEnter,
                PurrNetDiagnostics.LocalModeMissingExit);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(startContext =>
            {
                var symbols = PurrNetSymbols.Create(startContext.Compilation);
                if (!symbols.HasPurrNet)
                    return;

                startContext.RegisterSymbolAction(symbolContext => AnalyzeMethod(symbolContext, symbols), SymbolKind.Method);
                startContext.RegisterSyntaxNodeAction(
                    syntaxContext => AnalyzeLocalModeFlags(syntaxContext, symbols),
                    SyntaxKind.MethodDeclaration,
                    SyntaxKind.ConstructorDeclaration,
                    SyntaxKind.DestructorDeclaration,
                    SyntaxKind.OperatorDeclaration,
                    SyntaxKind.ConversionOperatorDeclaration);
            });
        }

        private static void AnalyzeMethod(SymbolAnalysisContext context, PurrNetSymbols symbols)
        {
            var method = (IMethodSymbol)context.Symbol;

            if (method.MethodKind != MethodKind.Ordinary)
                return;

            var rpcAttributes = GetRpcAttributes(method, symbols);
            if (rpcAttributes.Length == 0)
            {
                AnalyzeOwnerGuard(context, symbols, method);
                return;
            }

            if (rpcAttributes.Length > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.MultipleRpcAttributes,
                    method.Locations.FirstOrDefault(),
                    method.Name));
            }

            var rpc = rpcAttributes[0];
            AnalyzeRpcInfo(context, symbols, method);

            if (!method.IsStatic && !symbols.IsNetworkType(method.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.InstanceRpcOutsideNetworkType,
                    method.Locations.FirstOrDefault(),
                    method.Name,
                    method.ContainingType?.Name ?? "<unknown>"));
            }

            bool isObserversRpc = symbols.IsObserversRpc(rpc);
            bool isTargetRpc = symbols.IsTargetRpc(rpc);

            if ((isObserversRpc || isTargetRpc) &&
                GetBoolConstructorArgument(rpc, "bufferLast") &&
                GetBoolConstructorArgument(rpc, "deltaPacked"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.BufferLastWithDeltaPacked,
                    GetAttributeLocation(rpc, method),
                    isObserversRpc ? "ObserversRpc" : "TargetRpc"));
            }

            if (symbols.UnreliableSequencedValue is int sequencedValue &&
                symbols.MtuNetworkManagerValue is int mtuDefaultValue &&
                GetEnumConstructorArgument(rpc, "channel") == sequencedValue)
            {
                var mtuExceeded = GetEnumConstructorArgument(rpc, "mtuExceeded");
                if (mtuExceeded.HasValue && mtuExceeded.Value != mtuDefaultValue)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        PurrNetDiagnostics.MtuOverrideOnSequencedChannel,
                        GetAttributeLocation(rpc, method),
                        method.Name));
                }
            }

            if (symbols.UnreliableValue is int unreliableValue &&
                GetBoolConstructorArgument(rpc, "immediate") &&
                GetEnumConstructorArgument(rpc, "channel") != unreliableValue)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.ImmediateOnNonUnreliableChannel,
                    GetAttributeLocation(rpc, method),
                    method.Name));
            }

            var returnKind = GetReturnKind(method.ReturnType);
            if (returnKind == RpcReturnKind.Invalid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.InvalidRpcReturnType,
                    method.Locations.FirstOrDefault(),
                    method.Name));
            }

            if (isObserversRpc && returnKind == RpcReturnKind.IEnumerator)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.ObserversRpcCoroutine,
                    method.Locations.FirstOrDefault(),
                    method.Name));
            }

            if (isObserversRpc && IsAwaitable(returnKind))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.ObserversRpcAwaitable,
                    method.Locations.FirstOrDefault(),
                    method.Name));
            }

            if (isTargetRpc)
                AnalyzeTargetRpc(context, symbols, method, returnKind);

            AnalyzeOwnerGuard(context, symbols, method);
        }

        private static void AnalyzeRpcInfo(SymbolAnalysisContext context, PurrNetSymbols symbols, IMethodSymbol method)
        {
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var parameter = method.Parameters[i];
                if (!symbols.IsRpcInfo(parameter.Type))
                    continue;

                if (i == method.Parameters.Length - 1)
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.RpcInfoMustBeLast,
                    parameter.Locations.FirstOrDefault(),
                    method.Name));
            }
        }

        private static void AnalyzeTargetRpc(
            SymbolAnalysisContext context,
            PurrNetSymbols symbols,
            IMethodSymbol method,
            RpcReturnKind returnKind)
        {
            var targetKind = method.Parameters.Length == 0
                ? TargetParameterKind.Invalid
                : GetTargetParameterKind(context.Compilation, symbols, method.Parameters[0].Type);

            if (targetKind == TargetParameterKind.Invalid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.InvalidTargetRpcFirstParameter,
                    method.Parameters.Length == 0 ? method.Locations.FirstOrDefault() : method.Parameters[0].Locations.FirstOrDefault(),
                    method.Name));
            }

            if (IsAwaitable(returnKind) && targetKind == TargetParameterKind.Multiple)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.AwaitableTargetRpcMultipleTargets,
                    method.Parameters[0].Locations.FirstOrDefault(),
                    method.Name));
            }
        }

        private static void AnalyzeOwnerGuard(SymbolAnalysisContext context, PurrNetSymbols symbols, IMethodSymbol method)
        {
            if (!symbols.HasAttribute(method, symbols.OwnerAttribute))
                return;

            if (symbols.IsNetworkIdentity(method.ContainingType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                PurrNetDiagnostics.OwnerRequiresNetworkIdentity,
                method.Locations.FirstOrDefault(),
                method.ContainingType?.Name ?? "<unknown>"));
        }

        private static void AnalyzeLocalModeFlags(SyntaxNodeAnalysisContext context, PurrNetSymbols symbols)
        {
            if (symbols.EnterLocalExecution == null || symbols.ExitLocalExecution == null)
                return;

            if (context.Node is not BaseMethodDeclarationSyntax methodSyntax)
                return;

            var method = context.SemanticModel.GetDeclaredSymbol(methodSyntax, context.CancellationToken);
            if (method == null || symbols.HasAttribute(method, symbols.LocalModeAttribute))
                return;

            int depth = 0;
            Location? lastEnterLocation = null;

            foreach (var invocation in methodSyntax.DescendantNodes(ShouldDescendIntoLocalModeChild).OfType<InvocationExpressionSyntax>())
            {
                var invoked = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
                if (invoked == null)
                    continue;

                if (symbols.IsSame(invoked.OriginalDefinition, symbols.EnterLocalExecution))
                {
                    if (depth > 0)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            PurrNetDiagnostics.LocalModeNested,
                            invocation.GetLocation()));
                    }

                    depth++;
                    lastEnterLocation = invocation.GetLocation();
                }
                else if (symbols.IsSame(invoked.OriginalDefinition, symbols.ExitLocalExecution))
                {
                    if (depth == 0)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            PurrNetDiagnostics.LocalModeExitWithoutEnter,
                            invocation.GetLocation()));
                    }
                    else
                    {
                        depth--;
                    }
                }
            }

            if (depth > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.LocalModeMissingExit,
                    lastEnterLocation ?? methodSyntax.GetLocation()));
            }
        }

        private static bool ShouldDescendIntoLocalModeChild(SyntaxNode node) =>
            node is not LocalFunctionStatementSyntax &&
            node is not AnonymousFunctionExpressionSyntax;

        private static ImmutableArray<AttributeData> GetRpcAttributes(IMethodSymbol method, PurrNetSymbols symbols)
        {
            var builder = ImmutableArray.CreateBuilder<AttributeData>();
            foreach (var attribute in method.GetAttributes())
            {
                if (symbols.IsRpcAttribute(attribute.AttributeClass))
                    builder.Add(attribute);
            }

            return builder.ToImmutable();
        }

        private static bool GetBoolConstructorArgument(AttributeData attribute, string name)
        {
            var constructor = attribute.AttributeConstructor;
            if (constructor == null)
                return false;

            for (int i = 0; i < constructor.Parameters.Length; i++)
            {
                if (constructor.Parameters[i].Name != name)
                    continue;

                if (i < attribute.ConstructorArguments.Length &&
                    attribute.ConstructorArguments[i].Value is bool value)
                {
                    return value;
                }

                return constructor.Parameters[i].ExplicitDefaultValue is bool defaultValue && defaultValue;
            }

            return false;
        }

        private static int? GetEnumConstructorArgument(AttributeData attribute, string name)
        {
            var constructor = attribute.AttributeConstructor;
            if (constructor == null)
                return null;

            for (int i = 0; i < constructor.Parameters.Length; i++)
            {
                if (constructor.Parameters[i].Name != name)
                    continue;

                object? value = i < attribute.ConstructorArguments.Length
                    ? attribute.ConstructorArguments[i].Value
                    : constructor.Parameters[i].ExplicitDefaultValue;

                return value == null ? (int?)null : System.Convert.ToInt32(value);
            }

            return null;
        }

        private static Location? GetAttributeLocation(AttributeData attribute, ISymbol fallback)
        {
            var syntax = attribute.ApplicationSyntaxReference?.GetSyntax();
            return syntax?.GetLocation() ?? fallback.Locations.FirstOrDefault();
        }

        private static RpcReturnKind GetReturnKind(ITypeSymbol returnType)
        {
            string name = SymbolNames.FullName(returnType);

            if (name == "System.Void")
                return RpcReturnKind.Void;

            if (name == "System.Collections.IEnumerator")
                return RpcReturnKind.IEnumerator;

            if (name == "System.Threading.Tasks.Task" || name == "System.Threading.Tasks.Task`1")
                return RpcReturnKind.Task;

            if (name == "Cysharp.Threading.Tasks.UniTask" || name == "Cysharp.Threading.Tasks.UniTask`1")
                return RpcReturnKind.UniTask;

            return RpcReturnKind.Invalid;
        }

        private static bool IsAwaitable(RpcReturnKind returnKind) =>
            returnKind == RpcReturnKind.Task || returnKind == RpcReturnKind.UniTask;

        private static TargetParameterKind GetTargetParameterKind(
            Compilation compilation,
            PurrNetSymbols symbols,
            ITypeSymbol type)
        {
            if (symbols.IsPlayerId(type))
                return TargetParameterKind.Single;

            if (type is IArrayTypeSymbol array && symbols.IsPlayerId(array.ElementType))
                return TargetParameterKind.Multiple;

            if (symbols.PlayerId == null)
                return TargetParameterKind.Invalid;

            if (HasImplicitConversion(compilation, type, symbols.IListOfT?.Construct(symbols.PlayerId)) ||
                HasImplicitConversion(compilation, type, symbols.IEnumerableOfT?.Construct(symbols.PlayerId)))
            {
                return TargetParameterKind.Multiple;
            }

            return TargetParameterKind.Invalid;
        }

        private static bool HasImplicitConversion(Compilation compilation, ITypeSymbol source, ITypeSymbol? destination)
        {
            if (destination == null)
                return false;

            var conversion = compilation.ClassifyConversion(source, destination);
            return conversion.Exists && conversion.IsImplicit;
        }

        private enum RpcReturnKind
        {
            Invalid,
            Void,
            Task,
            UniTask,
            IEnumerator
        }

        private enum TargetParameterKind
        {
            Invalid,
            Single,
            Multiple
        }
    }
}
