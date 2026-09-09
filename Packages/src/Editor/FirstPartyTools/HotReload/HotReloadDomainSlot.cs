using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The one hot-reload domain this Unity domain runs on, and the wiring that lets pause-point
    /// reach it.
    /// </summary>
    /// <remarks>
    /// Why a static slot: Harmony resolves transpilers as static methods, so the transpiler cannot
    /// receive the domain as an argument and has to read it from somewhere fixed. Pause-point sits
    /// behind an assembly boundary that must not reference this one, so it reaches the same domain
    /// through the coordination delegates set here.
    /// This is a scaffold: the composition root replaces it as the owner of the domain, leaving
    /// only the transpiler's path to it behind.
    /// </remarks>
    internal static class HotReloadDomainSlot
    {
        private static readonly HotReloadDomain Domain = CreateDomain();

        internal static HotReloadDomain Current => Domain;

        private static HotReloadDomain CreateDomain()
        {
            HotReloadDomain domain = new HotReloadDomain();
            HotReloadPausePointCoordination.GetShimLookupForFile = domain.LookupShimsForFile;
            HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile =
                domain.LoadVerifiedSnapshotSourceForFile;
            HotReloadPausePointCoordination.GetVerifiedSnapshotSource = LoadVerifiedSnapshotSource;
            HotReloadPausePointCoordination.GetAddedFieldsForType = domain.GetAddedFieldsForType;
            HotReloadPausePointCoordination.GetActiveShimForMethod =
                method => domain.FindGenerationForMethod(method)?.FindPatchShim(method);
            HotReloadPausePointCoordination.GetTransplantLocals =
                method => domain.FindGenerationForMethod(method)?.FindTransplantLocals(method);
            HotReloadPausePointCoordination.GetTransplantPreambleLength =
                method => domain.FindGenerationForMethod(method)?.FindTransplantPreambleLength(method) ?? 0;
            return domain;
        }

        private static string LoadVerifiedSnapshotSource(string projectRelativeFile, string dllPath)
        {
            if (string.IsNullOrEmpty(projectRelativeFile) || string.IsNullOrEmpty(dllPath))
            {
                return null;
            }

            return HotReloadSourceBaseline.LoadVerifiedSnapshotSource(projectRelativeFile, dllPath);
        }
    }
}
