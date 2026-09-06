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

// Maps a type that is bound from a retained artifact assembly back to the identity its source
// belongs to. Without it a declaration would fingerprint differently depending on whether the
// type it depends on is currently a source declaration or already a retained artifact, even
// though the definition did not change.
internal sealed class IntroducedTypeArtifactMap
{
    private readonly Dictionary<string, string> normalizedIdentities;

    private IntroducedTypeArtifactMap(Dictionary<string, string> normalizedIdentities)
    {
        this.normalizedIdentities = normalizedIdentities;
    }

    internal static IntroducedTypeArtifactMap Empty { get; } =
        new IntroducedTypeArtifactMap(new Dictionary<string, string>(StringComparer.Ordinal));

    // Builds the mapping, or reports why the records cannot be trusted. A record is only usable
    // when the assembly resolved from its own reference path reports the identity the record
    // claims and really holds the types it lists: a self-reported name or a metadata name alone
    // would let an unrelated assembly of the same simple name drive normalization.
    internal static bool TryBuild(
        CSharpCompilation compilation,
        IReadOnlyList<(WorkerIntroducedTypeArtifact Artifact, MetadataReference Reference)> artifactReferences,
        out IntroducedTypeArtifactMap map,
        out string errorMessage)
    {
        Dictionary<string, string> identities = new Dictionary<string, string>(StringComparer.Ordinal);
        HashSet<string> normalizedTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach ((WorkerIntroducedTypeArtifact artifact, MetadataReference reference) in artifactReferences)
        {
            if (!TryResolveArtifactAssembly(compilation, artifact, reference, out IAssemblySymbol assembly, out errorMessage))
            {
                map = null;
                return false;
            }

            foreach (WorkerIntroducedTypeArtifactType artifactType in artifact.Types)
            {
                if (!TryAddArtifactType(assembly, artifactType, identities, normalizedTargets, out errorMessage))
                {
                    map = null;
                    return false;
                }
            }
        }

        map = new IntroducedTypeArtifactMap(identities);
        errorMessage = null;
        return true;
    }

    // The normalized identity of a type bound from an artifact, or null when the type does not
    // come from one.
    internal string FindNormalizedIdentity(IAssemblySymbol containingAssembly, string metadataName)
    {
        if (containingAssembly == null || metadataName == null)
        {
            return null;
        }

        return normalizedIdentities.TryGetValue(BuildKey(containingAssembly, metadataName), out string identity)
            ? identity
            : null;
    }

    private static bool TryResolveArtifactAssembly(
        CSharpCompilation compilation,
        WorkerIntroducedTypeArtifact artifact,
        MetadataReference reference,
        out IAssemblySymbol assembly,
        out string errorMessage)
    {
        assembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
        if (assembly == null)
        {
            errorMessage = "Introduced-type artifact could not be resolved as an assembly.";
            return false;
        }

        // Parsed rather than string-compared: the record and the compiler may render the same
        // identity with different display formatting, and a formatting difference is not a
        // mismatched artifact.
        if (!AssemblyIdentity.TryParseDisplayName(artifact.AssemblyFullName ?? string.Empty, out AssemblyIdentity claimed)
            || !claimed.Equals(assembly.Identity))
        {
            errorMessage = "Introduced-type artifact identity does not match the assembly it references.";
            return false;
        }

        return TrySucceed(out errorMessage);
    }

    private static bool TryAddArtifactType(
        IAssemblySymbol assembly,
        WorkerIntroducedTypeArtifactType artifactType,
        Dictionary<string, string> identities,
        HashSet<string> normalizedTargets,
        out string errorMessage)
    {
        if (artifactType == null
            || string.IsNullOrWhiteSpace(artifactType.MetadataName)
            || string.IsNullOrWhiteSpace(artifactType.OriginalAssemblyName)
            || string.IsNullOrWhiteSpace(artifactType.OriginalAssemblyMvid))
        {
            errorMessage = "Introduced-type artifact entry must carry a metadata name and a complete original identity.";
            return false;
        }

        if (assembly.GetTypeByMetadataName(artifactType.MetadataName) == null)
        {
            errorMessage = "Introduced-type artifact does not contain " + artifactType.MetadataName + ".";
            return false;
        }

        string normalizedIdentity = artifactType.OriginalAssemblyName
            + "|" + artifactType.OriginalAssemblyMvid
            + "|" + artifactType.MetadataName;
        if (!identities.TryAdd(BuildKey(assembly, artifactType.MetadataName), normalizedIdentity))
        {
            errorMessage = "Introduced-type artifact lists " + artifactType.MetadataName + " more than once.";
            return false;
        }

        // Two artifacts normalizing to the same original type would leave the fingerprint
        // depending on which record happened to be consulted first.
        if (!normalizedTargets.Add(normalizedIdentity))
        {
            errorMessage = "Two introduced-type artifacts normalize to " + artifactType.MetadataName + ".";
            return false;
        }

        return TrySucceed(out errorMessage);
    }

    private static bool TrySucceed(out string errorMessage)
    {
        errorMessage = null;
        return true;
    }

    private static string BuildKey(IAssemblySymbol assembly, string metadataName)
    {
        return assembly.Identity.GetDisplayName() + "|" + metadataName;
    }
}
