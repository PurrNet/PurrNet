using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace PurrNet.Analyzers
{
    internal sealed class PurrNetSymbols
    {
        public INamedTypeSymbol? ServerRpcAttribute { get; }
        public INamedTypeSymbol? ObserversRpcAttribute { get; }
        public INamedTypeSymbol? TargetRpcAttribute { get; }
        public INamedTypeSymbol? NetworkIdentity { get; }
        public INamedTypeSymbol? NetworkModule { get; }
        public INamedTypeSymbol? RpcInfo { get; }
        public INamedTypeSymbol? PlayerId { get; }
        public INamedTypeSymbol? OwnerAttribute { get; }
        public INamedTypeSymbol? LocalModeAttribute { get; }
        public IMethodSymbol? EnterLocalExecution { get; }
        public IMethodSymbol? ExitLocalExecution { get; }
        public INamedTypeSymbol? IEnumerableOfT { get; }
        public INamedTypeSymbol? IListOfT { get; }

        public bool HasPurrNet =>
            ServerRpcAttribute != null ||
            ObserversRpcAttribute != null ||
            TargetRpcAttribute != null ||
            NetworkIdentity != null ||
            NetworkModule != null;

        private PurrNetSymbols(Compilation compilation)
        {
            ServerRpcAttribute = compilation.GetTypeByMetadataName("PurrNet.ServerRpcAttribute");
            ObserversRpcAttribute = compilation.GetTypeByMetadataName("PurrNet.ObserversRpcAttribute");
            TargetRpcAttribute = compilation.GetTypeByMetadataName("PurrNet.TargetRpcAttribute");
            NetworkIdentity = compilation.GetTypeByMetadataName("PurrNet.NetworkIdentity");
            NetworkModule = compilation.GetTypeByMetadataName("PurrNet.NetworkModule");
            RpcInfo = compilation.GetTypeByMetadataName("PurrNet.RPCInfo");
            PlayerId = compilation.GetTypeByMetadataName("PurrNet.PlayerID");
            OwnerAttribute = compilation.GetTypeByMetadataName("PurrNet.OwnerAttribute");
            LocalModeAttribute = compilation.GetTypeByMetadataName("PurrNet.LocalModeAttribute");
            IEnumerableOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
            IListOfT = compilation.GetTypeByMetadataName("System.Collections.Generic.IList`1");

            var compilerFlags = compilation.GetTypeByMetadataName("PurrNet.PurrCompilerFlags");
            if (compilerFlags != null)
            {
                foreach (var member in compilerFlags.GetMembers())
                {
                    if (member is not IMethodSymbol method)
                        continue;

                    if (method.Name == "EnterLocalExecution")
                        EnterLocalExecution = method;
                    else if (method.Name == "ExitLocalExecution")
                        ExitLocalExecution = method;
                }
            }
        }

        public static PurrNetSymbols Create(Compilation compilation) => new PurrNetSymbols(compilation);

        public bool IsRpcAttribute(INamedTypeSymbol? attributeType) =>
            IsSame(attributeType, ServerRpcAttribute) ||
            IsSame(attributeType, ObserversRpcAttribute) ||
            IsSame(attributeType, TargetRpcAttribute);

        public bool IsServerRpc(AttributeData attribute) => IsSame(attribute.AttributeClass, ServerRpcAttribute);

        public bool IsObserversRpc(AttributeData attribute) => IsSame(attribute.AttributeClass, ObserversRpcAttribute);

        public bool IsTargetRpc(AttributeData attribute) => IsSame(attribute.AttributeClass, TargetRpcAttribute);

        public bool IsNetworkIdentity(INamedTypeSymbol? type) => InheritsFrom(type, NetworkIdentity);

        public bool IsNetworkModule(ITypeSymbol? type) => InheritsFrom(type as INamedTypeSymbol, NetworkModule);

        public bool IsNetworkType(INamedTypeSymbol? type) =>
            IsNetworkIdentity(type) || InheritsFrom(type, NetworkModule);

        public bool IsRpcInfo(ITypeSymbol? type) => IsSame(type, RpcInfo);

        public bool IsPlayerId(ITypeSymbol? type) => IsSame(type, PlayerId);

        public bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
        {
            if (attributeType == null)
                return false;

            ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                if (IsSame(attributes[i].AttributeClass, attributeType))
                    return true;
            }

            return false;
        }

        public bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol? baseType)
        {
            if (type == null || baseType == null)
                return false;

            for (INamedTypeSymbol? current = type; current != null; current = current.BaseType)
            {
                if (IsSame(current, baseType))
                    return true;
            }

            return false;
        }

        public bool IsSame(ISymbol? a, ISymbol? b) => SymbolEqualityComparer.Default.Equals(a, b);
    }
}
