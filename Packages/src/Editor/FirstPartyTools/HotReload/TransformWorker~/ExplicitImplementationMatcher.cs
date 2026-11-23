using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
/// Why one object for the run and not the symbols alone: the compilation binds a declaration's
/// interfaces with accessibility enforced, so an explicit implementation of an internal interface
/// of the compiled assembly reports no implemented member. The run resolves those once through the
/// accessibility-ignoring binding (see <see cref="ExplicitInterfaceMemberResolver"/>), and every
/// question about an explicit implementation is asked here so that each sees the same answer.
/// </remarks>
internal sealed class ExplicitImplementationMatcher
{
    private readonly IReadOnlyDictionary<ISymbol, ISymbol> _resolvedImplementedMembers;

    /// <param name="resolvedImplementedMembers">
    /// The interface member of each source explicit implementation the compilation left
    /// implementing nothing, accessors included; empty when the run resolved none.
    /// </param>
    internal ExplicitImplementationMatcher(IReadOnlyDictionary<ISymbol, ISymbol> resolvedImplementedMembers)
    {
        _resolvedImplementedMembers = resolvedImplementedMembers;
    }

    /// <summary>
    /// The interface member <paramref name="sourceMember"/> explicitly implements; null when it is
    /// not an explicit implementation or its interface member could not be resolved.
    /// </summary>
    internal ISymbol ImplementedMemberOrNull(ISymbol sourceMember)
    {
        ISymbol declared = DeclaredImplementedMemberOrNull(sourceMember);
        if (declared != null)
        {
            return declared;
        }

        return sourceMember != null && _resolvedImplementedMembers.TryGetValue(sourceMember, out ISymbol resolved)
            ? resolved
            : null;
    }

    /// <summary>
    /// Whether <paramref name="sourceMember"/>, or the property or event an accessor belongs to, is
    /// declared as an explicit interface implementation, whether or not its interface member resolves.
    /// </summary>
    /// <remarks>
    /// Why the declaration too: an explicit implementation whose member cannot be resolved is still
    /// no ordinary member, and reading it as one names it after its dotted declaration.
    /// </remarks>
    internal bool IsExplicitImplementation(ISymbol sourceMember)
    {
        if (ImplementedMemberOrNull(sourceMember) != null)
        {
            return true;
        }

        ISymbol declared = sourceMember is IMethodSymbol { AssociatedSymbol: { } associated } ? associated : sourceMember;
        return declared != null && declared.DeclaringSyntaxReferences.Any(reference => HasSpecifier(reference.GetSyntax()));
    }

    private static bool HasSpecifier(SyntaxNode declaration)
    {
        switch (declaration)
        {
            case MethodDeclarationSyntax method:
                return method.ExplicitInterfaceSpecifier != null;
            // Why one case: an event declaration is a BasePropertyDeclarationSyntax too.
            case BasePropertyDeclarationSyntax property:
                return property.ExplicitInterfaceSpecifier != null;
            default:
                return false;
        }
    }

    /// <summary>
    /// The compiled method that explicitly implements the same interface member with the same
    /// parameters and return type as <paramref name="sourceMethod"/>; null when the compiled type
    /// has none, that is when the implementation was added since the last compile.
    /// </summary>
    internal IMethodSymbol FindCompiledImplementation(INamedTypeSymbol compiledType, IMethodSymbol sourceMethod)
    {
        if (compiledType == null || !(ImplementedMemberOrNull(sourceMethod) is IMethodSymbol sourceMember))
        {
            return null;
        }

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
    internal string RowMethodName(INamedTypeSymbol compiledType, IMethodSymbol sourceMethod)
    {
        // Why not MethodKind: an accessor of an explicit property is PropertyGet or PropertySet,
        // yet carries the compiled metadata name all the same.
        if (!IsExplicitImplementation(sourceMethod))
        {
            return sourceMethod.Name;
        }

        IMethodSymbol compiledMethod = FindCompiledImplementation(compiledType, sourceMethod);
        return compiledMethod == null ? sourceMethod.Name : compiledMethod.MetadataName;
    }

    /// <summary>
    /// The compiled property that explicitly implements the same interface property, with the same
    /// type, as <paramref name="sourceProperty"/>; null when the compiled type has none.
    /// </summary>
    internal IPropertySymbol FindCompiledPropertyImplementation(INamedTypeSymbol compiledType, IPropertySymbol sourceProperty)
    {
        if (compiledType == null || !(ImplementedMemberOrNull(sourceProperty) is IPropertySymbol sourceMember))
        {
            return null;
        }

        string sourceInterface = sourceMember.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string sourceType = CecilTypeNames.ToCecilFullName(sourceProperty.Type);
        foreach (IPropertySymbol compiledProperty in compiledType.GetMembers().OfType<IPropertySymbol>())
        {
            bool implementsSameMember = compiledProperty.ExplicitInterfaceImplementations.Any(implemented =>
                implemented.Name == sourceMember.Name
                && implemented.IsIndexer == sourceMember.IsIndexer
                && implemented.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == sourceInterface);
            if (implementsSameMember && CecilTypeNames.ToCecilFullName(compiledProperty.Type) == sourceType)
            {
                return compiledProperty;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="sourceType"/> explicitly implements the interface property or event
    /// that <paramref name="compiledMember"/> explicitly implements.
    /// </summary>
    /// <remarks>
    /// Why: a source implementation respelled with <c>global::</c> is named after the new spelling,
    /// so looking it up by the compiled name finds nothing.
    /// </remarks>
    internal bool SourceImplementsSameInterfaceMember(INamedTypeSymbol sourceType, ISymbol compiledMember)
    {
        ISymbol compiledImplemented = DeclaredPropertyOrEventOrNull(compiledMember);
        if (compiledImplemented == null)
        {
            return false;
        }

        string compiledInterface = compiledImplemented.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (ISymbol sourceMember in sourceType.GetMembers())
        {
            ISymbol implemented = ImplementedPropertyOrEventOrNull(sourceMember);
            if (implemented != null
                && implemented.Kind == compiledImplemented.Kind
                && implemented.Name == compiledImplemented.Name
                && implemented.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == compiledInterface)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The name the compiler gives <paramref name="sourceMethod"/> when its interface member is
    /// accessible to the declaration: an accessor of an explicit property, indexer or event this run
    /// resolved is named after the interface accessor (Ns.IFoo.get_Item), not after its property
    /// (get_Ns.IFoo.Item).
    /// </summary>
    internal string SourceMethodName(IMethodSymbol sourceMethod)
    {
        if (DeclaredImplementedMemberOrNull(sourceMethod) != null
            || sourceMethod.AssociatedSymbol == null
            || !(ImplementedMemberOrNull(sourceMethod) is IMethodSymbol implemented))
        {
            return sourceMethod.Name;
        }

        string associatedName = sourceMethod.AssociatedSymbol.Name;
        int lastDot = associatedName.LastIndexOf('.');
        return lastDot < 0 ? sourceMethod.Name : associatedName.Substring(0, lastDot + 1) + implemented.Name;
    }

    /// <summary>The name of the interface member a source explicit implementation implements.</summary>
    internal string ImplementedMemberName(IMethodSymbol sourceMethod)
    {
        // Why the last segment as a fallback: an unresolved one is named after its dotted declaration.
        ISymbol implemented = ImplementedMemberOrNull(sourceMethod);
        return implemented != null ? implemented.Name : sourceMethod.Name.Substring(sourceMethod.Name.LastIndexOf('.') + 1);
    }

    // Why only properties and events: a compiled method is matched through FindCompiledImplementation.
    private static ISymbol DeclaredPropertyOrEventOrNull(ISymbol member)
    {
        ISymbol implemented = DeclaredImplementedMemberOrNull(member);
        return implemented is IPropertySymbol || implemented is IEventSymbol ? implemented : null;
    }

    private ISymbol ImplementedPropertyOrEventOrNull(ISymbol member)
    {
        ISymbol implemented = ImplementedMemberOrNull(member);
        return implemented is IPropertySymbol || implemented is IEventSymbol ? implemented : null;
    }

    // What the compiler recorded for the member: a metadata member always has it, and a source
    // member has it when its interface was accessible to the declaration.
    private static ISymbol DeclaredImplementedMemberOrNull(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol method when method.ExplicitInterfaceImplementations.Length > 0:
                return method.ExplicitInterfaceImplementations[0];
            case IPropertySymbol property when property.ExplicitInterfaceImplementations.Length > 0:
                return property.ExplicitInterfaceImplementations[0];
            case IEventSymbol eventSymbol when eventSymbol.ExplicitInterfaceImplementations.Length > 0:
                return eventSymbol.ExplicitInterfaceImplementations[0];
            default:
                return null;
        }
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
