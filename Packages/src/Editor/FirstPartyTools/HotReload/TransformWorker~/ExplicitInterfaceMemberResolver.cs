using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// What: the interface member of each explicit implementation in the given trees that the
// compilation left implementing nothing, resolved by binding its specifier with accessibility
// ignored. That happens when the interface is internal to the compiled assembly and declared in a
// file the run was not given: the worker sees that assembly only through its reference, and the
// compilation binds a declaration's interfaces with accessibility enforced.
// Why not let the compilation ignore accessibility instead: that switches off every binding the
// run's guards enforce accessibility with, and lets a body reach a member the running code cannot.
internal static class ExplicitInterfaceMemberResolver
{
    /// <summary>
    /// Source member (and each of its accessors) to the interface member it implements, for the
    /// explicit implementations whose implemented member the compiler did not record. An
    /// implementation whose specifier binds to no interface, or to an interface with no member or
    /// several members of the same shape, is left out, so it stays an implementation of nothing.
    /// </summary>
    /// <param name="semanticModels">Accessibility-ignoring models of the trees to scan.</param>
    internal static Dictionary<ISymbol, ISymbol> Resolve(IEnumerable<SemanticModel> semanticModels)
    {
        Dictionary<ISymbol, ISymbol> resolved = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);
        foreach (SemanticModel semanticModel in semanticModels)
        {
            foreach (MemberDeclarationSyntax declaration in semanticModel.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberDeclarationSyntax>())
            {
                AddResolved(semanticModel, declaration, resolved);
            }
        }

        return resolved;
    }

    private static void AddResolved(
        SemanticModel semanticModel,
        MemberDeclarationSyntax declaration,
        Dictionary<ISymbol, ISymbol> resolved)
    {
        ExplicitInterfaceSpecifierSyntax specifier = SpecifierOrNull(declaration);
        if (specifier == null)
        {
            return;
        }

        ISymbol sourceMember = semanticModel.GetDeclaredSymbol(declaration);
        if (sourceMember == null || ImplementsAMember(sourceMember))
        {
            return;
        }

        if (!(semanticModel.GetSymbolInfo(specifier.Name).Symbol is INamedTypeSymbol interfaceType)
            || interfaceType.TypeKind != TypeKind.Interface)
        {
            return;
        }

        List<ISymbol> matches = interfaceType.GetMembers()
            .Where(candidate => !candidate.IsStatic && SameShape(candidate, sourceMember))
            .ToList();
        if (matches.Count != 1)
        {
            return;
        }

        resolved[sourceMember] = matches[0];
        AddAccessors(sourceMember, matches[0], resolved);
    }

    private static ExplicitInterfaceSpecifierSyntax SpecifierOrNull(MemberDeclarationSyntax declaration)
    {
        switch (declaration)
        {
            case MethodDeclarationSyntax method:
                return method.ExplicitInterfaceSpecifier;
            // Why one case: an event declaration is a BasePropertyDeclarationSyntax too.
            case BasePropertyDeclarationSyntax property:
                return property.ExplicitInterfaceSpecifier;
            default:
                return null;
        }
    }

    private static bool ImplementsAMember(ISymbol sourceMember)
    {
        switch (sourceMember)
        {
            case IMethodSymbol method:
                return method.ExplicitInterfaceImplementations.Length > 0;
            case IPropertySymbol property:
                return property.ExplicitInterfaceImplementations.Length > 0;
            case IEventSymbol eventSymbol:
                return eventSymbol.ExplicitInterfaceImplementations.Length > 0;
            default:
                return true;
        }
    }

    // Why the implemented member's name and not the source member's: the source name is the
    // dotted explicit name (Ns.IFoo.Run), the interface member is named Run.
    private static bool SameShape(ISymbol candidate, ISymbol sourceMember)
    {
        switch (sourceMember)
        {
            case IMethodSymbol method:
                return candidate is IMethodSymbol interfaceMethod && SameMethodShape(interfaceMethod, method);
            case IPropertySymbol property:
                return candidate is IPropertySymbol interfaceProperty && SamePropertyShape(interfaceProperty, property);
            case IEventSymbol eventSymbol:
                return candidate is IEventSymbol interfaceEvent
                    && interfaceEvent.Name == ExplicitMemberName(eventSymbol.Name)
                    && SameType(interfaceEvent.Type, eventSymbol.Type);
            default:
                return false;
        }
    }

    private static bool SameMethodShape(IMethodSymbol interfaceMethod, IMethodSymbol sourceMethod)
    {
        if (interfaceMethod.MethodKind != MethodKind.Ordinary
            || interfaceMethod.Name != ExplicitMemberName(sourceMethod.Name)
            || interfaceMethod.Arity != sourceMethod.Arity)
        {
            return false;
        }

        // Why construct with the source's type parameters: the interface's own method type
        // parameters are different symbols, so M<T>(T) would never equal the source's M<T>(T).
        IMethodSymbol comparable = interfaceMethod.Arity == 0
            ? interfaceMethod
            : interfaceMethod.Construct(sourceMethod.TypeParameters.Cast<ITypeSymbol>().ToArray());
        return comparable.RefKind == sourceMethod.RefKind
            && SameType(comparable.ReturnType, sourceMethod.ReturnType)
            && SameParameters(comparable.Parameters, sourceMethod.Parameters);
    }

    private static bool SamePropertyShape(IPropertySymbol interfaceProperty, IPropertySymbol sourceProperty)
    {
        bool sameName = sourceProperty.IsIndexer
            ? interfaceProperty.IsIndexer
            : !interfaceProperty.IsIndexer && interfaceProperty.Name == ExplicitMemberName(sourceProperty.Name);
        return sameName
            && interfaceProperty.RefKind == sourceProperty.RefKind
            && SameType(interfaceProperty.Type, sourceProperty.Type)
            && SameParameters(interfaceProperty.Parameters, sourceProperty.Parameters);
    }

    private static bool SameParameters(
        IReadOnlyList<IParameterSymbol> interfaceParameters,
        IReadOnlyList<IParameterSymbol> sourceParameters)
    {
        if (interfaceParameters.Count != sourceParameters.Count)
        {
            return false;
        }

        for (int index = 0; index < sourceParameters.Count; index++)
        {
            if (interfaceParameters[index].RefKind != sourceParameters[index].RefKind
                || !SameType(interfaceParameters[index].Type, sourceParameters[index].Type))
            {
                return false;
            }
        }

        return true;
    }

    // Why by metadata name and not by symbol: a type the reloaded file declares has two copies, the
    // compiled one the interface's signature names and the source one the implementation names.
    private static bool SameType(ITypeSymbol interfaceType, ITypeSymbol sourceType)
    {
        return CecilTypeNames.ToCecilFullName(interfaceType) == CecilTypeNames.ToCecilFullName(sourceType);
    }

    // The member name after the specifier: 'Ns.IFoo.Run' declares 'Run'.
    private static string ExplicitMemberName(string sourceName)
    {
        int lastDot = sourceName.LastIndexOf('.');
        return lastDot < 0 ? sourceName : sourceName.Substring(lastDot + 1);
    }

    private static void AddAccessors(ISymbol sourceMember, ISymbol interfaceMember, Dictionary<ISymbol, ISymbol> resolved)
    {
        switch (sourceMember)
        {
            case IPropertySymbol property when interfaceMember is IPropertySymbol interfaceProperty:
                AddAccessor(property.GetMethod, interfaceProperty.GetMethod, resolved);
                AddAccessor(property.SetMethod, interfaceProperty.SetMethod, resolved);
                break;
            case IEventSymbol eventSymbol when interfaceMember is IEventSymbol interfaceEvent:
                AddAccessor(eventSymbol.AddMethod, interfaceEvent.AddMethod, resolved);
                AddAccessor(eventSymbol.RemoveMethod, interfaceEvent.RemoveMethod, resolved);
                break;
        }
    }

    private static void AddAccessor(IMethodSymbol sourceAccessor, IMethodSymbol interfaceAccessor, Dictionary<ISymbol, ISymbol> resolved)
    {
        if (sourceAccessor != null && interfaceAccessor != null)
        {
            resolved[sourceAccessor] = interfaceAccessor;
        }
    }
}
