using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies which object creations an added field's initializer may run: a public constructor
    /// of a compiled, externally visible type runs on first access once per instance, and anything
    /// the static lambda on the shim cannot reach skips the field's readers instead of failing
    /// when the field is first read.
    /// </summary>
    public sealed class HotReloadObjectCreationInitializerTests
    {
        private const string FixtureFileName = "HotReloadObjectCreationInitializerFixtures.cs";
        private const string EditedFileName = "ObjectCreationInitializer.cs";
        private const string RefusalFragment = "has an initializer that is not a literal";
        private const string CrossAssemblyHostFileName = "HotReloadObjectCreationCrossAssemblyHost.cs";
        private const string CrossAssemblyHostTypeName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadObjectCreationCrossAssemblyHost, "
            + "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";

        private const string OriginalRead =
            "[MethodImpl(MethodImplOptions.NoInlining)]\n"
            + "        public int Read()\n"
            + "        {\n"
            + "            return -1;\n"
            + "        }\n";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        /// <summary>
        /// What: a constructor call runs once per instance on first access, so the same instance
        /// keeps its value across reads and another instance starts from the initializer again.
        /// </summary>
        [Test]
        public async Task Run_PublicConstructor_RunsOncePerInstance()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "private System.Text.StringBuilder _log = new System.Text.StringBuilder(\"seed\");",
                "_log.Append(\"x\");\n            return _log.Length;");

            AssertPatched(result);
            HotReloadObjectCreationHost first = new HotReloadObjectCreationHost();
            Assert.That(first.Read(), Is.EqualTo(5), Describe(result));
            Assert.That(first.Read(), Is.EqualTo(6), Describe(result));
            Assert.That(new HotReloadObjectCreationHost().Read(), Is.EqualTo(5), Describe(result));
        }

        /// <summary>
        /// What: each accepted spelling of a creation evaluates to the value its source states, so
        /// a form the shim emits differently from the source still reaches the right constructor.
        /// </summary>
        [TestCase("private UnityEngine.Vector2 _value = new(1f, 2f);", "(int)(_value.x + _value.y)", 3)]
        [TestCase(
            "private UnityEngine.Vector2 _value = new UnityEngine.Vector2(UnityEngine.Mathf.Abs(-4f), 1f);",
            "(int)(_value.x + _value.y)",
            5)]
        [TestCase(
            "private static readonly UnityEngine.Vector2[] Values = { new(1f, 2f), new(3f, 4f) };",
            "(int)Values[1].y",
            4)]
        [TestCase(
            "private System.Collections.Generic.List<int> _values = new System.Collections.Generic.List<int> { 1, 2, 3 };",
            "_values.Count",
            3)]
        [TestCase(
            "private System.Collections.Generic.List<int> _values = new() { 1, 2 };",
            "_values.Count",
            2)]
        [TestCase(
            "private HotReloadObjectCreationHost _value = new HotReloadObjectCreationHost();",
            "_value == null ? 0 : 7",
            7)]
        [TestCase(
            "private HotReloadObjectCreationHost.Nested _value = new HotReloadObjectCreationHost.Nested();",
            "_value == null ? 0 : 8",
            8)]
        [TestCase("private Nested _value = new();", "_value == null ? 0 : 9", 9)]
        [TestCase(
            "private object _value = new HotReloadObjectCreationGenericBox<int>(1);",
            "_value == null ? 0 : 10",
            10)]
        [TestCase(
            "private HotReloadInternalMemberHost _value = new HotReloadInternalMemberHost();",
            "_value == null ? 0 : 6",
            6)]
        public async Task Run_AcceptedCreation_EvaluatesToTheStatedValue(
            string fieldDeclaration,
            string readExpression,
            int expected)
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                fieldDeclaration,
                "return " + readExpression + ";");

            AssertPatched(result);
            Assert.That(new HotReloadObjectCreationHost().Read(), Is.EqualTo(expected), Describe(result));
        }

        /// <summary>
        /// What: an added auto-property follows the same rule, because its store shares the field's
        /// initializer path.
        /// </summary>
        [Test]
        public async Task Run_AutoPropertyWithPublicConstructor_EvaluatesToTheStatedValue()
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                "public System.Text.StringBuilder Log { get; } = new System.Text.StringBuilder(\"seed\");",
                "return Log.Length;");

            AssertPatched(result);
            Assert.That(new HotReloadObjectCreationHost().Read(), Is.EqualTo(4), Describe(result));
        }

        /// <summary>
        /// What: each creation the static lambda on the shim cannot run skips the reader with the
        /// initializer's reason and leaves its compiled body, rather than applying and throwing
        /// when the field is first read.
        /// </summary>
        [TestCase("private object _value = new HotReloadObjectCreationInternalType();")]
        [TestCase("private object _value = new System.Collections.Generic.List<HotReloadObjectCreationInternalType>();")]
        [TestCase("private object _value = new HotReloadObjectCreationGenericOuter<HotReloadObjectCreationInternalType>.Inner();")]
        [TestCase("private object _value = new HotReloadObjectCreationRestrictedType(1);")]
        [TestCase("private object _value = new HotReloadInternalHostVisibleNeighbor(1);")]
        [TestCase("private object _value = new HotReloadObjectCreationRestrictedType { Number = 1 };")]
        [TestCase("private object _value = new HotReloadObjectCreationInternalAdd { 1 };")]
        [TestCase("private object _value = new HotReloadObjectCreationInternalAdd { new System.Text.StringBuilder() };")]
        [TestCase("private object _value = new HotReloadUnpassedProtectedConstructor(1);")]
        [TestCase("private object _value = new System.Exception(null, default(System.Runtime.Serialization.StreamingContext));")]
        [TestCase("private object _value = new HotReloadUnpassedHiddenOverload(1);")]
        [TestCase("private object _value = new HotReloadUnpassedInternalAdd { 1 };")]
        [TestCase("private object _value = new HotReloadUnpassedHiddenAddOverload { 1 };")]
        [TestCase("private Nested _value = new Nested();")]
        [TestCase("private object _value = new System.Collections.Generic.List<Nested>();")]
        [TestCase("private string _seed = \"seed\"; private object _value = new System.Text.StringBuilder(_seed);")]
        public async Task Run_RefusedCreation_SkipsTheReaderAndKeepsItsBody(string fieldDeclaration)
        {
            HotReloadOrchestratorResult result = await RunEditedAsync(
                fieldDeclaration,
                "return _value == null ? 0 : 1;");

            Assert.That(FindSkippedReason(result), Does.Contain(RefusalFragment), Describe(result));
            Assert.That(new HotReloadObjectCreationHost().Read(), Is.EqualTo(-1), Describe(result));
        }

        /// <summary>
        /// What: edited from another assembly, a referenced project assembly's protected member is
        /// on the surface the worker binds and in the copy the shim compiles against, yet the shim
        /// cannot call it, so the reader skips rather than failing when the field is first read.
        /// </summary>
        [TestCase("private object _value = new HotReloadUnpassedProtectedConstructor(1);")]
        [TestCase("private object _value = new HotReloadUnpassedOuter.ProtectedNested();")]
        [TestCase("private object _value = new HotReloadUnpassedProtectedAdd { 1 };")]
        public async Task Run_ProtectedMemberOfReferencedAssembly_SkipsTheReader(string fieldDeclaration)
        {
            HotReloadOrchestratorResult result = await RunSourceAtAsync(
                ResolveCrossAssemblyHostPath(),
                WithEditedRead(
                    File.ReadAllText(ResolveCrossAssemblyHostPath()).Replace("\r\n", "\n"),
                    fieldDeclaration,
                    "return _value == null ? 0 : 1;"));

            Assert.That(FindSkippedReason(result), Does.Contain(RefusalFragment), Describe(result));
            Assert.That(ReadCrossAssemblyHost(), Is.EqualTo(-1), Describe(result));
        }

        /// <summary>
        /// What: edited from another assembly, a referenced project assembly's hidden overload stays
        /// hidden from the shim too, so the public overload both bindings chose is the one that runs.
        /// </summary>
        [TestCase("private object _value = new HotReloadUnpassedHiddenOverload(1);")]
        [TestCase("private object _value = new HotReloadUnpassedHiddenAddOverload { 1 };")]
        public async Task Run_HiddenOverloadOfReferencedAssembly_RunsThePublicOne(string fieldDeclaration)
        {
            HotReloadOrchestratorResult result = await RunSourceAtAsync(
                ResolveCrossAssemblyHostPath(),
                WithEditedRead(
                    File.ReadAllText(ResolveCrossAssemblyHostPath()).Replace("\r\n", "\n"),
                    fieldDeclaration,
                    "return _value == null ? 0 : 1;"));

            AssertPatched(result);
            Assert.That(ReadCrossAssemblyHost(), Is.EqualTo(1), Describe(result));
        }

        /// <summary>
        /// What: a type the same reload introduces is constructed from the artifact it gets, so the
        /// compiled-type rule does not take that route away from it, whether the type is created
        /// directly, filled through its Add, or named only as a type argument.
        /// </summary>
        [TestCase(
            "public sealed class HotReloadObjectCreationBrandNewType\n    {\n    }\n\n",
            "new HotReloadObjectCreationBrandNewType()")]
        [TestCase(
            "public sealed class HotReloadObjectCreationBrandNewCollection : IEnumerable\n    {\n"
            + "        public void Add(int value)\n        {\n        }\n\n"
            + "        public IEnumerator GetEnumerator()\n        {\n"
            + "            return new int[0].GetEnumerator();\n        }\n    }\n\n",
            "new HotReloadObjectCreationBrandNewCollection { 1 }")]
        [TestCase(
            "public sealed class HotReloadObjectCreationBrandNewArgument\n    {\n    }\n\n",
            "new System.Collections.Generic.List<HotReloadObjectCreationBrandNewArgument>()")]
        [TestCase(
            "internal sealed class HotReloadObjectCreationBrandNewInternalArgument\n    {\n    }\n\n",
            "new System.Collections.Generic.List<HotReloadObjectCreationBrandNewInternalArgument>()")]
        public async Task Run_TypeTheSameReloadIntroduces_EvaluatesToTheStatedValue(
            string declaration,
            string creation)
        {
            const string neighbor = "public sealed class HotReloadObjectCreationRestrictedType\n";
            string fixture = ReadFixture();
            Assert.That(fixture, Does.Contain(neighbor), "Precondition: the neighbor must exist.");
            HotReloadOrchestratorResult result = await RunSourceAsync(
                WithEditedRead(
                        fixture,
                        "private object _value = " + creation + ";",
                        "return _value == null ? 0 : 1;")
                    .Replace(neighbor, declaration + "    " + neighbor));

            AssertPatched(result);
            Assert.That(new HotReloadObjectCreationHost().Read(), Is.EqualTo(1), Describe(result));
        }

        /// <summary>
        /// What: a creation only the edit itself makes possible is refused, because the shim runs
        /// against what was compiled: the constructor it adds and the visibility it widens, on the
        /// type, on a type argument, or on a collection's Add, are not there yet.
        /// </summary>
        [TestCase(
            "public HotReloadObjectCreationRestrictedType()\n",
            "public HotReloadObjectCreationRestrictedType(string added)\n        {\n        }\n\n"
            + "        public HotReloadObjectCreationRestrictedType()\n",
            "new HotReloadObjectCreationRestrictedType(\"added\")")]
        [TestCase(
            "internal sealed class HotReloadObjectCreationInternalType\n",
            "public sealed class HotReloadObjectCreationInternalType\n",
            "new HotReloadObjectCreationInternalType()")]
        [TestCase(
            "internal sealed class HotReloadObjectCreationInternalType\n",
            "public sealed class HotReloadObjectCreationInternalType\n",
            "new System.Collections.Generic.List<HotReloadObjectCreationInternalType>()")]
        [TestCase(
            "internal void Add(int value)\n",
            "public void Add(int value)\n",
            "new HotReloadObjectCreationInternalAdd { 1 }")]
        public async Task Run_CreationOnlyThisEditMakesPossible_SkipsTheReaderAndKeepsItsBody(
            string original,
            string replacement,
            string creation)
        {
            string fixture = ReadFixture();
            Assert.That(fixture, Does.Contain(original), "Precondition: the edited declaration must exist.");
            HotReloadOrchestratorResult result = await RunSourceAsync(
                WithEditedRead(
                        fixture,
                        "private object _value = " + creation + ";",
                        "return _value == null ? 0 : 1;")
                    .Replace(original, replacement));

            Assert.That(FindSkippedReason(result), Does.Contain(RefusalFragment), Describe(result));
            Assert.That(new HotReloadObjectCreationHost().Read(), Is.EqualTo(-1), Describe(result));
        }

        private static Task<HotReloadOrchestratorResult> RunEditedAsync(string fieldDeclaration, string readBody)
        {
            return RunSourceAsync(WithEditedRead(ReadFixture(), fieldDeclaration, readBody));
        }

        private static Task<HotReloadOrchestratorResult> RunSourceAsync(string edited)
        {
            return RunSourceAtAsync(ResolveFixturePath(), edited);
        }

        private static async Task<HotReloadOrchestratorResult> RunSourceAtAsync(string originalPath, string edited)
        {
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { originalPath },
                HotReloadTestSourceWriter.WriteEditedSource(EditedFileName, edited),
                CancellationToken.None);
        }

        // Why reflection: this test assembly is what the host's assembly references, so it cannot
        // name the host the other way round.
        private static int ReadCrossAssemblyHost()
        {
            System.Type hostType = System.Type.GetType(CrossAssemblyHostTypeName);
            Assert.That(hostType, Is.Not.Null, "Cross-assembly host missing: " + CrossAssemblyHostTypeName);
            object host = System.Activator.CreateInstance(hostType);
            return (int)hostType.GetMethod("Read").Invoke(host, null);
        }

        private static string WithEditedRead(string fixture, string fieldDeclaration, string readBody)
        {
            Assert.That(fixture, Does.Contain(OriginalRead), "Precondition: Read must exist.");
            return fixture.Replace(
                OriginalRead,
                fieldDeclaration + "\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int Read()\n"
                + "        {\n"
                + "            " + readBody + "\n"
                + "        }\n");
        }

        private static string ReadFixture()
        {
            return File.ReadAllText(ResolveFixturePath()).Replace("\r\n", "\n");
        }

        private static string ResolveFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                FixtureFileName);
            Assert.That(File.Exists(path), Is.True, "Fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveCrossAssemblyHostPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReloadCallSiteCrossAssembly",
                CrossAssemblyHostFileName);
            Assert.That(File.Exists(path), Is.True, "Cross-assembly host source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static void AssertPatched(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains(".Read("))
                {
                    return;
                }
            }

            Assert.Fail("Expected a Patched outcome for Read.\n" + Describe(result));
        }

        private static string FindSkippedReason(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method.Contains(".Read(")
                    && outcome.Reason != null)
                {
                    return outcome.Reason;
                }
            }

            Assert.Fail("Expected a Skipped outcome for Read.\n" + Describe(result));
            return null;
        }

        private static string Describe(HotReloadOrchestratorResult result)
        {
            string description = string.Empty;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                description += outcome.Kind + " " + outcome.Method + " " + outcome.Reason + "\n";
            }

            return description;
        }
    }
}
