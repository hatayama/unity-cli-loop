using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Dynamic Code API Surface behavior.
    /// </summary>
    [TestFixture]
    public class DynamicCodeApiSurfaceTests
    {
        [Test]
        public void IDynamicCodeExecutor_ShouldExposeAsyncExecutionOnly()
        {
            Assert.That(typeof(IDynamicCodeExecutor).GetMethod("ExecuteCode"), Is.Null);
            Assert.That(typeof(IDynamicCodeExecutor).GetMethod("ExecuteCodeAsync"), Is.Not.Null);
        }

        [Test]
        public void IDynamicCompilationService_ShouldExposeAsyncCompilationOnly()
        {
            Assert.That(typeof(IDynamicCompilationService).GetMethod("Compile"), Is.Null);
            Assert.That(typeof(IDynamicCompilationService).GetMethod("CompileAsync"), Is.Not.Null);
        }
    }
}
