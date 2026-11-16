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
    /// Transform worker coverage for lambdas and async bodies that reach an internal method only
    /// through a call the compiler generates (await, foreach, deconstruction, collection
    /// initializer, query expression, ref struct using). Such a body must be refused instead of
    /// being transplanted with a call the shim cannot make.
    /// </summary>
    public class TransformWorkerImplicitCallTests
    {
        private const string FixtureFileName = "HotReloadImplicitCallFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;

        /// <summary>
        /// What: an edited body whose await, foreach, deconstruction, positional pattern, collection
        /// initializer, query expression, ref struct using, or await using makes the compiler call
        /// an internal method is skipped with the implicit-call reason naming that method and
        /// construct, and produces no entry, whether or not the body also reads a private member.
        /// </summary>
        [TestCase("6101", "ClosureAwaitsThroughInternalGetAwaiter", "GetAwaiter", "await")]
        [TestCase("6111", "AsyncAwaitsThroughInternalGetAwaiter", "GetAwaiter", "await")]
        [TestCase("6121", "ClosureAwaitsThroughInternalGetResult", "GetResult", "await")]
        [TestCase("6201", "ClosureLoopsThroughInternalGetEnumerator", "GetEnumerator", "foreach")]
        [TestCase("6211", "AsyncLoopsThroughInternalGetEnumerator", "GetEnumerator", "foreach")]
        [TestCase("6301", "ClosureDeconstructsThroughInternalExtension", "Deconstruct", "a deconstruction")]
        [TestCase("6311", "ClosureDeconstructsThroughInternalInstanceMethod", "Deconstruct", "a deconstruction")]
        [TestCase("6321", "AsyncDeconstructsThroughInternalInstanceMethod", "Deconstruct", "a deconstruction")]
        [TestCase("6331", "ClosureDeconstructsNestedThroughInternalInstanceMethod", "Deconstruct", "a deconstruction")]
        [TestCase("6401", "ClosureFillsThroughInternalExtensionAdd", "Add", "a collection initializer")]
        [TestCase("6411", "ClosureFillsThroughInternalInstanceAdd", "Add", "a collection initializer")]
        [TestCase("6421", "AsyncFillsThroughInternalExtensionAdd", "Add", "a collection initializer")]
        [TestCase("6501", "ClosureQueriesThroughInternalSelect", "Select", "a query expression")]
        [TestCase("6511", "AsyncQueriesThroughInternalSelect", "Select", "a query expression")]
        [TestCase("6521", "ClosureQueriesThroughInternalWhere", "Where", "a query expression")]
        [TestCase("6601", "ClosureUsesRefStructWithInternalDispose", "Dispose", "using")]
        [TestCase("6701", "ClosureAwaitForEachesThroughInternalGetAsyncEnumerator", "GetAsyncEnumerator", "foreach")]
        [TestCase("6711", "AsyncAwaitForEachesThroughInternalGetAsyncEnumerator", "GetAsyncEnumerator", "foreach")]
        [TestCase("9101", "ClosureOnlyAwaitsThroughInternalGetAwaiter", "GetAwaiter", "await")]
        [TestCase("9111", "ClosureOnlyAwaitsThroughInternalGetResult", "GetResult", "await")]
        [TestCase("9121", "ClosureOnlyLoopsThroughInternalGetEnumerator", "GetEnumerator", "foreach")]
        [TestCase("9126", "AsyncOnlyLoopsThroughInternalGetEnumerator", "GetEnumerator", "foreach")]
        [TestCase("9131", "ClosureOnlyDeconstructsThroughInternalExtension", "Deconstruct", "a deconstruction")]
        [TestCase("9141", "ClosureOnlyDeconstructsThroughInternalInstanceMethod", "Deconstruct", "a deconstruction")]
        [TestCase("9151", "ClosureOnlyFillsThroughInternalExtensionAdd", "Add", "a collection initializer")]
        [TestCase("9161", "ClosureOnlyFillsThroughInternalInstanceAdd", "Add", "a collection initializer")]
        [TestCase("9171", "ClosureOnlyQueriesThroughInternalSelect", "Select", "a query expression")]
        [TestCase("9181", "ClosureOnlyUsesRefStructWithInternalDispose", "Dispose", "using")]
        [TestCase("9191", "ClosureOnlyAwaitForEachesThroughInternalGetAsyncEnumerator", "GetAsyncEnumerator", "foreach")]
        [TestCase("9301", "ClosureAwaitUsesInternalDisposeAsync", "DisposeAsync", "await using")]
        [TestCase("9311", "AsyncAwaitUsingDeclarationWithInternalDisposeAsync", "DisposeAsync", "await using")]
        [TestCase("9431", "ClosureAwaitUsesInheritedInternalDisposeAsync", "DisposeAsync", "await using")]
        [TestCase("9451", "ClosureAwaitUsesOptionalParameterDisposeAsync", "DisposeAsync", "await using")]
        [TestCase("9321", "ClosureMatchesIsPositionalPatternThroughInternalDeconstruct", "Deconstruct", "a positional pattern")]
        [TestCase("9331", "ClosureMatchesSwitchArmPositionalPatternThroughInternalDeconstruct", "Deconstruct", "a positional pattern")]
        [TestCase("9341", "ClosureMatchesCasePositionalPatternThroughInternalDeconstruct", "Deconstruct", "a positional pattern")]
        [TestCase("9351", "ClosureOrdersThroughInternalOrderBy", "OrderBy", "a query expression")]
        [TestCase("9361", "ClosureQueriesTypedFromThroughInternalCast", "Cast", "a query expression")]
        [TestCase("9371", "ClosureQueriesTypedJoinThroughInternalCast", "Cast", "a query expression")]
        [TestCase("9381", "ClosureLoopsWithDeconstructionThroughInternalDeconstruct", "Deconstruct", "a deconstruction")]
        [TestCase("9401", "ClosureUsesVarDeclarationOfRefStructWithInternalDispose", "Dispose", "using")]
        [TestCase("9411", "ClosureUsesExpressionOfRefStructWithInternalDispose", "Dispose", "using")]
        [TestCase("9421", "ClosureUsesDeclarationOfRefStructWithInternalDispose", "Dispose", "using")]
        public async Task ImplicitCallToInternalMethod_IsSkippedWithTheImplicitCallReason(
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

        /// <summary>
        /// What: a lambda that reads a private field and uses the same constructs through public
        /// methods (a public Deconstruct, a List collection initializer, LINQ to objects, awaiting a
        /// Task, foreach over a List) stays a patch entry, as does an await using on a resource that
        /// implements IAsyncDisposable beside an internal pattern DisposeAsync (the compiled call goes
        /// through the public interface method).
        /// </summary>
        [TestCase("6801", "ClosureDeconstructsThroughPublicMethod")]
        [TestCase("6811", "ClosureFillsPublicList")]
        [TestCase("6821", "ClosureQueriesPublicList")]
        [TestCase("6831", "ClosureAwaitsPublicTask")]
        [TestCase("6841", "ClosureLoopsOverPublicList")]
        [TestCase("9441", "ClosureAwaitUsesInterfaceDisposeAsyncBesideInternalPattern")]
        public async Task ImplicitCallToPublicMethod_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipReason(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string token)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string editedToken = token.Substring(0, 3) + "2";
            string edited = onDisk.Replace(token, editedToken, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ImplicitCall" + token + ".cs", edited);
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
