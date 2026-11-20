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
    /// Transform worker coverage for bodies that call an internal getter, setter or init accessor
    /// of a public property or indexer declared in a file the run does not pass. The worker reads
    /// that file through the public surface only, where the accessor is missing, while the shim
    /// assembly that runs a closure or a state machine cannot call it.
    /// </summary>
    public class TransformWorkerUnpassedAccessorTests
    {
        private const string FixtureFileName = "HotReloadUnpassedAccessorFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string InsideClosureAdvice = "Move the use out of the lambda, local function or query, or run 'uloop compile'.";
        private const string OutOfReachAdvice = "Run 'uloop compile'.";

        /// <summary>
        /// What: a lambda that calls an internal accessor of a public property or indexer of a type
        /// in a file the run does not pass is skipped with the inside-closure reason, which names
        /// that accessor, and produces no entry.
        /// </summary>
        [TestCase("7101", "SetterWriteInLambda", "The setter of 'Length'")]
        [TestCase("7121", "GetterReadInLambda", "The getter of 'Value'")]
        [TestCase("7141", "CompoundAssignmentInLambda", "The setter of 'Length'")]
        [TestCase("7161", "IncrementInLambda", "The setter of 'Length'")]
        [TestCase("7181", "IndexerSetterWriteInLambda", "The setter of 'this[]'")]
        [TestCase("7201", "IndexerGetterReadInLambda", "The getter of 'this[]'")]
        [TestCase("7221", "InitializerSetterInLambda", "The setter of 'Length'")]
        [TestCase("7241", "InitializerInitInLambda", "The init accessor of 'Length'")]
        [TestCase("7261", "IndexFromEndThroughLengthGetterInLambda", "The getter of 'Length'")]
        [TestCase("7281", "IndexFromEndThroughIndexerGetterInLambda", "The getter of 'this[]'")]
        [TestCase("7301", "RangeThroughLengthGetterInLambda", "The getter of 'Length'")]
        [TestCase("7321", "StaticSetterWriteInLambda", "The setter of 'Shared'")]
        [TestCase("7341", "GenericIndexerSetterWriteInLambda", "The setter of 'this[]'")]
        public async Task LambdaCallingUnpassedInternalAccessor_IsSkippedWithTheInsideClosureReason(
            string token,
            string methodName,
            string accessor)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain(accessor + " is internal to "));
            Assert.That(reason, Does.EndWith(InsideClosureAdvice));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: an async method that calls an internal accessor of a public property or indexer of
        /// a type in a file the run does not pass is skipped with the out-of-reach reason, which
        /// names that accessor, and produces no entry.
        /// </summary>
        [TestCase("7111", "SetterWriteInAsync", "The setter of 'Length'")]
        [TestCase("7131", "GetterReadInAsync", "The getter of 'Value'")]
        [TestCase("7151", "CompoundAssignmentInAsync", "The setter of 'Length'")]
        [TestCase("7171", "IncrementInAsync", "The setter of 'Length'")]
        [TestCase("7191", "IndexerSetterWriteInAsync", "The setter of 'this[]'")]
        [TestCase("7211", "IndexerGetterReadInAsync", "The getter of 'this[]'")]
        [TestCase("7231", "InitializerSetterInAsync", "The setter of 'Length'")]
        [TestCase("7251", "InitializerInitInAsync", "The init accessor of 'Length'")]
        [TestCase("7271", "IndexFromEndThroughLengthGetterInAsync", "The getter of 'Length'")]
        [TestCase("7291", "IndexFromEndThroughIndexerGetterInAsync", "The getter of 'this[]'")]
        [TestCase("7311", "RangeThroughLengthGetterInAsync", "The getter of 'Length'")]
        [TestCase("7331", "StaticSetterWriteInAsync", "The setter of 'Shared'")]
        public async Task AsyncCallingUnpassedInternalAccessor_IsSkippedWithTheOutOfReachReason(
            string token,
            string methodName,
            string accessor)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain(accessor + " is internal to "));
            Assert.That(reason, Does.EndWith(OutOfReachAdvice));
            Assert.That(reason, Does.Not.Contain(InsideClosureAdvice));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: the full reason of a lambda that writes through an internal setter, and of an
        /// async method that reads through an internal indexer getter, names the accessor, its
        /// type, the worker's diagnostic and the change that brings the use within reach.
        /// </summary>
        [TestCase(
            "7101",
            "SetterWriteInLambda",
            "The setter of 'Length' is internal to 'HotReloadUnpassedInternalSetterHolder', whose source this reload was not given (CS0200: ",
            InsideClosureAdvice)]
        [TestCase(
            "7211",
            "IndexerGetterReadInAsync",
            "The getter of 'this[]' is internal to 'HotReloadUnpassedIndexerInternalGetterHolder', whose source this reload was not given (CS0154: ",
            OutOfReachAdvice)]
        public async Task UnpassedInternalAccessorReason_NamesTheAccessorTheTypeAndTheAdvice(
            string token,
            string methodName,
            string expectedStart,
            string expectedEnd)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.StartWith(expectedStart));
            Assert.That(
                reason,
                Does.Contain(
                    "). Hot reload patches a use of such a member only where it is a field, a property or a "
                    + "method call written with its receiver"));
            Assert.That(reason, Does.EndWith(expectedEnd));
        }

        /// <summary>
        /// What: a lambda that calls only the public accessor of a property whose other accessor is
        /// internal, and a lambda whose range never reads the Length with the internal getter, stay
        /// patch entries, because the shim assembly can run every call they make.
        /// </summary>
        [TestCase("7401", "GetterReadOfInternalSetterInLambda")]
        [TestCase("7411", "SetterWriteOfInternalGetterInLambda")]
        [TestCase("7421", "RangeWithoutLengthInLambda")]
        public async Task LambdaCallingOnlyPublicAccessors_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipReason(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        /// <summary>
        /// What: a method whose own statements write through an internal setter or read through an
        /// internal getter of a type in a file the run does not pass stays a patch entry, because
        /// the patched method itself makes the call.
        /// </summary>
        [TestCase("7501", "SetterWriteInOwnStatements")]
        [TestCase("7511", "GetterReadInOwnStatements")]
        public async Task OwnStatementsCallingUnpassedInternalAccessor_StaysAnEntry(string token, string methodName)
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
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("UnpassedAccessor" + token + ".cs", edited);
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
