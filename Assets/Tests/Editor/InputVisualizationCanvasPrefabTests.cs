using System.IO;
using System.Text.RegularExpressions;
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
        private static readonly string[] OverlayPrefabPaths =
        {
            "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/SimulateMouseInput/SimulateMouseInputOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/SimulateKeyboard/SimulateKeyboardOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/SimulateMouseUi/SimulateMouseUiOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/RecordInput/RecordInputOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/ReplayInput/ReplayInputOverlay.prefab"
        };

        private static readonly string[] OverlayPrefabFilePaths =
        {
            "Packages/src/Runtime/Common/InputVisualizationCanvas.prefab",
            "Packages/src/Runtime/SimulateMouseInput/SimulateMouseInputOverlay.prefab",
            "Packages/src/Runtime/SimulateKeyboard/SimulateKeyboardOverlay.prefab",
            "Packages/src/Runtime/SimulateMouseUi/SimulateMouseUiOverlay.prefab",
            "Packages/src/Runtime/RecordInput/RecordInputOverlay.prefab",
            "Packages/src/Runtime/ReplayInput/ReplayInputOverlay.prefab"
        };

        private static readonly string[] EditorOnlyOverlayComponentSourcePaths =
        {
            "Packages/src/Runtime/Common/InputVisualizationCanvas.cs",
            "Packages/src/Runtime/SimulateMouseInput/SimulateMouseInputOverlay.cs",
            "Packages/src/Runtime/SimulateKeyboard/SimulateKeyboardOverlay.cs",
            "Packages/src/Runtime/SimulateMouseUi/SimulateMouseUiOverlay.cs",
            "Packages/src/Runtime/RecordInput/RecordInputOverlayPresenter.cs",
            "Packages/src/Runtime/RecordInput/RecordInputOverlayView.cs",
            "Packages/src/Runtime/ReplayInput/ReplayInputOverlay.cs"
        };

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

                Assert.That(canvas, Is.Not.Null);
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
                Assert.That(recordView, Is.Not.Null);
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

        [Test]
        public void InputVisualizationPrefabs_WhenLoaded_HaveNoMissingScripts()
        {
            // Verifies that package Overlay prefabs do not emit missing-script warnings when instantiated.
            for (int pathIndex = 0; pathIndex < OverlayPrefabPaths.Length; pathIndex++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPrefabPaths[pathIndex]);

                Assert.That(prefab, Is.Not.Null, OverlayPrefabPaths[pathIndex]);
                AssertNoMissingScripts(prefab, OverlayPrefabPaths[pathIndex]);

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    AssertNoMissingScripts(instance, OverlayPrefabPaths[pathIndex]);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void InputVisualizationPrefabs_WhenLoaded_AreEditorOnly()
        {
            // Verifies that package Overlay prefabs are excluded from Player builds by Unity's EditorOnly tag.
            for (int pathIndex = 0; pathIndex < OverlayPrefabPaths.Length; pathIndex++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPrefabPaths[pathIndex]);

                Assert.That(prefab, Is.Not.Null, OverlayPrefabPaths[pathIndex]);
                AssertEditorOnlyTags(prefab, OverlayPrefabPaths[pathIndex]);
            }
        }

        [Test]
        public void InputVisualizationPrefabs_WhenScanned_DoNotReferenceProjectScripts()
        {
            // Verifies that package Overlay prefabs do not depend on scripts outside the package.
            for (int pathIndex = 0; pathIndex < OverlayPrefabFilePaths.Length; pathIndex++)
            {
                string contents = ReadText(OverlayPrefabFilePaths[pathIndex]);
                MatchCollection matches = Regex.Matches(contents, @"m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-f]{32}),");

                foreach (Match match in matches)
                {
                    string guid = match.Groups[1].Value;
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    Assert.That(
                        assetPath,
                        Is.Not.Empty,
                        $"{OverlayPrefabFilePaths[pathIndex]} contains unresolved script GUID {guid}");

                    Assert.That(
                        assetPath,
                        Does.Not.StartWith("Assets/"),
                        $"{OverlayPrefabFilePaths[pathIndex]} references project script GUID {guid} at {assetPath}");
                }
            }
        }

        [Test]
        public void InputVisualizationOverlayComponents_WhenScanned_AreEditorOnly()
        {
            // Verifies that prefab-attached overlay components cannot compile into Player assemblies.
            for (int pathIndex = 0; pathIndex < EditorOnlyOverlayComponentSourcePaths.Length; pathIndex++)
            {
                string contents = ReadText(EditorOnlyOverlayComponentSourcePaths[pathIndex]);

                Assert.That(
                    contents.TrimStart(),
                    Does.StartWith("#if UNITY_EDITOR"),
                    EditorOnlyOverlayComponentSourcePaths[pathIndex]);
            }
        }

        private static void AssertEditorOnlyTags(GameObject root, string prefabPath)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject gameObject = transforms[transformIndex].gameObject;

                Assert.That(
                    gameObject.CompareTag("EditorOnly"),
                    Is.True,
                    $"{prefabPath} has non-EditorOnly tag on {gameObject.name}");
            }
        }

        private static void AssertSerializedReference(UnityEngine.Object target, string propertyName)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.objectReferenceValue, Is.Not.Null, propertyName);
        }

        private static void AssertNoMissingScripts(GameObject root, string prefabPath)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                GameObject gameObject = transforms[transformIndex].gameObject;
                int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);

                Assert.That(
                    missingScriptCount,
                    Is.EqualTo(0),
                    $"{prefabPath} has {missingScriptCount} missing script component(s) on {gameObject.name}");
            }
        }

        private static string ReadText(string relativePath)
        {
            string absolutePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), relativePath);

            return File.ReadAllText(absolutePath);
        }
    }
}
