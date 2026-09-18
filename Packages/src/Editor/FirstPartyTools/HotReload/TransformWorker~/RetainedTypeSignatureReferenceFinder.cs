using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Finds the retained types an edit would split. A retained declaration this run keeps in the
// source binds as a new definition, while a retained type the run leaves to its assembly still
// names the retained definition in its signatures. A value handed out through such a signature is
// then a different type from the one the edit declares, so every body mixing the two fails to
// bind and the members added to the file earlier stop being registered without any error.
internal static class RetainedTypeSignatureReferenceFinder
{
    // The reason the run is refused, or null when no kept declaration is named by a retained type
    // bound from its assembly. Refused before anything is emitted, because the referrer cannot be
    // rebuilt: the assembly serving it is already loaded.
    internal static string FindRefusal(
        Dictionary<WorkerSourceUnit, List<RetainedDeclarationVerdict>> verdicts,
        CSharpCompilation compilation,
        WorkerInput input,
        IAssemblySymbol targetAssembly,
        IntroducedTypeArtifactMap artifactMap)
    {
        List<RetainedDeclarationVerdict> kept = CollectKeptVerdicts(verdicts);
        if (kept.Count == 0)
        {
            return null;
        }

        HashSet<string> keptIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (RetainedDeclarationVerdict verdict in kept)
        {
            keptIdentities.Add(BuildIdentity(verdict.Record));
        }

        IntroducedTypeDependencyWalker walker = new IntroducedTypeDependencyWalker(
            compilation.Assembly,
            targetAssembly,
            input.TargetAssemblyName,
            input.TargetAssemblyMvid,
            artifactMap);
        Dictionary<string, HashSet<string>> signatureIdentitiesByReferrer =
            CollectSignatureIdentitiesOfBoundTypes(compilation, input, artifactMap, walker, keptIdentities);
        foreach (RetainedDeclarationVerdict verdict in kept)
        {
            List<string> referrers = FindReferrers(BuildIdentity(verdict.Record), signatureIdentitiesByReferrer);
            if (referrers.Count > 0)
            {
                return "Introduced type '" + verdict.MetadataName + "' appears in member signatures of '"
                    + string.Join("', '", referrers)
                    + "', which an earlier reload retained and this edit leaves unchanged; changing '"
                    + verdict.MetadataName
                    + "' would split it between the retained assembly and this edit. Run 'uloop compile' to apply this edit.";
            }
        }

        return null;
    }

    // Only a declaration the run keeps in the source is at risk: an unchanged one leaves the
    // source and binds from the retained assembly like its referrers do.
    private static List<RetainedDeclarationVerdict> CollectKeptVerdicts(
        Dictionary<WorkerSourceUnit, List<RetainedDeclarationVerdict>> verdicts)
    {
        List<RetainedDeclarationVerdict> kept = new List<RetainedDeclarationVerdict>();
        foreach (List<RetainedDeclarationVerdict> unitVerdicts in verdicts.Values)
        {
            foreach (RetainedDeclarationVerdict verdict in unitVerdicts)
            {
                if (verdict.Match.Kind == IntroducedTypeFingerprintMatchKind.MethodBodiesOnly
                    || verdict.Match.Kind == IntroducedTypeFingerprintMatchKind.MembersAdded)
                {
                    kept.Add(verdict);
                }
            }
        }

        return kept;
    }

    // Every retained type the run does not keep, whichever artifact holds it: two types of one
    // artifact split the same way once only one of them is bound from source. Keyed by metadata
    // name so the refusal can name the referrer the way the records do.
    private static Dictionary<string, HashSet<string>> CollectSignatureIdentitiesOfBoundTypes(
        CSharpCompilation compilation,
        WorkerInput input,
        IntroducedTypeArtifactMap artifactMap,
        IntroducedTypeDependencyWalker walker,
        HashSet<string> keptIdentities)
    {
        Dictionary<string, HashSet<string>> identitiesByReferrer =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (WorkerIntroducedTypeArtifact artifact in input.IntroducedTypeArtifacts)
        {
            foreach (WorkerIntroducedTypeArtifactType record in artifact.Types)
            {
                if (keptIdentities.Contains(BuildIdentity(record)))
                {
                    continue;
                }

                INamedTypeSymbol boundType = artifactMap.FindArtifactType(
                    compilation,
                    record.OriginalAssemblyName,
                    record.OriginalAssemblyMvid,
                    record.MetadataName);
                if (boundType != null)
                {
                    identitiesByReferrer[record.MetadataName] = CollectSignatureIdentities(boundType, walker);
                }
            }
        }

        return identitiesByReferrer;
    }

    // Private members count too: a private body that takes the retained type binds against the
    // retained definition exactly as a public one does.
    private static HashSet<string> CollectSignatureIdentities(
        INamedTypeSymbol type,
        IntroducedTypeDependencyWalker walker)
    {
        HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
        walker.AddDependencies(type.BaseType, identities);
        foreach (INamedTypeSymbol implemented in type.Interfaces)
        {
            walker.AddDependencies(implemented, identities);
        }

        foreach (ISymbol member in type.GetMembers())
        {
            // A nested type is a retained type of its own and is checked under its own record.
            if (member is INamedTypeSymbol)
            {
                continue;
            }

            walker.AddDependencies(member, identities);
        }

        return identities;
    }

    private static List<string> FindReferrers(
        string keptIdentity,
        Dictionary<string, HashSet<string>> signatureIdentitiesByReferrer)
    {
        List<string> referrers = new List<string>();
        foreach (KeyValuePair<string, HashSet<string>> entry in signatureIdentitiesByReferrer)
        {
            if (entry.Value.Contains(keptIdentity))
            {
                referrers.Add(entry.Key);
            }
        }

        referrers.Sort(StringComparer.Ordinal);
        return referrers;
    }

    // Spelled the way IntroducedTypeArtifactMap normalizes a retained type, which is the form the
    // dependency walker reports it in.
    private static string BuildIdentity(WorkerIntroducedTypeArtifactType record)
    {
        return record.OriginalAssemblyName + "|" + record.OriginalAssemblyMvid + "|" + record.MetadataName;
    }
}
