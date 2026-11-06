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
        [TestCase("private object _value = new HotReloadObjectCreationRestrictedType(1);")]
        [TestCase("private object _value = new HotReloadInternalHostVisibleNeighbor(1);")]
        [TestCase("private object _value = new HotReloadObjectCreationRestrictedType { Number = 1 };")]
        [TestCase("private object _value = new HotReloadObjectCreationInternalAdd { 1 };")]
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
        /// What: a type the same reload introduces is constructed from the artifact it gets, so the
        /// compiled-type rule does not take that route away from it.
        /// </summary>
        [Test]
        public async Task Run_TypeTheSameReloadIntroduces_EvaluatesToTheStatedValue()
        {
            const string neighbor = "public sealed class HotReloadObjectCreationRestrictedType\n";
            string fixture = ReadFixture();
            Assert.That(fixture, Does.Contain(neighbor), "Precondition: the neighbor must exist.");
            HotReloadOrchestratorResult result = await RunSourceAsync(
                WithEditedRead(
                        fixture,
                        "private object _value = new HotReloadObjectCreationBrandNewType();",
                        "return _value == null ? 0 : 1;")
                    .Replace(
                        neighbor,
                        "public sealed class HotReloadObjectCreationBrandNewType\n    {\n    }\n\n    " + neighbor));

            AssertPatched(result);
            Assert.That(new HotReloadObjectCreationHost().Read(), Is.EqualTo(1), Describe(result));
        }

        /// <summary>
        /// What: a creation only the edit itself makes possible is refused, because the shim runs
        /// against what was compiled: the constructor it adds and the visibility it widens are not
        /// there yet.
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

        private static async Task<HotReloadOrchestratorResult> RunSourceAsync(string edited)
        {
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { ResolveFixturePath() },
                HotReloadTestSourceWriter.WriteEditedSource(EditedFileName, edited),
                CancellationToken.None);
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
