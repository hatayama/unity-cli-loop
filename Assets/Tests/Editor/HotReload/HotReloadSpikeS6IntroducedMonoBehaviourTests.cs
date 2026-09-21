using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using Assembly = System.Reflection.Assembly;
using Object = UnityEngine.Object;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S6 for hot reload: measures what the Unity runtime allows for a MonoBehaviour type
    /// that lives in an assembly the domain loaded from bytes, which is the shape every introduced
    /// type has. Hot reload refuses such a type today with "Unity object introduced type requires
    /// a compile", and no design exists yet, so these tests pin what the Editor actually does
    /// rather than what the refusal implies. Every expectation here was first measured and then
    /// written down; each test fails if the opposite behaviour starts happening.
    /// </summary>
    public class HotReloadSpikeS6IntroducedMonoBehaviourTests
    {
        private const string BehaviourTypeMetadataName = "SpikeS6.SpikeIntroducedBehaviour";

        private const string AlwaysBehaviourTypeMetadataName = "SpikeS6.SpikeIntroducedAlwaysBehaviour";

        private const string ScriptableTypeMetadataName = "SpikeS6.SpikeIntroducedAsset";

        // Why two behaviours that differ only in ExecuteAlways: a message count measured on one of
        // them alone cannot tell "edit mode delivers nothing to a byte-loaded type" apart from
        // "edit mode delivers nothing to a type that did not ask for it". The pair separates them.
        private const string IntroducedSource = @"using System.Collections;
using UnityEngine;

namespace SpikeS6
{
    public sealed class SpikeIntroducedBehaviour : MonoBehaviour
    {
        public static int AwakeCount;
        public static int OnEnableCount;
        public static int OnDisableCount;
        public static int OnDestroyCount;
        // Start and Update only arrive in play mode, which these tests never enter. The counters
        // and the two methods exist so the play-mode measurement can reuse this very artifact.
        public static int StartCount;
        public static int UpdateCount;

        [SerializeField] private int serializedValue;
        public int publicValue;
        public int coroutineSteps;

        public int SerializedValue
        {
            get { return serializedValue; }
            set { serializedValue = value; }
        }

        private void Awake() { AwakeCount++; }
        private void OnEnable() { OnEnableCount++; }
        private void Start() { StartCount++; }
        private void Update() { UpdateCount++; }
        private void OnDisable() { OnDisableCount++; }
        private void OnDestroy() { OnDestroyCount++; }

        public IEnumerator Count()
        {
            coroutineSteps++;
            yield return null;
            coroutineSteps++;
        }
    }

    [ExecuteAlways]
    public sealed class SpikeIntroducedAlwaysBehaviour : MonoBehaviour
    {
        public static int AwakeCount;
        public static int OnEnableCount;
        public static int OnDisableCount;
        public static int OnDestroyCount;
        public static int StartCount;
        public static int UpdateCount;

        private void Awake() { AwakeCount++; }
        private void OnEnable() { OnEnableCount++; }
        private void Start() { StartCount++; }
        private void Update() { UpdateCount++; }
        private void OnDisable() { OnDisableCount++; }
        private void OnDestroy() { OnDestroyCount++; }
    }

    public sealed class SpikeIntroducedAsset : ScriptableObject
    {
        [SerializeField] private int serializedValue;

        public int SerializedValue
        {
            get { return serializedValue; }
            set { serializedValue = value; }
        }
    }
}
";

        // Why cached and static: compiling the snippet with the external compiler dominates the
        // runtime of this suite, and every test wants the same load. The cache lives no longer
        // than the domain, which is also the lifetime of the loaded assembly it describes.
        private static Task<LoadedArtifact> firstArtifactTask;

        private static Task<LoadedArtifact> secondArtifactTask;

        private readonly List<GameObject> createdGameObjects = new();

        private readonly List<Object> createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object createdObject in createdObjects)
            {
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }

            foreach (GameObject createdGameObject in createdGameObjects)
            {
                if (createdGameObject != null)
                {
                    Object.DestroyImmediate(createdGameObject);
                }
            }

            createdObjects.Clear();
            createdGameObjects.Clear();
        }

        /// <summary>What: Q1 - AddComponent accepts a MonoBehaviour type from an assembly the
        /// domain loaded from bytes, returns a live component of that exact type, and logs
        /// nothing.</summary>
        [Test]
        public async Task Q1_AddComponent_AcceptsByteLoadedMonoBehaviour()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            GameObject host = NewGameObject("SpikeS6Q1Host");

            List<string> logs = new();
            Component component = CaptureLogs(logs, () => host.AddComponent(artifact.BehaviourType));

            Assert.That(component, Is.Not.Null, "AddComponent returned a destroyed or null component.");
            Assert.That(component.GetType(), Is.SameAs(artifact.BehaviourType));
            Assert.That(logs, Is.Empty, "AddComponent logged: " + string.Join(" | ", logs));
        }

        /// <summary>What: Q1 - ScriptableObject.CreateInstance accepts a ScriptableObject type from
        /// the same byte-loaded assembly and logs nothing.</summary>
        [Test]
        public async Task Q1_CreateInstance_AcceptsByteLoadedScriptableObject()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();

            List<string> logs = new();
            ScriptableObject asset = CaptureLogs(
                logs,
                () => ScriptableObject.CreateInstance(artifact.ScriptableType));
            if (asset != null)
            {
                createdObjects.Add(asset);
            }

            Assert.That(asset, Is.Not.Null, "CreateInstance returned a destroyed or null object.");
            Assert.That(asset.GetType(), Is.SameAs(artifact.ScriptableType));
            Assert.That(logs, Is.Empty, "CreateInstance logged: " + string.Join(" | ", logs));
        }

        /// <summary>What: Q2 - Unity itself delivers Awake, OnEnable, OnDisable and OnDestroy to a
        /// byte-loaded MonoBehaviour that asks for edit-mode execution, so native message dispatch
        /// reaches a type with no script asset behind it.</summary>
        [Test]
        public async Task Q2_ExecuteAlwaysIntroducedType_ReceivesLifecycleMessagesInEditMode()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            Type behaviourType = artifact.AlwaysBehaviourType;
            GameObject host = NewGameObject("SpikeS6Q2Always");

            MessageDeltas deltas = MeasureLifecycle(host, behaviourType);

            Assert.That(deltas.AwakeOnAdd, Is.EqualTo(1), "Awake was not delivered on AddComponent.");
            Assert.That(deltas.EnableOnAdd, Is.EqualTo(1), "OnEnable was not delivered on AddComponent.");
            Assert.That(deltas.DisableOnDisableFlag, Is.EqualTo(1), "OnDisable was not delivered on enabled=false.");
            Assert.That(deltas.EnableOnEnableFlag, Is.EqualTo(1), "OnEnable was not delivered on enabled=true.");
            Assert.That(deltas.DisableOnDestroy, Is.EqualTo(1), "OnDisable was not delivered on destroy.");
            Assert.That(deltas.DestroyOnDestroy, Is.EqualTo(1), "OnDestroy was not delivered on destroy.");
        }

        /// <summary>What: Q2 - the same suite's byte-loaded MonoBehaviour that does not ask for
        /// edit-mode execution receives none of those messages in edit mode, so the delivery above
        /// follows the attribute rather than the assembly having come from bytes.</summary>
        [Test]
        public async Task Q2_PlainIntroducedType_ReceivesNoLifecycleMessagesInEditMode()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            Type behaviourType = artifact.BehaviourType;
            GameObject host = NewGameObject("SpikeS6Q2Plain");

            MessageDeltas deltas = MeasureLifecycle(host, behaviourType);

            Assert.That(deltas.AwakeOnAdd, Is.Zero, "Awake was delivered in edit mode.");
            Assert.That(deltas.EnableOnAdd, Is.Zero, "OnEnable was delivered in edit mode.");
            Assert.That(deltas.DisableOnDisableFlag, Is.Zero, "OnDisable was delivered in edit mode.");
            Assert.That(deltas.EnableOnEnableFlag, Is.Zero, "OnEnable was delivered in edit mode.");
            Assert.That(deltas.DisableOnDestroy, Is.Zero, "OnDisable was delivered in edit mode.");
            Assert.That(deltas.DestroyOnDestroy, Is.Zero, "OnDestroy was delivered in edit mode.");
        }

        /// <summary>What: Q3 - the added component is a MonoBehaviour that the normal component
        /// lookups find, both by its own type and in the generic MonoBehaviour list.</summary>
        [Test]
        public async Task Q3_IntroducedComponent_IsFoundByComponentLookups()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            GameObject host = NewGameObject("SpikeS6Q3Lookup");
            Component component = host.AddComponent(artifact.BehaviourType);

            Assert.That(component, Is.InstanceOf<MonoBehaviour>());
            Assert.That(host.GetComponent(artifact.BehaviourType), Is.SameAs(component));
            Assert.That(host.GetComponents<MonoBehaviour>(), Is.EqualTo(new[] { component }));
        }

        /// <summary>What: Q3 - StartCoroutine accepts a coroutine declared on the byte-loaded type
        /// and runs it up to the first yield synchronously; edit mode does not advance it further,
        /// so the steps past the yield are not observable here.</summary>
        [Test]
        public async Task Q3_StartCoroutine_RunsUntilTheFirstYieldInEditMode()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            GameObject host = NewGameObject("SpikeS6Q3Coroutine");
            MonoBehaviour behaviour = (MonoBehaviour)host.AddComponent(artifact.BehaviourType);
            MethodInfo countMethod = artifact.BehaviourType.GetMethod("Count");
            IEnumerator routine = (IEnumerator)countMethod.Invoke(behaviour, Array.Empty<object>());

            Coroutine coroutine = behaviour.StartCoroutine(routine);

            Assert.That(coroutine, Is.Not.Null, "StartCoroutine returned nothing.");
            Assert.That(
                ReadInstanceField(artifact.BehaviourType, behaviour, "coroutineSteps"),
                Is.EqualTo(1),
                "The coroutine body did not run up to its first yield.");
        }

        /// <summary>What: Q3 - MonoScript.FromMonoBehaviour returns a script object for the
        /// byte-loaded component, but one with no asset behind it: no name, no asset path and no
        /// source text, while GetClass still resolves to the introduced type.</summary>
        [Test]
        public async Task Q3_MonoScript_ExistsWithoutAnAssetBehindIt()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            GameObject host = NewGameObject("SpikeS6Q3MonoScript");
            MonoBehaviour behaviour = (MonoBehaviour)host.AddComponent(artifact.BehaviourType);

            MonoScript monoScript = MonoScript.FromMonoBehaviour(behaviour);

            Assert.That(monoScript, Is.Not.Null, "No MonoScript was produced for the component.");
            Assert.That(monoScript.GetClass(), Is.SameAs(artifact.BehaviourType));
            Assert.That(monoScript.name, Is.Empty);
            Assert.That(AssetDatabase.GetAssetPath(monoScript), Is.Empty);
            Assert.That(monoScript.text, Is.Empty);
        }

        /// <summary>What: Q4 - instantiating the host object clones the byte-loaded component and
        /// carries both the serialized field and the public field across, so serialization of such
        /// a component is not lost for lack of a script asset.</summary>
        [Test]
        public async Task Q4_Instantiate_ClonesTheComponentWithItsFieldValues()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            GameObject host = NewGameObject("SpikeS6Q4Source");
            Component component = host.AddComponent(artifact.BehaviourType);
            PropertyInfo serializedValue = artifact.BehaviourType.GetProperty("SerializedValue");
            FieldInfo publicValue = artifact.BehaviourType.GetField("publicValue");
            serializedValue.SetValue(component, 41);
            publicValue.SetValue(component, 42);

            GameObject clone = Object.Instantiate(host);
            createdGameObjects.Add(clone);

            Component cloneComponent = clone.GetComponent(artifact.BehaviourType);
            Assert.That(cloneComponent, Is.Not.Null, "The clone did not carry the component.");
            Assert.That(cloneComponent, Is.Not.SameAs(component));
            Assert.That(serializedValue.GetValue(cloneComponent), Is.EqualTo(41));
            Assert.That(publicValue.GetValue(cloneComponent), Is.EqualTo(42));
        }

        /// <summary>What: Q4 - JsonUtility serializes the byte-loaded component's serialized and
        /// public fields, which is the same shape Unity's own serializer sees.</summary>
        [Test]
        public async Task Q4_JsonUtility_SerializesTheIntroducedComponentFields()
        {
            LoadedArtifact artifact = await FirstArtifactAsync();
            GameObject host = NewGameObject("SpikeS6Q4Json");
            Component component = host.AddComponent(artifact.BehaviourType);
            artifact.BehaviourType.GetProperty("SerializedValue").SetValue(component, 7);
            artifact.BehaviourType.GetField("publicValue").SetValue(component, 8);

            string json = JsonUtility.ToJson(component);

            Assert.That(json, Is.EqualTo("{\"serializedValue\":7,\"publicValue\":8,\"coroutineSteps\":0}"));
        }

        /// <summary>What: Q5 - two artifact assemblies declaring the same fully qualified type name
        /// produce two distinct runtime types, both components can sit on one object at once, and
        /// GetComponent keeps them apart by type rather than by name.</summary>
        [Test]
        public async Task Q5_TwoGenerationsOfTheSameTypeName_CoexistAndStayDistinct()
        {
            LoadedArtifact first = await FirstArtifactAsync();
            LoadedArtifact second = await SecondArtifactAsync();
            Assert.That(second.BehaviourType.FullName, Is.EqualTo(first.BehaviourType.FullName));
            Assert.That(second.BehaviourType, Is.Not.SameAs(first.BehaviourType));
            Assert.That(
                second.BehaviourType.Assembly.GetName().Name,
                Is.Not.EqualTo(first.BehaviourType.Assembly.GetName().Name));

            GameObject host = NewGameObject("SpikeS6Q5Host");
            Component firstComponent = host.AddComponent(first.BehaviourType);
            Component secondComponent = host.AddComponent(second.BehaviourType);

            Assert.That(secondComponent, Is.Not.Null, "The second generation could not be added.");
            Assert.That(secondComponent, Is.Not.SameAs(firstComponent));
            Assert.That(host.GetComponent(first.BehaviourType), Is.SameAs(firstComponent));
            Assert.That(host.GetComponent(second.BehaviourType), Is.SameAs(secondComponent));
            Assert.That(host.GetComponents<MonoBehaviour>().Length, Is.EqualTo(2));
        }

        /// <summary>
        /// Adds the component, drives the enabled flag both ways and destroys it, reporting how
        /// many times each lifecycle message arrived at every step. Deltas rather than absolute
        /// counts because the counters live in the artifact assembly and outlive one test.
        /// </summary>
        private MessageDeltas MeasureLifecycle(GameObject host, Type behaviourType)
        {
            int awakeBefore = ReadStaticCounter(behaviourType, "AwakeCount");
            int enableBefore = ReadStaticCounter(behaviourType, "OnEnableCount");
            MonoBehaviour behaviour = (MonoBehaviour)host.AddComponent(behaviourType);
            int awakeOnAdd = ReadStaticCounter(behaviourType, "AwakeCount") - awakeBefore;
            int enableOnAdd = ReadStaticCounter(behaviourType, "OnEnableCount") - enableBefore;

            int disableBefore = ReadStaticCounter(behaviourType, "OnDisableCount");
            behaviour.enabled = false;
            int disableOnFlag = ReadStaticCounter(behaviourType, "OnDisableCount") - disableBefore;

            int enableAgainBefore = ReadStaticCounter(behaviourType, "OnEnableCount");
            behaviour.enabled = true;
            int enableOnFlag = ReadStaticCounter(behaviourType, "OnEnableCount") - enableAgainBefore;

            int disableOnDestroyBefore = ReadStaticCounter(behaviourType, "OnDisableCount");
            int destroyBefore = ReadStaticCounter(behaviourType, "OnDestroyCount");
            Object.DestroyImmediate(behaviour);
            return new MessageDeltas(
                awakeOnAdd,
                enableOnAdd,
                disableOnFlag,
                enableOnFlag,
                ReadStaticCounter(behaviourType, "OnDisableCount") - disableOnDestroyBefore,
                ReadStaticCounter(behaviourType, "OnDestroyCount") - destroyBefore);
        }

        private static T CaptureLogs<T>(List<string> logs, Func<T> action)
        {
            void Handler(string condition, string stackTrace, LogType type)
            {
                logs.Add(type + ": " + condition);
            }

            Application.logMessageReceived += Handler;
            try
            {
                return action();
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }
        }

        private GameObject NewGameObject(string name)
        {
            GameObject gameObject = new(name);
            createdGameObjects.Add(gameObject);
            return gameObject;
        }

        private static int ReadStaticCounter(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "The artifact type has no counter named " + fieldName + ".");
            return (int)field.GetValue(null);
        }

        private static int ReadInstanceField(Type type, object instance, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName);
            Assert.That(field, Is.Not.Null, "The artifact type has no field named " + fieldName + ".");
            return (int)field.GetValue(instance);
        }

        private static Task<LoadedArtifact> FirstArtifactAsync()
        {
            return firstArtifactTask ??= CompileAndLoadArtifactAsync("SpikeS6Artifact_Gen1");
        }

        private static Task<LoadedArtifact> SecondArtifactAsync()
        {
            return secondArtifactTask ??= CompileAndLoadArtifactAsync("SpikeS6Artifact_Gen2");
        }

        /// <summary>
        /// Compiles the snippet with the external Roslyn compiler and loads it the way production
        /// loads an introduced type's artifact: two-argument Assembly.Load over the dll and pdb
        /// bytes. The S4 helper is not reused because its reference list is private and fixed to
        /// the core library; deriving from MonoBehaviour needs the engine references as well.
        /// </summary>
        private static async Task<LoadedArtifact> CompileAndLoadArtifactAsync(string workSubdirectoryName)
        {
            string workRootPath = PrepareCleanDirectory(workSubdirectoryName);
            string sourcePath = Path.Combine(workRootPath, "Introduced.cs");
            File.WriteAllText(sourcePath, IntroducedSource);
            string dllPath = Path.Combine(workRootPath, workSubdirectoryName + ".dll");
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");

            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(
                externalCompilerPaths,
                Is.Not.Null,
                "External compiler paths could not be resolved for this Unity installation.");

            RoslynCompilerOptions compilerOptions = new(new List<string>(), false, emitDebugCode: true);
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(120));
            DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileAsync(
                sourcePath,
                dllPath,
                BuildReferencePaths(),
                externalCompilerPaths,
                compilerOptions,
                cts.Token,
                () => { },
                () => { },
                () => { });

            AssertNoCompileErrors(result.CompilerMessages);
            Assembly artifactAssembly = Assembly.Load(File.ReadAllBytes(dllPath), File.ReadAllBytes(pdbPath));
            Type behaviourType = artifactAssembly.GetType(BehaviourTypeMetadataName);
            Type alwaysBehaviourType = artifactAssembly.GetType(AlwaysBehaviourTypeMetadataName);
            Type scriptableType = artifactAssembly.GetType(ScriptableTypeMetadataName);
            Assert.That(behaviourType, Is.Not.Null, "The introduced MonoBehaviour type was not found.");
            Assert.That(alwaysBehaviourType, Is.Not.Null, "The introduced edit-mode type was not found.");
            Assert.That(scriptableType, Is.Not.Null, "The introduced ScriptableObject type was not found.");
            return new LoadedArtifact(artifactAssembly, behaviourType, alwaysBehaviourType, scriptableType);
        }

        /// <summary>
        /// Builds the reference list the snippet compiles against: the full set Unity reports for
        /// the assembly this suite lives in. Handing over the core library and the engine module
        /// alone is not enough — deriving from MonoBehaviour also needs the facade assembly the
        /// engine module itself references.
        /// </summary>
        private static List<string> BuildReferencePaths()
        {
            string ownAssemblyName = typeof(HotReloadSpikeS6IntroducedMonoBehaviourTests)
                .Assembly
                .GetName()
                .Name;
            List<string> references = new();
            foreach (UnityEditor.Compilation.Assembly editorAssembly in
                CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                if (editorAssembly.name != ownAssemblyName)
                {
                    continue;
                }

                foreach (string reference in editorAssembly.allReferences)
                {
                    if (!string.IsNullOrEmpty(reference)
                        && File.Exists(reference)
                        && !references.Contains(reference))
                    {
                        references.Add(reference);
                    }
                }
            }

            Assert.That(
                references,
                Is.Not.Empty,
                "No reference paths were found for the assembly this suite lives in.");
            return references;
        }

        private static string PrepareCleanDirectory(string subdirectoryName)
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workRootPath = Path.Combine(projectRootPath, "Library", "UloopHotReloadSpike", subdirectoryName);
            if (Directory.Exists(workRootPath))
            {
                Directory.Delete(workRootPath, true);
            }

            Directory.CreateDirectory(workRootPath);
            return workRootPath;
        }

        private static void AssertNoCompileErrors(CompilerMessage[] compilerMessages)
        {
            List<string> errors = new();
            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type == CompilerMessageType.Error)
                {
                    errors.Add(compilerMessage.message);
                }
            }

            Assert.That(errors, Is.Empty, "Artifact compilation failed:\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// How many times each lifecycle message arrived at each step of one add-toggle-destroy
        /// sequence.
        /// </summary>
        private readonly struct MessageDeltas
        {
            public int AwakeOnAdd { get; }
            public int EnableOnAdd { get; }
            public int DisableOnDisableFlag { get; }
            public int EnableOnEnableFlag { get; }
            public int DisableOnDestroy { get; }
            public int DestroyOnDestroy { get; }

            public MessageDeltas(
                int awakeOnAdd,
                int enableOnAdd,
                int disableOnDisableFlag,
                int enableOnEnableFlag,
                int disableOnDestroy,
                int destroyOnDestroy)
            {
                AwakeOnAdd = awakeOnAdd;
                EnableOnAdd = enableOnAdd;
                DisableOnDisableFlag = disableOnDisableFlag;
                EnableOnEnableFlag = enableOnEnableFlag;
                DisableOnDestroy = disableOnDestroy;
                DestroyOnDestroy = destroyOnDestroy;
            }
        }

        /// <summary>
        /// One compiled and loaded artifact assembly together with the three introduced types it
        /// declares, so a test can add either component and create the asset from the same load.
        /// </summary>
        private sealed class LoadedArtifact
        {
            public Assembly Assembly { get; }
            public Type BehaviourType { get; }
            public Type AlwaysBehaviourType { get; }
            public Type ScriptableType { get; }

            public LoadedArtifact(
                Assembly assembly,
                Type behaviourType,
                Type alwaysBehaviourType,
                Type scriptableType)
            {
                Assembly = assembly;
                BehaviourType = behaviourType;
                AlwaysBehaviourType = alwaysBehaviourType;
                ScriptableType = scriptableType;
            }
        }
    }
}
