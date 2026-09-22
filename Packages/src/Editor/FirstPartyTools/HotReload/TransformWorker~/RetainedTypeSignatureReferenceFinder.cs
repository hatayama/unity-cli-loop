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
        HashSet<string> preparedReferrers = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> signatureIdentitiesByReferrer = CollectSignatureIdentitiesOfBoundTypes(
            compilation, input, artifactMap, walker, keptIdentities, preparedReferrers);
        foreach (RetainedDeclarationVerdict verdict in kept)
        {
            List<string> referrers = FindReferrers(BuildIdentity(verdict.Record), signatureIdentitiesByReferrer);
            List<string> retainedReferrers = referrers.FindAll(referrer => !preparedReferrers.Contains(referrer));
            List<string> sameRunReferrers = referrers.FindAll(referrer => preparedReferrers.Contains(referrer));
            if (sameRunReferrers.Count > 0 && verdict.HoldsOnlyAppliedChanges)
            {
                return FormatAppliedChangesReferrerRefusal(verdict.MetadataName, sameRunReferrers);
            }

            if (sameRunReferrers.Count > 0)
            {
                return FormatSameRunReferrerRefusal(verdict.MetadataName, retainedReferrers, sameRunReferrers);
            }

            if (retainedReferrers.Count > 0)
            {
                return FormatRetainedReferrerRefusal(verdict.MetadataName, retainedReferrers);
            }
        }

        return null;
    }

    private static string FormatRetainedReferrerRefusal(string metadataName, List<string> referrers)
    {
        string referrerList = FormatNameList(referrers);
        return "Introduced type '" + metadataName + "' appears in member signatures of "
            + referrerList
            + ", which an earlier reload retained and this edit leaves unchanged; changing '"
            + metadataName
            + "' would split it between the retained assembly and this edit. To keep hot reloading, also edit "
            + referrerList
            + " in this same reload (a method body change is enough); passing its file unchanged does not help. "
            + "Otherwise run 'uloop compile' to apply this edit.";
    }

    // A type this run introduces was compiled against the loaded definition before the edit could
    // reach it, so editing it in this same reload cannot help. Once a reload has introduced it,
    // it is a retained type that a body edit keeps in the source next to the changed one. Why a
    // retained referrer is folded into the same two steps rather than refused on its own first:
    // editing only it in this reload still leaves the new type split, so that advice cannot work.
    private static string FormatSameRunReferrerRefusal(
        string metadataName,
        List<string> retainedReferrers,
        List<string> sameRunReferrers)
    {
        string sameRunList = FormatNameList(sameRunReferrers);
        List<string> allReferrers = new List<string>(retainedReferrers);
        allReferrers.AddRange(sameRunReferrers);
        string retainedClause = retainedReferrers.Count == 0
            ? string.Empty
            : FormatNameList(retainedReferrers) + ", which an earlier reload retained and this edit leaves unchanged, and of ";
        return "Introduced type '" + metadataName + "' appears in member signatures of "
            + retainedClause
            + sameRunList
            + ", introduced by this reload from a new file and compiled against the '"
            + metadataName
            + "' an earlier reload loaded; changing '"
            + metadataName
            + "' in the same reload would split it between that assembly and this edit. To keep hot reloading, "
            + "reload in two steps: first without the change to '"
            + metadataName
            + "', which introduces "
            + sameRunList
            + ", then make the change together with an edit of "
            + FormatNameList(allReferrers)
            + " (a method body change is enough). Otherwise run 'uloop compile' to apply this edit.";
    }

    // Why the two steps are not offered here: they begin with a reload without the change, and
    // this reload holds none. What is kept in the source is what earlier reloads added, and a new
    // type is compiled against the type those reloads first loaded, which never holds it, so no
    // order of reloads can join the two before a compile.
    private static string FormatAppliedChangesReferrerRefusal(string metadataName, List<string> sameRunReferrers)
    {
        string sameRunList = FormatNameList(sameRunReferrers);
        return "Introduced type '" + metadataName + "' appears in member signatures of "
            + sameRunList
            + ", introduced by this reload from a new file. The source of '"
            + metadataName
            + "' holds only what earlier reloads added to or edited in it, and this reload changes nothing in it, but "
            + sameRunList
            + " is compiled against the '"
            + metadataName
            + "' an earlier reload first loaded, which does not hold those changes, so the two would split. "
            + "Reloading in steps cannot join them: run 'uloop compile' to apply this edit, or name '"
            + metadataName
            + "' only inside method bodies of "
            + sameRunList
            + " rather than in its member signatures.";
    }

    private static string FormatNameList(List<string> names)
    {
        return "'" + string.Join("', '", names) + "'";
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
    // name so the refusal can name the referrer the way the records do. The types of the artifact
    // this run prepared split the same way, but are reported apart because no earlier reload
    // retained them.
    private static Dictionary<string, HashSet<string>> CollectSignatureIdentitiesOfBoundTypes(
        CSharpCompilation compilation,
        WorkerInput input,
        IntroducedTypeArtifactMap artifactMap,
        IntroducedTypeDependencyWalker walker,
        HashSet<string> keptIdentities,
        HashSet<string> preparedReferrers)
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
                if (boundType == null)
                {
                    continue;
                }

                identitiesByReferrer[record.MetadataName] = CollectSignatureIdentities(boundType, walker);
                if (artifact.PreparedByThisRun)
                {
                    preparedReferrers.Add(record.MetadataName);
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
