using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for transform-worker source enumeration, response-file membership,
    /// and cache-key sensitivity. These cases cannot be covered by the live TransformWorker~
    /// folder while it still contains a single .cs file.
    /// </summary>
    public class TransformWorkerBootstrapSourceEnumerationTests
    {
        /// <summary>
        /// What: EnumerateWorkerSourceFiles returns every *.cs file sorted by file name with
        /// StringComparer.Ordinal, independent of creation order.
        /// </summary>
        [Test]
        public void EnumerateWorkerSourceFiles_SortsByFileNameOrdinal()
        {
            string directoryPath = CreateWorkDirectory();
            WriteSource(directoryPath, "zeta.cs", "class Zeta {}");
            WriteSource(directoryPath, "alpha.cs", "class Alpha {}");
            WriteSource(directoryPath, "mu.cs", "class Mu {}");

            string[] sourcePaths = TransformWorkerBootstrap.EnumerateWorkerSourceFiles(directoryPath);

            Assert.That(sourcePaths.Length, Is.EqualTo(3));
            Assert.That(Path.GetFileName(sourcePaths[0]), Is.EqualTo("alpha.cs"));
            Assert.That(Path.GetFileName(sourcePaths[1]), Is.EqualTo("mu.cs"));
            Assert.That(Path.GetFileName(sourcePaths[2]), Is.EqualTo("zeta.cs"));
        }

        /// <summary>
        /// What: the csc response file lists every enumerated source path, in ordinal file-name
        /// order, so a second .cs file cannot be silently dropped.
        /// </summary>
        [Test]
        public void WriteWorkerResponseFile_AppendsEverySourceInOrdinalOrder()
        {
            string directoryPath = CreateWorkDirectory();
            WriteSource(directoryPath, "zeta.cs", "class Zeta {}");
            WriteSource(directoryPath, "alpha.cs", "class Alpha {}");
            string[] sourcePaths = TransformWorkerBootstrap.EnumerateWorkerSourceFiles(directoryPath);

            string sharedDirectoryPath = Path.Combine(directoryPath, "shared");
            Directory.CreateDirectory(sharedDirectoryPath);
            string responseFilePath = Path.Combine(directoryPath, "worker.rsp");
            TransformWorkerBootstrap.WriteWorkerResponseFile(
                responseFilePath,
                sourcePaths,
                Path.Combine(directoryPath, "worker.dll"),
                CreateDummyCompilerPaths(sharedDirectoryPath));

            string[] lines = File.ReadAllLines(responseFilePath);
            int firstSourceIndex = lines.Length - sourcePaths.Length;
            Assert.That(firstSourceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(lines[firstSourceIndex], Is.EqualTo("\"" + sourcePaths[0] + "\""));
            Assert.That(lines[firstSourceIndex + 1], Is.EqualTo("\"" + sourcePaths[1] + "\""));
            Assert.That(Path.GetFileName(sourcePaths[0]), Is.EqualTo("alpha.cs"));
            Assert.That(Path.GetFileName(sourcePaths[1]), Is.EqualTo("zeta.cs"));
        }

        /// <summary>
        /// What: ComputeCacheKey changes when any source file is renamed or when any source
        /// file's bytes change, so a second file cannot be omitted from the cache identity.
        /// </summary>
        [Test]
        public void ComputeCacheKey_ChangesWhenAnySourceNameOrContentChanges()
        {
            string directoryPath = CreateWorkDirectory();
            WriteSource(directoryPath, "alpha.cs", "class Alpha {}");
            WriteSource(directoryPath, "zeta.cs", "class Zeta {}");
            ExternalCompilerPaths paths = CreateDummyCompilerPaths(directoryPath);

            string originalKey = TransformWorkerBootstrap.ComputeCacheKey(
                TransformWorkerBootstrap.EnumerateWorkerSourceFiles(directoryPath),
                paths);

            File.Move(
                Path.Combine(directoryPath, "zeta.cs"),
                Path.Combine(directoryPath, "mu.cs"));
            string renamedKey = TransformWorkerBootstrap.ComputeCacheKey(
                TransformWorkerBootstrap.EnumerateWorkerSourceFiles(directoryPath),
                paths);
            Assert.That(renamedKey, Is.Not.EqualTo(originalKey));

            File.WriteAllText(Path.Combine(directoryPath, "alpha.cs"), "class AlphaChanged {}");
            string contentChangedKey = TransformWorkerBootstrap.ComputeCacheKey(
                TransformWorkerBootstrap.EnumerateWorkerSourceFiles(directoryPath),
                paths);
            Assert.That(contentChangedKey, Is.Not.EqualTo(renamedKey));
            Assert.That(contentChangedKey, Is.Not.EqualTo(originalKey));
        }

        private static string CreateWorkDirectory()
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workRootPath = Path.Combine(
                projectRootPath,
                "Library",
                "UloopHotReloadTests",
                "TransformWorkerBootstrap",
                Path.GetRandomFileName());
            Directory.CreateDirectory(workRootPath);
            return workRootPath;
        }

        private static void WriteSource(string directoryPath, string fileName, string contents)
        {
            File.WriteAllText(Path.Combine(directoryPath, fileName), contents);
        }

        private static ExternalCompilerPaths CreateDummyCompilerPaths(string sharedDirectoryPath)
        {
            return new ExternalCompilerPaths(
                "editor-contents",
                "scripting-root",
                "dotnet",
                "csc.dll",
                "csc.runtimeconfig.json",
                "csc.deps.json",
                "Microsoft.CodeAnalysis.dll",
                "Microsoft.CodeAnalysis.CSharp.dll",
                sharedDirectoryPath,
                ExternalCompilerLayoutKind.Unknown);
        }
    }
}
