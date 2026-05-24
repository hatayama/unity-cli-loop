using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the shared input visualization prefab contract.
    /// </summary>
    public sealed class InputVisualizationCanvasPrefabTests
    {
        private const string PrefabPath =
            "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab";
        private const string RuntimeAssemblyDefinitionPath =
            "Packages/src/Runtime/uLoopMCP.Runtime.asmdef";

        [Test]
        public void RuntimeAssemblyDefinition_WhenScanned_IsAttachableAndNotAutoReferenced()
        {
            // Verifies the overlay MonoBehaviours can attach to prefabs without becoming player auto-references.
            JObject asmdef = JObject.Parse(ReadText(RuntimeAssemblyDefinitionPath));
            JToken includePlatforms = asmdef["includePlatforms"];

            Assert.That(asmdef["autoReferenced"]?.Value<bool>(), Is.False);
            Assert.That(includePlatforms, Is.Not.Null);
            Assert.That(includePlatforms!.Type, Is.EqualTo(JTokenType.Array));
            Assert.That(includePlatforms!.HasValues, Is.False);
        }

        [Test]
        public void InputVisualizationCanvasPrefab_WhenLoaded_HasRuntimeOverlayReferences()
        {
            // Verifies that overlay tools can instantiate the shared visualization canvas.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);

            InputVisualizationCanvas canvas = prefab.GetComponent<InputVisualizationCanvas>();

            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.KeyboardOverlay, Is.Not.Null);
            Assert.That(canvas.MouseUiOverlay, Is.Not.Null);
            Assert.That(canvas.MouseInputOverlay, Is.Not.Null);
            Assert.That(canvas.RecordInputOverlayPresenter, Is.Not.Null);
            Assert.That(canvas.ReplayInputOverlay, Is.Not.Null);
        }

        private static string ReadText(string relativePath)
        {
            string absolutePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), relativePath);

            return File.ReadAllText(absolutePath);
        }
    }
}
