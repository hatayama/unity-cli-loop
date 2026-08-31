using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// String-level coverage for WrapperTemplate entry-point generation (no compile-and-run).
    /// </summary>
    [TestFixture]
    public class WrapperTemplateTests
    {
        [Test]
        public void Build_ShouldGenerateExecuteAsyncWithCancellationTokenAndOmitSyncExecute()
        {
            // Verifies wrapped snippets expose only ExecuteAsync with ct, not a sync Execute that
            // would discard the runtime token via ExecuteAsync(parameters, default).
            string wrappedSource = WrapperTemplate.Build(
                new List<string>(),
                System.Array.Empty<string>(),
                "TestNs",
                "TestClass",
                "return 1;");

            Assert.That(wrappedSource, Does.Contain("public async System.Threading.Tasks.Task<object> ExecuteAsync("));
            Assert.That(wrappedSource, Does.Contain("System.Threading.CancellationToken ct = default)"));
            Assert.That(wrappedSource, Does.Not.Contain("public object Execute("));
            Assert.That(wrappedSource, Does.Not.Contain("ExecuteAsync(parameters, default)"));
            Assert.That(wrappedSource, Does.Not.Contain("GetAwaiter().GetResult()"));
        }
    }
}
