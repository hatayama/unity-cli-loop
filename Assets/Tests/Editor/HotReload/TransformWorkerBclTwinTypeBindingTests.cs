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
    /// Transform worker coverage for lambda and async bodies that pass a BCL type a referenced
    /// assembly also declares internally (Span, ReadOnlySpan, Memory) to a non-public member. Each
    /// shape must get the row its oracle gets: the same shape with a type no referenced assembly
    /// copies (ArraySegment for Memory, a user ref struct for Span).
    /// </summary>
    public class TransformWorkerBclTwinTypeBindingTests
    {
        private const string FixtureFileName = "HotReloadBclTwinTypeFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            HotReloadBclTwinTypePrecondition.AssertReferencedAssemblyCarriesInternalBclTwins();
        }

        /// <summary>
        /// What: a body that passes a BCL twin type to a non-public member whose shape has no accessor
        /// (ref struct parameter, constructor, indexer, initializer indexer) is skipped with the reason
        /// its oracle shape gets, and produces no entry.
        /// </summary>
        [TestCase("6011", "ClosureCallsInternalMethodWithSpan", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6021", "ClosureCallsInternalExtensionOnSpan", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6031", "ClosureCallsInternalExtensionWithSpan", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6041", "ClosureCallsPrivateMethodWithSpan", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6051", "AsyncCallsInternalMethodWithSpan", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6061", "ClosureCallsInternalMethodWithSpanLocal", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6071", "ClosureCallsInternalMethodWithStackallocSpan", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6081", "ClosureCallsInternalMethodWithAsSpan", "6091", "ClosureCallsInternalMethodWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorRefLikeSignatureNoShape))]
        [TestCase("6121", "ClosureCreatesWithInternalConstructorTakingMemory", "6131", "ClosureCreatesWithInternalConstructorTakingArraySegment", nameof(HotReloadWorkerReasonCode.AccessorConstructorCallNoShape))]
        [TestCase("6141", "ClosureCreatesWithInternalConstructorTakingSpan", "6151", "ClosureCreatesWithInternalConstructorTakingUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorConstructorCallNoShape))]
        [TestCase("6161", "ClosureReadsPrivateIndexerWithMemory", "6171", "ClosureReadsPrivateIndexerWithArraySegment", nameof(HotReloadWorkerReasonCode.AccessorIndexerNoShape))]
        [TestCase("6181", "ClosureReadsPrivateIndexerWithSpan", "6191", "ClosureReadsPrivateIndexerWithUserRefStruct", nameof(HotReloadWorkerReasonCode.AccessorIndexerNoShape))]
        [TestCase("6271", "ClosureWritesPrivateIndexerWithMemory", "6281", "ClosureWritesPrivateIndexerWithArraySegment", nameof(HotReloadWorkerReasonCode.AccessorIndexerNoShape))]
        [TestCase("6291", "ClosureCompoundWritesPrivateIndexerWithMemory", "6301", "ClosureCompoundWritesPrivateIndexerWithArraySegment", nameof(HotReloadWorkerReasonCode.AccessorIndexerNoShape))]
        [TestCase("6311", "ClosureIncrementsPrivateIndexerWithMemory", "6321", "ClosureIncrementsPrivateIndexerWithArraySegment", nameof(HotReloadWorkerReasonCode.AccessorIndexerNoShape))]
        [TestCase("6331", "ClosureInitializesInternalIndexerWithMemory", "6341", "ClosureInitializesInternalIndexerWithArraySegment", nameof(HotReloadWorkerReasonCode.AccessorInitializerAssignmentNoShape))]
        public async Task TwinTypedShapeWithoutAccessor_IsSkippedWithTheOracleReason(
            string token,
            string methodName,
            string oracleToken,
            string oracleMethodName,
            string expectedCodeName)
        {
            // Why a name: the reason code enum is internal, and a public test method cannot take it.
            HotReloadWorkerReasonCode expectedCode =
                (HotReloadWorkerReasonCode)Enum.Parse(typeof(HotReloadWorkerReasonCode), expectedCodeName);

            TransformWorkerClientResult oracle = await RunEditedFixtureAsync(oracleToken);
            AssertSkippedWith(oracle, oracleMethodName, expectedCode);

            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);
            AssertSkippedWith(result, methodName, expectedCode);
        }

        /// <summary>
        /// What: a lambda that passes a Memory to an internal method becomes an entry whose shim calls
        /// the method through its accessor, as the same call with an ArraySegment does.
        /// </summary>
        [Test]
        public async Task ClosureCallingInternalMethodWithMemory_CallsTheAccessorAsItsOracleDoes()
        {
            TransformWorkerClientResult oracle = await RunEditedFixtureAsync("6111");
            AssertEntry(oracle, "ClosureCallsInternalMethodWithArraySegment");
            Assert.That(
                oracle.Output.shimSource,
                Does.Match(@"__M_SegmentLength\w*\(new ArraySegment<int>\(__F__ints\(__uloopInstance\)\)\) \+ 6112"));

            TransformWorkerClientResult result = await RunEditedFixtureAsync("6101");
            AssertEntry(result, "ClosureCallsInternalMethodWithMemory");
            Assert.That(
                result.Output.shimSource,
                Does.Match(@"__M_MemoryLength\w*\(new Memory<int>\(__F__ints\(__uloopInstance\)\)\) \+ 6102"));
        }

        /// <summary>
        /// What: an async body that passes a Memory to an internal method becomes an entry whose shim
        /// calls the method through its accessor, as the same call with an ArraySegment does.
        /// </summary>
        [Test]
        public async Task AsyncCallingInternalMethodWithMemory_CallsTheAccessorAsItsOracleDoes()
        {
            TransformWorkerClientResult oracle = await RunEditedFixtureAsync("6361");
            AssertEntry(oracle, "AsyncCallsInternalMethodWithArraySegment");
            Assert.That(
                oracle.Output.shimSource,
                Does.Match(@"__M_SegmentLength\w*\(new ArraySegment<int>\(__F__ints\(__uloopInstance\)\)\) \+ 6362"));

            TransformWorkerClientResult result = await RunEditedFixtureAsync("6351");
            AssertEntry(result, "AsyncCallsInternalMethodWithMemory");
            Assert.That(
                result.Output.shimSource,
                Does.Match(@"__M_MemoryLength\w*\(new Memory<int>\(__F__ints\(__uloopInstance\)\)\) \+ 6352"));
        }

        /// <summary>
        /// What: shapes that need no accessor for the twin type (plain body, public target, string,
        /// index from end, private Memory field and property) stay entries.
        /// </summary>
        [TestCase("6201", "PlainBodyCallsInternalMethodWithSpan")]
        [TestCase("6211", "ClosureCallsPublicMethodWithSpan")]
        [TestCase("6221", "ClosureCallsInternalMethodWithInterpolatedString")]
        [TestCase("6231", "ClosureCallsInternalMethodWithIndexFromEnd")]
        [TestCase("6251", "ClosureReadsAndWritesPrivateMemoryField")]
        [TestCase("6261", "ClosureReadsAndWritesPrivateMemoryProperty")]
        public async Task ShapeNeedingNoTwinTypedAccessor_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            AssertEntry(result, methodName);
        }

        /// <summary>
        /// What: a lambda that passes a Span to an internal method of a file the reload was not given
        /// stays skipped as an unpassed internal member use.
        /// </summary>
        [Test]
        public async Task ClosureCallingUnpassedInternalMethodWithSpan_StaysSkippedAsUnpassed()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("6241");

            TransformWorkerSkippedDto skipped = FindSkipped(result, "ClosureCallsInternalMethodOfAnotherFileWithSpan");
            Assert.That(skipped, Is.Not.Null, FormatSkipped(result));
            Assert.That(
                HotReloadWorkerReasonText.Render(skipped.reason),
                Does.Contain("whose source this reload was not given"));
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string token)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string editedToken = token.Substring(0, 3) + "2";
            string edited = onDisk.Replace(token, editedToken, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("BclTwinType" + token + ".cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static void AssertSkippedWith(
            TransformWorkerClientResult result,
            string methodName,
            HotReloadWorkerReasonCode expectedCode)
        {
            TransformWorkerSkippedDto skipped = FindSkipped(result, methodName);
            Assert.That(skipped, Is.Not.Null, methodName + " must be skipped. " + FormatSkipped(result));
            Assert.That(
                CollectCodes(skipped.reason),
                Does.Contain(expectedCode),
                HotReloadWorkerReasonText.Render(skipped.reason));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        private static void AssertEntry(TransformWorkerClientResult result, string methodName)
        {
            Assert.That(FindSkipped(result, methodName), Is.Null, FormatSkipped(result));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        // The accessor reason sits under the closure or async reason that wraps it.
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

        private static TransformWorkerSkippedDto FindSkipped(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains("." + methodName + "("))
                {
                    return skipped;
                }
            }

            return null;
        }

        private static string FormatSkipped(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerSkippedDto row in result.Output.skipped)
            {
                lines.Add(row.method + " => " + HotReloadWorkerReasonText.Render(row.reason));
            }

            return "Skipped=[" + string.Join(" | ", lines) + "]";
        }
    }
}
