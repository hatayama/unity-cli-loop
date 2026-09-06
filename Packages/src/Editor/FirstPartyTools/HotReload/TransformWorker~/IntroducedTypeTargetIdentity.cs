using System;
using System.IO;
using Microsoft.CodeAnalysis;

// Answers whether the assembly a request was analysed against is the one the request named.
// Planning and transforming both depend on it: a descriptor is only valid for the assembly
// generation it was planned against, and a retained record is only valid for that same
// generation. Keeping one rule is what stops the two paths from accepting different requests.
internal static class IntroducedTypeTargetIdentity
{
    internal static bool MatchesRequest(WorkerInput input, IAssemblySymbol targetAssembly)
    {
        if (targetAssembly == null)
        {
            return false;
        }

        // Assembly names are compared case-insensitively by the runtime, so a case difference in
        // the request is the same assembly and must not reject the plan.
        if (!string.Equals(
                input.TargetAssemblyName,
                targetAssembly.Identity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Guid.TryParse(input.TargetAssemblyMvid, out Guid requestedMvid) || requestedMvid == Guid.Empty)
        {
            return false;
        }

        return requestedMvid == ReadModuleVersionId(input.TargetTypesAssemblyPath);
    }

    private static Guid ReadModuleVersionId(string assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath))
        {
            return Guid.Empty;
        }

        try
        {
            using (ModuleMetadata metadata = ModuleMetadata.CreateFromFile(assemblyPath))
            {
                return metadata.GetModuleVersionId();
            }
        }
        catch (BadImageFormatException)
        {
            return Guid.Empty;
        }
        catch (IOException)
        {
            return Guid.Empty;
        }
    }
}
