using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker coverage for bodies that reach private members through accessor shapes: added
    /// members are rewritten to public shim calls, compiled private static properties go through
    /// receiver-less accessor delegates, and a compiled private method call with ref or out
    /// arguments stays refused. Also covers inaccessible extension method calls, which have no
    /// accessor shape in any call form.
    /// </summary>
    public class TransformWorkerAddedMemberAccessTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";

        private const string HostCloseMarker =
            "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n    }";

        // Why the Behaviour's member as the anchor: the host close marker belongs to the plain
        // host class, and members added there would not land on the MonoBehaviour.
        private const string BehaviourExistingTick =
            "        public void ExistingTick()\n        {\n        }";

        private const string RefReturningPropertyNoShape = "inaccessible ref-returning properties have no accessor rewrite shape";
        private const string RefOutInNotRewritten = "inaccessible method calls with ref/out/in parameters are not rewritten";
        private const string EventPassedByRef = "pass a field-like event by ref/out/in";
        private const string PropertyIncrementNoShape = "inaccessible property increment/decrement has no accessor rewrite shape";
        private const string ExtensionMethodGroupNoShape =
            "inaccessible extension method used as a method group (not called) has no accessor rewrite shape.";
        private const string InternalClassWithPublicExtensionsNotVisible =
            "accessor signature type is not visible from an external assembly: "
            + "global::io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadInternalClassExtensionCallMath";
        private const string PrivateMethodGroupNoShape = "inaccessible method group 'AddSeven' (non-invocation) has no accessor rewrite shape";
        private const string MethodGroupWrapperHint = "wrapping the method group";
        private const string TestNamespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string InternalExtensionClassName = "HotReloadInternalExtensionCallMath";
        private const string InternalClassExtensionClassName = "HotReloadInternalClassExtensionCallMath";

        /// <summary>
        /// What: an added method that reads an added private static property is added, not
        /// skipped for having no static-property accessor shape.
        /// </summary>
        [Test]
        public async Task AddedMethod_ReadingAnAddedPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private static int AddedStaticSeed => 4;\n\n"
                + "        public int AddedReadStaticSeed()\n        {\n            return AddedStaticSeed + 1;\n        }");

            AssertAddedAndNotSkipped(result, "AddedReadStaticSeed");
        }

        /// <summary>
        /// What: an added method that writes an added private static property is added, not
        /// skipped for having no static-property accessor shape.
        /// </summary>
        [Test]
        public async Task AddedMethod_WritingAnAddedPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private static int AddedStaticCounter { get; set; }\n\n"
                + "        public int AddedWriteStaticCounter(int value)\n        {\n"
                + "            AddedStaticCounter = value;\n            return value;\n        }");

            AssertAddedAndNotSkipped(result, "AddedWriteStaticCounter");
        }

        /// <summary>
        /// What: calling an added private method with an out argument is added whether the
        /// callee is declared before the caller or after it.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task AddedMethod_CallingAnAddedPrivateOutMethod_IsAddedInEitherDeclarationOrder(bool calleeFirst)
        {
            string callee =
                "private bool AddedTryStep(int input, out int output)\n        {\n"
                + "            output = input + 2;\n            return true;\n        }";
            string caller =
                "public int AddedStepCaller(int input)\n        {\n"
                + "            return AddedTryStep(input, out int output) ? output : -1;\n        }";
            string members = calleeFirst
                ? callee + "\n\n        " + caller
                : caller + "\n\n        " + callee;

            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(members);

            AssertAddedAndNotSkipped(result, "AddedStepCaller");
            AssertAddedAndNotSkipped(result, "AddedTryStep");
        }

        /// <summary>
        /// What: calling an added private method with a ref argument is added.
        /// </summary>
        [Test]
        public async Task AddedMethod_CallingAnAddedPrivateRefMethod_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedBumpCaller(int input)\n        {\n"
                + "            int value = input;\n            AddedBump(ref value);\n            return value;\n        }\n\n"
                + "        private void AddedBump(ref int value)\n        {\n            value += 3;\n        }");

            AssertAddedAndNotSkipped(result, "AddedBumpCaller");
            AssertAddedAndNotSkipped(result, "AddedBump(");
        }

        /// <summary>
        /// What: passing a compiled private field-like event by ref to an added private method is
        /// still skipped for passing the event by ref, because the event read is rewritten to an
        /// accessor call that cannot be passed by ref.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingACompiledPrivateEventByRefToAnAddedMethod_StaysSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedClearsEvent()\n        {\n            AddedClear(ref PrivateChanged);\n        }\n\n"
                + "        private void AddedClear(ref Action value)\n        {\n            value = null;\n        }");

            AssertHasSkip(result, "AddedClearsEvent", EventPassedByRef);
        }

        /// <summary>
        /// What: an added method that passes a compiled private field-like event by ref to an
        /// accessible method is skipped, because the event read is rewritten to an accessor call
        /// that cannot be passed by ref.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingACompiledPrivateEventByRefToAnAccessibleMethod_IsSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedSwapsEvent()\n        {\n"
                + "            System.Threading.Interlocked.Exchange(ref PrivateChanged, null);\n        }");

            AssertHasSkip(result, "AddedSwapsEvent", EventPassedByRef);
        }

        /// <summary>
        /// What: wrapping the event in parentheses before passing it by ref does not get past the
        /// by-ref check, because the rewritten read is still passed by reference.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingAParenthesizedCompiledPrivateEventByRef_IsSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedSwapsWrappedEvent()\n        {\n"
                + "            System.Threading.Interlocked.Exchange(ref ((this.PrivateChanged)), null);\n        }");

            AssertHasSkip(result, "AddedSwapsWrappedEvent", EventPassedByRef);
        }

        /// <summary>
        /// What: an added property whose getter passes a compiled private field-like event by ref
        /// to an accessible method is skipped for the same reason as a method body.
        /// </summary>
        [Test]
        public async Task AddedPropertyGetter_PassingACompiledPrivateEventByRefToAnAccessibleMethod_IsSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public bool AddedSwapsEventOnRead\n        {\n            get\n            {\n"
                + "                return System.Threading.Interlocked.Exchange(ref PrivateChanged, null) != null;\n"
                + "            }\n        }");

            AssertHasSkip(result, "get_AddedSwapsEventOnRead", EventPassedByRef);
        }

        /// <summary>
        /// What: a patched method that passes a compiled private field-like event by ref to an
        /// accessible method is skipped rather than producing a shim that does not compile.
        /// </summary>
        [Test]
        public async Task PatchedMethod_PassingACompiledPrivateEventByRefToAnAccessibleMethod_IsSkipped()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string compiledBody = "            return PrivateChanged != null;\n";
            Assert.That(onDisk, Does.Contain(compiledBody));
            string edited = onDisk.Replace(
                compiledBody,
                "            return System.Threading.Interlocked.Exchange(ref PrivateChanged, null) != null;\n",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("HostPassingEventByRef.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);

            AssertHasSkip(result, "HasPrivateChangedListeners", EventPassedByRef);
        }

        /// <summary>
        /// What: passing a compiled private field by ref to an added private method is added, and
        /// the shim keeps the argument by ref.
        /// </summary>
        [Test]
        public async Task AddedMethod_PassingACompiledPrivateFieldByRefToAnAddedMethod_IsAddedWithTheRefKept()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedBumpsSeed()\n        {\n            AddedBumpSeed(ref _privateSeed);\n            return _privateSeed;\n        }\n\n"
                + "        private void AddedBumpSeed(ref int value)\n        {\n            value += 3;\n        }");

            AssertAddedAndNotSkipped(result, "AddedBumpsSeed");
            Assert.That(result.Output.shimSource, Does.Contain("(__uloopInstance, ref __F__privateSeed(__uloopInstance))"), result.Output.shimSource);
        }

        /// <summary>
        /// What: an added private method that calls itself with an out argument is added.
        /// </summary>
        [Test]
        public async Task AddedMethod_RecursingThroughItsOwnOutParameter_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private int AddedCountDown(int remaining, out int steps)\n        {\n"
                + "            if (remaining <= 0)\n            {\n                steps = 0;\n                return 0;\n            }\n\n"
                + "            int result = AddedCountDown(remaining - 1, out int inner);\n"
                + "            steps = inner + 1;\n            return result;\n        }");

            AssertAddedAndNotSkipped(result, "AddedCountDown");
        }

        /// <summary>
        /// What: a patched method whose lambda reads an added private static property and calls an
        /// added private out method is patched, not skipped for the accessor shapes.
        /// </summary>
        [Test]
        public async Task PatchedClosure_ReachingAddedPrivateStaticPropertyAndOutMethod_IsNotSkipped()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "private static int AddedStaticSeed => 4;\n\n"
                + "        private bool AddedTryStep(int input, out int output)\n        {\n"
                + "            output = input + 2;\n            return true;\n        }");
            string existingCaller =
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }";
            Assert.That(edited, Does.Contain(existingCaller));
            edited = edited.Replace(
                existingCaller,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Func<int> read = () => AddedTryStep(value, out int output) ? output + AddedStaticSeed : -1;\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PatchedClosureReachesAdded.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, "ExistingCaller"), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, "ExistingCaller"), Is.Not.Null);
        }

        /// <summary>
        /// What: an added method that captures a compiled private method as a method group is
        /// skipped with a reason naming the lambda rewrite, and that rewrite - a lambda calling the
        /// same method - is added.
        /// </summary>
        [Test]
        public async Task AddedMethod_CompiledPrivateMethodGroupWrappedInALambda_IsAddedAsTheReasonSays()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedCaptureGroup()\n        {\n"
                + "            Func<int> read = PrivateCall;\n            return read();\n        }\n\n"
                + "        public int AddedCaptureLambda()\n        {\n"
                + "            Func<int> read = () => PrivateCall();\n            return read();\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string groupReason = FindSkipReason(result, "AddedCaptureGroup");
            Assert.That(groupReason, Does.Contain("method group 'PrivateCall' (non-invocation)"));
            Assert.That(
                groupReason,
                Does.Contain("wrapping the method group in a lambda that calls it"));
            AssertAddedAndNotSkipped(result, "AddedCaptureLambda");
        }

        /// <summary>
        /// What: the lambda the method-group reason offers takes as many parameters as the method,
        /// so it compiles as written: none, one without parentheses, and two in parentheses.
        /// </summary>
        [Test]
        public async Task AddedMethod_CompiledPrivateMethodGroup_ReasonLambdaMatchesTheParameterCount()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedCaptureNone()\n        {\n"
                + "            Func<int> read = PrivateCall;\n            return read();\n        }\n\n"
                + "        public int AddedCaptureOne()\n        {\n"
                + "            Func<int, int> add = PrivateAddSeed;\n            return add(1);\n        }\n\n"
                + "        public int AddedCaptureTwo()\n        {\n"
                + "            Func<int, int, int> sum = PrivateSumWithSeed;\n            return sum(1, 2);\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                FindSkipReason(result, "AddedCaptureNone"),
                Does.Contain("(such as '() => PrivateCall()')"));
            string oneReason = FindSkipReason(result, "AddedCaptureOne");
            Assert.That(oneReason, Does.Contain("(such as 'a => PrivateAddSeed(a)')"));
            Assert.That(oneReason, Does.Not.Contain("(a, b)"));
            Assert.That(
                FindSkipReason(result, "AddedCaptureTwo"),
                Does.Contain("(such as '(a, b) => PrivateSumWithSeed(a, b)')"));
        }

        /// <summary>
        /// What: a method group whose method has an out parameter gets the reason without an
        /// example lambda, because a lambda for it needs the modifier and an explicit type.
        /// </summary>
        [Test]
        public async Task AddedMethod_CompiledPrivateMethodGroupWithOutParameter_ReasonOffersNoExample()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public bool AddedCaptureOut()\n        {\n"
                + "            SeedReader read = TryReadPrivateSeed;\n            return read(out int value);\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string reason = FindSkipReason(result, "AddedCaptureOut");
            Assert.That(reason, Does.Contain("method group 'TryReadPrivateSeed' (non-invocation)"));
            Assert.That(reason, Does.Contain("a lambda that calls it keeps hot reloading."));
            Assert.That(reason, Does.Not.Contain("such as"));
        }

        /// <summary>
        /// What: an added method that reads a compiled private static property is added, and the
        /// shim calls the getter delegate with no receiver argument.
        /// </summary>
        [Test]
        public async Task AddedMethod_ReadingACompiledPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedReadCompiledStatic()\n        {\n            return PrivateStaticSeedValue + 1;\n        }");

            AssertAddedAndNotSkipped(result, "AddedReadCompiledStatic");
            Assert.That(result.Output.shimSource, Does.Contain("__P_get_PrivateStaticSeedValue()"), result.Output.shimSource);
        }

        /// <summary>
        /// What: an added method that assigns a compiled private static property is added, and the
        /// shim calls the setter delegate with the value as its only argument.
        /// </summary>
        [Test]
        public async Task AddedMethod_AssigningACompiledPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedWriteCompiledStatic(int value)\n        {\n            PrivateStaticCounter = value;\n        }");

            AssertAddedAndNotSkipped(result, "AddedWriteCompiledStatic");
            Assert.That(result.Output.shimSource, Does.Contain("__P_set_PrivateStaticCounter(value)"), result.Output.shimSource);
        }

        /// <summary>
        /// What: an added method that compound-assigns a compiled private static property is added,
        /// and the shim sets the property to the getter result combined with the right side.
        /// </summary>
        [Test]
        public async Task AddedMethod_CompoundAssigningACompiledPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedBumpCompiledStatic(int value)\n        {\n            HotReloadAddedMemberHost.PrivateStaticCounter += value;\n        }");

            AssertAddedAndNotSkipped(result, "AddedBumpCompiledStatic");
            Assert.That(
                result.Output.shimSource,
                Does.Match(@"__P_set_PrivateStaticCounter\(\(int\)\(__P_get_PrivateStaticCounter\(\)\s*\+\s*\(value\)\)\)"),
                result.Output.shimSource);
        }

        /// <summary>
        /// What: an added method that increments a compiled private static property is skipped, and
        /// its reason names the assignment statements that the accessor rewrite handles.
        /// </summary>
        [Test]
        public async Task AddedMethod_IncrementingACompiledPrivateStaticProperty_IsSkippedNamingTheAssignmentRewrite()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedIncrementCompiledStatic()\n        {\n            PrivateStaticCounter++;\n        }");

            AssertHasSkip(result, "AddedIncrementCompiledStatic", PropertyIncrementNoShape);
            AssertHasSkip(result, "AddedIncrementCompiledStatic", "'X += 1' or 'X = X + 1'");
        }

        /// <summary>
        /// What: the same increment written as the plain assignment statement the increment skip
        /// reason suggests is added, and the shim sets the property from its getter result.
        /// </summary>
        [Test]
        public async Task AddedMethod_IncrementWrittenAsAnAssignmentOfACompiledPrivateStaticProperty_IsAdded()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedAssignIncrementCompiledStatic()\n        {\n"
                + "            PrivateStaticCounter = PrivateStaticCounter + 1;\n        }");

            AssertAddedAndNotSkipped(result, "AddedAssignIncrementCompiledStatic");
            Assert.That(
                result.Output.shimSource,
                Does.Match(@"__P_set_PrivateStaticCounter\(__P_get_PrivateStaticCounter\(\)\s*\+\s*1\)"),
                result.Output.shimSource);
        }

        /// <summary>
        /// What: a compound assignment to a compiled private static byte property is added, and the
        /// value passed to the setter is cast back to byte as C# does for compound assignment.
        /// </summary>
        [Test]
        public async Task AddedMethod_CompoundAssigningACompiledPrivateStaticByteProperty_CastsToPropertyType()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedBumpCompiledStaticByte()\n        {\n            PrivateStaticByteCounter += 1;\n        }");

            AssertAddedAndNotSkipped(result, "AddedBumpCompiledStaticByte");
            Assert.That(
                result.Output.shimSource,
                Does.Match(@"__P_set_PrivateStaticByteCounter\(\(byte\)\(__P_get_PrivateStaticByteCounter\(\)\s*\+\s*\(1\)\)\)"),
                result.Output.shimSource);
        }

        /// <summary>
        /// What: a compound assignment to a compiled private instance byte property is added, and
        /// the value passed to the setter is cast back to byte as C# does for compound assignment.
        /// </summary>
        [Test]
        public async Task AddedMethod_CompoundAssigningACompiledPrivateInstanceByteProperty_CastsToPropertyType()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedBumpCompiledByte()\n        {\n            PrivateByteCounter += 1;\n        }");

            AssertAddedAndNotSkipped(result, "AddedBumpCompiledByte");
            Assert.That(
                result.Output.shimSource,
                Does.Match(@"__P_set_PrivateByteCounter\(__uloopInstance,\s*\(byte\)\(__P_get_PrivateByteCounter\(__uloopInstance\)\s*\+\s*\(1\)\)\)"),
                result.Output.shimSource);
        }

        /// <summary>
        /// What: an added method on a nested type that reads that type's compiled private static
        /// property is added, and the getter binds against the nested type.
        /// </summary>
        [Test]
        public async Task AddedMethod_ReadingANestedTypesCompiledPrivateStaticProperty_IsAdded()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string existingNested =
                "            public int ExistingNested()\n            {\n                return 1;\n            }";
            Assert.That(onDisk, Does.Contain(existingNested));
            string edited = onDisk.Replace(
                existingNested,
                existingNested
                + "\n\n            public int AddedReadNestedStatic()\n            {\n"
                + "                return NestedPrivateStaticSeed + 1;\n            }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("NestedStaticPropertyRead.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);

            AssertAddedAndNotSkipped(result, "AddedReadNestedStatic");
            Assert.That(
                result.Output.shimSource,
                Does.Contain("typeof(global::io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedMemberHost.NestedAddedFieldHost), \"get_NestedPrivateStaticSeed\""),
                result.Output.shimSource);
        }

        /// <summary>
        /// What: an added method that reads a compiled private static ref-returning property is
        /// still skipped, because a getter delegate cannot return the property by reference.
        /// </summary>
        [Test]
        public async Task AddedMethod_ReadingACompiledPrivateStaticRefReturningProperty_StaysSkipped()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public int AddedReadCompiledStaticRef()\n        {\n            return PrivateStaticRefSeed + 1;\n        }");

            AssertHasSkip(result, "AddedReadCompiledStaticRef", RefReturningPropertyNoShape);
        }

        /// <summary>
        /// What: an added method that calls a compiled private out method is still skipped, while
        /// a same-named added overload with a different out type in the same reload is added.
        /// </summary>
        [Test]
        public async Task AddedMethod_CallingACompiledPrivateOutMethod_StaysSkippedBesideAnAddedOverload()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private bool TryReadPrivateSeed(out long value)\n        {\n"
                + "            value = 9L;\n            return true;\n        }\n\n"
                + "        public int AddedCallsCompiledOut()\n        {\n"
                + "            return TryReadPrivateSeed(out int value) ? value : -1;\n        }\n\n"
                + "        public long AddedCallsAddedOut()\n        {\n"
                + "            return TryReadPrivateSeed(out long value) ? value : -1L;\n        }");

            AssertHasSkip(result, "AddedCallsCompiledOut", RefOutInNotRewritten);
            AssertAddedAndNotSkipped(result, "AddedCallsAddedOut");
        }

        /// <summary>
        /// What: when the added out method it calls is skipped for its own body, the caller is
        /// skipped too instead of reaching a shim method that was never emitted.
        /// </summary>
        [Test]
        public async Task AddedMethod_CallingASkippedAddedOutMethod_IsSkippedWithIt()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "private bool AddedTryBase(out string text)\n        {\n"
                + "            text = base.ToString();\n            return true;\n        }\n\n"
                + "        public int AddedUsesTryBase()\n        {\n"
                + "            return AddedTryBase(out string text) ? text.Length : 0;\n        }");

            Assert.That(FindSkipReason(result, "AddedTryBase"), Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindSkipReason(result, "AddedUsesTryBase"), Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, "AddedUsesTryBase"), Is.Null);
        }

        /// <summary>
        /// What: an added OnEnable on a MonoBehaviour whose body has no accessor rewrite names
        /// 'uloop compile' as the only step, because no rewrite of the body would make the
        /// engine call a message hot reload does not forward.
        /// </summary>
        [Test]
        public async Task AddedNotForwardedUnityMessage_WithoutAccessorShape_PointsOnlyToCompile()
        {
            TransformWorkerClientResult result = await RunBehaviourWithAddedMembersAsync(
                "private void OnEnable()\n        {\n"
                + "            System.Action handler = AddedHandler;\n            handler();\n        }\n\n"
                + "        private void AddedHandler()\n        {\n        }");

            AssertHasSkip(result, "OnEnable", "uloop compile");
            Assert.That(FindSkipReason(result, "OnEnable"), Does.Not.Contain("lambda"));
        }

        /// <summary>
        /// What: a method group on the right of '-=' is not offered the lambda rewrite, because a
        /// lambda there is a different delegate and would leave the handler subscribed.
        /// </summary>
        [Test]
        public async Task AddedMethod_UnsubscribingAMethodGroup_DoesNotSuggestALambda()
        {
            TransformWorkerClientResult result = await RunHostWithAddedMembersAsync(
                "public void AddedDetach()\n        {\n            PrivateChanged -= AddedHandler;\n        }\n\n"
                + "        private void AddedHandler()\n        {\n        }");

            AssertHasSkip(result, "AddedDetach", "'-='");
            Assert.That(FindSkipReason(result, "AddedDetach"), Does.Not.Contain("=> AddedHandler"));
            Assert.That(FindSkipReason(result, "AddedDetach"), Does.Not.Contain("keeps hot reloading"));
        }

        /// <summary>
        /// What: a lambda that reads a private field and calls, in reduced form (x.M()), an
        /// internal extension method of an internal class is an entry whose accessor finds the
        /// class by name, because the class is not public.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureReadingPrivateFieldAndCallingInternalExtension_BindsTheClassByName()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4101", "4102");
            AssertEntryAndNotSkipped(result, "ClosureReadsPrivateFieldAndCallsInternalExtension");
            AssertBindsByName(result, InternalExtensionClassName, "Twice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: a lambda whose only inaccessible use is an internal extension method of an
        /// internal class called in reduced form is an entry whose accessor finds the class by name.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingOnlyInternalExtension_BindsTheClassByName()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4201", "4202");
            AssertEntryAndNotSkipped(result, "ClosureCallsOnlyInternalExtension");
            AssertBindsByName(result, InternalExtensionClassName, "Twice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: an async method that calls an internal extension method of an internal class in
        /// reduced form is an entry whose accessor finds the class by name.
        /// </summary>
        [Test]
        public async Task ExtensionCall_AsyncMethodCallingInternalExtension_BindsTheClassByName()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4301", "4302");
            AssertEntryAndNotSkipped(result, "AsyncCallsInternalExtension");
            AssertBindsByName(result, InternalExtensionClassName, "Twice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: a method that calls an internal extension method in reduced form from its own
        /// statements, with no lambda, stays a patch entry.
        /// </summary>
        [Test]
        public async Task ExtensionCall_OwnStatementCallingInternalExtension_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4401", "4402");

            AssertEntryAndNotSkipped(result, "OwnStatementCallsInternalExtension");
        }

        /// <summary>
        /// What: a lambda that calls an internal extension method in static form through its
        /// internal class is an entry: the accessor call replaces the whole call, so the shim never
        /// names the class, and the accessor finds it by name.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureNamingInternalExtensionClass_BindsTheClassByName()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4501", "4502");
            AssertEntryAndNotSkipped(result, "ClosureCallsInternalExtensionInStaticForm");
            AssertBindsByName(result, InternalExtensionClassName, "Twice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: a lambda that calls an internal extension method of a public class in static form
        /// stays a patch entry, calling the method through an accessor.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingInternalExtensionOfPublicClassInStaticForm_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4701", "4702");

            AssertEntryAndNotSkipped(result, "ClosureCallsInternalExtensionOfPublicClassInStaticForm");
        }

        /// <summary>
        /// What: a lambda that converts an internal extension method in reduced form to a delegate
        /// (a method group, no call) is skipped with the extension method-group reason, without the
        /// lambda-wrapper hint, because a wrapping call is skipped as well.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureHoldingInternalExtensionMethodGroup_IsSkippedWithoutWrapperHint()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4901", "4902");

            AssertHasSkip(result, "ClosureHoldsInternalExtensionMethodGroup(", ExtensionMethodGroupNoShape);
            Assert.That(FindSkipReason(result, "ClosureHoldsInternalExtensionMethodGroup("), Does.Not.Contain(MethodGroupWrapperHint));
            Assert.That(FindEntry(result, "ClosureHoldsInternalExtensionMethodGroup"), Is.Null);
        }

        /// <summary>
        /// What: a lambda that converts a private non-extension method to a delegate keeps the
        /// method-group reason with its lambda-wrapper hint.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureHoldingPrivateMethodGroup_KeepsWrapperHint()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5001", "5002");

            AssertHasSkip(result, "ClosureHoldsPrivateMethodGroup(", PrivateMethodGroupNoShape);
            Assert.That(FindSkipReason(result, "ClosureHoldsPrivateMethodGroup("), Does.Contain(MethodGroupWrapperHint));
        }

        /// <summary>
        /// What: a lambda that calls, in reduced form, a public extension method of an internal
        /// class declared in the edited file is an entry whose accessor finds the class by name.
        /// The edited and compiled copies of the class both offer the method, so the call binds
        /// ambiguously; the accessor still binds the method the source calls, also while a public
        /// class in another file declares an extension of the same name.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingPublicExtensionOfInternalClassInEditedFile_BindsTheClassByName()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5101", "5102");
            AssertEntryAndNotSkipped(result, "ClosureCallsPublicExtensionOfInternalClass");
            AssertBindsByName(result, InternalClassExtensionClassName, "Thrice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: a lambda that calls a public extension method of a public class in another file,
        /// whose name it shares with an internal class's twins in the edited file, binds to it
        /// uniquely and stays a patch entry.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingSameNamedExtensionOfSecondClass_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5701", "5702");

            AssertEntryAndNotSkipped(result, "ClosureCallsSameNamedExtensionOfSecondClass");
        }

        /// <summary>
        /// What: an async method that calls, in reduced form, a public extension method of an
        /// internal class declared in the edited file is an entry whose accessor finds the class by
        /// name.
        /// </summary>
        [Test]
        public async Task ExtensionCall_AsyncMethodCallingPublicExtensionOfInternalClassInEditedFile_BindsTheClassByName()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5201", "5202");
            AssertEntryAndNotSkipped(result, "AsyncCallsPublicExtensionOfInternalClass");
            AssertBindsByName(result, InternalClassExtensionClassName, "Thrice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: a lambda that holds, as a method group, a public extension method of an internal
        /// class declared in the edited file is skipped with the extension method-group reason and
        /// produces no entry, although the method group binds ambiguously.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureHoldingPublicExtensionOfInternalClassMethodGroup_IsSkipped()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5301", "5302");

            AssertHasSkip(
                result,
                "ClosureHoldsPublicExtensionOfInternalClassMethodGroup(",
                ExtensionMethodGroupNoShape);
            Assert.That(FindEntry(result, "ClosureHoldsPublicExtensionOfInternalClassMethodGroup"), Is.Null);
        }

        /// <summary>
        /// What: a lambda that calls an extension method of an internal class in the edited file,
        /// whose overloads take different parameter types, is an entry whose accessor binds the
        /// overload the source calls (Step(int, int)), although the ambiguous binding lists every
        /// overload of both copies.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingOverloadedExtensionOfInternalClassInEditedFile_BindsTheCalledOverload()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5401", "5402");
            AssertEntryAndNotSkipped(result, "ClosureCallsOverloadedExtensionOfInternalClass");
            AssertBindsByName(result, InternalClassExtensionClassName, "Step", "typeof(int), typeof(int)");
            Assert.That(result.Output.shimSource, Does.Not.Contain("\"Step\", new global::System.Type[]{typeof(int), typeof(string)}"));
        }

        /// <summary>
        /// What: a lambda that calls an extension method of an internal class in the edited file,
        /// where an int and a long overload both apply, is an entry whose accessor binds the int
        /// overload the source calls, not the long one an implicit conversion would also accept.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingBetterOverloadOfExtensionOfInternalClassInEditedFile_BindsTheCalledOverload()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5501", "5502");
            AssertEntryAndNotSkipped(result, "ClosureCallsBetterOverloadOfExtensionOfInternalClass");
            AssertBindsByName(result, InternalClassExtensionClassName, "Pick", "typeof(int), typeof(int)");
            Assert.That(result.Output.shimSource, Does.Not.Contain("\"Pick\", new global::System.Type[]{typeof(int), typeof(long)}"));
        }

        /// <summary>
        /// What: when the same edit makes the internal extension class public and changes a lambda
        /// that calls it, the lambda is an entry whose accessor still finds the class by name,
        /// because the running code calls the compiled class, which is still internal.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingExtensionOfClassMadePublicInSameEdit_BindsTheClassByName()
        {
            string fixturePath = ResolveTestSourcePath("HotReloadExtensionCallFixtures.cs");
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "    internal static class HotReloadInternalClassExtensionCallMath",
                    "    public static class HotReloadInternalClassExtensionCallMath",
                    StringComparison.Ordinal)
                .Replace("5101", "5102", StringComparison.Ordinal);
            Assert.That(edited, Does.Contain("public static class HotReloadInternalClassExtensionCallMath"));
            Assert.That(edited, Does.Contain("5102"));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("ExtensionCallHostClassMadePublic.cs", edited),
                "Assets/Tests/Editor/HotReload/HotReloadExtensionCallFixtures.cs",
                snapshotSource: onDisk);

            AssertEntryAndNotSkipped(result, "ClosureCallsPublicExtensionOfInternalClass");
            AssertBindsByName(result, InternalClassExtensionClassName, "Thrice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: a lambda that reads a private field and calls an overloaded public extension
        /// method of a public class in the edited file stays a patch entry, although the call
        /// binds ambiguously against both copies of the class.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingOverloadedPublicExtensionOfPublicClass_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("5601", "5602");

            AssertEntryAndNotSkipped(result, "ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension");
        }

        /// <summary>
        /// What: a lambda that reads a private field and calls a public extension method in
        /// reduced form stays a patch entry.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingPublicExtension_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostEditAsync("4601", "4602");

            AssertEntryAndNotSkipped(result, "ClosureReadsPrivateFieldAndCallsPublicExtension");
        }

        /// <summary>
        /// What: a method added in this reload that calls an internal extension method of an internal
        /// class in reduced form is added, and its accessor finds the class by name.
        /// </summary>
        [Test]
        public async Task ExtensionCall_AddedMethodCallingInternalExtension_BindsTheClassByName()
        {
            string fixturePath = ResolveTestSourcePath("HotReloadExtensionCallFixtures.cs");
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "            return _value.Twice(4401);\n        }",
                "            return _value.Twice(4401);\n        }\n\n"
                + "        public int AddedCallsInternalExtension()\n        {\n"
                + "            return _value.Twice(4801);\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the anchor must be in the fixture.");

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("ExtensionCallHostWithAddedMethod.cs", edited),
                "Assets/Tests/Editor/HotReload/HotReloadExtensionCallFixtures.cs",
                snapshotSource: onDisk);

            AssertAddedAndNotSkipped(result, "AddedCallsInternalExtension");
            AssertBindsByName(result, InternalExtensionClassName, "Twice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: when the same edit makes an internal class public and changes a lambda that calls an
        /// internal extension method of it, the accessor still finds the class by name, because the
        /// running code calls the compiled class, which is still internal.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingInternalExtensionOfClassMadePublicInSameEdit_BindsTheClassByName()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostCustomEditAsync(onDisk =>
            {
                string edited = onDisk
                    .Replace(
                        "    internal static class HotReloadInternalExtensionCallMath",
                        "    public static class HotReloadInternalExtensionCallMath",
                        StringComparison.Ordinal)
                    .Replace("4101", "4102", StringComparison.Ordinal);
                Assert.That(edited, Does.Contain("    public static class HotReloadInternalExtensionCallMath"));
                Assert.That(edited, Does.Contain("4102"));
                return edited;
            });

            AssertEntryAndNotSkipped(result, "ClosureReadsPrivateFieldAndCallsInternalExtension");
            AssertBindsByName(result, InternalExtensionClassName, "Twice", "typeof(int), typeof(int)");
        }

        /// <summary>
        /// What: when one edit changes the return type of a public extension method of an internal
        /// class in the edited file and a lambda that calls it, the lambda is skipped for the class,
        /// because the compiled method the accessor would bind returns the old type.
        /// </summary>
        [Test]
        public async Task ExtensionCall_ClosureCallingTwinExtensionWhoseReturnTypeChanged_IsSkipped()
        {
            TransformWorkerClientResult result = await RunExtensionCallHostCustomEditAsync(onDisk => onDisk
                .Replace(
                    "public static int Thrice(this int value, int add)",
                    "public static long Thrice(this int value, int add)",
                    StringComparison.Ordinal)
                .Replace(
                    "Func<int> read = () => _value.Thrice(5101);",
                    "Func<int> read = () => (int)_value.Thrice(5102);",
                    StringComparison.Ordinal));

            AssertHasSkip(result, "ClosureCallsPublicExtensionOfInternalClass(", InternalClassWithPublicExtensionsNotVisible);
            Assert.That(FindEntry(result, "ClosureCallsPublicExtensionOfInternalClass"), Is.Null);
        }

        /// <summary>
        /// What: when one edit changes the return type of an internal extension method of an internal
        /// class and a lambda that calls it, the lambda is skipped with the reason the static-form
        /// call gets whether the call is written on a receiver or in the static form, so no accessor
        /// finds the compiled method of the old return type by name.
        /// </summary>
        [TestCase("(int)_value.Twice(4102)")]
        [TestCase("(int)HotReloadInternalExtensionCallMath.Twice(_value, 4102)")]
        public async Task ExtensionCall_ClosureCallingInternalExtensionWhoseReturnTypeChanged_IsSkipped(string editedCall)
        {
            TransformWorkerClientResult result = await RunExtensionCallHostCustomEditAsync(onDisk => onDisk
                .Replace(
                    "internal static int Twice(this int value, int add)",
                    "internal static long Twice(this int value, int add)",
                    StringComparison.Ordinal)
                .Replace(
                    "Func<int> read = () => _value.Twice(4101);",
                    "Func<int> read = () => " + editedCall + ";",
                    StringComparison.Ordinal));

            AssertHasSkip(
                result,
                "ClosureReadsPrivateFieldAndCallsInternalExtension(",
                "accessor signature type is not visible from an external assembly: "
                + "global::io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadInternalExtensionCallMath");
            Assert.That(FindEntry(result, "ClosureReadsPrivateFieldAndCallsInternalExtension"), Is.Null);
        }

        private static Task<TransformWorkerClientResult> RunExtensionCallHostEditAsync(
            string originalToken,
            string editedToken)
        {
            return RunExtensionCallHostCustomEditAsync(
                onDisk => onDisk.Replace(originalToken, editedToken, StringComparison.Ordinal));
        }

        private static async Task<TransformWorkerClientResult> RunExtensionCallHostCustomEditAsync(
            Func<string, string> edit)
        {
            string fixturePath = ResolveTestSourcePath("HotReloadExtensionCallFixtures.cs");
            string onDisk = File.ReadAllText(fixturePath);
            string edited = edit(onDisk);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the edit must change the fixture.");
            return await RunWorkerOnSourceAsync(
                WriteEdited("ExtensionCallHost.cs", edited),
                "Assets/Tests/Editor/HotReload/HotReloadExtensionCallFixtures.cs",
                snapshotSource: onDisk);
        }

        private static void AssertEntryAndNotSkipped(TransformWorkerClientResult result, string methodName)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
        }

        private static void AssertBindsByName(
            TransformWorkerClientResult result,
            string className,
            string methodName,
            string lookupTypes)
        {
            Assert.That(
                result.Output.shimSource,
                Does.Contain(
                    "global::System.Type.GetType(\"" + TestNamespace + "." + className + ", " + TestAssemblyName
                    + "\", true), \"" + methodName + "\", new global::System.Type[]{" + lookupTypes + "})"));
        }

        private static void AssertAddedAndNotSkipped(TransformWorkerClientResult result, string methodNameFragment)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                FindSkipReason(result, methodNameFragment),
                Is.Null,
                FormatSkipped(result.Output.skipped));
            string methodName = methodNameFragment.TrimEnd('(');
            TransformWorkerEntryDto entry = FindEntry(result, methodName);
            Assert.That(entry, Is.Not.Null, methodName + " must be an entry.");
            Assert.That(entry.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
        }

        private static void AssertHasSkip(
            TransformWorkerClientResult result,
            string methodNameFragment,
            string reasonFragment)
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string reason = FindSkipReason(result, methodNameFragment);
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain(reasonFragment));
        }

        private static async Task<TransformWorkerClientResult> RunHostWithAddedMembersAsync(string extraMembers)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, extraMembers);
            return await RunWorkerOnSourceAsync(
                WriteEdited("HostWithAddedMemberAccess.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
        }

        private static async Task<TransformWorkerClientResult> RunBehaviourWithAddedMembersAsync(string extraMembers)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            Assert.That(onDisk, Does.Contain(BehaviourExistingTick));
            string edited = onDisk.Replace(
                BehaviourExistingTick,
                BehaviourExistingTick + "\n\n        " + extraMembers,
                StringComparison.Ordinal);
            return await RunWorkerOnSourceAsync(
                WriteEdited("BehaviourWithAddedMembers.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
        }

        private static string WithHostMembers(string onDisk, string extraMembers)
        {
            Assert.That(onDisk, Does.Contain(HostCloseMarker));
            return onDisk.Replace(
                HostCloseMarker,
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n        "
                + extraMembers
                + "\n    }",
                StringComparison.Ordinal);
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

        private static string WriteEdited(string fileName, string contents)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private static string ResolveHostPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedMemberHost.cs");
            Assert.That(File.Exists(path), Is.True, "Added-member host source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveTestSourcePath(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName);
            Assert.That(File.Exists(path), Is.True, "Test source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = null;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    compilationAssembly = assembly;
                    break;
                }
            }

            Assert.That(compilationAssembly, Is.Not.Null, "CompilationPipeline assembly not found.");

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = sourcePath,
                        projectRelativePath = projectRelativePath,
                        snapshotSource = snapshotSource
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles),
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = Array.Empty<string>()
            };

            return await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (allReferences != null)
            {
                foreach (string reference in allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        paths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            if (!paths.Exists(path => string.Equals(path, fullTarget, StringComparison.OrdinalIgnoreCase)))
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
        }

        private static string[] BuildAbsoluteAssemblySourcePaths(string[] sourceFiles)
        {
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return Array.Empty<string>();
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] paths = new string[sourceFiles.Length];
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string normalizedRelativePath = sourceFiles[index].Replace('\\', '/');
                paths[index] = Path.GetFullPath(Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            return paths;
        }
    }
}
