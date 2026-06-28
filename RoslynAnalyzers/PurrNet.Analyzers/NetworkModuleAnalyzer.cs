using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace PurrNet.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NetworkModuleAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                PurrNetDiagnostics.NetworkModuleOutsideNetworkType,
                PurrNetDiagnostics.StaticNetworkModuleField,
                PurrNetDiagnostics.NetworkModuleProperty,
                PurrNetDiagnostics.UninitializedNetworkModuleField,
                PurrNetDiagnostics.NetworkModuleFieldReassignedAfterInitialization);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(startContext =>
            {
                var symbols = PurrNetSymbols.Create(startContext.Compilation);
                if (!symbols.HasPurrNet || symbols.NetworkModule == null)
                    return;

                startContext.RegisterSymbolAction(symbolContext => AnalyzeField(symbolContext, symbols), SymbolKind.Field);
                startContext.RegisterSymbolAction(symbolContext => AnalyzeProperty(symbolContext, symbols), SymbolKind.Property);
                startContext.RegisterOperationAction(operationContext => AnalyzeAssignment(operationContext, symbols), OperationKind.SimpleAssignment);
            });
        }

        private static void AnalyzeField(SymbolAnalysisContext context, PurrNetSymbols symbols)
        {
            var field = (IFieldSymbol)context.Symbol;

            if (!symbols.IsNetworkModule(field.Type))
                return;

            if (field.IsImplicitlyDeclared)
                return;

            var containingType = field.ContainingType;

            if (field.IsStatic)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.StaticNetworkModuleField,
                    field.Locations.FirstOrDefault(),
                    field.Name));
                return;
            }

            if (!symbols.IsNetworkType(containingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.NetworkModuleOutsideNetworkType,
                    field.Locations.FirstOrDefault(),
                    field.Name,
                    containingType?.Name ?? "<unknown>"));
                return;
            }

            if (!IsUnitySerializedField(field) &&
                !HasNonNullFieldInitializer(context.Compilation, field) &&
                !HasAssignmentInInitializationMethod(context.Compilation, field))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.UninitializedNetworkModuleField,
                    field.Locations.FirstOrDefault(),
                    field.Name));
            }
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context, PurrNetSymbols symbols)
        {
            var property = (IPropertySymbol)context.Symbol;
            if (property.IsImplicitlyDeclared || property.IsIndexer)
                return;

            if (!symbols.IsNetworkModule(property.Type))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                PurrNetDiagnostics.NetworkModuleProperty,
                property.Locations.FirstOrDefault(),
                property.Name));
        }

        private static void AnalyzeAssignment(OperationAnalysisContext context, PurrNetSymbols symbols)
        {
            var assignment = (ISimpleAssignmentOperation)context.Operation;
            var field = UnwrapFieldReference(assignment.Target);

            if (field == null || !symbols.IsNetworkModule(field.Type))
                return;

            if (!symbols.IsNetworkType(field.ContainingType))
                return;

            if (IsAllowedInitializationMethod(context.ContainingSymbol as IMethodSymbol))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                PurrNetDiagnostics.NetworkModuleFieldReassignedAfterInitialization,
                assignment.Syntax.GetLocation(),
                field.Name));
        }

        private static IFieldSymbol? UnwrapFieldReference(IOperation operation)
        {
            while (operation is IConversionOperation conversion)
                operation = conversion.Operand;

            return operation is IFieldReferenceOperation fieldReference
                ? fieldReference.Field
                : null;
        }

        private static bool HasNonNullFieldInitializer(Compilation compilation, IFieldSymbol field)
        {
            foreach (var reference in field.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not VariableDeclaratorSyntax declarator ||
                    declarator.Initializer == null)
                {
                    continue;
                }

                if (IsNullLikeExpression(declarator.Initializer.Value))
                    continue;

                var semanticModel = compilation.GetSemanticModel(declarator.SyntaxTree);
                var constantValue = semanticModel.GetConstantValue(declarator.Initializer.Value);
                if (constantValue.HasValue && constantValue.Value == null)
                    continue;

                return true;
            }

            return false;
        }

        private static bool IsUnitySerializedField(IFieldSymbol field)
        {
            if (field.IsStatic || field.IsConst || field.IsReadOnly)
                return false;

            if (HasAttribute(field, "System.NonSerializedAttribute"))
                return false;

            if (field.DeclaredAccessibility == Accessibility.Public)
                return true;

            return HasAttribute(field, "UnityEngine.SerializeField") ||
                   HasAttribute(field, "UnityEngine.SerializeFieldAttribute") ||
                   HasAttribute(field, "UnityEngine.SerializeReference") ||
                   HasAttribute(field, "UnityEngine.SerializeReferenceAttribute");
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null)
                    continue;

                if (SymbolNames.FullName(attributeClass) == metadataName)
                    return true;
            }

            return false;
        }

        private static bool HasAssignmentInInitializationMethod(Compilation compilation, IFieldSymbol field)
        {
            var containingType = field.ContainingType;
            if (containingType == null)
                return false;

            foreach (var reference in containingType.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration)
                    continue;

                var semanticModel = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);
                foreach (var member in typeDeclaration.Members)
                {
                    if (!IsInitializationMember(member))
                        continue;

                    foreach (var assignment in member.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                        if (SymbolEqualityComparer.Default.Equals(assignedSymbol, field))
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool IsInitializationMember(MemberDeclarationSyntax member)
        {
            if (member is ConstructorDeclarationSyntax)
                return true;

            if (member is not MethodDeclarationSyntax method)
                return false;

            return method.Identifier.ValueText == "Awake" ||
                   method.Identifier.ValueText == "OnInitializeModules";
        }

        private static bool IsAllowedInitializationMethod(IMethodSymbol? method)
        {
            if (method == null)
                return false;

            if (method.MethodKind == MethodKind.Constructor)
                return true;

            return method.Name == "Awake" ||
                   method.Name == "OnInitializeModules";
        }

        private static bool IsNullLikeExpression(ExpressionSyntax expression)
        {
            expression = StripParentheses(expression);

            if (expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
                expression is DefaultExpressionSyntax)
            {
                return true;
            }

            if (expression is PostfixUnaryExpressionSyntax postfix &&
                postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                return IsNullLikeExpression(postfix.Operand);
            }

            return false;
        }

        private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
                expression = parenthesized.Expression;

            return expression;
        }
    }
}
