using System.Text;
using Microsoft.CodeAnalysis;

namespace PurrNet.Analyzers
{
    internal static class SymbolNames
    {
        public static string FullName(ISymbol symbol)
        {
            var builder = new StringBuilder(symbol.MetadataName);

            for (var containingType = symbol.ContainingType; containingType != null; containingType = containingType.ContainingType)
            {
                builder.Insert(0, ".");
                builder.Insert(0, containingType.MetadataName);
            }

            for (var containingNamespace = symbol.ContainingNamespace;
                 containingNamespace != null && !containingNamespace.IsGlobalNamespace;
                 containingNamespace = containingNamespace.ContainingNamespace)
            {
                builder.Insert(0, ".");
                builder.Insert(0, containingNamespace.MetadataName);
            }

            return builder.ToString();
        }
    }
}
