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
    /// What: the symbol a call, method group, member access, object creation, element access, or
    /// assignment target binds to, or null when nothing is bound. When binding failed because a
    /// static class is offered twice (the edited source and its compiled copy) and such a compiled
    /// class is not externally visible, returns one of its methods from that compiled class, the one
    /// the running code would call. That method shares the containing type of the overload the
    /// compiler would pick but is not necessarily that overload, so it is only good for deciding
    /// refusal. Otherwise, when the node is still unbound, returns what binding with accessibility
    /// enforced gives.
    /// </summary>
    /// <remarks>
    /// Why the accessibility-enforcing binding is asked last: the worker binds with accessibility
    /// ignored so that private and internal members resolve, which the enforcing binding cannot do.
    /// But ignoring accessibility also exposes internal types of referenced assemblies, and a DLL
    /// that carries an internal copy of a BCL type (Harmony carries ReadOnlySpan, Span, Memory and
    /// more) makes that type name ambiguous in a body. An argument of that type then has an error
    /// type, the call binds to nothing, and every accessor check passes it by. The enforcing binding
    /// does not see the internal copy, so it resolves the name to the BCL type and binds the call.
    /// </remarks>
    internal static ISymbol BoundSymbolOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        SymbolInfo info = semanticModel.GetSymbolInfo(node);
        if (info.Symbol != null)
        {
            return info.Symbol;
        }

        // Why: the compiled assembly is referenced beside the edited source of the same file, and
        // extension lookup gathers methods from both copies of a static class, every overload of
        // each. A public method of an internal class is imported from metadata and is eligible
        // because the worker binds with accessibility ignored, so a call to it fails as ambiguous.
        // Treating that as "no symbol" made every access check pass, and the shim called the
        // method and threw.
        if (info.CandidateReason == CandidateReason.OverloadResolutionFailure)
        {
            IMethodSymbol twin = FindTwinCandidateToRefuseOrNull(
                semanticModel.Compilation.Assembly,
                info.CandidateSymbols);
            if (twin != null)
            {
                return FindTwinOfTheEditedOverloadOrNull(semanticModel, node, twin, info.CandidateSymbols) ?? twin;
            }
        }

        return EnforcingModel(semanticModel).GetSymbolInfo(node).Symbol;
    }

    // What: the candidate of the twin's compiled class that has the signature of the overload the
    // edited source calls, or null when the enforcing binding resolves no method or no candidate
    // matches. Why not any candidate of that class: the twin is picked to refuse the call, and any
    // overload refuses it, but an accessor that reaches the class by name binds the overload it is
    // given, which must be the one the source calls (Pick(int) over Pick(long) for an int argument).
    private static IMethodSymbol FindTwinOfTheEditedOverloadOrNull(
        SemanticModel semanticModel,
        SyntaxNode node,
        IMethodSymbol twin,
        ImmutableArray<ISymbol> candidates)
    {
        if (EnforcingModel(semanticModel).GetSymbolInfo(node).Symbol is not IMethodSymbol edited)
        {
            return null;
        }

        IMethodSymbol editedMethod = UnreducedMethod(edited);
        IAssemblySymbol twinAssembly = twin.ContainingAssembly;
        foreach (ISymbol candidate in candidates)
        {
            if (candidate is IMethodSymbol method
                && SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, twinAssembly)
                && HaveTheSameSignature(UnreducedMethod(method), editedMethod))
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>
    /// What: whether two unreduced methods, which may come from different copies of an assembly,
    /// have the same class, name, arity, static-ness and parameter types.
    /// </summary>
    internal static bool HaveTheSameSignature(IMethodSymbol first, IMethodSymbol second)
    {
        if (first.ContainingType == null
            || second.ContainingType == null
            || first.Name != second.Name
            || first.Arity != second.Arity
            || first.IsStatic != second.IsStatic
            || first.Parameters.Length != second.Parameters.Length
            || ConstDriftCollector.ToReflectionMetadataName(first.ContainingType.OriginalDefinition)
                != ConstDriftCollector.ToReflectionMetadataName(second.ContainingType.OriginalDefinition))
        {
            return false;
        }

        return first.Parameters
            .Select(CecilTypeNames.ToParameterTypeFullName)
            .SequenceEqual(second.Parameters.Select(CecilTypeNames.ToParameterTypeFullName));
    }

    /// <summary>
    /// What: the symbol the node binds to, binding again with accessibility enforced when the
    /// accessibility-ignoring binding leaves it unbound (see BoundSymbolOrNull for why that
    /// happens). Unlike BoundSymbolOrNull it never stands a compiled twin in for the call, so the
    /// result is the member the edited source names, which is what deciding a rewrite needs.
    /// </summary>
    internal static ISymbol ResolvedSymbolOrNull(SemanticModel semanticModel, SyntaxNode node)
    {
        return semanticModel.GetSymbolInfo(node).Symbol
            ?? EnforcingModel(semanticModel).GetSymbolInfo(node).Symbol;
    }

    /// <summary>
    /// What: the semantic model of the same tree that binds with accessibility enforced. Asked only
    /// for a node the accessibility-ignoring binding left unbound; see BoundSymbolOrNull for why.
    /// </summary>
    internal static SemanticModel EnforcingModel(SemanticModel semanticModel)
    {
        return semanticModel.Compilation.GetSemanticModel(semanticModel.SyntaxTree, ignoreAccessibility: false);
    }

    // Why per containing type: the candidates are the whole method group, so they include every
    // overload and every same-named extension of every static class in scope, not only the ones
    // that tied. A class with a candidate from the edited source and one from another assembly is
    // a twin class; only twins can make binding fail without a real ambiguity, which Unity's
    // compiler would reject too, so other classes are ignored. Only public members are imported
    // from metadata, so a twin call is inaccessible exactly when the compiled class is not
    // externally visible; when any twin class is hidden, the call is refused (fail closed).
    // Why the compiled copy's class: the running code calls the compiled class, and one edit may
    // make the edited class public while the compiled one stays internal.
    private static IMethodSymbol FindTwinCandidateToRefuseOrNull(
        IAssemblySymbol editedAssembly,
        ImmutableArray<ISymbol> candidates)
    {
        HashSet<string> editedTypes = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, IMethodSymbol> hiddenCompiledCandidates =
            new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
        foreach (ISymbol candidate in candidates)
        {
            // A method with no containing type (a function pointer signature) has no twin.
            if (candidate is not IMethodSymbol method
                || UnreducedMethod(method).ContainingType is not INamedTypeSymbol candidateType)
            {
                continue;
            }

            string typeName = candidateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, editedAssembly))
            {
                editedTypes.Add(typeName);
            }
            else if (!IsExternallyVisibleType(candidateType))
            {
                hiddenCompiledCandidates[typeName] = method;
            }
        }

        foreach (KeyValuePair<string, IMethodSymbol> hidden in hiddenCompiledCandidates)
        {
            if (editedTypes.Contains(hidden.Key))
            {
                return hidden.Value;
            }
        }

        return null;
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
