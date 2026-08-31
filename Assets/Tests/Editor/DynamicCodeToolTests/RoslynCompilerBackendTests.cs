using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Guards one-shot csc response-file options that enable portable PDB emission.
    /// </summary>
    [TestFixture]
    public sealed class RoslynCompilerBackendTests
    {
        /// <summary>
        /// Verifies WriteCompilerResponseFile emits -debug:portable so the one-shot csc path keeps PDBs.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_IncludesPortableDebugOption()
        {
            string responseFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-rsp-" + Path.GetRandomFileName());
            try
            {
                RoslynCompilerBackend.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath: "snippet.cs",
                    dllPath: "snippet.dll",
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: false);

                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain("-debug:portable"));
                Assert.That(lines, Does.Not.Contain("-debug-"));
                Assert.That(lines, Does.Contain("-optimize+"));
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }

        /// <summary>
        /// What: WriteCompilerResponseFile requests English diagnostics and UTF-8 compiler output.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_IncludesEnglishDiagnosticsAndUtf8OutputOptions()
        {
            string responseFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-rsp-" + Path.GetRandomFileName());
            try
            {
                RoslynCompilerBackend.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath: "snippet.cs",
                    dllPath: "snippet.dll",
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: false);

                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain("-preferreduilang:en-US"));
                Assert.That(lines, Does.Contain("-utf8output"));
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }

        /// <summary>
        /// What: emitDebugCode writes -optimize- so hot-reload shim one-shot fallback keeps locals.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_WithEmitDebugCode_DisablesOptimization()
        {
            string responseFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-rsp-" + Path.GetRandomFileName());
            try
            {
                RoslynCompilerBackend.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath: "snippet.cs",
                    dllPath: "snippet.dll",
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: true);

                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain("-optimize-"));
                Assert.That(lines, Does.Not.Contain("-optimize+"));
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }

        /// <summary>
        /// What: WriteCompilerResponseFile maps the source directory onto the project root via -pathmap.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_MapsSourceDirectoryToProjectRoot()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-roslyn-pathmap-" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            string responseFilePath = Path.Combine(tempDirectory, "snippet.rsp");
            string sourcePath = Path.Combine(tempDirectory, "snippet.cs");
            string dllPath = Path.Combine(tempDirectory, "snippet.dll");
            try
            {
                RoslynCompilerBackend.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath,
                    dllPath,
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: false);

                string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
                string expectedPathmap = "-pathmap:\"" + sourceDirectory + "=" + UnityCliLoopPathResolver.GetProjectRoot() + "\"";
                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain(expectedPathmap));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// What: WriteWorkerRequestFile encodes emitDebugCode for the shared Roslyn worker.
        /// </summary>
        [Test]
        public void WriteWorkerRequestFile_IncludesDebugCodeFlag()
        {
            string requestFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-worker-" + Path.GetRandomFileName());
            string sourcePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-src-" + Path.GetRandomFileName() + ".cs");
            string dllPath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-dll-" + Path.GetRandomFileName() + ".dll");
            File.WriteAllText(sourcePath, "class C {}");
            try
            {
                RoslynCompilerBackend.WriteWorkerRequestFile(
                    requestFilePath,
                    sourcePath,
                    dllPath,
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: true);

                string[] lines = File.ReadAllLines(requestFilePath);
                Assert.That(lines, Does.Contain("debugCode:1"));
                Assert.That(lines, Does.Contain("unsafe:0"));
            }
            finally
            {
                if (File.Exists(requestFilePath))
                {
                    File.Delete(requestFilePath);
                }

                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
        }
    }
}
