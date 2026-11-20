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
        private const string BareNameAdvice = "Qualify the bare name with 'this.' or the type name, or run 'uloop compile'.";

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
        [TestCase("7351", "NestedInitializerGetterInLambda", "The getter of 'Inner'")]
        [TestCase("7371", "WithInitializerInitInLambda", "The init accessor of 'Length'")]
        [TestCase("7391", "IndexInitializerSetterInLambda", "The setter of 'this[]'")]
        [TestCase("7441", "CompoundAssignmentOfInternalGetterInLambda", "The getter of 'Value'")]
        [TestCase("7451", "IncrementOfInternalGetterInLambda", "The getter of 'Value'")]
        [TestCase("7461", "DeconstructionIntoInternalSetterInLambda", "The setter of 'Length'")]
        [TestCase("7571", "PropertyPatternGetterInLambda", "The getter of 'Value'")]
        [TestCase("7591", "NestedPropertyPatternGetterInLambda", "The getter of 'Inner'")]
        [TestCase("7541", "BareSetterWriteInLambda", "The setter of 'Length'")]
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
        [TestCase("7361", "NestedInitializerGetterInAsync", "The getter of 'Inner'")]
        [TestCase("7381", "WithInitializerInitInAsync", "The init accessor of 'Length'")]
        [TestCase("7431", "IndexInitializerSetterInAsync", "The setter of 'this[]'")]
        [TestCase("7581", "PropertyPatternGetterInAsync", "The getter of 'Value'")]
        [TestCase("7551", "BareSetterWriteInAsync", "The setter of 'Length'")]
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
        /// What: a lambda whose extended property pattern reads, through an internal getter, the
        /// member that the pattern goes through is skipped with the inside-closure reason that names
        /// that getter.
        /// </summary>
        [Test]
        public async Task LambdaWithExtendedPropertyPatternThroughUnpassedInternalGetter_IsSkippedWithTheInsideClosureReason()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "{ Inner: { Count: 7591 } }",
                "{ Inner.Count: 7592 }");

            string reason = FindSkipReason(result, "NestedPropertyPatternGetterInLambda(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain("The getter of 'Inner' is internal to "));
            Assert.That(reason, Does.EndWith(InsideClosureAdvice));
        }

        /// <summary>
        /// What: a method of a type in the edited file that writes, by its bare name, an inherited
        /// property or static property whose setter is internal to a base type in a file the run does
        /// not pass is skipped with the bare-name reason, as a bare-name use of an internal member
        /// is, because the shim cannot qualify a name the worker does not bind as written.
        /// </summary>
        [TestCase("7531", "BareSetterWriteInOwnStatements", "The setter of 'Length'")]
        [TestCase("7561", "BareStaticSetterWriteInOwnStatements", "The setter of 'Shared'")]
        public async Task OwnStatementsWritingUnpassedInternalSetterByBareName_IsSkippedWithTheBareNameReason(
            string token,
            string methodName,
            string accessor)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            string reason = FindSkipReason(result, methodName + "(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.StartWith(accessor + " is internal to 'HotReloadUnpassedAccessorBase'"));
            Assert.That(reason, Does.EndWith(BareNameAdvice));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: a lambda with a genuine error that only looks like a use of an internal accessor (an
        /// index of the wrong type or an undefined name against an indexer whose getter is internal,
        /// an Index argument against a type whose only indexer takes a string and has an internal getter,
        /// a write to a property with no setter, a write to a read-only indexer beside an overload
        /// whose getter is internal, a write to a get-only property of the edited file that hides an
        /// inherited property whose setter is internal) is not skipped by the transform worker, as before this lookup,
        /// so the shim compile reports the error.
        /// </summary>
        [TestCase("7471 - 7471", "\"a\"", "WrongIndexTypeInLambda")]
        [TestCase("7481 - 7481", "undefinedIndex7481", "UndefinedIndexInLambda")]
        [TestCase("int value = 7491;", "holder.Fixed = 7491; int value = 1;", "GetOnlyWriteInLambda")]
        [TestCase("int value = 7521;", "holder[0] = 7521; int value = 1;", "ReadOnlyIndexerWriteBesideInternalGetterOverloadInLambda")]
        [TestCase("[\"7621\"]", "[^1]", "StringIndexerReadInLambda")]
        [TestCase("int value = 7611;", "this.Length = 7611; int value = 1;", "HidingPropertyWriteInLambda")]
        public async Task LambdaWithGenuineErrorNearUnpassedInternalAccessor_IsNotSkippedByTheWorker(
            string fragment,
            string replacement,
            string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(fragment, replacement);

            Assert.That(FindSkipReason(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
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
        [TestCase("7601", "PropertyPatternGetterInOwnStatements")]
        public async Task OwnStatementsCallingUnpassedInternalAccessor_StaysAnEntry(string token, string methodName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipReason(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        private static Task<TransformWorkerClientResult> RunEditedFixtureAsync(string token)
        {
            return RunEditedFixtureAsync(token, token.Substring(0, 3) + "2");
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string fragment, string replacement)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            int first = onDisk.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Precondition: the fragment must be in the fixture.");
            Assert.That(
                onDisk.IndexOf(fragment, first + fragment.Length, StringComparison.Ordinal),
                Is.EqualTo(-1),
                "Precondition: the fragment must occur once in the fixture.");
            string edited = onDisk.Replace(fragment, replacement, StringComparison.Ordinal);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "UnpassedAccessor" + Math.Abs(fragment.GetHashCode()) + ".cs",
                edited);
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
