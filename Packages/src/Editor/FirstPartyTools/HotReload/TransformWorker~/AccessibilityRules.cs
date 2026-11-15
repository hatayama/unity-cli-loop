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
    // What: the method an invocation really calls. A reduced extension call (x.M()) binds to a
    // ReducedExtension symbol; its static definition is the method the shim would have to reach.
    internal static IMethodSymbol UnreducedMethod(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.ReducedExtension && method.ReducedFrom != null
            ? method.ReducedFrom
            : method;
    }

    // What: whether an accessor for this inaccessible member would need an instance of a struct
    // as its receiver. Why such accessors are refused: Harmony cannot build a field ref whose
    // instance type is a value type, so the shim fails to bind, and a property or method delegate
    // takes the struct by value, so a write through it lands on a copy and is lost.
    internal static bool NeedsStructInstanceReceiver(ISymbol member)
    {
        return !member.IsStatic
            && member.ContainingType != null
            && member.ContainingType.IsValueType;
    }

    /// <summary>
    /// What: the symbol a call, method group, or member access binds to. When binding failed only
    /// because the same method is offered twice, returns the copy declared in the edited sources.
    /// Otherwise the bound symbol, or null when nothing is bound.
    /// </summary>
    internal static ISymbol BoundSymbolOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        SymbolInfo info = semanticModel.GetSymbolInfo(node);
        if (info.Symbol != null)
        {
            return info.Symbol;
        }

        // Why: the compiled assembly is referenced beside the edited source of the same file, and
        // extension lookup gathers methods from both copies of a static class. A public method of
        // an internal class is imported from metadata and is eligible because the worker binds
        // with accessibility ignored, so a call to it fails as ambiguous. Treating that as "no
        // symbol" made every access check pass, and the shim called the method and threw.
        if (info.CandidateReason != CandidateReason.OverloadResolutionFailure
            || info.CandidateSymbols.Length < 2)
        {
            return null;
        }

        IMethodSymbol first = info.CandidateSymbols[0] as IMethodSymbol;
        IMethodSymbol sourceCopy = null;
        foreach (ISymbol candidate in info.CandidateSymbols)
        {
            if (candidate is not IMethodSymbol method
                || first == null
                || !IsSameMethodDeclaration(UnreducedMethod(first), UnreducedMethod(method)))
            {
                return null;
            }

            if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, semanticModel.Compilation.Assembly))
            {
                sourceCopy = method;
            }
        }

        return sourceCopy;
    }

    // Whether two method symbols from different assemblies declare the same method: same name,
    // same containing type, same parameter types and ref kinds, same generic arity.
    private static bool IsSameMethodDeclaration(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Name != right.Name
            || left.Arity != right.Arity
            || left.Parameters.Length != right.Parameters.Length
            || FullyQualified(left.ContainingType) != FullyQualified(right.ContainingType))
        {
            return false;
        }

        for (int index = 0; index < left.Parameters.Length; index++)
        {
            IParameterSymbol leftParameter = left.Parameters[index];
            IParameterSymbol rightParameter = right.Parameters[index];
            if (leftParameter.RefKind != rightParameter.RefKind
                || FullyQualified(leftParameter.Type) != FullyQualified(rightParameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static string FullyQualified(ISymbol symbol)
    {
        return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

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
