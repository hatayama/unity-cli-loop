using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies captured-variable formatting: scope ordering, UnityEngine.Object classification,
    /// async/iterator hoisted-local demangling, and value/count truncation.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointVariableFormatterTests
    {
        private GameObject _testGameObject;
        private ScriptableObject _testScriptableObject;

        [TearDown]
        public void TearDown()
        {
            if (_testGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_testGameObject);
                _testGameObject = null;
            }

            if (_testScriptableObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_testScriptableObject);
                _testScriptableObject = null;
            }
        }

        [Test]
        public void Format_WithLocalsAndParameters_OrdersLocalsBeforeParameters()
        {
            // Verifies locals are reported before parameters, matching the response ordering.
            object[] locals = { "speed", 5 };
            object[] parameters = { "damage", 3 };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, parameters, locals);

            Assert.That(variables.Select(v => v.Name), Is.EqualTo(new[] { "speed", "damage" }));
            Assert.That(variables[0].Scope, Is.EqualTo(UloopCapturedVariableScope.Local));
            Assert.That(variables[0].Value, Is.EqualTo("5"));
            Assert.That(variables[1].Scope, Is.EqualTo(UloopCapturedVariableScope.Parameter));
            Assert.That(variables[1].Value, Is.EqualTo("3"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WhenValueIsNull_ReportsNullWithoutUnityObjectFields()
        {
            // Verifies a real C# null reference reports "null" with no UnityObject classification.
            object[] locals = { "target", null };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            UloopCapturedVariable variable = variables.Single();
            Assert.That(variable.Value, Is.EqualTo("null"));
            Assert.That(variable.UnityObjectKind, Is.Empty);
        }

        [Test]
        public void Format_WhenValueToStringThrows_ReturnsSafeToStringSentinel()
        {
            // Verifies the sanctioned SafeToString try-catch reports a sentinel instead of throwing.
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: boom");
            object[] locals = { "broken", new ThrowingToString() };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("(toString threw InvalidOperationException)"));
        }

        [Test]
        public void Format_WhenValueExceedsMaxLength_TruncatesValueAndSetsTruncatedFlag()
        {
            // Verifies an over-long value is clipped to the configured cap and reports truncation.
            string longValue = new string('a', SourcePausePointConstants.MaxCapturedVariableValueLength + 10);
            object[] locals = { "text", longValue };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value.Length, Is.EqualTo(SourcePausePointConstants.MaxCapturedVariableValueLength));
            Assert.That(truncated, Is.True);
        }

        [Test]
        public void Format_WhenOneValueExceedsMaxLength_StillCapturesSubsequentVariables()
        {
            // Regression test: an over-long value must only clip itself, never abort capture of
            // the parameters, the synthetic "this" entry, and instance fields that come after it
            // in the same call.
            string longValue = new string('a', SourcePausePointConstants.MaxCapturedVariableValueLength + 10);
            object[] locals = { "longText", longValue };
            object[] parameters = { "hp", 42 };
            InstanceFieldFixture instance = new() { PublicField = 5 };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                instance, parameters, locals);

            Assert.That(variables.Select(v => v.Name), Is.EqualTo(new[] { "longText", "hp", "this", "PublicField", "Prop" }));
            Assert.That(variables.Single(v => v.Name == "hp").Value, Is.EqualTo("42"));
            Assert.That(variables.Single(v => v.Name == "PublicField").Value, Is.EqualTo("5"));
            Assert.That(truncated, Is.True);
        }

        [Test]
        public void Format_WhenVariableCountExceedsMax_StopsAtCapAndSetsTruncatedFlag()
        {
            // Verifies capture stops at MaxCapturedVariableCount rather than growing unbounded.
            int localCount = SourcePausePointConstants.MaxCapturedVariableCount + 10;
            object[] locals = new object[localCount * 2];
            for (int i = 0; i < localCount; i++)
            {
                locals[i * 2] = $"local{i}";
                locals[i * 2 + 1] = i;
            }

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Count, Is.EqualTo(SourcePausePointConstants.MaxCapturedVariableCount));
            Assert.That(truncated, Is.True);
        }

        [Test]
        public void Format_WithInstanceFields_CapturesThemAfterLocalsAndParameters()
        {
            // Verifies the synthetic "this" entry (Scope=This) lands after locals/parameters and
            // before instance fields (Scope=InstanceField), and an auto-property's
            // compiler-generated backing field ("<Prop>k__BackingField") is un-mangled and
            // captured under its property name.
            InstanceFieldFixture instance = new() { PublicField = 9 };
            instance.Prop = "hello";
            object[] locals = { "local", 1 };
            object[] parameters = { "param", 2 };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                instance, parameters, locals);

            Assert.That(variables.Select(v => v.Name), Is.EqualTo(new[] { "local", "param", "this", "PublicField", "Prop" }));
            Assert.That(variables[2].Scope, Is.EqualTo(UloopCapturedVariableScope.This));
            Assert.That(variables[3].Scope, Is.EqualTo(UloopCapturedVariableScope.InstanceField));
            Assert.That(variables[3].Value, Is.EqualTo("9"));
            Assert.That(variables[4].Scope, Is.EqualTo(UloopCapturedVariableScope.InstanceField));
            Assert.That(variables[4].Value, Is.EqualTo("hello"));
        }

        [Test]
        public void Format_WithHoistedAsyncLocalField_DemanglesFieldNameToLocalScope()
        {
            // Verifies Roslyn's hoisted "<name>5__N" state-machine field demangles to Scope=Local.
            AsyncStateMachineFixture fixture = new();
            (object stateMachine, Type stateMachineType) = CreateStateMachine(fixture);
            SetHoistedField(stateMachine, stateMachineType, "localValue", 42);

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                stateMachine, Array.Empty<object>(), Array.Empty<object>());

            UloopCapturedVariable variable = variables.Single(v => v.Name == "localValue");
            Assert.That(variable.Scope, Is.EqualTo(UloopCapturedVariableScope.Local));
            Assert.That(variable.Value, Is.EqualTo("42"));
        }

        [Test]
        public void Format_WithHoistedAsyncParameterField_ReportsItAsParameterScope()
        {
            // Verifies a hoisted parameter (stored under its plain source name, unlike locals)
            // reports Scope=Parameter rather than InstanceField, since it belongs to the
            // compiler-generated state machine type rather than the calling object's own class.
            AsyncStateMachineFixture fixture = new();
            (object stateMachine, _) = CreateStateMachine(fixture);

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                stateMachine, Array.Empty<object>(), Array.Empty<object>());

            UloopCapturedVariable variable = variables.Single(v => v.Name == "seed");
            Assert.That(variable.Scope, Is.EqualTo(UloopCapturedVariableScope.Parameter));
        }

        [Test]
        public void Format_WithStateMachineOuterThisField_FollowsItOneLevelDeep()
        {
            // Verifies "<>4__this" is followed exactly one level to surface the real instance's fields.
            AsyncStateMachineFixture fixture = new() { OuterField = 7 };
            (object stateMachine, Type stateMachineType) = CreateStateMachine(fixture);
            FieldInfo outerThisField = stateMachineType.GetField(
                "<>4__this", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(outerThisField, Is.Not.Null, "compiler must hoist <>4__this for this fixture");
            object boxedStateMachine = stateMachine;
            outerThisField.SetValue(boxedStateMachine, fixture);

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                boxedStateMachine, Array.Empty<object>(), Array.Empty<object>());

            UloopCapturedVariable variable = variables.Single(v => v.Name == "OuterField");
            Assert.That(variable.Scope, Is.EqualTo(UloopCapturedVariableScope.InstanceField));
            Assert.That(variable.Value, Is.EqualTo("7"));
        }

        [Test]
        public void Format_WithSceneGameObjectValue_ClassifiesAsSceneObject()
        {
            // Verifies a scene-attached GameObject classifies as SceneObject with a hierarchy path.
            _testGameObject = new GameObject("PausePointFormatterSceneFixture");
            object[] locals = { "target", _testGameObject };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            UloopCapturedVariable variable = variables.Single();
            Assert.That(variable.UnityObjectKind, Is.EqualTo(UloopCapturedVariableUnityObjectKind.SceneObject));
            Assert.That(variable.Value, Is.EqualTo("PausePointFormatterSceneFixture"));
            Assert.That(variable.UnityObjectPath, Does.Contain("PausePointFormatterSceneFixture"));
        }

        [Test]
        public void Format_WithSceneComponentValue_ClassifiesAsSceneObjectUsingComponentHandle()
        {
            // Verifies the Component branch (as opposed to GameObject) resolves its handle via
            // the component itself, not the owning GameObject.
            _testGameObject = new GameObject("PausePointFormatterComponentFixture");
            Transform componentValue = _testGameObject.transform;
            object[] locals = { "target", componentValue };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            UloopCapturedVariable variable = variables.Single();
            Assert.That(variable.UnityObjectKind, Is.EqualTo(UloopCapturedVariableUnityObjectKind.SceneObject));
            Assert.That(variable.UnityObjectPath, Does.Contain("PausePointFormatterComponentFixture"));
            Assert.That(variable.UnityObjectInstanceId, Is.EqualTo(componentValue.GetInstanceID()));
        }

        [Test]
        public void Format_WithPrefabAssetGameObjectValue_ClassifiesAsPrefabAsset()
        {
            // Verifies a GameObject loaded from a saved prefab asset (invalid scene, resolvable
            // asset path) classifies as PrefabAsset rather than SceneObject.
            const string prefabPath = "Assets/PausePointFormatterPrefabAssetFixture.prefab";
            GameObject source = new("PausePointFormatterPrefabAssetFixture");
            GameObject prefabAsset;
            try
            {
                prefabAsset = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            try
            {
                object[] locals = { "target", prefabAsset };

                (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                    null, Array.Empty<object>(), locals);

                UloopCapturedVariable variable = variables.Single();
                Assert.That(variable.UnityObjectKind, Is.EqualTo(UloopCapturedVariableUnityObjectKind.PrefabAsset));
                Assert.That(variable.UnityObjectPath, Is.EqualTo(prefabPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void Format_WithPersistedAssetValue_ClassifiesAsAsset()
        {
            // Verifies a non-GameObject persisted asset classifies as Asset with its asset path.
            // Loads this test project's own asmdef file rather than creating a throwaway asset.
            const string assetPath =
                "Assets/Tests/Editor/SourcePausePointCapture/UnityCLILoop.Tests.Editor.SourcePausePointCapture.asmdef";
            AssemblyDefinitionAsset asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(assetPath);
            Assert.That(asset, Is.Not.Null, "fixture asmdef asset must exist at the expected path");
            object[] locals = { "target", asset };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            UloopCapturedVariable variable = variables.Single();
            Assert.That(variable.UnityObjectKind, Is.EqualTo(UloopCapturedVariableUnityObjectKind.Asset));
            Assert.That(variable.UnityObjectPath, Is.EqualTo(assetPath));
        }

        [Test]
        public void Format_WithRuntimeOnlyScriptableObjectValue_ClassifiesAsRuntimeInstance()
        {
            // Verifies a ScriptableObject with no asset path classifies as RuntimeInstance.
            _testScriptableObject = ScriptableObject.CreateInstance<ScriptableObject>();
            object[] locals = { "target", _testScriptableObject };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().UnityObjectKind, Is.EqualTo(UloopCapturedVariableUnityObjectKind.RuntimeInstance));
        }

        [Test]
        public void Format_WithDestroyedUnityObjectValue_ClassifiesAsDestroyed()
        {
            // Verifies a destroyed (fake-null) UnityEngine.Object reports Destroyed, not real null.
            GameObject destroyed = new("PausePointFormatterDestroyedFixture");
            UnityEngine.Object.DestroyImmediate(destroyed);
            object[] locals = { "target", destroyed };

            (List<UloopCapturedVariable> variables, _) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            UloopCapturedVariable variable = variables.Single();
            Assert.That(variable.UnityObjectKind, Is.EqualTo(UloopCapturedVariableUnityObjectKind.Destroyed));
            Assert.That(variable.Value, Is.EqualTo("(destroyed)"));
        }

        [Test]
        public void Format_WithListOfIntegers_SerializesCollectionAsJsonArray()
        {
            // Verifies List<T> values preview as JSON instead of the default type-name ToString.
            object[] locals = { "scores", new List<int> { 1, 2, 3 } };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("[1,2,3]"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithStringDictionary_SerializesCollectionAsJsonObject()
        {
            // Verifies dictionary values preview as JSON objects with string keys.
            object[] locals =
            {
                "labels",
                new Dictionary<string, string> { { "hp", "100" }, { "mp", "50" } }
            };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("{\"hp\":\"100\",\"mp\":\"50\"}"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WhenCollectionElementCountExceedsPreviewCap_TruncatesElementsAndSetsFlag()
        {
            // Verifies only the first preview-cap elements are serialized and truncation is reported.
            List<int> values = Enumerable.Range(0, SourcePausePointConstants.MaxCollectionPreviewElementCount + 5).ToList();
            object[] locals = { "scores", values };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            string value = variables.Single().Value;
            Assert.That(value, Does.StartWith("["));
            Assert.That(value, Does.EndWith("]"));
            Assert.That(value.Split(',').Length, Is.EqualTo(SourcePausePointConstants.MaxCollectionPreviewElementCount));
            Assert.That(truncated, Is.True);
        }

        [Test]
        public void Format_WithCompositeCollectionElements_ExpandsElementFieldsAsJson()
        {
            // Verifies composite element types without a ToString override expand their fields as
            // JSON (via the same field-based preview custom types use), instead of falling back to
            // the default type-name ToString.
            object[] locals = { "items", new List<InstanceFieldFixture> { new() { PublicField = 7 } } };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("[{\"PublicField\":7,\"Prop\":null}]"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithCircularCollectionReference_DoesNotThrow()
        {
            // Verifies cyclic graphs degrade safely instead of crashing collection preview.
            List<object> outer = new();
            List<object> inner = new();
            outer.Add(inner);
            inner.Add(outer);
            object[] locals = { "graph", outer };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Does.Contain("(circular)"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithStringValue_KeepsPlainStringPreview()
        {
            // Verifies string is excluded from IEnumerable JSON preview and keeps the raw value.
            object[] locals = { "label", "ready" };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("ready"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WhenCollectionPreviewExceedsMaxLength_TruncatesValueAndSetsTruncatedFlag()
        {
            // Verifies expanded collection JSON uses the larger preview cap before clipping.
            List<string> values = Enumerable.Range(0, SourcePausePointConstants.MaxCollectionPreviewElementCount)
                .Select(_ => new string('x', 120))
                .ToList();
            object[] locals = { "chunks", values };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value.Length, Is.EqualTo(SourcePausePointConstants.MaxCollectionPreviewValueLength));
            Assert.That(truncated, Is.True);
        }

        [Test]
        public void Format_WithDeferredLinqQuery_FallsBackToToStringInsteadOfJson()
        {
            // Verifies deferred IEnumerable/LINQ is not executed for JSON preview.
            IEnumerable<int> query = Enumerable.Range(1, 3).Select(static value => value);
            object[] locals = { "query", query };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo(query.ToString()));
            Assert.That(variables.Single().Value, Does.Not.StartWith("["));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WhenMaterializedCollectionEnumerationThrows_FallsBackToToString()
        {
            // Verifies enumeration exceptions during preview do not escape into user game code.
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: enum boom");
            ThrowingOnEnumerateCollection collection = new();
            object[] locals = { "broken", collection };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo(collection.ToString()));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithPlainCustomTypeWithoutToStringOverride_PreviewsFieldsAsJson()
        {
            // Verifies a custom class without a ToString override previews its fields as JSON
            // instead of the default type-name ToString.
            object[] locals = { "stats", new PlainCustomType { Score = 42, Label = "gold" } };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("{\"Score\":42,\"Label\":\"gold\"}"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithCustomStructWithoutToStringOverride_PreviewsFieldsAsJson()
        {
            // Verifies value types without a ToString override also preview as JSON fields, not
            // just reference types.
            object[] locals = { "point", new PlainCustomStruct { X = 1, Y = 2 } };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("{\"X\":1,\"Y\":2}"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithCustomTypeOverridingToString_KeepsToStringResult()
        {
            // Verifies a custom type that overrides ToString keeps using ToString instead of the
            // new field-based JSON preview.
            object[] locals = { "stats", new CustomTypeWithToStringOverride { Score = 42 } };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("Score=42"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithNestedCustomTypeBeyondPreviewDepth_TruncatesInnermostLevelToToString()
        {
            // Verifies the field-based JSON preview reuses the existing depth cap: nesting beyond
            // MaxCollectionPreviewDepth degrades the innermost level to ToString instead of
            // expanding further.
            NestingOuterType graph = new()
            {
                Middle = new NestingMiddleType { Inner = new NestingInnerType { Value = 9 } }
            };
            object[] locals = { "graph", graph };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Does.Contain(nameof(NestingInnerType)));
            Assert.That(variables.Single().Value, Does.Not.Contain("\"Value\":9"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithShadowedFieldName_PrefersDerivedFieldOverBaseField()
        {
            // Verifies a derived class field that shadows a same-named base class field ("new"
            // hiding) previews the derived (runtime-visible) value, not the base class's, since
            // field enumeration walks derived-to-base and the derived name must win.
            object[] locals = { "entity", new ShadowingDerivedType { Score = 2 } };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Is.EqualTo("{\"Score\":2}"));
            Assert.That(truncated, Is.False);
        }

        [Test]
        public void Format_WithSelfReferencingCustomType_DoesNotThrow()
        {
            // Verifies the field-based JSON preview reuses the existing circular-reference guard.
            SelfReferencingType instance = new();
            instance.Self = instance;
            object[] locals = { "node", instance };

            (List<UloopCapturedVariable> variables, bool truncated) = SourcePausePointVariableFormatter.Format(
                null, Array.Empty<object>(), locals);

            Assert.That(variables.Single().Value, Does.Contain("(circular)"));
            Assert.That(truncated, Is.False);
        }

        [UnityTest]
        public IEnumerator Format_WhenCalledOffMainThread_DegradesUnityObjectValueWithoutEngineApiAccess()
        {
            // Verifies UnityEngine.Object values degrade to a placeholder off the main thread,
            // since Transform/AssetDatabase/InstanceID access is unsafe there. Uses the same
            // background-Task-plus-polling shape as MainThreadSwitcherTests to avoid blocking waits.
            _testGameObject = new GameObject("PausePointFormatterOffThreadFixture");
            object[] locals = { "target", _testGameObject };
            List<UloopCapturedVariable> capturedVariables = null;
            bool completed = false;

            Task.Run(() =>
            {
                (capturedVariables, _) = SourcePausePointVariableFormatter.Format(null, Array.Empty<object>(), locals);
                completed = true;
            });

            float timeoutTime = Time.realtimeSinceStartup + 5f;
            while (!completed && Time.realtimeSinceStartup < timeoutTime)
            {
                yield return null;
            }

            Assert.That(completed, Is.True, "background formatting should complete within timeout");
            UloopCapturedVariable variable = capturedVariables.Single();
            Assert.That(variable.Value, Is.EqualTo("(captured off main thread)"));
            Assert.That(variable.UnityObjectKind, Is.Empty);
        }

        private static (object StateMachine, Type StateMachineType) CreateStateMachine(AsyncStateMachineFixture fixture)
        {
            Type stateMachineType = typeof(AsyncStateMachineFixture)
                .GetNestedTypes(BindingFlags.NonPublic)
                .Single(t => t.Name.StartsWith("<RunAsync>d__", StringComparison.Ordinal));
            object stateMachine = Activator.CreateInstance(stateMachineType);
            return (stateMachine, stateMachineType);
        }

        private static void SetHoistedField(object stateMachine, Type stateMachineType, string localName, object value)
        {
            FieldInfo field = stateMachineType
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(f => System.Text.RegularExpressions.Regex.IsMatch(f.Name, $@"^<{localName}>5__\d+$"));
            field.SetValue(stateMachine, value);
        }

        private sealed class ThrowingToString
        {
            public override string ToString()
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class InstanceFieldFixture
        {
            public int PublicField;
            public string Prop { get; set; }
        }

        private sealed class AsyncStateMachineFixture
        {
            public int OuterField;

            public async Task<int> RunAsync(int seed)
            {
                int localValue = seed * 2;
                await Task.Yield();
                OuterField += localValue;
                return localValue;
            }
        }

        private sealed class PlainCustomType
        {
            public int Score;
            public string Label;
        }

        private struct PlainCustomStruct
        {
            public int X;
            public int Y;
        }

        private sealed class CustomTypeWithToStringOverride
        {
            public int Score;

            public override string ToString()
            {
                return $"Score={Score}";
            }
        }

        private sealed class NestingOuterType
        {
            public NestingMiddleType Middle;
        }

        private sealed class NestingMiddleType
        {
            public NestingInnerType Inner;
        }

        private sealed class NestingInnerType
        {
            public int Value;
        }

        private sealed class SelfReferencingType
        {
            public SelfReferencingType Self;
        }

        private class ShadowingBaseType
        {
            public int Score;
        }

        private sealed class ShadowingDerivedType : ShadowingBaseType
        {
            public new int Score;
        }

        private sealed class ThrowingOnEnumerateCollection : ICollection
        {
            public int Count => 3;
            public bool IsSynchronized => false;
            public object SyncRoot => this;

            public void CopyTo(Array array, int index)
            {
            }

            public IEnumerator GetEnumerator()
            {
                throw new InvalidOperationException("enum boom");
            }
        }
    }
}
