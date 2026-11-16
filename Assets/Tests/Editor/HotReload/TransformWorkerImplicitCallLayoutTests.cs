using System;
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
    /// Transform worker coverage for calls the compiler generates into an internal method the
    /// worker cannot see: one declared in a file the run does not pass, which the worker reads
    /// through the public surface only, and a public extension method of an internal class in the
    /// edited file, which both the edited source and the compiled assembly offer.
    /// </summary>
    public class TransformWorkerImplicitCallLayoutTests
    {
        private const string FixtureFileName = "HotReloadImplicitCallLayoutFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string InsideClosureAdvice = "Move the use out of the lambda, local function or query, or run 'uloop compile'.";

        /// <summary>
        /// What: a lambda whose await, foreach, deconstruction, positional pattern, collection
        /// initializer, query, ref struct using, or await using calls an internal method of a type
        /// in a file the run does not pass is skipped with the reason that names that method and
        /// advises moving the use out of the lambda, and produces no entry.
        /// </summary>
        [TestCase("8101", "ClosureAwaitsThroughUnpassedGetAwaiter", "GetAwaiter")]
        [TestCase("8111", "ClosureLoopsThroughUnpassedGetEnumerator", "GetEnumerator")]
        [TestCase("8121", "ClosureDeconstructsThroughUnpassedExtension", "Deconstruct")]
        [TestCase("8131", "ClosureDeconstructsThroughUnpassedInstanceMethod", "Deconstruct")]
        [TestCase("8141", "ClosureFillsThroughUnpassedExtensionAdd", "Add")]
        [TestCase("8151", "ClosureFillsThroughUnpassedInstanceAdd", "Add")]
        [TestCase("8161", "ClosureQueriesThroughUnpassedSelect", "Select")]
        [TestCase("8331", "ClosureQueriesThroughUnpassedWhere", "Where")]
        [TestCase("8341", "ClosureMatchesPositionalPatternThroughUnpassedDeconstruct", "Deconstruct")]
        [TestCase("8171", "ClosureUsesUnpassedRefStructWithInternalDispose", "Dispose")]
        [TestCase("8181", "ClosureAwaitUsesUnpassedInternalDisposeAsync", "DisposeAsync")]
        public async Task ClosureImplicitCallToUnpassedInternalMethod_IsSkippedWithTheInsideClosureReason(
            string token,
            string methodName,
            string calledMethod)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain("'" + calledMethod + "'"));
            Assert.That(reason, Does.Contain(InsideClosureAdvice));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: an async method whose foreach or deconstruction calls an internal method of a type
        /// in a file the run does not pass is skipped with the out-of-reach reason that names the
        /// method, and produces no entry.
        /// </summary>
        [TestCase("8301", "AsyncLoopsThroughUnpassedGetEnumerator", "GetEnumerator")]
        [TestCase("8311", "AsyncDeconstructsThroughUnpassedInstanceMethod", "Deconstruct")]
        public async Task AsyncImplicitCallToUnpassedInternalMethod_IsSkippedWithTheOutOfReachReason(
            string token,
            string methodName,
            string calledMethod)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain("'" + calledMethod + "'"));
            Assert.That(reason, Does.EndWith("Run 'uloop compile'."));
            Assert.That(reason, Does.Not.Contain(InsideClosureAdvice));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a method whose own statements loop through an internal GetEnumerator of a type in
        /// a file the run does not pass stays a patch entry, because the patched method itself
        /// makes the call.
        /// </summary>
        [Test]
        public async Task OwnStatementImplicitCallToUnpassedInternalMethod_StaysAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("8321");

            Assert.That(
                FindSkipReason(result, "OwnStatementsLoopThroughUnpassedGetEnumerator("),
                Is.Null,
                FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, "OwnStatementsLoopThroughUnpassedGetEnumerator"), Is.Not.Null);
        }

        /// <summary>
        /// What: a lambda whose await, foreach, deconstruction, collection initializer, or query
        /// calls a public extension method of an internal class in the edited file is skipped with
        /// the implicit-call reason, and produces no entry.
        /// </summary>
        [TestCase("8201", "ClosureAwaitsThroughTwinGetAwaiter", "GetAwaiter", "await")]
        [TestCase("8211", "ClosureLoopsThroughTwinGetEnumerator", "GetEnumerator", "foreach")]
        [TestCase("8221", "ClosureDeconstructsThroughTwinExtension", "Deconstruct", "a deconstruction")]
        [TestCase("8231", "ClosureFillsThroughTwinExtensionAdd", "Add", "a collection initializer")]
        [TestCase("8241", "ClosureQueriesThroughTwinSelect", "Select", "a query expression")]
        public async Task ImplicitCallToExtensionOfInternalClassInEditedFile_IsSkippedWithTheImplicitCallReason(
            string token,
            string methodName,
            string calledMethod,
            string construct)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                reason,
                Does.Contain(
                    "inaccessible method '" + calledMethod + "' that the compiler calls for " + construct
                    + " has no accessor rewrite shape."));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string token)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string editedToken = token.Substring(0, 3) + "2";
            string edited = onDisk.Replace(token, editedToken, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ImplicitCallLayout" + token + ".cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static TransformWorkerEntryDto FindEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static string FindSkipReason(TransformWorkerClientResult result, string methodNameFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodNameFragment))
                {
                    return HotReloadWorkerReasonText.Render(skipped.reason);
                }
            }

            return null;
        }

        private static string FormatSkipped(TransformWorkerSkippedDto[] skipped)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerSkippedDto row in skipped)
            {
                lines.Add(row.method + " => " + HotReloadWorkerReasonText.Render(row.reason));
            }

            return "Skipped=[" + string.Join(" | ", lines) + "]";
        }
    }
}
