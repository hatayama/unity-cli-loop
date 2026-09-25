using System;
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
        private readonly Func<string, string> _readSourceContentHashOrNull;

        // Why the hash reader is passed in: the hasher lives in the apply pipeline's assembly,
        // which this one may not reference, and the check must hash the way the worker did.
        public HotReloadPausePointPort(HotReloadDomain domain, Func<string, string> readSourceContentHashOrNull)
        {
            _domain = domain;
            _readSourceContentHashOrNull = readSourceContentHashOrNull;
        }

        public MethodBase GetActiveShimForMethod(MethodBase method)
        {
            return _domain.FindGenerationForMethod(method)?.FindPatchShim(method);
        }

        public HotReloadShimFileLookup GetShimLookupForFile(string file)
        {
            return _domain.LookupShimsForFile(file);
        }

        public HotReloadAddedMethodAtLine FindAddedMethodContainingLine(string file, int line)
        {
            return _domain.FindAddedMethodContainingLine(file, line);
        }

        // Why the domain picks the path to read: it is the path the transform worker hashed, which
        // differs from the argument when a reload was run from an edited copy.
        public bool HasShimSourceChangedOnDisk(string file)
        {
            return _domain.HasShimSourceChangedOnDisk(file, _readSourceContentHashOrNull);
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

        // Why the same path matching as the shim lookup: the pause point tool passes whatever
        // path the caller typed, and a descriptor only ever records the project-relative one.
        public bool IsIntroducedTypeSourceFile(string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                return false;
            }

            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors =
                _domain.IntroducedTypes.DescribeActive();
            for (int index = 0; index < descriptors.Count; index++)
            {
                string ownerProjectRelativePath = descriptors[index].OwnerProjectRelativePath;
                if (string.IsNullOrEmpty(ownerProjectRelativePath))
                {
                    continue;
                }

                if (HotReloadSourcePathNormalizer.PathsReferToSameFile(file, ownerProjectRelativePath))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
