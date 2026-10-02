using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies end-to-end C# source migration for a legacy assembly.
    /// </summary>
    public sealed class ThirdPartyToolMigrationCSharpRulesTests
    {
        /// <summary>
        /// Verifies that a registrar qualified with the current Application namespace is moved to ToolContracts when the file also uses the legacy registrar.
        /// </summary>
        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenApplicationRegistrarIsQualified_RewritesToToolContractsRegistrar()
        {
            const string source =
                "class C { void M() { CustomToolManager.RegisterCustomTool(t); " +
                "var r = io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools(); } }";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationCSharpRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    true,
                    false,
                    false,
                    false,
                    false,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>());

            Assert.That(
                result.Content,
                Is.EqualTo(
                    "class C { void M() { io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.RegisterCustomTool(t); " +
                    "var r = io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.GetRegisteredCustomTools(); } }"));
            Assert.That(result.ReplacementCount, Is.EqualTo(2));
        }
    }
}
