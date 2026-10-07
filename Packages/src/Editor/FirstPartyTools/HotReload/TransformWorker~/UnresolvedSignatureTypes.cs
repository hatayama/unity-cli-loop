using Microsoft.CodeAnalysis;

// Finds a type in a method signature that the worker compilation could not bind. An
// inaccessible type is not unresolved: an internal type of the compiled assembly binds as an
// error type with CandidateReason.Inaccessible, its candidate still names the compiled type, and
// such a method matches its compiled signature, so only a missing type counts.
internal static class UnresolvedSignatureTypes
{
    internal static bool TryFindInSignature(IMethodSymbol methodSymbol, out ITypeSymbol unresolvedType)
    {
        if (TryFind(methodSymbol.ReturnType, out unresolvedType))
        {
            return true;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (TryFind(parameter.Type, out unresolvedType))
            {
                return true;
            }
        }

        return false;
    }

    // Same walk as AddedFieldClassifier.TryFindUnresolvedType (error type, array element, type
    // arguments), except that an inaccessible error type is treated as resolved. That walk stays
    // as it is so added fields and properties keep reporting what they report today.
    internal static bool TryFind(ITypeSymbol typeSymbol, out ITypeSymbol unresolvedType)
    {
        unresolvedType = null;
        if (typeSymbol == null)
        {
            return false;
        }

        if (typeSymbol is IErrorTypeSymbol errorType)
        {
            if (errorType.CandidateReason == CandidateReason.Inaccessible
                && errorType.CandidateSymbols.Length > 0)
            {
                return false;
            }

            unresolvedType = typeSymbol;
            return true;
        }

        if (typeSymbol is ITypeParameterSymbol)
        {
            return false;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return TryFind(arrayType.ElementType, out unresolvedType);
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                if (TryFind(typeArgument, out unresolvedType))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
