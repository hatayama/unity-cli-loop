using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ToolAssemblyClassifier
    {
        private const string ApplicationAssemblyName = "UnityCLILoop.Application";
        private const string FirstPartyToolsAssemblyNamePrefix = "UnityCLILoop.FirstPartyTools.";

        public static bool IsThirdPartyAssembly(string assemblyName)
        {
            return assemblyName != ApplicationAssemblyName &&
                   !assemblyName.StartsWith(FirstPartyToolsAssemblyNamePrefix, StringComparison.Ordinal);
        }
    }
}
