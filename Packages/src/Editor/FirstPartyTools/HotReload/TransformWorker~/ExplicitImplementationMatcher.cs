using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>
/// Finds the compiled counterpart of a source explicit interface implementation by the interface
/// member it implements, not by its name.
/// </summary>
/// <remarks>
/// Why not by name: the compiler copies the specifier's spelling into the metadata name, so
/// <c>void global::Ns.IFoo.Run()</c> and <c>void IFoo.Run()</c> are different names for the same
/// implementation. The fully qualified display of the interface is the same for a source symbol
/// and a metadata symbol whatever the spelling (alias, <c>global::</c>, <c>int</c> or
/// <c>System.Int32</c>).
/// </remarks>
internal static class ExplicitImplementationMatcher
{
    /// <summary>
    /// The compiled method that explicitly implements the same interface member with the same
    /// parameters and return type as <paramref name="sourceMethod"/>; null when the compiled type
    /// has none, that is when the implementation was added since the last compile.
    /// </summary>
    internal static IMethodSymbol FindCompiledImplementation(INamedTypeSymbol compiledType, IMethodSymbol sourceMethod)
    {
        if (compiledType == null || sourceMethod.ExplicitInterfaceImplementations.Length == 0)
        {
            return null;
        }

        IMethodSymbol sourceMember = sourceMethod.ExplicitInterfaceImplementations[0];
        foreach (IMethodSymbol compiledMethod in compiledType.GetMembers().OfType<IMethodSymbol>())
        {
            if (ImplementsSameMember(compiledMethod, sourceMember)
                && SignaturesMatch(compiledMethod, sourceMethod))
            {
                return compiledMethod;
            }
        }

        return null;
    }

    /// <summary>
    /// The method name a worker row carries for <paramref name="sourceMethod"/>: the compiled
    /// metadata name for an explicit implementation the compiled type holds, else the source name.
    /// </summary>
    /// <remarks>
    /// Why: the Editor resolves and reverts patches by the compiled name, and a source spelled
    /// <c>global::Ns.IFoo.Run</c> has a different name from the compiled <c>Ns.IFoo.Run</c>.
    /// </remarks>
    internal static string RowMethodName(INamedTypeSymbol compiledType, IMethodSymbol sourceMethod)
    {
        if (sourceMethod.MethodKind != MethodKind.ExplicitInterfaceImplementation)
        {
            return sourceMethod.Name;
        }

        IMethodSymbol compiledMethod = FindCompiledImplementation(compiledType, sourceMethod);
        return compiledMethod == null ? sourceMethod.Name : compiledMethod.MetadataName;
    }

    /// <summary>The name of the interface member a source explicit implementation implements.</summary>
    internal static string ImplementedMemberName(IMethodSymbol sourceMethod)
    {
        return sourceMethod.ExplicitInterfaceImplementations[0].Name;
    }

    private static bool ImplementsSameMember(IMethodSymbol compiledMethod, IMethodSymbol sourceMember)
    {
        string sourceInterface = sourceMember.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        // Why arity too: M(int) and M<T>(int) of one interface share a name and parameter types.
        return compiledMethod.ExplicitInterfaceImplementations.Any(implemented =>
            implemented.Name == sourceMember.Name
            && implemented.Arity == sourceMember.Arity
            && implemented.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == sourceInterface);
    }

    private static bool SignaturesMatch(IMethodSymbol compiledMethod, IMethodSymbol sourceMethod)
    {
        if (compiledMethod.Parameters.Length != sourceMethod.Parameters.Length
            || !CompiledMemberMatcher.ReturnTypesMatch(compiledMethod, sourceMethod))
        {
            return false;
        }

        for (int index = 0; index < sourceMethod.Parameters.Length; index++)
        {
            IParameterSymbol compiledParameter = compiledMethod.Parameters[index];
            IParameterSymbol sourceParameter = sourceMethod.Parameters[index];
            if (compiledParameter.RefKind != sourceParameter.RefKind
                || CecilTypeNames.ToParameterTypeFullName(compiledParameter)
                    != CecilTypeNames.ToParameterTypeFullName(sourceParameter))
            {
                return false;
            }
        }

        return true;
    }
}
