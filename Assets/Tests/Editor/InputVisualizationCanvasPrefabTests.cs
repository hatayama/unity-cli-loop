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

        [Test]
        public void InputVisualizationCanvasPrefab_WhenInstantiated_HasRuntimeOverlayReferences()
        {
            // Verifies that stale prefab import artifacts do not leave runtime overlay references unassigned.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                InputVisualizationCanvas canvas = instance.GetComponent<InputVisualizationCanvas>();

                Assert.That(canvas.KeyboardOverlay, Is.Not.Null);
                Assert.That(canvas.MouseUiOverlay, Is.Not.Null);
                Assert.That(canvas.MouseInputOverlay, Is.Not.Null);
                Assert.That(canvas.RecordInputOverlayPresenter, Is.Not.Null);
                Assert.That(canvas.ReplayInputOverlay, Is.Not.Null);

                AssertSerializedReference(canvas.KeyboardOverlay, "_container");
                AssertSerializedReference(canvas.KeyboardOverlay, "_containerImage");
                AssertSerializedReference(canvas.MouseUiOverlay, "_canvasGroup");
                AssertSerializedReference(canvas.MouseUiOverlay, "_cursorGroup");
                AssertSerializedReference(canvas.MouseUiOverlay, "_circleImage");
                AssertSerializedReference(canvas.MouseUiOverlay, "_crosshairH");
                AssertSerializedReference(canvas.MouseUiOverlay, "_crosshairV");
                AssertSerializedReference(canvas.MouseUiOverlay, "_longPressText");
                AssertSerializedReference(canvas.MouseUiOverlay, "_dragStartMarker");
                AssertSerializedReference(canvas.MouseUiOverlay, "_circleSprite");
                AssertSerializedReference(canvas.MouseInputOverlay, "_leftButton");
                AssertSerializedReference(canvas.MouseInputOverlay, "_rightButton");
                AssertSerializedReference(canvas.MouseInputOverlay, "_scrollWheel");
                AssertSerializedReference(canvas.MouseInputOverlay, "_scrollArrowTop");
                AssertSerializedReference(canvas.MouseInputOverlay, "_scrollArrowBottom");
                AssertSerializedReference(canvas.MouseInputOverlay, "_moveDirectionGroup");
                AssertSerializedReference(canvas.RecordInputOverlayPresenter, "_view");

                RecordInputOverlayView recordView =
                    canvas.RecordInputOverlayPresenter.GetComponent<RecordInputOverlayView>();
                AssertSerializedReference(recordView, "_canvasGroup");
                AssertSerializedReference(recordView, "_countdownGroup");
                AssertSerializedReference(recordView, "_countdownText");
                AssertSerializedReference(recordView, "_recordingGroup");
                AssertSerializedReference(recordView, "_recDotText");
                AssertSerializedReference(recordView, "_statusText");
                AssertSerializedReference(canvas.ReplayInputOverlay, "_statusText");
                AssertSerializedReference(canvas.ReplayInputOverlay, "_progressBarFill");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertSerializedReference(UnityEngine.Object target, string propertyName)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.objectReferenceValue, Is.Not.Null, propertyName);
        }

        private static string ReadText(string relativePath)
        {
            string absolutePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), relativePath);

            return File.ReadAllText(absolutePath);
        }
    }
}
