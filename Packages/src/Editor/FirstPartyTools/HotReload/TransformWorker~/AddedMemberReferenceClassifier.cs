using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// What a body names that a hot reload added to a type that already exists.
internal enum AddedMemberUse
{
    None,
    MethodsFieldsOrProperties,
    Event
}

// Tells whether a body names a member that the sources of this run add to a type that already
// exists - compiled, or served by an artifact an earlier reload retained - and that the existing
// type does not hold. The name binds in the planning compilation, which sees the adding source,
// but not in an artifact, which compiles against the existing type alone.
internal sealed class AddedMemberReferenceClassifier
{
    private readonly CSharpCompilation compilation;
    private readonly WorkerTypeHome home;
    private readonly IntroducedTypeArtifactMap artifactMap;
    private readonly string targetAssemblyMvid;

    internal AddedMemberReferenceClassifier(
        CSharpCompilation compilation,
        WorkerTypeHome home,
        IntroducedTypeArtifactMap artifactMap,
        string targetAssemblyMvid)
    {
        this.compilation = compilation;
        this.home = home;
        this.artifactMap = artifactMap;
        this.targetAssemblyMvid = targetAssemblyMvid;
    }

    /// <summary>
    /// Event when any name in the bodies is an added event the store cannot keep, otherwise
    /// whether any names an added method, field, property, or store-kept event. Such an event
    /// wins because a body that uses one is never transformed, so it has to keep failing the way
    /// it fails without a stub.
    /// </summary>
    internal AddedMemberUse Classify(IReadOnlyList<SyntaxNode> bodyNodes, SemanticModel semanticModel)
    {
        bool namesAddedMember = false;
        foreach (SyntaxNode bodyNode in bodyNodes)
        {
            foreach (SimpleNameSyntax name in bodyNode.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
            {
                AddedMemberUse use = ClassifyName(semanticModel.GetSymbolInfo(name));
                if (use == AddedMemberUse.Event)
                {
                    return AddedMemberUse.Event;
                }

                if (use == AddedMemberUse.MethodsFieldsOrProperties)
                {
                    namesAddedMember = true;
                }
            }
        }

        return namesAddedMember ? AddedMemberUse.MethodsFieldsOrProperties : AddedMemberUse.None;
    }

    // Overload resolution that fails still reports its candidates, and a body naming an added
    // overload whose arguments do not fit yet should read the same as one that binds.
    private AddedMemberUse ClassifyName(SymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol != null)
        {
            return ClassifySymbol(symbolInfo.Symbol);
        }

        AddedMemberUse strongest = AddedMemberUse.None;
        foreach (ISymbol candidate in symbolInfo.CandidateSymbols)
        {
            AddedMemberUse use = ClassifySymbol(candidate);
            if (use == AddedMemberUse.Event)
            {
                return AddedMemberUse.Event;
            }

            if (use == AddedMemberUse.MethodsFieldsOrProperties)
            {
                strongest = use;
            }
        }

        return strongest;
    }

    private AddedMemberUse ClassifySymbol(ISymbol symbol)
    {
        ISymbol definition = ToDefinition(symbol);
        INamedTypeSymbol containingType = definition.ContainingType;
        // Why an enum is left out: hot reload never adds an enum member, so a body naming one has
        // to keep failing with the hint that says so rather than being stubbed and patched.
        if (containingType == null
            || containingType.TypeKind == TypeKind.Enum
            || !SymbolEqualityComparer.Default.Equals(containingType.ContainingAssembly, compilation.Assembly))
        {
            return AddedMemberUse.None;
        }

        INamedTypeSymbol existingType = PlannedAddedMemberNames.FindExistingType(
            containingType,
            home,
            compilation,
            artifactMap,
            home.AssemblyName,
            targetAssemblyMvid);
        if (existingType == null)
        {
            return AddedMemberUse.None;
        }

        return ClassifyAgainstExistingType(definition, existingType);
    }

    private static AddedMemberUse ClassifyAgainstExistingType(ISymbol definition, INamedTypeSymbol existingType)
    {
        switch (definition)
        {
            case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                return CompiledMemberMatcher.MatchCompiledOrdinaryMethod(existingType, method) == CompiledMethodMatch.Matched
                    ? AddedMemberUse.None
                    : AddedMemberUse.MethodsFieldsOrProperties;
            case IFieldSymbol field when !field.IsImplicitlyDeclared:
                return CompiledMemberMatcher.MatchCompiledField(existingType, field) == CompiledFieldMatch.Matched
                    ? AddedMemberUse.None
                    : AddedMemberUse.MethodsFieldsOrProperties;
            case IPropertySymbol property when !property.IsIndexer:
                return HoldsMemberOfKind(existingType, property.Name, SymbolKind.Property)
                    ? AddedMemberUse.None
                    : AddedMemberUse.MethodsFieldsOrProperties;
            case IEventSymbol addedEvent:
                if (HoldsMemberOfKind(existingType, addedEvent.Name, SymbolKind.Event))
                {
                    return AddedMemberUse.None;
                }

                // An event the store keeps is patched like an added field, so the body is
                // stubbed as one; any other added event keeps failing the way it does unstubbed.
                return AddedEventStorePolicy.IsStoreBacked(addedEvent, existingType)
                    ? AddedMemberUse.MethodsFieldsOrProperties
                    : AddedMemberUse.Event;
            default:
                return AddedMemberUse.None;
        }
    }

    // A reduced extension method names the static method it was declared as, and a member of a
    // constructed generic type names the member of the generic definition; the existing type is
    // looked up and matched in those terms.
    private static ISymbol ToDefinition(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            IMethodSymbol declared = method.ReducedFrom ?? method;
            return declared.OriginalDefinition;
        }

        return symbol.OriginalDefinition;
    }

    private static bool HoldsMemberOfKind(INamedTypeSymbol existingType, string name, SymbolKind kind)
    {
        foreach (ISymbol member in existingType.GetMembers(name))
        {
            if (member.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }
}
