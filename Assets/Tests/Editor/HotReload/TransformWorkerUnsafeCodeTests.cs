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
    /// Worker coverage for edited members that need an unsafe context: each is Skipped with
    /// MethodTransformUnsafeCode while a safe method edited in the same file is still an entry, and
    /// safe shapes that only look unsafe (Span stackalloc, sizeof(int), a safe body in an unsafe type,
    /// bitwise and and multiply on ints) are still entries.
    /// </summary>
    public class TransformWorkerUnsafeCodeTests
    {
        private const string FixtureAssemblyName = "UnityCLILoop.Tests.Editor.HotReloadUnsafeFixture";
        private const string FixtureDirectoryName = "HotReloadUnsafeFixture";
        private const string FixtureFileName = "HotReloadUnsafeFixture.cs";
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/" + FixtureDirectoryName + "/" + FixtureFileName;
        private const string SafeHost = "UnsafeFixtureSafeHost";

        /// <summary>What: an edited method with an unsafe block is Skipped and the safe method is an entry.</summary>
        [Test]
        public async Task Run_UnsafeBlock_IsSkipped()
        {
            await AssertRefusedAsync("*p += 1100;", "*p += 1101;", "UnsafeFixtureBlockHost.Read(");
        }

        /// <summary>What: an edited method declared unsafe that uses pointers is Skipped.</summary>
        [Test]
        public async Task Run_UnsafeModifierWithPointers_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1200", "*p + 1201", "UnsafeFixtureModifierHost.Read(");
        }

        /// <summary>What: an edited method of an unsafe class whose body uses pointers but has no unsafe token is Skipped.</summary>
        [Test]
        public async Task Run_PointerBodyInUnsafeClass_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1300", "*p + 1301", "UnsafeFixtureClassPointerHost.Read(");
        }

        /// <summary>What: an edited method with a pointer parameter is Skipped.</summary>
        [Test]
        public async Task Run_PointerParameter_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1400", "*p + 1401", "UnsafeFixturePointerParameterHost.Read(");
        }

        /// <summary>What: an edited method with a pointer return type is Skipped.</summary>
        [Test]
        public async Task Run_PointerReturn_IsSkipped()
        {
            await AssertRefusedAsync("1450 - 1450", "1451 - 1451", "UnsafeFixturePointerReturnHost.Offset(");
        }

        /// <summary>What: an edited property getter with pointer code is Skipped.</summary>
        [Test]
        public async Task Run_PointerPropertyGetter_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1500", "*p + 1501", "UnsafeFixturePointerGetterHost.get_Value(");
        }

        /// <summary>What: an edited property setter with pointer code is Skipped.</summary>
        [Test]
        public async Task Run_PointerPropertySetter_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1550", "*p + 1551", "UnsafeFixturePointerSetterHost.set_Value(");
        }

        /// <summary>What: an edited indexer getter with pointer code is Skipped.</summary>
        [Test]
        public async Task Run_PointerIndexerGetter_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1600", "*p + 1601", "UnsafeFixturePointerIndexerHost.get_Item(");
        }

        /// <summary>What: an edited getter of a pointer-typed property is Skipped.</summary>
        [Test]
        public async Task Run_PointerTypedPropertyGetter_IsSkipped()
        {
            await AssertRefusedAsync("(IntPtr)1650", "(IntPtr)1651", "UnsafeFixturePointerTypedPropertyHost.get_Pointer(");
        }

        /// <summary>What: an edited method whose lambda holds an unsafe block is Skipped.</summary>
        [Test]
        public async Task Run_UnsafeBlockInLambda_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1700", "*p + 1701", "UnsafeFixtureLambdaHost.Read(");
        }

        /// <summary>What: an edited method with an unsafe local function is Skipped.</summary>
        [Test]
        public async Task Run_UnsafeLocalFunction_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1750", "*p + 1751", "UnsafeFixtureLocalFunctionHost.Read(");
        }

        /// <summary>What: an edited method with a fixed statement is Skipped.</summary>
        [Test]
        public async Task Run_FixedStatement_IsSkipped()
        {
            await AssertRefusedAsync("*p + 1800", "*p + 1801", "UnsafeFixtureFixedHost.Read(");
        }

        /// <summary>What: an edited method with a function pointer is Skipped.</summary>
        [Test]
        public async Task Run_FunctionPointer_IsSkipped()
        {
            await AssertRefusedAsync("twice(1850)", "twice(1851)", "UnsafeFixtureFunctionPointerHost.Read(");
        }

        /// <summary>What: an edited method that holds a pointer in a var local, with no pointer syntax, is Skipped.</summary>
        [Test]
        public async Task Run_ImplicitlyTypedPointerLocal_IsSkipped()
        {
            await AssertRefusedAsync("Read(p) + 1900", "Read(p) + 1901", "UnsafeFixtureImplicitPointerHost.Read(");
        }

        /// <summary>What: an edited method with sizeof of a user struct is Skipped.</summary>
        [Test]
        public async Task Run_SizeOfUserStruct_IsSkipped()
        {
            await AssertRefusedAsync("sizeof(UnsafeFixtureStruct) + 1950", "sizeof(UnsafeFixtureStruct) + 1951", "UnsafeFixtureSizeOfStructHost.Read(");
        }

        /// <summary>What: a call that omits an optional pointer argument, leaving no pointer expression in the body, is Skipped.</summary>
        [Test]
        public async Task Run_CallOmittingOptionalPointerArgument_IsSkipped()
        {
            await AssertRefusedAsync("Count() + 2300", "Count() + 2301", "UnsafeFixtureOptionalPointerCallHost.Read(");
        }

        /// <summary>What: an added method with pointer code is Skipped and the safe method is an entry.</summary>
        [Test]
        public async Task Run_AddedMethodWithPointers_IsSkipped()
        {
            await AssertRefusedAsync(
                "return 1010;\n        }",
                "return 1011;\n        }\n\n        public unsafe int Added()\n        {\n            int x = 1;\n            int* p = &x;\n            return *p;\n        }",
                "UnsafeFixtureAddedMethodHost.Added(");
        }

        /// <summary>What: a stackalloc converted to Span is not refused; both methods are entries.</summary>
        [Test]
        public async Task Run_StackallocToSpan_IsAnEntry()
        {
            await AssertAcceptedAsync("values[0] = 2000", "values[0] = 2001", "UnsafeFixtureStackallocSpanHost");
        }

        /// <summary>What: sizeof(int) is not refused; both methods are entries.</summary>
        [Test]
        public async Task Run_SizeOfInt_IsAnEntry()
        {
            await AssertAcceptedAsync("sizeof(int) + 2050", "sizeof(int) + 2051", "UnsafeFixtureSizeOfIntHost");
        }

        /// <summary>What: a safe method inside an unsafe class is not refused; both methods are entries.</summary>
        [Test]
        public async Task Run_SafeMethodInUnsafeClass_IsAnEntry()
        {
            await AssertAcceptedAsync("return 1350;", "return 1351;", "UnsafeFixtureClassSafeHost");
        }

        /// <summary>What: a method declared unsafe that uses no pointers is not refused; both methods are entries.</summary>
        [Test]
        public async Task Run_UnsafeModifierWithoutPointers_IsAnEntry()
        {
            await AssertAcceptedAsync("return 2100;", "return 2101;", "UnsafeFixtureModifierWithoutPointerHost");
        }

        /// <summary>What: bitwise and and multiply on ints are not refused; both methods are entries.</summary>
        [Test]
        public async Task Run_BitwiseAndAndMultiply_IsAnEntry()
        {
            await AssertAcceptedAsync("(a * b) + 2150", "(a * b) + 2151", "UnsafeFixtureBitwiseHost");
        }

        /// <summary>What: pointer and function pointer types used only as typeof operands are not refused; both methods are entries.</summary>
        [Test]
        public async Task Run_PointerTypeInTypeOf_IsAnEntry()
        {
            await AssertAcceptedAsync(".Name + 2200", ".Name + 2201", "UnsafeFixtureTypeOfPointerHost");
        }

        /// <summary>What: a pointer type used only as a generic type argument is not refused; both methods are entries.</summary>
        [Test]
        public async Task Run_PointerTypeAsTypeArgument_IsAnEntry()
        {
            await AssertAcceptedAsync("values.Count + 2250", "values.Count + 2251", "UnsafeFixturePointerTypeArgumentHost");
        }

        private static async Task AssertRefusedAsync(string anchor, string replacement, string methodFragment)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(anchor, replacement);

            int matches = 0;
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null && skipped.method.Contains(methodFragment))
                {
                    Assert.That(skipped.reason?.code, Is.EqualTo(HotReloadWorkerReasonCode.MethodTransformUnsafeCode), Describe(result));
                    matches++;
                }
            }

            Assert.That(matches, Is.EqualTo(1), "Expected one Skipped row for " + methodFragment + ". " + Describe(result));
            AssertSafeHostIsAnEntry(result);
        }

        private static async Task AssertAcceptedAsync(string anchor, string replacement, string typeName)
        {
            TransformWorkerClientResult result = await RunEditedFixtureAsync(anchor, replacement);

            Assert.That(CountEntries(result, typeName), Is.EqualTo(1), "Expected one entry for " + typeName + ". " + Describe(result));
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                Assert.That(skipped.method ?? string.Empty, Does.Not.Contain(typeName + "."), Describe(result));
            }

            AssertSafeHostIsAnEntry(result);
        }

        private static void AssertSafeHostIsAnEntry(TransformWorkerClientResult result)
        {
            Assert.That(CountEntries(result, SafeHost), Is.EqualTo(1), "The safe method must still be an entry. " + Describe(result));
        }

        private static async Task<TransformWorkerClientResult> RunEditedFixtureAsync(string anchor, string replacement)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", FixtureDirectoryName, FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            // Why match the fixture's line ending: a multi-line anchor is written with \n, and a CRLF
            // checkout on Windows would otherwise fail the precondition before the worker runs.
            string newline = onDisk.Contains("\r\n") ? "\r\n" : "\n";
            string edited = ReplaceUnique(onDisk, anchor.Replace("\n", newline), replacement.Replace("\n", newline));
            edited = ReplaceUnique(edited, "return 1000;", "return 1001;");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("UnsafeCodeWorker.cs", edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk,
                targetAssemblyName: FixtureAssemblyName);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result;
        }

        private static string ReplaceUnique(string source, string anchor, string replacement)
        {
            Assert.That(
                source.IndexOf(anchor, StringComparison.Ordinal),
                Is.EqualTo(source.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                "Precondition: the fixture must hold the anchor exactly once: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static int CountEntries(TransformWorkerClientResult result, string typeName)
        {
            int count = 0;
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.typeMetadataName.EndsWith("." + typeName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string Describe(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                lines.Add("entry " + entry.typeMetadataName + "." + entry.methodName);
            }

            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                lines.Add("skipped " + skipped.method + " => " + HotReloadWorkerReasonText.Render(skipped.reason));
            }

            return string.Join(" | ", lines);
        }
    }
}
