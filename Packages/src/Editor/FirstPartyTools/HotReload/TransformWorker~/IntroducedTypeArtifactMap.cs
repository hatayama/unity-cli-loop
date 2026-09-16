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
    private readonly Index index;

    private IntroducedTypeArtifactMap(Index index)
    {
        this.index = index;
    }

    internal static IntroducedTypeArtifactMap Empty { get; } = new IntroducedTypeArtifactMap(new Index());

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
        Index index = new Index();
        foreach ((WorkerIntroducedTypeArtifact artifact, MetadataReference reference) in artifactReferences)
        {
            if (!TryResolveArtifactAssembly(compilation, artifact, reference, out IAssemblySymbol assembly, out errorMessage))
            {
                map = null;
                return false;
            }

            // A record that lists no type puts its reference into the compilation but takes no
            // declaration out of the binding tree, so the type binds from source while the loaded
            // assembly already holds it - the same silent fallback an incomplete record produces.
            if (artifact.Types == null || artifact.Types.Length == 0)
            {
                map = null;
                errorMessage = "Introduced-type artifact must list the types it holds.";
                return false;
            }

            foreach (WorkerIntroducedTypeArtifactType artifactType in artifact.Types)
            {
                if (!TryAddArtifactType(assembly, reference, artifactType, index, out errorMessage))
                {
                    map = null;
                    return false;
                }
            }
        }

        map = new IntroducedTypeArtifactMap(index);
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

        return index.NormalizedIdentities.TryGetValue(BuildKey(containingAssembly, metadataName), out string identity)
            ? identity
            : null;
    }

    // The fingerprint the retained assembly of this type was compiled from, or null when no
    // record holds the type. A declaration whose fingerprint matches is already introduced, so
    // planning it again would produce a second record normalizing to the same original type.
    internal string FindActiveDeclarationFingerprint(
        string originalAssemblyName,
        string originalAssemblyMvid,
        string metadataName)
    {
        string normalizedIdentity = BuildNormalizedIdentity(originalAssemblyName, originalAssemblyMvid, metadataName);
        if (normalizedIdentity == null)
        {
            return null;
        }

        return index.FingerprintsByNormalizedIdentity.TryGetValue(normalizedIdentity, out string fingerprint)
            ? fingerprint
            : null;
    }

    /// <summary>
    /// The type a retained artifact serves under a recorded identity, bound in the compilation
    /// asked about, or null when no record holds the type. Every member is visible, private ones
    /// included, because a caller that classifies the source against this type would otherwise
    /// read a private method the artifact already runs as one this edit added.
    /// </summary>
    internal INamedTypeSymbol FindArtifactType(
        CSharpCompilation compilation,
        string originalAssemblyName,
        string originalAssemblyMvid,
        string metadataName)
    {
        string normalizedIdentity = BuildNormalizedIdentity(originalAssemblyName, originalAssemblyMvid, metadataName);
        if (compilation == null
            || normalizedIdentity == null
            || !index.HomesByNormalizedIdentity.TryGetValue(normalizedIdentity, out ArtifactTypeHome home))
        {
            return null;
        }

        IAssemblySymbol assembly = WorkerCompiledAssemblySymbols.ResolveWithAllMembers(compilation, home.Reference);
        return assembly?.GetTypeByMetadataName(metadataName);
    }

    /// <summary>
    /// The simple name of the assembly a retained artifact serves a recorded type from, or null
    /// when no record holds the type. It is the assembly a patch of that type has to name, which
    /// is not the one the edited file belongs to.
    /// </summary>
    internal string FindArtifactAssemblyName(
        string originalAssemblyName,
        string originalAssemblyMvid,
        string metadataName)
    {
        string normalizedIdentity = BuildNormalizedIdentity(originalAssemblyName, originalAssemblyMvid, metadataName);
        if (normalizedIdentity == null
            || !index.HomesByNormalizedIdentity.TryGetValue(normalizedIdentity, out ArtifactTypeHome home))
        {
            return null;
        }

        return home.AssemblySimpleName;
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
        MetadataReference reference,
        WorkerIntroducedTypeArtifactType artifactType,
        Index index,
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

        // Without the owning file and the fingerprint, re-verification finds no record to compare
        // the edited declaration against, silently leaves it in the tree and binds the type from
        // source while the loaded assembly already holds it.
        if (string.IsNullOrWhiteSpace(artifactType.OwnerProjectRelativePath)
            || string.IsNullOrWhiteSpace(artifactType.DeclarationFingerprint))
        {
            errorMessage = "Introduced-type artifact entry must carry its owning file and its declaration fingerprint.";
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
        if (!index.NormalizedIdentities.TryAdd(BuildKey(assembly, artifactType.MetadataName), normalizedIdentity))
        {
            errorMessage = "Introduced-type artifact lists " + artifactType.MetadataName + " more than once.";
            return false;
        }

        // Two artifacts normalizing to the same original type would leave the fingerprint
        // depending on which record happened to be consulted first.
        if (!index.FingerprintsByNormalizedIdentity.TryAdd(normalizedIdentity, artifactType.DeclarationFingerprint))
        {
            errorMessage = "Two introduced-type artifacts normalize to " + artifactType.MetadataName + ".";
            return false;
        }

        index.HomesByNormalizedIdentity[normalizedIdentity] = new ArtifactTypeHome(reference, assembly.Identity.Name);
        return TrySucceed(out errorMessage);
    }

    private static string BuildNormalizedIdentity(
        string originalAssemblyName,
        string originalAssemblyMvid,
        string metadataName)
    {
        if (originalAssemblyName == null || originalAssemblyMvid == null || metadataName == null)
        {
            return null;
        }

        return originalAssemblyName + "|" + originalAssemblyMvid + "|" + metadataName;
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

    // Where one recorded type is served from: the reference the run put into the compilation, so
    // the type can be bound again where a later stage needs it, and the simple name that patch
    // has to be applied to.
    private sealed class ArtifactTypeHome
    {
        internal ArtifactTypeHome(MetadataReference reference, string assemblySimpleName)
        {
            Reference = reference;
            AssemblySimpleName = assemblySimpleName;
        }

        internal MetadataReference Reference { get; }

        internal string AssemblySimpleName { get; }
    }

    // The three lookups the map is built from, carried together so building one entry does not
    // spread over a parameter list that says nothing about which dictionary is which.
    private sealed class Index
    {
        internal Dictionary<string, string> NormalizedIdentities { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal Dictionary<string, string> FingerprintsByNormalizedIdentity { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal Dictionary<string, ArtifactTypeHome> HomesByNormalizedIdentity { get; } =
            new Dictionary<string, ArtifactTypeHome>(StringComparer.Ordinal);
    }
}
