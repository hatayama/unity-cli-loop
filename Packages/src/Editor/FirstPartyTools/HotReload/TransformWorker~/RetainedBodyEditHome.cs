using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Where the emit stages find the compiled counterpart of a type the patch target does not hold
// because a retained artifact serves it. Without this the type reads as never compiled, and every
// method of it is refused as belonging to a type this edit introduced - which is what a body edit
// of an already introduced type must not be told.
internal static class RetainedBodyEditHome
{
    /// <summary>
    /// Binds the type state to the retained artifact that serves it and returns that artifact's
    /// type, or null when no artifact of this run serves the type. On success the state also
    /// carries the assembly the patch belongs in and the method keys this edit changed, because
    /// every other method still runs the body the artifact holds.
    /// </summary>
    internal static INamedTypeSymbol Adopt(TypeEmitState typeState, SemanticModel semanticModel)
    {
        WorkerRetainedBodyEditType bodyEditType = FindBodyEditType(
            typeState.SourceUnit.RetainedBodyEditTypes,
            CecilTypeNames.ToMetadataName(typeState.TypeSymbol));
        if (bodyEditType == null || !(semanticModel.Compilation is CSharpCompilation compilation))
        {
            return null;
        }

        INamedTypeSymbol artifactType = typeState.SourceUnit.ArtifactMap.FindArtifactType(
            compilation,
            bodyEditType.OriginalAssemblyName,
            bodyEditType.OriginalAssemblyMvid,
            bodyEditType.MetadataName);
        if (artifactType == null)
        {
            return null;
        }

        // Why the assembly name comes from the artifact rather than from the record: the patch
        // has to name the assembly the domain loaded, and the recorded original identity is only
        // what that assembly stands in for.
        string homeAssemblyName = typeState.SourceUnit.ArtifactMap.FindArtifactAssemblyName(
            bodyEditType.OriginalAssemblyName,
            bodyEditType.OriginalAssemblyMvid,
            bodyEditType.MetadataName);
        if (string.IsNullOrEmpty(homeAssemblyName))
        {
            return null;
        }

        typeState.HomeAssemblyName = homeAssemblyName;
        typeState.RetainedChangedMethodKeys = new HashSet<string>(
            bodyEditType.ChangedMethodKeys ?? new string[0],
            StringComparer.Ordinal);
        return artifactType;
    }

    private static WorkerRetainedBodyEditType FindBodyEditType(
        List<WorkerRetainedBodyEditType> bodyEditTypes,
        string metadataName)
    {
        foreach (WorkerRetainedBodyEditType bodyEditType in bodyEditTypes)
        {
            if (string.Equals(bodyEditType.MetadataName, metadataName, StringComparison.Ordinal))
            {
                return bodyEditType;
            }
        }

        return null;
    }
}
