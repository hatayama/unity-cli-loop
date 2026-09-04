using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports patches whose method no longer exists in the edited source. No run reverts them,
    /// so without a row they only show up as an ActivePatchTotal larger than the listed methods.
    /// </summary>
    internal static class HotReloadStalePatchOutcomes
    {
        public static void Append(
            List<HotReloadMethodOutcome> outcomes,
            TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures,
            IReadOnlyCollection<string> gatedReplacementMethodKeys,
            string projectRelativePath,
            string assemblyResolvePath)
        {
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
            HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedMethodSignatures)
            {
                if (signature == null || string.IsNullOrEmpty(signature.methodName))
                {
                    continue;
                }

                string[] parameterTypeFullNames =
                    signature.parameterTypeFullNames ?? Array.Empty<string>();
                // Why the gate check uses the wire key: GatedReplacementMethodKeys is worker-side
                // ("Type::Method(args)"), while the patch ledger keys are display-side
                // ("Type.Method(args)"). A gated replacement is not a deletion, so it is not stale.
                string wireKey = HotReloadWireMethodKeys.BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    parameterTypeFullNames,
                    signature.genericArity);
                if (gatedKeys.Contains(wireKey))
                {
                    continue;
                }

                string displayKey = HotReloadPatcher.FormatMethodKeyParts(
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
