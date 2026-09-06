using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies how Dynamic Compilation Health reports name the paths it could not find.
    /// </summary>
    [TestFixture]
    public class DynamicCompilationHealthMonitorTests
    {
        /// <summary>
        /// Every missing path reaches the report text, so a layout change can be diagnosed from the log alone.
        /// </summary>
        [Test]
        public void FormatMissingComponents_WithSeveralPaths_ListsEveryPath()
        {
            string formatted = DynamicCompilationHealthMonitor.FormatMissingComponents(new[]
            {
                "/editor/Data/DotNetSdkRoslyn/csc.dll",
                "/editor/Data/DotNetSdk/dotnet",
                "/editor/Data/NetCoreRuntime/shared/Microsoft.NETCore.App"
            });

            Assert.That(formatted, Does.Contain("/editor/Data/DotNetSdkRoslyn/csc.dll"));
            Assert.That(formatted, Does.Contain("/editor/Data/DotNetSdk/dotnet"));
            Assert.That(formatted, Does.Contain("/editor/Data/NetCoreRuntime/shared/Microsoft.NETCore.App"));
        }

        /// <summary>
        /// The report must never fall back to the collection's type name, which is what hid the paths before.
        /// </summary>
        [Test]
        public void FormatMissingComponents_WithSeveralPaths_DoesNotPrintTheCollectionTypeName()
        {
            List<string> missingComponents = new()
            {
                "/editor/Data/DotNetSdkRoslyn/csc.dll"
            };

            string formatted = DynamicCompilationHealthMonitor.FormatMissingComponents(missingComponents);

            Assert.That(formatted, Does.Not.Contain("System.Collections.Generic"));
            Assert.That(formatted, Does.Not.Contain(missingComponents.GetType().ToString()));
        }

        /// <summary>
        /// An empty list still has to read as "nothing was reported" rather than as an empty log line.
        /// </summary>
        [Test]
        public void FormatMissingComponents_WithNoPaths_ReportsThatNoneWereCollected()
        {
            string formatted = DynamicCompilationHealthMonitor.FormatMissingComponents(new string[0]);

            Assert.That(formatted, Is.Not.Empty);
        }

        /// <summary>
        /// A null collection must not throw while a report is being written about a failure.
        /// </summary>
        [Test]
        public void FormatMissingComponents_WithNullCollection_ReportsThatNoneWereCollected()
        {
            string formatted = DynamicCompilationHealthMonitor.FormatMissingComponents(null);

            Assert.That(formatted, Is.Not.Empty);
        }

        /// <summary>
        /// The context the report carries names each missing path, so the console line is self-contained.
        /// </summary>
        [Test]
        public void BuildFastPathUnavailableContext_WithMissingPaths_NamesEachPathInItsText()
        {
            object context = DynamicCompilationHealthMonitor.BuildFastPathUnavailableContext(
                "/editor/Unity",
                "/editor/Data",
                new[]
                {
                    "/editor/Data/DotNetSdkRoslyn/csc.dll",
                    "/editor/Data/DotNetSdk/dotnet"
                });

            string contextText = context.ToString();

            Assert.That(contextText, Does.Contain("/editor/Unity"));
            Assert.That(contextText, Does.Contain("/editor/Data"));
            Assert.That(contextText, Does.Contain("/editor/Data/DotNetSdkRoslyn/csc.dll"));
            Assert.That(contextText, Does.Contain("/editor/Data/DotNetSdk/dotnet"));
            Assert.That(contextText, Does.Not.Contain("System.Collections.Generic"));
        }
    }
}
