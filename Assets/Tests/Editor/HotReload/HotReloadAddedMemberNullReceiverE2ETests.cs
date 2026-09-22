using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a patched body reaching a hot-reload-added member through a null
    /// receiver. Compiled code throws NullReferenceException there, and an added member is reached
    /// through a static shim call that takes the receiver as an argument, so nothing throws unless
    /// the rewrite checks the receiver itself.
    /// </summary>
    public class HotReloadAddedMemberNullReceiverE2ETests
    {
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";
        private const string HostValueAnchor = "        public int Value()";
        private const string CallerCallBodyAnchor = "return host.Value();";

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
        /// What: reading or writing an added field, an added auto-property, or an added bodied
        /// property, or calling an added method, through a null receiver throws exactly one
        /// NullReferenceException and logs nothing, the way the same access to a compiled member
        /// does. The bodied members here never touch the receiver, so without a receiver check
        /// they would run and return normally.
        /// </summary>
        [TestCase("Field read", "        public int Counter;\n\n", "return host.Counter + 40;")]
        [TestCase("Field write", "        public int Counter;\n\n", "host.Counter = 5;\n            return 0;")]
        [TestCase("Field compound write", "        public int Counter;\n\n", "host.Counter += 1;\n            return 0;")]
        [TestCase("Auto-property read", "        public int Count { get; set; }\n\n", "return host.Count;")]
        [TestCase("Auto-property write", "        public int Count { get; set; }\n\n", "host.Count = 3;\n            return 0;")]
        [TestCase("Bodied getter", "        public int Seven\n        {\n            get => 7;\n            set { }\n        }\n\n", "return host.Seven;")]
        [TestCase("Bodied setter", "        public int Seven\n        {\n            get => 7;\n            set { }\n        }\n\n", "host.Seven = 1;\n            return 0;")]
        [TestCase("Method", "        public int Added()\n        {\n            return 41;\n        }\n\n", "return host.Added();")]
        public async Task Call_AddedMemberThroughNullReceiver_ThrowsOneNullReferenceException(
            string label,
            string hostMember,
            string callerBody)
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "NullReceiver" + label.Replace(" ", string.Empty).Replace("-", string.Empty),
                InsertHostMember(hostMember),
                ReplaceCallerBody(callerBody));
            AssertNoFailure(result);
            HotReloadCrossFileAddedMemberCaller caller = new HotReloadCrossFileAddedMemberCaller();
            Assert.DoesNotThrow(
                () => caller.Call(new HotReloadCrossFileAddedMemberHost()),
                "Precondition: the patched caller runs on a real receiver.");

            Assert.Throws<NullReferenceException>(() => caller.Call(null), label);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// What: an added method or getter whose expression body is a throw expression still
        /// applies once the receiver check turns that body into a block: a real receiver gets the
        /// member's own exception and a null receiver gets NullReferenceException.
        /// </summary>
        [TestCase("Throw method", "        public int Stub() => throw new System.InvalidOperationException();\n\n", "return host.Stub();")]
        [TestCase("Throw getter", "        public int StubValue => throw new System.InvalidOperationException();\n\n", "return host.StubValue;")]
        public async Task Call_AddedThrowExpressionMember_ThrowsItsOwnExceptionOrNullReference(
            string label,
            string hostMember,
            string callerBody)
        {
            HotReloadOrchestratorResult result = await RunPairAsync(
                "NullReceiver" + label.Replace(" ", string.Empty),
                InsertHostMember(hostMember),
                ReplaceCallerBody(callerBody));
            AssertNoFailure(result);
            HotReloadCrossFileAddedMemberCaller caller = new HotReloadCrossFileAddedMemberCaller();

            Assert.Throws<InvalidOperationException>(
                () => caller.Call(new HotReloadCrossFileAddedMemberHost()),
                label);
            Assert.Throws<NullReferenceException>(() => caller.Call(null), label);
            LogAssert.NoUnexpectedReceived();
        }

        private static Task<HotReloadOrchestratorResult> RunPairAsync(
            string editedFileNamePrefix,
            string editedHostSource,
            string editedCallerSource)
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath, callerPath },
                contentPathOverride: null,
                CancellationToken.None,
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

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }
    }
}
