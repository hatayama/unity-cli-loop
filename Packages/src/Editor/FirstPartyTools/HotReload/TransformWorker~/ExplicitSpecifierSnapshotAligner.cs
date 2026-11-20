using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// What: gives the snapshot's explicit interface specifiers the current spelling where only the
// spelling changed ('IFoo.Run' against 'global::Ns.IFoo.Run'), so the syntax keys and the syntax
// comparisons that pair the snapshot with the current source see one member, not a removed one
// and an added one.
internal static class ExplicitSpecifierSnapshotAligner
{
    // One explicit implementation: its declaration, the member it declares without its interface,
    // and the specifier.
    private sealed class ExplicitMember
    {
        internal ExplicitMember(MemberDeclarationSyntax declaration, string shapeKey, NameSyntax specifierName)
        {
            Declaration = declaration;
            ShapeKey = shapeKey;
            SpecifierName = specifierName;
            SpecifierText = specifierName.NormalizeWhitespace().ToString();
        }

        internal MemberDeclarationSyntax Declaration { get; }

        internal string ShapeKey { get; }

        internal NameSyntax SpecifierName { get; }

        internal string SpecifierText { get; }
    }

    /// <summary>
    /// What: the snapshot root, with each explicit interface specifier the current source spells
    /// another way replaced by the current spelling. A specifier is replaced only when exactly one
    /// current implementation of the same member is spelled differently and both implement the same
    /// interface member, each bound in its own source. Any other specifier is kept, so the member is
    /// reported as before.
    /// </summary>
    internal static CompilationUnitSyntax Align(CompilationUnitSyntax snapshotRoot, SemanticModel currentModel)
    {
        Debug.Assert(snapshotRoot != null, "snapshotRoot");
        Debug.Assert(currentModel != null, "currentModel");

        List<ExplicitMember> snapshotMembers = CollectExplicitMembers(snapshotRoot);
        List<ExplicitMember> currentMembers = CollectExplicitMembers(currentModel.SyntaxTree.GetCompilationUnitRoot());
        List<ExplicitMember> respelledSnapshot = WithoutSameSpelling(snapshotMembers, currentMembers);
        List<ExplicitMember> respelledCurrent = WithoutSameSpelling(currentMembers, snapshotMembers);
        if (respelledSnapshot.Count == 0 || respelledCurrent.Count == 0)
        {
            return snapshotRoot;
        }

        // Why bind the snapshot in its own source, not its text at the current position: a using
        // directive the edit changed can make the old text name another interface or type there.
        // Why accessibility enforced: the compilation binds a declaration's interfaces with
        // accessibility enforced, so an implementation whose interface or parameter type only the
        // accessibility-ignoring binding resolves implements nothing for the rest of the reload, and
        // pairing it would disagree with that. It also resolves the BCL types a referenced assembly
        // declares internally (Memory<int>), which the accessibility-ignoring binding leaves unbound.
        SemanticModel snapshotModel = currentModel.Compilation
            .ReplaceSyntaxTree(currentModel.SyntaxTree, snapshotRoot.SyntaxTree)
            .GetSemanticModel(snapshotRoot.SyntaxTree, ignoreAccessibility: false);
        SemanticModel enforcingCurrentModel = AccessibilityRules.EnforcingModel(currentModel);
        Dictionary<NameSyntax, NameSyntax> replacements = new Dictionary<NameSyntax, NameSyntax>();
        foreach (ExplicitMember snapshotMember in respelledSnapshot)
        {
            ExplicitMember counterpart = FindSameMemberCounterpartOrNull(
                snapshotMember,
                snapshotModel,
                respelledCurrent,
                enforcingCurrentModel);
            if (counterpart != null)
            {
                replacements[snapshotMember.SpecifierName] = SyntaxFactory.ParseName(counterpart.SpecifierText)
                    .WithTriviaFrom(snapshotMember.SpecifierName);
            }
        }

        if (replacements.Count == 0)
        {
            return snapshotRoot;
        }

        return snapshotRoot.ReplaceNodes(replacements.Keys, (original, _) => replacements[original]);
    }

    // What: the one current implementation of the snapshot member's member that implements the same
    // interface member, or null when none or several do, or the snapshot member's parts bind to none.
    // Why the parameter types and not the interface alone: a using alias the edit retargeted can
    // move 'IFoo.Take(Arg)' from 'Take(A)' to 'Take(B)' of the same interface.
    private static ExplicitMember FindSameMemberCounterpartOrNull(
        ExplicitMember snapshotMember,
        SemanticModel snapshotModel,
        List<ExplicitMember> currentMembers,
        SemanticModel currentModel)
    {
        string snapshotImplemented = ImplementedMemberOrNull(snapshotModel, snapshotMember);
        if (snapshotImplemented == null)
        {
            return null;
        }

        List<ExplicitMember> counterparts = currentMembers
            .Where(current => current.ShapeKey == snapshotMember.ShapeKey
                && ImplementedMemberOrNull(currentModel, current) == snapshotImplemented)
            .ToList();
        return counterparts.Count == 1 ? counterparts[0] : null;
    }

    // What: what the declaration implements, spelled alike in both sources: the interface and the
    // types of the parameters, each as bound; null when one of them binds to nothing. The member
    // name, arity and ref kinds are already equal, since the shape keys are.
    // Why the bound names and not the declared symbol: both say the same under accessibility
    // enforced, and binding the names needs no symbol for the whole declaration.
    private static string ImplementedMemberOrNull(SemanticModel semanticModel, ExplicitMember member)
    {
        ITypeSymbol interfaceType = BoundTypeOrNull(semanticModel, member.SpecifierName);
        if (interfaceType == null || interfaceType.TypeKind != TypeKind.Interface)
        {
            return null;
        }

        List<string> parts = new List<string> { interfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) };
        foreach (ParameterSyntax parameter in ParametersOf(member.Declaration))
        {
            ITypeSymbol parameterType = parameter.Type == null ? null : BoundTypeOrNull(semanticModel, parameter.Type);
            if (parameterType == null)
            {
                return null;
            }

            parts.Add(parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return string.Join("|", parts);
    }

    // What: the type the syntax names, or null when it binds to none.
    // Why an unbound part rejects the type: it displays as the text written, so two spellings of
    // one could match while naming nothing.
    private static ITypeSymbol BoundTypeOrNull(SemanticModel semanticModel, TypeSyntax syntax)
    {
        ITypeSymbol type = semanticModel.GetTypeInfo(syntax).Type
            ?? semanticModel.GetSymbolInfo(syntax).Symbol as ITypeSymbol;
        return type == null || ContainsErrorType(type) ? null : type;
    }

    private static IEnumerable<ParameterSyntax> ParametersOf(MemberDeclarationSyntax declaration)
    {
        switch (declaration)
        {
            case MethodDeclarationSyntax method:
                return method.ParameterList.Parameters;
            case IndexerDeclarationSyntax indexer:
                return indexer.ParameterList.Parameters;
            default:
                return Enumerable.Empty<ParameterSyntax>();
        }
    }

    private static bool ContainsErrorType(ITypeSymbol type)
    {
        switch (type)
        {
            case null:
                return false;
            case IErrorTypeSymbol:
                return true;
            case IArrayTypeSymbol array:
                return ContainsErrorType(array.ElementType);
            case IPointerTypeSymbol pointer:
                return ContainsErrorType(pointer.PointedAtType);
            case INamedTypeSymbol named:
                return named.TypeArguments.Any(ContainsErrorType) || ContainsErrorType(named.ContainingType);
            default:
                return false;
        }
    }

    // What: the members of 'members' that no member of 'others' declares with the same specifier
    // spelling, which are the only ones whose pairing can have been lost to a respelling.
    private static List<ExplicitMember> WithoutSameSpelling(List<ExplicitMember> members, List<ExplicitMember> others)
    {
        HashSet<string> otherSpellings = new HashSet<string>(
            others.Select(other => other.ShapeKey + "|" + other.SpecifierText));
        return members.Where(member => !otherSpellings.Contains(member.ShapeKey + "|" + member.SpecifierText)).ToList();
    }

    private static List<ExplicitMember> CollectExplicitMembers(CompilationUnitSyntax root)
    {
        List<ExplicitMember> members = new List<ExplicitMember>();
        foreach (TypeDeclarationSyntax typeDeclaration in TransformWorkerProgram.EnumerateTypeDeclarations(root))
        {
            string typeMetadataName = WorkerSyntaxIndex.BuildTypeMetadataNameFromSyntax(typeDeclaration);
            foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
            {
                ExplicitMember explicitMember = ToExplicitMemberOrNull(typeMetadataName, member);
                if (explicitMember != null)
                {
                    members.Add(explicitMember);
                }
            }
        }

        return members;
    }

    // Why the kind prefix: a method, a property and an event can share a name, and a shape key
    // must name one member.
    private static ExplicitMember ToExplicitMemberOrNull(string typeMetadataName, MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method when method.ExplicitInterfaceSpecifier != null:
                return new ExplicitMember(
                    method,
                    "method|" + WorkerSyntaxIndex.BuildSyntaxMethodKey(
                        typeMetadataName,
                        method.WithExplicitInterfaceSpecifier(null)),
                    method.ExplicitInterfaceSpecifier.Name);
            case PropertyDeclarationSyntax property when property.ExplicitInterfaceSpecifier != null:
                return new ExplicitMember(
                    property,
                    "property|" + WorkerSyntaxIndex.BuildSyntaxPropertyKey(
                        typeMetadataName,
                        property.WithExplicitInterfaceSpecifier(null)),
                    property.ExplicitInterfaceSpecifier.Name);
            case IndexerDeclarationSyntax indexer when indexer.ExplicitInterfaceSpecifier != null:
                return new ExplicitMember(
                    indexer,
                    "indexer|" + WorkerSyntaxIndex.BuildSyntaxIndexerKey(typeMetadataName, indexer),
                    indexer.ExplicitInterfaceSpecifier.Name);
            case EventDeclarationSyntax eventDeclaration when eventDeclaration.ExplicitInterfaceSpecifier != null:
                return new ExplicitMember(
                    eventDeclaration,
                    "event|" + WorkerSyntaxIndex.BuildSyntaxEventKey(
                        typeMetadataName,
                        eventDeclaration.WithExplicitInterfaceSpecifier(null)),
                    eventDeclaration.ExplicitInterfaceSpecifier.Name);
            default:
                return null;
        }
    }
}
