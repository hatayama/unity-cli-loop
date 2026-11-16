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
    /// Transform worker coverage for lambdas and async bodies that call an internal extension
    /// method of a public class. Calls the accessor can carry become entries that call the
    /// accessor with the receiver as the first argument; the shapes it cannot carry are skipped
    /// with the reason of the check that refuses them.
    /// </summary>
    public class TransformWorkerPublicClassExtensionCallTests
    {
        private const string FixtureFileName = "HotReloadPublicClassExtensionCallFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;

        /// <summary>
        /// What: a lambda or async body that calls an internal extension method of a public class
        /// (reduced form, static form, chained, on a call result, on a struct passed by value, on a
        /// null receiver) becomes an entry whose shim calls the accessor with the receiver first.
        /// </summary>
        [TestCase("7101", "ClosureCallsInternalExtension", @"__M_TriplePlus\w*\(__F__value\(__uloopInstance\), 7102\)")]
        [TestCase("7111", "AsyncCallsInternalExtension", @"__M_TriplePlus\w*\(__F__value\(__uloopInstance\), 7112\)")]
        [TestCase("7121", "ClosureCallsInternalExtensionInStaticForm", @"__M_TriplePlus\w*\(__F__value\(__uloopInstance\), 7122\)")]
        [TestCase("7131", "ClosureChainsInternalExtensions", @"__M_PlusOne\w*\(__M_TriplePlus\w*\(__F__value\(__uloopInstance\), 7132\), 1\)")]
        [TestCase("7141", "ClosureCallsInternalExtensionOnCallResult", @"__M_TriplePlus\w*\(__M_NextReceiver\w*\(__uloopInstance\), __M_RecordArgument\w*\(__uloopInstance, 7142\)\)")]
        [TestCase("7191", "ClosureCallsInternalExtensionOnStructByValue", @"__M_PointRead\w*\(__F__point\(__uloopInstance\), 7192\)")]
        [TestCase("7201", "ClosureCallsInternalExtensionOnNullReceiver", @"__M_NullableLengthPlus\w*\(__F__missingLabel\(__uloopInstance\), 7202\)")]
        [TestCase("7241", "ClosureCallsParamsInternalExtensionWithArray", @"__M_SumAll\w*\(__F__value\(__uloopInstance\), new\[\]\s*\{\s*7242\s*\}\)")]
        public async Task InternalExtensionCall_IsAnEntryCallingTheAccessorWithTheReceiverFirst(
            string token,
            string methodName,
            string expectedCallPattern)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipReason(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
            Assert.That(result.Output.shimSource, Does.Match(expectedCallPattern));
        }

        /// <summary>
        /// What: a call to an internal extension method of a public class that the accessor cannot
        /// carry (conditional access, also further down the receiver chain, an expanded params
        /// argument, a [Conditional] method, a ref struct return type, generic, `this ref`,
        /// `this in`, a method group) is skipped
        /// with the reason of the check that refuses that shape, and produces no entry.
        /// </summary>
        [TestCase("7151", "ClosureCallsInternalExtensionConditionally", "inaccessible member access via conditional access has no rewrite shape.")]
        [TestCase("7221", "ClosureCallsInternalExtensionAfterConditionalAccess", "inaccessible member access via conditional access has no rewrite shape.")]
        [TestCase("7231", "ClosureCallsParamsInternalExtensionExpanded", "inaccessible method calls with omitted optional or expanded params arguments")]
        [TestCase("7251", "ClosureCallsInternalExtensionAfterConditionalCall", "inaccessible member access via conditional access has no rewrite shape.")]
        [TestCase("7261", "ClosureCallsInternalExtensionAfterConditionalElement", "inaccessible member access via conditional access has no rewrite shape.")]
        [TestCase("7271", "ClosureCallsInternalMethodAfterConditionalAccess", "inaccessible member access via conditional access has no rewrite shape.")]
        [TestCase("7281", "ClosureCallsParamsInternalExtensionWithNoElements", "inaccessible method calls with omitted optional or expanded params arguments")]
        [TestCase("7321", "ClosureCallsConditionalInternalExtension", "inaccessible [Conditional] method calls are not rewritten, because the compiler omits them when the condition symbol is not defined.")]
        [TestCase("7331", "ClosureCallsConditionalInternalMethod", "inaccessible [Conditional] method calls are not rewritten, because the compiler omits them when the condition symbol is not defined.")]
        [TestCase("7361", "ClosureCallsRefLikeReturningInternalMethod", "inaccessible method with a ref struct parameter or return type has no accessor rewrite shape.")]
        [TestCase("7291", "ClosureCallsParamsInternalMethodExpanded", "inaccessible method calls with omitted optional or expanded params arguments")]
        [TestCase("7161", "ClosureCallsGenericInternalExtension", "inaccessible generic method calls are not rewritten.")]
        [TestCase("7171", "ClosureCallsRefInternalExtension", "inaccessible method calls with ref/out/in parameters are not rewritten")]
        [TestCase("7181", "ClosureCallsInInternalExtension", "inaccessible method calls with ref/out/in parameters are not rewritten")]
        [TestCase("7211", "ClosureHoldsInternalExtensionMethodGroup", "inaccessible extension method used as a method group (not called) has no accessor rewrite shape.")]
        public async Task InternalExtensionCallWithoutAccessorShape_IsSkippedWithTheShapeReason(
            string token,
            string methodName,
            string expectedReason)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain(expectedReason));
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
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("PublicClassExtension" + token + ".cs", edited);
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
