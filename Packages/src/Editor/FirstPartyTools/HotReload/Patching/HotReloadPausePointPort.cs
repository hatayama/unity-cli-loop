using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Answers the pause-point tool's questions about one hot-reload domain. The composition root
    /// publishes an instance of this for the domain it installs, so an answer can never come from
    /// a domain that is no longer installed.
    /// </summary>
    internal sealed class HotReloadPausePointPort : IHotReloadPausePointPort
    {
        private readonly HotReloadDomain _domain;

        public HotReloadPausePointPort(HotReloadDomain domain)
        {
            _domain = domain;
        }

        public MethodBase GetActiveShimForMethod(MethodBase method)
        {
            return _domain.FindGenerationForMethod(method)?.FindPatchShim(method);
        }

        public HotReloadShimFileLookup GetShimLookupForFile(string file)
        {
            return _domain.LookupShimsForFile(file);
        }

        public string GetVerifiedSnapshotSourceForFile(string projectRelativeFile)
        {
            return _domain.LoadVerifiedSnapshotSourceForFile(projectRelativeFile);
        }

        public string GetVerifiedSnapshotSource(string projectRelativeFile, string dllPath)
        {
            if (string.IsNullOrEmpty(projectRelativeFile) || string.IsNullOrEmpty(dllPath))
            {
                return null;
            }

            return HotReloadSourceBaseline.LoadVerifiedSnapshotSource(projectRelativeFile, dllPath);
        }

        public IReadOnlyList<LocalBuilder> GetTransplantLocals(MethodBase method)
        {
            return _domain.FindGenerationForMethod(method)?.FindTransplantLocals(method);
        }

        public int GetTransplantPreambleLength(MethodBase method)
        {
            return _domain.FindGenerationForMethod(method)?.FindTransplantPreambleLength(method) ?? 0;
        }

        public IReadOnlyList<string> GetAddedFieldsForType(string typeFullName)
        {
            return _domain.GetAddedFieldsForType(typeFullName);
        }
    }
}
