using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Newtonsoft.Json.Linq;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of the InvocationCount a member hot reload added reports: each added
    /// member counts the calls that start its shim's body on a counter of its own, so a reapply
    /// starts the count over and an unchanged reload keeps it.
    /// </summary>
    public class HotReloadAddedMemberInvocationCountE2ETests
    {
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";
        private const string SameFileFixtureName = "HotReloadSignatureChangeSameFileFixture.cs";
        private const string HostValueAnchor = "        public int Value()";
        private const string CallerCallBodyAnchor = "return host.Value();";
        private const string AddedMethodMember =
            "        public int Added()\n        {\n            return 41;\n        }\n\n";
        private const string BodiedPropertyMember =
            "        public int Seven\n        {\n            get => 7;\n            set { }\n        }\n\n";
        private const string AutoPropertyMember = "        public int Count { get; set; }\n\n";
        private const string IteratorMember =
            "        public System.Collections.Generic.IEnumerable<int> Numbers()\n"
            + "        {\n            yield return 5;\n        }\n\n";
        private const string AsyncMember =
            "        public async System.Threading.Tasks.Task<int> AddedAsync()\n"
            + "        {\n            await System.Threading.Tasks.Task.CompletedTask;\n            return 5;\n        }\n\n";
        private const string SameFileTargetDeclaration =
            "        public int Target(int value)\n        {\n            return value;\n        }";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        /// <summary>
        /// What: an added member of each kind reports InvocationCount 0 with the never-invoked
        /// Reason until a patched caller reaches it, then one per call with an empty Reason; a call
        /// through a null receiver, which the receiver check refuses before the body starts, is not
        /// counted.
        /// </summary>
        [TestCase("Method", AddedMethodMember, "return host.Added();", "Added(", true)]
        [TestCase("Expression-bodied method", "        public int AddedArrow() => 41;\n\n", "return host.AddedArrow();", "AddedArrow(", true)]
        [TestCase("Static method", "        public static int AddedStatic()\n        {\n            return 41;\n        }\n\n", "return HotReloadCrossFileAddedMemberHost.AddedStatic();", "AddedStatic(", false)]
        [TestCase("Static expression-bodied method", "        public static int AddedStaticArrow() => 41;\n\n", "return HotReloadCrossFileAddedMemberHost.AddedStaticArrow();", "AddedStaticArrow(", false)]
        [TestCase("Bodied getter", BodiedPropertyMember, "return host.Seven;", "get_Seven(", true)]
        [TestCase("Bodied setter", BodiedPropertyMember, "host.Seven = 1;\n            return 0;", "set_Seven(", true)]
        [TestCase("Auto-property getter", AutoPropertyMember, "return host.Count;", "get_Count(", true)]
        [TestCase("Auto-property setter", AutoPropertyMember, "host.Count = 3;\n            return 0;", "set_Count(", true)]
        public async Task Status_AddedMemberCalledThroughAPatchedCaller_CountsEachCall(
            string label,
            string hostMember,
            string callerBody,
            string memberToken,
            bool hasReceiver)
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "InvocationCount" + label.Replace(" ", string.Empty).Replace("-", string.Empty),
                InsertHostMember(hostMember),
                ReplaceCallerBody(callerBody));
            AssertNoFailure(result);
            HotReloadMethodResult beforeCalls = FindRow(await ExecuteStatusAsync(), "Added", memberToken);
            Assert.That(beforeCalls.InvocationCount, Is.EqualTo(0L), label);
            Assert.That(beforeCalls.Reason, Is.EqualTo(HotReloadConstants.AddedMemberNeverInvokedReason), label);

            HotReloadCrossFileAddedMemberCaller caller = new HotReloadCrossFileAddedMemberCaller();
            HotReloadCrossFileAddedMemberHost host = new HotReloadCrossFileAddedMemberHost();
            caller.Call(host);
            caller.Call(host);
            if (hasReceiver)
            {
                Assert.Throws<NullReferenceException>(() => caller.Call(null), label);
            }

            HotReloadMethodResult afterCalls = FindRow(await ExecuteStatusAsync(), "Added", memberToken);
            Assert.That(afterCalls.InvocationCount, Is.EqualTo(2L), label);
            Assert.That(afterCalls.Reason, Is.EqualTo(string.Empty), label);
        }

        /// <summary>
        /// What: an added member whose expression body is a throw expression counts each call,
        /// including those that throw, because the count is taken before the body runs.
        /// </summary>
        [TestCase("Throw method", "        public int Stub() => throw new System.InvalidOperationException();\n\n", "return host.Stub();", "Stub(")]
        [TestCase("Throw getter", "        public int StubValue => throw new System.InvalidOperationException();\n\n", "return host.StubValue;", "get_StubValue(")]
        public async Task Status_AddedThrowExpressionMember_CountsTheCallsThatThrew(
            string label,
            string hostMember,
            string callerBody,
            string memberToken)
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "InvocationCount" + label.Replace(" ", string.Empty),
                InsertHostMember(hostMember),
                ReplaceCallerBody(callerBody));
            AssertNoFailure(result);
            HotReloadCrossFileAddedMemberCaller caller = new HotReloadCrossFileAddedMemberCaller();
            HotReloadCrossFileAddedMemberHost host = new HotReloadCrossFileAddedMemberHost();

            Assert.Throws<InvalidOperationException>(() => caller.Call(host), label);
            Assert.Throws<InvalidOperationException>(() => caller.Call(host), label);

            HotReloadMethodResult row = FindRow(await ExecuteStatusAsync(), "Added", memberToken);
            Assert.That(row.InvocationCount, Is.EqualTo(2L), label);
        }

        /// <summary>
        /// What: an added iterator counts when its enumeration starts, not when it is called,
        /// because its whole body, the count included, waits for the first MoveNext; an added async
        /// method counts when it is called, because its body runs synchronously up to the first
        /// await that has to wait.
        /// </summary>
        [TestCase("Iterator called only", IteratorMember, "host.Numbers();\n            return 0;", "Numbers(", 0L)]
        [TestCase("Iterator enumerated", IteratorMember, "foreach (int number in host.Numbers())\n            {\n                return number;\n            }\n\n            return 0;", "Numbers(", 1L)]
        [TestCase("Async called", AsyncMember, "host.AddedAsync();\n            return 0;", "AddedAsync(", 1L)]
        public async Task Status_AddedIteratorOrAsyncMember_CountsWhenItsBodyStarts(
            string label,
            string hostMember,
            string callerBody,
            string memberToken,
            long expectedCount)
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "InvocationCount" + label.Replace(" ", string.Empty),
                InsertHostMember(hostMember),
                ReplaceCallerBody(callerBody));
            AssertNoFailure(result);

            new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost());

            HotReloadMethodResult row = FindRow(await ExecuteStatusAsync(), "Added", memberToken);
            Assert.That(row.InvocationCount, Is.EqualTo(expectedCount), label);
        }

        /// <summary>
        /// What: editing an added member's body and reloading replaces its shim, so its count
        /// starts over from 0 with the never-invoked Reason, the way a re-patched method's does.
        /// </summary>
        [Test]
        public async Task Status_ReappliedAddedMember_StartsCountingFromZero()
        {
            string callerSource = ReplaceCallerBody("return host.Added();");
            HotReloadOrchestratorResult first = await RunPairAsync(
                "InvocationCountReappliedFirst",
                InsertHostMember(AddedMethodMember),
                callerSource);
            AssertNoFailure(first);
            Assert.That(
                new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost()),
                Is.EqualTo(41));
            Assert.That(
                FindRow(await ExecuteStatusAsync(), "Added", "Added(").InvocationCount,
                Is.EqualTo(1L),
                "Precondition: the first shim counted the call.");

            HotReloadOrchestratorResult second = await RunPairAsync(
                "InvocationCountReappliedSecond",
                InsertHostMember(AddedMethodMember.Replace("return 41;", "return 42;")),
                callerSource);
            AssertNoFailure(second);
            AssertHasAdded(second, "Added(");

            HotReloadMethodResult row = FindRow(await ExecuteStatusAsync(), "Added", "Added(");
            Assert.That(row.InvocationCount, Is.EqualTo(0L));
            Assert.That(row.Reason, Is.EqualTo(HotReloadConstants.AddedMemberNeverInvokedReason));
        }

        /// <summary>
        /// What: reloading only the caller re-applies the host file as a sibling, which replaces
        /// the added member's shim with the rest of the host, so its count starts over from 0 even
        /// though the host's source did not change.
        /// </summary>
        [Test]
        public async Task Status_AddedMemberOfAReappliedSibling_StartsCountingFromZero()
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "InvocationCountSiblingHost.cs",
                    InsertHostMember(AddedMethodMember)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "InvocationCountSiblingCallerFirst.cs",
                    ReplaceCallerBody("return host.Added();"))
            };
            HotReloadOrchestratorResult first = await RunAsync(new[] { hostPath, callerPath }, overrides);
            AssertNoFailure(first);
            new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost());
            Assert.That(
                FindRow(await ExecuteStatusAsync(), "Added", "Added(").InvocationCount,
                Is.EqualTo(1L),
                "Precondition: the first shim counted the call.");

            overrides[callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "InvocationCountSiblingCallerSecond.cs",
                ReplaceCallerBody("return host.Added() + 1;"));
            HotReloadOrchestratorResult second = await RunAsync(new[] { callerPath }, overrides);
            AssertNoFailure(second);
            Assert.That(
                second.ReappliedSiblingPaths,
                Does.Contain(ProjectRelativePath(HostFileName)),
                "Precondition: the host is re-applied as a sibling of the edited caller.");

            HotReloadMethodResult row = FindRow(await ExecuteStatusAsync(), "Added", "Added(");
            Assert.That(row.InvocationCount, Is.EqualTo(0L));
            Assert.That(row.Reason, Is.EqualTo(HotReloadConstants.AddedMemberNeverInvokedReason));
        }

        /// <summary>
        /// What: after a body patch of a method and a later change of its return type, the label
        /// names both the patch of the old signature and the added new declaration, and each keeps
        /// its own count: --status reports the old patch's calls on the Active row and the new
        /// declaration's on the Added row, and an unchanged reload's AlreadyActive row carries the
        /// added declaration's count rather than the ledger's.
        /// </summary>
        [Test]
        public async Task Status_ReturnTypeChangeAfterABodyPatch_CountsTheOldPatchAndTheAddedMemberApart()
        {
            string fixturePath = FixturePath(SameFileFixtureName);
            string onDisk = File.ReadAllText(fixturePath);
            Assert.That(onDisk, Does.Contain(SameFileTargetDeclaration), "Precondition: Target anchor must exist.");
            string bodyOnlyEdit = onDisk.Replace(
                SameFileTargetDeclaration,
                SameFileTargetDeclaration.Replace("return value;", "return value + 7;"),
                StringComparison.Ordinal);
            AssertNoFailure(await RunSingleAsync(fixturePath, "InvocationCountBodyPatch.cs", bodyOnlyEdit));
            HotReloadSignatureChangeSameFileFixture host = new HotReloadSignatureChangeSameFileFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(10), "Precondition: the compiled caller reaches the patched body.");

            string returnTypeChange = WithReturnTypeChange(onDisk);
            HotReloadOrchestratorResult second = await RunSingleAsync(
                fixturePath,
                "InvocationCountReturnTypeChange.cs",
                returnTypeChange);
            AssertNoFailure(second);
            AssertHasAdded(second, "Target(");
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));

            HotReloadResponse status = await ExecuteStatusAsync();
            Assert.That(FindRow(status, "Active", "Target(").InvocationCount, Is.EqualTo(1L));
            Assert.That(FindRow(status, "Added", "Target(").InvocationCount, Is.EqualTo(2L));

            HotReloadOrchestratorResult unchanged = await RunSingleAsync(
                fixturePath,
                "InvocationCountUnchanged.cs",
                returnTypeChange);
            HotReloadMethodResult alreadyActive = FindRow(
                HotReloadTool.BuildApplyResponse(unchanged),
                nameof(HotReloadMethodOutcomeKind.AlreadyActive),
                "Target(");
            Assert.That(alreadyActive.Reason, Is.EqualTo(HotReloadConstants.AlreadyActiveAddedMemberReason));
            Assert.That(alreadyActive.InvocationCount, Is.EqualTo(2L));
        }

        private static Task<HotReloadOrchestratorResult> RunPairAsync(
            string editedFileNamePrefix,
            string editedHostSource,
            string editedCallerSource)
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            return RunAsync(
                new[] { hostPath, callerPath },
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        editedFileNamePrefix + "Host.cs",
                        editedHostSource),
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        editedFileNamePrefix + "Caller.cs",
                        editedCallerSource)
                });
        }

        private static Task<HotReloadOrchestratorResult> RunSingleAsync(
            string fixturePath,
            string editedFileName,
            string editedSource)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                HotReloadTestSourceWriter.WriteEditedSource(editedFileName, editedSource),
                CancellationToken.None);
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(
            string[] files,
            Dictionary<string, string> overrides)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>(overrides));
        }

        private static async Task<HotReloadResponse> ExecuteStatusAsync()
        {
            UnityCliLoopToolResponse baseResponse = await new HotReloadTool().ExecuteAsync(
                new JObject { ["Status"] = true },
                CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.True);
            return response;
        }

        private static HotReloadMethodResult FindRow(HotReloadResponse response, string kind, string methodToken)
        {
            List<string> rows = new List<string>();
            foreach (HotReloadMethodResult row in response.Methods)
            {
                if (row.Kind == kind && row.Method.Contains(methodToken))
                {
                    return row;
                }

                rows.Add(row.Kind + " " + row.Method + " (" + row.InvocationCount + ")");
            }

            Assert.Fail("No " + kind + " row containing '" + methodToken + "' in:\n" + string.Join("\n", rows));
            return null;
        }

        private static string InsertHostMember(string memberText)
        {
            string source = File.ReadAllText(FixturePath(HostFileName));
            Assert.That(source, Does.Contain(HostValueAnchor), "Precondition: host anchor must exist.");
            return source.Replace(HostValueAnchor, memberText + HostValueAnchor, StringComparison.Ordinal);
        }

        private static string ReplaceCallerBody(string bodyText)
        {
            string source = File.ReadAllText(FixturePath(CallerFileName));
            Assert.That(source, Does.Contain(CallerCallBodyAnchor), "Precondition: caller anchor must exist.");
            return source.Replace(CallerCallBodyAnchor, bodyText, StringComparison.Ordinal);
        }

        // The same-file return-type change: Target returns long, and its only caller casts back.
        private static string WithReturnTypeChange(string onDisk)
        {
            string edited = onDisk
                .Replace(
                    SameFileTargetDeclaration,
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "            return Target(value);\n        }",
                    "            return (int)Target(value);\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the return-type edit must apply.");
            return edited;
        }

        private static void AssertNoFailure(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    outcome.Method + " " + outcome.Reason);
            }
        }

        private static void AssertHasAdded(HotReloadOrchestratorResult result, string methodToken)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Added && outcome.Method.Contains(methodToken))
                {
                    return;
                }
            }

            Assert.Fail("No Added outcome containing '" + methodToken + "'.");
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string ProjectRelativePath(string fileName)
        {
            return "Assets/Tests/Editor/HotReload/" + fileName;
        }
    }
}
