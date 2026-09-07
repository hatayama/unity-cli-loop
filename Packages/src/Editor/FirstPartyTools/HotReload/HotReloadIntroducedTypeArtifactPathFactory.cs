using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Allocates a unique, domain-reload-lifetime location for one introduced-type artifact batch.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeArtifactPathFactory
    {
        private readonly string projectRoot;
        private readonly string sessionId;

        public HotReloadIntroducedTypeArtifactPathFactory(string projectRoot, string sessionId)
        {
            this.projectRoot = projectRoot;
            this.sessionId = sessionId;
        }

        public HotReloadIntroducedTypeArtifactPaths Create()
        {
            string artifactId = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(
                projectRoot,
                "Library",
                "UloopHotReload",
                "IntroducedTypes",
                sessionId,
                artifactId);
            string assemblyName = "UloopIntroducedTypes_" + artifactId;
            string dllPath = Path.Combine(directory, assemblyName + ".dll");
            return new HotReloadIntroducedTypeArtifactPaths(
                Path.Combine(directory, assemblyName + ".cs"),
                dllPath,
                Path.Combine(directory, assemblyName + ".pdb"),
                assemblyName);
        }
    }

    /// <summary>
    /// Carries the file paths and assembly identity allocated to one preparation batch.
    /// </summary>
    internal sealed class HotReloadIntroducedTypeArtifactPaths
    {
        public string DirectoryPath { get; }

        public string SourcePath { get; }

        public string DllPath { get; }

        public string PdbPath { get; }

        public string AssemblyName { get; }

        public string AssemblyFullName { get; }

        public HotReloadIntroducedTypeArtifactPaths(
            string sourcePath,
            string dllPath,
            string pdbPath,
            string assemblyName)
        {
            DirectoryPath = Path.GetDirectoryName(sourcePath);
            SourcePath = sourcePath;
            DllPath = dllPath;
            PdbPath = pdbPath;
            AssemblyName = assemblyName;
            AssemblyName identity = new AssemblyName(assemblyName)
            {
                Version = new Version(0, 0, 0, 0),
                CultureInfo = CultureInfo.InvariantCulture
            };
            identity.SetPublicKeyToken(Array.Empty<byte>());
            AssemblyFullName = identity.FullName;
        }

        public string CreateSourcePath(int ordinal)
        {
            if (ordinal < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ordinal));
            }

            return Path.Combine(DirectoryPath, AssemblyName + "_source_" + ordinal + ".cs");
        }
    }
}
