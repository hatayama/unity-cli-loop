using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports patches whose method no longer exists in the edited source. Nothing in the run that
    /// dropped the method reverts them, so without a row they only show up as an ActivePatchTotal
    /// larger than the listed methods.
    /// </summary>
    internal static class HotReloadStalePatchOutcomes
    {
        public static void Append(
            List<HotReloadMethodOutcome> outcomes,
            TransformWorkerOutputDto workerOutput,
            IReadOnlyCollection<string> gatedReplacementMethodKeys,
            string projectRelativePath,
            string assemblyResolvePath)
        {
            TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures =
                workerOutput?.removedMethodSignatures;
            if (removedMethodSignatures == null || removedMethodSignatures.Length == 0)
            {
                return;
            }

            HashSet<string> activeDisplayKeys = new HashSet<string>(
                HotReloadPatcher.ListActiveMethodKeys(projectRelativePath),
                StringComparer.Ordinal);
            if (activeDisplayKeys.Count == 0)
            {
                return;
            }

            HashSet<string> gatedKeys = new HashSet<string>(
                gatedReplacementMethodKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            IReadOnlyDictionary<string, TransformWorkerEntryDto> replacementsByWireKey =
                HotReloadReplacedCompiledMethodEntries.IndexByReplacedWireKey(workerOutput.entries);
            HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedMethodSignatures)
            {
                if (signature == null || string.IsNullOrEmpty(signature.methodName))
                {
                    continue;
                }

                string[] parameterTypeFullNames =
                    signature.parameterTypeFullNames ?? Array.Empty<string>();
                // Why the replacement check matters: a return-type change removes the old signature and
                // adds a replacement under the same display key, so an earlier body patch on that key
                // is still reachable and must not be read as a patch left behind by a deleted method.
                // Why the gate and replacement checks use the wire key: both worker-side sets are
                // "Type::Method(args)", while the patch ledger keys are display-side
                // ("Type.Method(args)"). Neither a gated nor an applied replacement is a deletion.
                string wireKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                if (gatedKeys.Contains(wireKey) || replacementsByWireKey.ContainsKey(wireKey))
                {
                    continue;
                }

                string displayKey = HotReloadMethodKeys.FormatMethodLabelParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                if (!activeDisplayKeys.Contains(displayKey) || !reported.Add(displayKey))
                {
                    continue;
                }

                outcomes.Add(HotReloadMethodOutcome.Stale(displayKey, assemblyResolvePath));
            }
        }

    }
}
