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
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

/// <summary>
/// What: when an accessor finds the class of an extension method by its name, because the class is
/// not public, and what that name is. The class must be compiled into the patch target, and the call
/// must bind to the overload the edited source calls.
/// </summary>
internal static class ExtensionClassNameBind
{
    /// <summary>
    /// What: whether the accessor for <paramref name="method"/>, an unreduced method, finds its class
    /// by name: the method is an extension method whose class is not public in the edited source or
    /// in <paramref name="targetAssembly"/> (read with every member visible, or null when the run
    /// could not read it).
    /// </summary>
    /// <remarks>
    /// Why only an extension method: the language makes its class top-level, non-generic and static,
    /// so the metadata name alone finds the class, and the delegate type never names it, because the
    /// class is not a parameter. Another non-public class may be generic or nested in one, which a
    /// name lookup cannot construct, so a method of such a class stays refused. Why the compiled class
    /// too: one edit can make the class public while the class that runs stays internal, and the bind
    /// finds the class by name whenever the class that runs is not public.
    /// </remarks>
    internal static bool FindsClassByName(IMethodSymbol method, IAssemblySymbol targetAssembly)
    {
        if (!method.IsExtensionMethod)
        {
            return false;
        }

        if (!AccessibilityRules.IsExternallyVisibleType(method.ContainingType))
        {
            return true;
        }

        INamedTypeSymbol compiledClass = CompiledClassOrNull(method, targetAssembly);
        return compiledClass != null && !AccessibilityRules.IsExternallyVisibleType(compiledClass);
    }

    /// <summary>
    /// What: whether <paramref name="targetAssembly"/> holds the class of <paramref name="method"/>,
    /// and holds the method with the same return type or not at all.
    /// </summary>
    /// <remarks>
    /// Why the class must already be compiled: the bind looks it up by name in that assembly when the
    /// shim loads, and a class this reload adds is not there, so the bind would throw and fail the
    /// whole file instead of skipping the one method. Why a method this reload adds passes: only its
    /// static-form call gets this far, the reduced form being refused before, and the added-method
    /// rewrite replaces that call, so the shim binds no accessor for it. Why a changed return type
    /// fails: the bind would find the compiled method by its parameters and could not make a delegate
    /// of the edited return type.
    /// </remarks>
    internal static bool IsCompiledIn(IMethodSymbol method, IAssemblySymbol targetAssembly)
    {
        INamedTypeSymbol compiledClass = CompiledClassOrNull(method, targetAssembly);
        return compiledClass != null
            && CompiledMemberMatcher.MatchCompiledOrdinaryMethod(compiledClass, method)
                != CompiledMethodMatch.ReturnTypeChanged;
    }

    /// <summary>
    /// What: the reason an accessor may not bind <paramref name="methodSymbol"/>, the unreduced
    /// method the worker bound <paramref name="invocation"/> to, or null when it may. Only a method of
    /// a class that is not public is checked; the reason is the one the class would get without the
    /// lookup by name, or the [Conditional] one.
    /// </summary>
    /// <remarks>
    /// Why at the call: when the bound method's class is not public, the worker's binding may have
    /// stood a compiled twin in for the call, and an accessor that finds the class by name binds the
    /// method it is given, so it must be the one the edited source calls. Why read [Conditional] from
    /// the edited overload: a twin carries the attributes of the last compile, and an edit that makes
    /// the method conditional means the compiler omits the call.
    /// </remarks>
    internal static WorkerReason FindRejectReasonOrNull(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.IsExtensionMethod || AccessibilityRules.IsExternallyVisibleType(methodSymbol.ContainingType))
        {
            return null;
        }

        IMethodSymbol edited = EditedOverloadOrNull(semanticModel, invocation, methodSymbol);
        if (edited == null)
        {
            return WorkerReason.Of(
                HotReloadWorkerReasonCode.AccessorSignatureTypeNotVisible,
                methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return BaseCallRules.IsConditional(edited)
            ? WorkerReason.Of(HotReloadWorkerReasonCode.AccessorConditionalMethodNotRewritten)
            : null;
    }

    /// <summary>
    /// What: the unreduced method the edited source calls at <paramref name="invocation"/>, when
    /// <paramref name="method"/>, the unreduced method the worker bound it to, has its class, name,
    /// arity, parameter types and return type; null otherwise.
    /// </summary>
    /// <remarks>
    /// Why ask the binding that enforces accessibility: the edited file's class and its compiled copy
    /// both offer a public method of a non-public class, so the worker's binding can come out
    /// ambiguous and stand in a compiled candidate that was picked to refuse the call, not to make
    /// it. The enforcing binding cannot see the compiled copy of a non-public class, so it resolves
    /// to what the edited source calls, or to nothing for a class only the compiled assembly holds.
    /// Why the return type too: a compiled candidate stands in for an edited method whose return
    /// type this edit changed, and a delegate of the old type would not compile against the caller.
    /// </remarks>
    private static IMethodSymbol EditedOverloadOrNull(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (AccessibilityRules.EnforcingModel(semanticModel).GetSymbolInfo(invocation).Symbol
            is not IMethodSymbol edited)
        {
            return null;
        }

        IMethodSymbol editedMethod = AccessibilityRules.UnreducedMethod(edited);
        return AccessibilityRules.HaveTheSameSignature(editedMethod, method)
            && CompiledMemberMatcher.ReturnTypesMatch(method, editedMethod)
                ? editedMethod
                : null;
    }

    /// <summary>
    /// What: the assembly-qualified name the bind looks the class of <paramref name="method"/> up by,
    /// in <paramref name="targetAssembly"/>.
    /// </summary>
    internal static string AssemblyQualifiedClassName(IMethodSymbol method, IAssemblySymbol targetAssembly)
    {
        if (targetAssembly == null)
        {
            throw new InvalidOperationException(
                "An accessor for an extension method of a non-public class needs the target assembly.");
        }

        return ConstDriftCollector.ToReflectionMetadataName(method.ContainingType) + ", " + targetAssembly.Identity.Name;
    }

    // Why only a class of the patch target: a class of another assembly can share its metadata name
    // with an unrelated class of the target, and the accessor would bind that one. A class from the
    // edited source belongs to the target, as does one the worker reads from the target's metadata.
    private static INamedTypeSymbol CompiledClassOrNull(IMethodSymbol method, IAssemblySymbol targetAssembly)
    {
        INamedTypeSymbol containingType = method.ContainingType;
        if (targetAssembly == null
            || (containingType.DeclaringSyntaxReferences.IsEmpty
                && containingType.ContainingAssembly?.Identity.Name != targetAssembly.Identity.Name))
        {
            return null;
        }

        return targetAssembly.GetTypeByMetadataName(ConstDriftCollector.ToReflectionMetadataName(containingType));
    }
}
