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
    /// Transform worker coverage for lambdas and async bodies that call an extension method of a
    /// non-public static class. Calls the accessor can carry become entries whose accessor names
    /// the class through typeof; the shapes it cannot carry are skipped with the reason of the check that
    /// refuses them, as they are for an internal extension method of a public class.
    /// </summary>
    public class TransformWorkerNonPublicClassExtensionCallTests
    {
        private const string FixtureFileName = "HotReloadNonPublicClassExtensionCallFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string AddedExtensionAnchor = "        internal static int NonPublicPlusOne(this int value, int add)";
        private const string StaticFormCallOfTriple = "HotReloadNonPublicClassExtensions.NonPublicTriple(_value, 6121)";
        private const string OwnClassAddedMethodAnchor =
            "        internal static int NonPublicClosureOverOwnClass(this int value, int add)";
        private const string OwnClassAddedMethodSource =
            "        internal static int NonPublicAddedFive(int value)\n"
            + "        {\n"
            + "            return value * 5;\n"
            + "        }\n\n";
        private const string HostClassAnchor = "    /// <summary>\n    /// Compiled host whose lambdas and async methods";

        private const string AddedClassSource =
            "    internal static class HotReloadNonPublicAddedClassExtensions\n"
            + "    {\n"
            + "        internal static int NonPublicNewQuad(this int value, int add)\n"
            + "        {\n"
            + "            return value * 4 + add;\n"
            + "        }\n"
            + "    }\n\n";

        /// <summary>
        /// What: a lambda or async body that calls an extension method of a non-public static class
        /// (reduced form, static form, chained, a public method of the class, or a class in a
        /// namespace named with a keyword) becomes an entry
        /// whose shim calls the accessor with the receiver first, and whose accessor names the class
        /// through typeof.
        /// </summary>
        [TestCase("6101", "ClosureCallsNonPublicClassExtension", @"__M_NonPublicTriple\w*\(__F__value\(__uloopInstance\), 6102\)", "HotReloadNonPublicClassExtensions")]
        [TestCase("6111", "AsyncCallsNonPublicClassExtension", @"__M_NonPublicTriple\w*\(__F__value\(__uloopInstance\), 6112\)", "HotReloadNonPublicClassExtensions")]
        [TestCase("6121", "ClosureCallsNonPublicClassExtensionInStaticForm", @"__M_NonPublicTriple\w*\(__F__value\(__uloopInstance\), 6122\)", "HotReloadNonPublicClassExtensions")]
        [TestCase("6131", "ClosureChainsNonPublicClassExtensions", @"__M_NonPublicPlusOne\w*\(__M_NonPublicTriple\w*\(__F__value\(__uloopInstance\), 6132\), 1\)", "HotReloadNonPublicClassExtensions")]
        [TestCase("6141", "ClosureCallsPublicExtensionOfNonPublicClass", @"__M_NonPublicDouble\w*\(__F__value\(__uloopInstance\), 6142\)", "HotReloadNonPublicClassExtensions")]
        [TestCase("6311", "ClosureCallsKeywordNamespaceExtension", @"__M_KeywordNamespaceTriple\w*\(__F__value\(__uloopInstance\), 6312\)", "@event.HotReloadKeywordNamespaceExtensions")]
        [TestCase("6291", "ClosureRecordsThroughPublicExtensionOfNonPublicClass", @"__M_NonPublicRecord\w*\(__F__value\(__uloopInstance\), ", "HotReloadNonPublicClassExtensions")]
        public async Task NonPublicClassExtensionCall_IsAnEntryBindingTheClassThroughTypeof(
            string token,
            string methodName,
            string expectedCallPattern,
            string className)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(token);

            Assert.That(FindSkipReason(result, methodName + "("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry.");
            Assert.That(result.Output.shimSource, Does.Match(expectedCallPattern));
            Assert.That(
                result.Output.shimSource,
                Does.Contain("typeof(global::" + Namespace + "." + className + ")"));
        }

        /// <summary>
        /// What: a call to an extension method of a non-public static class that the accessor cannot
        /// carry (conditional access, generic in either form, `this ref`, `this in`, a [Conditional]
        /// method, a ref struct return type, a method group, a receiver type that is not visible) is
        /// skipped with the reason of the check that refuses that shape. A body that names the class
        /// outside a call, or in a static-form call outside the closure the accessors rewrite, a call
        /// to an ordinary static method of the class, and a receiver whose type is not visible are
        /// still refused for that type.
        /// </summary>
        [TestCase("6161", "ClosureCallsNonPublicClassExtensionConditionally", "inaccessible member access via conditional access has no rewrite shape.")]
        [TestCase("6171", "ClosureCallsGenericNonPublicClassExtension", "inaccessible generic method calls are not rewritten.")]
        [TestCase("6181", "ClosureCallsRefNonPublicClassExtension", "inaccessible method calls with ref/out/in parameters are not rewritten")]
        [TestCase("6191", "ClosureCallsInNonPublicClassExtension", "inaccessible method calls with ref/out/in parameters are not rewritten")]
        [TestCase("6201", "ClosureCallsConditionalNonPublicClassExtension", "inaccessible [Conditional] method calls are not rewritten, because the compiler omits them when the condition symbol is not defined.")]
        [TestCase("6211", "ClosureCallsRefLikeReturningNonPublicClassExtension", "inaccessible method with a ref struct parameter or return type has no accessor rewrite shape.")]
        [TestCase("6221", "ClosureHoldsNonPublicClassExtensionMethodGroup", "inaccessible extension method used as a method group (not called) has no accessor rewrite shape.")]
        [TestCase("6231", "ClosureNamesTheNonPublicClassOutsideACall", "body uses a type that is not visible from an external assembly: global::" + Namespace + ".HotReloadNonPublicClassExtensions")]
        [TestCase("6241", "ClosureCallsOrdinaryStaticMethodOfNonPublicClass", "accessor signature type is not visible from an external assembly: global::" + Namespace + ".HotReloadNonPublicClassExtensions")]
        [TestCase("6251", "ClosureCallsNonPublicClassExtensionOnNonPublicReceiverType", "accessor signature type is not visible from an external assembly: global::" + Namespace + ".IHotReloadNonPublicMarker")]
        [TestCase("6261", "ClosureCallsGenericNonPublicClassExtensionInStaticForm", "inaccessible generic method calls are not rewritten.")]
        [TestCase("6281", "ClosureCallsNonPublicClassExtensionOnAValueOfANonPublicType", "body uses a type that is not visible from an external assembly: global::" + Namespace + ".IHotReloadNonPublicMarker")]
        [TestCase("6271", "StatementCallsNonPublicClassExtensionInStaticFormBesideAClosure", "body uses a type that is not visible from an external assembly: global::" + Namespace + ".HotReloadNonPublicClassExtensions")]
        public async Task NonPublicClassExtensionCallWithoutAccessorShape_IsSkippedWithTheShapeReason(
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

        /// <summary>
        /// What: a lambda that calls an extension method of a non-public static class this reload
        /// adds is skipped for the class, because the class is not in the compiled assembly the shim
        /// compiles against, so the accessor's typeof of it would fail the shim compile.
        /// </summary>
        [Test]
        public async Task ExtensionCallOfNonPublicClassAddedInThisReload_IsSkippedForTheClass()
        {
            TransformWorkerClientResult result = await RunEditAsync(
                "NonPublicClassAddedClass.cs",
                onDisk => onDisk
                    .Replace(HostClassAnchor, AddedClassSource + HostClassAnchor, StringComparison.Ordinal)
                    .Replace("_value.NonPublicTriple(6101)", "_value.NonPublicNewQuad(6102)", StringComparison.Ordinal));

            string reason = FindSkipReason(result, "ClosureCallsNonPublicClassExtension(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                reason,
                Does.Contain("accessor signature type is not visible from an external assembly: global::" + Namespace + ".HotReloadNonPublicAddedClassExtensions"));
            Assert.That(FindEntry(result, "ClosureCallsNonPublicClassExtension"), Is.Null);
        }

        /// <summary>
        /// What: a lambda that calls a public extension method of an internal class declared in a
        /// file this reload does not pass is skipped for the class: only the compiled copy of the
        /// class is visible, so nothing proves which of its methods the edited source calls.
        /// </summary>
        [Test]
        public async Task ExtensionCallOfNonPublicClassInAnUnpassedFile_IsSkippedForTheClass()
        {
            TransformWorkerClientResult result = await RunEditAsync(
                "NonPublicClassInUnpassedFile.cs",
                onDisk => onDisk.Replace("_value.NonPublicTriple(6101)", "_value.Thrice(6102)", StringComparison.Ordinal));

            string reason = FindSkipReason(result, "ClosureCallsNonPublicClassExtension(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                reason,
                Does.Contain("accessor signature type is not visible from an external assembly: global::" + Namespace + ".HotReloadInternalClassExtensionCallMath"));
            Assert.That(FindEntry(result, "ClosureCallsNonPublicClassExtension"), Is.Null);
        }

        /// <summary>
        /// What: when one edit makes a public extension method of a non-public class [Conditional]
        /// and changes a lambda that calls it, the lambda is skipped with the [Conditional] reason:
        /// the compiler omits the edited call, and the compiled copy the accessor would bind is not
        /// conditional yet.
        /// </summary>
        [Test]
        public async Task ExtensionCallMadeConditionalInTheSameEdit_IsSkippedAsConditional()
        {
            TransformWorkerClientResult result = await RunEditAsync(
                "NonPublicClassMadeConditional.cs",
                onDisk => onDisk
                    .Replace(
                        "        public static void NonPublicRecord(this int value, List<int> sink)",
                        "        [Conditional(\"ULOOP_HOT_RELOAD_TEST_UNDEFINED_SYMBOL\")]\n"
                        + "        public static void NonPublicRecord(this int value, List<int> sink)",
                        StringComparison.Ordinal)
                    .Replace("6291", "6292", StringComparison.Ordinal));

            string reason = FindSkipReason(result, "ClosureRecordsThroughPublicExtensionOfNonPublicClass(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain("inaccessible [Conditional] method calls are not rewritten"));
            Assert.That(FindEntry(result, "ClosureRecordsThroughPublicExtensionOfNonPublicClass"), Is.Null);
        }

        /// <summary>
        /// What: a lambda inside a non-public static class that calls, in the static form, a method
        /// this reload adds to that class is an entry, because the added-method rewrite replaces the
        /// call and the class name with it.
        /// </summary>
        [Test]
        public async Task ClosureInNonPublicClassCallingItsAddedMethodInStaticForm_IsAnEntry()
        {
            TransformWorkerClientResult result = await RunEditAsync(
                "NonPublicClassOwnAddedMethod.cs",
                onDisk => onDisk
                    .Replace(OwnClassAddedMethodAnchor, OwnClassAddedMethodSource + OwnClassAddedMethodAnchor, StringComparison.Ordinal)
                    .Replace(
                        "() => value + add + 6301",
                        "() => HotReloadNonPublicClassExtensions.NonPublicAddedFive(value) + add + 6302",
                        StringComparison.Ordinal));

            Assert.That(FindSkipReason(result, "NonPublicClosureOverOwnClass("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, "NonPublicClosureOverOwnClass"), Is.Not.Null);
            Assert.That(FindEntry(result, "NonPublicAddedFive"), Is.Not.Null);
        }

        /// <summary>
        /// What: a lambda that calls, in the static form, an extension method this reload adds to a
        /// non-public class is an entry whose shim calls the added method and binds no accessor for
        /// it, because the compiled class holds no such method to bind.
        /// </summary>
        [Test]
        public async Task StaticFormCallOfAnAddedExtension_BindsNoAccessorForIt()
        {
            TransformWorkerClientResult result = await RunEditAsync(
                "NonPublicClassAddedExtension.cs",
                onDisk => onDisk
                    .Replace(
                        AddedExtensionAnchor,
                        "        internal static int NonPublicAddedTriple(this int value, int add)\n"
                        + "        {\n            return value * 3 + add + 1000;\n        }\n\n"
                        + AddedExtensionAnchor,
                        StringComparison.Ordinal)
                    .Replace(
                        StaticFormCallOfTriple,
                        "HotReloadNonPublicClassExtensions.NonPublicAddedTriple(_value, 6122)",
                        StringComparison.Ordinal));

            Assert.That(FindSkipReason(result, "ClosureCallsNonPublicClassExtensionInStaticForm("), Is.Null, FormatSkipped(result.Output.skipped));
            Assert.That(FindEntry(result, "ClosureCallsNonPublicClassExtensionInStaticForm"), Is.Not.Null);
            Assert.That(result.Output.shimSource, Does.Contain("NonPublicAddedTriple__shim0(__F__value(__uloopInstance), 6122)"));
            Assert.That(result.Output.shimSource, Does.Not.Contain("\"NonPublicAddedTriple\", new global::System.Type[]"));
        }

        /// <summary>
        /// What: a lambda that calls, in the static form, an extension method this reload adds to a
        /// non-public class but skips is skipped for calling a skipped added method, instead of
        /// binding an accessor to a method the compiled class does not hold.
        /// </summary>
        [Test]
        public async Task StaticFormCallOfASkippedAddedExtension_IsSkippedForTheAddedMethod()
        {
            TransformWorkerClientResult result = await RunEditAsync(
                "NonPublicClassSkippedAddedExtension.cs",
                onDisk => onDisk
                    .Replace(
                        AddedExtensionAnchor,
                        "        internal static int NonPublicAddedSkipped(this int value, int add)\n"
                        + "        {\n            Func<int> f = () => NonPublicGenericEcho<int>(value, add);\n"
                        + "            return f() + 1000;\n        }\n\n"
                        + AddedExtensionAnchor,
                        StringComparison.Ordinal)
                    .Replace(
                        StaticFormCallOfTriple,
                        "HotReloadNonPublicClassExtensions.NonPublicAddedSkipped(_value, 6122)",
                        StringComparison.Ordinal));

            string reason = FindSkipReason(result, "ClosureCallsNonPublicClassExtensionInStaticForm(");
            Assert.That(reason, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(reason, Does.Contain("NonPublicAddedSkipped(int, int)', which this reload skipped"));
            Assert.That(FindEntry(result, "ClosureCallsNonPublicClassExtensionInStaticForm"), Is.Null);
        }

        private static Task<TransformWorkerClientResult> RunEditedFixtureAsync(string token)
        {
            string editedToken = token.Substring(0, 3) + "2";
            return RunEditAsync(
                "NonPublicClassExtension" + token + ".cs",
                onDisk => onDisk.Replace(token, editedToken, StringComparison.Ordinal));
        }

        private static async Task<TransformWorkerClientResult> RunEditAsync(string editedFileName, Func<string, string> edit)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = edit(onDisk);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the edit must change the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(editedFileName, edited);
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
