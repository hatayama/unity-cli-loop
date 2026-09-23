using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Spells out the declaring types of a nested type that a body names by its simple name, so the
// name still resolves once the body is moved out of the type that declares it.
internal static class NestedTypeNameQualifier
{
    // Why every nested type and not only private ones: the shim lives in a hoisted static class
    // of the same namespace, where no nested type is visible by its simple name.
    internal static TypeSyntax QualifyOrNull(SimpleNameSyntax name, ISymbol symbol)
    {
        Debug.Assert(name != null, "name");

        if (symbol is not INamedTypeSymbol named || named.ContainingType == null)
        {
            return null;
        }

        // Why generic declaring types stay as written: the declaring chain would need the type
        // arguments of the enclosing instantiation, which the shim does not have.
        for (INamedTypeSymbol outer = named.ContainingType; outer != null; outer = outer.ContainingType)
        {
            if (outer.Arity > 0)
            {
                return null;
            }
        }

        return SyntaxFactory
            .ParseTypeName(named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .WithTriviaFrom(name);
    }
}
