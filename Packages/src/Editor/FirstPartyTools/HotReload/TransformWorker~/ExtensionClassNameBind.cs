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
/// What: when an accessor may find the class of an extension method by its name, because the class
/// is not public and a shim cannot name it, and what that name is. The class must be compiled into
/// the patch target, and the call must bind to the overload the edited source calls.
/// </summary>
internal static class ExtensionClassNameBind
{
    /// <summary>
    /// What: whether an accessor for <paramref name="method"/>, an unreduced method, would have to
    /// find its class by name: the method is an extension method and its class is not public.
    /// </summary>
    /// <remarks>
    /// Why only an extension method: the language makes its class top-level, non-generic and static,
    /// so the metadata name alone finds the class, and the delegate type never names it, because the
    /// class is not a parameter. Another non-public class may be generic or nested in one, which a
    /// name lookup cannot construct, so a method of such a class stays refused.
    /// </remarks>
    internal static bool AppliesTo(IMethodSymbol method)
    {
        return method.IsExtensionMethod
            && !AccessibilityRules.IsExternallyVisibleType(method.ContainingType);
    }

    /// <summary>
    /// What: whether <paramref name="targetAssembly"/> (read with every member visible, or null when
    /// the run could not read it) holds the class of <paramref name="method"/>, and holds the method
    /// with the same return type or not at all.
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
        INamedTypeSymbol compiledClass = targetAssembly?.GetTypeByMetadataName(
            ConstDriftCollector.ToReflectionMetadataName(method.ContainingType));
        return compiledClass != null
            && CompiledMemberMatcher.MatchCompiledOrdinaryMethod(compiledClass, method)
                != CompiledMethodMatch.ReturnTypeChanged;
    }

    /// <summary>
    /// What: whether <paramref name="method"/>, the unreduced method the worker bound
    /// <paramref name="invocation"/> to, is the overload the edited source calls, with the return
    /// type the edited source declares.
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
    internal static bool IsTheEditedOverload(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (AccessibilityRules.EnforcingModel(semanticModel).GetSymbolInfo(invocation).Symbol
            is not IMethodSymbol edited)
        {
            return false;
        }

        IMethodSymbol editedMethod = AccessibilityRules.UnreducedMethod(edited);
        return AccessibilityRules.HaveTheSameSignature(editedMethod, method)
            && CompiledMemberMatcher.ReturnTypesMatch(method, editedMethod);
    }

    /// <summary>
    /// What: the assembly-qualified name the bind looks the class of <paramref name="method"/> up by.
    /// </summary>
    internal static string AssemblyQualifiedClassName(IMethodSymbol method, string targetAssemblyName)
    {
        if (string.IsNullOrEmpty(targetAssemblyName))
        {
            throw new InvalidOperationException(
                "An accessor for an extension method of a non-public class needs the target assembly name.");
        }

        return ConstDriftCollector.ToReflectionMetadataName(method.ContainingType) + ", " + targetAssemblyName;
    }
}
