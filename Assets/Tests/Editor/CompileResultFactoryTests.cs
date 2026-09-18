using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests how raw compiler messages become a compile result, including the duplicates Unity
    /// can deliver for one diagnostic.
    /// </summary>
    [TestFixture]
    public sealed class CompileResultFactoryTests
    {
        /// <summary>
        /// What: the same warning delivered twice is counted and listed once.
        /// </summary>
        [Test]
        public void CreateCompileResult_WithTheSameWarningTwice_KeepsOne()
        {
            CompileResult result = CompileResultFactory.CreateCompileResult(
                new[] { Warning("Assets/A.cs", "CS0414: unused"), Warning("Assets/A.cs", "CS0414: unused") },
                isForceCompile: false);

            Assert.That(result.WarningCount, Is.EqualTo(1));
            Assert.That(result.Warnings.Length, Is.EqualTo(1));
            Assert.That(result.Messages.Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: two warnings that differ only by file are both kept.
        /// </summary>
        [Test]
        public void CreateCompileResult_WithTheSameWarningInTwoFiles_KeepsBoth()
        {
            CompileResult result = CompileResultFactory.CreateCompileResult(
                new[] { Warning("Assets/A.cs", "CS0414: unused"), Warning("Assets/B.cs", "CS0414: unused") },
                isForceCompile: false);

            Assert.That(result.WarningCount, Is.EqualTo(2));
            Assert.That(result.Warnings.Length, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a forced recompile, which reports counts without details, counts a duplicated
        /// error once.
        /// </summary>
        [Test]
        public void CreateCompileResult_ForcedWithTheSameErrorTwice_CountsOne()
        {
            CompilerMessage error = new CompilerMessage
            {
                type = CompilerMessageType.Error,
                message = "CS0103: missing",
                file = "Assets/A.cs",
                line = 3,
                column = 5
            };

            CompileResult result = CompileResultFactory.CreateCompileResult(
                new[] { error, error },
                isForceCompile: true);

            Assert.That(result.ErrorCount, Is.EqualTo(1));
        }

        private static CompilerMessage Warning(string file, string message)
        {
            return new CompilerMessage
            {
                type = CompilerMessageType.Warning,
                message = message,
                file = file,
                line = 10,
                column = 7
            };
        }
    }
}
