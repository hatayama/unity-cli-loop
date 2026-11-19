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
    /// Transform worker coverage for lambdas and async bodies that reach an internal member only
    /// through an interpolated string handler, the implicit Index or Range support, or the awaiter
    /// that await using and await foreach call. Such a body must be refused instead of being
    /// transplanted with a call the shim cannot make.
    /// </summary>
    public class TransformWorkerImplicitCallPatternTests
    {
        private const string FixtureFileName = "HotReloadImplicitCallPatternFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string HandlerConstruct = "an interpolated string handler";
        private const string IndexOrRangeConstruct = "an index or range";
        private const string AwaitUsingConstruct = "await using";
        private const string AwaitForEachConstruct = "await foreach";

        /// <summary>
        /// What: an edited lambda or async body whose handler, index or range, or await using and
        /// await foreach awaiter makes the compiler call an internal member is skipped with the
        /// implicit-call reason naming that member and construct, and produces no entry.
        /// </summary>
        [TestCase("7101", "ClosureBuildsHandlerWithInternalCtor", "new HotReloadPatternHandlerInternalCtor(...)", HandlerConstruct)]
        [TestCase("7111", "AsyncBuildsHandlerWithInternalCtor", "new HotReloadPatternHandlerInternalCtor(...)", HandlerConstruct)]
        [TestCase("7121", "ClosureBuildsHandlerWithInternalAppendLiteral", "AppendLiteral", HandlerConstruct)]
        [TestCase("7131", "AsyncBuildsHandlerWithInternalAppendLiteral", "AppendLiteral", HandlerConstruct)]
        [TestCase("7141", "ClosureBuildsHandlerWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7151", "AsyncBuildsHandlerWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7161", "ClosureBuildsRefHandlerWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7171", "AsyncBuildsRefHandlerWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7181", "ClosureConvertsToHandlerLocalWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7191", "AsyncConvertsToHandlerLocalWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7601", "ClosureBuildsHandlerFromInterpolatedAddition", "AppendFormatted", HandlerConstruct)]
        [TestCase("7611", "AsyncBuildsHandlerFromInterpolatedAddition", "AppendFormatted", HandlerConstruct)]
        [TestCase("7791", "ClosureCastsToHandlerWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7801", "AsyncCastsToHandlerWithInternalAppendFormatted", "AppendFormatted", HandlerConstruct)]
        [TestCase("7201", "ClosureIndexesFromEndWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7211", "AsyncIndexesFromEndWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7221", "ClosureIndexesFromEndWithInternalCount", "Count", IndexOrRangeConstruct)]
        [TestCase("7231", "AsyncIndexesFromEndWithInternalCount", "Count", IndexOrRangeConstruct)]
        [TestCase("7241", "ClosureSlicesRangeWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7251", "AsyncSlicesRangeWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7261", "ClosureAssignsFromEndWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7271", "AsyncAssignsFromEndWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7651", "ClosureSlicesRangeWithInternalSlice", "Slice", IndexOrRangeConstruct)]
        [TestCase("7661", "AsyncSlicesRangeWithInternalSlice", "Slice", IndexOrRangeConstruct)]
        [TestCase("7671", "ClosureIndexesFromEndWithInternalGetter", "this[]", IndexOrRangeConstruct)]
        [TestCase("7681", "AsyncIndexesFromEndWithInternalGetter", "this[]", IndexOrRangeConstruct)]
        [TestCase("7621", "ClosureIndexesConditionallyFromEndWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7631", "AsyncIndexesConditionallyFromEndWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7761", "ClosureSlicesOpenEndWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7771", "ClosureSlicesFromEndToStartWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7781", "AsyncSlicesFromEndToStartWithInternalLength", "Length", IndexOrRangeConstruct)]
        [TestCase("7301", "ClosureAwaitUsesDisposeAsyncWithInternalGetAwaiter", "GetAwaiter", AwaitUsingConstruct)]
        [TestCase("7311", "AsyncAwaitUsesDisposeAsyncWithInternalGetAwaiter", "GetAwaiter", AwaitUsingConstruct)]
        [TestCase("7321", "ClosureAwaitUsesDisposeAsyncWithInternalIsCompleted", "IsCompleted", AwaitUsingConstruct)]
        [TestCase("7331", "AsyncAwaitUsesDisposeAsyncWithInternalIsCompleted", "IsCompleted", AwaitUsingConstruct)]
        [TestCase("7341", "ClosureAwaitUsesDisposeAsyncWithInternalGetResult", "GetResult", AwaitUsingConstruct)]
        [TestCase("7351", "AsyncAwaitUsesDisposeAsyncWithInternalGetResult", "GetResult", AwaitUsingConstruct)]
        [TestCase("7361", "ClosureAwaitUsingDeclarationWithInternalGetAwaiter", "GetAwaiter", AwaitUsingConstruct)]
        [TestCase("7371", "AsyncAwaitUsingDeclarationWithInternalGetAwaiter", "GetAwaiter", AwaitUsingConstruct)]
        [TestCase("7401", "ClosureAwaitForEachesMoveNextWithInternalGetAwaiter", "GetAwaiter", AwaitForEachConstruct)]
        [TestCase("7411", "AsyncAwaitForEachesMoveNextWithInternalGetAwaiter", "GetAwaiter", AwaitForEachConstruct)]
        [TestCase("7421", "ClosureAwaitForEachesMoveNextWithInternalIsCompleted", "IsCompleted", AwaitForEachConstruct)]
        [TestCase("7431", "AsyncAwaitForEachesMoveNextWithInternalIsCompleted", "IsCompleted", AwaitForEachConstruct)]
        [TestCase("7441", "ClosureAwaitForEachesMoveNextWithInternalGetResult", "GetResult", AwaitForEachConstruct)]
        [TestCase("7451", "AsyncAwaitForEachesMoveNextWithInternalGetResult", "GetResult", AwaitForEachConstruct)]
        [TestCase("7461", "ClosureAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter", "GetAwaiter", AwaitForEachConstruct)]
        [TestCase("7471", "AsyncAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter", "GetAwaiter", AwaitForEachConstruct)]
        public async Task PatternCallToInternalMember_IsSkippedWithTheImplicitCallReason(
            string token,
            string methodName,
            string calledMember,
            string construct)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                CollectCodes(skipped.reason),
                Does.Contain(HotReloadWorkerReasonCode.AccessorImplicitCallNoShape));
            Assert.That(
                HotReloadWorkerReasonText.Render(skipped.reason),
                Does.Contain(
                    "inaccessible method '" + calledMember + "' that the compiler calls for " + construct
                    + " has no accessor rewrite shape."));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a lambda that uses an interpolated string handler, the implicit Index and Range
        /// support, await using, or await foreach whose members and awaiters are all public stays a
        /// patch entry.
        /// </summary>
        [TestCase("7196", "ClosureBuildsPublicHandler")]
        [TestCase("7281", "ClosureIndexesAndSlicesPublicMembers")]
        [TestCase("7641", "ClosureIndexesConditionallyFromEndWithPublicMembers")]
        [TestCase("7381", "ClosureAwaitUsesPublicDisposeAsync")]
        [TestCase("7481", "ClosureAwaitForEachesPublicSequence")]
        public async Task PatternCallToPublicMembers_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipped(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        /// <summary>
        /// What: a lambda or async body that writes `x[^1]` through an internal indexer setter is
        /// skipped and produces no entry. The indexer check refuses it before the implicit-call
        /// check, so the reason is the indexer one.
        /// </summary>
        [TestCase("7691", "ClosureAssignsFromEndWithInternalSetter")]
        [TestCase("7701", "AsyncAssignsFromEndWithInternalSetter")]
        public async Task IndexFromEndThroughInternalSetter_IsSkipped(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(CollectCodes(skipped.reason), Does.Contain(HotReloadWorkerReasonCode.AccessorIndexerNoShape));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a simple assignment to `x[^1]` through parentheses calls only the public setter, so
        /// an internal indexer getter does not refuse the lambda or async body.
        /// </summary>
        [TestCase("7811", "ClosureAssignsParenthesizedFromEndWithInternalGetter")]
        [TestCase("7821", "AsyncAssignsParenthesizedFromEndWithInternalGetter")]
        public async Task ParenthesizedIndexAssignmentWithInternalGetter_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipped(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        /// <summary>
        /// What: a range whose given endpoints all count from the start and whose end is given
        /// (`1..2`, `..2`) never reads Length, so an internal Length alone does not refuse the
        /// lambda or async body.
        /// </summary>
        [TestCase("7721", "ClosureSlicesFromStartWithInternalLength")]
        [TestCase("7731", "AsyncSlicesFromStartWithInternalLength")]
        [TestCase("7741", "ClosureSlicesOpenStartWithInternalLength")]
        [TestCase("7751", "AsyncSlicesOpenStartWithInternalLength")]
        public async Task RangeFromStartWithInternalLength_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipped(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
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
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ImplicitCallPattern" + token + ".cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static List<HotReloadWorkerReasonCode> CollectCodes(TransformWorkerReasonDto reason)
        {
            List<HotReloadWorkerReasonCode> codes = new List<HotReloadWorkerReasonCode>();
            for (TransformWorkerReasonDto current = reason; current != null; current = current.detail)
            {
                codes.Add(current.code);
            }

            return codes;
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

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string methodNameFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodNameFragment))
                {
                    return skipped;
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
