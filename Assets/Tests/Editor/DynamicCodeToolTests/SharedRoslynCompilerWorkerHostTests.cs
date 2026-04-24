using System.Diagnostics;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class SharedRoslynCompilerWorkerHostTests
    {
        [Test]
        public void ConfigureWorkerDotnetRuntimeEnvironment_WhenCalled_ShouldDisableMultilevelLookup()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.EnvironmentVariables[SharedRoslynCompilerWorkerHost.DotnetMultilevelLookupEnvironmentVariableName] = "1";

            SharedRoslynCompilerWorkerHost.ConfigureWorkerDotnetRuntimeEnvironment(startInfo);

            Assert.That(
                startInfo.EnvironmentVariables[SharedRoslynCompilerWorkerHost.DotnetMultilevelLookupEnvironmentVariableName],
                Is.EqualTo(SharedRoslynCompilerWorkerHost.DotnetMultilevelLookupDisabledValue));
        }
    }
}
