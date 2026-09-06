using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;

// Resolves each retained-artifact record to the metadata reference the compilation will bind it
// through. Adding a second reference for a file the run already references would give the
// compilation two references of one assembly identity; it drops one, and the record's own
// reference then resolves to no symbol, which would report a valid artifact as unreadable.
internal static class IntroducedTypeArtifactReferences
{
    internal static List<(WorkerIntroducedTypeArtifact Artifact, MetadataReference Reference)> Collect(
        WorkerInput input,
        List<MetadataReference> references,
        List<string> parseErrors)
    {
        Dictionary<string, MetadataReference> referencesByPath = BuildPathIndex(references);
        List<(WorkerIntroducedTypeArtifact, MetadataReference)> artifactReferences =
            new List<(WorkerIntroducedTypeArtifact, MetadataReference)>();
        foreach (WorkerIntroducedTypeArtifact artifact in input.IntroducedTypeArtifacts)
        {
            if (artifact == null || string.IsNullOrEmpty(artifact.ReferencePath))
            {
                parseErrors.Add("Introduced-type artifact record must carry a reference path.");
                continue;
            }

            if (!File.Exists(artifact.ReferencePath))
            {
                parseErrors.Add("Introduced-type artifact not found: " + artifact.ReferencePath);
                continue;
            }

            string fullPath = Path.GetFullPath(artifact.ReferencePath);
            if (!referencesByPath.TryGetValue(fullPath, out MetadataReference reference))
            {
                reference = MetadataReference.CreateFromFile(fullPath);
                references.Add(reference);
                referencesByPath.Add(fullPath, reference);
            }

            artifactReferences.Add((artifact, reference));
        }

        return artifactReferences;
    }

    private static Dictionary<string, MetadataReference> BuildPathIndex(List<MetadataReference> references)
    {
        Dictionary<string, MetadataReference> referencesByPath =
            new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        foreach (MetadataReference reference in references)
        {
            if (string.IsNullOrEmpty(reference.Display))
            {
                continue;
            }

            referencesByPath[Path.GetFullPath(reference.Display)] = reference;
        }

        return referencesByPath;
    }
}
