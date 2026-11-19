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
    /// Transform worker coverage for lambdas and async bodies that reach an internal member of a
    /// type in a file the run does not pass, which the worker reads through the public surface
    /// only, through an interpolated string handler, the implicit Index or Range support, the
    /// awaiter of await using and await foreach, or an extension method whose this parameter is a
    /// type parameter.
    /// </summary>
    public class TransformWorkerImplicitCallPatternLayoutTests
    {
        private const string FixtureFileName = "HotReloadImplicitCallPatternLayoutFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string InsideClosureAdvice = "Move the use out of the lambda, local function or query, or run 'uloop compile'.";

        /// <summary>
        /// What: a lambda that reaches such a member is skipped with the inside-closure reason that
        /// names the member, and produces no entry.
        /// </summary>
        [TestCase("7501", "ClosureBuildsHandlerWithInternalCtor", "new HotReloadPatternHandlerInternalCtor(...)")]
        [TestCase("7505", "ClosureCastsToHandlerWithInternalAppendFormatted", "AppendFormatted")]
        [TestCase("7555", "ClosureIndexesNullableStructFromEndWithInternalLength", "Length")]
        [TestCase("7685", "ClosureSlicesThroughInternalRangeIndexer", "this[]")]
        [TestCase("7705", "ClosureDeconstructsAnArrayThroughGenericExtension", "Deconstruct")]
        [TestCase("7725", "ClosureDeconstructsAnArrayThroughArrayExtension", "Deconstruct")]
        [TestCase("7745", "ClosureDeconstructsATypeParameterWithNew", "Deconstruct")]
        [TestCase("7575", "ClosureDeconstructsThroughClassConstrainedExtension", "Deconstruct")]
        [TestCase("7585", "ClosureDeconstructsThroughStructConstrainedExtension", "Deconstruct")]
        [TestCase("7595", "ClosureAwaitUsesInternalDisposeAsyncBesideAPublicExtension", "DisposeAsync")]
        [TestCase("7615", "ClosureIndexesTwoArgumentsWithInternalIndexer", "this[]")]
        [TestCase("7655", "ClosureIndexesWithInternalIntIndexer", "this[]")]
        [TestCase("7581", "ClosureBuildsHandlerFromInterpolatedAddition", "AppendFormatted")]
        [TestCase("7881", "ClosureIndexesConditionallyFromEndWithInternalLength", "Length")]
        [TestCase("7521", "ClosureBuildsHandlerWithInternalAppendLiteral", "AppendLiteral")]
        [TestCase("7541", "ClosureBuildsHandlerWithInternalAppendFormatted", "AppendFormatted")]
        [TestCase("7561", "ClosureBuildsRefHandlerWithInternalAppendFormatted", "AppendFormatted")]
        [TestCase("7601", "ClosureIndexesFromEndWithInternalLength", "Length")]
        [TestCase("7621", "ClosureIndexesFromEndWithInternalCount", "Count")]
        [TestCase("7641", "ClosureIndexesFromEndWithInternalIndexer", "this[]")]
        [TestCase("7661", "ClosureSlicesRangeWithInternalSlice", "Slice")]
        [TestCase("7681", "ClosureSlicesRangeWithInternalLength", "Length")]
        [TestCase("7701", "ClosureAwaitUsesDisposeAsyncWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7721", "ClosureAwaitUsesDisposeAsyncWithInternalIsCompleted", "IsCompleted")]
        [TestCase("7741", "ClosureAwaitUsesDisposeAsyncWithInternalGetResult", "GetResult")]
        [TestCase("7761", "ClosureAwaitUsingDeclarationWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7801", "ClosureAwaitForEachesMoveNextWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7821", "ClosureAwaitForEachesMoveNextWithInternalIsCompleted", "IsCompleted")]
        [TestCase("7841", "ClosureAwaitForEachesMoveNextWithInternalGetResult", "GetResult")]
        [TestCase("7861", "ClosureAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7781", "ClosureAwaitForEachDeconstructsThroughConstrainedGenericExtension", "Deconstruct")]
        [TestCase("7901", "ClosureDeconstructsThroughConstrainedGenericExtension", "Deconstruct")]
        [TestCase("7921", "ClosureLoopsThroughConstrainedGenericExtension", "GetEnumerator")]
        [TestCase("7941", "ClosureDeconstructsThroughUnconstrainedGenericExtension", "Deconstruct")]
        public async Task ClosurePatternCallToUnpassedInternalMember_IsSkippedWithTheInsideClosureReason(
            string token,
            string methodName,
            string calledMember)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                CollectCodes(skipped.reason),
                Does.Contain(HotReloadWorkerReasonCode.MethodTransformUnpassedInternalMemberInsideClosure));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(reason, Does.Contain("'" + calledMember + "'"));
            Assert.That(reason, Does.Contain(InsideClosureAdvice));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: an async method that reaches such a member is skipped with the out-of-reach reason
        /// that names the member, and produces no entry.
        /// </summary>
        [TestCase("7511", "AsyncBuildsHandlerWithInternalCtor", "new HotReloadPatternHandlerInternalCtor(...)")]
        [TestCase("7515", "AsyncCastsToHandlerWithInternalAppendFormatted", "AppendFormatted")]
        [TestCase("7565", "AsyncSlicesNullableStructWithInternalLength", "Length")]
        [TestCase("7695", "AsyncSlicesThroughInternalRangeIndexer", "this[]")]
        [TestCase("7735", "AsyncDeconstructsAnArrayThroughArrayExtension", "Deconstruct")]
        [TestCase("7755", "AsyncDeconstructsATypeParameterWithNew", "Deconstruct")]
        [TestCase("7605", "AsyncAwaitUsesInternalDisposeAsyncBesideAPublicExtension", "DisposeAsync")]
        [TestCase("7645", "AsyncIndexesTwoArgumentsWithInternalIndexer", "this[]")]
        [TestCase("7665", "AsyncIndexesWithInternalIntIndexer", "this[]")]
        [TestCase("7591", "AsyncBuildsHandlerFromInterpolatedAddition", "AppendFormatted")]
        [TestCase("7891", "AsyncIndexesConditionallyFromEndWithInternalLength", "Length")]
        [TestCase("7531", "AsyncBuildsHandlerWithInternalAppendLiteral", "AppendLiteral")]
        [TestCase("7551", "AsyncBuildsHandlerWithInternalAppendFormatted", "AppendFormatted")]
        [TestCase("7571", "AsyncBuildsRefHandlerWithInternalAppendFormatted", "AppendFormatted")]
        [TestCase("7611", "AsyncIndexesFromEndWithInternalLength", "Length")]
        [TestCase("7631", "AsyncIndexesFromEndWithInternalCount", "Count")]
        [TestCase("7651", "AsyncIndexesFromEndWithInternalIndexer", "this[]")]
        [TestCase("7671", "AsyncSlicesRangeWithInternalSlice", "Slice")]
        [TestCase("7691", "AsyncSlicesRangeWithInternalLength", "Length")]
        [TestCase("7711", "AsyncAwaitUsesDisposeAsyncWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7731", "AsyncAwaitUsesDisposeAsyncWithInternalIsCompleted", "IsCompleted")]
        [TestCase("7751", "AsyncAwaitUsesDisposeAsyncWithInternalGetResult", "GetResult")]
        [TestCase("7771", "AsyncAwaitUsingDeclarationWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7811", "AsyncAwaitForEachesMoveNextWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7831", "AsyncAwaitForEachesMoveNextWithInternalIsCompleted", "IsCompleted")]
        [TestCase("7851", "AsyncAwaitForEachesMoveNextWithInternalGetResult", "GetResult")]
        [TestCase("7871", "AsyncAwaitForEachesEnumeratorDisposeWithInternalGetAwaiter", "GetAwaiter")]
        [TestCase("7791", "AsyncAwaitForEachDeconstructsThroughConstrainedGenericExtension", "Deconstruct")]
        [TestCase("7911", "AsyncDeconstructsThroughConstrainedGenericExtension", "Deconstruct")]
        [TestCase("7931", "AsyncLoopsThroughConstrainedGenericExtension", "GetEnumerator")]
        [TestCase("7951", "AsyncDeconstructsThroughUnconstrainedGenericExtension", "Deconstruct")]
        public async Task AsyncPatternCallToUnpassedInternalMember_IsSkippedWithTheOutOfReachReason(
            string token,
            string methodName,
            string calledMember)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                CollectCodes(skipped.reason),
                Does.Contain(HotReloadWorkerReasonCode.MethodTransformUnpassedInternalMemberOutOfReach));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(reason, Does.Contain("'" + calledMember + "'"));
            Assert.That(reason, Does.EndWith("Run 'uloop compile'."));
            Assert.That(reason, Does.Not.Contain(InsideClosureAdvice));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a range whose given endpoints all count from the start and whose end is given
        /// (`1..2`, `..2`) never reads Length, so an internal Length alone does not refuse the
        /// lambda or async body.
        /// </summary>
        [TestCase("7961", "ClosureSlicesFromStartWithInternalLength")]
        [TestCase("7971", "AsyncSlicesFromStartWithInternalLength")]
        [TestCase("7981", "ClosureSlicesOpenStartWithInternalLength")]
        [TestCase("7991", "AsyncSlicesOpenStartWithInternalLength")]
        public async Task RangeFromStartWithUnpassedInternalLength_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipped(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        /// <summary>
        /// What: a two-part deconstruction or positional pattern whose receiver has internal
        /// extension Deconstruct methods with two and with three out parameters is skipped with a
        /// reason that names the two-out one, the method the compiled body calls.
        /// </summary>
        [TestCase("7525", "ClosureDeconstructsBesideAThreeOutOverload")]
        [TestCase("7535", "AsyncDeconstructsBesideAThreeOutOverload")]
        [TestCase("7545", "ClosureMatchesPositionalPatternBesideAThreeOutOverload")]
        public async Task DeconstructionBesideAThreeOutOverload_NamesTheTwoOutDeconstruct(
            string token,
            string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(reason, Does.Contain("HotReloadUnpassedArityTwoOutExtensions"));
            Assert.That(reason, Does.Not.Contain("HotReloadUnpassedArityThreeOutExtensions"));
            Assert.That(reason, Does.Not.Contain("HotReloadUnpassedUnconstrainedExtensions"));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a two-part deconstruction of a derived type whose own internal Deconstruct takes a
        /// required argument before two out parameters is skipped with a reason that names the base
        /// type's two-out Deconstruct, the method the compiled body calls.
        /// </summary>
        [TestCase("7715", "ClosureDeconstructsBesideAnOverloadWithARequiredArgument")]
        public async Task DeconstructionBesideAnOverloadWithARequiredArgument_NamesTheBaseDeconstruct(
            string token,
            string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(reason, Does.Contain("'HotReloadUnpassedRequiredArgumentBase'"));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a deconstruction whose receiver does not satisfy the new(), unmanaged, or struct
        /// constraint of an internal generic extension Deconstruct is skipped with a reason that
        /// names the extension the compiled body calls, not the constrained one.
        /// </summary>
        [TestCase("7625", "ClosureDeconstructsBesideANewConstrainedExtension", "HotReloadUnpassedConstraintTargetExtensions", "HotReloadUnpassedNewConstrainedExtensions")]
        [TestCase("7635", "ClosureDeconstructsBesideAnUnmanagedConstrainedExtension", "HotReloadUnpassedConstraintTargetExtensions", "HotReloadUnpassedNewConstrainedExtensions")]
        [TestCase("7675", "ClosureDeconstructsANullableStructBesideAStructConstrainedExtension", "HotReloadUnpassedNullableEightOutExtensions", "HotReloadUnpassedStructOnlyEightOutExtensions")]
        public async Task DeconstructionBesideAnUnsatisfiedConstraint_NamesTheCalledExtension(
            string token,
            string methodName,
            string calledType,
            string constrainedType)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            string reason = HotReloadWorkerReasonText.Render(skipped.reason);
            Assert.That(reason, Does.Contain(calledType));
            Assert.That(reason, Does.Not.Contain(constrainedType));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a lambda or async body whose handler is declared in the passed file, with its
        /// InterpolatedStringHandler attribute declared internally in a file the run does not pass,
        /// is skipped with the implicit-call reason naming the internal AppendFormatted.
        /// </summary>
        [TestCase("7765", "ClosureBuildsPassedFileHandlerWithInternalAppendFormatted")]
        [TestCase("7775", "AsyncBuildsPassedFileHandlerWithInternalAppendFormatted")]
        public async Task HandlerWithAttributeFromUnpassedFile_IsSkippedWithTheImplicitCallReason(
            string token,
            string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName + "(");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(CollectCodes(skipped.reason), Has.Member(HotReloadWorkerReasonCode.AccessorImplicitCallNoShape));
            Assert.That(
                HotReloadWorkerReasonText.Render(skipped.reason),
                Does.Contain(
                    "inaccessible method 'AppendFormatted' that the compiler calls for an interpolated string handler"
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
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ImplicitCallPatternLayout" + token + ".cs", edited);
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
