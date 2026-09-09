using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test double for the hot-reload side of the coordination point. Every member falls through
    /// to <see cref="Inner"/> (the port that was installed when the test opened) unless the test
    /// overrides that one member, so a test states only the answers it cares about.
    /// </summary>
    public sealed class StubHotReloadPausePointPort : IHotReloadPausePointPort
    {
        /// <summary>The port answers fall through to. Null answers "no domain installed".</summary>
        public IHotReloadPausePointPort Inner { get; set; }

        public System.Func<MethodBase, MethodBase> ActiveShimForMethod { get; set; }

        public System.Func<string, HotReloadShimFileLookup> ShimLookupForFile { get; set; }

        public System.Func<string, string> VerifiedSnapshotSourceForFile { get; set; }

        public System.Func<string, string, string> VerifiedSnapshotSource { get; set; }

        public System.Func<MethodBase, IReadOnlyList<LocalBuilder>> TransplantLocals { get; set; }

        public System.Func<MethodBase, int> TransplantPreambleLength { get; set; }

        public System.Func<string, IReadOnlyList<string>> AddedFieldsForType { get; set; }

        public MethodBase GetActiveShimForMethod(MethodBase method)
        {
            return ActiveShimForMethod != null
                ? ActiveShimForMethod(method)
                : Inner?.GetActiveShimForMethod(method);
        }

        public HotReloadShimFileLookup GetShimLookupForFile(string file)
        {
            return ShimLookupForFile != null
                ? ShimLookupForFile(file)
                : Inner?.GetShimLookupForFile(file);
        }

        public string GetVerifiedSnapshotSourceForFile(string projectRelativeFile)
        {
            return VerifiedSnapshotSourceForFile != null
                ? VerifiedSnapshotSourceForFile(projectRelativeFile)
                : Inner?.GetVerifiedSnapshotSourceForFile(projectRelativeFile);
        }

        public string GetVerifiedSnapshotSource(string projectRelativeFile, string dllPath)
        {
            return VerifiedSnapshotSource != null
                ? VerifiedSnapshotSource(projectRelativeFile, dllPath)
                : Inner?.GetVerifiedSnapshotSource(projectRelativeFile, dllPath);
        }

        public IReadOnlyList<LocalBuilder> GetTransplantLocals(MethodBase method)
        {
            return TransplantLocals != null
                ? TransplantLocals(method)
                : Inner?.GetTransplantLocals(method);
        }

        public int GetTransplantPreambleLength(MethodBase method)
        {
            if (TransplantPreambleLength != null)
            {
                return TransplantPreambleLength(method);
            }

            return Inner?.GetTransplantPreambleLength(method) ?? 0;
        }

        public IReadOnlyList<string> GetAddedFieldsForType(string typeFullName)
        {
            return AddedFieldsForType != null
                ? AddedFieldsForType(typeFullName)
                : Inner?.GetAddedFieldsForType(typeFullName);
        }
    }
}
