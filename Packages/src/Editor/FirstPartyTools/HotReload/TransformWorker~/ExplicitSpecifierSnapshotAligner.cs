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
    // One explicit implementation: the member it declares without its interface, and the specifier.
    private sealed class ExplicitMember
    {
        internal ExplicitMember(string shapeKey, NameSyntax specifierName)
        {
            ShapeKey = shapeKey;
            SpecifierName = specifierName;
            SpecifierText = specifierName.NormalizeWhitespace().ToString();
        }

        internal string ShapeKey { get; }

        internal NameSyntax SpecifierName { get; }

        internal string SpecifierText { get; }
    }

    /// <summary>
    /// What: the snapshot root, with each explicit interface specifier the current source spells
    /// another way replaced by the current spelling. A specifier is replaced only when exactly one
    /// current implementation of the same member is spelled differently and both spellings bind to
    /// the same interface, each in its own source. Any other specifier is kept, so the member is
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
        // directive the edit changed can make the old text name another interface there.
        // Why accessibility enforced: a referenced assembly carries internal copies of BCL types,
        // which leave a type argument such as Memory<int> unbound when accessibility is ignored.
        SemanticModel snapshotModel = currentModel.Compilation
            .ReplaceSyntaxTree(currentModel.SyntaxTree, snapshotRoot.SyntaxTree)
            .GetSemanticModel(snapshotRoot.SyntaxTree, ignoreAccessibility: false);
        SemanticModel enforcingCurrentModel = AccessibilityRules.EnforcingModel(currentModel);
        Dictionary<NameSyntax, NameSyntax> replacements = new Dictionary<NameSyntax, NameSyntax>();
        foreach (ExplicitMember snapshotMember in respelledSnapshot)
        {
            ExplicitMember counterpart = FindSameInterfaceCounterpartOrNull(
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

    // What: the one current implementation of the snapshot member's member whose specifier binds to
    // the same interface, or null when none or several do, or the snapshot specifier binds to none.
    private static ExplicitMember FindSameInterfaceCounterpartOrNull(
        ExplicitMember snapshotMember,
        SemanticModel snapshotModel,
        List<ExplicitMember> currentMembers,
        SemanticModel currentModel)
    {
        string snapshotInterface = InterfaceNameOrNull(snapshotModel, snapshotMember.SpecifierName);
        if (snapshotInterface == null)
        {
            return null;
        }

        List<ExplicitMember> counterparts = currentMembers
            .Where(current => current.ShapeKey == snapshotMember.ShapeKey
                && InterfaceNameOrNull(currentModel, current.SpecifierName) == snapshotInterface)
            .ToList();
        return counterparts.Count == 1 ? counterparts[0] : null;
    }

    // Why the fully qualified display: the two spellings bind in two compilations, whose symbols
    // for a type of the edited assembly are never equal, while the qualified name of one
    // interface is the same in both.
    private static string InterfaceNameOrNull(SemanticModel semanticModel, NameSyntax specifierName)
    {
        if (semanticModel.GetSymbolInfo(specifierName).Symbol is not INamedTypeSymbol interfaceType
            || interfaceType.TypeKind != TypeKind.Interface)
        {
            return null;
        }

        return interfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
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
                    "method|" + WorkerSyntaxIndex.BuildSyntaxMethodKey(
                        typeMetadataName,
                        method.WithExplicitInterfaceSpecifier(null)),
                    method.ExplicitInterfaceSpecifier.Name);
            case PropertyDeclarationSyntax property when property.ExplicitInterfaceSpecifier != null:
                return new ExplicitMember(
                    "property|" + WorkerSyntaxIndex.BuildSyntaxPropertyKey(
                        typeMetadataName,
                        property.WithExplicitInterfaceSpecifier(null)),
                    property.ExplicitInterfaceSpecifier.Name);
            case IndexerDeclarationSyntax indexer when indexer.ExplicitInterfaceSpecifier != null:
                return new ExplicitMember(
                    "indexer|" + WorkerSyntaxIndex.BuildSyntaxIndexerKey(typeMetadataName, indexer),
                    indexer.ExplicitInterfaceSpecifier.Name);
            case EventDeclarationSyntax eventDeclaration when eventDeclaration.ExplicitInterfaceSpecifier != null:
                return new ExplicitMember(
                    "event|" + WorkerSyntaxIndex.BuildSyntaxEventKey(
                        typeMetadataName,
                        eventDeclaration.WithExplicitInterfaceSpecifier(null)),
                    eventDeclaration.ExplicitInterfaceSpecifier.Name);
            default:
                return null;
        }
    }
}
