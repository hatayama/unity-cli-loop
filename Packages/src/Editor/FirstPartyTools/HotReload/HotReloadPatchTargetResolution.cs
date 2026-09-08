using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The outcome of resolving the compiled assembly target for one hot-reload file: either an
    /// early result the caller must report as-is, or the resolved target the apply stage needs.
    /// </summary>
    internal sealed class HotReloadPatchTargetResolution
    {
        public HotReloadFileProcessResult EarlyResult { get; }
        public string ProjectRelativePath { get; }
        public string AssemblyName { get; }
        public UnityCompilationAssembly CompilationAssembly { get; }
        public string TargetDllPath { get; }
        public string ProjectRoot { get; }
        public HotReloadUnchangedSourceDecision UnchangedDecision { get; }
        public HotReloadNewSourceMembershipEvidence NewSourceMembershipEvidence { get; }

        public bool IsEarlyExit => EarlyResult != null;

        private HotReloadPatchTargetResolution(
            HotReloadFileProcessResult earlyResult,
            string projectRelativePath,
            string assemblyName,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string projectRoot,
            HotReloadUnchangedSourceDecision unchangedDecision,
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence)
        {
            EarlyResult = earlyResult;
            ProjectRelativePath = projectRelativePath;
            AssemblyName = assemblyName;
            CompilationAssembly = compilationAssembly;
            TargetDllPath = targetDllPath;
            ProjectRoot = projectRoot;
            UnchangedDecision = unchangedDecision;
            NewSourceMembershipEvidence = newSourceMembershipEvidence;
        }

        /// <summary>
        /// A file that never reached a patch target. The caller reports the early result and
        /// reads none of the target fields.
        /// </summary>
        internal static HotReloadPatchTargetResolution EarlyExit(HotReloadFileProcessResult earlyResult)
        {
            Debug.Assert(earlyResult != null, "earlyResult must not be null.");

            return new HotReloadPatchTargetResolution(
                earlyResult,
                null,
                null,
                null,
                null,
                null,
                HotReloadUnchangedSourceDecision.NotUnchanged,
                null);
        }

        /// <summary>
        /// A file whose compiled assembly, DLL and project root were all resolved.
        /// </summary>
        internal static HotReloadPatchTargetResolution Resolved(
            string projectRelativePath,
            string assemblyName,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string projectRoot,
            HotReloadUnchangedSourceDecision unchangedDecision,
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(compilationAssembly != null, "compilationAssembly must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(targetDllPath), "targetDllPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");

            return new HotReloadPatchTargetResolution(
                null,
                projectRelativePath,
                assemblyName,
                compilationAssembly,
                targetDllPath,
                projectRoot,
                unchangedDecision,
                newSourceMembershipEvidence);
        }
    }
}
