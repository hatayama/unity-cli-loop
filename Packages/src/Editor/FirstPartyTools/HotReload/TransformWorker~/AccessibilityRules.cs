using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static class AccessibilityRules
{
    public static bool IsInaccessibleFromExternalAssembly(ISymbol symbol)
    {
        if (IsNonCrossAssemblySymbol(symbol))
        {
            return false;
        }

        // Local functions / lambdas are emitted into the shim assembly itself, so they have no
        // cross-assembly accessibility problem and must not be treated as accessor targets.
        if (IsLocalOrAnonymousFunction(symbol))
        {
            return false;
        }

        if (symbol is ITypeSymbol typeSymbol)
        {
            return HasInaccessibleAccessibility(typeSymbol.DeclaredAccessibility)
                || (typeSymbol.ContainingType != null
                    && IsInaccessibleFromExternalAssembly(typeSymbol.ContainingType));
        }

        if (IsMemberSymbol(symbol))
        {
            return HasInaccessibleMemberAccessibility(symbol);
        }

        return false;
    }

    private static bool IsNonCrossAssemblySymbol(ISymbol symbol)
    {
        return symbol is ILocalSymbol
            || symbol is IParameterSymbol
            || symbol is IRangeVariableSymbol
            || symbol is ITypeParameterSymbol
            || symbol is INamespaceSymbol
            || symbol is ILabelSymbol
            || symbol is IDiscardSymbol;
    }

    private static bool IsLocalOrAnonymousFunction(ISymbol symbol)
    {
        return symbol is IMethodSymbol methodKindSymbol
            && (methodKindSymbol.MethodKind == MethodKind.LocalFunction
                || methodKindSymbol.MethodKind == MethodKind.AnonymousFunction);
    }

    private static bool IsMemberSymbol(ISymbol symbol)
    {
        return symbol is IFieldSymbol
            || symbol is IPropertySymbol
            || symbol is IMethodSymbol
            || symbol is IEventSymbol;
    }

    private static bool HasInaccessibleMemberAccessibility(ISymbol symbol)
    {
        if (HasInaccessibleAccessibility(symbol.DeclaredAccessibility))
        {
            return true;
        }

        // Recurse through nested containing types (same rule as the type-symbol branch).
        return symbol.ContainingType != null
            && IsInaccessibleFromExternalAssembly(symbol.ContainingType);
    }

    /// <summary>
    /// What: accessibility of a property get/set accessor method (not the property declaration).
    /// Missing accessors are treated as inaccessible so write-only/read-only misuse fails closed.
    /// </summary>
    public static bool IsInaccessibleAccessor(IMethodSymbol accessorMethod)
    {
        if (accessorMethod == null)
        {
            return true;
        }

        return IsInaccessibleFromExternalAssembly(accessorMethod);
    }

    private static bool HasInaccessibleAccessibility(Accessibility accessibility)
    {
        return accessibility == Accessibility.Private
            || accessibility == Accessibility.Internal
            || accessibility == Accessibility.Protected
            || accessibility == Accessibility.ProtectedAndInternal
            || accessibility == Accessibility.ProtectedOrInternal;
    }

    /// <summary>
    /// What: whether a type can appear in an accessor signature / as a type mention in a shim
    /// that will JIT outside Harmony skip-visibility.
    /// </summary>
    public static bool IsExternallyVisibleType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (typeSymbol is ITypeParameterSymbol)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsExternallyVisibleType(arrayType.ElementType);
        }

        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            return IsExternallyVisibleType(pointerType.PointedAtType);
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (IsInaccessibleFromExternalAssembly(namedType))
            {
                return false;
            }

            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                if (!IsExternallyVisibleType(typeArgument))
                {
                    return false;
                }
            }

            return true;
        }

        return !IsInaccessibleFromExternalAssembly(typeSymbol);
    }
}
