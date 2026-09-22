using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Lists the members a unit's sources declare on a type that already exists, compiled or retained
// from an earlier reload, and that the existing type does not hold: the members this reload is
// about to add. The prepare run reports them because the introduced-type compilation fails on
// such a member before the transform run ever records it as added.
internal static class PlannedAddedMemberNames
{
    internal static string[] Collect(
        WorkerSourceUnit unit,
        WorkerTypeHome home,
        CSharpCompilation compilation,
        IntroducedTypeArtifactMap artifactMap,
        string targetAssemblyName,
        string targetAssemblyMvid)
    {
        HashSet<string> names = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (TypeDeclarationSyntax declaration in unit.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            INamedTypeSymbol sourceType = unit.SemanticModel.GetDeclaredSymbol(declaration);
            INamedTypeSymbol existingType = FindExistingType(
                sourceType,
                home,
                compilation,
                artifactMap,
                targetAssemblyName,
                targetAssemblyMvid);
            if (existingType == null)
            {
                continue;
            }

            foreach (ISymbol member in sourceType.GetMembers())
            {
                if (IsNameableMember(member) && existingType.GetMembers(member.Name).IsEmpty)
                {
                    names.Add(member.Name);
                }
            }
        }

        return names.ToArray();
    }

    /// <summary>
    /// Lists the members a unit's sources add to a compiled enum. Hot reload never adds an enum
    /// member, so these stay absent from every compilation until a compile.
    /// </summary>
    internal static string[] CollectCompiledEnumMembers(WorkerSourceUnit unit, WorkerTypeHome home)
    {
        HashSet<string> names = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (EnumDeclarationSyntax declaration in unit.Root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            INamedTypeSymbol sourceType = unit.SemanticModel.GetDeclaredSymbol(declaration);
            INamedTypeSymbol compiledType = sourceType == null ? null : home.FindCompiledType(sourceType);
            if (compiledType == null)
            {
                continue;
            }

            foreach (ISymbol member in sourceType.GetMembers())
            {
                if (member is IFieldSymbol && !member.IsImplicitlyDeclared && compiledType.GetMembers(member.Name).IsEmpty)
                {
                    names.Add(member.Name);
                }
            }
        }

        return names.ToArray();
    }

    // Why only top-level types are looked up in the artifacts: hot reload introduces top-level
    // types only, so a retained artifact never serves a nested one.
    private static INamedTypeSymbol FindExistingType(
        INamedTypeSymbol sourceType,
        WorkerTypeHome home,
        CSharpCompilation compilation,
        IntroducedTypeArtifactMap artifactMap,
        string targetAssemblyName,
        string targetAssemblyMvid)
    {
        if (sourceType == null)
        {
            return null;
        }

        INamedTypeSymbol compiledType = home.FindCompiledType(sourceType);
        if (compiledType != null || sourceType.ContainingType != null)
        {
            return compiledType;
        }

        return artifactMap.FindArtifactType(
            compilation,
            targetAssemblyName,
            targetAssemblyMvid,
            CecilTypeNames.ToMetadataName(sourceType));
    }

    // A member a call site can name. Accessors, constructors and compiler-generated backing
    // fields never appear as the missing member of CS1061 or CS0117.
    private static bool IsNameableMember(ISymbol member)
    {
        if (member.IsImplicitlyDeclared)
        {
            return false;
        }

        if (member is IMethodSymbol method)
        {
            return method.MethodKind == MethodKind.Ordinary;
        }

        return member is IFieldSymbol || member is IPropertySymbol || member is IEventSymbol;
    }
}
