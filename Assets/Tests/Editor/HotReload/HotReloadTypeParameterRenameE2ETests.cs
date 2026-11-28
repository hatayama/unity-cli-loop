using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for an edited source that renames a type parameter of an existing
    /// generic method or type while keeping its arity and positions: the member still matches its
    /// compiled counterpart, so it is never reported as an added generic method and its callers
    /// are patched.
    /// </summary>
    public class HotReloadTypeParameterRenameE2ETests
    {
        private const string FixtureFileName = "HotReloadTypeParameterRenameFixture.cs";

        private const string EchoHeader = "        public int Echo<T>(T value)\n";
        private const string RenamedEchoHeader = "        public int Echo<U>(U value)\n";
        private const string EchoValue = "            return 10;\n";
        private const string EditedEchoValue = "            return 20;\n";
        private const string CallerValue = "            return Echo<int>(1) + 100;\n";
        private const string EditedCallerValue = "            return Echo<int>(1) + 200;\n";
        private const string ReadValue = "            return 30;\n";
        private const string EditedReadValue = "            return 40;\n";
        private const string ReadCallerValue = "            return new HotReloadTypeParameterRenameGenericHost<int>().Read(1) + 300;\n";
        private const string EditedReadCallerValue = "            return new HotReloadTypeParameterRenameGenericHost<int>().Read(1) + 400;\n";
        private const string AddedGenericReason = "Added generic methods are skipped";
        private const string GenericEditReason = "Generic methods and methods inside generic types cannot be safely patched";
        private const string RemovedMembersWarning = "Removed members stay present in the compiled assembly";

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
        /// What: renaming only Echo's type parameter (T to U) while editing its caller is not
        /// reported as an added generic method or a removed member: Echo gets the edited-generic
        /// row, and the caller is Patched and returns the compiled Echo's value plus its edited
        /// constant.
        /// </summary>
        [Test]
        public async Task Run_RenamedTypeParameterWithUnchangedBody_CallerIsPatched()
        {
            HotReloadTypeParameterRenameFixture fixture = new HotReloadTypeParameterRenameFixture();
            Assert.That(fixture.CallEcho(), Is.EqualTo(110));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                EchoHeader, RenamedEchoHeader,
                CallerValue, EditedCallerValue);

            AssertNoAddedGenericRow(result);
            AssertNoRemovedMemberWarning(result);
            HotReloadMethodOutcome echo = FindOutcome(result, "HotReloadTypeParameterRenameFixture.Echo");
            Assert.That(echo.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), Describe(result));
            Assert.That(echo.Reason, Does.Contain(GenericEditReason), Describe(result));
            HotReloadMethodOutcome caller = FindOutcome(result, "HotReloadTypeParameterRenameFixture.CallEcho(");
            Assert.That(caller.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), Describe(result));
            Assert.That(fixture.CallEcho(), Is.EqualTo(210));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.CallEcho(), Is.EqualTo(110));
        }

        /// <summary>
        /// What: renaming Echo's type parameter and editing its body gives Echo the same row
        /// (kind and reason) as the same body edit without the rename, never the added-generic reason, and the
        /// caller is Patched and returns the value of the Echo that runs.
        /// </summary>
        [Test]
        public async Task Run_RenamedTypeParameterWithEditedBody_MatchesTheUnrenamedEdit()
        {
            HotReloadTypeParameterRenameFixture fixture = new HotReloadTypeParameterRenameFixture();
            HotReloadOrchestratorResult control = await RunEditedFixtureAsync(
                EchoValue, EditedEchoValue,
                CallerValue, EditedCallerValue);
            HotReloadMethodOutcome controlEcho = FindOutcome(control, "HotReloadTypeParameterRenameFixture.Echo");
            int controlValue = fixture.CallEcho();
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.CallEcho(), Is.EqualTo(110));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                EchoHeader, RenamedEchoHeader,
                EchoValue, EditedEchoValue,
                CallerValue, EditedCallerValue);

            AssertNoAddedGenericRow(result);
            AssertNoRemovedMemberWarning(result);
            HotReloadMethodOutcome echo = FindOutcome(result, "HotReloadTypeParameterRenameFixture.Echo");
            Assert.That(echo.Kind, Is.EqualTo(controlEcho.Kind), Describe(result));
            Assert.That(echo.Reason, Is.EqualTo(controlEcho.Reason), Describe(result));
            HotReloadMethodOutcome caller = FindOutcome(result, "HotReloadTypeParameterRenameFixture.CallEcho(");
            Assert.That(caller.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), Describe(result));
            Assert.That(fixture.CallEcho(), Is.EqualTo(controlValue));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.CallEcho(), Is.EqualTo(110));
        }

        /// <summary>
        /// What: the control, editing Echo's body and its caller without renaming anything, patches
        /// the caller and never reports the added-generic reason.
        /// </summary>
        [Test]
        public async Task Run_EditedBodyWithoutRename_CallerIsPatched()
        {
            HotReloadTypeParameterRenameFixture fixture = new HotReloadTypeParameterRenameFixture();

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                EchoValue, EditedEchoValue,
                CallerValue, EditedCallerValue);

            AssertNoAddedGenericRow(result);
            HotReloadMethodOutcome caller = FindOutcome(result, "HotReloadTypeParameterRenameFixture.CallEcho(");
            Assert.That(caller.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), Describe(result));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.CallEcho(), Is.EqualTo(110));
        }

        /// <summary>
        /// What: renaming a generic type's type parameter (Host&lt;T&gt; to Host&lt;U&gt;) while
        /// editing one of its methods and a caller of that method outside the type gives the method
        /// the same row as the same edit without the rename, and the caller is Patched and returns
        /// the value of the method that runs.
        /// </summary>
        [Test]
        public async Task Run_RenamedGenericTypeParameter_MatchesTheUnrenamedEdit()
        {
            HotReloadTypeParameterRenameFixture fixture = new HotReloadTypeParameterRenameFixture();
            Assert.That(fixture.CallRead(), Is.EqualTo(330));
            HotReloadOrchestratorResult control = await RunEditedFixtureAsync(
                ReadValue, EditedReadValue,
                ReadCallerValue, EditedReadCallerValue);
            HotReloadMethodOutcome controlRead = FindOutcome(control, "HotReloadTypeParameterRenameGenericHost");
            HotReloadMethodOutcome controlCaller = FindOutcome(control, "HotReloadTypeParameterRenameFixture.CallRead(");
            Assert.That(controlCaller.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), Describe(control));
            int controlValue = fixture.CallRead();
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.CallRead(), Is.EqualTo(330));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                "    public sealed class HotReloadTypeParameterRenameGenericHost<T>\n",
                "    public sealed class HotReloadTypeParameterRenameGenericHost<U>\n",
                "        public int Read(T value)\n",
                "        public int Read(U value)\n",
                ReadValue, EditedReadValue,
                ReadCallerValue, EditedReadCallerValue);

            AssertNoAddedGenericRow(result);
            AssertNoRemovedMemberWarning(result);
            HotReloadMethodOutcome read = FindOutcome(result, "HotReloadTypeParameterRenameGenericHost");
            Assert.That(read.Kind, Is.EqualTo(controlRead.Kind), Describe(result));
            Assert.That(read.Reason, Is.EqualTo(controlRead.Reason), Describe(result));
            HotReloadMethodOutcome caller = FindOutcome(result, "HotReloadTypeParameterRenameFixture.CallRead(");
            Assert.That(caller.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), Describe(result));
            Assert.That(fixture.CallRead(), Is.EqualTo(controlValue));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.CallRead(), Is.EqualTo(330));
        }

        // Why pairs of strings: the replacements run in order, and each must find its text in the
        // fixture, so a typo fails the precondition instead of editing nothing.
        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(params string[] replacements)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string edited = File.ReadAllText(fixturePath).Replace("\r\n", "\n");
            for (int index = 0; index < replacements.Length; index += 2)
            {
                Assert.That(edited.Contains(replacements[index]), "Precondition: the fixture must contain " + replacements[index]);
                edited = edited.Replace(replacements[index], replacements[index + 1]);
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "TypeParameterRenameE2E" + System.Math.Abs(edited.GetHashCode()) + ".cs",
                edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static HotReloadMethodOutcome FindOutcome(HotReloadOrchestratorResult result, string methodPart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains(methodPart))
                {
                    return outcome;
                }
            }

            Assert.Fail("No outcome row for " + methodPart + ".\n" + Describe(result));
            return null;
        }

        private static void AssertNoAddedGenericRow(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Reason ?? string.Empty,
                    Does.Not.Contain(AddedGenericReason),
                    Describe(result));
            }
        }

        private static void AssertNoRemovedMemberWarning(HotReloadOrchestratorResult result)
        {
            foreach (string warning in result.Warnings)
            {
                Assert.That(warning, Does.Not.Contain(RemovedMembersWarning));
            }
        }

        private static string Describe(HotReloadOrchestratorResult result)
        {
            List<string> rows = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                rows.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", rows);
        }
    }
}
