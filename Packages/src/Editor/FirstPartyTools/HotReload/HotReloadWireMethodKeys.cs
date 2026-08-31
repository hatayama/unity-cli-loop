using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats Unity-side wire method keys; keep in sync with the worker and call-site scanner.
    /// </summary>
    internal static class HotReloadWireMethodKeys
    {
        // Keep in sync with TransformWorkerProgram.BuildMethodKey (out-of-process worker side)
        // and HotReloadCallSiteScanner.CreateHit.
        // Why arity suffix: Caller(int) and Caller<T>(int) must not share a wire key.
        // Arity 0 keeps the bare name so existing non-generic keys stay stable.
        internal static string BuildMethodKey(TransformWorkerEntryDto entry)
        {
            return BuildMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                entry.parameterTypeFullNames,
                entry.genericArity);
        }

        internal static string BuildMethodKeyParts(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            string nameWithArity = methodName;
            if (genericArity > 0)
            {
                nameWithArity = methodName + "`" + genericArity.ToString();
            }

            return typeMetadataName + "::" + nameWithArity + "("
                + string.Join(",", parameterTypeFullNames ?? Array.Empty<string>()) + ")";
        }
    }
}
