using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

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
                    allowUnsafeCode: false);

                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain("-debug:portable"));
                Assert.That(lines, Does.Not.Contain("-debug-"));
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }
    }
}
