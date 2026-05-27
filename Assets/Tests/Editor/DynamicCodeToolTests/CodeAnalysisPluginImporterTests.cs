using NUnit.Framework;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies bundled Roslyn support plugins stay editor-only.
    /// </summary>
    public sealed class CodeAnalysisPluginImporterTests
    {
        private static readonly string[] CodeAnalysisPluginPaths =
        {
            "Packages/io.github.hatayama.uloopmcp/Editor/FirstPartyTools/ExecuteDynamicCode/Plugins/CodeAnalysis/System.Collections.Immutable.dll",
            "Packages/io.github.hatayama.uloopmcp/Editor/FirstPartyTools/ExecuteDynamicCode/Plugins/CodeAnalysis/System.Reflection.Metadata.dll",
            "Packages/io.github.hatayama.uloopmcp/Editor/FirstPartyTools/ExecuteDynamicCode/Plugins/CodeAnalysis/System.Runtime.CompilerServices.Unsafe.dll"
        };

        [Test]
        public void CodeAnalysisPlugins_WhenLoaded_AreEditorOnly()
        {
            // Tests that Roslyn support plugins cannot leak into player assembly resolution.
            for (int pathIndex = 0; pathIndex < CodeAnalysisPluginPaths.Length; pathIndex++)
            {
                PluginImporter importer = AssetImporter.GetAtPath(CodeAnalysisPluginPaths[pathIndex]) as PluginImporter;

                Assert.That(importer, Is.Not.Null, CodeAnalysisPluginPaths[pathIndex]);
                Assert.That(importer!.GetCompatibleWithAnyPlatform(), Is.False, CodeAnalysisPluginPaths[pathIndex]);
                Assert.That(importer.GetCompatibleWithEditor(), Is.True, CodeAnalysisPluginPaths[pathIndex]);
            }
        }
    }
}
