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
    /// Worker coverage for a lambda or expression-bodied local function whose whole body assigns a
    /// private-setter or private static property: it is rewritten to a setter call when nothing
    /// reads the assignment's value, and stays refused when the value is returned.
    /// </summary>
    public class TransformWorkerVoidLambdaAssignmentTests
    {
        private const string FixtureFileName = "HotReloadVoidLambdaAssignmentFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string ValueConsumed = "assignment value is consumed; the setter delegate returns void";
        private const string IncrementNoShape = "inaccessible property increment/decrement has no accessor rewrite shape";

        /// <summary>
        /// What: an Action lambda whose body is only an assignment to a private-setter property is
        /// a patch entry, not refused as a consumed assignment value.
        /// </summary>
        [Test]
        public async Task ActionLambda_AssigningPrivateSetterProperty_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5101", "5102");

            AssertEntryAndNotSkipped(result, "AssignInActionLambda");
        }

        /// <summary>
        /// What: an Action lambda whose body is only a compound assignment to a private-setter
        /// property is a patch entry.
        /// </summary>
        [Test]
        public async Task ActionLambda_CompoundAssigningPrivateSetterProperty_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5201", "5202");

            AssertEntryAndNotSkipped(result, "CompoundAssignInActionLambda");
        }

        /// <summary>
        /// What: an Action lambda whose body is only an assignment to a private static property is
        /// a patch entry.
        /// </summary>
        [Test]
        public async Task ActionLambda_AssigningPrivateStaticProperty_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5701", "5702");

            AssertEntryAndNotSkipped(result, "StaticAssignInActionLambda");
        }

        /// <summary>
        /// What: an Action lambda whose body is only a compound assignment to a private static
        /// property is a patch entry.
        /// </summary>
        [Test]
        public async Task ActionLambda_CompoundAssigningPrivateStaticProperty_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5801", "5802");

            AssertEntryAndNotSkipped(result, "StaticCompoundAssignInActionLambda");
        }

        /// <summary>
        /// What: a void local function whose expression body is only an assignment to a
        /// private-setter property is a patch entry.
        /// </summary>
        [Test]
        public async Task ArrowLocalFunction_AssigningPrivateSetterProperty_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5501", "5502");

            AssertEntryAndNotSkipped(result, "AssignInArrowLocalFunction");
        }

        /// <summary>
        /// What: a statement-bodied Action lambda that assigns a private-setter property stays a
        /// patch entry.
        /// </summary>
        [Test]
        public async Task StatementLambda_AssigningPrivateSetterProperty_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5601", "5602");

            AssertEntryAndNotSkipped(result, "AssignInStatementLambda");
        }

        /// <summary>
        /// What: a Func lambda that returns the value of an assignment to a private-setter property
        /// stays skipped, because the void setter call cannot produce that value.
        /// </summary>
        [Test]
        public async Task FuncLambda_ReturningAssignmentToPrivateSetterProperty_IsSkipped()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5401", "5402");

            AssertSkipped(result, "AssignInFuncLambda", ValueConsumed);
        }

        /// <summary>
        /// What: a local function whose => body returns the value of an assignment to a
        /// private-setter property stays skipped, because the void setter call cannot produce it.
        /// </summary>
        [Test]
        public async Task ValueReturningArrowLocalFunction_AssigningPrivateSetterProperty_IsSkipped()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("6101", "6102");

            AssertSkipped(result, "AssignInValueReturningArrowLocalFunction", ValueConsumed);
        }

        /// <summary>
        /// What: a method added in the reload whose => body returns the value of an assignment to a
        /// private-setter property is skipped, because the void setter call cannot produce it.
        /// </summary>
        [Test]
        public async Task AddedValueReturningArrowMethod_AssigningPrivateSetterProperty_IsSkipped()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(
                "        private static int StaticValue { get; set; }\n",
                "        private static int StaticValue { get; set; }\n\n"
                + "        public int AddedReturnsAssignment() => Value = 6201;\n");

            AssertSkipped(result, "AddedReturnsAssignment", ValueConsumed);
        }

        /// <summary>
        /// What: an Action lambda that increments a private-setter property stays skipped by the
        /// increment rule, which this change does not touch.
        /// </summary>
        [Test]
        public async Task ActionLambda_IncrementingPrivateSetterProperty_StaysSkippedByTheIncrementRule()
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync("5301", "5302");

            AssertSkipped(result, "IncrementInActionLambda", IncrementNoShape);
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(
            string originalToken,
            string editedToken)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(originalToken, editedToken, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("VoidLambdaAssignmentWorker.cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static void AssertEntryAndNotSkipped(TransformWorkerClientResult result, string methodName)
        {
            Assert.That(FindSkipReason(result, methodName), Is.Null, FormatSkipped(result));
            Assert.That(HasEntry(result, methodName), Is.True, methodName + " must be an entry.");
        }

        private static void AssertSkipped(TransformWorkerClientResult result, string methodName, string reasonFragment)
        {
            string reason = FindSkipReason(result, methodName);
            Assert.That(reason, Is.Not.Null, FormatSkipped(result));
            Assert.That(reason, Does.Contain(reasonFragment));
            Assert.That(HasEntry(result, methodName), Is.False, methodName + " must not be an entry.");
        }

        private static bool HasEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FindSkipReason(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains("." + methodName + "("))
                {
                    return HotReloadWorkerReasonText.Render(skipped.reason);
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
