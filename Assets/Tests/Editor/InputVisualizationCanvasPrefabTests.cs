#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Verifies that the shared input visualization prefab keeps attachable overlay components.
    /// </summary>
    public sealed class InputVisualizationCanvasPrefabTests
    {
        private const string PrefabPath =
            "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab";
        private const string RuntimeAssemblyDefinitionPath =
            "Packages/src/Runtime/uLoopMCP.Runtime.asmdef";
        private const string SimulateMouseInputOverlayPrefabPath =
            "Packages/src/Runtime/SimulateMouseInput/SimulateMouseInputOverlay.prefab";
        private const string DemoMouseInputOverlayTesterMetaPath =
            "Assets/Tests/Demo/DemoMouseInputOverlayTester.cs.meta";

        [Test]
        public void RuntimeAssemblyDefinition_WhenScanned_IsPrefabAttachableAndNotAutoReferenced()
        {
            // The runtime assembly must stay attachable for prefab scripts while avoiding implicit user assembly references.
            JObject asmdef = JObject.Parse(ReadProjectText(RuntimeAssemblyDefinitionPath));
            JToken? includePlatformsToken = asmdef["includePlatforms"];

            Assert.That(includePlatformsToken, Is.Not.Null);
            Assert.That(includePlatformsToken!.Type, Is.EqualTo(JTokenType.Array));
            Assert.That(includePlatformsToken.HasValues, Is.False);
            Assert.That(asmdef["autoReferenced"]?.Value<bool>(), Is.False);
        }

        [Test]
        public void InputVisualizationCanvasPrefab_WhenInstantiated_HasOverlayReferences()
        {
            // Instantiation proves Unity can resolve the prefab scripts after import, not only in raw YAML.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);

            UnityEngine.Object instantiatedObject = PrefabUtility.InstantiatePrefab(prefab);
            Assert.That(instantiatedObject, Is.TypeOf<GameObject>());

            GameObject instance = (GameObject)instantiatedObject;
            try
            {
                InputVisualizationCanvas canvas = instance.GetComponent<InputVisualizationCanvas>();

                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.KeyboardOverlay, Is.Not.Null);
                Assert.That(canvas.MouseUiOverlay, Is.Not.Null);
                Assert.That(canvas.MouseInputOverlay, Is.Not.Null);
                Assert.That(canvas.RecordInputOverlayPresenter, Is.Not.Null);
                Assert.That(canvas.ReplayInputOverlay, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SimulateMouseInputOverlayPrefab_WhenSerialized_DoesNotReferenceDemoTester()
        {
            // Package prefabs cannot reference demo/test scripts because consumers import only Packages/src.
            string prefabText = ReadProjectText(SimulateMouseInputOverlayPrefabPath);
            string demoTesterGuid = ReadMetaGuid(DemoMouseInputOverlayTesterMetaPath);

            Assert.That(prefabText, Does.Not.Contain(demoTesterGuid));
        }

        private static string ReadProjectText(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.Combine(projectRoot, relativePath);

            return File.ReadAllText(absolutePath);
        }

        private static string ReadMetaGuid(string relativePath)
        {
            string[] lines = ReadProjectText(relativePath).Split('\n');
            const string Prefix = "guid: ";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    return line.Substring(Prefix.Length).Trim();
                }
            }

            Assert.Fail("Meta file must contain a guid line");
            return string.Empty;
        }
    }
}
